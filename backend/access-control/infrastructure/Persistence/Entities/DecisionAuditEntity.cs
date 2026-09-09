namespace Playground.AccessControl.Infrastructure.Persistence.Entities;

/// <summary>
/// Registro de auditoría de una decisión de autorización.
/// </summary>
/// <remarks>
/// <para>
/// Las tres columnas que hacen que esta tabla sirva para algo son <see cref="PathJson"/>,
/// <see cref="ModelId"/> y <see cref="Timestamp"/>. Sin ellas queda un log de
/// "usuario · acción · recurso · permitido" que no permite responder la única pregunta que se
/// hace de verdad en una investigación: <b>por qué</b>.
/// </para>
/// <para>
/// Y en ReBAC esa pregunta es especialmente difícil sin ayuda: el acceso pudo llegar por una
/// relación que creó otra persona, sobre otro objeto, tres niveles más arriba, hace meses.
/// Reconstruirlo a posteriori es imposible si las tuplas han cambiado desde entonces — por eso
/// el camino se guarda en el momento, no se recalcula.
/// </para>
/// </remarks>
public sealed class DecisionAuditEntity
{
    public long Id { get; set; }

    public DateTime Timestamp { get; set; }

    public string Subject { get; set; } = null!;

    public string Relation { get; set; } = null!;

    public string Object { get; set; } = null!;

    public bool Allowed { get; set; }

    public string Reason { get; set; } = null!;

    /// <summary>Árbol de evaluación y caminos, serializados en el instante de decidir.</summary>
    public string? PathJson { get; set; }

    /// <summary>
    /// Modelo con el que se evaluó. Sin esto la auditoría no es reproducible: la misma
    /// pregunta con las mismas tuplas puede dar respuestas distintas si el modelo cambió.
    /// </summary>
    public string? ModelId { get; set; }

    public string? EvaluationMode { get; set; }

    public double DurationMs { get; set; }

    public int StoreQueries { get; set; }

    /// <summary>
    /// Quién preguntó: <c>business-api</c> o <c>explorer</c>.
    /// </summary>
    /// <remarks>
    /// Separar las decisiones que protegieron una operación real de las que solo eran
    /// experimentos evita que el laboratorio ensucie la auditoría — y de paso enseña que una
    /// auditoría de autorización sin contexto de origen es difícil de interpretar.
    /// </remarks>
    public string? Origin { get; set; }
}
