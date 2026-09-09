using Playground.AccessControl.Domain.Checking;
using Playground.AccessControl.Domain.Model;

namespace Playground.AccessControl.Application.Authorization.Engine;

/// <summary>
/// El evaluador de <c>Check</c>: recorre el árbol de reescrituras del modelo en profundidad
/// y construye la traza mientras lo hace.
/// </summary>
/// <remarks>
/// <para>
/// <b>Este es el fichero central del proyecto.</b> Son unas doscientas líneas y contienen,
/// literalmente, el algoritmo de autorización de Zanzibar. Si solo vas a leer un fichero,
/// que sea este.
/// </para>
/// <para><b>Idea general.</b> La pregunta que se responde es siempre la misma:</para>
/// <code>
/// ¿Pertenece SUJETO al conjunto de quienes tienen RELACIÓN sobre OBJETO?
/// </code>
/// <para>
/// Y se responde de arriba abajo: se mira qué dice el modelo sobre esa relación, y según la
/// regla que encuentre, el problema se transforma en <i>otros problemas de la misma forma</i>
/// hasta llegar a tuplas concretas. No hay ningún caso especial para equipos, para grupos ni
/// para jerarquías: los tres salen de aplicar las mismas cinco reglas.
/// </para>
/// <para><b>Cómo se transforma el problema en cada regla:</b></para>
/// <list type="table">
///   <item><term><c>_this</c></term><description>
///     Único caso base. Lee las tuplas de <c>OBJETO#RELACIÓN</c>. Si alguna apunta al sujeto,
///     se acabó. Si alguna apunta a un userset, el problema pasa a ser "¿pertenece el sujeto
///     a <i>ese</i> userset?", que es otra pregunta de la misma forma.
///   </description></item>
///   <item><term><c>computed_userset</c></term><description>
///     Cambia la RELACIÓN, mismo objeto.
///   </description></item>
///   <item><term><c>tuple_to_userset</c></term><description>
///     Cambia el OBJETO (sube al padre) y la RELACIÓN. Aquí está la herencia.
///   </description></item>
///   <item><term><c>union</c> / <c>intersection</c> / <c>exclusion</c></term><description>
///     Combinan resultados de subproblemas.
///   </description></item>
/// </list>
/// <para>
/// <b>Dirección del recorrido.</b> Se va del objeto hacia el sujeto, no al contrario. Esto
/// no es arbitrario: partiendo del objeto, cada paso está acotado por las tuplas de <i>ese</i>
/// objeto, que son pocas. Si se partiera del sujeto habría que explorar todo lo que el sujeto
/// toca, que puede ser medio sistema. Es exactamente la razón por la que <c>Check</c> es
/// rápido y <c>ListObjects</c> (que sí va del sujeto hacia los objetos) es el problema
/// difícil.
/// </para>
/// </remarks>
internal sealed class CheckEvaluator
{
    /// <summary>
    /// Tope de caminos recolectados en modo <see cref="CheckOptions.FindAllPaths"/>. Con
    /// grupos anidados y varias vías de acceso el número de caminos crece de forma
    /// combinatoria, y una traza de 4.000 caminos no explica nada mejor que una de 20.
    /// </summary>
    public const int MaxCollectedPaths = 20;

    /// <summary>
    /// Evalúa la pregunta y devuelve la raíz de la traza. El resultado está en
    /// <see cref="CheckNode.Allowed"/>.
    /// </summary>
    public async Task<CheckNode> EvaluateAsync(
        EvaluationContext context,
        SubjectRef subject,
        string relation,
        ObjectRef @object,
        CancellationToken cancellationToken) =>
        await EvaluateRelationAsync(context, subject, relation, @object, depth: 0, cancellationToken);

