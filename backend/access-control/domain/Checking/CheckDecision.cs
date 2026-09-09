using Playground.AccessControl.Domain.Model;

namespace Playground.AccessControl.Domain.Checking;

/// <summary>
/// Coste de una evaluación. Existe porque en autorización el rendimiento no es un detalle
/// de implementación: el Check está en el camino crítico de <b>cada</b> petición del sistema.
/// </summary>
/// <remarks>
/// Google reporta en el paper un p95 por debajo de 10 ms sobre miles de millones de tuplas.
/// Ese número es el que justifica toda la maquinaria (caché distribuida, índice Leopard,
/// consistencia relajada). Medir aquí las mismas magnitudes, aunque sea a escala de juguete,
/// permite ver de dónde sale el coste: casi siempre de <see cref="StoreQueries"/>, no de CPU.
/// </remarks>
public sealed record CheckMetrics
{
    /// <summary>Consultas lanzadas contra el almacén de tuplas. El coste real está aquí.</summary>
    public int StoreQueries { get; init; }

    /// <summary>Tuplas leídas en total. Sirve para ver el efecto de un userset muy poblado.</summary>
    public int TuplesRead { get; init; }

    /// <summary>Nodos del árbol de evaluación recorridos.</summary>
    public int NodesEvaluated { get; init; }

    /// <summary>Profundidad máxima alcanzada. Delata jerarquías profundas.</summary>
    public int MaxDepthReached { get; init; }

    /// <summary>Subárboles servidos desde la memoización.</summary>
    public int MemoizationHits { get; init; }

    /// <summary>Ramas abandonadas por detectar un ciclo.</summary>
    public int CyclesDetected { get; init; }

    /// <summary>
    /// Checks de confirmación ejecutados durante un listado por expansión inversa.
    /// </summary>
    /// <remarks>
    /// Es el precio de las reglas no monótonas (<c>but not</c>, <c>and</c>): la expansión
    /// solo puede producir un superconjunto de candidatos y hay que comprobar cada uno. Si
    /// este número es alto, el listado ha degenerado prácticamente en la estrategia naive, y
    /// merece la pena mirar si la exclusión se puede mover a otro sitio del modelo.
    /// </remarks>
    public int ConfirmationChecks { get; init; }

    /// <summary>Duración total de la evaluación.</summary>
    public double DurationMs { get; init; }

    /// <summary>
    /// <c>InProcess</c> o <c>Remote</c>. Al mostrarlo junto a <see cref="DurationMs"/> queda
    /// a la vista el coste de que el control de acceso sea un servicio aparte.
    /// </summary>
    public string? EvaluationMode { get; init; }
}

/// <summary>
/// Resultado de un Check: la decisión, el porqué y lo que costó.
/// </summary>
/// <remarks>
/// <para>
/// Devolver un objeto y no un <c>bool</c> es una decisión de diseño consciente del
/// laboratorio. El negocio solo mirará <see cref="Allowed"/>, pero todo lo demás es
/// gratis de producir (el motor ya recorrió el árbol) y es lo que hace posible aprender.
/// </para>
/// <para>
/// En un sistema real se devolvería el booleano al negocio y la traza solo bajo una bandera
/// de depuración, porque serializar el árbol en cada petición es caro.
/// </para>
/// </remarks>
public sealed record CheckDecision
{
    public required bool Allowed { get; init; }

    /// <summary>Sujeto por el que se preguntó.</summary>
    public required SubjectRef Subject { get; init; }

    /// <summary>Relación o permiso por el que se preguntó.</summary>
    public required string Relation { get; init; }

    /// <summary>Objeto sobre el que se preguntó.</summary>
    public required ObjectRef Object { get; init; }

    /// <summary>
    /// Explicación en lenguaje natural, redactada por el explicador a partir del árbol.
    /// Es lo que se muestra en grande en el Authorization Explorer.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>Árbol de evaluación completo, ramas fallidas incluidas.</summary>
    public CheckNode? Trace { get; init; }

    /// <summary>
    /// Caminos que conceden acceso. Vacío si <see cref="Allowed"/> es <c>false</c>. Con
    /// <see cref="CheckOptions.FindAllPaths"/> desactivado contiene como máximo uno.
    /// </summary>
    public IReadOnlyList<AuthorizationPath> Paths { get; init; } = [];

    /// <summary>
    /// Tuplas que, creadas, convertirían este DENY en ALLOW. Cada una es una lección sobre
    /// el modelo: enseña por qué vías se puede conceder acceso a este objeto.
    /// </summary>
    public IReadOnlyList<TupleKey> SuggestedTuples { get; init; } = [];

    /// <summary>Identificador del modelo con el que se evaluó. Importa al comparar versiones.</summary>
    public string? ModelId { get; init; }

    public required CheckMetrics Metrics { get; init; }

    public override string ToString() =>
        $"{(Allowed ? "ALLOW" : "DENY")}  {Subject} --{Relation}--> {Object}";
}
