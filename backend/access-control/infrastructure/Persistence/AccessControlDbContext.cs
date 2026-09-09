using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Playground.AccessControl.Infrastructure.Persistence.Entities;

namespace Playground.AccessControl.Infrastructure.Persistence;

/// <summary>
/// Contexto del módulo de control de acceso. Tres tablas y ninguna referencia al negocio.
/// </summary>
/// <remarks>
/// <para>
/// Merece la pena fijarse en lo que <b>no</b> hay aquí: ni <c>Users</c>, ni <c>Projects</c>,
/// ni ninguna entidad de negocio. Este contexto apunta a una base de datos distinta
/// (<c>postgres-access-control</c>), y esa separación física es intencionada.
/// </para>
/// <para>
/// El efecto práctico es que resulta <b>imposible</b> escribir un <c>JOIN</c> entre un
/// proyecto y quien tiene acceso a él. Y eso, que parece una molestia, es justo lo que hace
/// que el laboratorio enseñe algo: con las dos cosas en la misma base, la tentación de
/// resolver "qué proyectos puede ver Juan" con un <c>JOIN</c> de tres tablas sería
/// irresistible, funcionaría en el dataset pequeño, y no se aprendería nada de por qué existe
/// ListObjects.
/// </para>
/// </remarks>
public sealed class AccessControlDbContext(DbContextOptions<AccessControlDbContext> options)
    : DbContext(options)
{
    public DbSet<RelationshipTupleEntity> RelationshipTuples => Set<RelationshipTupleEntity>();

    public DbSet<AuthorizationModelEntity> AuthorizationModels => Set<AuthorizationModelEntity>();

    public DbSet<DecisionAuditEntity> DecisionAudit => Set<DecisionAuditEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
