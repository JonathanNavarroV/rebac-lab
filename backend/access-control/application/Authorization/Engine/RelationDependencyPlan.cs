using Playground.AccessControl.Domain.Model;

namespace Playground.AccessControl.Application.Authorization.Engine;

/// <summary>
/// El modelo de autorización, leído <b>al revés</b>: en lugar de "cómo se concede esta
/// relación", responde "qué relaciones se habilitan cuando esta se cumple".
/// </summary>
/// <remarks>
/// <para>
/// Es la pieza que hace posible la expansión inversa, y expresa una asimetría bonita del
/// problema. <see cref="CheckEvaluator"/> lee el modelo hacia delante: "para conceder
/// <c>can_view</c> mira <c>viewer</c>, <c>can_edit</c> y el padre". Aquí se necesita lo
/// contrario: "si acabo de descubrir que el sujeto tiene <c>editor</c> sobre este objeto,
/// ¿qué más se deduce?".
/// </para>
/// <para>
/// El mismo modelo, invertido. Y calcularlo una vez por consulta permite que la propagación
/// de la fase 2 no vuelva a mirar el modelo: solo consulta estos índices.
/// </para>
/// </remarks>
internal sealed class RelationDependencyPlan
{
    private readonly Dictionary<string, List<string>> _computedConsumers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(string ChildType, string ChildRelation, string Tupleset)>> _inheritanceConsumers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<UsersetRewrite.DirectAssignment>> _directAssignments = new(StringComparer.Ordinal);

    /// <summary>
    /// Relaciones que admiten tuplas directas, por pareja <c>tipo#relación</c>. Son las
    /// semillas: los puntos donde la expansión inversa entra en el índice.
    /// </summary>
    public IReadOnlyDictionary<string, List<UsersetRewrite.DirectAssignment>> DirectAssignmentsByPair => _directAssignments;

    /// <summary>
    /// <c>true</c> si en algún punto del recorrido apareció una intersección o una exclusión.
    /// </summary>
    /// <remarks>
    /// Cuando es <c>true</c>, lo que produce la propagación es un <b>superconjunto</b> y hay
    /// que confirmar cada candidato con un Check real. Es el precio de las reglas no
    /// monótonas, y conviene saber que se paga: un modelo con un <c>but not</c> en el camino
    /// convierte un listado barato en un listado con un Check por candidato.
    /// </remarks>
    public bool RequiresConfirmation { get; private set; }

    public static RelationDependencyPlan Build(AuthorizationModel model, string objectType, string relation)
    {
        var plan = new RelationDependencyPlan();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        plan.Walk(model, objectType, relation, visited);

        return plan;
    }

    public IReadOnlyList<string> GetComputedConsumers(string type, string relation) =>
        _computedConsumers.TryGetValue(BuildPair(type, relation), out var consumers) ? consumers : [];

    public IReadOnlyList<(string ChildType, string ChildRelation, string Tupleset)> GetInheritanceConsumers(
        string parentType,
        string relation) =>
        _inheritanceConsumers.TryGetValue(BuildPair(parentType, relation), out var consumers) ? consumers : [];

    public static string BuildPair(string type, string relation) => $"{type}#{relation}";

    public static (string Type, string Relation) SplitPair(string pair)
    {
        var index = pair.IndexOf('#');
        return (pair[..index], pair[(index + 1)..]);
    }

    private void Walk(AuthorizationModel model, string type, string relation, HashSet<string> visited)
    {
        var pair = BuildPair(type, relation);

        // Corta la recursión del modelo. 'folder.can_view' depende de 'folder.can_view' del
        // padre: registrar la dependencia una vez es suficiente, porque la propagación en
        // tiempo de ejecución ya la aplicará tantas veces como niveles haya.
        if (!visited.Add(pair))
            return;

        if (!model.TryGetRelation(type, relation, out var definition))
            return;

        WalkRewrite(model, type, relation, definition.Rewrite, visited);
    }

    private void WalkRewrite(
        AuthorizationModel model,
        string type,
        string relation,
        UsersetRewrite rewrite,
        HashSet<string> visited)
    {
        switch (rewrite)
        {
            case UsersetRewrite.This direct:
            {
                var pair = BuildPair(type, relation);

                if (!_directAssignments.TryGetValue(pair, out var assignments))
                    _directAssignments[pair] = assignments = [];

                assignments.AddRange(direct.Allowed);
                break;
            }

            case UsersetRewrite.ComputedUserset computed:
            {
                // "si se cumple computed.Relation, se habilita relation" (mismo objeto).
                AddComputedConsumer(type, computed.Relation, relation);
                Walk(model, type, computed.Relation, visited);
                break;
            }

            case UsersetRewrite.TupleToUserset ttu:
            {
                if (!model.TryGetRelation(type, ttu.Tupleset, out var tuplesetDefinition))
                    break;

                var parentTypes = tuplesetDefinition.DirectAssignments
                    .Where(assignment => !assignment.Wildcard && assignment.SubjectRelation is null)
                    .Select(assignment => assignment.SubjectType)
                    .Distinct();

                foreach (var parentType in parentTypes)
                {
                    // "si se cumple ttu.ComputedRelation en un objeto de parentType, se
                    // habilita relation en los objetos de type que lo tengan como padre".
                    AddInheritanceConsumer(parentType, ttu.ComputedRelation, type, relation, ttu.Tupleset);
                    Walk(model, parentType, ttu.ComputedRelation, visited);
                }

                break;
            }

            case UsersetRewrite.Union union:
                union.Children.ToList().ForEach(child => WalkRewrite(model, type, relation, child, visited));
                break;

            case UsersetRewrite.Intersection intersection:
                // No monótona: la propagación generará candidatos de más (trata la
                // intersección como si fuera unión) y habrá que confirmar con un Check.
                RequiresConfirmation = true;
                intersection.Children.ToList().ForEach(child => WalkRewrite(model, type, relation, child, visited));
                break;

            case UsersetRewrite.Exclusion exclusion:
                // Tampoco monótona, y por el motivo más incómodo: hay que RESTAR, y no se
                // puede saber a quién sin comprobarlo objeto a objeto. Se recorre solo la
                // base para generar candidatos.
                RequiresConfirmation = true;
                WalkRewrite(model, type, relation, exclusion.Base, visited);
                break;
        }
    }

    private void AddComputedConsumer(string type, string producedRelation, string consumerRelation)
    {
        var pair = BuildPair(type, producedRelation);

        if (!_computedConsumers.TryGetValue(pair, out var consumers))
            _computedConsumers[pair] = consumers = [];

        if (!consumers.Contains(consumerRelation))
            consumers.Add(consumerRelation);
    }

    private void AddInheritanceConsumer(
        string parentType,
        string producedRelation,
        string childType,
        string childRelation,
        string tupleset)
    {
        var pair = BuildPair(parentType, producedRelation);

        if (!_inheritanceConsumers.TryGetValue(pair, out var consumers))
            _inheritanceConsumers[pair] = consumers = [];

        var entry = (childType, childRelation, tupleset);

        if (!consumers.Contains(entry))
            consumers.Add(entry);
    }
}
