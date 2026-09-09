using System.Text;
using Playground.AccessControl.Domain.Abstractions;
using Playground.AccessControl.Domain.Checking;
using Playground.AccessControl.Domain.Model;

namespace Playground.AccessControl.Application.Authorization.Engine;

/// <summary>
/// Convierte una traza de evaluación en una explicación en lenguaje natural, y en un DENY
/// calcula qué tuplas bastaría crear para que fuera un ALLOW.
/// </summary>
/// <remarks>
/// <para>
/// Esta clase no aporta nada al funcionamiento del control de acceso: aporta todo al
/// aprendizaje. La parte de las <b>tuplas sugeridas</b> es, en mi opinión, lo más útil del
/// laboratorio: convierte cada denegación en una lección sobre el modelo, porque para
/// responder "¿qué me falta?" hay que enumerar todas las vías por las que ese objeto puede
/// conceder ese permiso — y verlas juntas es lo que hace que el modelo se entienda.
/// </para>
/// <para>
/// Cuidado con la tentación de exponer algo así en un sistema real de cara al usuario final:
/// decirle a alguien exactamente qué relación le falta para acceder a un recurso es filtrar
/// la estructura interna de la organización. En un laboratorio es la funcionalidad estrella;
/// en producción, un endpoint solo para administradores.
/// </para>
/// </remarks>
internal sealed class CheckExplainer(IRelationshipTupleStore tuples)
{
    private const int MaxSuggestions = 8;

    /// <summary>Redacta el "por qué" de una decisión ya evaluada.</summary>
    public string BuildReason(
        CheckNode root,
        SubjectRef subject,
        string relation,
        ObjectRef @object,
        IReadOnlyList<AuthorizationPath> paths,
        AuthorizationModel model)
    {
        if (!model.TryGetType(@object.Type, out _))
        {
            return $"El tipo «{@object.Type}» no existe en el modelo de autorización, así que no hay nada que "
                   + $"comprobar. Tipos declarados: {string.Join(", ", model.Types.Keys.Order())}.";
        }

        if (!model.TryGetRelation(@object.Type, relation, out var definition))
        {
            var available = model.Types[@object.Type].Relations.Keys.Order();

            return $"El tipo «{@object.Type}» no tiene ninguna relación llamada «{relation}», por lo que la "
                   + $"respuesta es DENY por definición del modelo, no por falta de tuplas. "
                   + $"Relaciones disponibles: {string.Join(", ", available)}.";
        }

        return root.Allowed
            ? BuildAllowReason(subject, relation, @object, paths, definition)
            : BuildDenyReason(root, subject, relation, @object, definition);
    }

    private static string BuildAllowReason(
        SubjectRef subject,
        string relation,
        ObjectRef @object,
        IReadOnlyList<AuthorizationPath> paths,
        RelationDefinition definition)
    {
        var builder = new StringBuilder();

        builder.Append($"«{subject}» SÍ tiene «{relation}» sobre «{@object}».");

        // El comentario del modelo describe la relación entera, no necesariamente la rama que
        // concedió este acceso concreto, así que se introduce como nota y no como si fuera la
        // explicación de esta decisión.
        if (definition.Comment is not null)
            builder.Append($" [Nota del modelo sobre «{relation}»: {definition.Comment}]");

        if (paths.Count == 0)
        {
            // Ocurre si se pidió la decisión sin traza: la respuesta es válida, pero no
            // podemos justificarla.
            builder.Append(" (No se recolectaron caminos en esta evaluación.)");
            return builder.ToString();
        }

        var shortest = paths.MinBy(path => path.Length)!;

        if (paths.Count == 1)
        {
            builder.Append($" El acceso viene de un único camino de {shortest.Length} salto(s): {shortest.ToChainString()}.");
            return builder.ToString();
        }

        builder.Append($" Hay {paths.Count} caminos distintos que conceden este acceso; el más corto "
                       + $"({shortest.Length} salto(s)) es: {shortest.ToChainString()}. ");
        builder.Append("Que haya varios importa para revocar: quitar uno solo no retira el acceso.");

        return builder.ToString();
    }

    private static string BuildDenyReason(
        CheckNode root,
        SubjectRef subject,
        string relation,
        ObjectRef @object,
        RelationDefinition definition)
    {
        var builder = new StringBuilder();

        builder.Append($"«{subject}» NO tiene «{relation}» sobre «{@object}». ");

        // Una exclusión es un DENY de naturaleza muy distinta a "no hay ninguna relación":
        // aquí sí había acceso y algo lo anuló, y conviene decirlo explícitamente porque el
        // arreglo también es distinto (borrar la exclusión, no añadir una relación).
        var blocking = FindBlockingExclusion(root);

        if (blocking is not null)
        {
            builder.Append($"Ojo: sí existían caminos válidos, pero una exclusión explícita los anula todos "
                           + $"({blocking.Label}). Añadir más relaciones no servirá de nada mientras esa exclusión exista.");
            return builder.ToString();
        }

        var cycle = FindNode(root, node => node.Detail?.StartsWith("ciclo detectado") == true);

        if (cycle is not null)
        {
            builder.Append("Durante la evaluación se detectó un ciclo en las relaciones "
                           + $"(al volver a «{cycle.Relation}» sobre «{cycle.Object}»), así que esa rama se abandonó. "
                           + "Revisa si hay grupos o equipos que se contienen mutuamente.");
        }

        var depthLimited = FindNode(root, node => node.Detail?.StartsWith("profundidad máxima") == true);

        if (depthLimited is not null)
        {
            builder.Append("Se alcanzó la profundidad máxima de evaluación, así que esta respuesta es "
                           + "\"no se pudo determinar\" más que un \"no\" rotundo: la jerarquía es demasiado profunda "
                           + "para el límite configurado.");
        }

        builder.Append($"No se encontró ninguna tupla que lo conceda. El modelo permite concederlo así: "
                       + $"«{definition.Name}: {definition.Rewrite.ToDsl()}».");

        return builder.ToString();
    }

