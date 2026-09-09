namespace Playground.Business.Domain.Authorization;

/// <summary>
/// Decisión tal y como la ve el negocio.
/// </summary>
/// <remarks>
/// Mucho más pobre que la <c>CheckDecision</c> del módulo de control de acceso, y eso es
/// intencionado. El negocio solo necesita <see cref="Allowed"/>; <see cref="Reason"/> está
/// para el mensaje de error, y las métricas para que el laboratorio pueda enseñar la latencia.
/// Ni traza, ni caminos, ni sugerencias: si el negocio recibiera eso, acabaría razonando
/// sobre el modelo de autorización, que es justo lo que se quiere evitar.
/// </remarks>
public sealed record AccessDecision(
    bool Allowed,
    string Reason,
    string Mode,
    double DurationMs,
    int StoreQueries = 0)
{
    public static AccessDecision Denied(string reason) => new(false, reason, "Unknown", 0);
}

/// <summary>
/// <b>El puerto.</b> Todo lo que el negocio sabe sobre autorización cabe en esta interfaz.
/// </summary>
/// <remarks>
/// <para>
/// Merece la pena leerla entera y fijarse en lo pequeña que es. No hay <c>GetPermissions</c>,
/// ni <c>GetRoles</c>, ni <c>IsAdmin</c>, ni <c>HasAccessLevel</c>. Solo se puede preguntar
/// si un sujeto puede hacer algo, y pedir la lista de lo que puede.
/// </para>
/// <para>
/// Esa estrechez es la que hace que el modelo de autorización pueda cambiar de arriba abajo
/// sin tocar el negocio. Si mañana decides que los administradores de la organización también
/// borran proyectos, o que las carpetas dejan de heredar, o que hace falta ser miembro para
/// publicar, el negocio sigue preguntando exactamente lo mismo.
/// </para>
/// <para>
/// La interfaz la define el <b>negocio</b>, no el módulo de control de acceso, y esa dirección
/// importa: es el negocio quien declara qué necesita saber. Las implementaciones
/// (<c>InProcess</c> y <c>Remote</c>) viven en la infraestructura.
/// </para>
/// </remarks>
public interface IAccessControlService
{
    /// <summary>
    /// ¿Puede <paramref name="subject"/> hacer <paramref name="relation"/> sobre
    /// <paramref name="object"/>?
    /// </summary>
    /// <param name="subject">Notación de sujeto: <c>user:juan</c>.</param>
    /// <param name="relation">Permiso: <c>can_edit</c>.</param>
    /// <param name="object">Notación de objeto: <c>project:alpha</c>.</param>
    Task<AccessDecision> CanAsync(
        string subject,
        string relation,
        string @object,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Varias preguntas de golpe.
    /// </summary>
    /// <remarks>
    /// El negocio lo usa al pintar listas: para cada proyecto hace falta saber si se puede
    /// editar y si se puede borrar, y eso son dos preguntas por fila. Con el módulo remoto,
    /// hacerlas de una en una multiplica la latencia por el número de filas.
    /// </remarks>
    Task<IReadOnlyList<AccessDecision>> CanManyAsync(
        IReadOnlyList<(string Subject, string Relation, string Object)> questions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Los objetos de un tipo sobre los que el sujeto tiene la relación indicada.
    /// </summary>
    /// <remarks>
    /// Devuelve referencias (<c>project:alpha</c>), no entidades: el módulo de control de
    /// acceso no conoce las entidades del negocio. Es el negocio quien cruza esta lista con su
    /// propio catálogo, que es el patrón correcto de integración.
    /// </remarks>
    Task<IReadOnlyList<string>> ListAuthorizedAsync(
        string subject,
        string relation,
        string objectType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Declara una relación.
    /// </summary>
    /// <remarks>
    /// El negocio la necesita al crear entidades: al dar de alta un proyecto hay que declarar
    /// quién es su dueño y de qué organización cuelga. Fíjate en que el negocio <b>declara
    /// hechos</b> (<c>owner</c>, <c>parent</c>), nunca permisos: no existe forma de decir
    /// "concede can_edit a Juan".
    /// </remarks>
    Task WriteRelationshipAsync(
        string @object,
        string relation,
        string subject,
        CancellationToken cancellationToken = default);

    /// <summary>Modo activo: <c>InProcess</c> o <c>Remote</c>. Solo para mostrarlo en la UI.</summary>
    string Mode { get; }
}
