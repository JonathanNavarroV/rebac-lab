using Playground.AccessControl.Domain.Model;

namespace Playground.AccessControl.Application.Authorization.Seed;

/// <summary>Una persona del laboratorio, con la frase que resume su situación.</summary>
public sealed record ScenarioUser(string Id, string DisplayName, string Story);

/// <summary>Una entidad de negocio del laboratorio.</summary>
public sealed record ScenarioEntity(string Type, string Id, string DisplayName, string? ParentRef = null);

/// <summary>
/// Un caso guiado: la pregunta, el resultado esperado y qué enseña.
/// </summary>
/// <remarks>
/// Estos casos se usan en tres sitios a la vez, y eso es a propósito: son los tests de
/// conformidad, son los botones de "casos guiados" del frontend y son los ejemplos de la
/// documentación. Al estar declarados una sola vez, es imposible que la documentación diga
/// una cosa y el sistema haga otra.
/// </remarks>
public sealed record ScenarioCase(
    string Code,
    string Title,
    string Subject,
    string Relation,
    string Object,
    bool ExpectedAllowed,
    string WhatItTeaches,
    string? WhyRbacStruggles = null,
    int MinimumPaths = 1);

/// <summary>
/// El dataset inicial del laboratorio: entidades, tuplas y casos guiados.
/// </summary>
/// <remarks>
/// <para>
/// Está construido para que <b>desde el primer arranque</b> existan ejemplos de acceso
/// directo, heredado, por equipo, por grupo, por organización, por comodín, por compartición,
/// con múltiples caminos, con exclusión, con intersección y denegado. No hay ninguna tupla de
/// relleno: cada una está para demostrar algo concreto, y los comentarios dicen qué.
/// </para>
/// <para><b>El mapa mental:</b></para>
/// <code>
/// organization:acme                          organization:globex
/// ├── admin: maria                           └── member ← team:sales#member
/// ├── member ← team:backend#member
/// ├── member ← team:frontend#member          team:sales
/// ├── member ← team:management#member        └── member: ana
/// │
/// ├── team:backend      → member: juan
/// ├── team:frontend     → member: pedro
/// ├── team:management   → member: maria
/// │
/// ├── project:alpha     editor ← team:backend#member   ← Caso B
/// │   │                 viewer ← user:juan             ← Caso H (2.º camino)
/// │   │                 editor ← user:sofia            ← colaboradora externa
/// │   ├── resource:a    viewer ← group:seguridad#member ← Caso G
/// │   │                 shared_with ← user:ana          ← Caso F
/// │   └── folder:docs   viewer ← user:pedro             ← Caso C
/// │       └── folder:docs-sub
/// │           └── resource:b                            ← heredado 3 niveles
/// │
/// ├── project:beta      viewer ← organization:acme#member ← Caso A
/// │   ├── resource:c    owner ← user:maria                ← Caso D
/// │   └── folder:specs
/// │       └── resource:e  editor ← group:auditoria#member ← grupos anidados
/// │
/// └── project:gamma     owner ← user:pedro
///                       viewer ← user:*                   ← público
///
/// organization:globex
/// └── project:delta     viewer ← organization:globex#member
///     └── resource:d    blocked ← user:ana                ← exclusión
///
/// group:seguridad  → member: pedro
/// group:auditoria  → member ← group:seguridad#member, member: maria
/// </code>
/// </remarks>
public static class PlaygroundScenario
{
    public static IReadOnlyList<ScenarioUser> Users =>
    [
        new("juan", "Juan",
            "Miembro de Team Backend. No tiene NINGUNA relación directa con la mayoría de "
            + "recursos a los que accede: todo le llega por pertenecer al equipo."),

        new("pedro", "Pedro",
            "Miembro de Team Frontend y del grupo Seguridad. Es el caso de quien acumula "
            + "accesos por vías muy distintas: equipo, grupo, propiedad y una carpeta."),

        new("maria", "María",
            "Administradora de Acme. No aparece nombrada en casi ningún proyecto y sin "
            + "embargo puede editarlos todos, porque el permiso se hereda de la organización."),

        new("ana", "Ana",
            "Miembro de Globex. Fuera de Acme no tiene nada, salvo un recurso que Juan le "
            + "compartió. Sirve para ver DENY explicados y el efecto de una exclusión."),

        new("sofia", "Sofía",
            "Colaboradora externa: es editora de Project Alpha pero NO es miembro de Acme. "
            + "Puede editar y no puede publicar. Es el caso que hace explotar los roles en RBAC."),
    ];

    public static IReadOnlyList<ScenarioEntity> Organizations =>
    [
        new("organization", "acme", "Acme Corporation"),
        new("organization", "globex", "Globex Industries"),
    ];

