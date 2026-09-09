using FluentValidation;
using MediatR;
using Playground.Business.Application.Features.Projects.Common;
using Playground.Business.Domain.Authorization;
using Playground.Business.Domain.Entities;
using Playground.Business.Domain.Repositories;

namespace Playground.Business.Application.Features.Projects.Commands;

// ═════════════════════════════════════════════════════════════════════════════
// NOTA SOBRE LA ESTRUCTURA DE FICHEROS
//
// En tus otros proyectos cada comando vive en su propia carpeta con tres ficheros
// (Command, Handler, Validator). Aquí los tres comandos van juntos a propósito:
// lo que se quiere enseñar es que los tres hacen EXACTAMENTE lo mismo con la
// autorización y solo cambia la relación que preguntan, y eso se ve de un vistazo
// cuando están uno debajo de otro. Repartidos en nueve ficheros, no.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Modifica un proyecto. Exige <c>can_edit</c>.</summary>
public sealed record UpdateProjectCommand(string Id, string Name, string? Description) : IRequest<ProjectDto>;

public sealed class UpdateProjectCommandValidator : AbstractValidator<UpdateProjectCommand>
{
    public UpdateProjectCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty().WithMessage("El identificador del proyecto es obligatorio.");
        RuleFor(command => command.Name).NotEmpty().MaximumLength(128)
            .WithMessage("El nombre es obligatorio y no puede superar los 128 caracteres.");
    }
}

public sealed class UpdateProjectCommandHandler(
    IBusinessRepository<Project> projects,
    IAccessControlService accessControl,
    ICurrentUser currentUser) : IRequestHandler<UpdateProjectCommand, ProjectDto>
{
    public async Task<ProjectDto> Handle(UpdateProjectCommand request, CancellationToken cancellationToken)
    {
        var project = await projects.GetByIdAsync(request.Id, cancellationToken)
                      ?? throw new NotFoundException("project", request.Id);

        // ── LA ÚNICA LÍNEA DE AUTORIZACIÓN DE TODO EL HANDLER ───────────────────
        //
        // Que Juan pueda editar por ser miembro de un equipo que es editor del proyecto,
        // que María pueda por ser administradora de la organización, o que Sofía pueda por
        // una tupla directa, es información que este código NO TIENE y NO NECESITA.
        //
        // Y ese es el objetivo entero del ejercicio: mañana se puede añadir "los miembros
        // del equipo de soporte también pueden editar" cambiando una línea del modelo de
        // autorización, y este fichero no se toca.
        await EnsureAllowedAsync(accessControl, currentUser, "can_edit", project.ObjectRef, cancellationToken);

        project.Name = request.Name;
        project.Description = request.Description;

        await projects.UpdateAsync(project, cancellationToken);

        return await ProjectPermissions.DescribeAsync(project, accessControl, currentUser, cancellationToken);
    }

    internal static async Task EnsureAllowedAsync(
        IAccessControlService accessControl,
        ICurrentUser currentUser,
        string relation,
        string objectRef,
        CancellationToken cancellationToken)
    {
        var decision = await accessControl.CanAsync(currentUser.SubjectRef, relation, objectRef, cancellationToken);

        if (!decision.Allowed)
            throw new ForbiddenException(currentUser.SubjectRef, relation, objectRef, decision.Reason);
    }
}

/// <summary>Borra un proyecto. Exige <c>can_delete</c>.</summary>
public sealed record DeleteProjectCommand(string Id) : IRequest<Unit>;

public sealed class DeleteProjectCommandValidator : AbstractValidator<DeleteProjectCommand>
{
    public DeleteProjectCommandValidator() =>
        RuleFor(command => command.Id).NotEmpty().WithMessage("El identificador del proyecto es obligatorio.");
}

public sealed class DeleteProjectCommandHandler(
    IBusinessRepository<Project> projects,
    IAccessControlService accessControl,
    ICurrentUser currentUser) : IRequestHandler<DeleteProjectCommand, Unit>
{
    public async Task<Unit> Handle(DeleteProjectCommand request, CancellationToken cancellationToken)
    {
        var project = await projects.GetByIdAsync(request.Id, cancellationToken)
                      ?? throw new NotFoundException("project", request.Id);

        // Misma forma que editar, otra relación. En el modelo, 'can_delete' es más estricta
        // que 'can_edit' (no basta con ser editor), pero aquí eso no se ve ni hace falta.
        await UpdateProjectCommandHandler.EnsureAllowedAsync(
            accessControl, currentUser, "can_delete", project.ObjectRef, cancellationToken);

        await projects.DeleteAsync(project, cancellationToken);

        // Deuda deliberada: las tuplas del proyecto borrado siguen ahí. Es un problema real
        // de integrar un sistema de autorización separado — nadie limpia las relaciones de un
        // objeto que ya no existe, y acaban siendo tuplas huérfanas que conceden acceso a la
        // nada. En producción esto se resuelve con un evento de borrado o un recolector.
        // Se deja a la vista porque el laboratorio permite comprobarlo: borra un proyecto y
        // mira el grafo.
        return Unit.Value;
    }
}