    /// <summary>
    /// Resuelve "¿tiene <paramref name="subject"/> la relación <paramref name="relation"/>
    /// sobre <paramref name="object"/>?". Es el punto de entrada de la recursión y donde
    /// viven las tres salvaguardas: profundidad, ciclos y memoización.
    /// </summary>
    private async Task<CheckNode> EvaluateRelationAsync(
        EvaluationContext context,
        SubjectRef subject,
        string relation,
        ObjectRef @object,
        int depth,
        CancellationToken cancellationToken)
    {
        context.CountNode(depth);

        var node = new CheckNode
        {
            Kind = "relation",
            Label = $"{relation}  {@object}",
            Object = @object,
            Relation = relation,
            Subject = subject,
            Depth = depth,
        };

        // ── Salvaguarda 1: profundidad ──────────────────────────────────────────
        if (depth > context.Options.MaxDepth)
        {
            node.Allowed = false;
            node.Detail = $"profundidad máxima alcanzada ({context.Options.MaxDepth})";
            return node;
        }

        // ── El modelo manda: si la relación no existe, no hay nada que evaluar ──
        if (!context.Model.TryGetRelation(@object.Type, relation, out var definition))
        {
            node.Allowed = false;
            node.Detail = context.Model.TryGetType(@object.Type, out _)
                ? $"el tipo '{@object.Type}' no tiene la relación '{relation}'"
                : $"el tipo '{@object.Type}' no existe en el modelo";

            // Esto es habitual y correcto durante una evaluación normal, no un error: en
            // 'can_view from parent', el padre puede ser un project o un folder, y solo uno
            // de los dos tipos tendrá según el caso la relación que se pregunta. La rama
            // muere aquí y ya está.
            return node;
        }

        var key = EvaluationContext.BuildKey(subject, relation, @object);

        // ── Salvaguarda 2: memoización ──────────────────────────────────────────
        if (context.TryGetMemoized(key, out var memoized))
        {
            node.Allowed = memoized;
            node.FromCache = true;
            node.Detail = "resuelto antes en este mismo Check";
            return node;
        }

        // ── Salvaguarda 3: ciclos ───────────────────────────────────────────────
        if (!context.TryEnter(key))
        {
            context.CountCycle();
            node.Allowed = false;
            node.Detail = "ciclo detectado: ya estábamos evaluando esta misma pregunta";

            // No se memoiza: este 'false' no es una conclusión sobre el problema, solo
            // significa "por este camino no se llega a ninguna parte nueva". Otra rama
            // podría resolver el mismo subproblema legítimamente.
            return node;
        }

        try
        {
            var child = await EvaluateRewriteAsync(
                context, definition.Rewrite, subject, relation, @object, depth, cancellationToken);

            node.AddChild(child);
            node.Allowed = child.Allowed;

            context.Memoize(key, node.Allowed);
            return node;
        }
        finally
        {
            context.Leave(key);
        }
    }

    /// <summary>Aplica la regla de reescritura que corresponda.</summary>
    private async Task<CheckNode> EvaluateRewriteAsync(
        EvaluationContext context,
        UsersetRewrite rewrite,
        SubjectRef subject,
        string relation,
        ObjectRef @object,
        int depth,
        CancellationToken cancellationToken) => rewrite switch
        {
            UsersetRewrite.This direct =>
                await EvaluateThisAsync(context, direct, subject, relation, @object, depth, cancellationToken),

            UsersetRewrite.ComputedUserset computed =>
                await EvaluateComputedUsersetAsync(context, computed, subject, @object, depth, cancellationToken),

            UsersetRewrite.TupleToUserset ttu =>
                await EvaluateTupleToUsersetAsync(context, ttu, subject, @object, depth, cancellationToken),

            UsersetRewrite.Union union =>
                await EvaluateUnionAsync(context, union, subject, relation, @object, depth, cancellationToken),

            UsersetRewrite.Intersection intersection =>
                await EvaluateIntersectionAsync(context, intersection, subject, relation, @object, depth, cancellationToken),

            UsersetRewrite.Exclusion exclusion =>
                await EvaluateExclusionAsync(context, exclusion, subject, relation, @object, depth, cancellationToken),

            _ => throw new NotSupportedException($"Regla de reescritura no soportada: {rewrite.GetType().Name}."),
        };

