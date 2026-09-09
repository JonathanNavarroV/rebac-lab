using MediatR;
using Playground.Business.Application.Features.Projects.Common;
using Playground.Business.Domain.Authorization;
using Playground.Business.Domain.Entities;
using Playground.Business.Domain.Repositories;

namespace Playground.Business.Application.Features.Projects.Queries.GetProjects;

/// <summary>
/// Los proyectos que el usuario actual puede ver, con lo que puede hacer con cada uno.
/// </summary>
public sealed record GetProjectsQuery : IRequest<ProjectListDto>;

/// <summary>
/// Handler que demuestra el patrón correcto de integración con un sistema tipo Zanzibar.
/// </summary>
/// <remarks>
/// <para>
/// Este handler es, probablemente, el trozo de código de negocio que más enseña de todo el
/// proyecto. Hace tres cosas y ninguna implica saber nada del modelo de autorización:
/// </para>
/// <list type="number">
///   <item>
///     Pregunta al módulo <b>qué proyectos</b> puede ver el usuario. Recibe referencias
///     (<c>project:alpha</c>), no entidades: el módulo no conoce la tabla <c>projects</c>.
///   </item>
///   <item>
///     <b>Cruza</b> esa lista con su propio catálogo. Este es el punto clave: el negocio
///     enumera, el control de acceso decide. Ni el módulo pide la lista de proyectos al
///     negocio, ni el negocio consulta las tuplas.
///   </item>
///   <item>
///     Pide en <b>un solo lote</b> los tres permisos de cada proyecto para pintar los botones.
///   </item>
/// </list>
/// <para>
/// El error clásico aquí es el paso 1 al revés: traer todos los proyectos y preguntar uno a
/// uno. Funciona con veinte proyectos y hunde la pantalla con veinte mil. Es la diferencia
/// entre <c>ListObjects</c> y un bucle de <c>Check</c>, vista desde el lado del negocio.
/// </para>
/// </remarks>
public sealed class GetProjectsQueryHandler(
    IBusinessRepository<Project> projects,
    IAccessControlService accessControl,
    ICurrentUser currentUser) : IRequestHandler<GetProjectsQuery, ProjectListDto>
{
    private const string ViewRelation = "can_view";

    private static readonly string[] ActionRelations = ["can_edit", "can_delete", "can_publish"];

    public async Task<ProjectListDto> Handle(GetProjectsQuery request, CancellationToken cancellationToken)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();

        // 1. Qué puede ver, según el módulo de control de acceso.
        var authorized = await accessControl.ListAuthorizedAsync(
            currentUser.SubjectRef, ViewRelation, "project", cancellationToken);

        var authorizedIds = authorized
            .Select(reference => reference.Split(':', 2))
            .Where(parts => parts.Length == 2)
            .Select(parts => parts[1])
            .ToHashSet(StringComparer.Ordinal);

        // 2. Cruce con el catálogo propio. El módulo pudo devolver referencias de proyectos
        //    que ya no existen (una tupla huérfana de un proyecto borrado), y el negocio es
        //    quien sabe cuáles existen de verdad.
        var all = await projects.GetAllAsync(cancellationToken);
        var visible = all.Where(project => authorizedIds.Contains(project.Id)).ToList();

        // 3. Un único lote para los botones, en vez de 3 preguntas por fila.
        var questions = visible
            .SelectMany(project => ActionRelations.Select(relation =>
                (currentUser.SubjectRef, relation, project.ObjectRef)))
            .ToList();

        var decisions = await accessControl.CanManyAsync(questions, cancellationToken);

        var permissions = questions
            .Select((question, index) => (question.Item3, question.relation, decisions[index].Allowed))
            .ToDictionary(entry => $"{entry.Item1}|{entry.relation}", entry => entry.Allowed, StringComparer.Ordinal);

        var dtos = visible
            .Select(project => new ProjectDto(
                project.Id,
                project.Name,
                project.ObjectRef,
                project.Description,
                project.OrganizationId,
                CanView: true,
                CanEdit: permissions.GetValueOrDefault($"{project.ObjectRef}|can_edit"),
                CanDelete: permissions.GetValueOrDefault($"{project.ObjectRef}|can_delete"),
                CanPublish: permissions.GetValueOrDefault($"{project.ObjectRef}|can_publish")))
            .ToList();

        return new ProjectListDto(
            dtos,
            TotalInCatalog: all.Count,
            Visible: dtos.Count,
            AuthorizationMode: accessControl.Mode,
            ChecksPerformed: questions.Count + 1,
            AuthorizationMs: Math.Round(started.Elapsed.TotalMilliseconds, 2));
    }
}
