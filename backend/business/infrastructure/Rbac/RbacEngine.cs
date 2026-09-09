using Microsoft.EntityFrameworkCore;
using Playground.Business.Domain.Rbac;
using Playground.Business.Infrastructure.Persistence;

namespace Playground.Business.Infrastructure.Rbac;

/// <summary>
/// Motor RBAC clásico: usuario → roles → permisos.
/// </summary>
/// <remarks>
/// <para>
/// Compáralo con <c>CheckEvaluator</c>, que tiene diez veces más código. Esa diferencia de
/// tamaño <b>es una ventaja real de RBAC</b> y sería deshonesto no reconocerla: son dos
/// consultas y una comparación de cadenas. Se audita fácil, se explica a alguien no técnico en
/// una frase y no tiene ni recursión, ni ciclos, ni profundidad máxima, ni expansión inversa.
/// </para>
/// <para>
/// Toda la complejidad de RBAC está en otro sitio: en <b>mantener las filas</b>. El motor es
/// trivial porque alguien ya hizo el trabajo duro de enumerar cada objeto concreto al que cada
/// rol tiene acceso — y de rehacerlo cada vez que algo cambia de sitio. Ver
/// <see cref="RbacSeeder"/>, donde ese trabajo está escrito como un bucle.
/// </para>
/// </remarks>
public sealed class RbacEngine(BusinessDbContext context) : IRbacEngine
{
    public async Task<RbacDecision> CheckAsync(
        string userId,
        string action,
        string objectRef,
        CancellationToken cancellationToken = default)
    {
        var trace = new List<RbacTraceStep>();

        // Paso 1: qué roles tiene.
        var roleIds = await context.RbacUserRoles
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId)
            .Select(assignment => assignment.RoleId)
            .ToListAsync(cancellationToken);

        if (roleIds.Count == 0)
        {
            trace.Add(new RbacTraceStep($"El usuario '{userId}' no tiene ningún rol asignado.", false));

            return new RbacDecision(
                false,
                $"DENY. En RBAC, sin rol no hay permisos: '{userId}' no aparece en user_roles. "
                + "No existe forma de que herede acceso por pertenecer a un equipo o por estar "
                + "dentro de una carpeta, porque RBAC no sabe qué es eso.",
                [], [], trace, 0);
        }

        trace.Add(new RbacTraceStep(
            $"El usuario '{userId}' tiene {roleIds.Count} rol(es): {string.Join(", ", roleIds)}.", true));

        // Paso 2: qué permisos tienen esos roles.
        var permissions = await context.RbacRolePermissions
            .AsNoTracking()
            .Where(permission => roleIds.Contains(permission.RoleId))
            .ToListAsync(cancellationToken);

        trace.Add(new RbacTraceStep(
            $"Esos roles acumulan {permissions.Count} permisos en role_permissions.", true));

        // Paso 3: ¿está el permiso buscado en la lista?
        var exact = $"{objectRef}:{action}";
        var wildcard = BuildWildcard(objectRef, action);

        var granting = permissions
            .Where(permission => permission.Permission == exact || permission.Permission == wildcard)
            .ToList();

        if (granting.Count == 0)
        {
            trace.Add(new RbacTraceStep(
                $"Ninguno de esos permisos es '{exact}' ni '{wildcard}'.", false));

            return new RbacDecision(
                false,
                $"DENY. Ningún rol de '{userId}' incluye el permiso '{exact}'. En RBAC eso significa "
                + "literalmente que nadie ha insertado esa fila: el permiso o está enumerado o no existe. "
                + "No hay nada que deducir.",
                roleIds, [], trace, permissions.Count);
        }

        trace.Add(new RbacTraceStep(
            $"El permiso '{granting[0].Permission}' está concedido al rol '{granting[0].RoleId}'.", true));

        return new RbacDecision(
            true,
            $"ALLOW. El rol '{granting[0].RoleId}' tiene el permiso '{granting[0].Permission}'. "
            + "El razonamiento de RBAC siempre tiene esta forma y esta longitud, sin importar lo "
            + "complicada que sea la organización.",
            roleIds,
            granting.Select(permission => permission.Permission).ToList(),
            trace,
            permissions.Count);
    }

    public async Task<RbacStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var roles = await context.RbacRoles.CountAsync(cancellationToken);
        var permissions = await context.RbacRolePermissions.CountAsync(cancellationToken);
        var assignments = await context.RbacUserRoles.CountAsync(cancellationToken);

        // Cuántas filas habría que insertar para añadir UN recurso a Project Alpha: una por
        // cada rol que ya tiene algún permiso sobre Alpha, multiplicada por las acciones que
        // ese rol puede hacer. Es el número que hace tangible el coste de materializar.
        var rolesTouchingAlpha = await context.RbacRolePermissions
            .AsNoTracking()
            .Where(permission => permission.Permission.StartsWith("project:alpha:")
                                 || permission.Permission.StartsWith("folder:docs")
                                 || permission.Permission.StartsWith("resource:a:"))
            .CountAsync(cancellationToken);

        return new RbacStats(
            roles,
            permissions,
            assignments,
            roles + permissions + assignments,
            RowsToAddOneResource: Math.Max(1, rolesTouchingAlpha / 3));
    }

    /// <summary>
    /// Construye la versión con comodín del permiso: <c>project:alpha:view</c> →
    /// <c>project:*:view</c>.
    /// </summary>
    /// <remarks>
    /// El comodín es el único mecanismo que tiene RBAC para no enumerar, y por eso se abusa de
    /// él. El problema es que es todo o nada: <c>project:*:edit</c> significa "todos los
    /// proyectos del sistema", incluidos los de otras organizaciones. No hay forma de decir
    /// "todos los proyectos <i>de Acme</i>" sin volver a enumerar, porque eso es una
    /// <b>relación</b> y RBAC no las tiene.
    /// </remarks>
    private static string BuildWildcard(string objectRef, string action)
    {
        var separator = objectRef.IndexOf(':');

        return separator <= 0
            ? $"{objectRef}:{action}"
            : $"{objectRef[..separator]}:*:{action}";
    }
}