    /// <summary>
    /// <c>_this</c>: el caso base. La única regla que consulta el almacén de tuplas.
    /// </summary>
    /// <remarks>
    /// Se lee <b>toda</b> la lista de sujetos de <c>OBJETO#RELACIÓN</c> con una sola consulta
    /// y luego se clasifica en memoria. La alternativa sería una consulta exacta buscando el
    /// sujeto (más barata) más otra para los usersets, pero el ahorro no compensa la pérdida
    /// de claridad y además así queda a la vista en las métricas lo que cuesta una relación
    /// con muchísimos sujetos, que es un problema real: <c>TuplesRead</c> se dispara.
    /// </remarks>
    private async Task<CheckNode> EvaluateThisAsync(
        EvaluationContext context,
        UsersetRewrite.This direct,
        SubjectRef subject,
        string relation,
        ObjectRef @object,
        int depth,
        CancellationToken cancellationToken)
    {
        var node = new CheckNode
        {
            Kind = "_this",
            Label = $"tuplas directas de «{relation}»  {direct.ToDsl()}",
            Object = @object,
            Relation = relation,
            Subject = subject,
            Depth = depth,
        };

        var tuples = await context.ReadAsync(TupleFilter.Forward(@object, relation), cancellationToken);

        if (tuples.Count == 0)
        {
            node.Allowed = false;
            node.Detail = "no hay ninguna tupla escrita";
            return node;
        }

        node.Detail = $"{tuples.Count} tupla(s) que revisar";

        foreach (var tuple in tuples)
        {
            var tupleNode = new CheckNode
            {
                Kind = "tuple",
                Label = tuple.ToString(),
                Object = @object,
                Relation = relation,
                Subject = subject,
                Depth = depth + 1,
                Tuple = tuple.Key,
            };

            node.AddChild(tupleNode);
            context.CountNode(depth + 1);

            // El modelo declara qué sujetos admite esta relación. Una tupla que no encaje
            // con ninguna asignación se ignora: es un dato viejo de cuando el modelo era
            // distinto, y respetarlo sería conceder acceso por una regla que ya no existe.
            if (!direct.Allowed.Any(assignment => assignment.Accepts(tuple.Subject)))
            {
                tupleNode.Allowed = false;
                tupleNode.Detail = $"el modelo ya no admite sujetos como «{tuple.Subject}» en esta relación";
                continue;
            }

            // Caso comodín: 'user:*' concede a cualquiera de ese tipo.
            if (tuple.Subject.IsWildcard)
            {
                tupleNode.Allowed = tuple.Subject.Type == subject.Type;
                tupleNode.Detail = tupleNode.Allowed
                    ? $"comodín: cualquier «{tuple.Subject.Type}» tiene esta relación"
                    : $"comodín para «{tuple.Subject.Type}», y el sujeto es «{subject.Type}»";
            }
            // Caso individuo: comparación directa.
            else if (!tuple.Subject.IsUserset)
            {
                tupleNode.Allowed = tuple.Subject == subject;
                tupleNode.Detail = tupleNode.Allowed
                    ? "tupla directa: el sujeto aparece nombrado"
                    : $"nombra a «{tuple.Subject}», no al sujeto";
            }
            // Caso userset: el problema se transforma y la recursión hace el resto.
            else
            {
                tupleNode.Detail = $"el sujeto es el userset «{tuple.Subject}» → hay que comprobar la pertenencia";

                var membership = await EvaluateRelationAsync(
                    context,
                    subject,
                    tuple.Subject.Relation!,
                    tuple.Subject.AsObject(),
                    depth + 2,
                    cancellationToken);

                tupleNode.AddChild(membership);
                tupleNode.Allowed = membership.Allowed;
            }

            if (tupleNode.Allowed && !context.Options.FindAllPaths)
            {
                // Cortocircuito: con una tupla que conceda ya está respondida la pregunta.
                node.Allowed = true;
                return node;
            }
        }

        node.Allowed = node.Children.Any(child => child.Allowed);
        return node;
    }

    /// <summary>
    /// <c>computed_userset</c>: misma entidad, otra relación.
    /// </summary>
    private async Task<CheckNode> EvaluateComputedUsersetAsync(
        EvaluationContext context,
        UsersetRewrite.ComputedUserset computed,
        SubjectRef subject,
        ObjectRef @object,
        int depth,
        CancellationToken cancellationToken)
    {
        var node = new CheckNode
        {
            Kind = "computed_userset",
            Label = $"«{computed.Relation}» sobre la misma entidad",
            Object = @object,
            Relation = computed.Relation,
            Subject = subject,
            Depth = depth,
        };

        var child = await EvaluateRelationAsync(
            context, subject, computed.Relation, @object, depth + 1, cancellationToken);

        node.AddChild(child);
        node.Allowed = child.Allowed;
        return node;
    }

