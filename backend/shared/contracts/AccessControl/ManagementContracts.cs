namespace Playground.Contracts.AccessControl;

// ═════════════════════════════════════════════════════════════════════════════
// Relaciones (tuplas)
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Alta de una relación. Admite las dos formas de escribirla.
/// </summary>
/// <param name="Tuple">
/// Notación completa: <c>project:alpha#editor@team:backend#member</c>. Si viene informada,
/// los tres campos sueltos se ignoran.
/// </param>
public sealed record CreateRelationshipRequest(
    string? Tuple = null,
    string? Object = null,
    string? Relation = null,
    string? Subject = null);

public sealed record RelationshipDto(
    long Id,
    string Tuple,
    string Object,
    string ObjectType,
    string ObjectId,
    string Relation,
    string Subject,
    string SubjectType,
    string SubjectId,
    string? SubjectRelation,
    bool IsUserset,
    bool IsWildcard,
    DateTime CreatedAt);

/// <summary>
/// Resultado de crear o borrar una relación, con el efecto que tuvo sobre las decisiones.
/// </summary>
/// <remarks>
/// <see cref="AffectedChecks"/> es la razón de ser del laboratorio: al crear una tupla se
/// re-evalúan los casos guiados y se devuelve cuáles han cambiado de respuesta. Es la forma
/// más directa de ver que <b>una sola relación puede cambiar muchas decisiones a la vez</b>,
/// que es exactamente lo que pedía el punto 6 del enunciado.
/// </remarks>
public sealed record RelationshipMutationResponse(
    RelationshipDto? Relationship,
    bool Created,
    IReadOnlyList<DecisionChangeDto> AffectedChecks);

/// <summary>Una decisión que ha cambiado de resultado a raíz de una escritura.</summary>
public sealed record DecisionChangeDto(
    string Subject,
    string Relation,
    string Object,
    bool Before,
    bool After,
    string Reason);

// ═════════════════════════════════════════════════════════════════════════════
// Modelo de autorización
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Una relación del modelo, ya descompuesta para la pantalla de administración.</summary>
/// <param name="Kind">Variante de la regla: <c>_this</c>, <c>union</c>, <c>tuple_to_userset</c>...</param>
/// <param name="IsDirectlyAssignable">
/// <c>true</c> si admite tuplas. Distinguirlo en la UI es importante: las relaciones que no lo
/// son son <i>conclusiones</i> del modelo y escribirlas sería materializar un permiso.
/// </param>
public sealed record ModelRelationDto(
    string Name,
    string Expression,
    string Kind,
    bool IsDirectlyAssignable,
    IReadOnlyList<string> AcceptedSubjects,
    string? Comment);

public sealed record ModelTypeDto(
    string Name,
    IReadOnlyList<ModelRelationDto> Relations,
    string? Comment);

public sealed record AuthorizationModelDto(
    string Id,
    string SchemaVersion,
    string? Name,
    string? Description,
    string Dsl,
    IReadOnlyList<ModelTypeDto> Types,
    bool IsCurrent);

public sealed record PublishModelRequest(string Dsl, string? Name = null, string? Description = null);

// ═════════════════════════════════════════════════════════════════════════════
// Grafo
// ═════════════════════════════════════════════════════════════════════════════

/// <param name="Id">Identificador del nodo: <c>project:alpha</c>.</param>
/// <param name="Kind">Tipo del objeto, para colorear.</param>
public sealed record GraphNodeDto(string Id, string Label, string Kind, string? DisplayName);

/// <param name="Userset">
/// <c>true</c> si la arista sale de un userset (<c>team:backend#member</c>) en lugar de un
/// individuo. En el grafo se dibuja distinta porque significa algo distinto: no es "esta
/// persona tiene acceso" sino "quien pertenezca a este conjunto lo tendrá".
/// </param>
public sealed record GraphEdgeDto(
    long Id,
    string Source,
    string Target,
    string Relation,
    bool Userset,
    bool Wildcard,
    string Tuple);

public sealed record GraphResponse(
    IReadOnlyList<GraphNodeDto> Nodes,
    IReadOnlyList<GraphEdgeDto> Edges);

// ═════════════════════════════════════════════════════════════════════════════
// Auditoría
// ═════════════════════════════════════════════════════════════════════════════

public sealed record AuditEntryDto(
    long Id,
    DateTime Timestamp,
    string Subject,
    string Relation,
    string Object,
    bool Allowed,
    string Reason,
    string? PathJson,
    string? ModelId,
    string? EvaluationMode,
    double DurationMs,
    int StoreQueries,
    string? Origin);

// ═════════════════════════════════════════════════════════════════════════════
// Casos guiados
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Un caso guiado del laboratorio, con su resultado actual.
/// </summary>
/// <remarks>
/// <see cref="StillMatchesExpectation"/> permite que la pantalla avise cuando has cambiado
/// tantas relaciones que el escenario original ya no se cumple. Es útil: al experimentar es
/// muy fácil romper sin querer el caso que estabas estudiando.
/// </remarks>
public sealed record GuidedCaseDto(
    string Code,
    string Title,
    string Subject,
    string Relation,
    string Object,
    bool ExpectedAllowed,
    bool? ActualAllowed,
    bool StillMatchesExpectation,
    string WhatItTeaches,
    string? WhyRbacStruggles,
    int MinimumPaths);
