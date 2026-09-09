using Playground.AccessControl.Domain.Checking;
using Playground.AccessControl.Domain.Model;

namespace Playground.AccessControl.Domain.Abstractions;

/// <summary>
/// El motor de control de acceso: las tres operaciones del paper de Zanzibar.
/// </summary>
/// <remarks>
/// <para>
/// Cualquier implementación que cumpla este contrato es intercambiable, y la suite de
/// conformidad está escrita contra esta interfaz precisamente por eso: si algún día añades
/// un adaptador contra OpenFGA o SpiceDB, heredas los tests y sabes si tu motor responde
/// igual.
/// </para>
/// <para>
/// Nótese que esta interfaz es del <b>módulo de control de acceso</b>, no del negocio. El
/// negocio ve un puerto mucho más estrecho (<c>IAccessControlService</c>, en su propio
/// dominio) con un solo método: preguntar si puede. Ni ListObjects con estrategia, ni
/// Expand, ni opciones de traza. Esa asimetría es intencionada: cuanto menos sepa el
/// negocio, más libre eres de cambiar el modelo.
/// </para>
/// </remarks>
public interface IAccessControlEngine
{
    /// <summary>
    /// ¿Tiene <paramref name="subject"/> la relación <paramref name="relation"/> sobre
    /// <paramref name="object"/>?
    /// </summary>
    Task<CheckDecision> CheckAsync(
        SubjectRef subject,
        string relation,
        ObjectRef @object,
        CheckOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Varios checks en una sola llamada.
    /// </summary>
    /// <remarks>
    /// Existe por un motivo puramente práctico que solo se ve cuando el módulo es remoto:
    /// pintar una lista de 50 recursos con sus botones de editar y borrar son 100 checks. En
    /// modo <c>InProcess</c> eso son 100 llamadas a memoria y da igual; en modo <c>Remote</c>
    /// son 100 viajes de red y la pantalla tarda segundos. Toda API de autorización real
    /// acaba teniendo un <c>BatchCheck</c>, y aquí se puede comprobar por qué.
    /// </remarks>
    Task<IReadOnlyList<CheckDecision>> BatchCheckAsync(
        IReadOnlyCollection<(SubjectRef Subject, string Relation, ObjectRef Object)> requests,
        CheckOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>¿Quiénes tienen <paramref name="relation"/> sobre <paramref name="object"/>?</summary>
    Task<ExpandResult> ExpandAsync(
        ObjectRef @object,
        string relation,
        CheckOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ¿Sobre qué objetos de tipo <paramref name="objectType"/> tiene
    /// <paramref name="subject"/> la relación <paramref name="relation"/>?
    /// </summary>
    Task<ListObjectsResult> ListObjectsAsync(
        SubjectRef subject,
        string relation,
        string objectType,
        ListObjectsStrategy strategy = ListObjectsStrategy.ReverseExpansion,
        CheckOptions? options = null,
        CancellationToken cancellationToken = default);
}