    /// <summary>
    /// <c>tuple_to_userset</c>: la herencia. Sube por el tupleset y pregunta arriba.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Dos consultas conceptuales en una: primero "¿quién es el padre de este objeto?"
    /// (las tuplas del tupleset), y luego, por cada padre encontrado, la pregunta original
    /// trasladada allí.
    /// </para>
    /// <para>
    /// Un objeto puede tener <b>varios</b> padres. El modelo no lo impide y el motor tampoco:
    /// se prueban todos. Sirve para modelar un recurso que vive en dos carpetas a la vez
    /// (como los "atajos" de Drive) sin ningún tratamiento especial.
    /// </para>
    /// </remarks>
    private async Task<CheckNode> EvaluateTupleToUsersetAsync(
        EvaluationContext context,
        UsersetRewrite.TupleToUserset ttu,
        SubjectRef subject,
        ObjectRef @object,
        int depth,
        CancellationToken cancellationToken)
    {
        var node = new CheckNode
        {
            Kind = "tuple_to_userset",
            Label = $"«{ttu.ComputedRelation}» heredado a través de «{ttu.Tupleset}»",
            Object = @object,
            Relation = ttu.ComputedRelation,
            Subject = subject,
            Depth = depth,
        };

        var parentTuples = await context.ReadAsync(
            TupleFilter.Forward(@object, ttu.Tupleset), cancellationToken);

        if (parentTuples.Count == 0)
        {
            node.Allowed = false;
            node.Detail = $"«{@object}» no tiene ninguna tupla «{ttu.Tupleset}»: no hay de quién heredar";
            return node;
        }

        foreach (var parentTuple in parentTuples)
        {
            // El padre tiene que ser un objeto concreto. Un userset como padre
            // ('project:alpha#parent@team:backend#member') no tiene sentido: la jerarquía
            // relaciona objetos con objetos, no con conjuntos de sujetos.
            if (parentTuple.Subject.IsUserset || parentTuple.Subject.IsWildcard)
            {
                var invalid = new CheckNode
                {
                    Kind = "parent",
                    Label = parentTuple.ToString(),
                    Object = @object,
                    Relation = ttu.Tupleset,
                    Subject = subject,
                    Depth = depth + 1,
                    Tuple = parentTuple.Key,
                    Allowed = false,
                    Detail = $"«{parentTuple.Subject}» no es un objeto concreto y no puede ser padre",
                };

                node.AddChild(invalid);
                continue;
            }

            var parentObject = parentTuple.Subject.AsObject();

            var parentNode = new CheckNode
            {
                Kind = "parent",
                Label = $"padre: {parentObject}",
                Object = parentObject,
                Relation = ttu.Tupleset,
                Subject = subject,
                Depth = depth + 1,
                Tuple = parentTuple.Key,
                Detail = $"se sube por «{ttu.Tupleset}» y se pregunta «{ttu.ComputedRelation}» allí",
            };

            node.AddChild(parentNode);
            context.CountNode(depth + 1);

            var inherited = await EvaluateRelationAsync(
                context, subject, ttu.ComputedRelation, parentObject, depth + 2, cancellationToken);

            parentNode.AddChild(inherited);
            parentNode.Allowed = inherited.Allowed;

            if (parentNode.Allowed && !context.Options.FindAllPaths)
            {
                node.Allowed = true;
                return node;
            }
        }

        node.Allowed = node.Children.Any(child => child.Allowed);
        return node;
    }

