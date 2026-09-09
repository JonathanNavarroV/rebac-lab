using System.Diagnostics;
using Playground.AccessControl.Domain.Abstractions;
using Playground.AccessControl.Domain.Checking;
using Playground.AccessControl.Domain.Model;

namespace Playground.AccessControl.Application.Authorization.Engine;

/// <summary>
/// Listado de objetos autorizados por <b>expansión inversa</b>: se parte del sujeto y se
/// recorre el índice inverso hasta los objetos.
/// </summary>
/// <remarks>
/// <para>
/// Esta es la implementación "de verdad", la que hace que un listado sea viable con millones
/// de objetos, y también la que tiene toda la complejidad del problema. Va en tres fases y
/// merece la pena entender por qué son tres.
/// </para>
///
/// <para><b>Fase 1 — ¿a qué conjuntos pertenece el sujeto?</b></para>
/// <para>
/// Antes de mirar ningún objeto hay que saber quién es Juan <i>a efectos de autorización</i>:
/// no solo <c>user:juan</c>, sino también <c>team:backend#member</c>,
/// <c>organization:acme#member</c>, <c>group:seguridad#member</c>... y los conjuntos a los
/// que pertenecen <i>esos</i> conjuntos, transitivamente. Es un cierre transitivo de
/// pertenencia, y en Google es tan caro que le dedicaron un sistema entero: el índice
/// <b>Leopard</b> del paper existe exactamente para esto.
/// </para>
/// <para>
/// El modelo ayuda a acotarlo: solo hacen falta las relaciones que aparecen como userset en
/// alguna asignación (típicamente <c>member</c>), no todas.
/// </para>
///
/// <para><b>Fase 2 — propagación monótona hasta punto fijo</b></para>
/// <para>
/// Con los alias en mano, una consulta al índice inverso da los objetos donde el sujeto
/// tiene una relación <i>directa</i>. Pero eso es solo la semilla: falta propagar hacia
/// abajo por la jerarquía (si puede ver la carpeta raíz, puede ver todo lo de dentro) y hacia
/// los permisos derivados (si es <c>editor</c>, entonces <c>can_edit</c>, y por tanto
/// <c>can_view</c>).
/// </para>
/// <para>
/// Se hace con una lista de trabajo, no con recursión, y esa decisión no es de estilo: el
/// modelo tiene reglas recursivas (<c>folder.parent</c> admite <c>folder</c>), así que una
/// recursión directa no terminaría nunca. Lo que se necesita es un <b>punto fijo</b>: ir
/// añadiendo objetos hasta que una pasada completa no añada ninguno nuevo.
/// </para>
///
/// <para><b>Fase 3 — confirmación de lo que no se puede invertir</b></para>
/// <para>
/// Y aquí está la parte que casi nunca se cuenta. La propagación de la fase 2 solo funciona
/// con reglas <b>monótonas</b>: añadir tuplas solo puede añadir accesos. La unión, la
/// relación computada y la herencia lo son. La <b>exclusión</b> (<c>but not blocked</c>) y la
/// <b>intersección</b> (<c>can_edit and member from parent</c>) no: con ellas, añadir una
/// tupla puede <i>quitar</i> acceso, y no hay forma de saber a quién hay que restar sin
/// mirar objeto por objeto.
/// </para>
/// <para>
/// La solución honesta, y la que usan las implementaciones reales, es tratar la fase 2 como
/// generadora de un <b>superconjunto de candidatos</b> y confirmar cada candidato con un
/// <c>Check</c> real. Sigue siendo muchísimo mejor que la estrategia naive, porque los
/// candidatos son "lo que el sujeto podría ver" y no "todo el catálogo" — pero deja de ser
/// gratis, y explica por qué en un modelo con exclusiones el listado se encarece.
/// </para>
/// </remarks>
internal sealed class ListObjectsReverseStrategy(IRelationshipTupleStore tuples)
{
    public async Task<ListObjectsResult> ExecuteAsync(
        SubjectRef subject,
        string relation,
        string objectType,
        AuthorizationModel model,
        CheckOptions options,
        Func<ObjectRef, CancellationToken, Task<CheckDecision>> confirm,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var storeQueries = 0;
        var tuplesRead = 0;

        async Task<IReadOnlyList<RelationshipTuple>> ReadReverse(
            IReadOnlyCollection<SubjectRef> subjects,
            IReadOnlyCollection<string> relations,
            string? type)
        {
            storeQueries++;
            var result = await tuples.ReadReverseAsync(subjects, relations, type, cancellationToken);
            tuplesRead += result.Count;
            return result;
        }

        // ── Fase 1 ──────────────────────────────────────────────────────────────
        var aliases = await ComputeAliasesAsync(subject, model, options.MaxDepth, ReadReverse);

        // ── Fase 2 ──────────────────────────────────────────────────────────────
        var plan = RelationDependencyPlan.Build(model, objectType, relation);

        var sets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var worklist = new Queue<(string Type, string Relation, string ObjectId)>();

        HashSet<string> SetFor(string type, string relationName)
        {
            var key = $"{type}#{relationName}";

            if (!sets.TryGetValue(key, out var set))
                sets[key] = set = new HashSet<string>(StringComparer.Ordinal);

            return set;
        }

        void Add(string type, string relationName, string objectId)
        {
            if (SetFor(type, relationName).Add(objectId))
                worklist.Enqueue((type, relationName, objectId));
        }

        // Semillas: una consulta al índice inverso por cada relación que admite tuplas
        // directas. Esto es lo que sustituye a recorrer el catálogo entero.
        foreach (var (pairKey, assignments) in plan.DirectAssignmentsByPair)
        {
            var acceptable = aliases
                .Where(alias => assignments.Any(assignment => assignment.Accepts(alias)))
                .ToList();

            if (acceptable.Count == 0)
                continue;

            var (pairType, pairRelation) = RelationDependencyPlan.SplitPair(pairKey);
            var hits = await ReadReverse(acceptable, [pairRelation], pairType);

            hits.ToList().ForEach(tuple => Add(pairType, pairRelation, tuple.Object.Id));
        }

        // Punto fijo. Cada elemento sacado de la lista puede habilitar dos cosas: una
        // relación derivada sobre el MISMO objeto, o el mismo permiso sobre sus HIJOS.
        while (worklist.Count > 0)
        {
            var (currentType, currentRelation, currentObjectId) = worklist.Dequeue();

            // 2a. Relaciones computadas: 'can_view: ... or can_edit' significa que todo
            //     objeto con can_edit tiene también can_view. Sin tocar el almacén.
            foreach (var consumer in plan.GetComputedConsumers(currentType, currentRelation))
                Add(currentType, consumer, currentObjectId);

            // 2b. Herencia: si el sujeto tiene la relación en este objeto, la tiene en todos
            //     los que lo declaran como padre. Aquí sí hay consulta, y es la clave del
            //     rendimiento: se pregunta "¿quién tiene como padre a ESTE objeto?", no se
            //     recorre nada más.
            foreach (var (childType, childRelation, tupleset) in plan.GetInheritanceConsumers(currentType, currentRelation))
            {
                var parentAsSubject = new SubjectRef(currentType, currentObjectId, null);
                var children = await ReadReverse([parentAsSubject], [tupleset], childType);

                children.ToList().ForEach(tuple => Add(childType, childRelation, tuple.Object.Id));
            }
        }

        var candidates = SetFor(objectType, relation).Order().ToList();

        // ── Fase 3 ──────────────────────────────────────────────────────────────
        var authorized = new List<AuthorizedObject>();
        var confirmationChecks = 0;

        if (plan.RequiresConfirmation)
        {
            foreach (var candidateId in candidates)
            {
                var @object = new ObjectRef(objectType, candidateId);
                var decision = await confirm(@object, cancellationToken);
                confirmationChecks++;

                storeQueries += decision.Metrics.StoreQueries;
                tuplesRead += decision.Metrics.TuplesRead;

                if (decision.Allowed)
                    authorized.Add(new AuthorizedObject(@object, decision.Reason, decision.Paths.MinBy(path => path.Length)));
            }
        }
        else
        {
            authorized.AddRange(candidates.Select(candidateId => new AuthorizedObject(
                new ObjectRef(objectType, candidateId),
                "Encontrado por expansión inversa desde el sujeto. El modelo no tiene reglas no monótonas "
                + "para esta relación, así que la pertenencia al conjunto es concluyente sin más comprobaciones.")));
        }

        return new ListObjectsResult
        {
            Subject = subject,
            Relation = relation,
            ObjectType = objectType,
            Strategy = ListObjectsStrategy.ReverseExpansion,
            Objects = authorized,

            // Deliberadamente vacío: la expansión inversa nunca mira los objetos a los que
            // el sujeto no tiene acceso, así que no puede decir por qué no los ve. Para eso
            // hace falta un Check concreto.
            Denied = [],

            Metrics = new CheckMetrics
            {
                StoreQueries = storeQueries,
                TuplesRead = tuplesRead,
                NodesEvaluated = sets.Values.Sum(set => set.Count),
                MaxDepthReached = 0,
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                ConfirmationChecks = confirmationChecks,
            },
        };
    }

