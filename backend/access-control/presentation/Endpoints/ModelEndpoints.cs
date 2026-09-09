using System.Diagnostics.CodeAnalysis;
using Playground.AccessControl.Api.Mapping;
using Playground.AccessControl.Application.Authorization.Model;
using Playground.AccessControl.Domain.Abstractions;
using Playground.AccessControl.Domain.Exceptions;
using Playground.Contracts.AccessControl;

namespace Playground.AccessControl.Api.Endpoints;

/// <summary>
/// Consulta y publicación de modelos de autorización.
/// </summary>
[ExcludeFromCodeCoverage]
public static class ModelEndpoints
{
    public static RouteGroupBuilder MapModelEndpoints(this RouteGroupBuilder group)
    {
        var models = group.MapGroup("/models").WithTags("Authorization model");

        models.MapGet("/", GetModelsAsync)
            .WithName("GetModels")
            .WithSummary("Modelos publicados, del más reciente al más antiguo")
            .WithDescription(
                "Los modelos son inmutables y versionados: publicar no sobrescribe. Tener varias "
                + "versiones a la vez permite responder la misma pregunta con dos modelos y ver qué "
                + "cambia — por ejemplo, con y sin herencia en carpetas.")
            .Produces<IReadOnlyList<AuthorizationModelDto>>();

        models.MapGet("/{id}", GetModelByIdAsync)
            .WithName("GetModelById")
            .WithSummary("Un modelo concreto, con sus tipos y relaciones descompuestos")
            .Produces<AuthorizationModelDto>()
            .Produces(StatusCodes.Status404NotFound);

        models.MapPost("/", PublishModelAsync)
            .WithName("PublishModel")
            .WithSummary("Compila y publica un modelo escrito en DSL")
            .WithDescription(
                "Valida el DSL antes de guardarlo y devuelve TODOS los errores encontrados, no solo "
                + "el primero. El identificador se deriva de un hash del contenido, así que "
                + "republicar un texto idéntico no crea una versión nueva.")
            .Produces<AuthorizationModelDto>()
            .ProducesValidationProblem();

        models.MapGet("/templates", GetTemplates)
            .WithName("GetModelTemplates")
            .WithSummary("Los modelos de ejemplo del laboratorio, listos para publicar")
            .Produces<IReadOnlyList<ModelTemplateDto>>();

        return group;
    }

    private static async Task<IResult> GetModelsAsync(
        IAuthorizationModelStore models,
        CancellationToken cancellationToken)
    {
        var all = await models.GetAllAsync(cancellationToken);
        var current = await models.GetLatestAsync(cancellationToken);

        return Results.Ok(all.Select(model => model.ToDto(model.Id == current.Id)).ToList());
    }

    private static async Task<IResult> GetModelByIdAsync(
        string id,
        IAuthorizationModelStore models,
        CancellationToken cancellationToken)
    {
        var model = await models.GetByIdAsync(id, cancellationToken);

        if (model is null)
            return Results.NotFound();

        var current = await models.GetLatestAsync(cancellationToken);

        return Results.Ok(model.ToDto(model.Id == current.Id));
    }

    private static async Task<IResult> PublishModelAsync(
        PublishModelRequest request,
        IAuthorizationModelStore models,
        CancellationToken cancellationToken)
    {
        try
        {
            var model = await models.PublishAsync(request.Dsl, request.Name, request.Description, cancellationToken);
            var current = await models.GetLatestAsync(cancellationToken);

            return Results.Ok(model.ToDto(model.Id == current.Id));
        }
        catch (ModelValidationException exception)
        {
            // Cada error del modelo se devuelve como un error de validación con su línea, para
            // que el editor del frontend pueda señalarlas todas de golpe.
            var errors = exception.Errors
                .GroupBy(error => error.Line > 0 ? $"Línea {error.Line}" : "Modelo")
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(error => error.Hint is null ? error.Message : $"{error.Message} — {error.Hint}")
                        .ToArray());

            return Results.ValidationProblem(errors, title: "Validation failed");
        }
    }

    private static IResult GetTemplates() => Results.Ok(new List<ModelTemplateDto>
    {
        new("full", "Modelo completo del laboratorio",
            "Organizaciones, equipos, grupos anidados, proyectos, carpetas recursivas y recursos. "
            + "Incluye herencia, comodín, compartición, intersección y exclusión.",
            PlaygroundModels.Full),

        new("no-folder-inheritance", "Sin herencia en carpetas",
            "Idéntico al completo salvo que a folder.can_view y folder.can_edit se les ha quitado "
            + "la cláusula 'from parent'. Publícalo y repite la misma pregunta para ver exactamente "
            + "qué hace un tuple_to_userset.",
            PlaygroundModels.WithoutFolderInheritance),

        new("rbac-equivalent", "RBAC expresado en ReBAC",
            "Cinco líneas que demuestran que ReBAC contiene a RBAC: un rol es un objeto con una "
            + "relación 'assignee', y tener un permiso es tener una relación con ese objeto. "
            + "Lo contrario no se puede hacer.",
            PlaygroundModels.RbacEquivalent),
    });
}

/// <summary>Un modelo de ejemplo listo para publicar desde el frontend.</summary>
public sealed record ModelTemplateDto(string Key, string Name, string Description, string Dsl);
