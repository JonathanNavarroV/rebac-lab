using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Playground.AccessControl.Infrastructure.Persistence.Entities;

namespace Playground.AccessControl.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración de la tabla de tuplas. <b>Los dos índices son el diseño del sistema</b>, no
/// una optimización posterior.
/// </summary>
public sealed class RelationshipTupleConfiguration : IEntityTypeConfiguration<RelationshipTupleEntity>
{
    public void Configure(EntityTypeBuilder<RelationshipTupleEntity> builder)
    {
        builder.ToTable("relationship_tuples");

        builder.HasKey(tuple => tuple.Id);

        builder.Property(tuple => tuple.ObjectType).HasMaxLength(64).IsRequired();
        builder.Property(tuple => tuple.ObjectId).HasMaxLength(128).IsRequired();
        builder.Property(tuple => tuple.Relation).HasMaxLength(64).IsRequired();
        builder.Property(tuple => tuple.SubjectType).HasMaxLength(64).IsRequired();
        builder.Property(tuple => tuple.SubjectId).HasMaxLength(128).IsRequired();
        builder.Property(tuple => tuple.SubjectRelation).HasMaxLength(64);
        builder.Property(tuple => tuple.CreatedAt).IsRequired();

        // ── Unicidad ────────────────────────────────────────────────────────────
        // Una tupla es un HECHO, y un hecho no se repite: "los miembros de backend son
        // editores de alpha" o es cierto o no lo es. Sin esta restricción, invitar dos veces
        // a la misma persona duplicaría el hecho y la traza mostraría el mismo camino dos
        // veces, como si hubiera dos motivos independientes de acceso.
        //
        // Ojo con PostgreSQL: en un índice único, varios NULL se consideran distintos entre
        // sí, así que dos filas con SubjectRelation NULL e idéntico resto NO chocarían. Se
        // arregla con NULLS NOT DISTINCT (PostgreSQL 15+), que es justo lo que queremos aquí:
        // 'user:juan' aparece con SubjectRelation NULL y debe ser único.
        builder.HasIndex(tuple => new
            {
                tuple.ObjectType,
                tuple.ObjectId,
                tuple.Relation,
                tuple.SubjectType,
                tuple.SubjectId,
                tuple.SubjectRelation,
            })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("ux_relationship_tuples");

        // ── Índice hacia delante: el que usa Check ──────────────────────────────
        // "¿quién tiene <relation> sobre <object>?". Es LA consulta del sistema: se ejecuta
        // varias veces por cada Check, y un Check ocurre en cada petición de la aplicación.
        // Si solo pudieras tener un índice, sería este.
        builder.HasIndex(tuple => new { tuple.ObjectType, tuple.ObjectId, tuple.Relation })
            .HasDatabaseName("ix_relationship_tuples_forward");

        // ── Índice hacia atrás: el que usa ListObjects ──────────────────────────
        // "¿sobre qué objetos tiene <subject> alguna de estas relaciones?". Es la dirección
        // contraria y por eso necesita su propio índice: sin él, la expansión inversa
        // degeneraría en un escaneo completo de la tabla y sería más lenta que la estrategia
        // naive que pretende sustituir.
        builder.HasIndex(tuple => new
            {
                tuple.SubjectType,
                tuple.SubjectId,
                tuple.SubjectRelation,
                tuple.Relation,
                tuple.ObjectType,
            })
            .HasDatabaseName("ix_relationship_tuples_reverse");
    }
}
