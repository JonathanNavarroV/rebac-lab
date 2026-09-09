using Playground.AccessControl.Domain.Checking;
using Playground.AccessControl.Domain.Model;

namespace Playground.AccessControl.Application.Authorization.Engine;

/// <summary>
/// Evaluador de <c>Expand</c>: construye el árbol de quién tiene una relación sobre un
/// objeto, <b>sin resolver los usersets</b>.
/// </summary>
/// <remarks>
/// <para>
/// La diferencia con <see cref="CheckEvaluator"/> es que aquí no hay sujeto: no se pregunta
/// por alguien concreto, se pregunta "quién". Y la consecuencia es que <b>no hay
/// cortocircuito posible</b>: hay que recorrer el árbol completo siempre, porque no existe
/// una primera respuesta que permita parar.
/// </para>
/// <para>
/// Lo que <b>no</b> hace es aplanar los usersets a personas. Si el equipo backend es editor,
/// devuelve el nodo <c>team:backend#member</c> y ahí se detiene. Es deliberado y es lo que
/// hace que la operación sea viable: aplanar obligaría a recorrer el cierre transitivo de
/// todos los grupos implicados, y el resultado caducaría en el instante en que alguien entra
/// o sale de un equipo. Es la razón por la que los paneles de "compartido con" de cualquier
/// producto real muestran "Equipo Backend" en lugar de las 500 personas que lo componen.
/// </para>
/// </remarks>
internal sealed class ExpandEvaluator
{
    public async Task<ExpandNode> EvaluateAsync(
        EvaluationContext context,
        ObjectRef @object,
        string relation,
        CancellationToken cancellationToken) =>
        await ExpandRelationAsync(context, @object, relation, depth: 0, cancellationToken);

    private async Task<ExpandNode> ExpandRelationAsync(
        EvaluationContext context,
        ObjectRef @object,
        string relation,
        int depth,
        CancellationToken cancellationToken)
    {
        context.CountNode(depth);

        if (depth > context.Options.MaxDepth)
        {
            return new ExpandNode
            {
                Kind = "depth-limit",
                Label = $"profundidad máxima alcanzada ({context.Options.MaxDepth})",
                Object = @object,
                Relation = relation,
            };
        }

        if (!context.Model.TryGetRelation(@object.Type, relation, out var definition))
        {
            return new ExpandNode
            {
                Kind = "undefined",
                Label = $"«{@object.Type}» no tiene la relación «{relation}»",
                Object = @object,
                Relation = relation,
            };
        }

        var key = EvaluationContext.BuildKey(SubjectRef.Wildcard("expand"), relation, @object);

        // Sin sujeto no hay memoización de resultados, pero sí hace falta cortar los ciclos:
        // un grupo que se contiene a sí mismo colgaría la expansión igual que colgaría un
        // Check.
        if (!context.TryEnter(key))
        {
            context.CountCycle();

            return new ExpandNode
            {
                Kind = "cycle",
                Label = $"ciclo detectado en «{relation}» sobre «{@object}»",
                Object = @object,
                Relation = relation,
            };
        }

        try
        {
            var node = await ExpandRewriteAsync(context, definition.Rewrite, @object, relation, depth, cancellationToken);
            return node;
        }
        finally
        {
            context.Leave(key);
        }
    }

