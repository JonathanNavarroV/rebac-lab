using System.Reflection;
using FluentAssertions;
using Playground.Business.Application.Features.Projects.Queries.GetProjects;
using Playground.Business.Domain.Authorization;
using Playground.Business.Domain.Entities;

namespace Business.UnitTests.Architecture;

/// <summary>
/// Tests de arquitectura que protegen la frontera entre el negocio y el control de acceso.
/// </summary>
/// <remarks>
/// <para>
/// Son los tests más aburridos del repositorio y los que más valor defienden. La separación
/// entre negocio y autorización es la tesis entera del proyecto, y es exactamente el tipo de
/// cosa que se erosiona sola: un día alguien necesita "solo mirar una tupla" desde un handler,
/// añade la referencia, y a partir de ahí el modelo de autorización deja de poder cambiar sin
/// tocar el negocio.
/// </para>
/// <para>
/// Estos tests fallan en cuanto eso pasa.
/// </para>
/// </remarks>
public class AuthorizationBoundaryTests
{
    private static Assembly BusinessApplication => typeof(GetProjectsQuery).Assembly;

    private static Assembly BusinessDomain => typeof(BusinessEntity).Assembly;

    [Fact]
    public void BusinessApplication_DoesNotReferenceTheAccessControlModule()
    {
        var referenced = BusinessApplication
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToList();

        referenced.Should().NotContain(
            name => name.StartsWith("Playground.AccessControl", StringComparison.Ordinal),
            because: "los handlers de negocio solo pueden autorizar a través del puerto "
                     + "IAccessControlService. Si esta referencia aparece, alguien ha metido "
                     + "conocimiento del modelo de autorización dentro del negocio.");
    }

    [Fact]
    public void BusinessDomain_DoesNotReferenceTheAccessControlModule()
    {
        var referenced = BusinessDomain
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToList();

        referenced.Should().NotContain(
            name => name.StartsWith("Playground.AccessControl", StringComparison.Ordinal));
    }

    [Fact]
    public void BusinessEntities_DoNotExposeAnyPermissionOrOwnershipProperty()
    {
        // En cuanto una entidad de negocio tiene un OwnerId o un AccessLevel, la autorización
        // empieza a vivir en dos sitios a la vez: en las tuplas y en la propia entidad. Y el
        // día que discrepen, el bug es imposible de encontrar.
        var forbidden = new[] { "owner", "permission", "role", "acl", "accesslevel", "ispublic", "visibility" };

        var offenders = BusinessDomain
            .GetTypes()
            .Where(type => type.IsSubclassOf(typeof(BusinessEntity)))
            .SelectMany(type => type.GetProperties().Select(property => new
            {
                Type = type.Name,
                Property = property.Name,
            }))
            .Where(entry => forbidden.Any(word =>
                entry.Property.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        offenders.Should().BeEmpty(
            because: "ninguna entidad de negocio debe saber quién puede tocarla. Eso vive en las "
                     + "tuplas del módulo de control de acceso.");
    }

    [Fact]
    public void AccessControlPort_ExposesOnlyQuestionsAndFactDeclarations()
    {
        // El puerto tiene que seguir siendo estrecho. Si aparece un GetPermissions, un
        // GetRoles o un GrantPermission, el negocio ha empezado a razonar sobre autorización.
        var methods = typeof(IAccessControlService)
            .GetMethods()
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .ToList();

        methods.Should().BeEquivalentTo(
            ["CanAsync", "CanManyAsync", "ListAuthorizedAsync", "WriteRelationshipAsync"],
            because: "el negocio solo puede preguntar si puede, pedir la lista de lo que puede, "
                     + "y declarar hechos. No puede conceder permisos.");
    }
}
