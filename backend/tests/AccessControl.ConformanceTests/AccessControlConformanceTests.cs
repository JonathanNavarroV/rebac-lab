using FluentAssertions;
using Playground.AccessControl.Application.Authorization.Model;
using Playground.AccessControl.Application.Authorization.Seed;
using Playground.AccessControl.Domain.Abstractions;
using Playground.AccessControl.Domain.Checking;
using Playground.AccessControl.Domain.Model;

namespace AccessControl.ConformanceTests;

/// <summary>
/// Suite de conformidad: qué debe responder <b>cualquier</b> motor que se diga compatible
/// con un modelo estilo Zanzibar.
/// </summary>
/// <remarks>
/// <para>
/// Está escrita contra <see cref="IAccessControlEngine"/> y no contra la implementación, así
/// que si algún día se añade un adaptador contra OpenFGA o SpiceDB basta heredar de aquí para
/// saber si responde igual. Ese es también el argumento de que el motor propio de este
/// laboratorio no es un juguete arbitrario: cumple una especificación comprobable.
/// </para>
/// <para>
/// Los casos vienen de <see cref="PlaygroundScenario.Cases"/>, que es la misma lista que
/// alimenta los botones del frontend y los ejemplos de la documentación. Si alguien cambia el
/// modelo y rompe un caso, se enteran los tres a la vez.
/// </para>
/// </remarks>
public abstract class AccessControlConformanceTests
{
    /// <summary>
    /// Construye el motor bajo prueba con el modelo y las tuplas indicadas.
    /// </summary>
    protected abstract Task<IAccessControlEngine> CreateEngineAsync(
        string dsl,
        IEnumerable<string> tuples);

    private Task<IAccessControlEngine> CreatePlaygroundEngineAsync() =>
        CreateEngineAsync(PlaygroundModels.Full, PlaygroundScenario.Tuples);

    // ═══════════════════════════════════════════════════════════════════════════
    // Los casos guiados del laboratorio
    // ═══════════════════════════════════════════════════════════════════════════

