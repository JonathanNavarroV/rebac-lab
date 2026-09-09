namespace Playground.AccessControl.Application.Authorization.Model;

/// <summary>
/// Nombres de tipos y relaciones usados en el laboratorio, como constantes.
/// </summary>
/// <remarks>
/// Existen para que el negocio y el seed no escriban cadenas a mano. Ojo con la tentación de
/// convertirlas en un <c>enum</c>: el conjunto de relaciones lo define el <b>modelo</b>, que
/// es un dato en base de datos y puede cambiar sin recompilar. Un enum daría la falsa
/// sensación de que la lista es cerrada.
/// </remarks>
public static class ObjectTypes
{
    public const string User = "user";
    public const string Organization = "organization";
    public const string Team = "team";
    public const string Group = "group";
    public const string Project = "project";
    public const string Folder = "folder";
    public const string Resource = "resource";
}

/// <inheritdoc cref="ObjectTypes"/>
public static class Relations
{
    // ── Relaciones que se escriben como tuplas (hechos declarados) ──────────────
    public const string Member = "member";
    public const string Admin = "admin";
    public const string Parent = "parent";
    public const string Owner = "owner";
    public const string Editor = "editor";
    public const string Viewer = "viewer";
    public const string SharedWith = "shared_with";
    public const string Blocked = "blocked";

    // ── Permisos derivados (los que pregunta el negocio; NUNCA se escriben) ─────
    public const string CanView = "can_view";
    public const string CanEdit = "can_edit";
    public const string CanDelete = "can_delete";
    public const string CanPublish = "can_publish";
    public const string CanManageMembers = "can_manage_members";
}

