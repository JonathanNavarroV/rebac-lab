using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Playground.AccessControl.Application.Authorization.Seed;
using Playground.Business.Domain.Entities;

namespace Playground.Business.Infrastructure.Persistence;

/// <summary>
/// Crea las entidades de negocio del escenario.
/// </summary>
/// <remarks>
/// <para>
/// Las entidades y las tuplas se generan a partir de <b>la misma</b> definición
/// (<c>PlaygroundScenario</c>), y eso resuelve un problema práctico que en un sistema real es
/// bastante molesto: que el catálogo de negocio y el grafo de autorización hablen de objetos
/// distintos.
/// </para>
/// <para>
/// Si el negocio tiene un <c>project:epsilon</c> del que el módulo de control de acceso no
/// sabe nada, nadie podrá verlo jamás. Y al revés, si hay tuplas de un proyecto que el negocio
/// ya borró, son tuplas huérfanas que ocupan y confunden. Mantener las dos copias alineadas es
/// trabajo de integración que no desaparece por elegir bien la arquitectura.
/// </para>
/// </remarks>
public sealed class BusinessSeeder(BusinessDbContext context, ILogger<BusinessSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await context.Projects.AnyAsync(cancellationToken))
            return;

        context.Users.AddRange(PlaygroundScenario.Users.Select(user => new BusinessUser
        {
            Id = user.Id,
            Name = user.DisplayName,
            Story = user.Story,
        }));

        context.Organizations.AddRange(PlaygroundScenario.Organizations.Select(entity => new Organization
        {
            Id = entity.Id,
            Name = entity.DisplayName,
        }));

        context.Teams.AddRange(PlaygroundScenario.Teams.Select(entity => new Team
        {
            Id = entity.Id,
            Name = entity.DisplayName,
            OrganizationId = StripType(entity.ParentRef),
        }));

        context.Groups.AddRange(PlaygroundScenario.Groups.Select(entity => new Group
        {
            Id = entity.Id,
            Name = entity.DisplayName,
        }));

        context.Projects.AddRange(PlaygroundScenario.Projects.Select(entity => new Project
        {
            Id = entity.Id,
            Name = entity.DisplayName,
            OrganizationId = StripType(entity.ParentRef),
        }));

        context.Folders.AddRange(PlaygroundScenario.Folders.Select(entity => new Folder
        {
            Id = entity.Id,
            Name = entity.DisplayName,
            ParentRef = entity.ParentRef,
        }));

        context.Resources.AddRange(PlaygroundScenario.Resources.Select(entity => new ResourceItem
        {
            Id = entity.Id,
            Name = entity.DisplayName,
            ParentRef = entity.ParentRef,
            Content = $"Contenido de ejemplo de {entity.DisplayName}.",
        }));

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Catálogo de negocio creado a partir del escenario del laboratorio.");
    }

    /// <summary>Convierte <c>organization:acme</c> en <c>acme</c>.</summary>
    private static string? StripType(string? objectRef)
    {
        if (objectRef is null)
            return null;

        var separator = objectRef.IndexOf(':');
        return separator < 0 ? objectRef : objectRef[(separator + 1)..];
    }
}
