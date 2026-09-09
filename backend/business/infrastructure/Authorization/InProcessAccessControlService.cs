using System.Diagnostics;
using Playground.AccessControl.Domain.Abstractions;
using Playground.AccessControl.Domain.Checking;
using Playground.AccessControl.Domain.Model;
using Playground.Business.Domain.Authorization;

namespace Playground.Business.Infrastructure.Authorization;

/// <summary>
/// Adaptador <b>en proceso</b>: el motor de control de acceso se ejecuta dentro del propio
/// servicio de negocio.
/// </summary>
/// <remarks>
/// <para>
/// Es el modo cómodo: latencia prácticamente cero, se puede depurar de un tirón desde el
/// handler hasta la evaluación de la última tupla, y no hay nada que levantar.
/// </para>
/// <para>
/// Y es el modo que <b>oculta</b> los tres problemas más interesantes del paper de Zanzibar.
/// Con el motor aquí dentro, un <c>ListObjects</c> naive de 200 objetos son 200 llamadas a
/// memoria y no se nota; una caché de decisiones no hace falta; y la pregunta "¿el módulo ya
/// se ha enterado de que borré esa tupla?" ni siquiera tiene sentido, porque es la misma
/// transacción.
/// </para>
/// <para>
/// Por eso existe también <see cref="RemoteAccessControlService"/> y se puede cambiar de uno a
/// otro con una línea de configuración: para poder medir la diferencia en lugar de leerla.
/// </para>
/// </remarks>
public sealed class InProcessAccessControlService(
    IAccessControlEngine engine,
    IRelationshipTupleStore tupleStore) : IAccessControlService
{
    public string Mode => "InProcess";

    public async Task<AccessDecision> CanAsync(
        string subject,
        string relation,
        string @object,
        CancellationToken cancellationToken = default)
    {
        if (!SubjectRef.TryParse(subject, out var parsedSubject) || !ObjectRef.TryParse(@object, out var parsedObject))
            return AccessDecision.Denied($"Referencias no válidas: sujeto '{subject}', objeto '{@object}'.");

        var decision = await engine.CheckAsync(
            parsedSubject, relation, parsedObject, CheckOptions.Default, cancellationToken);

        return new AccessDecision(
            decision.Allowed,
            decision.Reason,
            Mode,
            decision.Metrics.DurationMs,
            decision.Metrics.StoreQueries);
    }

    public async Task<IReadOnlyList<AccessDecision>> CanManyAsync(
        IReadOnlyList<(string Subject, string Relation, string Object)> questions,
        CancellationToken cancellationToken = default)
    {
        var parsed = new List<(SubjectRef, string, ObjectRef)>(questions.Count);
        var invalid = new List<int>();

        foreach (var (question, index) in questions.Select((question, index) => (question, index)))
        {
            if (SubjectRef.TryParse(question.Subject, out var subject)
                && ObjectRef.TryParse(question.Object, out var @object))
            {
                parsed.Add((subject, question.Relation, @object));
            }
            else
            {
                invalid.Add(index);
            }
        }

        var decisions = await engine.BatchCheckAsync(parsed, CheckOptions.Default, cancellationToken);

        var results = decisions
            .Select(decision => new AccessDecision(
                decision.Allowed, decision.Reason, Mode, decision.Metrics.DurationMs, decision.Metrics.StoreQueries))
            .ToList();

        // Se reinsertan las preguntas mal formadas como denegadas para que el índice del
        // resultado siga correspondiendo con el de la pregunta. Devolver una lista más corta
        // desalinearía los flags de permisos de la interfaz sin que nadie se diera cuenta.
        invalid.ForEach(index => results.Insert(index, AccessDecision.Denied("Referencia no válida.")));

        return results;
    }

    public async Task<IReadOnlyList<string>> ListAuthorizedAsync(
        string subject,
        string relation,
        string objectType,
        CancellationToken cancellationToken = default)
    {
        if (!SubjectRef.TryParse(subject, out var parsedSubject))
            return [];

        var result = await engine.ListObjectsAsync(
            parsedSubject,
            relation,
            objectType,
            ListObjectsStrategy.ReverseExpansion,
            CheckOptions.Default,
            cancellationToken);

        return result.Objects.Select(item => item.Object.ToString()).ToList();
    }

    public Task WriteRelationshipAsync(
        string @object,
        string relation,
        string subject,
        CancellationToken cancellationToken = default) =>
        // Se escribe directamente contra el almacén y no a través del motor, porque el motor
        // solo evalúa: ampliarlo con operaciones de escritura mezclaría decidir con administrar.
        tupleStore.WriteAsync(
            new TupleKey(ObjectRef.Parse(@object), relation, SubjectRef.Parse(subject)),
            cancellationToken);
}
