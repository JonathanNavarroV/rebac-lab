using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Playground.Business.Domain.Entities;
using Playground.Business.Domain.Rbac;

namespace Playground.Business.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración común de las entidades de negocio.
/// </summary>
/// <remarks>
/// Todas comparten forma porque en este laboratorio el negocio es deliberadamente aburrido:
/// un id, un nombre y poco más. Lo interesante del proyecto no está aquí.
/// </remarks>
internal static class BusinessEntityConfiguration
{
    public static void ConfigureCommon<TEntity>(EntityTypeBuilder<TEntity> builder, string table)
        where TEntity : BusinessEntity
    {
        builder.ToTable(table);
        builder.HasKey(entity => entity.Id);

        // El id es una cadena elegida por quien crea la entidad, no un autonumérico: tiene que
        // coincidir con el id que aparece en las tuplas ('project:alpha'), y un entero
        // autogenerado obligaría a traducir en los dos sentidos constantemente.
        builder.Property(entity => entity.Id).HasMaxLength(128).ValueGeneratedNever();
        builder.Property(entity => entity.Name).HasMaxLength(256).IsRequired();
        builder.Property(entity => entity.CreatedAt).IsRequired();

        builder.Ignore(entity => entity.ObjectType);
        builder.Ignore(entity => entity.ObjectRef);
    }
}

public sealed class BusinessUserConfiguration : IEntityTypeConfiguration<BusinessUser>
{
    public void Configure(EntityTypeBuilder<BusinessUser> builder)
    {
        BusinessEntityConfiguration.ConfigureCommon(builder, "users");
        builder.Property(user => user.Story).HasMaxLength(1024);
    }
}

public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder) =>
        BusinessEntityConfiguration.ConfigureCommon(builder, "organizations");
}

public sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        BusinessEntityConfiguration.ConfigureCommon(builder, "teams");

        // Sin FK a organizations a propósito: la pertenencia real, la que decide accesos, es
        // la tupla 'team:backend#parent@organization:acme' y vive en la otra base de datos.
        // Esta columna es solo el dato de negocio, y que puedan desincronizarse es un problema
        // real que el laboratorio no esconde.
        builder.Property(team => team.OrganizationId).HasMaxLength(128);
    }
}

public sealed class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder) =>
        BusinessEntityConfiguration.ConfigureCommon(builder, "groups");
}

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        BusinessEntityConfiguration.ConfigureCommon(builder, "projects");
        builder.Property(project => project.OrganizationId).HasMaxLength(128);
        builder.Property(project => project.Description).HasMaxLength(1024);
    }
}

public sealed class FolderConfiguration : IEntityTypeConfiguration<Folder>
{
    public void Configure(EntityTypeBuilder<Folder> builder)
    {
        BusinessEntityConfiguration.ConfigureCommon(builder, "folders");
        builder.Property(folder => folder.ParentRef).HasMaxLength(256);
    }
}

public sealed class ResourceItemConfiguration : IEntityTypeConfiguration<ResourceItem>
{
    public void Configure(EntityTypeBuilder<ResourceItem> builder)
    {
        BusinessEntityConfiguration.ConfigureCommon(builder, "resources");
        builder.Property(resource => resource.ParentRef).HasMaxLength(256);
        builder.Property(resource => resource.Content).HasMaxLength(4096);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// RBAC: las tres tablas de toda la vida
// ═════════════════════════════════════════════════════════════════════════════

public sealed class RbacRoleConfiguration : IEntityTypeConfiguration<RbacRole>
{
    public void Configure(EntityTypeBuilder<RbacRole> builder)
    {
        builder.ToTable("rbac_roles");
        builder.HasKey(role => role.Id);
        builder.Property(role => role.Id).HasMaxLength(128).ValueGeneratedNever();
        builder.Property(role => role.Name).HasMaxLength(256).IsRequired();
        builder.Property(role => role.Description).HasMaxLength(1024);
    }
}

public sealed class RbacRolePermissionConfiguration : IEntityTypeConfiguration<RbacRolePermission>
{
    public void Configure(EntityTypeBuilder<RbacRolePermission> builder)
    {
        builder.ToTable("rbac_role_permissions");
        builder.HasKey(permission => permission.Id);
        builder.Property(permission => permission.RoleId).HasMaxLength(128).IsRequired();
        builder.Property(permission => permission.Permission).HasMaxLength(256).IsRequired();

        builder.HasIndex(permission => new { permission.RoleId, permission.Permission })
            .IsUnique()
            .HasDatabaseName("ux_rbac_role_permissions");
    }
}

public sealed class RbacUserRoleConfiguration : IEntityTypeConfiguration<RbacUserRole>
{
    public void Configure(EntityTypeBuilder<RbacUserRole> builder)
    {
        builder.ToTable("rbac_user_roles");
        builder.HasKey(assignment => assignment.Id);
        builder.Property(assignment => assignment.UserId).HasMaxLength(128).IsRequired();
        builder.Property(assignment => assignment.RoleId).HasMaxLength(128).IsRequired();

        builder.HasIndex(assignment => new { assignment.UserId, assignment.RoleId })
            .IsUnique()
            .HasDatabaseName("ux_rbac_user_roles");
    }
}
