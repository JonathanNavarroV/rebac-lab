using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Playground.AccessControl.Infrastructure.Persistence.Entities;

namespace Playground.AccessControl.Infrastructure.Persistence.Configurations;

public sealed class AuthorizationModelConfiguration : IEntityTypeConfiguration<AuthorizationModelEntity>
{
    public void Configure(EntityTypeBuilder<AuthorizationModelEntity> builder)
    {
        builder.ToTable("authorization_models");

        builder.HasKey(model => model.Id);

        // El id lo calcula la aplicación (hash del DSL), no la base de datos: así el mismo
        // modelo tiene el mismo id en desarrollo, en los tests y en producción, y las
        // decisiones auditadas se pueden comparar entre entornos.
        builder.Property(model => model.Id).HasMaxLength(64).ValueGeneratedNever();

        builder.Property(model => model.Sequence).ValueGeneratedOnAdd();
        builder.Property(model => model.SchemaVersion).HasMaxLength(16).IsRequired();
        builder.Property(model => model.RawDsl).IsRequired();
        builder.Property(model => model.Name).HasMaxLength(128);
        builder.Property(model => model.Description).HasMaxLength(1024);
        builder.Property(model => model.PublishedAt).IsRequired();

        builder.HasIndex(model => model.Sequence)
            .IsUnique()
            .HasDatabaseName("ux_authorization_models_sequence");
    }
}