    /// <summary>
    /// Fase 1: cierre transitivo de los conjuntos a los que pertenece el sujeto.
    /// </summary>
    /// <remarks>
    /// Se acota con dos ideas. La primera, que solo importan las relaciones que el modelo usa
    /// como userset: si nadie escribe nunca <c>algo#viewer</c> como sujeto, no hace falta
    /// buscar de qué es viewer el sujeto para resolver su pertenencia. La segunda, el límite
    /// de profundidad, que aquí protege de cadenas de grupos anidados patológicas.
    /// </remarks>
    private static async Task<List<SubjectRef>> ComputeAliasesAsync(
        SubjectRef subject,
        AuthorizationModel model,
        int maxDepth,
        Func<IReadOnlyCollection<SubjectRef>, IReadOnlyCollection<string>, string?, Task<IReadOnlyList<RelationshipTuple>>> readReverse)
    {
        var usersetPairs = CollectUsersetPairs(model);

        var aliases = new List<SubjectRef> { subject };
        var seen = new HashSet<string>(StringComparer.Ordinal) { subject.ToString() };

        // El comodín es un alias más: una tupla '@user:*' concede a cualquier usuario, así
        // que hay que buscarla igual que se busca al sujeto por su nombre.
        var wildcard = SubjectRef.Wildcard(subject.Type);

        if (seen.Add(wildcard.ToString()))
            aliases.Add(wildcard);

        var usersetRelations = usersetPairs.Select(pair => pair.Relation).Distinct().ToList();

        if (usersetRelations.Count == 0)
            return aliases;

        var frontier = new List<SubjectRef> { subject };

        for (var depth = 0; depth < maxDepth && frontier.Count > 0; depth++)
        {
            var memberships = await readReverse(frontier, usersetRelations, null);
            var next = new List<SubjectRef>();

            foreach (var tuple in memberships)
            {
                // La tupla dice que el sujeto tiene 'tuple.Relation' sobre 'tuple.Object'.
                // Eso lo convierte en miembro del userset 'tuple.Object#tuple.Relation',
                // pero solo es un alias útil si el modelo usa esa combinación como userset.
                if (!usersetPairs.Contains((tuple.Object.Type, tuple.Relation)))
                    continue;

                var alias = SubjectRef.Userset(tuple.Object.Type, tuple.Object.Id, tuple.Relation);

                if (!seen.Add(alias.ToString()))
                    continue;

                aliases.Add(alias);
                next.Add(alias);
            }

            frontier = next;
        }

        return aliases;
    }

    /// <summary>
    /// Combinaciones <c>(tipo, relación)</c> que el modelo admite como sujeto de tipo
    /// userset. Es lo que permite no buscar pertenencias que nunca se usarían.
    /// </summary>
    private static HashSet<(string Type, string Relation)> CollectUsersetPairs(AuthorizationModel model)
    {
        var pairs = new HashSet<(string, string)>();

        foreach (var type in model.Types.Values)
        {
            foreach (var relation in type.Relations.Values)
            {
                foreach (var assignment in relation.DirectAssignments.Where(a => a.SubjectRelation is not null))
                    pairs.Add((assignment.SubjectType, assignment.SubjectRelation!));
            }
        }

        return pairs;
    }
}
