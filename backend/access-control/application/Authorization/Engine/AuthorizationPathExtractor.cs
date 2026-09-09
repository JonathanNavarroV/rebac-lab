using Playground.AccessControl.Domain.Checking;

namespace Playground.AccessControl.Application.Authorization.Engine;

/// <summary>
/// Extrae los caminos que conceden acceso a partir de la traza de evaluación.
/// </summary>
/// <remarks>
/// <para>
/// Va en una clase aparte del evaluador por una razón práctica: llevar el camino "a cuestas"
/// durante la recursión obligaría a que cada rama fuera arrastrando y clonando listas, y
/// habría enturbiado el fichero que más importa que se lea bien. Como la traza ya contiene
/// todo lo necesario, los caminos se pueden reconstruir después, en un recorrido trivial.
/// </para>
/// <para>
/// El recorrido solo baja por nodos que concedieron acceso, así que lo que sale son
/// exactamente las cadenas de tuplas que llevan del objeto al sujeto. Se emiten en orden de
/// evaluación (objeto → sujeto) y se le da la vuelta al pintarlas, porque leídas del sujeto
/// hacia el objeto son mucho más naturales: "Juan es miembro de Backend, que es editor de
/// Alpha, que es el padre de Resource A".
/// </para>
/// </remarks>
internal static class AuthorizationPathExtractor
{
    public static IReadOnlyList<AuthorizationPath> Extract(CheckNode root, int maxPaths)
    {
        if (!root.Allowed)
            return [];

        return Walk(root)
            .Take(maxPaths)
            .Select(steps => new AuthorizationPath(steps))
            .ToList();
    }

    private static IEnumerable<List<PathStep>> Walk(CheckNode node)
    {
        if (!node.Allowed)
            return [];

        return node.Kind switch
        {
            // Nodos que solo envuelven: se atraviesan sin aportar un salto.
            "relation" or "_this" or "union" => node.Children.Where(child => child.Allowed).SelectMany(Walk),

            // La exclusión no concedió nada por sí misma; el acceso viene de su base.
            "exclusion" => node.Children.Take(1).SelectMany(Walk),

            // La intersección se cumple por varias condiciones a la vez, así que su "camino"
            // es la concatenación de un camino de cada condición. No es un camino en sentido
            // estricto — es la razón por la que la UI lo etiqueta como acceso condicionado.
            "intersection" => CombineIntersection(node),

            "computed_userset" => node.Children
                .Where(child => child.Allowed)
                .SelectMany(Walk)
                .Select(path => Prepend(
                    new PathStep(
                        PathStepKind.ComputedRelation,
                        $"el modelo deriva este permiso de «{node.Relation}» sobre la misma entidad"),
                    path)),

            "tuple_to_userset" => node.Children.Where(child => child.Allowed).SelectMany(Walk),

            "parent" => node.Children
                .Where(child => child.Allowed)
                .SelectMany(Walk)
                .Select(path => Prepend(
                    new PathStep(
                        PathStepKind.ParentTraversal,
                        $"«{node.Tuple?.Object}» hereda de «{node.Object}»",
                        node.Tuple),
                    path)),

            "tuple" => WalkTuple(node),

            _ => [],
        };
    }

    private static IEnumerable<List<PathStep>> WalkTuple(CheckNode node)
    {
        // Hoja: la tupla nombra al sujeto (o es un comodín). Fin del camino.
        if (node.Children.Count == 0)
        {
            var kind = node.Tuple?.Subject.IsWildcard == true
                ? PathStepKind.Wildcard
                : PathStepKind.DirectTuple;

            var description = kind == PathStepKind.Wildcard
                ? $"«{node.Tuple?.Object}» concede «{node.Relation}» a cualquier «{node.Tuple?.Subject.Type}»"
                : $"tupla directa: {node.Tuple}";

            return [[new PathStep(kind, description, node.Tuple)]];
        }

        // La tupla apunta a un userset: el salto es "pertenecer a ese conjunto", y el resto
        // del camino explica por qué el sujeto pertenece.
        return node.Children
            .Where(child => child.Allowed)
            .SelectMany(Walk)
            .Select(path => Prepend(
                new PathStep(
                    PathStepKind.UsersetTuple,
                    $"«{node.Tuple?.Subject}» tiene «{node.Relation}» sobre «{node.Tuple?.Object}»",
                    node.Tuple),
                path));
    }

    private static IEnumerable<List<PathStep>> CombineIntersection(CheckNode node)
    {
        var combined = new List<PathStep>();

        foreach (var child in node.Children.Where(child => child.Allowed))
        {
            var first = Walk(child).FirstOrDefault();

            if (first is not null)
                combined.AddRange(first);
        }

        return combined.Count == 0 ? [] : [combined];
    }

    private static List<PathStep> Prepend(PathStep step, List<PathStep> path) => [step, .. path];
}