/// <summary>
/// Los modelos de autorización del laboratorio, en DSL.
/// </summary>
/// <remarks>
/// <para>
/// Hay varias variantes publicadas a la vez a propósito. Poder responder <b>la misma
/// pregunta con dos modelos distintos</b> y ver en qué difieren es la forma más directa de
/// entender qué hace cada regla, y es lo que da sentido al punto 4 del laboratorio
/// ("quiero que la herencia sea configurable").
/// </para>
/// </remarks>
public static class PlaygroundModels
{
    /// <summary>
    /// El modelo principal. Todo lo que el laboratorio necesita demostrar está aquí.
    /// </summary>
    /// <remarks>
    /// <para><b>Dos convenciones de modelado que conviene entender antes de leerlo:</b></para>
    /// <para>
    /// <b>1. Se separan las relaciones que se escriben de los permisos que se preguntan.</b>
    /// <c>owner</c>, <c>editor</c>, <c>viewer</c>, <c>member</c> y <c>parent</c> son
    /// <i>hechos</i>: alguien los declara escribiendo una tupla. <c>can_view</c>,
    /// <c>can_edit</c> y <c>can_delete</c> son <i>conclusiones</i>: nunca se escriben, se
    /// calculan. El negocio pregunta siempre por las segundas.
    /// </para>
    /// <para>
    /// Esa separación cuesta cuatro líneas de modelo y compra muchísimo: el día que decidas
    /// que los administradores de la organización también pueden borrar proyectos, cambias
    /// <c>can_delete</c> en el modelo y no tocas ni una línea de negocio ni una sola tupla.
    /// Si el negocio preguntara por <c>owner</c> directamente, ese cambio sería una
    /// migración de datos.
    /// </para>
    /// <para>
    /// <b>2. No se usan paréntesis.</b> La precedencia (<c>and</c> &gt; <c>or</c> &gt;
    /// <c>but not</c>) basta, y así el modelo es válido tal cual en OpenFGA. Cuando hace
    /// falta combinar exclusión con unión, se introduce una relación intermedia
    /// (<c>viewable</c> en <c>resource</c>), que es lo que OpenFGA obliga a hacer — y que de
    /// paso deja la intención más clara que un paréntesis.
    /// </para>
    /// </remarks>
    public const string Full =
        """
        model
          schema 1.1

        // ═══════════════════════════════════════════════════════════════════════════
        // user — el tipo sin relaciones
        // ═══════════════════════════════════════════════════════════════════════════
        // Un usuario nunca es objeto de una comprobación, solo sujeto. No tiene
        // relaciones propias y eso es correcto: en ReBAC el usuario no "tiene permisos",
        // es un extremo de relaciones que cuelgan de los objetos.

        type user

        // ═══════════════════════════════════════════════════════════════════════════
        // organization — la raíz de la jerarquía
        // ═══════════════════════════════════════════════════════════════════════════

        type organization
          relations
            // Admite un userset 'team#member': con UNA tupla
            // (organization:acme#member@team:backend#member) todos los miembros del equipo
            // pasan a ser miembros de la organización, hoy y en el futuro. Esto es lo que
            // en RBAC exigiría una fila por persona y un proceso que las mantenga.
            define member: [user, team#member]

            // Se mantiene separada de 'member' porque conceden cosas distintas: los
            // administradores heredan can_edit y can_delete sobre todos los proyectos
            // (ver 'admin from parent' más abajo), los miembros no.
            define admin: [user]

        // ═══════════════════════════════════════════════════════════════════════════
        // team y group — dos formas de agrupar, a propósito distintas
        // ═══════════════════════════════════════════════════════════════════════════
        // La diferencia no es cosmética: 'team' pertenece a una organización y 'group' no.
        // Un grupo puede cruzar organizaciones (un grupo "Seguridad" con gente de Acme y de
        // Globex), un equipo no. Tener los dos permite ver que el modelo no impone una
        // única forma de agrupar: cada tipo declara sus propias reglas.

        type team
          relations
            define parent: [organization]

            // Equipos dentro de equipos. Al admitirse 'team#member' como sujeto, la
            // anidación sale gratis y a cualquier profundidad.
            define member: [user, team#member]

        type group
          relations
            // Grupos anidados sin límite de profundidad. Es también el sitio donde se puede
            // provocar un ciclo (A miembro de B, B miembro de A) para ver cómo lo detecta
            // el motor en lugar de colgarse.
            define member: [user, group#member]

        // ═══════════════════════════════════════════════════════════════════════════
        // project — el primer objeto realmente protegido
        // ═══════════════════════════════════════════════════════════════════════════

        type project
          relations
            define parent: [organization]

            // ── Hechos: esto es lo que se escribe como tupla ──────────────────────
            define owner: [user]

            // Admite individuos Y usersets. La misma relación sirve para
            // 'project:alpha#editor@user:juan' (acceso directo) y para
            // 'project:alpha#editor@team:backend#member' (acceso por equipo). Que sean la
            // misma relación es importante: el motor no necesita saber de antemano si el
            // acceso vino por una vía o por otra.
            define editor: [user, team#member, group#member]

            // 'organization#member' es lo que resuelve el Caso A: con una sola tupla
            // (project:beta#viewer@organization:acme#member) todos los miembros de Acme
            // pueden ver Beta. Y 'user:*' permite marcar un proyecto como público sin
            // escribir una tupla por usuario.
            define viewer: [user, team#member, group#member, organization#member, user:*]

            // ── Conclusiones: esto NUNCA se escribe, se calcula ───────────────────

            // Unión simple: cualquiera de las tres vías basta. Es lo que produce los
            // múltiples caminos de autorización del Caso H.
            define can_view: viewer or can_edit

            // 'admin from parent' es un tuple_to_userset: sube por 'parent' hasta la
            // organización y pregunta 'admin' allí. Un administrador de Acme puede editar
            // todos sus proyectos sin que exista ninguna tupla que le nombre en ellos.
            define can_edit: editor or owner or admin from parent

            define can_delete: owner or admin from parent

            // Intersección: hay que poder editar Y ser miembro de la organización. Modela
            // el caso del colaborador externo, al que se le da 'editor' sobre un proyecto
            // concreto pero que no debe poder publicar porque no es de la casa.
            // Con RBAC esto obliga a inventar el rol "editor-externo-que-no-publica".
            define can_publish: can_edit and member from parent

            define can_manage_members: owner or admin from parent

        // ═══════════════════════════════════════════════════════════════════════════
        // folder — la jerarquía recursiva
        // ═══════════════════════════════════════════════════════════════════════════

        type folder
          relations
            // 'parent' admite 'folder', es decir, el propio tipo. Eso hace que las tres
            // reglas 'from parent' de más abajo recorran el árbol completo, a cualquier
            // profundidad, sin que el modelo sepa cuántos niveles hay. Un árbol de 50
            // niveles usa exactamente estas mismas líneas.
            define parent: [project, folder]

            define owner: [user]
            define viewer: [user, team#member, group#member]
            define editor: [user, team#member, group#member]

            // Esta línea es, probablemente, la más importante del modelo. 'can_view from
            // parent' aplicado a un tipo cuyo 'parent' puede ser él mismo ES la herencia
            // jerárquica. Bórrala y las carpetas dejan de heredar (ver la variante
            // WithoutFolderInheritance). En RBAC el equivalente es un job nocturno que
            // propaga permisos hacia abajo y que hay que volver a ejecutar cada vez que
            // alguien mueve una carpeta.
            define can_view: viewer or can_edit or can_view from parent

            define can_edit: editor or owner or can_edit from parent
            define can_delete: owner or can_delete from parent

        // ═══════════════════════════════════════════════════════════════════════════
        // resource — la hoja, con compartición y bloqueo
        // ═══════════════════════════════════════════════════════════════════════════

        type resource
          relations
            // El padre puede ser un proyecto o una carpeta. El mismo recurso movido de
            // sitio cambia de permisos heredados con solo reescribir esta tupla: no hay
            // nada que recalcular.
            define parent: [project, folder]

            define owner: [user]
            define viewer: [user, team#member, group#member, organization#member, user:*]
            define editor: [user, team#member, group#member]

            // Compartición explícita, separada de 'viewer' a propósito. Funcionalmente
            // podría ser lo mismo, pero tenerla aparte permite responder "¿esto lo ve por
            // su rol o porque alguien se lo compartió?", que es justo lo que hace falta
            // saber en una revisión de accesos. La relación es el registro de la intención.
            define shared_with: [user, team#member, group#member, organization#member]

            // Bloqueo explícito de una persona sobre un recurso concreto.
            define blocked: [user]

            // Relación intermedia: junta todas las vías por las que se PODRÍA ver.
            // Existe para no mezclar 'or' con 'but not' en la misma línea, que es lo que
            // OpenFGA exige y lo que mantiene este modelo portable.
            define viewable: viewer or shared_with or can_edit or can_view from parent

            // La exclusión gana siempre: da igual cuántos caminos concedan acceso, una
            // tupla 'blocked' los anula todos. Ojo, esta línea tiene un coste oculto
            // importante: rompe la expansión inversa de ListObjects, porque no se puede
            // saber a quién hay que quitar sin comprobarlo objeto a objeto. Ver docs/04.
            define can_view: viewable but not blocked

            define can_edit: editor or owner or can_edit from parent
            define can_delete: owner or can_delete from parent
        """;

