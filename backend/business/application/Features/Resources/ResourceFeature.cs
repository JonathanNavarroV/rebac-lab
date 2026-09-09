using FluentValidation;
using MediatR;
using Playground.Business.Domain.Authorization;
using Playground.Business.Domain.Entities;
using Playground.Business.Domain.Repositories;

namespace Playground.Business.Application.Features.Resources;

public sealed record ResourceDto(
    string Id,
    string Name,
    string ObjectRef,
    string? ParentRef,
    string? Content,
    bool CanView,
    bool CanEdit,
    bool CanDelete);

public sealed record ResourceListDto(
    IReadOnlyList<ResourceDto> Resources,
    int TotalInCatalog,
    int Visible,
    string AuthorizationMode,
    int ChecksPerformed,
    double AuthorizationMs);

/// <summary>Los recursos que el usuario actual puede ver.</summary>
public sealed record GetResourcesQuery(string? ParentRef = null) : IRequest<ResourceListDto>;

public sealed class GetResourcesQueryHandler(
    IBusinessRepository<ResourceItem> resources,
    IAccessControlService accessControl,
    ICurrentUser currentUser) : IRequestHandler<GetResourcesQuery, ResourceListDto>
{
    public async Task<ResourceListDto> Handle(GetResourcesQuery request, CancellationToken cancellationToken)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();

        var authorized = await accessControl.ListAuthorizedAsync(
            currentUser.SubjectRef, "can_view", "resource", cancellationToken);

        var authorizedRefs = authorized.ToHashSet(StringComparer.Ordinal);

        var all = await resources.GetAllAsync(cancellationToken);

        var visible = all
            .Where(resource => authorizedRefs.Contains(resource.ObjectRef))
            .Where(resource => request.ParentRef is null || resource.ParentRef == request.ParentRef)
            .ToList();

        var questions = visible
            .SelectMany(resource => new[] { "can_edit", "can_delete" }
                .Select(relation => (currentUser.SubjectRef, relation, resource.ObjectRef)))
            .ToList();

        var decisions = await accessControl.CanManyAsync(questions, cancellationToken);

        var permissions = questions
            .Select((question, index) => (question.Item3, question.relation, decisions[index].Allowed))
            .ToDictionary(entry => $"{entry.Item1}|{entry.relation}", entry => entry.Allowed, StringComparer.Ordinal);

        var dtos = visible
            .Select(resource => new ResourceDto(
                resource.Id,
                resource.Name,
                resource.ObjectRef,
                resource.ParentRef,
                resource.Content,
                CanView: true,
                CanEdit: permissions.GetValueOrDefault($"{resource.ObjectRef}|can_edit"),
                CanDelete: permissions.GetValueOrDefault($"{resource.ObjectRef}|can_delete")))
            .ToList();

        return new ResourceListDto(
            dtos,
            all.Count,
            dtos.Count,
            accessControl.Mode,
            questions.Count + 1,
            Math.Round(started.Elapsed.TotalMilliseconds, 2));
    }
}

/// <summary>
/// Comparte un recurso con alguien.
/// </summary>
/// <remarks>
/// <para>
/// El destinatario se indica en notación de sujeto, y ahí está lo interesante: la misma
/// operación sirve para compartir con una persona (<c>user:pedro</c>), con un equipo entero
/// (<c>team:backend#member</c>), con un grupo (<c>group:seguridad#member</c>) o con toda una
/// organización (<c>organization:acme#member</c>).
/// </para>
/// <para>
/// <b>Y el código es el mismo en los cuatro casos.</b> No hay un <c>ShareWithUser</c>, un
/// <c>ShareWithTeam</c> y un <c>ShareWithOrganization</c>, ni tres tablas, ni un <c>switch</c>.
/// En un modelo de permisos tradicional, compartir con un grupo suele ser una funcionalidad
/// entera aparte que se añade meses después; aquí es el mismo endpoint con otra cadena.
/// </para>
/// </remarks>
public sealed record ShareResourceCommand(string ResourceId, string ShareWith) : IRequest<ShareResultDto>;

