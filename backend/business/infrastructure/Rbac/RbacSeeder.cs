using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Playground.Business.Domain.Rbac;
using Playground.Business.Infrastructure.Persistence;

namespace Playground.Business.Infrastructure.Rbac;

/// <summary>
/// Construye el equivalente RBAC del mismo escenario, para poder compararlos.
/// </summary>
/// <remarks>
/// <para>
/// <b>Este fichero es la demostración.</b> Aquí está, escrito como código, el trabajo que RBAC
/// obliga a hacer y que ReBAC no: <i>enumerar</i>. Fíjate en los bucles anidados de
/// <c>MaterializePermissions</c> — cada iteración es una fila que alguien tendrá que mantener.
/// </para>
/// <para>
/// Y lo importante no es el número de filas, es <b>cuándo hay que volver a ejecutar ese
/// bucle</b>:
/// </para>
/// <list type="bullet">
///   <item>alguien crea un recurso en Alpha → hay que añadir filas a todos los roles que
///   alcanzaban Alpha;</item>
///   <item>alguien mueve una carpeta a otro proyecto → hay que quitar filas de unos roles y
///   ponerlas en otros, incluidas las de todo lo que colgaba de ella;</item>
///   <item>alguien entra en el equipo backend → hay que asignarle el rol correcto;</item>
///   <item>se crea un proyecto nuevo → hay que crear roles nuevos, porque los permisos nombran
///   objetos concretos.</item>
/// </list>
/// <para>
/// En ReBAC ninguna de esas cuatro cosas requiere recalcular nada: son una tupla cada una, o
/// ninguna. Ese es todo el argumento, y por eso este seeder está escrito de forma explícita en
/// lugar de con una lista de filas ya hecha.
/// </para>
/// </remarks>
public sealed class RbacSeeder(BusinessDbContext context, ILogger<RbacSeeder> logger)
{
    private static readonly string[] ViewOnly = ["view"];
    private static readonly string[] ViewEdit = ["view", "edit"];
    private static readonly string[] FullControl = ["view", "edit", "delete"];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await context.RbacRoles.AnyAsync(cancellationToken))
            return;

        // ── Los roles que un equipo diseñaría para este escenario ───────────────
        var roles = new List<RbacRole>
        {
            new()
            {
                Id = "acme-admin",
                Name = "Administrador de Acme",
                Description = "Control total sobre todo lo de Acme. En ReBAC es UNA tupla "
                              + "('organization:acme#admin@user:maria'); aquí hay que enumerar cada objeto.",
            },
            new()
            {
                Id = "alpha-editor",
                Name = "Editor de Project Alpha",
                Description = "Edición sobre Alpha y todo lo que cuelga de él. Fíjate en que el rol "
                              + "nombra el proyecto: con 500 proyectos hacen falta 500 roles como este.",
            },
            new()
            {
                Id = "acme-member",
                Name = "Miembro de Acme",
                Description = "Lectura de lo que Acme comparte con toda la organización.",
            },
            new()
            {
                Id = "gamma-owner",
                Name = "Dueño de Project Gamma",
                Description = "Otro rol atado a un proyecto concreto.",
            },
            new()
            {
                Id = "globex-member",
                Name = "Miembro de Globex",
                Description = "El equivalente de acme-member para la otra organización. Duplicado "
                              + "inevitable: el rol no puede parametrizarse por organización.",
            },
        };

        context.RbacRoles.AddRange(roles);

        // ── Los permisos: aquí es donde RBAC se hace grande ─────────────────────
        var permissions = new List<RbacRolePermission>();

        // Administrador de Acme: todo lo de Acme. Como RBAC no sabe qué cuelga de Acme, hay
        // que listar los cuatro niveles a mano.
        MaterializePermissions(permissions, "acme-admin", FullControl,
        [
            "project:alpha", "project:beta", "project:gamma",
            "folder:docs", "folder:docs-sub", "folder:specs",
            "resource:a", "resource:b", "resource:c", "resource:e",
        ]);

        // Editor de Alpha: el proyecto y TODO su subárbol, enumerado. Esta lista es la que
        // hay que revisar cada vez que alguien crea o mueve algo dentro de Alpha.
        MaterializePermissions(permissions, "alpha-editor", ViewEdit,
        [
            "project:alpha",
            "folder:docs", "folder:docs-sub",
            "resource:a", "resource:b",
        ]);

        // Miembro de Acme: solo lo que Acme comparte. En ReBAC esto era una tupla con un
        // userset ('project:beta#viewer@organization:acme#member').
        MaterializePermissions(permissions, "acme-member", ViewOnly, ["project:beta", "resource:c"]);

        MaterializePermissions(permissions, "gamma-owner", FullControl, ["project:gamma"]);

        MaterializePermissions(permissions, "globex-member", ViewOnly, ["project:delta", "resource:d"]);

        context.RbacRolePermissions.AddRange(permissions);

        // ── Asignaciones ────────────────────────────────────────────────────────
        var assignments = new List<RbacUserRole>
        {
            new() { UserId = "maria", RoleId = "acme-admin" },
            new() { UserId = "juan", RoleId = "alpha-editor" },
            new() { UserId = "juan", RoleId = "acme-member" },
            new() { UserId = "pedro", RoleId = "acme-member" },
            new() { UserId = "pedro", RoleId = "gamma-owner" },
            new() { UserId = "ana", RoleId = "globex-member" },

            // Sofía es el caso incómodo: es editora de Alpha pero NO es de Acme, así que no
            // puede publicar. Con estos roles no hay forma de expresarlo — 'alpha-editor' le
            // daría edición, pero RBAC no tiene manera de decir "editar sí, publicar no"
            // sin inventar un rol más. Se le asigna igualmente para que en la pantalla
            // comparativa se vea que los dos modelos DIFIEREN en su caso.
            new() { UserId = "sofia", RoleId = "alpha-editor" },
        };

        context.RbacUserRoles.AddRange(assignments);

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Modelo RBAC creado: {Roles} roles, {Permissions} permisos, {Assignments} asignaciones = {Total} filas. "
            + "El mismo escenario en ReBAC son 42 tuplas.",
            roles.Count, permissions.Count, assignments.Count,
            roles.Count + permissions.Count + assignments.Count);
    }

    /// <summary>
    /// El bucle que RBAC obliga a escribir: una fila por objeto y acción.
    /// </summary>
    /// <remarks>
    /// Un producto cartesiano. Con 3 acciones y 10 objetos son 30 filas por rol, y crece
    /// multiplicando. En ReBAC el equivalente no existe: no hay ningún sitio donde se
    /// enumeren los objetos, porque la relación con el padre ya lo dice todo.
    /// </remarks>
    private static void MaterializePermissions(
        List<RbacRolePermission> target,
        string roleId,
        IReadOnlyList<string> actions,
        IReadOnlyList<string> objectRefs)
    {
        target.AddRange(objectRefs
            .SelectMany(_ => actions, (objectRef, action) => new RbacRolePermission
            {
                RoleId = roleId,
                Permission = $"{objectRef}:{action}",
            }));
    }
}
