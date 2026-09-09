using System.Diagnostics.CodeAnalysis;
using Playground.AccessControl.Api.Helpers;
using Playground.AccessControl.Api.Mapping;
using Playground.AccessControl.Domain.Abstractions;
using Playground.AccessControl.Domain.Checking;
using Playground.Contracts.AccessControl;

namespace Playground.AccessControl.Api.Endpoints;

/// <summary>
/// Endpoints de decisión: <c>check</c>, <c>explain</c>, <c>batch-check</c>, <c>expand</c> y
/// <c>list-objects</c>.
/// </summary>
/// <remarks>
/// <para>
/// Estos endpoints van directos al motor, sin pasar por MediatR, y es una decisión consciente
/// que se aparta de la convención de tus otros proyectos. El motivo: en el negocio, un handler
/// aporta orquestación real (validar, cargar, decidir, persistir, publicar eventos). Aquí no
/// hay nada que orquestar — el endpoint parsea tres cadenas y llama al motor — así que un
/// handler por endpoint sería una capa que solo reenvía y que alejaría el código de lo que
/// importa entender.
/// </para>
/// <para>
/// El servicio de negocio sí usa CQRS con MediatR, como el resto de tus módulos.
/// </para>
/// </remarks>
[ExcludeFromCodeCoverage]
public static class CheckEndpoints
{
    public static RouteGroupBuilder MapCheckEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/check", CheckAsync)
            .WithName("Check")
            .WithSummary("¿Puede este sujeto hacer esto sobre este objeto?")
            .WithDescription(
                "La operación central del sistema. Devuelve la decisión y, si se pide 'explain', "
                + "el árbol de evaluación completo con las ramas que fallaron, todos los caminos que "
                + "conceden acceso y —cuando deniega— las tuplas que bastaría crear para que "
                + "concediera.")
            .Produces<CheckResponse>()
            .ProducesValidationProblem();

        group.MapPost("/explain", ExplainAsync)
            .WithName("Explain")
            .WithSummary("Igual que /check pero forzando la explicación completa")
            .WithDescription(
                "Atajo para el Authorization Explorer. Explora TODAS las ramas en lugar de parar en "
                + "el primer ALLOW, así que es más caro y a cambio puede mostrar los múltiples "
                + "caminos. Compara sus métricas con las de /check para ver cuánto cuesta explicar.")
            .Produces<CheckResponse>()
            .ProducesValidationProblem();

        group.MapPost("/batch-check", BatchCheckAsync)
            .WithName("BatchCheck")
            .WithSummary("Varias preguntas en una sola llamada")
            .WithDescription(
                "Existe por la latencia de red, no por la CPU: pintar una lista de 50 elementos con "
                + "sus botones de editar y borrar son 100 checks. En modo InProcess da igual; en "
                + "modo Remote son 100 viajes de ida y vuelta.")
            .Produces<BatchCheckResponse>()
            .ProducesValidationProblem();

        group.MapPost("/expand", ExpandAsync)
            .WithName("Expand")
            .WithSummary("¿Quiénes tienen esta relación sobre este objeto?")
            .WithDescription(
                "Devuelve un árbol de usersets SIN resolver. Si un equipo de 500 personas tiene "
                + "acceso, devuelve el equipo, no las 500 personas: aplanarlo sería carísimo y el "
                + "resultado caducaría al instante.")
            .Produces<ExpandResponse>()
            .ProducesValidationProblem();

        group.MapPost("/list-objects", ListObjectsAsync)
            .WithName("ListObjects")
            .WithSummary("¿Sobre qué objetos de este tipo tiene el sujeto esta relación?")
            .WithDescription(
                "El problema inverso al Check, y el difícil. Admite las dos estrategias: 'naive' "
                + "(enumerar el catálogo y comprobar uno a uno) y 'reverse' (partir del sujeto y "
                + "recorrer el índice inverso).")
            .Produces<ListObjectsResponse>()
            .ProducesValidationProblem();

        group.MapPost("/list-objects/compare", CompareListStrategiesAsync)
            .WithName("CompareListStrategies")
            .WithSummary("Responde el mismo listado con las dos estrategias y compara")
            .WithDescription(
                "Devuelve ambos resultados con sus métricas y verifica que coinciden. Si alguna vez "
                + "'sameResult' es false, hay un bug en la expansión inversa.")
            .Produces<ListObjectsComparisonResponse>()
            .ProducesValidationProblem();

