using AutoFixture;
using FluentAssertions;
using Moq;
using Playground.Business.Application.Features.Projects.Commands;
using Playground.Business.Domain.Authorization;
using Playground.Business.Domain.Entities;
using Playground.Business.Domain.Repositories;

namespace Business.UnitTests.Features.Projects;

/// <summary>
/// Comprueba que los handlers de negocio consultan al puerto y respetan su respuesta.
/// </summary>
/// <remarks>
/// Lo que se verifica aquí no es el modelo de autorización —eso lo cubre la suite de
/// conformidad— sino algo más básico y más fácil de romper por descuido: que el handler
/// <b>pregunta</b>, que pregunta por <b>la relación correcta</b>, y que <b>no modifica nada</b>
/// cuando la respuesta es DENY.
/// </remarks>
public class UpdateProjectCommandHandlerTests
{
    private readonly Fixture _fixture = new();
    private readonly Mock<IBusinessRepository<Project>> _projects = new();
    private readonly Mock<IAccessControlService> _accessControl = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private readonly Project _project = new() { Id = "alpha", Name = "Project Alpha" };

    public UpdateProjectCommandHandlerTests()
    {
        _currentUser.SetupGet(user => user.Id).Returns("juan");
        _currentUser.SetupGet(user => user.SubjectRef).Returns("user:juan");

        _projects
            .Setup(repository => repository.GetByIdAsync("alpha", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_project);

        _accessControl.SetupGet(service => service.Mode).Returns("InProcess");

        _accessControl
            .Setup(service => service.CanManyAsync(
                It.IsAny<IReadOnlyList<(string, string, string)>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<(string, string, string)> questions, CancellationToken _) =>
                questions.Select(_ => new AccessDecision(true, "ok", "InProcess", 0)).ToList());
    }

    private UpdateProjectCommandHandler CreateHandler() =>
        new(_projects.Object, _accessControl.Object, _currentUser.Object);

    [Fact]
    public async Task Handle_WhenTheAccessControlModuleAllows_UpdatesTheProject()
    {
        Allow("can_edit");

        var command = new UpdateProjectCommand("alpha", "Nuevo nombre", "Nueva descripción");

        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.Name.Should().Be("Nuevo nombre");
        _projects.Verify(repository => repository.UpdateAsync(_project, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTheAccessControlModuleDenies_ThrowsAndDoesNotTouchTheRepository()
    {
        Deny("can_edit", "No hay ninguna relación que lo conceda.");

        var command = new UpdateProjectCommand("alpha", "Nuevo nombre", null);

        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();

        // Lo importante: no se persiste nada. Un handler que comprueba después de modificar
        // no está protegiendo nada.
        _projects.Verify(
            repository => repository.UpdateAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_AsksForCanEditAndNotForAnUnderlyingRelation()
    {
        // El negocio debe preguntar por el permiso DERIVADO. Si preguntara por 'editor',
        // cambiar quién puede editar exigiría tocar el negocio, que es justo lo que se evita.
        Allow("can_edit");

        await CreateHandler().Handle(new UpdateProjectCommand("alpha", "x", null), CancellationToken.None);

        _accessControl.Verify(
            service => service.CanAsync("user:juan", "can_edit", "project:alpha", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTheProjectDoesNotExist_ThrowsNotFoundWithoutAskingForAuthorization()
    {
        _projects
            .Setup(repository => repository.GetByIdAsync("fantasma", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Project?)null);

        var act = () => CreateHandler().Handle(
            new UpdateProjectCommand("fantasma", "x", null), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();

        _accessControl.Verify(
            service => service.CanAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ForbiddenException_CarriesTheReasonGivenByTheModule()
    {
        const string reason = "Ana no es miembro de ningún equipo con acceso a este proyecto.";

        Deny("can_edit", reason);

        var act = () => CreateHandler().Handle(new UpdateProjectCommand("alpha", "x", null), CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Reason.Should().Be(reason);
    }

    private void Allow(string relation) =>
        _accessControl
            .Setup(service => service.CanAsync(
                It.IsAny<string>(), relation, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessDecision(true, "Concedido.", "InProcess", 0.5));

    private void Deny(string relation, string reason) =>
        _accessControl
            .Setup(service => service.CanAsync(
                It.IsAny<string>(), relation, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessDecision(false, reason, "InProcess", 0.5));
}

/// <summary>
/// El comando de publicar, que es el que usa la relación con intersección.
/// </summary>
/// <remarks>
/// Merece test propio por un motivo que no es obvio: el handler es <b>idéntico</b> al de
/// editar salvo por la cadena que pregunta. Toda la condición compuesta ("poder editar Y ser
/// miembro de la organización") vive en el modelo, no aquí. Este test lo deja documentado.
/// </remarks>
public class PublishProjectCommandHandlerTests
{
    private readonly Mock<IBusinessRepository<Project>> _projects = new();
    private readonly Mock<IAccessControlService> _accessControl = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    public PublishProjectCommandHandlerTests()
    {
        _currentUser.SetupGet(user => user.SubjectRef).Returns("user:sofia");

        _projects
            .Setup(repository => repository.GetByIdAsync("alpha", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = "alpha", Name = "Project Alpha" });

        _accessControl.SetupGet(service => service.Mode).Returns("InProcess");
    }

    [Fact]
    public async Task Handle_ForAnExternalCollaboratorWhoCanEditButIsNotAMember_Throws()
    {
        // Sofía puede editar Alpha, pero can_publish exige además ser miembro de Acme.
        _accessControl
            .Setup(service => service.CanAsync(
                "user:sofia", "can_publish", "project:alpha", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessDecision(
                false,
                "Se exige poder editar Y ser miembro de la organización; falta lo segundo.",
                "InProcess",
                1.2));

        var handler = new PublishProjectCommandHandler(
            _projects.Object, _accessControl.Object, _currentUser.Object);

        var act = () => handler.Handle(new PublishProjectCommand("alpha"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .Where(exception => exception.Relation == "can_publish");
    }
}