    private async Task<ExpandNode> ExpandRewriteAsync(
        EvaluationContext context,
        UsersetRewrite rewrite,
        ObjectRef @object,
        string relation,
        int depth,
        CancellationToken cancellationToken)
    {
        switch (rewrite)
        {
            case UsersetRewrite.This:
            {
                var tuples = await context.ReadAsync(TupleFilter.Forward(@object, relation), cancellationToken);

                var node = new ExpandNode
                {
                    Kind = "_this",
                    Label = $"tuplas directas de «{relation}»",
                    Object = @object,
                    Relation = relation,
                };

                // Cada tupla es una hoja. Los usersets se quedan sin expandir a propósito.
                node.Children.AddRange(tuples.Select(tuple => new ExpandNode
                {
                    Kind = tuple.Subject.IsUserset ? "userset" : tuple.Subject.IsWildcard ? "wildcard" : "user",
                    Label = tuple.Subject.ToString(),
                    Object = @object,
                    Relation = relation,
                    Subject = tuple.Subject,
                    Tuple = tuple.Key,
                }));

                return node;
            }

            case UsersetRewrite.ComputedUserset computed:
            {
                var node = new ExpandNode
                {
                    Kind = "computed_userset",
                    Label = $"quien tenga «{computed.Relation}» sobre la misma entidad",
                    Object = @object,
                    Relation = computed.Relation,
                };

                node.Children.Add(await ExpandRelationAsync(
                    context, @object, computed.Relation, depth + 1, cancellationToken));

                return node;
            }

            case UsersetRewrite.TupleToUserset ttu:
            {
                var node = new ExpandNode
                {
                    Kind = "tuple_to_userset",
                    Label = $"quien tenga «{ttu.ComputedRelation}» en el padre (vía «{ttu.Tupleset}»)",
                    Object = @object,
                    Relation = ttu.ComputedRelation,
                };

                var parents = await context.ReadAsync(
                    TupleFilter.Forward(@object, ttu.Tupleset), cancellationToken);

                foreach (var parent in parents.Where(tuple => !tuple.Subject.IsUserset && !tuple.Subject.IsWildcard))
                {
                    node.Children.Add(await ExpandRelationAsync(
                        context, parent.Subject.AsObject(), ttu.ComputedRelation, depth + 1, cancellationToken));
                }

                return node;
            }

            case UsersetRewrite.Union union:
                return await ExpandCompositeAsync(context, union.Children, "union", "cualquiera de", @object, relation, depth, cancellationToken);

            case UsersetRewrite.Intersection intersection:
                return await ExpandCompositeAsync(context, intersection.Children, "intersection", "a la vez", @object, relation, depth, cancellationToken);

            case UsersetRewrite.Exclusion exclusion:
            {
                var node = new ExpandNode
                {
                    Kind = "exclusion",
                    Label = "concedido salvo la rama excluida",
                    Object = @object,
                    Relation = relation,
                };

                node.Children.Add(await ExpandRewriteAsync(context, exclusion.Base, @object, relation, depth + 1, cancellationToken));

                var subtract = await ExpandRewriteAsync(context, exclusion.Subtract, @object, relation, depth + 1, cancellationToken);
                node.Children.Add(subtract with { Label = $"⛔ excluidos: {subtract.Label}" });

                return node;
            }

            default:
                throw new NotSupportedException($"Regla de reescritura no soportada: {rewrite.GetType().Name}.");
        }
    }

    private async Task<ExpandNode> ExpandCompositeAsync(
        EvaluationContext context,
        IReadOnlyList<UsersetRewrite> children,
        string kind,
        string labelPrefix,
        ObjectRef @object,
        string relation,
        int depth,
        CancellationToken cancellationToken)
    {
        var node = new ExpandNode
        {
            Kind = kind,
            Label = $"{labelPrefix} {children.Count} vías",
            Object = @object,
            Relation = relation,
        };

        foreach (var child in children)
        {
            node.Children.Add(await ExpandRewriteAsync(
                context, child, @object, relation, depth + 1, cancellationToken));
        }

        return node;
    }

    /// <summary>
    /// Recolecta los sujetos hoja del árbol, sin duplicados. Es la lista que se mostraría en
    /// un panel de "quién tiene acceso".
    /// </summary>
    public static IReadOnlyList<SubjectRef> CollectLeaves(ExpandNode root)
    {
        var leaves = new List<SubjectRef>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        Walk(root, excluded: false);

        return leaves;

        void Walk(ExpandNode node, bool excluded)
        {
            if (node.Subject is not null && !excluded && seen.Add(node.Subject.ToString()))
                leaves.Add(node.Subject);

            // En una exclusión, el segundo hijo son los sujetos que quedan FUERA: incluirlos
            // en la lista de "quién tiene acceso" sería exactamente lo contrario de la verdad.
            for (var index = 0; index < node.Children.Count; index++)
            {
                var childExcluded = excluded || (node.Kind == "exclusion" && index == 1);
                Walk(node.Children[index], childExcluded);
            }
        }
    }
}
