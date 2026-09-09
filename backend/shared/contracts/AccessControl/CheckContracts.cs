namespace Playground.Contracts.AccessControl;

/// <summary>
/// Pregunta de autorización. Es <b>el</b> contrato del sistema: todo lo demás gira alrededor.
/// </summary>
/// <remarks>
/// <para>
/// Fíjate en lo que tiene y en lo que no. Tiene tres cadenas. No tiene tenant, ni rol, ni
/// lista de permisos, ni contexto de sesión, ni atributos del recurso. Esa pobreza es
/// intencionada y es lo que hace que el negocio pueda preguntar sin saber nada del modelo de
/// autorización.
/// </para>
/// <para>
/// Es también, casi literalmente, la firma del <c>Check</c> del paper de Zanzibar.
/// </para>
/// </remarks>
/// <param name="Subject">Sujeto en notación <c>user:juan</c> o <c>team:backend#member</c>.</param>
/// <param name="Relation">Relación o permiso: <c>can_edit</c>.</param>
/// <param name="Object">Objeto en notación <c>project:alpha</c>.</param>
/// <param name="Explain">
/// Si es <c>true</c>, se recorren todas las ramas para poder devolver la traza, todos los
/// caminos y las sugerencias. Más caro; es lo que usa el Authorization Explorer.
/// </param>
/// <param name="ModelId">Modelo con el que evaluar. <c>null</c> = el vigente.</param>
/// <param name="MaxDepth">Límite de profundidad. <c>null</c> = el del sistema (25).</param>
public sealed record CheckRequest(
    string Subject,
    string Relation,
    string Object,
    bool Explain = false,
    string? ModelId = null,
    int? MaxDepth = null);

/// <summary>Un salto dentro de un camino de autorización.</summary>
public sealed record PathStepDto(string Kind, string Description, string? Tuple);

/// <summary>Un camino completo que concede acceso.</summary>
public sealed record AuthorizationPathDto(int Length, string Chain, IReadOnlyList<PathStepDto> Steps);

/// <summary>Un nodo del árbol de evaluación.</summary>
public sealed record CheckTraceNodeDto(
    string Kind,
    string Label,
    bool Allowed,
    string? Detail,
    string? Tuple,
    int Depth,
    bool FromCache,
    IReadOnlyList<CheckTraceNodeDto> Children);

/// <summary>Coste de la evaluación.</summary>
public sealed record CheckMetricsDto(
    int StoreQueries,
    int TuplesRead,
    int NodesEvaluated,
    int MaxDepthReached,
    int MemoizationHits,
    int CyclesDetected,
    int ConfirmationChecks,
    double DurationMs,
    string? EvaluationMode);

/// <summary>Respuesta de un <c>Check</c>.</summary>
public sealed record CheckResponse(
    bool Allowed,
    string Subject,
    string Relation,
    string Object,
    string Reason,
    IReadOnlyList<AuthorizationPathDto> Paths,
    IReadOnlyList<string> SuggestedTuples,
    CheckTraceNodeDto? Trace,
    string? ModelId,
    CheckMetricsDto Metrics);

/// <summary>Varias preguntas en una sola llamada.</summary>
/// <remarks>
/// Existe por la latencia de red, no por la CPU. Pintar una lista de 50 elementos con sus
/// botones de editar y borrar son 100 checks: en proceso da igual, por HTTP son 100 viajes.
/// </remarks>
public sealed record BatchCheckRequest(IReadOnlyList<CheckRequest> Checks);

public sealed record BatchCheckResponse(IReadOnlyList<CheckResponse> Results, CheckMetricsDto Totals);

/// <summary>Petición de expansión: quién tiene esta relación sobre este objeto.</summary>
public sealed record ExpandRequest(string Object, string Relation, string? ModelId = null);

public sealed record ExpandNodeDto(
    string Kind,
    string Label,
    string? Object,
    string? Relation,
    string? Subject,
    string? Tuple,
    IReadOnlyList<ExpandNodeDto> Children);

public sealed record ExpandResponse(
    string Object,
    string Relation,
    ExpandNodeDto Root,
    IReadOnlyList<string> Leaves,
    CheckMetricsDto Metrics);

/// <summary>Petición de listado de objetos autorizados.</summary>
/// <param name="Strategy"><c>naive</c> o <c>reverse</c>.</param>
public sealed record ListObjectsRequest(
    string Subject,
    string Relation,
    string ObjectType,
    string Strategy = "reverse",
    string? ModelId = null);

public sealed record AuthorizedObjectDto(string Object, string Reason, AuthorizationPathDto? Path);

public sealed record ListObjectsResponse(
    string Subject,
    string Relation,
    string ObjectType,
    string Strategy,
    IReadOnlyList<AuthorizedObjectDto> Objects,
    IReadOnlyList<AuthorizedObjectDto> Denied,
    CheckMetricsDto Metrics);

/// <summary>
/// Resultado de responder la misma pregunta con las dos estrategias de listado.
/// </summary>
/// <remarks>
/// <see cref="SameResult"/> es el dato importante: si alguna vez es <c>false</c>, hay un bug
/// en la expansión inversa, porque las dos estrategias deben ser semánticamente equivalentes.
/// Tenerlo en la respuesta convierte la pantalla de comparación en una verificación continua.
/// </remarks>
public sealed record ListObjectsComparisonResponse(
    ListObjectsResponse Naive,
    ListObjectsResponse Reverse,
    bool SameResult,
    string Verdict);
