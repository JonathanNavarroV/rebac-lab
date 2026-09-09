using Playground.AccessControl.Domain.Abstractions;
using Playground.AccessControl.Domain.Checking;
using Playground.AccessControl.Domain.Model;
using Playground.Contracts.AccessControl;

namespace Playground.AccessControl.Api.Mapping;

/// <summary>
/// Traducción entre el dominio del control de acceso y los DTOs de la API.
/// </summary>
/// <remarks>
/// Mapeo manual, sin AutoMapper ni Mapster, como en el resto de tus proyectos. Aquí además se
/// agradece: el dominio usa tipos ricos (<c>SubjectRef</c>, <c>UsersetRewrite</c>) y los DTOs
/// son cadenas planas, así que el mapeo no es mecánico y conviene tenerlo a la vista.
/// </remarks>
public static class AccessControlMapper
{
    public static CheckResponse ToDto(this CheckDecision decision) => new(
        decision.Allowed,
        decision.Subject.ToString(),
        decision.Relation,
        decision.Object.ToString(),
        decision.Reason,
        decision.Paths.Select(ToDto).ToList(),
        decision.SuggestedTuples.Select(tuple => tuple.ToString()).ToList(),
        decision.Trace is null ? null : ToDto(decision.Trace),
        decision.ModelId,
        decision.Metrics.ToDto());

    public static AuthorizationPathDto ToDto(this AuthorizationPath path) => new(
        path.Length,
        path.ToChainString(),
        path.Steps.Select(step => new PathStepDto(
            step.Kind.ToString(),
            step.Description,
            step.Tuple?.ToString())).ToList());

    public static CheckTraceNodeDto ToDto(this CheckNode node) => new(
        node.Kind,
        node.Label,
        node.Allowed,
        node.Detail,
        node.Tuple?.ToString(),
        node.Depth,
        node.FromCache,
        node.Children.Select(ToDto).ToList());

    public static CheckMetricsDto ToDto(this CheckMetrics metrics) => new(
        metrics.StoreQueries,
        metrics.TuplesRead,
        metrics.NodesEvaluated,
        metrics.MaxDepthReached,
        metrics.MemoizationHits,
        metrics.CyclesDetected,
        metrics.ConfirmationChecks,
        Math.Round(metrics.DurationMs, 3),
        metrics.EvaluationMode);

    public static ExpandResponse ToDto(this ExpandResult result) => new(
        result.Object.ToString(),
        result.Relation,
        ToDto(result.Root),
        result.Leaves.Select(leaf => leaf.ToString()).ToList(),
        result.Metrics.ToDto());

    public static ExpandNodeDto ToDto(this ExpandNode node) => new(
        node.Kind,
        node.Label,
        node.Object?.ToString(),
        node.Relation,
        node.Subject?.ToString(),
        node.Tuple?.ToString(),
        node.Children.Select(ToDto).ToList());

    public static ListObjectsResponse ToDto(this ListObjectsResult result) => new(
        result.Subject.ToString(),
        result.Relation,
        result.ObjectType,
        result.Strategy == ListObjectsStrategy.Naive ? "naive" : "reverse",
        result.Objects.Select(ToDto).ToList(),
        result.Denied.Select(ToDto).ToList(),
        result.Metrics.ToDto());

    public static AuthorizedObjectDto ToDto(this AuthorizedObject authorized) => new(
        authorized.Object.ToString(),
        authorized.Reason,
        authorized.Path?.ToDto());

    public static RelationshipDto ToDto(this RelationshipTuple tuple) => new(
        tuple.Id,
        tuple.ToString(),
        tuple.Object.ToString(),
        tuple.Object.Type,
        tuple.Object.Id,
        tuple.Relation,
        tuple.Subject.ToString(),
        tuple.Subject.Type,
        tuple.Subject.Id,
        tuple.Subject.Relation,
        tuple.Subject.IsUserset,
        tuple.Subject.IsWildcard,
        tuple.CreatedAt);

    public static AuditEntryDto ToDto(this DecisionAuditEntry entry) => new(
        entry.Id,
        entry.Timestamp,
        entry.Subject,
        entry.Relation,
        entry.Object,
        entry.Allowed,
        entry.Reason,
        entry.PathJson,
        entry.ModelId,
        entry.EvaluationMode,
        Math.Round(entry.DurationMs, 3),
        entry.StoreQueries,
        entry.Origin);

    public static AuthorizationModelDto ToDto(this AuthorizationModel model, bool isCurrent) => new(
        model.Id,
        model.SchemaVersion,
        model.Name,
        model.Description,
        model.RawDsl,
        model.Types.Values.Select(ToDto).ToList(),
        isCurrent);

    public static ModelTypeDto ToDto(this TypeDefinition type) => new(
        type.Name,
        type.Relations.Values.Select(ToDto).ToList(),
        type.Comment);

    public static ModelRelationDto ToDto(this RelationDefinition relation) => new(
        relation.Name,
        relation.Rewrite.ToDsl(),
        relation.Rewrite.Kind,
        relation.IsDirectlyAssignable,
        relation.DirectAssignments.Select(assignment => assignment.ToDsl()).Distinct().ToList(),
        relation.Comment);

    /// <summary>Suma las métricas de varias decisiones. Para el total de un BatchCheck.</summary>
    public static CheckMetricsDto Aggregate(IReadOnlyCollection<CheckDecision> decisions) => new(
        decisions.Sum(decision => decision.Metrics.StoreQueries),
        decisions.Sum(decision => decision.Metrics.TuplesRead),
        decisions.Sum(decision => decision.Metrics.NodesEvaluated),
        decisions.Count == 0 ? 0 : decisions.Max(decision => decision.Metrics.MaxDepthReached),
        decisions.Sum(decision => decision.Metrics.MemoizationHits),
        decisions.Sum(decision => decision.Metrics.CyclesDetected),
        decisions.Sum(decision => decision.Metrics.ConfirmationChecks),
        Math.Round(decisions.Sum(decision => decision.Metrics.DurationMs), 3),
        decisions.FirstOrDefault()?.Metrics.EvaluationMode);
}