public sealed record ShareResultDto(string Tuple, string Explanation);

public sealed class ShareResourceCommandValidator : AbstractValidator<ShareResourceCommand>
{
    public ShareResourceCommandValidator()
    {
        RuleFor(command => command.ResourceId).NotEmpty();

        RuleFor(command => command.ShareWith)
            .NotEmpty()
            .Must(value => value.Contains(':'))
            .WithMessage("Indica con quién compartir en notación de sujeto: 'user:pedro' (una persona), "
                         + "'team:backend#member' (un equipo), 'group:seguridad#member' (un grupo) o "
                         + "'organization:acme#member' (toda una organización).");
    }
}

public sealed class ShareResourceCommandHandler(
    IBusinessRepository<ResourceItem> resources,
    IAccessControlService accessControl,
    ICurrentUser currentUser) : IRequestHandler<ShareResourceCommand, ShareResultDto>
{
    public async Task<ShareResultDto> Handle(ShareResourceCommand request, CancellationToken cancellationToken)
    {
        var resource = await resources.GetByIdAsync(request.ResourceId, cancellationToken)
                       ?? throw new NotFoundException("resource", request.ResourceId);

        // Para compartir hay que poder editar. Es una política, y vive donde deben vivir las
        // políticas: en la pregunta que hace el negocio, no en un 'if' sobre campos.
        var decision = await accessControl.CanAsync(
            currentUser.SubjectRef, "can_edit", resource.ObjectRef, cancellationToken);

        if (!decision.Allowed)
            throw new ForbiddenException(currentUser.SubjectRef, "can_edit", resource.ObjectRef, decision.Reason);

        await accessControl.WriteRelationshipAsync(
            resource.ObjectRef, "shared_with", request.ShareWith, cancellationToken);

        var isUserset = request.ShareWith.Contains('#');

        return new ShareResultDto(
            $"{resource.ObjectRef}#shared_with@{request.ShareWith}",
            isUserset
                ? $"Compartido con el conjunto «{request.ShareWith}». No se ha concedido acceso a ninguna "
                  + "persona concreta: lo tendrá quien pertenezca a ese conjunto ahora y en el futuro. "
                  + "Si mañana entra alguien nuevo, lo hereda sin que nadie toque esta compartición."
                : $"Compartido directamente con «{request.ShareWith}». Es una excepción individual: "
                  + "solo afecta a esa persona.");
    }
}

/// <summary>Modifica el contenido de un recurso. Exige <c>can_edit</c>.</summary>
public sealed record UpdateResourceCommand(string Id, string Name, string? Content) : IRequest<ResourceDto>;

public sealed class UpdateResourceCommandValidator : AbstractValidator<UpdateResourceCommand>
{
    public UpdateResourceCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(128);
    }
}

public sealed class UpdateResourceCommandHandler(
    IBusinessRepository<ResourceItem> resources,
    IAccessControlService accessControl,
    ICurrentUser currentUser) : IRequestHandler<UpdateResourceCommand, ResourceDto>
{
    public async Task<ResourceDto> Handle(UpdateResourceCommand request, CancellationToken cancellationToken)
    {
        var resource = await resources.GetByIdAsync(request.Id, cancellationToken)
                       ?? throw new NotFoundException("resource", request.Id);

        var decision = await accessControl.CanAsync(
            currentUser.SubjectRef, "can_edit", resource.ObjectRef, cancellationToken);

        if (!decision.Allowed)
            throw new ForbiddenException(currentUser.SubjectRef, "can_edit", resource.ObjectRef, decision.Reason);

        resource.Name = request.Name;
        resource.Content = request.Content;

        await resources.UpdateAsync(resource, cancellationToken);

        return new ResourceDto(
            resource.Id, resource.Name, resource.ObjectRef, resource.ParentRef, resource.Content,
            CanView: true, CanEdit: true, CanDelete: false);
    }
}
