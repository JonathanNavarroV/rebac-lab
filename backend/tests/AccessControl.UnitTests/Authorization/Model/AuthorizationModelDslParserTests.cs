using FluentAssertions;
using Playground.AccessControl.Application.Authorization.Model;
using Playground.AccessControl.Domain.Exceptions;
using Playground.AccessControl.Domain.Model;

namespace AccessControl.UnitTests.Authorization.Model;

public class AuthorizationModelDslParserTests
{
    private readonly AuthorizationModelDslParser _parser = new();

    // ── Los modelos del laboratorio compilan ────────────────────────────────────

    [Theory]
    [InlineData(nameof(PlaygroundModels.Full))]
    [InlineData(nameof(PlaygroundModels.WithoutFolderInheritance))]
    [InlineData(nameof(PlaygroundModels.RbacEquivalent))]
    public void Parse_PlaygroundModel_CompilesWithoutErrors(string modelName)
    {
        var dsl = modelName switch
        {
            nameof(PlaygroundModels.Full) => PlaygroundModels.Full,
            nameof(PlaygroundModels.WithoutFolderInheritance) => PlaygroundModels.WithoutFolderInheritance,
            nameof(PlaygroundModels.RbacEquivalent) => PlaygroundModels.RbacEquivalent,
            _ => throw new ArgumentOutOfRangeException(nameof(modelName)),
        };

        var act = () => _parser.Parse(dsl);

        act.Should().NotThrow<ModelValidationException>();
    }

    [Fact]
    public void Parse_FullModel_DeclaresTheSevenPlaygroundTypes()
    {
        var model = _parser.Parse(PlaygroundModels.Full);

        model.Types.Keys.Should().BeEquivalentTo(
            "user", "organization", "team", "group", "project", "folder", "resource");
    }

    [Fact]
    public void Parse_SameDslTwice_ProducesTheSameModelId()
    {
        // El id es un hash del contenido normalizado: republicar un modelo idéntico no debe
        // generar una versión nueva ni invalidar lo ya auditado.
        var first = _parser.Parse(PlaygroundModels.Full);
        var second = _parser.Parse("\n\n" + PlaygroundModels.Full + "   \n");

        second.Id.Should().Be(first.Id);
    }

    // ── Cada variante de regla se compila al nodo correcto ──────────────────────

    [Fact]
    public void Parse_DirectAssignmentList_ProducesThisWithEverySubjectKind()
    {
        var model = _parser.Parse(
            """
            model
              schema 1.1
            type user
            type team
              relations
                define member: [user]
            type project
              relations
                define viewer: [user, team#member, user:*]
            """);

        var rewrite = model.Types["project"].Relations["viewer"].Rewrite;

        var direct = rewrite.Should().BeOfType<UsersetRewrite.This>().Subject;
        direct.Allowed.Should().HaveCount(3);
        direct.Allowed[0].Should().Be(UsersetRewrite.DirectAssignment.OfType("user"));
        direct.Allowed[1].Should().Be(UsersetRewrite.DirectAssignment.OfUserset("team", "member"));
        direct.Allowed[2].Should().Be(UsersetRewrite.DirectAssignment.OfWildcard("user"));
    }

    [Fact]
    public void Parse_RelationFromTupleset_ProducesTupleToUserset()
    {
        var model = _parser.Parse(
            """
            model
              schema 1.1
            type user
            type organization
              relations
                define admin: [user]
            type project
              relations
                define parent: [organization]
                define can_edit: admin from parent
            """);

        var rewrite = model.Types["project"].Relations["can_edit"].Rewrite;

        rewrite.Should().BeOfType<UsersetRewrite.TupleToUserset>()
            .Which.Should().BeEquivalentTo(new UsersetRewrite.TupleToUserset("parent", "admin"));
    }

    [Fact]
    public void Parse_MixedOperators_AppliesAndOverOrOverButNot()
    {
        // 'a or b and c but not d' debe leerse '(a or (b and c)) but not d'.
        var model = _parser.Parse(
            """
            model
              schema 1.1
            type user
            type project
              relations
                define a: [user]
                define b: [user]
                define c: [user]
                define d: [user]
                define combined: a or b and c but not d
            """);

        var rewrite = model.Types["project"].Relations["combined"].Rewrite;

        var exclusion = rewrite.Should().BeOfType<UsersetRewrite.Exclusion>().Subject;
        exclusion.Subtract.Should().BeOfType<UsersetRewrite.ComputedUserset>()
            .Which.Relation.Should().Be("d");

        var union = exclusion.Base.Should().BeOfType<UsersetRewrite.Union>().Subject;
        union.Children.Should().HaveCount(2);
        union.Children[0].Should().BeOfType<UsersetRewrite.ComputedUserset>();
        union.Children[1].Should().BeOfType<UsersetRewrite.Intersection>();
    }

    [Fact]
    public void Parse_Parentheses_OverrideDefaultPrecedence()
    {
        var model = _parser.Parse(
            """
            model
              schema 1.1
            type user
            type project
              relations
                define a: [user]
                define b: [user]
                define c: [user]
                define combined: (a or b) and c
            """);

        var rewrite = model.Types["project"].Relations["combined"].Rewrite;

        var intersection = rewrite.Should().BeOfType<UsersetRewrite.Intersection>().Subject;
        intersection.Children[0].Should().BeOfType<UsersetRewrite.Union>();
    }

