namespace Playground.AccessControl.Infrastructure.Persistence.Entities;

/// <summary>
/// La tabla de tuplas: <b>el único estado persistente del control de acceso</b>.
/// </summary>
/// <remarks>
/// <para>
/// Una sola tabla para todo el sistema. No hay <c>project_permissions</c>, ni
/// <c>folder_acl</c>, ni <c>user_roles</c>. Todo — pertenencias a equipos, jerarquías,
/// comparticiones, bloqueos, propiedad — son filas de aquí, distinguidas únicamente por el
/// valor de la columna <c>relation</c>.
/// </para>
/// <para>
/// Esa uniformidad es lo que permite añadir un tipo de recurso protegido nuevo sin ninguna
/// migración: se declara en el modelo de autorización, que es un dato, y ya está.
/// </para>
/// <para>
/// A cambio, la tabla es enorme y de acceso constante: en Google son miles de millones de
/// filas y cada petición del sistema la consulta varias veces. Por eso los índices de
/// <c>RelationshipTupleConfiguration</c> no son una optimización opcional, son el diseño.
/// </para>
/// </remarks>
public sealed class RelationshipTupleEntity
{
    public long Id { get; set; }

    /// <summary>Tipo del objeto protegido: <c>project</c>.</summary>
    public string ObjectType { get; set; } = null!;

    /// <summary>Identificador del objeto: <c>alpha</c>.</summary>
    public string ObjectId { get; set; } = null!;

    /// <summary>Relación: <c>editor</c>, <c>member</c>, <c>parent</c>...</summary>
    public string Relation { get; set; } = null!;

    /// <summary>Tipo del sujeto: <c>user</c>, <c>team</c>, <c>organization</c>.</summary>
    public string SubjectType { get; set; } = null!;

    /// <summary>Identificador del sujeto, o <c>*</c> si es un comodín.</summary>
    public string SubjectId { get; set; } = null!;

    /// <summary>
    /// Relación del userset, o <c>null</c> si el sujeto es un individuo.
    /// </summary>
    /// <remarks>
    /// <b>Esta columna es la diferencia entre RBAC y ReBAC.</b> Con ella a <c>null</c>, la
    /// fila dice "Juan es editor de Alpha", que es lo que sabe expresar cualquier sistema de
    /// permisos. Con <c>'member'</c>, la fila dice "quien sea miembro de Backend es editor de
    /// Alpha" — no nombra a nadie, y el conjunto se evalúa en el momento de preguntar.
    /// </remarks>
    public string? SubjectRelation { get; set; }

    public DateTime CreatedAt { get; set; }
}
