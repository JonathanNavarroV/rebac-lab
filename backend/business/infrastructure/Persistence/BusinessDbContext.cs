using Microsoft.EntityFrameworkCore;
using Playground.Business.Domain.Entities;
using Playground.Business.Domain.Rbac;

namespace Playground.Business.Infrastructure.Persistence;

/// <summary>
/// Base de datos del negocio. Y, aparte, las tablas de RBAC.
/// </summary>
/// <remarks>
/// <para>
/// Que las tablas de RBAC estén <b>aquí dentro</b> y no en el módulo de control de acceso no
/// es una casualidad de organización: es el argumento visual del laboratorio.
/// </para>
/// <para>
/// En RBAC los permisos son tablas de tu aplicación, con FKs a tus entidades, consultadas con
/// <c>JOIN</c>s desde tus queries. No hay ningún servicio de autorización, porque no hace
/// falta: el modelo es tan simple que cabe en tres tablas. En ReBAC, en cambio, la
/// autorización vive en otra base de datos y detrás de una API, porque el modelo es lo bastante
/// complejo como para merecer su propio sistema.
/// </para>
/// <para>
/// Poner las dos cosas una al lado de la otra deja claro el intercambio: RBAC es más simple de
/// desplegar y de entender; ReBAC es más caro de montar y expresa cosas que RBAC no puede.
/// </para>
/// </remarks>
public sealed class BusinessDbContext(DbContextOptions<BusinessDbContext> options) : DbContext(options)
{
    // ── Negocio ─────────────────────────────────────────────────────────────
    public DbSet<BusinessUser> Users => Set<BusinessUser>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Team> Teams => Set<Team>();

    public DbSet<Group> Groups => Set<Group>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Folder> Folders => Set<Folder>();

    public DbSet<ResourceItem> Resources => Set<ResourceItem>();

    // ── RBAC, solo para la comparación ──────────────────────────────────────
    public DbSet<RbacRole> RbacRoles => Set<RbacRole>();

    public DbSet<RbacRolePermission> RbacRolePermissions => Set<RbacRolePermission>();

    public DbSet<RbacUserRole> RbacUserRoles => Set<RbacUserRole>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BusinessDbContext).Assembly);
    }
}