    public static IReadOnlyList<ScenarioEntity> Teams =>
    [
        new("team", "backend", "Team Backend", "organization:acme"),
        new("team", "frontend", "Team Frontend", "organization:acme"),
        new("team", "management", "Team Management", "organization:acme"),
        new("team", "sales", "Team Sales", "organization:globex"),
        new("team", "support", "Team Support", "organization:globex"),
    ];

    public static IReadOnlyList<ScenarioEntity> Groups =>
    [
        // Los grupos NO tienen organización: uno de ellos cruza fronteras a propósito, para
        // que se vea que agrupar y pertenecer a una organización son cosas distintas.
        new("group", "seguridad", "Grupo Seguridad"),
        new("group", "auditoria", "Grupo Auditoría"),
    ];

    public static IReadOnlyList<ScenarioEntity> Projects =>
    [
        new("project", "alpha", "Project Alpha", "organization:acme"),
        new("project", "beta", "Project Beta", "organization:acme"),
        new("project", "gamma", "Project Gamma", "organization:acme"),
        new("project", "delta", "Project Delta", "organization:globex"),
    ];

    public static IReadOnlyList<ScenarioEntity> Folders =>
    [
        new("folder", "docs", "Carpeta Docs", "project:alpha"),
        new("folder", "docs-sub", "Carpeta Docs / Interno", "folder:docs"),
        new("folder", "specs", "Carpeta Specs", "project:beta"),
    ];

    public static IReadOnlyList<ScenarioEntity> Resources =>
    [
        new("resource", "a", "Resource A", "project:alpha"),
        new("resource", "b", "Resource B", "folder:docs-sub"),
        new("resource", "c", "Resource C", "project:beta"),
        new("resource", "d", "Resource D", "project:delta"),
        new("resource", "e", "Resource E", "folder:specs"),
    ];

    /// <summary>
    /// Las tuplas iniciales, en la notación <c>objeto#relación@sujeto</c>.
    /// </summary>
    /// <remarks>
    /// Se declaran como texto y no construyendo objetos porque así <b>se leen</b>: esta lista
    /// es, literalmente, el estado completo de autorización del sistema. Todo lo que alguien
    /// puede hacer en el laboratorio sale de estas líneas.
    /// </remarks>
    public static IReadOnlyList<string> Tuples =>
    [
        // ── Jerarquía: quién está dentro de qué ─────────────────────────────────
        // Ninguna de estas tuplas concede acceso por sí sola. Son las vías por las que el
        // acceso viaja hacia abajo cuando alguna regla 'from parent' las recorre.
        "team:backend#parent@organization:acme",
        "team:frontend#parent@organization:acme",
        "team:management#parent@organization:acme",
        "team:sales#parent@organization:globex",
        "team:support#parent@organization:globex",

        "project:alpha#parent@organization:acme",
        "project:beta#parent@organization:acme",
        "project:gamma#parent@organization:acme",
        "project:delta#parent@organization:globex",

        "folder:docs#parent@project:alpha",
        "folder:docs-sub#parent@folder:docs",
        "folder:specs#parent@project:beta",

        "resource:a#parent@project:alpha",
        "resource:b#parent@folder:docs-sub",
        "resource:c#parent@project:beta",
        "resource:d#parent@project:delta",
        "resource:e#parent@folder:specs",

        // ── Pertenencia a equipos ───────────────────────────────────────────────
        "team:backend#member@user:juan",
        "team:frontend#member@user:pedro",
        "team:management#member@user:maria",
        "team:sales#member@user:ana",

        // ── Pertenencia a la organización, vía userset ──────────────────────────
        // CUATRO tuplas y toda la plantilla de Acme es miembro de Acme, hoy y en el futuro.
        // Contrátese a alguien mañana y bastará meterle en su equipo: no hay ninguna tabla
        // de "usuarios de la organización" que mantener. En RBAC esto serían N filas y un
        // proceso que las sincronice.
        "organization:acme#member@team:backend#member",
        "organization:acme#member@team:frontend#member",
        "organization:acme#member@team:management#member",
        "organization:globex#member@team:sales#member",

        // María es administradora de Acme. Esta única tupla, combinada con
        // 'can_edit: ... or admin from parent', le da edición sobre los cuatro proyectos de
        // Acme sin aparecer nombrada en ninguno.
        "organization:acme#admin@user:maria",

        // ── Grupos, incluido uno anidado ────────────────────────────────────────
        "group:seguridad#member@user:pedro",
        "group:auditoria#member@user:maria",
        // Grupo dentro de grupo: los de Seguridad son también de Auditoría. Pedro hereda
        // Auditoría sin que nadie le haya metido ahí.
        "group:auditoria#member@group:seguridad#member",

        // ── Accesos a proyectos ─────────────────────────────────────────────────
        // Caso B: el equipo es editor, no las personas.
        "project:alpha#editor@team:backend#member",
        // Caso H: Juan tiene ADEMÁS un acceso directo. Dos caminos hacia el mismo permiso.
        "project:alpha#viewer@user:juan",
        // Colaboradora externa: puede editar Alpha, pero no es de Acme.
        "project:alpha#editor@user:sofia",
        // Caso A: una tupla y toda la organización puede ver Beta.
        "project:beta#viewer@organization:acme#member",
        "project:delta#viewer@organization:globex#member",
        // Caso D: propiedad directa.
        "project:gamma#owner@user:pedro",
        // Comodín: Gamma es público para cualquier usuario autenticado.
        "project:gamma#viewer@user:*",

        // ── Accesos a carpetas ──────────────────────────────────────────────────
        // Caso C: Pedro solo tiene ESTA tupla en toda la jerarquía de Alpha, y con ella ve
        // la subcarpeta y el recurso que hay dentro.
        "folder:docs#viewer@user:pedro",

        // ── Accesos a recursos ──────────────────────────────────────────────────
        // Caso G: el acceso lo tiene el grupo.
        "resource:a#viewer@group:seguridad#member",
        // Caso F: compartición explícita. Es la ÚNICA vía de Ana hacia cualquier cosa de
        // Acme, así que si se borra esta tupla su acceso desaparece por completo.
        "resource:a#shared_with@user:ana",
        // Caso D sobre un recurso.
        "resource:c#owner@user:maria",
        // Exclusión: Ana vería Resource D por ser de Globex, pero está bloqueada en ese
        // recurso concreto. La exclusión gana a todos los caminos.
        "resource:d#blocked@user:ana",
        // Grupos anidados llevados hasta el final: Pedro → Seguridad → Auditoría → editor.
        "resource:e#editor@group:auditoria#member",
    ];