    [Fact]
    public void Parse_CommentAboveDefine_IsAttachedToTheRelation()
    {
        // Los comentarios no son decoración: alimentan las explicaciones del Explorer.
        var model = _parser.Parse(
            """
            model
              schema 1.1
            type user
            type project
              relations
                // Quien lo creó.
                define owner: [user]
                define editor: [user]   // Puede modificar pero no borrar.
            """);

        model.Types["project"].Relations["owner"].Comment.Should().Be("Quien lo creó.");
        model.Types["project"].Relations["editor"].Comment.Should().Be("Puede modificar pero no borrar.");
    }

    // ── El modelo derivado se calcula bien ──────────────────────────────────────

    [Fact]
    public void GetContributingRelations_ForDerivedPermission_IncludesEveryRelationThatCanGrantIt()
    {
        // Es la consulta que hace viable la expansión inversa de ListObjects: para saber qué
        // proyectos puede ver alguien hay que saber qué relaciones alimentan can_view.
        var model = _parser.Parse(PlaygroundModels.Full);

        var contributing = model.GetContributingRelations("project", "can_view");

        contributing.Should().Contain(["can_view", "viewer", "can_edit", "editor", "owner", "parent"]);
    }

    [Fact]
    public void IsDirectlyAssignable_DistinguishesFactsFromConclusions()
    {
        var model = _parser.Parse(PlaygroundModels.Full);
        var project = model.Types["project"];

        // 'editor' es un hecho: se escribe como tupla.
        project.Relations["editor"].IsDirectlyAssignable.Should().BeTrue();

        // 'can_edit' es una conclusión: escribir 'project:alpha#can_edit@user:juan' sería
        // materializar un permiso calculado, que es el error que ReBAC evita.
        project.Relations["can_edit"].IsDirectlyAssignable.Should().BeFalse();
    }

    // ── Validación: los errores que de verdad se cometen ────────────────────────

    [Fact]
    public void Parse_TypoInComputedRelation_IsRejectedWithTheAvailableRelations()
    {
        // Sin esta validación, 'editorr' sería una rama que siempre deniega en silencio.
        var act = () => _parser.Parse(
            """
            model
              schema 1.1
            type user
            type project
              relations
                define editor: [user]
                define can_edit: editorr
            """);

        act.Should().Throw<ModelValidationException>()
            .Which.Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("editorr").And.Contain("no existe");
    }

    [Fact]
    public void Parse_UndeclaredSubjectType_IsRejected()
    {
        var act = () => _parser.Parse(
            """
            model
              schema 1.1
            type user
            type project
              relations
                define viewer: [teams#member]
            """);

        act.Should().Throw<ModelValidationException>()
            .Which.Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("teams");
    }

    [Fact]
    public void Parse_TupleToUsersetOverMissingTupleset_IsRejected()
    {
        // 'from parent' sin haber definido 'parent' deja la herencia desconectada.
        var act = () => _parser.Parse(
            """
            model
              schema 1.1
            type user
            type project
              relations
                define viewer: [user]
                define can_view: viewer or can_view from parent
            """);

        act.Should().Throw<ModelValidationException>()
            .Which.Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("parent");
    }

    [Fact]
    public void Parse_TupleToUsersetWhoseRelationIsMissingInParent_IsRejected()
    {
        var act = () => _parser.Parse(
            """
            model
              schema 1.1
            type user
            type organization
              relations
                define member: [user]
            type project
              relations
                define parent: [organization]
                define can_edit: admin from parent
            """);

        act.Should().Throw<ModelValidationException>()
            .Which.Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("admin");
    }

    [Fact]
    public void Parse_SelfReferencingComputedUserset_IsRejectedWithTheFromHint()
    {
        // Es el error clásico al intentar hacer herencia: se escribe la recursión sobre el
        // mismo objeto en lugar de subir por 'parent'.
        var act = () => _parser.Parse(
            """
            model
              schema 1.1
            type user
            type folder
              relations
                define viewer: [user]
                define can_view: can_view
            """);

        act.Should().Throw<ModelValidationException>()
            .Which.Errors.Should().ContainSingle()
            .Which.Hint.Should().Contain("from");
    }

    [Fact]
    public void Parse_DuplicateRelation_IsRejected()
    {
        var act = () => _parser.Parse(
            """
            model
              schema 1.1
            type user
            type project
              relations
                define viewer: [user]
                define viewer: [user]
            """);

        act.Should().Throw<ModelValidationException>()
            .Which.Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("dos veces");
    }

    [Fact]
    public void Parse_ConcreteObjectInAssignmentList_IsRejectedWithAnExplanation()
    {
        // Confundir el TIPO con un objeto concreto es el error más habitual al empezar:
        // el modelo habla de tipos, las tuplas de objetos.
        var act = () => _parser.Parse(
            """
            model
              schema 1.1
            type user
            type organization
              relations
                define member: [user]
            type project
              relations
                define viewer: [organization:acme]
            """);

        act.Should().Throw<ModelValidationException>()
            .Which.Errors.Should().ContainSingle()
            .Which.Hint.Should().Contain("TIPO");
    }

    [Fact]
    public void Parse_SeveralBrokenRelations_ReportsAllErrorsAtOnce()
    {
        // Acumular errores en lugar de fallar en el primero: al escribir un modelo a mano se
        // cometen varios fallos a la vez y verlos juntos ahorra muchas iteraciones.
        var act = () => _parser.Parse(
            """
            model
              schema 1.1
            type user
            type project
              relations
                define viewer: [nope]
                define can_view: missing
                define can_edit: other from nowhere
            """);

        act.Should().Throw<ModelValidationException>()
            .Which.Errors.Should().HaveCountGreaterThanOrEqualTo(3);
    }
}