    /// <summary>
    /// Variante sin herencia jerárquica en carpetas.
    /// </summary>
    /// <remarks>
    /// Es idéntica a <see cref="Full"/> salvo que a <c>folder.can_view</c> y
    /// <c>folder.can_edit</c> se les ha quitado la cláusula <c>from parent</c>. Comparar las
    /// dos respondiendo la misma pregunta enseña de golpe qué hace exactamente un
    /// <c>tuple_to_userset</c>: con <see cref="Full"/>, un viewer de la carpeta raíz ve todo
    /// el árbol; con esta, solo ve la carpeta que se le concedió.
    /// </remarks>
    public const string WithoutFolderInheritance =
        """
        model
          schema 1.1

        type user

        type organization
          relations
            define member: [user, team#member]
            define admin: [user]

        type team
          relations
            define parent: [organization]
            define member: [user, team#member]

        type group
          relations
            define member: [user, group#member]

        type project
          relations
            define parent: [organization]
            define owner: [user]
            define editor: [user, team#member, group#member]
            define viewer: [user, team#member, group#member, organization#member, user:*]
            define can_view: viewer or can_edit
            define can_edit: editor or owner or admin from parent
            define can_delete: owner or admin from parent
            define can_publish: can_edit and member from parent
            define can_manage_members: owner or admin from parent

        type folder
          relations
            define parent: [project, folder]
            define owner: [user]
            define viewer: [user, team#member, group#member]
            define editor: [user, team#member, group#member]
            // SIN 'from parent': la carpeta ya no hereda nada de su padre.
            define can_view: viewer or can_edit
            define can_edit: editor or owner
            define can_delete: owner

        type resource
          relations
            define parent: [project, folder]
            define owner: [user]
            define viewer: [user, team#member, group#member, organization#member, user:*]
            define editor: [user, team#member, group#member]
            define shared_with: [user, team#member, group#member, organization#member]
            define blocked: [user]
            define viewable: viewer or shared_with or can_edit or can_view from parent
            define can_view: viewable but not blocked
            define can_edit: editor or owner or can_edit from parent
            define can_delete: owner or can_delete from parent
        """;

    /// <summary>
    /// Modelo mínimo que imita RBAC, para tener el contraste dentro del propio motor ReBAC.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Aquí está el argumento central del laboratorio, y por eso este modelo merece
    /// existir: <b>ReBAC contiene a RBAC</b>. Un rol es simplemente un objeto intermedio con
    /// una relación <c>assignee</c>, y "tener un permiso" es tener una relación con ese
    /// objeto. Se puede modelar en cinco líneas.
    /// </para>
    /// <para>
    /// Lo que <b>no</b> se puede hacer al revés: no existe forma de expresar
    /// "los miembros del equipo que es editor del proyecto padre de esta carpeta" en un
    /// esquema usuario→rol→permiso, porque en RBAC el permiso no tiene sujeto ni contexto.
    /// La única salida es multiplicar roles (<c>editor-proyecto-alpha</c>,
    /// <c>editor-proyecto-beta</c>, ...) hasta que el catálogo de roles es inmanejable. Es
    /// lo que en la literatura se llama <i>role explosion</i>.
    /// </para>
    /// </remarks>
    public const string RbacEquivalent =
        """
        model
          schema 1.1

        type user

        // Un rol es un objeto como cualquier otro. 'assignee' es la tabla user_roles.
        type role
          relations
            define assignee: [user]

        // El permiso se concede a un userset de rol, no a personas. Es exactamente
        // 'role_permissions', pero expresado como relación.
        type project
          relations
            define can_view: [user, role#assignee]
            define can_edit: [user, role#assignee]
            define can_delete: [user, role#assignee]
        """;
}