    /// <summary>
    /// Calcula tuplas que convertirían el DENY en ALLOW.
    /// </summary>
    /// <remarks>
    /// Se exploran tres vías, que son las tres formas de conceder acceso en este modelo:
    /// nombrar al sujeto directamente, usar un userset al que el sujeto ya pertenece, o
    /// conceder en el objeto padre para que se herede. Solo se baja un nivel en la jerarquía:
    /// con más niveles la lista de sugerencias crece mucho y deja de ser una pista para
    /// convertirse en ruido.
    /// </remarks>
    public async Task<IReadOnlyList<TupleKey>> SuggestMissingTuplesAsync(
        SubjectRef subject,
        string relation,
        ObjectRef @object,
        AuthorizationModel model,
        CancellationToken cancellationToken)
    {
        if (!model.TryGetRelation(@object.Type, relation, out _))
            return [];

        var suggestions = new List<TupleKey>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        await CollectAsync(@object, relation, allowParentHop: true);

        return suggestions;

        async Task CollectAsync(ObjectRef target, string targetRelation, bool allowParentHop)
        {
            if (suggestions.Count >= MaxSuggestions)
                return;

            var contributing = model.GetContributingRelations(target.Type, targetRelation);

            foreach (var candidateRelation in contributing)
            {
                if (suggestions.Count >= MaxSuggestions)
                    return;

                if (!model.TryGetRelation(target.Type, candidateRelation, out var candidate))
                    continue;

                if (!candidate.IsDirectlyAssignable)
                    continue;

                foreach (var assignment in candidate.DirectAssignments)
                {
                    // Vía 1: nombrar al sujeto directamente.
                    if (assignment.SubjectRelation is null
                        && !assignment.Wildcard
                        && assignment.SubjectType == subject.Type)
                    {
                        Add(new TupleKey(target, candidateRelation, subject));
                        continue;
                    }

                    // Vía 2: un userset al que el sujeto YA pertenece. Es la sugerencia más
                    // interesante, porque enseña que no hace falta tocar al usuario: basta
                    // conceder al grupo del que ya forma parte.
                    if (assignment.SubjectRelation is not null)
                    {
                        var memberships = await tuples.ReadReverseAsync(
                            [subject],
                            [assignment.SubjectRelation],
                            assignment.SubjectType,
                            cancellationToken);

                        foreach (var membership in memberships)
                        {
                            Add(new TupleKey(
                                target,
                                candidateRelation,
                                SubjectRef.Userset(
                                    membership.Object.Type,
                                    membership.Object.Id,
                                    assignment.SubjectRelation)));
                        }
                    }
                }
            }

            if (!allowParentHop)
                return;

            // Vía 3: conceder en el padre para que se herede hacia abajo.
            foreach (var ttu in FindTupleToUsersets(model, target.Type, targetRelation))
            {
                var parentTuples = await tuples.ReadAsync(
                    TupleFilter.Forward(target, ttu.Tupleset), cancellationToken);

                foreach (var parentTuple in parentTuples.Where(tuple =>
                             !tuple.Subject.IsUserset && !tuple.Subject.IsWildcard))
                {
                    await CollectAsync(parentTuple.Subject.AsObject(), ttu.ComputedRelation, allowParentHop: false);
                }
            }
        }

        void Add(TupleKey key)
        {
            if (suggestions.Count >= MaxSuggestions)
                return;

            if (seen.Add(key.ToString()))
                suggestions.Add(key);
        }
    }

    /// <summary>
    /// Todas las reglas <c>tuple_to_userset</c> alcanzables desde una relación, incluidas las
    /// que están detrás de relaciones computadas.
    /// </summary>
    private static IEnumerable<UsersetRewrite.TupleToUserset> FindTupleToUsersets(
        AuthorizationModel model,
        string objectType,
        string relation)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var found = new List<UsersetRewrite.TupleToUserset>();

        Collect(relation);

        return found;

        void Collect(string current)
        {
            if (!visited.Add(current))
                return;

            if (!model.TryGetRelation(objectType, current, out var definition))
                return;

            Walk(definition.Rewrite);
        }

        void Walk(UsersetRewrite rewrite)
        {
            switch (rewrite)
            {
                case UsersetRewrite.TupleToUserset ttu:
                    found.Add(ttu);
                    break;

                case UsersetRewrite.ComputedUserset computed:
                    Collect(computed.Relation);
                    break;

                case UsersetRewrite.Union union:
                    union.Children.ToList().ForEach(Walk);
                    break;

                case UsersetRewrite.Intersection intersection:
                    intersection.Children.ToList().ForEach(Walk);
                    break;

                case UsersetRewrite.Exclusion exclusion:
                    Walk(exclusion.Base);
                    break;
            }
        }
    }

    /// <summary>Busca la rama de exclusión que anuló un acceso que por lo demás era válido.</summary>
    private static CheckNode? FindBlockingExclusion(CheckNode node)
    {
        if (node is { Kind: "exclusion", Allowed: false }
            && node.Children.Count == 2
            && node.Children[0].Allowed
            && node.Children[1].Allowed)
        {
            return node.Children[1];
        }

        return node.Children.Select(FindBlockingExclusion).FirstOrDefault(found => found is not null);
    }

    private static CheckNode? FindNode(CheckNode node, Func<CheckNode, bool> predicate) =>
        predicate(node)
            ? node
            : node.Children.Select(child => FindNode(child, predicate)).FirstOrDefault(found => found is not null);
}