        return group;
    }

    private static async Task<IResult> CheckAsync(
        CheckRequest request,
        IAccessControlEngine engine,
        IDecisionAuditStore audit,
        CancellationToken cancellationToken) =>
        await RunCheckAsync(request, engine, audit, forceExplain: false, cancellationToken);

    private static async Task<IResult> ExplainAsync(
        CheckRequest request,
        IAccessControlEngine engine,
        IDecisionAuditStore audit,
        CancellationToken cancellationToken) =>
        await RunCheckAsync(request, engine, audit, forceExplain: true, cancellationToken);

    private static async Task<IResult> RunCheckAsync(
        CheckRequest request,
        IAccessControlEngine engine,
        IDecisionAuditStore audit,
        bool forceExplain,
        CancellationToken cancellationToken)
    {
        var errors = new RequestParsing.ValidationCollector();

        var subject = RequestParsing.ParseSubject(request.Subject, nameof(request.Subject), errors);
        var relation = RequestParsing.ParseRelation(request.Relation, nameof(request.Relation), errors);
        var @object = RequestParsing.ParseObject(request.Object, nameof(request.Object), errors);

        if (errors.HasErrors || subject is null || relation is null || @object is null)
            return errors.ToResult();

        var options = RequestParsing.BuildOptions(forceExplain || request.Explain, request.ModelId, request.MaxDepth);

        var decision = await engine.CheckAsync(subject, relation, @object, options, cancellationToken);

        // Se audita SIEMPRE, incluidos los experimentos, pero marcando el origen para poder
        // separarlos después de las decisiones que protegieron una operación real.
        await audit.RecordAsync(decision, origin: "explorer", cancellationToken);

        return Results.Ok(decision.ToDto());
    }

    private static async Task<IResult> BatchCheckAsync(
        BatchCheckRequest request,
        IAccessControlEngine engine,
        CancellationToken cancellationToken)
    {
        var errors = new RequestParsing.ValidationCollector();

        if (request.Checks.Count == 0)
            errors.Add(nameof(request.Checks), "Hay que enviar al menos una comprobación.");

        if (request.Checks.Count > 200)
            errors.Add(nameof(request.Checks), "Máximo 200 comprobaciones por lote.");

        var parsed = new List<(Domain.Model.SubjectRef, string, Domain.Model.ObjectRef)>();

        foreach (var (check, index) in request.Checks.Select((check, index) => (check, index)))
        {
            var subject = RequestParsing.ParseSubject(check.Subject, $"Checks[{index}].Subject", errors);
            var relation = RequestParsing.ParseRelation(check.Relation, $"Checks[{index}].Relation", errors);
            var @object = RequestParsing.ParseObject(check.Object, $"Checks[{index}].Object", errors);

            if (subject is not null && relation is not null && @object is not null)
                parsed.Add((subject, relation, @object));
        }

        if (errors.HasErrors)
            return errors.ToResult();

        var first = request.Checks[0];
        var options = RequestParsing.BuildOptions(first.Explain, first.ModelId, first.MaxDepth);

        var decisions = await engine.BatchCheckAsync(parsed, options, cancellationToken);

        return Results.Ok(new BatchCheckResponse(
            decisions.Select(decision => decision.ToDto()).ToList(),
            AccessControlMapper.Aggregate(decisions)));
    }

    private static async Task<IResult> ExpandAsync(
        ExpandRequest request,
        IAccessControlEngine engine,
        CancellationToken cancellationToken)
    {
        var errors = new RequestParsing.ValidationCollector();

        var @object = RequestParsing.ParseObject(request.Object, nameof(request.Object), errors);
        var relation = RequestParsing.ParseRelation(request.Relation, nameof(request.Relation), errors);

        if (errors.HasErrors || @object is null || relation is null)
            return errors.ToResult();

        var options = RequestParsing.BuildOptions(explain: true, request.ModelId, maxDepth: null);
        var result = await engine.ExpandAsync(@object, relation, options, cancellationToken);

        return Results.Ok(result.ToDto());
    }

    private static async Task<IResult> ListObjectsAsync(
        ListObjectsRequest request,
        IAccessControlEngine engine,
        CancellationToken cancellationToken)
    {
        var errors = new RequestParsing.ValidationCollector();

        var subject = RequestParsing.ParseSubject(request.Subject, nameof(request.Subject), errors);
        var relation = RequestParsing.ParseRelation(request.Relation, nameof(request.Relation), errors);
        var strategy = RequestParsing.ParseStrategy(request.Strategy, nameof(request.Strategy), errors);

        if (string.IsNullOrWhiteSpace(request.ObjectType))
            errors.Add(nameof(request.ObjectType), "El tipo de objeto es obligatorio, por ejemplo 'project'.");

        if (errors.HasErrors || subject is null || relation is null || strategy is null)
            return errors.ToResult();

        var options = RequestParsing.BuildOptions(explain: false, request.ModelId, maxDepth: null);

        var result = await engine.ListObjectsAsync(
            subject, relation, request.ObjectType.Trim(), strategy.Value, options, cancellationToken);

        return Results.Ok(result.ToDto());
    }

    private static async Task<IResult> CompareListStrategiesAsync(
        ListObjectsRequest request,
        IAccessControlEngine engine,
        CancellationToken cancellationToken)
    {
        var errors = new RequestParsing.ValidationCollector();

        var subject = RequestParsing.ParseSubject(request.Subject, nameof(request.Subject), errors);
        var relation = RequestParsing.ParseRelation(request.Relation, nameof(request.Relation), errors);

        if (string.IsNullOrWhiteSpace(request.ObjectType))
            errors.Add(nameof(request.ObjectType), "El tipo de objeto es obligatorio.");

        if (errors.HasErrors || subject is null || relation is null)
            return errors.ToResult();

        var options = RequestParsing.BuildOptions(explain: false, request.ModelId, maxDepth: null);
        var objectType = request.ObjectType.Trim();

        var naive = await engine.ListObjectsAsync(
            subject, relation, objectType, ListObjectsStrategy.Naive, options, cancellationToken);

        var reverse = await engine.ListObjectsAsync(
            subject, relation, objectType, ListObjectsStrategy.ReverseExpansion, options, cancellationToken);

        var naiveIds = naive.Objects.Select(item => item.Object.ToString()).OrderBy(id => id).ToList();
        var reverseIds = reverse.Objects.Select(item => item.Object.ToString()).OrderBy(id => id).ToList();
        var same = naiveIds.SequenceEqual(reverseIds);

        return Results.Ok(new ListObjectsComparisonResponse(
            naive.ToDto(),
            reverse.ToDto(),
            same,
            BuildVerdict(same, naive, reverse)));
    }

    /// <summary>
    /// Redacta la conclusión de la comparación. Es donde el laboratorio dice en voz alta lo que
    /// las métricas insinúan.
    /// </summary>
    private static string BuildVerdict(bool same, ListObjectsResult naive, ListObjectsResult reverse)
    {
        if (!same)
        {
            return "⚠️ Las dos estrategias devuelven conjuntos DISTINTOS. Eso es un bug en la "
                   + "expansión inversa: la estrategia naive es solo un bucle de Checks y por tanto "
                   + "la referencia correcta.";
        }

        var verdict = $"Mismo resultado ({naive.Objects.Count} objeto(s)). "
                      + $"Naive: {naive.Metrics.StoreQueries} consultas al almacén. "
                      + $"Inversa: {reverse.Metrics.StoreQueries}. ";

        if (reverse.Metrics.ConfirmationChecks > 0)
        {
            verdict += $"La inversa ha tenido que confirmar {reverse.Metrics.ConfirmationChecks} "
                       + "candidato(s) con un Check real, porque el modelo tiene reglas no monótonas "
                       + "(un 'but not' o un 'and') que no se pueden invertir. Ese es su punto débil: "
                       + "con un catálogo pequeño puede salirle más cara que la naive. ";
        }

        verdict += reverse.Metrics.StoreQueries < naive.Metrics.StoreQueries
            ? "Aquí gana la inversa. Y la ventaja crece con el catálogo: el coste de la naive depende "
              + "de cuántos objetos hay, el de la inversa de cuántos puede ver el sujeto."
            : "Aquí NO gana la inversa, porque el catálogo es diminuto y el sujeto ve casi todo. "
              + "Añade objetos que el sujeto no pueda ver y verás cómo se invierte la relación.";

        return verdict;
    }
}
