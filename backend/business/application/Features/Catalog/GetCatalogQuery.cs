using MediatR;
using Playground.Business.Domain.Authorization;
using Playground.Business.Domain.Entities;
using Playground.Business.Domain.Repositories;

namespace Playground.Business.Application.Features.Catalog;

public sealed record CatalogEntityDto(string Id, string Name, string ObjectRef, string? Parent, string? Story);

/// <summary>
/// Todo el inventario del laboratorio, sin filtrar por permisos.
/// </summary>
/// <remarks>
/// <para>
/// Esta consulta <b>no comprueba autorización a propósito</b>, y conviene decirlo en voz alta
/// porque en cualquier otro proyecto sería un fallo de seguridad.
/// </para>
/// <para>
/// Sirve para poblar los selectores del laboratorio: para preguntar "¿puede Ana ver Project
/// Delta?" hace falta poder elegir a Ana y a Delta aunque Ana no los vea. Un laboratorio de
/// autorización necesita un modo omnisciente; una aplicación real, no.
/// </para>
/// </remarks>
public sealed record GetCatalogQuery : IRequest<CatalogDto>;

public sealed record CatalogDto(
    IReadOnlyList<CatalogEntityDto> Users,
    IReadOnlyList<CatalogEntityDto> Organizations,
    IReadOnlyList<CatalogEntityDto> Teams,
    IReadOnlyList<CatalogEntityDto> Groups,
    IReadOnlyList<CatalogEntityDto> Projects,
    IReadOnlyList<CatalogEntityDto> Folders,
    IReadOnlyList<CatalogEntityDto> Resources,
    string AuthorizationMode);

public sealed class GetCatalogQueryHandler(
    IBusinessRepository<BusinessUser> users,
    IBusinessRepository<Organization> organizations,
    IBusinessRepository<Team> teams,
    IBusinessRepository<Group> groups,
    IBusinessRepository<Project> projects,
    IBusinessRepository<Folder> folders,
    IBusinessRepository<ResourceItem> resources,
    IAccessControlService accessControl) : IRequestHandler<GetCatalogQuery, CatalogDto>
{
    public async Task<CatalogDto> Handle(GetCatalogQuery request, CancellationToken cancellationToken) =>
        new(
            (await users.GetAllAsync(cancellationToken))
                .Select(user => new CatalogEntityDto(user.Id, user.Name, user.ObjectRef, null, user.Story)).ToList(),

            (await organizations.GetAllAsync(cancellationToken))
                .Select(entity => new CatalogEntityDto(entity.Id, entity.Name, entity.ObjectRef, null, null)).ToList(),

            (await teams.GetAllAsync(cancellationToken))
                .Select(team => new CatalogEntityDto(
                    team.Id, team.Name, team.ObjectRef,
                    team.OrganizationId is null ? null : $"organization:{team.OrganizationId}", null)).ToList(),

            (await groups.GetAllAsync(cancellationToken))
                .Select(entity => new CatalogEntityDto(entity.Id, entity.Name, entity.ObjectRef, null, null)).ToList(),

            (await projects.GetAllAsync(cancellationToken))
                .Select(project => new CatalogEntityDto(
                    project.Id, project.Name, project.ObjectRef,
                    project.OrganizationId is null ? null : $"organization:{project.OrganizationId}", null)).ToList(),

            (await folders.GetAllAsync(cancellationToken))
                .Select(folder => new CatalogEntityDto(
                    folder.Id, folder.Name, folder.ObjectRef, folder.ParentRef, null)).ToList(),

            (await resources.GetAllAsync(cancellationToken))
                .Select(resource => new CatalogEntityDto(
                    resource.Id, resource.Name, resource.ObjectRef, resource.ParentRef, null)).ToList(),

            accessControl.Mode);
}
