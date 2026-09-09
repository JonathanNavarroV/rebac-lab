using System.Diagnostics.CodeAnalysis;
using Playground.AccessControl.Api.Helpers;
using Playground.AccessControl.Api.Mapping;
using Playground.AccessControl.Application.Authorization.Seed;
using Playground.AccessControl.Domain.Abstractions;
using Playground.AccessControl.Domain.Model;
using Playground.Contracts.AccessControl;

namespace Playground.AccessControl.Api.Endpoints;

/// <summary>
/// Alta, baja y consulta de relaciones. La única forma de cambiar quién puede hacer qué.
/// </summary>
/// <remarks>
/// No hay ningún endpoint de "conceder permiso" ni de "asignar rol". Solo se escriben y se
/// borran hechos. Todo lo que alguien puede hacer en el sistema es consecuencia de estas
/// filas y del modelo.
/// </remarks>
[ExcludeFromCodeCoverage]
public static class RelationshipEndpoints
{
    public static RouteGroupBuilder MapRelationshipEndpoints(this RouteGroupBuilder group)
    {
        var relationships = group.MapGroup("/relationships").WithTags("Relationships");

        relationships.MapGet("/", GetRelationshipsAsync)
            .WithName("GetRelationships")
            .WithSummary("Lista las relaciones existentes, con filtros opcionales")
            .Produces<IReadOnlyList<RelationshipDto>>();

        relationships.MapPost("/", CreateRelationshipAsync)
            .WithName("CreateRelationship")
            .WithSummary("Crea una relación y muestra qué decisiones ha cambiado")
            .WithDescription(
                "Además de crear la tupla, re-evalúa los casos guiados antes y después y devuelve "
                + "los que han cambiado de respuesta. Es la forma más directa de ver que UNA sola "
                + "relación puede alterar muchas decisiones a la vez.")
            .Produces<RelationshipMutationResponse>()
            .ProducesValidationProblem();

        relationships.MapDelete("/{id:long}", DeleteRelationshipByIdAsync)
            .WithName("DeleteRelationship")
            .WithSummary("Borra una relación por identificador")
            .Produces<RelationshipMutationResponse>()
            .Produces(StatusCodes.Status404NotFound);

        relationships.MapDelete("/", DeleteRelationshipByKeyAsync)
            .WithName("DeleteRelationshipByTuple")
            .WithSummary("Borra una relación por su notación textual")
            .Produces<RelationshipMutationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        return group;
    }

    private static async Task<IResult> GetRelationshipsAsync(
        IRelationshipTupleStore tuples,
        CancellationToken cancellationToken,
        string? objectType = null,
        string? objectId = null,
        string? relation = null,
        string? subjectType = null,
        string? subjectId = null)
    {
        var filter = new TupleFilter
        {
            ObjectType = Normalize(objectType),
            ObjectId = Normalize(objectId),
            Relation = Normalize(relation),
            SubjectType = Normalize(subjectType),
            SubjectId = Normalize(subjectId),
        };

        var result = await tuples.ReadAsync(filter, cancellationToken);

        return Results.Ok(result.Select(tuple => tuple.ToDto()).ToList());
    }

    private static async Task<IResult> CreateRelationshipAsync(
        CreateRelationshipRequest request,
        IRelationshipTupleStore tuples,
        IAccessControlEngine engine,
        IAuthorizationModelStore models,
        CancellationToken cancellationToken)
    {
        var errors = new RequestParsing.ValidationCollector();
        TupleKey? key;

        if (!string.IsNullOrWhiteSpace(request.Tuple))
        {
            key = RequestParsing.ParseTuple(request.Tuple, nameof(request.Tuple), errors);
        }
        else
        {
            var @object = RequestParsing.ParseObject(request.Object, nameof(request.Object), errors);
            var relation = RequestParsing.ParseRelation(request.Relation, nameof(request.Relation), errors);
            var subject = RequestParsing.ParseSubject(request.Subject, nameof(request.Subject), errors);

            key = @object is not null && relation is not null && subject is not null
                ? new TupleKey(@object, relation, subject)
                : null;
        }

        if (errors.HasErrors || key is null)
            return errors.ToResult();

        // Validación contra el modelo. No es opcional: escribir una tupla que el modelo no
        // admite crea un dato que nunca concederá nada y que además desaparecerá de la vista
        // sin avisar. Vale mucho más rechazarla aquí y explicar por qué.
        var model = await models.GetLatestAsync(cancellationToken);

        if (!model.TryGetRelation(key.Object.Type, key.Relation, out var definition))
        {
            errors.Add("Relation",
                $"El tipo '{key.Object.Type}' no tiene la relación '{key.Relation}' en el modelo vigente. "
                + $"Relaciones disponibles: {AvailableRelations(model, key.Object.Type)}.");

            return errors.ToResult();
        }

        if (!definition.IsDirectlyAssignable)
        {
            errors.Add("Relation",
                $"'{key.Relation}' es una relación DERIVADA ({definition.Rewrite.ToDsl()}), no un hecho que "
                + "se pueda escribir. Escribirla sería materializar un permiso calculado, que es "
                + "justamente lo que ReBAC evita. Escribe en su lugar una de las relaciones que la "
                + "alimentan.");

            return errors.ToResult();
        }

        if (!definition.DirectAssignments.Any(assignment => assignment.Accepts(key.Subject)))
        {
            errors.Add("Subject",
                $"El modelo no admite sujetos como '{key.Subject}' en '{key.Object.Type}.{key.Relation}'. "
                + $"Admitidos: {string.Join(", ", definition.DirectAssignments.Select(assignment => assignment.ToDsl()).Distinct())}.");

            return errors.ToResult();
        }

        var before = await EvaluateGuidedCasesAsync(engine, cancellationToken);

        var existing = await tuples.ReadAsync(
            new TupleFilter
            {
                ObjectType = key.Object.Type,
                ObjectId = key.Object.Id,
                Relation = key.Relation,
                SubjectType = key.Subject.Type,
                SubjectId = key.Subject.Id,
                SubjectRelation = key.Subject.Relation,
                MatchSubjectRelationExactly = true,
            },
            cancellationToken);

        var created = await tuples.WriteAsync(key, cancellationToken);

        var after = await EvaluateGuidedCasesAsync(engine, cancellationToken);

        return Results.Ok(new RelationshipMutationResponse(
            created.ToDto(),
            Created: existing.Count == 0,
            Diff(before, after)));
    }

