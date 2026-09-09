using Playground.AccessControl.Domain.Abstractions;
using Playground.AccessControl.Domain.Checking;

namespace Playground.AccessControl.Application.Authorization.Storage;

/// <summary>
/// Auditoría en memoria, con un tope de entradas.
/// </summary>
/// <remarks>
/// El tope existe porque en un laboratorio se disparan cientos de checks en minutos y la
/// lista crecería sin control. En un sistema real el equivalente es la retención: la auditoría
/// de autorización es de los datos que más rápido crecen de todo el sistema, porque hay al
/// menos una decisión por petición.
/// </remarks>
public sealed class InMemoryDecisionAuditStore : IDecisionAuditStore
{
    private const int MaxEntries = 1000;

    private readonly Lock _gate = new();
    private readonly List<DecisionAuditEntry> _entries = [];
    private long _nextId = 1;

    public Task RecordAsync(
        CheckDecision decision,
        string? origin = null,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _entries.Add(new DecisionAuditEntry
            {
                Id = _nextId++,
                Timestamp = DateTime.UtcNow,
                Subject = decision.Subject.ToString(),
                Relation = decision.Relation,
                Object = decision.Object.ToString(),
                Allowed = decision.Allowed,
                Reason = decision.Reason,
                ModelId = decision.ModelId,
                EvaluationMode = decision.Metrics.EvaluationMode,
                DurationMs = decision.Metrics.DurationMs,
                StoreQueries = decision.Metrics.StoreQueries,
                Origin = origin,
            });

            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(0, _entries.Count - MaxEntries);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DecisionAuditEntry>> QueryAsync(
        string? subject = null,
        string? @object = null,
        bool? allowed = null,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<DecisionAuditEntry> result = _entries
                .Where(entry => subject is null || entry.Subject == subject)
                .Where(entry => @object is null || entry.Object == @object)
                .Where(entry => allowed is null || entry.Allowed == allowed)
                .OrderByDescending(entry => entry.Id)
                .Take(Math.Clamp(take, 1, 500))
                .ToList();

            return Task.FromResult(result);
        }
    }
}