    /// <summary>
    /// <c>union</c>: basta una rama. Es de donde salen los múltiples caminos de acceso.
    /// </summary>
    private async Task<CheckNode> EvaluateUnionAsync(
        EvaluationContext context,
        UsersetRewrite.Union union,
        SubjectRef subject,
        string relation,
        ObjectRef @object,
        int depth,
        CancellationToken cancellationToken)
    {
        var node = new CheckNode
        {
            Kind = "union",
            Label = $"«{relation}» se concede por cualquiera de {union.Children.Count} vías",
            Object = @object,
            Relation = relation,
            Subject = subject,
            Depth = depth,
        };

        foreach (var child in union.Children)
        {
            var childNode = await EvaluateRewriteAsync(
                context, child, subject, relation, @object, depth + 1, cancellationToken);

            node.AddChild(childNode);

            if (!childNode.Allowed || context.Options.FindAllPaths)
                continue;

            // Aquí está el cortocircuito que hace rápido el Check de producción, y también la
            // razón por la que ese Check no puede contarte cuántos caminos hay: en cuanto
            // uno concede, los demás no se miran.
            node.Allowed = true;
            node.Detail = "primera vía que concede acceso (el resto no se evalúa)";
            return node;
        }

        node.Allowed = node.Children.Any(child => child.Allowed);

        if (!node.Allowed)
            node.Detail = "ninguna de las vías concede acceso";

        return node;
    }

    /// <summary>
    /// <c>intersection</c>: todas las ramas deben conceder.
    /// </summary>
    private async Task<CheckNode> EvaluateIntersectionAsync(
        EvaluationContext context,
        UsersetRewrite.Intersection intersection,
        SubjectRef subject,
        string relation,
        ObjectRef @object,
        int depth,
        CancellationToken cancellationToken)
    {
        var node = new CheckNode
        {
            Kind = "intersection",
            Label = $"«{relation}» exige cumplir las {intersection.Children.Count} condiciones",
            Object = @object,
            Relation = relation,
            Subject = subject,
            Depth = depth,
        };

        foreach (var child in intersection.Children)
        {
            var childNode = await EvaluateRewriteAsync(
                context, child, subject, relation, @object, depth + 1, cancellationToken);

            node.AddChild(childNode);

            if (childNode.Allowed || context.Options.FindAllPaths)
                continue;

            node.Allowed = false;
            node.Detail = "una de las condiciones no se cumple (el resto no se evalúa)";
            return node;
        }

        node.Allowed = node.Children.Count > 0 && node.Children.All(child => child.Allowed);
        return node;
    }

    /// <summary>
    /// <c>exclusion</c>: concede la base salvo que la resta también conceda.
    /// </summary>
    /// <remarks>
    /// La rama sustraída <b>gana siempre</b>. Da igual por cuántos caminos se conceda el
    /// acceso: una sola tupla en la rama de exclusión los anula todos. Es la forma de
    /// modelar un bloqueo explícito, y es también lo que rompe la expansión inversa de
    /// <c>ListObjects</c> (no se puede saber a quién hay que restar sin comprobarlo objeto a
    /// objeto). Ver <c>docs/04</c>.
    /// </remarks>
    private async Task<CheckNode> EvaluateExclusionAsync(
        EvaluationContext context,
        UsersetRewrite.Exclusion exclusion,
        SubjectRef subject,
        string relation,
        ObjectRef @object,
        int depth,
        CancellationToken cancellationToken)
    {
        var node = new CheckNode
        {
            Kind = "exclusion",
            Label = $"«{relation}» se concede salvo exclusión explícita",
            Object = @object,
            Relation = relation,
            Subject = subject,
            Depth = depth,
        };

        var baseNode = await EvaluateRewriteAsync(
            context, exclusion.Base, subject, relation, @object, depth + 1, cancellationToken);

        node.AddChild(baseNode);

        if (!baseNode.Allowed)
        {
            node.Allowed = false;
            node.Detail = "la vía principal ya no concede acceso, la exclusión es irrelevante";
            return node;
        }

        var subtractNode = await EvaluateRewriteAsync(
            context, exclusion.Subtract, subject, relation, @object, depth + 1, cancellationToken);

        // Se marca al revés a propósito: en esta rama, "conceder" significa "excluir". Sin
        // esta inversión la traza se leería como si la exclusión hubiera dado acceso.
        subtractNode.Detail = subtractNode.Allowed
            ? "⛔ EXCLUYE: esta rama anula todos los caminos anteriores"
            : "no excluye";

        node.AddChild(subtractNode);

        node.Allowed = !subtractNode.Allowed;
        node.Detail = node.Allowed
            ? "concedido y sin exclusión aplicable"
            : "había acceso, pero una exclusión explícita lo anula";

        return node;
    }
}
