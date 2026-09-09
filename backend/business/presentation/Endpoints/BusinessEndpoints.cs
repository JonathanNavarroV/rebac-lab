using System.Diagnostics.CodeAnalysis;
using MediatR;
using Playground.Business.Api.Identity;
using Playground.Business.Application.Features.Catalog;
using Playground.Business.Application.Features.Comparison;
using Playground.Business.Application.Features.Projects.Commands;
using Playground.Business.Application.Features.Projects.Common;
using Playground.Business.Application.Features.Projects.Queries.GetProjects;
using Playground.Business.Application.Features.Resources;
using Playground.Business.Domain.Authorization;
using Playground.Business.Domain.Entities;
using Playground.Business.Domain.Repositories;

namespace Playground.Business.Api.Endpoints;

[ExcludeFromCodeCoverage]
public static class BusinessEndpoints
{
    public static WebApplication MapBusinessEndpoints(this WebApplication app)
    {
        MapAuthEndpoints(app);
        MapProjectEndpoints(app);
        MapResourceEndpoints(app);
        MapCatalogEndpoints(app);
        MapComparisonEndpoints(app);

        return app;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Identidad
    // ═════════════════════════════════════════════════════════════════════════

    private static void MapAuthEndpoints(WebApplication app)
    {
        var auth = app.MapGroup("/auth").WithTags("Identity");

        auth.MapGet("/users", async (IBusinessRepository<BusinessUser> users, CancellationToken cancellationToken) =>
            {
                var all = await users.GetAllAsync(cancellationToken);

                return Results.Ok(all.Select(user => new
                {
                    user.Id,
                    user.Name,
                    subject = user.ObjectRef,
                    user.Story,
                }));
            })
            .WithName("GetLabUsers")
            .WithSummary("Las personas del laboratorio, con su situación")
            .WithDescription("Alimenta el selector de «actuar como». Cada una está diseñada para "
                             + "demostrar una forma distinta de obtener (o no obtener) acceso.");

        auth.MapPost("/act-as", (ActAsRequest request, ActAsTokenService tokens) =>
                Results.Ok(new
                {
                    token = tokens.IssueToken(request.UserId),
                    subject = $"user:{request.UserId}",
                    note = "Token de laboratorio sin contraseña. También puedes cambiar de identidad "
                           + $"enviando la cabecera {CurrentUser.ActAsHeader}.",
                }))
            .WithName("ActAs")
            .WithSummary("Cambia de identidad sin contraseña")
            .WithDescription("Sin login ni logout: en un laboratorio de autorización interesa poder "
                             + "repetir la misma pregunta como otra persona en dos clics.");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Negocio protegido
    // ═════════════════════════════════════════════════════════════════════════

    private static void MapProjectEndpoints(WebApplication app)
    {
        var projects = app.MapGroup("/projects").WithTags("Projects");

        projects.MapGet("/", async (ISender sender, CancellationToken cancellationToken) =>
                Results.Ok(await sender.Send(new GetProjectsQuery(), cancellationToken)))
            .WithName("GetProjects")
            .WithSummary("Los proyectos que el usuario actual puede ver")
            .WithDescription("Devuelve además cuántas preguntas de autorización ha costado pintar la "
                             + "lista y en qué modo (InProcess o Remote), que es donde se ve el coste "
                             + "de tener el control de acceso fuera del proceso.")
            .Produces<ProjectListDto>();

        projects.MapPost("/", async (CreateProjectCommand command, ISender sender, CancellationToken cancellationToken) =>
                Results.Ok(await sender.Send(command, cancellationToken)))
            .WithName("CreateProject")
            .WithSummary("Crea un proyecto y declara quién es su dueño y de qué organización cuelga")
            .Produces<ProjectDto>();

        projects.MapPut("/{id}", async (string id, UpdateProjectBody body, ISender sender, CancellationToken cancellationToken) =>
                Results.Ok(await sender.Send(new UpdateProjectCommand(id, body.Name, body.Description), cancellationToken)))
            .WithName("UpdateProject")
            .WithSummary("Modifica un proyecto. Exige can_edit")
            .Produces<ProjectDto>()
            .Produces(StatusCodes.Status403Forbidden);

        projects.MapPost("/{id}/publish", async (string id, ISender sender, CancellationToken cancellationToken) =>
                Results.Ok(await sender.Send(new PublishProjectCommand(id), cancellationToken)))
            .WithName("PublishProject")
            .WithSummary("Publica un proyecto. Exige can_publish, que es una intersección")
            .WithDescription("El caso interesante: hay que poder editar Y ser miembro de la "
                             + "organización. Una colaboradora externa con permiso de edición recibe "
                             + "DENY aquí, y el handler es idéntico al de editar.")
            .Produces<ProjectDto>()
            .Produces(StatusCodes.Status403Forbidden);

        projects.MapDelete("/{id}", async (string id, ISender sender, CancellationToken cancellationToken) =>
            {
                await sender.Send(new DeleteProjectCommand(id), cancellationToken);
                return Results.NoContent();
            })
            .WithName("DeleteProject")
            .WithSummary("Borra un proyecto. Exige can_delete")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status403Forbidden);
    }

    private static void MapResourceEndpoints(WebApplication app)
    {
        var resources = app.MapGroup("/resources").WithTags("Resources");

        resources.MapGet("/", async (ISender sender, CancellationToken cancellationToken, string? parentRef = null) =>
                Results.Ok(await sender.Send(new GetResourcesQuery(parentRef), cancellationToken)))
            .WithName("GetResources")
            .WithSummary("Los recursos que el usuario actual puede ver")
            .Produces<ResourceListDto>();

        resources.MapPut("/{id}", async (string id, UpdateResourceBody body, ISender sender, CancellationToken cancellationToken) =>
                Results.Ok(await sender.Send(new UpdateResourceCommand(id, body.Name, body.Content), cancellationToken)))
            .WithName("UpdateResource")
            .WithSummary("Modifica un recurso. Exige can_edit")
            .Produces<ResourceDto>()
            .Produces(StatusCodes.Status403Forbidden);

        resources.MapPost("/{id}/share", async (string id, ShareBody body, ISender sender, CancellationToken cancellationToken) =>
                Results.Ok(await sender.Send(new ShareResourceCommand(id, body.ShareWith), cancellationToken)))
            .WithName("ShareResource")
            .WithSummary("Comparte un recurso con una persona, un equipo, un grupo o una organización")
            .WithDescription("El mismo endpoint sirve para los cuatro casos: solo cambia la notación "
                             + "del sujeto. En un modelo de permisos tradicional, compartir con un "
                             + "grupo suele ser una funcionalidad aparte que se añade meses después.")
            .Produces<ShareResultDto>()
            .Produces(StatusCodes.Status403Forbidden);
    }

    private static void MapCatalogEndpoints(WebApplication app)
    {
        app.MapGet("/catalog", async (ISender sender, CancellationToken cancellationToken) =>
                Results.Ok(await sender.Send(new GetCatalogQuery(), cancellationToken)))
            .WithTags("Catalog")
            .WithName("GetCatalog")
            .WithSummary("Todo el inventario, SIN filtrar por permisos")
            .WithDescription("Deliberadamente omnisciente: para preguntar «¿puede Ana ver Delta?» hay "
                             + "que poder elegir a Ana y a Delta aunque Ana no los vea. Un laboratorio "
                             + "necesita este modo; una aplicación real, no.")
            .Produces<CatalogDto>();
    }

    private static void MapComparisonEndpoints(WebApplication app)
    {
        app.MapPost("/comparison/check", async (CompareModelsQuery query, ISender sender, CancellationToken cancellationToken) =>
                Results.Ok(await sender.Send(query, cancellationToken)))
            .WithTags("RBAC vs ReBAC")
            .WithName("CompareModels")
            .WithSummary("La misma pregunta respondida por los dos modelos")
            .WithDescription("Devuelve la decisión de cada uno, cómo llegó a ella, y cuántas filas "
                             + "necesita cada modelo para expresar este mismo escenario.")
            .Produces<ComparisonDto>();
    }
}

public sealed record ActAsRequest(string UserId);

public sealed record UpdateProjectBody(string Name, string? Description);

public sealed record UpdateResourceBody(string Name, string? Content);

public sealed record ShareBody(string ShareWith);