    private static async Task<IResult> DeleteRelationshipByIdAsync(
        long id,
        IRelationshipTupleStore tuples,
        IAccessControlEngine engine,
        CancellationToken cancellationToken)
    {
        var before = await EvaluateGuidedCasesAsync(engine, cancellationToken);
        var deleted = await tuples.DeleteAsync(id, cancellationToken);

        if (!deleted)
            return Results.NotFound();

        var after = await EvaluateGuidedCasesAsync(engine, cancellationToken);

        return Results.Ok(new RelationshipMutationResponse(null, Created: false, Diff(before, after)));
    }

    private static async Task<IResult> DeleteRelationshipByKeyAsync(
        string tuple,
        IRelationshipTupleStore tuples,
        IAccessControlEngine engine,
        CancellationToken cancellationToken)
    {
        var errors = new RequestParsing.ValidationCollector();
        var key = RequestParsing.ParseTuple(tuple, nameof(tuple), errors);

        if (errors.HasErrors || key is null)
            return errors.ToResult();

        var before = await EvaluateGuidedCasesAsync(engine, cancellationToken);
        var deleted = await tuples.DeleteAsync(key, cancellationToken);

        if (!deleted)
            return Results.NotFound();

        var after = await EvaluateGuidedCasesAsync(engine, cancellationToken);

        return Results.Ok(new RelationshipMutationResponse(null, Created: false, Diff(before, after)));
    }

    /// <summary>
    /// Evalúa todos los casos guiados. Se ejecuta antes y después de cada escritura para poder
    /// mostrar el efecto de la tupla.
    /// </summary>
    /// <remarks>
    /// Son unas pocas decenas de Checks y en un laboratorio es perfectamente asumible. En un
    /// sistema real esto no se haría jamás en el camino de una escritura: el efecto de una
    /// tupla sobre las decisiones existentes es, en el caso general, incalculable sin recorrer
    /// todo el grafo. Es también la razón por la que invalidar cachés de autorización es tan
    /// difícil — ver <c>docs/06</c>.
    /// </remarks>
    private static async Task<Dictionary<string, (bool Allowed, string Reason)>> EvaluateGuidedCasesAsync(
        IAccessControlEngine engine,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, (bool, string)>(StringComparer.Ordinal);

        foreach (var scenario in PlaygroundScenario.Cases)
        {
            var decision = await engine.CheckAsync(
                SubjectRef.Parse(scenario.Subject),
                scenario.Relation,
                ObjectRef.Parse(scenario.Object),
                cancellationToken: cancellationToken);

            results[scenario.Code] = (decision.Allowed, decision.Reason);
        }

        return results;
    }

    private static IReadOnlyList<DecisionChangeDto> Diff(
        Dictionary<string, (bool Allowed, string Reason)> before,
        Dictionary<string, (bool Allowed, string Reason)> after) =>
        PlaygroundScenario.Cases
            .Where(scenario => before.TryGetValue(scenario.Code, out var previous)
                               && after.TryGetValue(scenario.Code, out var current)
                               && previous.Allowed != current.Allowed)
            .Select(scenario => new DecisionChangeDto(
                scenario.Subject,
                scenario.Relation,
                scenario.Object,
                before[scenario.Code].Allowed,
                after[scenario.Code].Allowed,
                after[scenario.Code].Reason))
            .ToList();

    private static string AvailableRelations(AuthorizationModel model, string type) =>
        model.TryGetType(type, out var definition)
            ? string.Join(", ", definition.Relations.Keys.Order())
            : $"(el tipo '{type}' no existe en el modelo)";

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
