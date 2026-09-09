namespace Playground.Business.Application.Features.Projects.Common;

/// <summary>
/// Un proyecto tal y como lo ve el usuario actual, con lo que puede hacer con él.
/// </summary>
/// <remarks>
/// <para>
/// Los flags <c>Can*</c> no salen de ningún campo de la entidad: los calcula el módulo de
/// control de acceso en el momento de servir la petición. Es lo que permite que la interfaz
/// esconda el botón de borrar sin que el negocio sepa por qué.
/// </para>
/// <para>
/// Y es también donde se paga el precio de tener el control de acceso fuera: pintar una lista
/// de N proyectos con tres flags cada uno son 3N preguntas. Por eso el handler usa
/// <c>CanManyAsync</c> y no un bucle de <c>CanAsync</c>.
/// </para>
/// </remarks>
public sealed record ProjectDto(
    string Id,
    string Name,
    string ObjectRef,
    string? Description,
    string? OrganizationId,
    bool CanView,
    bool CanEdit,
    bool CanDelete,
    bool CanPublish);

/// <param name="AuthorizationMode"><c>InProcess</c> o <c>Remote</c>.</param>
/// <param name="ChecksPerformed">
/// Cuántas preguntas de autorización ha costado pintar esta lista. Es el número que hace
/// evidente por qué existe <c>BatchCheck</c>.
/// </param>
public sealed record ProjectListDto(
    IReadOnlyList<ProjectDto> Projects,
    int TotalInCatalog,
    int Visible,
    string AuthorizationMode,
    int ChecksPerformed,
    double AuthorizationMs);
