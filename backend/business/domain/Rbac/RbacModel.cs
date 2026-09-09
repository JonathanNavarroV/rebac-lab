namespace Playground.Business.Domain.Rbac;

/// <summary>
/// Un rol clásico. Una fila en <c>roles</c>.
/// </summary>
public sealed class RbacRole
{
    public string Id { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }
}

/// <summary>
/// Un permiso concedido a un rol. Una fila en <c>role_permissions</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Aquí está la diferencia esencial con una tupla.</b> Un permiso RBAC es una cadena
/// (<c>project:alpha:edit</c>) que nombra un objeto <i>concreto</i> y una acción. No hay
/// sujeto — el sujeto llega por el rol — y sobre todo no hay <b>relación entre objetos</b>.
/// </para>
/// <para>
/// Esa ausencia es la que obliga a materializar: como no se puede decir "los recursos que
/// cuelgan de Alpha", hay que enumerarlos. Y cuando alguien mueve un recurso de carpeta, hay
/// que reenumerarlos otra vez.
/// </para>
/// </remarks>
public sealed class RbacRolePermission
{
    public long Id { get; set; }

    public string RoleId { get; set; } = null!;

    /// <summary>
    /// Permiso en formato <c>tipo:id:acción</c>, con comodín admitido en el id
    /// (<c>project:*:view</c>).
    /// </summary>
    public string Permission { get; set; } = null!;
}

/// <summary>Asignación de un rol a una persona. Una fila en <c>user_roles</c>.</summary>
public sealed class RbacUserRole
{
    public long Id { get; set; }

    public string UserId { get; set; } = null!;

    public string RoleId { get; set; } = null!;
}

/// <summary>Un paso del razonamiento de RBAC, para poder mostrarlo al lado del de ReBAC.</summary>
public sealed record RbacTraceStep(string Description, bool Matched);

/// <summary>
/// Decisión del motor RBAC, con su razonamiento.
/// </summary>
/// <remarks>
/// El razonamiento de RBAC es notablemente más corto que el de ReBAC, y eso es una <b>ventaja
/// real</b> de RBAC que conviene no ocultar: son tres pasos siempre (qué roles tienes, qué
/// permisos tienen esos roles, ¿está el permiso en la lista?), es fácil de auditar y es fácil
/// de explicar a alguien no técnico. Lo que RBAC no puede hacer es responder por relaciones
/// que no se hayan enumerado antes.
/// </remarks>
public sealed record RbacDecision(
    bool Allowed,
    string Reason,
    IReadOnlyList<string> UserRoles,
    IReadOnlyList<string> GrantingPermissions,
    IReadOnlyList<RbacTraceStep> Trace,
    int PermissionRowsScanned);

/// <summary>Motor de autorización RBAC clásico.</summary>
public interface IRbacEngine
{
    /// <param name="action">Acción en el vocabulario de RBAC: <c>view</c>, <c>edit</c>, <c>delete</c>.</param>
    /// <param name="objectRef">Objeto en notación <c>project:alpha</c>.</param>
    Task<RbacDecision> CheckAsync(
        string userId,
        string action,
        string objectRef,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Estadísticas del modelo RBAC: número de roles, permisos y asignaciones.
    /// </summary>
    /// <remarks>
    /// Se exponen para poder ponerlas al lado del número de tuplas de ReBAC. Comparar esos dos
    /// números sobre el <i>mismo</i> escenario es el argumento más contundente del laboratorio,
    /// mucho más que cualquier explicación.
    /// </remarks>
    Task<RbacStats> GetStatsAsync(CancellationToken cancellationToken = default);
}

/// <param name="RowsToAddOneResource">
/// Cuántas filas habría que insertar en RBAC para añadir UN recurso nuevo a Project Alpha,
/// frente a la única tupla que hace falta en ReBAC.
/// </param>
public sealed record RbacStats(
    int Roles,
    int Permissions,
    int UserRoleAssignments,
    int TotalRows,
    int RowsToAddOneResource);
