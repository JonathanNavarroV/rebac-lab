using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Playground.Business.Domain.Authorization;
using Playground.Contracts.AccessControl;

namespace Playground.Business.Infrastructure.Authorization;

/// <summary>
/// Adaptador <b>remoto</b>: el control de acceso es un servicio aparte y se le pregunta por
/// HTTP.
/// </summary>
/// <remarks>
/// <para>
/// Esta es la situación real de cualquier equipo que use Zanzibar, OpenFGA o SpiceDB. Y cambia
/// tres cosas respecto al modo en proceso, todas importantes:
/// </para>
///
/// <para><b>1. La latencia deja de ser cero.</b></para>
/// <para>
/// Cada <c>CanAsync</c> es un salto de red. En local son unos pocos milisegundos; entre zonas
/// de disponibilidad, decenas. Multiplicado por los checks que hace una pantalla, es la
/// diferencia entre una interfaz fluida y una que se arrastra. De ahí que
/// <see cref="CanManyAsync"/> use el endpoint de lote: no ahorra CPU, ahorra <i>viajes</i>.
/// </para>
///
/// <para><b>2. Aparece el modo degradado.</b></para>
/// <para>
/// El servicio de autorización puede estar caído. Y entonces hay que decidir algo que en
/// proceso no se plantea: ¿denegar todo (el sistema se para) o permitir todo (agujero de
/// seguridad)? La respuesta correcta es <b>denegar</b>, y está implementada así abajo. Es
/// también la razón por la que Zanzibar está diseñado para una disponibilidad altísima: si el
/// sistema de autorización cae, cae todo lo demás.
/// </para>
///
/// <para><b>3. Aparece la consistencia.</b></para>
/// <para>
/// Si el negocio escribe una tupla y acto seguido pregunta, ¿el módulo ya lo sabe? Aquí sí,
/// porque hay una sola instancia y una sola base. En un despliegue real con réplicas, no
/// necesariamente — y ese es exactamente el problema que Zanzibar resuelve con los
/// <i>zookies</i>. Ver <c>docs/06</c>.
/// </para>
/// </remarks>
public sealed class RemoteAccessControlService(
    HttpClient httpClient,
    ILogger<RemoteAccessControlService> logger) : IAccessControlService
{
    public string Mode => "Remote";

    public async Task<AccessDecision> CanAsync(
        string subject,
        string relation,
        string @object,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await httpClient.PostAsJsonAsync(
                "/access-control/check",
                new CheckRequest(subject, relation, @object),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return Unavailable(subject, relation, @object, $"HTTP {(int)response.StatusCode}", stopwatch);

            var payload = await response.Content.ReadFromJsonAsync<CheckResponse>(cancellationToken);

            if (payload is null)
                return Unavailable(subject, relation, @object, "respuesta vacía", stopwatch);

            return new AccessDecision(
                payload.Allowed,
                payload.Reason,
                Mode,
                // Se mide el tiempo TOTAL desde el negocio, no el que reporta el módulo. La
                // diferencia entre ambos es justo el coste de la red, que es lo que se quiere
                // ver en la interfaz.
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
                payload.Metrics.StoreQueries);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(exception, "El módulo de control de acceso no responde.");
            return Unavailable(subject, relation, @object, exception.Message, stopwatch);
        }
    }

    public async Task<IReadOnlyList<AccessDecision>> CanManyAsync(
        IReadOnlyList<(string Subject, string Relation, string Object)> questions,
        CancellationToken cancellationToken = default)
    {
        if (questions.Count == 0)
            return [];

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var request = new BatchCheckRequest(questions
                .Select(question => new CheckRequest(question.Subject, question.Relation, question.Object))
                .ToList());

            var response = await httpClient.PostAsJsonAsync(
                "/access-control/batch-check", request, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return DenyAll(questions, $"HTTP {(int)response.StatusCode}", stopwatch);

            var payload = await response.Content.ReadFromJsonAsync<BatchCheckResponse>(cancellationToken);

            if (payload is null || payload.Results.Count != questions.Count)
                return DenyAll(questions, "respuesta incompleta", stopwatch);

            // El tiempo se reparte entre las preguntas: la gracia del lote es que UN viaje
            // resuelve N, así que atribuir el total a cada una daría una idea equivocada.
            var perQuestion = Math.Round(stopwatch.Elapsed.TotalMilliseconds / questions.Count, 3);

            return payload.Results
                .Select(result => new AccessDecision(
                    result.Allowed, result.Reason, Mode, perQuestion, result.Metrics.StoreQueries))
                .ToList();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(exception, "El módulo de control de acceso no responde.");
            return DenyAll(questions, exception.Message, stopwatch);
        }
    }

    public async Task<IReadOnlyList<string>> ListAuthorizedAsync(
        string subject,
        string relation,
        string objectType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync(
                "/access-control/list-objects",
                new ListObjectsRequest(subject, relation, objectType),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return [];

            var payload = await response.Content.ReadFromJsonAsync<ListObjectsResponse>(cancellationToken);

            return payload?.Objects.Select(item => item.Object).ToList() ?? [];
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(exception, "El módulo de control de acceso no responde.");

            // Lista vacía = no ve nada. Es la opción segura: ante la duda, no mostrar.
            return [];
        }
    }

    public async Task WriteRelationshipAsync(
        string @object,
        string relation,
        string subject,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/access-control/relationships",
            new CreateRelationshipRequest(Object: @object, Relation: relation, Subject: subject),
            cancellationToken);

        // Aquí sí se propaga el error. Una escritura fallida es distinta de una lectura
        // fallida: si al crear un proyecto no se logra declarar quién es su dueño, el proyecto
        // queda huérfano y nadie podrá tocarlo nunca. Mejor que la operación entera falle.
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Denegar cuando el módulo no responde.
    /// </summary>
    /// <remarks>
    /// <b>Fail closed.</b> Es la decisión correcta y conviene que sea explícita y visible, no
    /// un efecto secundario de un <c>catch</c> vacío. La alternativa —permitir cuando no se
    /// puede comprobar— convierte una caída del servicio de autorización en un acceso libre a
    /// todo el sistema.
    /// </remarks>
    private AccessDecision Unavailable(
        string subject,
        string relation,
        string @object,
        string detail,
        Stopwatch stopwatch) =>
        new(false,
            $"DENY por indisponibilidad: el módulo de control de acceso no ha podido responder ({detail}). "
            + $"No se ha evaluado si «{subject}» puede «{relation}» sobre «{@object}»; se deniega por "
            + "principio de fallo seguro. Este escenario simplemente no existe cuando el motor corre "
            + "en proceso.",
            Mode,
            Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2));

    private IReadOnlyList<AccessDecision> DenyAll(
        IReadOnlyList<(string Subject, string Relation, string Object)> questions,
        string detail,
        Stopwatch stopwatch) =>
        questions
            .Select(question => Unavailable(question.Subject, question.Relation, question.Object, detail, stopwatch))
            .ToList();
}
