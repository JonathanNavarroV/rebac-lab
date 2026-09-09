using System.Diagnostics;
using Playground.AccessControl.Domain.Abstractions;
using Playground.AccessControl.Domain.Checking;
using Playground.AccessControl.Domain.Model;

namespace Playground.AccessControl.Application.Authorization.Engine;

/// <summary>
/// Estado de <b>una sola</b> evaluación: contador de métricas, memoización y pila de nodos
/// en visita para detectar ciclos.
/// </summary>
/// <remarks>
/// <para>
/// Es importante que este objeto viva y muera con la evaluación, y merece la pena entender
/// por qué, porque es la diferencia entre una caché segura y una caché que provoca fallos de
/// seguridad.
/// </para>
/// <para>
/// La memoización de aquí <b>no puede quedar obsoleta</b>: nace al empezar el Check y se tira
/// al terminar, así que no existe ninguna ventana en la que una tupla borrada siga
/// concediendo acceso. Es una caché "gratis" en términos de correctitud.
/// </para>
/// <para>
/// La caché que sí es peligrosa es la que sobrevive entre peticiones, que es la que Zanzibar
/// necesita de verdad para dar p95 &lt; 10 ms y la que le obliga a inventar los <i>zookies</i>
/// para poder decir "dame una respuesta al menos tan reciente como este momento". Ver
/// <c>docs/06</c>. Aquí no la implementamos a propósito: preferimos que cada Check sea
/// observable y reproducible antes que rápido.
/// </para>
/// </remarks>
internal sealed class EvaluationContext(
    IRelationshipTupleStore tuples,
    AuthorizationModel model,
    CheckOptions options)
{
    private readonly Dictionary<string, bool> _memo = new(StringComparer.Ordinal);
    private readonly HashSet<string> _visiting = new(StringComparer.Ordinal);
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public AuthorizationModel Model { get; } = model;

    public CheckOptions Options { get; } = options;

    public int StoreQueries { get; private set; }

    public int TuplesRead { get; private set; }

    public int NodesEvaluated { get; private set; }

    public int MaxDepthReached { get; private set; }

    public int MemoizationHits { get; private set; }

    public int CyclesDetected { get; private set; }

    /// <summary>
    /// Lee tuplas contabilizando el coste. Todo acceso al almacén pasa por aquí para que las
    /// métricas no se puedan quedar desfasadas por descuido.
    /// </summary>
    public async Task<IReadOnlyList<RelationshipTuple>> ReadAsync(
        TupleFilter filter,
        CancellationToken cancellationToken)
    {
        StoreQueries++;
        var result = await tuples.ReadAsync(filter, cancellationToken);
        TuplesRead += result.Count;
        return result;
    }

    public void CountNode(int depth)
    {
        NodesEvaluated++;
        MaxDepthReached = Math.Max(MaxDepthReached, depth);
    }

    // ── Detección de ciclos ─────────────────────────────────────────────────────

    /// <summary>
    /// Marca un nodo como "en visita". Devuelve <c>false</c> si ya lo estaba, lo que
    /// significa que hemos vuelto al mismo punto y por tanto hay un ciclo.
    /// </summary>
    /// <remarks>
    /// Los ciclos no son un caso rebuscado: aparecen en cuanto el modelo admite grupos
    /// dentro de grupos y alguien mete el grupo A en el B y el B en el A. Un motor sin esta
    /// comprobación se cuelga (o revienta la pila), y como el Check está en el camino
    /// crítico de cada petición, eso es una caída total del sistema provocada por una
    /// operación de administración perfectamente normal.
    /// </remarks>
    public bool TryEnter(string key) => _visiting.Add(key);

    public void Leave(string key) => _visiting.Remove(key);

    public void CountCycle() => CyclesDetected++;

    // ── Memoización ─────────────────────────────────────────────────────────────

    public bool TryGetMemoized(string key, out bool result)
    {
        result = false;

        if (!Options.EnableMemoization)
            return false;

        if (!_memo.TryGetValue(key, out result))
            return false;

        MemoizationHits++;
        return true;
    }

    public void Memoize(string key, bool result)
    {
        if (!Options.EnableMemoization)
            return;

        _memo[key] = result;
    }

    /// <summary>
    /// Clave de un subproblema: sujeto + relación + objeto. Es lo que identifica de forma
    /// única "¿tiene este sujeto esta relación sobre este objeto?".
    /// </summary>
    public static string BuildKey(SubjectRef subject, string relation, ObjectRef @object) =>
        $"{subject}|{relation}|{@object}";

    public CheckMetrics BuildMetrics(string? evaluationMode = null) => new()
    {
        StoreQueries = StoreQueries,
        TuplesRead = TuplesRead,
        NodesEvaluated = NodesEvaluated,
        MaxDepthReached = MaxDepthReached,
        MemoizationHits = MemoizationHits,
        CyclesDetected = CyclesDetected,
        DurationMs = _stopwatch.Elapsed.TotalMilliseconds,
        EvaluationMode = evaluationMode,
    };
}
