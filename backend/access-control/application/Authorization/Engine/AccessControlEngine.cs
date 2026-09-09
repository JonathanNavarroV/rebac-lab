using Playground.AccessControl.Domain.Abstractions;
using Playground.AccessControl.Domain.Checking;
using Playground.AccessControl.Domain.Exceptions;
using Playground.AccessControl.Domain.Model;

namespace Playground.AccessControl.Application.Authorization.Engine;

/// <summary>
/// Implementación del motor de control de acceso: resuelve el modelo, delega en el evaluador
/// que corresponda y arma la respuesta con explicación y métricas.
/// </summary>
/// <remarks>
/// Es una clase de orquestación deliberadamente delgada. Toda la lógica interesante está en
/// <see cref="CheckEvaluator"/>, <see cref="ExpandEvaluator"/> y las dos estrategias de
/// listado; aquí solo se decide qué modelo usar y se compone el resultado. Mantenerla así
/// tiene un motivo práctico: los evaluadores no tocan el almacén de modelos ni las opciones
/// por defecto, y por eso la suite de conformidad puede ejercitarlos directamente.
/// </remarks>
public sealed class AccessControlEngine(
    IRelationshipTupleStore tuples,
    IAuthorizationModelStore models,
    IObjectCatalog catalog) : IAccessControlEngine
{
    /// <summary>
    /// Modo de evaluación que se reporta en las métricas. El motor siempre corre en proceso;
    /// es el adaptador remoto del lado del negocio el que sobrescribe esto por "Remote" para
    /// que en la UI se distinga de un vistazo qué latencia estás mirando.
    /// </summary>
    public const string InProcessMode = "InProcess";

    private readonly CheckEvaluator _checkEvaluator = new();
    private readonly ExpandEvaluator _expandEvaluator = new();
    private readonly CheckExplainer _explainer = new(tuples);
    private readonly ListObjectsNaiveStrategy _naive = new(catalog);
    private readonly ListObjectsReverseStrategy _reverse = new(tuples);

    public async Task<CheckDecision> CheckAsync(
        SubjectRef subject,
        string relation,
        ObjectRef @object,
        CheckOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveOptions = options ?? CheckOptions.Default;
        var model = await ResolveModelAsync(effectiveOptions, cancellationToken);

        return await CheckWithModelAsync(subject, relation, @object, model, effectiveOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<CheckDecision>> BatchCheckAsync(
        IReadOnlyCollection<(SubjectRef Subject, string Relation, ObjectRef Object)> requests,
        CheckOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveOptions = options ?? CheckOptions.Default;

        // El modelo se resuelve UNA vez para todo el lote. Es el primer ahorro obvio de un
        // BatchCheck, y el menos importante: el grande es que en modo remoto todo el lote
        // viaja en una sola petición HTTP en lugar de en N.
        var model = await ResolveModelAsync(effectiveOptions, cancellationToken);

        var decisions = new List<CheckDecision>(requests.Count);

        foreach (var (subject, relation, @object) in requests)
        {
            decisions.Add(await CheckWithModelAsync(
                subject, relation, @object, model, effectiveOptions, cancellationToken));
        }

        return decisions;
    }

    private async Task<CheckDecision> CheckWithModelAsync(
        SubjectRef subject,
        string relation,
        ObjectRef @object,
        AuthorizationModel model,
        CheckOptions options,
        CancellationToken cancellationToken)
    {
        var context = new EvaluationContext(tuples, model, options);

        var trace = await _checkEvaluator.EvaluateAsync(context, subject, relation, @object, cancellationToken);

        var paths = AuthorizationPathExtractor.Extract(trace, CheckEvaluator.MaxCollectedPaths);

        var reason = _explainer.BuildReason(trace, subject, relation, @object, paths, model);

        var suggestions = !trace.Allowed && options.SuggestMissingTuples
            ? await _explainer.SuggestMissingTuplesAsync(subject, relation, @object, model, cancellationToken)
            : [];

        return new CheckDecision
        {
            Allowed = trace.Allowed,
            Subject = subject,
            Relation = relation,
            Object = @object,
            Reason = reason,
            Trace = options.IncludeFailedBranches || trace.Allowed ? trace : null,
            Paths = paths,
            SuggestedTuples = suggestions,
            ModelId = model.Id,
            Metrics = context.BuildMetrics(InProcessMode),
        };
    }

    public async Task<ExpandResult> ExpandAsync(
        ObjectRef @object,
        string relation,
        CheckOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveOptions = options ?? CheckOptions.Default;
        var model = await ResolveModelAsync(effectiveOptions, cancellationToken);

        var context = new EvaluationContext(tuples, model, effectiveOptions);
        var root = await _expandEvaluator.EvaluateAsync(context, @object, relation, cancellationToken);

        return new ExpandResult
        {
            Object = @object,
            Relation = relation,
            Root = root,
            Leaves = ExpandEvaluator.CollectLeaves(root),
            Metrics = context.BuildMetrics(InProcessMode),
        };
    }

    public async Task<ListObjectsResult> ListObjectsAsync(
        SubjectRef subject,
        string relation,
        string objectType,
        ListObjectsStrategy strategy = ListObjectsStrategy.ReverseExpansion,
        CheckOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveOptions = options ?? CheckOptions.Default;
        var model = await ResolveModelAsync(effectiveOptions, cancellationToken);

        // Las dos estrategias necesitan poder hacer un Check: la naive para decidir cada
        // objeto, la inversa para confirmar candidatos cuando el modelo tiene reglas no
        // monótonas. Se les pasa como delegado para que no dependan del motor completo.
        Task<CheckDecision> Check(ObjectRef @object, CancellationToken token) =>
            CheckWithModelAsync(subject, relation, @object, model, effectiveOptions, token);

        return strategy switch
        {
            ListObjectsStrategy.Naive =>
                await _naive.ExecuteAsync(subject, relation, objectType, Check, cancellationToken),

            ListObjectsStrategy.ReverseExpansion =>
                await _reverse.ExecuteAsync(
                    subject, relation, objectType, model, effectiveOptions, Check, cancellationToken),

            _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Estrategia de listado no soportada."),
        };
    }

    private async Task<AuthorizationModel> ResolveModelAsync(CheckOptions options, CancellationToken cancellationToken)
    {
        if (options.ModelId is null)
            return await models.GetLatestAsync(cancellationToken);

        return await models.GetByIdAsync(options.ModelId, cancellationToken)
               ?? throw new ModelValidationException(0,
                   $"No existe ningún modelo de autorización con id '{options.ModelId}'.",
                   "Consulta los modelos publicados en GET /access-control/models.");
    }
}