    public static TheoryData<string> CaseCodes()
    {
        var data = new TheoryData<string>();
        PlaygroundScenario.Cases.ToList().ForEach(scenario => data.Add(scenario.Code));
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseCodes))]
    public async Task Check_GuidedCase_MatchesTheExpectedDecision(string caseCode)
    {
        var scenario = PlaygroundScenario.Cases.Single(item => item.Code == caseCode);
        var engine = await CreatePlaygroundEngineAsync();

        var decision = await engine.CheckAsync(
            SubjectRef.Parse(scenario.Subject),
            scenario.Relation,
            ObjectRef.Parse(scenario.Object),
            CheckOptions.Explain);

        decision.Allowed.Should().Be(
            scenario.ExpectedAllowed,
            because: $"caso {scenario.Code} — {scenario.Title}. {scenario.WhatItTeaches}"
                     + Environment.NewLine
                     + "Traza de la evaluación:"
                     + Environment.NewLine
                     + decision.Trace?.ToTraceString());
    }

    [Theory]
    [MemberData(nameof(CaseCodes))]
    public async Task Check_GuidedCase_FindsAtLeastTheExpectedNumberOfPaths(string caseCode)
    {
        var scenario = PlaygroundScenario.Cases.Single(item => item.Code == caseCode);

        if (!scenario.ExpectedAllowed)
            return;

        var engine = await CreatePlaygroundEngineAsync();

        var decision = await engine.CheckAsync(
            SubjectRef.Parse(scenario.Subject),
            scenario.Relation,
            ObjectRef.Parse(scenario.Object),
            CheckOptions.Explain);

        decision.Paths.Should().HaveCountGreaterThanOrEqualTo(
            scenario.MinimumPaths,
            because: $"caso {scenario.Code} — {scenario.Title}");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Cómo cambian las respuestas al tocar UNA relación
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Check_WithoutTheTeamMembershipTuple_JuanLosesEditButKeepsView()
    {
        // Es la pregunta del punto 20 del laboratorio: "¿qué pasa si saco a Juan del equipo?".
        // Y la respuesta es la lección más importante sobre los múltiples caminos: pierde lo
        // que solo tenía por el equipo, y conserva lo que tenía por otra vía.
        var withoutMembership = PlaygroundScenario.Tuples
            .Where(tuple => tuple != "team:backend#member@user:juan");

        var engine = await CreateEngineAsync(PlaygroundModels.Full, withoutMembership);

        var canEdit = await engine.CheckAsync(
            SubjectRef.User("juan"), Relations.CanEdit, ObjectRef.Parse("project:alpha"), CheckOptions.Explain);

        var canView = await engine.CheckAsync(
            SubjectRef.User("juan"), Relations.CanView, ObjectRef.Parse("project:alpha"), CheckOptions.Explain);

        canEdit.Allowed.Should().BeFalse(
            "su edición venía únicamente de ser miembro de team:backend");

        canView.Allowed.Should().BeTrue(
            "sigue teniendo la tupla 'project:alpha#viewer@user:juan', que es un camino independiente. "
            + "Quien creyera que sacarle del equipo le retira el acceso, se equivocaba.");
    }

    [Fact]
    public async Task Check_WhenTheTeamLosesEditorOnTheProject_TheWholeTeamLosesItAtOnce()
    {
        // "¿Qué pasa si el equipo deja de tener acceso?" — una sola tupla borrada afecta a
        // todos los miembros del equipo, presentes y futuros, y a todo lo que cuelga del
        // proyecto. Es la contrapartida de la comodidad: el radio de una tupla es enorme.
        var withoutTeamEditor = PlaygroundScenario.Tuples
            .Where(tuple => tuple != "project:alpha#editor@team:backend#member");

        var engine = await CreateEngineAsync(PlaygroundModels.Full, withoutTeamEditor);

        var canEditProject = await engine.CheckAsync(
            SubjectRef.User("juan"), Relations.CanEdit, ObjectRef.Parse("project:alpha"));

        var canEditResource = await engine.CheckAsync(
            SubjectRef.User("juan"), Relations.CanEdit, ObjectRef.Parse("resource:a"));

        canEditProject.Allowed.Should().BeFalse();
        canEditResource.Allowed.Should().BeFalse(
            "el recurso heredaba la edición del proyecto; sin ella no queda ninguna vía");
    }

    [Fact]
    public async Task Check_WhenTheProjectMovesToAnotherOrganization_InheritedAccessFollowsIt()
    {
        // "¿Qué pasa si el proyecto cambia de organización?" — se reescribe UNA tupla y
        // cambian de golpe todas las decisiones que dependían de la jerarquía. No hay nada
        // que recalcular ni ningún permiso materializado que haya que migrar.
        var moved = PlaygroundScenario.Tuples
            .Where(tuple => tuple != "project:alpha#parent@organization:acme")
            .Append("project:alpha#parent@organization:globex");

        var engine = await CreateEngineAsync(PlaygroundModels.Full, moved);

        var mariaCanEdit = await engine.CheckAsync(
            SubjectRef.User("maria"), Relations.CanEdit, ObjectRef.Parse("project:alpha"), CheckOptions.Explain);

        mariaCanEdit.Allowed.Should().BeFalse(
            "María es administradora de Acme, y Alpha ya no pertenece a Acme. Su acceso venía "
            + "exclusivamente de 'admin from parent'.");

        var juanCanEdit = await engine.CheckAsync(
            SubjectRef.User("juan"), Relations.CanEdit, ObjectRef.Parse("project:alpha"));

        juanCanEdit.Allowed.Should().BeTrue(
            "el acceso de Juan viene de una tupla directa entre el equipo y el proyecto, que no "
            + "depende de la organización");
    }

    [Fact]
    public async Task Check_GivingDirectAccessInsteadOfTeamAccess_ProducesTheSameAnswerByADifferentPath()
    {
        // "¿Qué diferencia hay entre dar acceso a Juan directamente y darlo a su equipo?"
        // Para el Check, ninguna: ALLOW y ALLOW. La diferencia está en todo lo demás.
        var directInstead = PlaygroundScenario.Tuples
            .Where(tuple => tuple != "project:alpha#editor@team:backend#member")
            .Append("project:alpha#editor@user:juan");

        var directEngine = await CreateEngineAsync(PlaygroundModels.Full, directInstead);
        var teamEngine = await CreatePlaygroundEngineAsync();

        var direct = await directEngine.CheckAsync(
            SubjectRef.User("juan"), Relations.CanEdit, ObjectRef.Parse("project:alpha"), CheckOptions.Explain);

        var viaTeam = await teamEngine.CheckAsync(
            SubjectRef.User("juan"), Relations.CanEdit, ObjectRef.Parse("project:alpha"), CheckOptions.Explain);

        direct.Allowed.Should().BeTrue();
        viaTeam.Allowed.Should().BeTrue();

        // La diferencia real: con acceso directo, el camino es un solo salto y solo afecta a
        // Juan. Con acceso por equipo, el camino tiene dos saltos y la misma tupla sirve para
        // todos los miembros. Ninguna de las dos es "mejor": una es para excepciones, la otra
        // para políticas.
        direct.Paths.Min(path => path.Length).Should()
            .BeLessThan(viaTeam.Paths.Min(path => path.Length));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Listado de objetos autorizados
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ListObjects_ForJuan_ReturnsTheThreeAcmeProjectsAndNotDelta()
    {
        var engine = await CreatePlaygroundEngineAsync();

        var result = await engine.ListObjectsAsync(
            SubjectRef.User("juan"), Relations.CanView, ObjectTypes.Project, ListObjectsStrategy.ReverseExpansion);

        result.Objects.Select(item => item.Object.Id).Should().BeEquivalentTo(
            ["alpha", "beta", "gamma"],
            because: "alpha por su equipo y por acceso directo, beta por ser miembro de Acme, "
                     + "gamma por ser público. Delta es de Globex y no tiene ninguna vía.");
    }

    public static TheoryData<string, string, string> ListObjectsCombinations()
    {
        var data = new TheoryData<string, string, string>();

        foreach (var subject in new[] { "user:juan", "user:pedro", "user:maria", "user:ana", "user:sofia" })
        {
            foreach (var (relation, objectType) in new[]
                     {
                         (Relations.CanView, ObjectTypes.Project),
                         (Relations.CanEdit, ObjectTypes.Project),
                         (Relations.CanDelete, ObjectTypes.Project),
                         (Relations.CanView, ObjectTypes.Resource),
                         (Relations.CanEdit, ObjectTypes.Resource),
                         (Relations.CanView, ObjectTypes.Folder),
                     })
            {
                data.Add(subject, relation, objectType);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ListObjectsCombinations))]
    public async Task ListObjects_ReverseExpansion_AgreesWithTheNaiveOracle(
        string subject,
        string relation,
        string objectType)
    {
        // ESTE es el test que hace creíble la expansión inversa. La estrategia naive es un
        // simple bucle de Checks y por tanto obviamente correcta; la inversa es un algoritmo
        // de punto fijo con propagación y confirmación. Si difieren en cualquier combinación,
        // el bug está en la inversa.
        var engine = await CreatePlaygroundEngineAsync();
        var parsedSubject = SubjectRef.Parse(subject);

        var naive = await engine.ListObjectsAsync(
            parsedSubject, relation, objectType, ListObjectsStrategy.Naive);

        var reverse = await engine.ListObjectsAsync(
            parsedSubject, relation, objectType, ListObjectsStrategy.ReverseExpansion);

        reverse.Objects.Select(item => item.Object.ToString()).Should().BeEquivalentTo(
            naive.Objects.Select(item => item.Object.ToString()),
            because: $"las dos estrategias deben ser semánticamente equivalentes para "
                     + $"{subject} / {relation} / {objectType}");
    }

    [Fact]
    public async Task ListObjects_ForAMonotonicRelation_ReverseExpansionTouchesTheStoreLess()
    {
        // 'project.can_view' se compone solo de unión, relación computada y herencia: todas
        // monótonas. La expansión inversa puede resolverlo del tirón, sin confirmar nada, y
        // ahí es donde gana claramente.
        var engine = await CreatePlaygroundEngineAsync();

        var naive = await engine.ListObjectsAsync(
            SubjectRef.User("juan"), Relations.CanView, ObjectTypes.Project, ListObjectsStrategy.Naive);

        var reverse = await engine.ListObjectsAsync(
            SubjectRef.User("juan"), Relations.CanView, ObjectTypes.Project, ListObjectsStrategy.ReverseExpansion);

        reverse.Metrics.ConfirmationChecks.Should().Be(0, "no hay reglas no monótonas en el camino");

        reverse.Metrics.StoreQueries.Should().BeLessThan(
            naive.Metrics.StoreQueries,
            because: "la naive hace un Check completo por cada objeto del catálogo; la inversa "
                     + "parte del sujeto y solo toca lo alcanzable");
    }

    [Fact]
    public async Task ListObjects_ForANonMonotonicRelation_PaysForConfirmationChecks()
    {
        // 'resource.can_view' termina en 'viewable but not blocked'. La exclusión no se puede
        // invertir, así que la fase 2 produce candidatos y la fase 3 los confirma uno a uno.
        //
        // La consecuencia incómoda, y por eso este test existe: con un catálogo pequeño donde
        // el sujeto ve casi todo, la expansión inversa NO es más barata que la naive. Hace
        // prácticamente los mismos Checks más el coste de la expansión.
        //
        // La ventaja de la expansión inversa no viene del algoritmo en abstracto: viene de la
        // proporción entre el tamaño del catálogo y el del conjunto accesible. Ver el test
        // siguiente, que es donde se ve de verdad.
        var engine = await CreatePlaygroundEngineAsync();

        var naive = await engine.ListObjectsAsync(
            SubjectRef.User("juan"), Relations.CanView, ObjectTypes.Resource, ListObjectsStrategy.Naive);

        var reverse = await engine.ListObjectsAsync(
            SubjectRef.User("juan"), Relations.CanView, ObjectTypes.Resource, ListObjectsStrategy.ReverseExpansion);

        reverse.Metrics.ConfirmationChecks.Should().BeGreaterThan(
            0, "la exclusión obliga a confirmar cada candidato con un Check real");

        reverse.Objects.Should().HaveCount(naive.Objects.Count, "el resultado sigue siendo correcto");
    }

    [Fact]
    public async Task ListObjects_WhenTheCatalogIsMuchBiggerThanWhatTheSubjectCanSee_ReverseExpansionWins()
    {
        // Aquí está el argumento de verdad, y hace falta un catálogo grande para verlo.
        //
        // Se añaden 200 proyectos de una organización a la que Juan no pertenece. Para Juan no
        // cambia nada: sigue viendo tres. Pero el coste de averiguarlo cambia radicalmente
        // según la estrategia, porque la naive tiene que preguntar por los 204 y la inversa
        // ni siquiera llega a mirarlos.
        //
        // Es exactamente la diferencia entre una pantalla que carga y una que no, cuando el
        // sistema tiene 200.000 documentos y tú puedes ver doce.
        var noise = Enumerable.Range(1, 200)
            .SelectMany(index => new[]
            {
                $"project:ruido-{index}#parent@organization:globex",
                $"project:ruido-{index}#viewer@organization:globex#member",
            });

        var engine = await CreateEngineAsync(PlaygroundModels.Full, PlaygroundScenario.Tuples.Concat(noise));

        var naive = await engine.ListObjectsAsync(
            SubjectRef.User("juan"), Relations.CanView, ObjectTypes.Project, ListObjectsStrategy.Naive);

        var reverse = await engine.ListObjectsAsync(
            SubjectRef.User("juan"), Relations.CanView, ObjectTypes.Project, ListObjectsStrategy.ReverseExpansion);

        reverse.Objects.Select(item => item.Object.Id).Should().BeEquivalentTo(
            naive.Objects.Select(item => item.Object.Id),
            "las dos siguen devolviendo lo mismo");

        naive.Objects.Should().HaveCount(3);

        // El coste de la naive crece con el catálogo; el de la inversa, con lo accesible.
        reverse.Metrics.StoreQueries.Should().BeLessThan(
            naive.Metrics.StoreQueries / 10,
            because: "el catálogo es ~68 veces mayor que lo que Juan puede ver, y ese es "
                     + "justo el factor que la expansión inversa se ahorra");
    }

    [Fact]
    public async Task ListObjects_NaiveStrategy_ExplainsWhyEachObjectWasExcluded()
    {
        // Lo único que la naive hace mejor: sabe qué objetos existen, así que puede decir por
        // qué no ves los que no ves. La expansión inversa no puede, y no es un defecto de la
        // implementación: nunca llega a mirarlos.
        var engine = await CreatePlaygroundEngineAsync();

        var naive = await engine.ListObjectsAsync(
            SubjectRef.User("juan"), Relations.CanView, ObjectTypes.Project, ListObjectsStrategy.Naive);

        var reverse = await engine.ListObjectsAsync(
            SubjectRef.User("juan"), Relations.CanView, ObjectTypes.Project, ListObjectsStrategy.ReverseExpansion);

        naive.Denied.Should().Contain(item => item.Object.Id == "delta");
        naive.Denied.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.Reason));
        reverse.Denied.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Robustez: ciclos, profundidad y datos raros
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Check_WithMutuallyNestedGroups_TerminatesAndDenies()
    {
        // Dos grupos que se contienen mutuamente es una operación de administración
        // perfectamente normal, y sin detección de ciclos cuelga el servicio entero.
        var cyclic = PlaygroundScenario.Tuples
            .Append("group:seguridad#member@group:auditoria#member")
            .Append("resource:c#viewer@group:seguridad#member");

        var engine = await CreateEngineAsync(PlaygroundModels.Full, cyclic);

        var decision = await engine.CheckAsync(
            SubjectRef.User("ana"), Relations.CanView, ObjectRef.Parse("resource:c"), CheckOptions.Explain);

        decision.Allowed.Should().BeFalse("Ana no está en ninguno de los dos grupos");
        decision.Metrics.CyclesDetected.Should().BeGreaterThan(0, "el ciclo debe detectarse, no ignorarse");
    }

    [Fact]
    public async Task Check_WithMutuallyNestedGroups_StillResolvesLegitimateMembership()
    {
        // Que haya un ciclo no puede romper los accesos válidos: Pedro sigue estando en
        // Seguridad, y el ciclo solo debe cortar la rama que no lleva a ninguna parte nueva.
        var cyclic = PlaygroundScenario.Tuples
            .Append("group:seguridad#member@group:auditoria#member");

        var engine = await CreateEngineAsync(PlaygroundModels.Full, cyclic);

        var decision = await engine.CheckAsync(
            SubjectRef.User("pedro"), Relations.CanView, ObjectRef.Parse("resource:a"), CheckOptions.Explain);

        decision.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task Check_WithATightDepthLimit_StopsInheritingDownTheHierarchy()
    {
        // Bajar el límite convierte un ALLOW heredado en un DENY. Y el matiz importa: la
        // respuesta correcta en ese caso es "no se pudo determinar", no "no tiene acceso".
        var engine = await CreatePlaygroundEngineAsync();

        var deep = await engine.CheckAsync(
            SubjectRef.User("pedro"),
            Relations.CanView,
            ObjectRef.Parse("resource:b"),
            CheckOptions.Explain with { MaxDepth = 3 });

        deep.Allowed.Should().BeFalse();
        deep.Reason.Should().Contain("profundidad");
    }

    [Fact]
    public async Task Check_ForARelationThatDoesNotExistInTheModel_DeniesAndSaysSo()
    {
        var engine = await CreatePlaygroundEngineAsync();

        var decision = await engine.CheckAsync(
            SubjectRef.User("juan"), "can_teleport", ObjectRef.Parse("project:alpha"), CheckOptions.Explain);

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Contain("can_teleport");
    }

    [Fact]
    public async Task Check_WhenTheModelNoLongerAcceptsASubjectKind_IgnoresTheStaleTuple()
    {
        // Escenario real de una migración: el modelo deja de admitir usersets de grupo en
        // 'viewer', pero las tuplas viejas siguen en la base. Deben dejar de conceder acceso,
        // porque conceder por una regla que ya no existe es una brecha silenciosa.
        const string withoutGroups =
            """
            model
              schema 1.1
            type user
            type group
              relations
                define member: [user, group#member]
            type resource
              relations
                define viewer: [user]
                define can_view: viewer
            """;

        var engine = await CreateEngineAsync(withoutGroups,
        [
            "group:seguridad#member@user:pedro",
            "resource:a#viewer@group:seguridad#member",
        ]);

        var decision = await engine.CheckAsync(
            SubjectRef.User("pedro"), Relations.CanView, ObjectRef.Parse("resource:a"), CheckOptions.Explain);

        decision.Allowed.Should().BeFalse(
            "el modelo vigente ya no admite 'group#member' en 'viewer'");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Explicaciones
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Check_WhenDenied_SuggestsTuplesThatWouldGrantAccess()
    {
        var engine = await CreatePlaygroundEngineAsync();

        var decision = await engine.CheckAsync(
            SubjectRef.User("ana"), Relations.CanEdit, ObjectRef.Parse("project:alpha"), CheckOptions.Explain);

        decision.Allowed.Should().BeFalse();
        decision.SuggestedTuples.Should().NotBeEmpty();

        // Debe proponer tanto la vía directa como la vía por un conjunto al que Ana YA
        // pertenece, que es la sugerencia interesante: no hace falta tocar a la persona.
        var rendered = decision.SuggestedTuples.Select(tuple => tuple.ToString()).ToList();

        rendered.Should().Contain("project:alpha#editor@user:ana");
        rendered.Should().Contain(tuple => tuple.Contains('#') && tuple.Contains("team:sales#member"));
    }

    [Fact]
    public async Task Check_WhenDeniedByExclusion_SaysThatAddingRelationsWillNotHelp()
    {
        // Un DENY por exclusión es de otra naturaleza que un DENY por falta de relaciones, y
        // el arreglo también: hay que borrar la exclusión, no añadir accesos.
        var engine = await CreatePlaygroundEngineAsync();

        var decision = await engine.CheckAsync(
            SubjectRef.User("ana"), Relations.CanView, ObjectRef.Parse("resource:d"), CheckOptions.Explain);

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Contain("exclusión");
    }

    [Fact]
    public async Task Check_WhenAllowedByMultiplePaths_MentionsTheRevocationRisk()
    {
        var engine = await CreatePlaygroundEngineAsync();

        var decision = await engine.CheckAsync(
            SubjectRef.User("juan"), Relations.CanView, ObjectRef.Parse("project:alpha"), CheckOptions.Explain);

        decision.Allowed.Should().BeTrue();
        decision.Paths.Should().HaveCountGreaterThanOrEqualTo(2);
        decision.Reason.Should().Contain("revocar");
    }

    [Fact]
    public async Task Check_ByDefault_ShortCircuitsAndFindsASinglePath()
    {
        // El Check de producción no explora todo el árbol: para en el primer ALLOW. Por eso
        // es más rápido y por eso no puede contar los caminos.
        var engine = await CreatePlaygroundEngineAsync();

        var quick = await engine.CheckAsync(
            SubjectRef.User("juan"), Relations.CanView, ObjectRef.Parse("project:alpha"), CheckOptions.Default);

        var thorough = await engine.CheckAsync(
            SubjectRef.User("juan"), Relations.CanView, ObjectRef.Parse("project:alpha"), CheckOptions.Explain);

        quick.Allowed.Should().Be(thorough.Allowed);
        quick.Paths.Should().HaveCount(1);
        quick.Metrics.StoreQueries.Should().BeLessThanOrEqualTo(thorough.Metrics.StoreQueries);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Expand
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Expand_ReturnsUsersetsWithoutFlatteningThemToPeople()
    {
        // La propiedad esencial de Expand: devuelve "Team Backend", no sus miembros.
        var engine = await CreatePlaygroundEngineAsync();

        var expanded = await engine.ExpandAsync(ObjectRef.Parse("project:alpha"), Relations.CanEdit);

        var rendered = expanded.Leaves.Select(leaf => leaf.ToString()).ToList();

        rendered.Should().Contain("team:backend#member");
        rendered.Should().Contain("user:sofia");
        rendered.Should().NotContain("user:juan",
            "Juan tiene acceso, pero a través del userset. Expand no resuelve la pertenencia: "
            + "aplanarla sería carísimo y el resultado caducaría al instante.");
    }

    [Fact]
    public async Task Expand_IncludesSubjectsInheritedFromTheParent()
    {
        var engine = await CreatePlaygroundEngineAsync();

        var expanded = await engine.ExpandAsync(ObjectRef.Parse("resource:a"), Relations.CanEdit);

        expanded.Leaves.Select(leaf => leaf.ToString()).Should().Contain("team:backend#member",
            "el recurso hereda la edición de project:alpha");
    }

    [Fact]
    public async Task Expand_DoesNotListExcludedSubjectsAsHavingAccess()
    {
        var engine = await CreatePlaygroundEngineAsync();

        var expanded = await engine.ExpandAsync(ObjectRef.Parse("resource:d"), Relations.CanView);

        expanded.Leaves.Select(leaf => leaf.ToString()).Should().NotContain("user:ana",
            "Ana está en la rama de exclusión: aparece en el árbol, pero como excluida");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // BatchCheck
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BatchCheck_ReturnsTheSameDecisionsAsIndividualChecks()
    {
        var engine = await CreatePlaygroundEngineAsync();

        var requests = PlaygroundScenario.Cases
            .Select(scenario => (
                Subject: SubjectRef.Parse(scenario.Subject),
                scenario.Relation,
                Object: ObjectRef.Parse(scenario.Object)))
            .ToList();

        var batch = await engine.BatchCheckAsync(requests);

        batch.Should().HaveCount(requests.Count);

        foreach (var (request, index) in requests.Select((request, index) => (request, index)))
        {
            var individual = await engine.CheckAsync(request.Subject, request.Relation, request.Object);

            batch[index].Allowed.Should().Be(individual.Allowed,
                $"el lote no puede diferir del check individual para {request.Subject} / {request.Relation} / {request.Object}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // El modelo importa: la misma pregunta con dos modelos
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Check_WithoutFolderInheritance_TheSameQuestionChangesAnswer()
    {
        // La demostración más limpia de qué hace un tuple_to_userset: mismas tuplas, mismo
        // sujeto, mismo objeto, y la respuesta cambia por una línea del modelo.
        var withInheritance = await CreateEngineAsync(PlaygroundModels.Full, PlaygroundScenario.Tuples);
        var withoutInheritance = await CreateEngineAsync(PlaygroundModels.WithoutFolderInheritance, PlaygroundScenario.Tuples);

        var inherited = await withInheritance.CheckAsync(
            SubjectRef.User("pedro"), Relations.CanView, ObjectRef.Parse("resource:b"));

        var notInherited = await withoutInheritance.CheckAsync(
            SubjectRef.User("pedro"), Relations.CanView, ObjectRef.Parse("resource:b"), CheckOptions.Explain);

        inherited.Allowed.Should().BeTrue();
        notInherited.Allowed.Should().BeFalse(
            "sin 'can_view from parent' en folder, la carpeta hija no hereda de la padre");
    }
}
