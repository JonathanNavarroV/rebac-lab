using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Playground.AccessControl.Infrastructure.Persistence.Entities;

namespace Playground.AccessControl.Infrastructure.Persistence.Configurations;

public sealed class DecisionAuditConfiguration : IEntityTypeConfiguration<DecisionAuditEntity>
{
    public void Configure(EntityTypeBuilder<DecisionAuditEntity> builder)
    {
        builder.ToTable("decision_audit");

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Timestamp).IsRequired();
        builder.Property(entry => entry.Subject).HasMaxLength(256).IsRequired();
        builder.Property(entry => entry.Relation).HasMaxLength(64).IsRequired();
        builder.Property(entry => entry.Object).HasMaxLength(256).IsRequired();
        builder.Property(entry => entry.Reason).IsRequired();
        builder.Property(entry => entry.ModelId).HasMaxLength(64);
        builder.Property(entry => entry.EvaluationMode).HasMaxLength(32);
        builder.Property(entry => entry.Origin).HasMaxLength(64);

        // El camino se guarda como JSON en una columna 'jsonb'. Es la elección natural:
        // el árbol de evaluación tiene forma libre (profundidad variable, ramas de tipos
        // distintos) y normalizarlo en tablas relacionales daría un esquema ilegible que
        // nadie consultaría nunca. Con jsonb se puede además filtrar por contenido si algún
        // día hace falta ("todas las decisiones que pasaron por team:backend").
        builder.Property(entry => entry.PathJson).HasColumnType("jsonb");

        // La auditoría se consulta casi siempre por lo mismo: qué pasó últimamente, o qué
        // pasó con este sujeto o con este objeto.
        builder.HasIndex(entry => entry.Timestamp)
            .IsDescending()
            .HasDatabaseName("ix_decision_audit_timestamp");

        builder.HasIndex(entry => entry.Subject).HasDatabaseName("ix_decision_audit_subject");
        builder.HasIndex(entry => entry.Object).HasDatabaseName("ix_decision_audit_object");
    }
}
