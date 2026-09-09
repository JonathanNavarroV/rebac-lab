using Playground.AccessControl.Domain.Checking;

namespace Playground.AccessControl.Domain.Abstractions;

/// <summary>Un registro de auditoría de una decisión de autorización.</summary>
/// <remarks>
/// <para>
/// Guardar <see cref="PathJson"/> junto a la decisión es lo que distingue una auditoría útil
/// de un log inútil. Un registro que dice <c>"Juan · edit · project:alpha · ALLOW"</c> no
/// permite responder la única pregunta que de verdad se hace en una investigación:
/// <b>¿por qué tenía acceso?</b> Y en ReBAC eso es especialmente grave, porque el acceso
/// pudo venir de una relación creada por otra persona, en otro objeto, tres niveles más
/// arriba, hace seis meses.
/// </para>
/// <para>
/// Se guarda también <see cref="ModelId"/>: la misma pregunta con el mismo estado de tuplas
/// puede dar respuestas distintas si el modelo cambió. Sin esa columna, la auditoría no es
/// reproducible.
/// </para>
/// </remarks>
public sealed record DecisionAuditEntry
{
    public long Id { get; init; }

    public required DateTime Timestamp { get; init; }

    public required string Subject { get; init; }

    public required string Relation { get; init; }

    public required string Object { get; init; }

    public required bool Allowed { get; init; }

    public required string Reason { get; init; }

    /// <summary>Árbol de evaluación y caminos, serializados. El "por qué" completo.</summary>
    public string? PathJson { get; init; }

    /// <summary>Modelo con el que se evaluó. Sin esto la auditoría no es reproducible.</summary>
    public string? ModelId { get; init; }

    /// <summary><c>InProcess</c> o <c>Remote</c>.</summary>
    public string? EvaluationMode { get; init; }

    public double DurationMs { get; init; }

    public int StoreQueries { get; init; }

    /// <summary>
    /// De dónde vino la pregunta: <c>business-api</c> (un handler protegiendo una operación)
    /// o <c>explorer</c> (tú experimentando). Separarlas evita que el laboratorio ensucie la
    /// auditoría de las decisiones reales.
    /// </summary>
    public string? Origin { get; init; }
}

/// <summary>Almacén de auditoría de decisiones.</summary>
public interface IDecisionAuditStore
{
    Task RecordAsync(
        CheckDecision decision,
        string? origin = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DecisionAuditEntry>> QueryAsync(
        string? subject = null,
        string? @object = null,
        bool? allowed = null,
        int take = 100,
        CancellationToken cancellationToken = default);
}
