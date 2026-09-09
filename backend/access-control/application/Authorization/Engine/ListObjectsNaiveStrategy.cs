using System.Diagnostics;
using Playground.AccessControl.Domain.Abstractions;
using Playground.AccessControl.Domain.Checking;
using Playground.AccessControl.Domain.Model;

namespace Playground.AccessControl.Application.Authorization.Engine;

/// <summary>
/// Listado de objetos autorizados por fuerza bruta: enumerar todo y comprobar uno a uno.
/// </summary>
/// <remarks>
/// <para>
/// <b>Esta implementación está aquí para que se vea lo mala que es</b>, y a la vez porque es
/// la única que se puede considerar obviamente correcta. Cumple dos funciones:
/// </para>
/// <list type="number">
///   <item>
///     <b>Oráculo.</b> La suite de conformidad comprueba que la expansión inversa devuelve
///     exactamente el mismo conjunto que esta. Si difieren, el bug está en la lista, no aquí:
///     esto es solo un bucle de Checks, y los Checks están testeados por separado.
///   </item>
///   <item>
///     <b>Baseline medible.</b> Es la única estrategia que sabe qué objetos existen y por
///     tanto la única que puede rellenar <see cref="ListObjectsResult.Denied"/> con el motivo
///     de cada exclusión — que es información valiosísima para aprender, aunque se pague muy
///     cara.
///   </item>
/// </list>
/// <para>
/// El coste es <c>O(N)</c> Checks sobre el <b>tamaño del catálogo</b>. Con 20 proyectos son
/// 20 Checks y no se nota. Con 200.000 documentos son 200.000 Checks, y si además el módulo
/// de control de acceso es remoto, 200.000 viajes de red. Es el camino más corto para tumbar
/// un servicio, y prácticamente todo el mundo lo implementa así la primera vez.
/// </para>
/// </remarks>
internal sealed class ListObjectsNaiveStrategy(IObjectCatalog catalog)
{
    public async Task<ListObjectsResult> ExecuteAsync(
        SubjectRef subject,
        string relation,
        string objectType,
        Func<ObjectRef, CancellationToken, Task<CheckDecision>> check,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var candidates = await catalog.GetObjectIdsAsync(objectType, cancellationToken);

        var allowed = new List<AuthorizedObject>();
        var denied = new List<AuthorizedObject>();

        var storeQueries = 0;
        var tuplesRead = 0;
        var nodes = 0;
        var maxDepth = 0;

        foreach (var candidateId in candidates)
        {
            var @object = new ObjectRef(objectType, candidateId);
            var decision = await check(@object, cancellationToken);

            // Las métricas se acumulan a propósito: es justo el número que hace evidente el
            // problema. Un ListObjects naive de 20 objetos hace ~60 consultas al almacén.
            storeQueries += decision.Metrics.StoreQueries;
            tuplesRead += decision.Metrics.TuplesRead;
            nodes += decision.Metrics.NodesEvaluated;
            maxDepth = Math.Max(maxDepth, decision.Metrics.MaxDepthReached);

            var entry = new AuthorizedObject(
                @object,
                decision.Reason,
                decision.Paths.MinBy(path => path.Length));

            (decision.Allowed ? allowed : denied).Add(entry);
        }

        return new ListObjectsResult
        {
            Subject = subject,
            Relation = relation,
            ObjectType = objectType,
            Strategy = ListObjectsStrategy.Naive,
            Objects = allowed,
            Denied = denied,
            Metrics = new CheckMetrics
            {
                StoreQueries = storeQueries + 1, // +1 por la enumeración del catálogo
                TuplesRead = tuplesRead,
                NodesEvaluated = nodes,
                MaxDepthReached = maxDepth,
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
            },
        };
    }
}
