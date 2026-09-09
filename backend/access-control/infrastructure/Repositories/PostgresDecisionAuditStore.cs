using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Playground.AccessControl.Domain.Abstractions;
using Playground.AccessControl.Domain.Checking;
using Playground.AccessControl.Infrastructure.Persistence;
using Playground.AccessControl.Infrastructure.Persistence.Entities;

namespace Playground.AccessControl.Infrastructure.Repositories;

/// <summary>
/// Auditoría de decisiones sobre PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// Se guarda el árbol de evaluación entero, incluidas las ramas que denegaron. Es más caro que
/// guardar un booleano, y es a propósito: sin las ramas fallidas no se puede responder "¿por
/// qué se le denegó a esta persona?", que en una auditoría de seguridad se pregunta tanto como
/// la contraria.
/// </para>
/// <para>
/// En un sistema con volumen real esto no se escribiría de forma síncrona ni para todas las
/// decisiones: se muestrearía, o se enviaría a una cola. Aquí se escribe todo y en el momento
/// porque el objetivo es poder mirar la tabla después de cada experimento.
/// </para>
/// </remarks>
public sealed class PostgresDecisionAuditStore(AccessControlDbContext context) : IDecisionAuditStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task RecordAsync(
        CheckDecision decision,
        string? origin = null,
        CancellationToken cancellationToken = default)
    {
        var entity = new DecisionAuditEntity
        {
            Timestamp = DateTime.UtcNow,
            Subject = decision.Subject.ToString(),
            Relation = decision.Relation,
            Object = decision.Object.ToString(),
            Allowed = decision.Allowed,
            Reason = decision.Reason,
            PathJson = SerializePath(decision),
            ModelId = decision.ModelId,
            EvaluationMode = decision.Metrics.EvaluationMode,
            DurationMs = decision.Metrics.DurationMs,
            StoreQueries = decision.Metrics.StoreQueries,
            Origin = origin,
        };

        context.DecisionAudit.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DecisionAuditEntry>> QueryAsync(
        string? subject = null,
        string? @object = null,
        bool? allowed = null,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var query = context.DecisionAudit.AsNoTracking();

        if (subject is not null)
            query = query.Where(entry => entry.Subject == subject);

        if (@object is not null)
            query = query.Where(entry => entry.Object == @object);

        if (allowed is not null)
            query = query.Where(entry => entry.Allowed == allowed);

        var entities = await query
            .OrderByDescending(entry => entry.Timestamp)
            .ThenByDescending(entry => entry.Id)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken);

        return entities.Select(Map).ToList();
    }

    private static string? SerializePath(CheckDecision decision)
    {
        if (decision.Trace is null && decision.Paths.Count == 0)
            return null;

        var payload = new
        {
            paths = decision.Paths.Select(path => new
            {
                length = path.Length,
                chain = path.ToChainString(),
                steps = path.Steps.Select(step => new
                {
                    kind = step.Kind.ToString(),
                    description = step.Description,
                    tuple = step.Tuple?.ToString(),
                }),
            }),
            trace = decision.Trace is null ? null : SerializeNode(decision.Trace),
        };

        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    private static object SerializeNode(CheckNode node) => new
    {
        kind = node.Kind,
        label = node.Label,
        allowed = node.Allowed,
        detail = node.Detail,
        tuple = node.Tuple?.ToString(),
        depth = node.Depth,
        fromCache = node.FromCache,
        children = node.Children.Select(SerializeNode).ToList(),
    };

    private static DecisionAuditEntry Map(DecisionAuditEntity entity) => new()
    {
        Id = entity.Id,
        Timestamp = entity.Timestamp,
        Subject = entity.Subject,
        Relation = entity.Relation,
        Object = entity.Object,
        Allowed = entity.Allowed,
        Reason = entity.Reason,
        PathJson = entity.PathJson,
        ModelId = entity.ModelId,
        EvaluationMode = entity.EvaluationMode,
        DurationMs = entity.DurationMs,
        StoreQueries = entity.StoreQueries,
        Origin = entity.Origin,
    };
}
