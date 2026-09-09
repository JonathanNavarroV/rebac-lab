using System.Diagnostics.CodeAnalysis;
using Playground.AccessControl.Api.Mapping;
using Playground.AccessControl.Application.Authorization.Seed;
using Playground.AccessControl.Domain.Abstractions;
using Playground.AccessControl.Domain.Model;
using Playground.Contracts.AccessControl;

namespace Playground.AccessControl.Api.Endpoints;

/// <summary>
/// Grafo de relaciones, auditoría y casos guiados.
/// </summary>
[ExcludeFromCodeCoverage]
public static class GraphAuditAndCaseEndpoints
{
    public static RouteGroupBuilder MapGraphEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/graph", GetGraphAsync)
            .WithTags("Graph")
            .WithName("GetGraph")
            .WithSummary("El grafo de autorización completo")
            .WithDescription(
                "Nodos y aristas construidos a partir de las tuplas. Ojo con la interpretación: "
                + "este NO es el diagrama de entidades de la aplicación, es el grafo por el que "
                + "viaja el acceso. Un nodo 'team:backend#member' no es una entidad del negocio, "
                + "es un conjunto.")
            .Produces<GraphResponse>();

        return group;
    }

    public static RouteGroupBuilder MapAuditEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/audit", GetAuditAsync)
            .WithTags("Audit")
            .WithName("GetAudit")
            .WithSummary("Decisiones registradas, de la más reciente a la más antigua")
            .WithDescription(
                "Cada entrada guarda el camino de autorización serializado y el modelo con el que "
                + "se evaluó. Sin esas dos cosas, una auditoría de autorización no permite "
                + "responder la única pregunta que se hace de verdad: por qué.")
            .Produces<IReadOnlyList<AuditEntryDto>>();

        return group;
    }

    public static RouteGroupBuilder MapGuidedCaseEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/cases", GetGuidedCasesAsync)
            .WithTags("Guided cases")
            .WithName("GetGuidedCases")
            .WithSummary("Los casos guiados del laboratorio, con su resultado actual")
            .WithDescription(
                "Cada caso trae la pregunta, el resultado esperado, el resultado real de ahora "
                + "mismo y qué enseña. Si has cambiado relaciones y algún caso deja de coincidir "
                + "con lo esperado, se marca — es fácil romper sin querer el escenario que estabas "
                + "estudiando.")
            .Produces<IReadOnlyList<GuidedCaseDto>>();

        return group;
    }

    private static async Task<IResult> GetGraphAsync(
        IRelationshipTupleStore tuples,
        CancellationToken cancellationToken,
        string? focus = null,
        int depth = 3)
    {
        var all = await tuples.ReadAsync(new TupleFilter(), cancellationToken);

        var relevant = string.IsNullOrWhiteSpace(focus)
            ? all
            : FilterAroundFocus(all, focus.Trim(), Math.Clamp(depth, 1, 10));

        var names = PlaygroundScenario.AllEntities()
            .ToDictionary(entity => $"{entity.Type}:{entity.Id}", entity => entity.DisplayName, StringComparer.Ordinal);

        PlaygroundScenario.Users.ToList()
            .ForEach(user => names[$"user:{user.Id}"] = user.DisplayName);

        var nodes = new Dictionary<string, GraphNodeDto>(StringComparer.Ordinal);

        void AddNode(string id, string type, string label)
        {
            if (!nodes.ContainsKey(id))
                nodes[id] = new GraphNodeDto(id, label, type, names.GetValueOrDefault(id));
        }

        var edges = new List<GraphEdgeDto>();

        foreach (var tuple in relevant)
        {
            var objectId = tuple.Object.ToString();
            AddNode(objectId, tuple.Object.Type, names.GetValueOrDefault(objectId, objectId));

            // Un sujeto userset se dibuja como su PROPIO nodo ('team:backend#member') además
            // del objeto del que sale ('team:backend'). Son cosas distintas y confundirlas es
            // justo lo que impide entender el grafo: uno es una entidad, el otro un conjunto.
            var subjectId = tuple.Subject.ToString();
            var subjectBase = tuple.Subject.AsObject().ToString();

            AddNode(subjectBase, tuple.Subject.Type, names.GetValueOrDefault(subjectBase, subjectBase));

            if (tuple.Subject.IsUserset)
            {
                AddNode(subjectId, $"{tuple.Subject.Type}-userset", subjectId);

                // Arista implícita del objeto a su userset, para que el grafo se lea entero.
                if (edges.All(edge => edge.Source != subjectBase || edge.Target != subjectId))
                {
                    edges.Add(new GraphEdgeDto(
                        -Math.Abs((long)subjectId.GetHashCode()),
                        subjectBase,
                        subjectId,
                        tuple.Subject.Relation!,
                        Userset: true,
                        Wildcard: false,
                        $"{subjectBase} define el conjunto «{tuple.Subject.Relation}»"));
                }
            }

            edges.Add(new GraphEdgeDto(
                tuple.Id,
                subjectId,
                objectId,
                tuple.Relation,
                tuple.Subject.IsUserset,
                tuple.Subject.IsWildcard,
                tuple.ToString()));
        }

        return Results.Ok(new GraphResponse(nodes.Values.ToList(), edges));
    }

    /// <summary>
    /// Recorta el grafo alrededor de un nodo, expandiendo por vecindad hasta la profundidad
    /// indicada.
    /// </summary>
    /// <remarks>
    /// Hace falta porque el grafo completo se vuelve ilegible en cuanto hay unas decenas de
    /// tuplas — que en un sistema real son millones. Es la misma razón por la que ningún
    /// producto muestra "el grafo de permisos": solo se puede mirar un vecindario.
    /// </remarks>
    private static List<RelationshipTuple> FilterAroundFocus(
        IReadOnlyList<RelationshipTuple> all,
        string focus,
        int depth)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal) { focus };
        var selected = new List<RelationshipTuple>();
        var selectedIds = new HashSet<long>();

        for (var level = 0; level < depth; level++)
        {
            var added = false;

            foreach (var tuple in all)
            {
                var objectId = tuple.Object.ToString();
                var subjectId = tuple.Subject.ToString();
                var subjectBase = tuple.Subject.AsObject().ToString();

                var touches = reached.Contains(objectId) || reached.Contains(subjectId) || reached.Contains(subjectBase);

                if (!touches || !selectedIds.Add(tuple.Id))
                    continue;

                selected.Add(tuple);
                added |= reached.Add(objectId) | reached.Add(subjectId) | reached.Add(subjectBase);
            }

            if (!added)
                break;
        }

        return selected;
    }

    private static async Task<IResult> GetAuditAsync(
        IDecisionAuditStore audit,
        CancellationToken cancellationToken,
        string? subject = null,
        string? @object = null,
        bool? allowed = null,
        int take = 100)
    {
        var entries = await audit.QueryAsync(
            Normalize(subject), Normalize(@object), allowed, take, cancellationToken);

        return Results.Ok(entries.Select(entry => entry.ToDto()).ToList());
    }

    private static async Task<IResult> GetGuidedCasesAsync(
        IAccessControlEngine engine,
        CancellationToken cancellationToken)
    {
        var results = new List<GuidedCaseDto>();

        foreach (var scenario in PlaygroundScenario.Cases)
        {
            bool? actual = null;

            try
            {
                var decision = await engine.CheckAsync(
                    SubjectRef.Parse(scenario.Subject),
                    scenario.Relation,
                    ObjectRef.Parse(scenario.Object),
                    cancellationToken: cancellationToken);

                actual = decision.Allowed;
            }
            catch (Exception)
            {
                // Si el modelo publicado ya no tiene la relación del caso, el caso no se puede
                // evaluar. No es un error del servidor: es información útil para quien está
                // experimentando con modelos distintos.
                actual = null;
            }

            results.Add(new GuidedCaseDto(
                scenario.Code,
                scenario.Title,
                scenario.Subject,
                scenario.Relation,
                scenario.Object,
                scenario.ExpectedAllowed,
                actual,
                actual == scenario.ExpectedAllowed,
                scenario.WhatItTeaches,
                scenario.WhyRbacStruggles,
                scenario.MinimumPaths));
        }

        return Results.Ok(results);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