/// <summary>
/// Publica un proyecto. Exige <c>can_publish</c>, que en el modelo es una <b>intersección</b>.
/// </summary>
/// <remarks>
/// Es el comando más interesante de los tres. <c>can_publish</c> exige poder editar
/// <i>y además</i> ser miembro de la organización, así que una colaboradora externa con
/// permiso de edición recibe un DENY aquí. Y este handler es idéntico a los otros dos: la
/// condición compuesta vive entera en el modelo.
/// </remarks>
public sealed record PublishProjectCommand(string Id) : IRequest<ProjectDto>;

public sealed class PublishProjectCommandValidator : AbstractValidator<PublishProjectCommand>
{
    public PublishProjectCommandValidator() =>
        RuleFor(command => command.Id).NotEmpty().WithMessage("El identificador del proyecto es obligatorio.");
}

public sealed class PublishProjectCommandHandler(
    IBusinessRepository<Project> projects,
    IAccessControlService accessControl,
    ICurrentUser currentUser) : IRequestHandler<PublishProjectCommand, ProjectDto>
{
    public async Task<ProjectDto> Handle(PublishProjectCommand request, CancellationToken cancellationToken)
    {
        var project = await projects.GetByIdAsync(request.Id, cancellationToken)
                      ?? throw new NotFoundException("project", request.Id);

        await UpdateProjectCommandHandler.EnsureAllowedAsync(
            accessControl, currentUser, "can_publish", project.ObjectRef, cancellationToken);

        return await ProjectPermissions.DescribeAsync(project, accessControl, currentUser, cancellationToken);
    }
}

/// <summary>Crea un proyecto y declara los hechos que lo sitúan en el sistema.</summary>
public sealed record CreateProjectCommand(string Id, string Name, string? Description, string OrganizationId)
    : IRequest<ProjectDto>;

public sealed class CreateProjectCommandValidator : AbstractValidator<CreateProjectCommand>
{
    public CreateProjectCommandValidator()
    {
        RuleFor(command => command.Id)
            .NotEmpty()
            .Matches("^[a-z0-9][a-z0-9-]*$")
            .WithMessage("El identificador debe ser minúsculas, dígitos y guiones, por ejemplo 'nuevo-proyecto'. "
                         + "Se usa tal cual en las tuplas ('project:nuevo-proyecto'), por eso es restrictivo.");

        RuleFor(command => command.Name).NotEmpty().MaximumLength(128);
        RuleFor(command => command.OrganizationId).NotEmpty();
    }
}

public sealed class CreateProjectCommandHandler(
    IBusinessRepository<Project> projects,
    IAccessControlService accessControl,
    ICurrentUser currentUser) : IRequestHandler<CreateProjectCommand, ProjectDto>
{
    public async Task<ProjectDto> Handle(CreateProjectCommand request, CancellationToken cancellationToken)
    {
        var project = new Project
        {
            Id = request.Id,
            Name = request.Name,
            Description = request.Description,
            OrganizationId = request.OrganizationId,
        };

        await projects.AddAsync(project, cancellationToken);

        // El negocio declara HECHOS, no permisos. Fíjate en que no existe forma de escribir
        // "concede can_edit a Juan": solo se puede decir quién es el dueño y de qué cuelga.
        // Lo que eso implique en materia de permisos lo decide el modelo.
        await accessControl.WriteRelationshipAsync(
            project.ObjectRef, "owner", currentUser.SubjectRef, cancellationToken);

        await accessControl.WriteRelationshipAsync(
            project.ObjectRef, "parent", $"organization:{request.OrganizationId}", cancellationToken);

        return await ProjectPermissions.DescribeAsync(project, accessControl, currentUser, cancellationToken);
    }
}

/// <summary>Calcula los flags de permisos de un proyecto para el usuario actual.</summary>
internal static class ProjectPermissions
{
    public static async Task<ProjectDto> DescribeAsync(
        Project project,
        IAccessControlService accessControl,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var questions = new[] { "can_view", "can_edit", "can_delete", "can_publish" }
            .Select(relation => (currentUser.SubjectRef, relation, project.ObjectRef))
            .ToList();

        var decisions = await accessControl.CanManyAsync(questions, cancellationToken);

        return new ProjectDto(
            project.Id,
            project.Name,
            project.ObjectRef,
            project.Description,
            project.OrganizationId,
            decisions[0].Allowed,
            decisions[1].Allowed,
            decisions[2].Allowed,
            decisions[3].Allowed);
    }
}
