using FluentAssertions;
using Moq;
using Playground.Business.Application.Features.Projects.Queries.GetProjects;
using Playground.Business.Domain.Authorization;
using Playground.Business.Domain.Entities;
using Playground.Business.Domain.Repositories;

namespace Business.UnitTests.Features.Projects;

/// <summary>
/// El handler que demuestra el patrón correcto de integración con un sistema tipo Zanzibar.
/// </summary>
/// <remarks>
/// Los dos comportamientos que se protegen aquí son los que más se rompen en la práctica:
/// pedir la lista al módulo en lugar de comprobar objeto por objeto, y agrupar los permisos de
/// los botones en un solo lote en lugar de en un bucle.
/// </remarks>
public class GetProjectsQueryHandlerTests
{
    private readonly Mock<IBusinessRepository<Project>> _projects = new();
    private readonly Mock<IAccessControlService> _accessControl = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private readonly List<Project> _catalog =
    [
        new() { Id = "alpha", Name = "Project Alpha" },
        new() { Id = "beta", Name = "Project Beta" },
        new() { Id = "gamma", Name = "Project Gamma" },
        new() { Id = "delta", Name = "Project Delta" },
    ];

    public GetProjectsQueryHandlerTests()
    {
        _currentUser.SetupGet(user => user.SubjectRef).Returns("user:juan");
        _accessControl.SetupGet(service => service.Mode).Returns("InProcess");

        _projects
            .Setup(repository => repository.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_catalog);

        _accessControl
            .Setup(service => service.CanManyAsync(
                It.IsAny<IReadOnlyList<(string, string, string)>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<(string, string, string)> questions, CancellationToken _) =>
                questions.Select(_ => new AccessDecision(true, "ok", "InProcess", 0.1)).ToList());
    }

    private GetProjectsQueryHandler CreateHandler() =>
        new(_projects.Object, _accessControl.Object, _currentUser.Object);

    [Fact]
    public async Task Handle_ReturnsOnlyTheProjectsTheModuleAuthorized()
    {
        Authorize("project:alpha", "project:beta", "project:gamma");

        var result = await CreateHandler().Handle(new GetProjectsQuery(), CancellationToken.None);

        result.Projects.Select(project => project.Id).Should().BeEquivalentTo(["alpha", "beta", "gamma"]);
        result.TotalInCatalog.Should().Be(4);
        result.Visible.Should().Be(3);
    }

    [Fact]
    public async Task Handle_AsksTheModuleForTheListInsteadOfCheckingEachProject()
    {
        // Es la diferencia entre ListObjects y un bucle de Check, vista desde el negocio.
        // Con cuatro proyectos da igual; con veinte mil, la pantalla no carga.
        Authorize("project:alpha");

        await CreateHandler().Handle(new GetProjectsQuery(), CancellationToken.None);

        _accessControl.Verify(
            service => service.ListAuthorizedAsync(
                "user:juan", "can_view", "project", It.IsAny<CancellationToken>()),
            Times.Once);

        _accessControl.Verify(
            service => service.CanAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_AsksForTheButtonPermissionsInASingleBatch()
    {
        // Tres permisos por proyecto. En modo remoto, hacerlos de uno en uno serían nueve
        // viajes de red en lugar de uno.
        Authorize("project:alpha", "project:beta", "project:gamma");

        var result = await CreateHandler().Handle(new GetProjectsQuery(), CancellationToken.None);

        _accessControl.Verify(
            service => service.CanManyAsync(
                It.Is<IReadOnlyList<(string, string, string)>>(questions => questions.Count == 9),
                It.IsAny<CancellationToken>()),
            Times.Once);

        result.ChecksPerformed.Should().Be(10, "nueve permisos de botón más la consulta del listado");
    }

    [Fact]
    public async Task Handle_IgnoresAuthorizedReferencesThatNoLongerExistInTheCatalog()
    {
        // Tuplas huérfanas: el módulo sigue concediendo acceso a un proyecto que el negocio ya
        // borró. Es un problema real de tener dos almacenes, y el negocio es quien sabe qué
        // existe de verdad.
        Authorize("project:alpha", "project:fantasma");

        var result = await CreateHandler().Handle(new GetProjectsQuery(), CancellationToken.None);

        result.Projects.Select(project => project.Id).Should().BeEquivalentTo(["alpha"]);
    }

    [Fact]
    public async Task Handle_WhenTheModuleAuthorizesNothing_ReturnsAnEmptyListWithoutAskingForPermissions()
    {
        Authorize();

        var result = await CreateHandler().Handle(new GetProjectsQuery(), CancellationToken.None);

        result.Projects.Should().BeEmpty();
        result.Visible.Should().Be(0);
        result.TotalInCatalog.Should().Be(4, "el catálogo sigue teniendo cuatro; simplemente no ve ninguno");
    }

    [Fact]
    public async Task Handle_ReportsTheAuthorizationModeSoTheUiCanShowIt()
    {
        _accessControl.SetupGet(service => service.Mode).Returns("Remote");
        Authorize("project:alpha");

        var result = await CreateHandler().Handle(new GetProjectsQuery(), CancellationToken.None);

        result.AuthorizationMode.Should().Be("Remote");
    }

    private void Authorize(params string[] objectRefs) =>
        _accessControl
            .Setup(service => service.ListAuthorizedAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(objectRefs);
}