    /// <summary>
    /// Los casos guiados del laboratorio. Son a la vez tests, botones del frontend y
    /// ejemplos de la documentación.
    /// </summary>
    public static IReadOnlyList<ScenarioCase> Cases =>
    [
        new("A", "Acceso por pertenecer a la organización",
            "user:pedro", "can_view", "project:beta", true,
            "Pedro no aparece en Project Beta. Es miembro de Team Frontend, el equipo es "
            + "miembro de Acme, y Acme tiene 'viewer' sobre Beta. Tres saltos, ninguna tupla "
            + "que nombre a Pedro y a Beta juntos.",
            "En RBAC habría que crear un rol por organización y por proyecto, o mantener una "
            + "tabla de miembros de la organización sincronizada a mano."),

        new("B", "Acceso por pertenecer a un equipo",
            "user:juan", "can_edit", "project:alpha", true,
            "La tupla dice 'los miembros de backend son editores de alpha'. No nombra a "
            + "nadie. Juan entra o sale del equipo y su acceso aparece o desaparece sin que "
            + "nadie toque los permisos del proyecto.",
            "RBAC puede hacer esto con un rol 'editor de alpha', pero necesita una fila por "
            + "persona y proyecto. Con 500 proyectos son 500 roles."),

        new("C", "Herencia a través de una jerarquía de carpetas",
            "user:pedro", "can_view", "resource:b", true,
            "Pedro solo tiene 'viewer' sobre folder:docs. El recurso está dos niveles más "
            + "abajo (docs → docs-sub → resource:b) y el acceso baja solo, porque el modelo "
            + "dice 'can_view: ... or can_view from parent'.",
            "RBAC necesita propagar los permisos hacia abajo al conceder, y volver a "
            + "propagarlos cada vez que algo se mueve de carpeta. Aquí mover un recurso es "
            + "reescribir UNA tupla."),

        new("D", "Propiedad directa",
            "user:pedro", "can_delete", "project:gamma", true,
            "El caso más simple y el que RBAC también resuelve bien: una tupla directa entre "
            + "la persona y el objeto. Conviene tenerlo presente para no pensar que ReBAC va "
            + "de complicar las cosas."),

        new("E", "El permiso del proyecto llega al recurso",
            "user:juan", "can_edit", "resource:a", true,
            "Juan es editor de Alpha (por su equipo), y Resource A pertenece a Alpha. El "
            + "recurso no tiene ninguna tupla que mencione a Juan ni a su equipo.",
            "En RBAC hay que decidir al conceder si el permiso se propaga o no, y "
            + "materializarlo. Aquí se decide al preguntar."),

        new("F", "Compartición explícita con una persona",
            "user:ana", "can_view", "resource:a", true,
            "Ana es de Globex y no tiene absolutamente nada en Acme. Su único acceso es la "
            + "tupla 'shared_with'. Bórrala y pierde el acceso al instante; no hay ninguna "
            + "caché ni permiso materializado que haya que limpiar."),

        new("G", "Acceso a través de un grupo",
            "user:pedro", "can_view", "resource:a", true,
            "El acceso lo tiene 'group:seguridad#member', no Pedro. Los grupos funcionan "
            + "igual que los equipos porque en el modelo son lo mismo: un objeto con una "
            + "relación 'member' que se usa como userset."),

        new("H", "Varios caminos hacia el mismo permiso",
            "user:juan", "can_view", "project:alpha", true,
            "Juan llega por dos vías: 'viewer' directo, y 'editor' vía su equipo (porque "
            + "can_view incluye can_edit). Esto importa muchísimo al revocar: sacarle del "
            + "equipo NO le quita el acceso.",
            "En RBAC la pregunta '¿por qué tiene acceso?' tiene una respuesta. Aquí puede "
            + "tener varias, y hay que verlas todas antes de creer que has revocado algo.",
            MinimumPaths: 2),

        new("I", "Herencia desde la organización por ser administrador",
            "user:maria", "can_edit", "project:alpha", true,
            "María no aparece en Project Alpha. Es administradora de Acme y el modelo dice "
            + "'can_edit: ... or admin from parent'. Una tupla le da edición sobre todos los "
            + "proyectos presentes y futuros de la organización."),

        new("J", "Grupos anidados",
            "user:pedro", "can_edit", "resource:e", true,
            "Pedro está en Seguridad; Seguridad está dentro de Auditoría; Auditoría es "
            + "editora de Resource E. El motor no tiene ningún código especial para grupos "
            + "anidados: es la misma recursión que resuelve todo lo demás."),

        new("K", "Comodín: acceso público",
            "user:ana", "can_view", "project:gamma", true,
            "La tupla '@user:*' concede a cualquier usuario. Es la forma de modelar 'público' "
            + "sin escribir una tupla por persona, que es lo que haría RBAC."),

        new("L", "Denegado: no existe ninguna relación",
            "user:ana", "can_edit", "project:alpha", false,
            "Ana no tiene ninguna vía hacia Alpha. Fíjate en la explicación: enumera las tres "
            + "formas de conceder el permiso y propone las tuplas que lo arreglarían. Cada "
            + "DENY es una lección sobre el modelo."),

        new("M", "Exclusión: el bloqueo gana a todos los caminos",
            "user:ana", "can_view", "resource:d", false,
            "Ana SÍ tendría acceso: es miembro de Globex y Globex ve Project Delta, del que "
            + "cuelga Resource D. Pero hay una tupla 'blocked' sobre ella en ese recurso, y "
            + "la exclusión anula todos los caminos. Compáralo con el caso siguiente."),

        new("N", "La exclusión es local, no global",
            "user:ana", "can_view", "project:delta", true,
            "La misma Ana del caso anterior sí puede ver el proyecto. El bloqueo estaba en el "
            + "recurso, no en el proyecto: las exclusiones no se propagan hacia arriba."),

        new("O", "Intersección: editar no implica publicar",
            "user:sofia", "can_publish", "project:alpha", false,
            "Sofía es editora de Alpha, así que 'can_edit' es ALLOW. Pero 'can_publish' exige "
            + "'can_edit AND member from parent', y Sofía no es miembro de Acme. Es el caso "
            + "del colaborador externo.",
            "Aquí es donde RBAC empieza a inventar roles: 'editor', 'editor-que-publica', "
            + "'editor-externo'... La combinatoria de condiciones se convierte en catálogo de roles."),

        new("P", "La misma pregunta para alguien de la casa",
            "user:juan", "can_publish", "project:alpha", true,
            "Juan cumple las dos condiciones: puede editar (por su equipo) y es miembro de "
            + "Acme (también por su equipo). Mismo permiso, misma regla, resultado distinto "
            + "según las relaciones de cada uno."),
    ];

    /// <summary>Las tuplas ya parseadas, para alimentar cualquier almacén.</summary>
    public static IReadOnlyList<TupleKey> ParsedTuples() =>
        Tuples.Select(TupleKey.Parse).ToList();

    /// <summary>Todas las entidades de negocio del escenario.</summary>
    public static IEnumerable<ScenarioEntity> AllEntities() =>
        Organizations.Concat(Teams).Concat(Groups).Concat(Projects).Concat(Folders).Concat(Resources);
}
