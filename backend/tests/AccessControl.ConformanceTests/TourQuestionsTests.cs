using FluentAssertions;
using Playground.AccessControl.Application.Authorization.Engine;
using Playground.AccessControl.Application.Authorization.Model;
using Playground.AccessControl.Application.Authorization.Seed;
using Playground.AccessControl.Application.Authorization.Storage;
using Playground.AccessControl.Application.Authorization.Tour;
using Playground.AccessControl.Domain.Abstractions;

namespace AccessControl.ConformanceTests;

/// <summary>
/// El test que impide que el cuestionario mienta.
/// </summary>
/// <remarks>
/// <para>
/// Un cuestionario sobre un motor tiene un problema que un cuestionario normal no tiene: el
/// motor cambia. Basta con tocar una línea del modelo o quitar una tupla del escenario para que
/// una respuesta «correcta» pase a ser falsa, y nadie se entera hasta que alguien está
/// aprendiendo con ella.
/// </para>
/// <para>
/// Por eso cada pregunta de predicción declara las comprobaciones que la respaldan, y esta
/// suite las ejecuta todas contra el motor real. Si alguien cambia el modelo y una lección deja
/// de cumplirse, falla aquí en lugar de enseñar algo que no es cierto.
/// </para>
/// </remarks>
public class TourQuestionsTests
{
    private static TourService CreateTour()
    {
        var tuples = new InMemoryRelationshipTupleStore(PlaygroundScenario.Tuples.ToArray());
        var models = new InMemoryAuthorizationModelStore(PlaygroundModels.Full);
        var catalog = new TupleDerivedObjectCatalog(tuples);

        IAccessControlEngine engine = new AccessControlEngine(tuples, models, catalog);

        return new TourService(engine);
    }

    public static TheoryData<string> QuestionCodes()
    {
        var data = new TheoryData<string>();
        TourQuestions.Questions.ToList().ForEach(question => data.Add(question.Code));
        return data;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Lo importante: la evidencia se cumple
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EveryProbe_MatchesWhatTheEngineActuallyAnswers()
    {
        var tour = CreateTour();

        var results = await tour.RunAllProbesAsync();

        var broken = results
            .Where(entry => entry.Result.MatchesExpectation == false)
            .Select(entry =>
                $"{entry.Question.Code}: «{entry.Result.Probe.Label}» "
                + $"({entry.Result.Probe.Subject} / {entry.Result.Probe.Relation} / {entry.Result.Probe.Object}) "
                + $"— el motor responde {(entry.Result.Decision?.Allowed is { } allowed ? allowed.ToString() : "otra cosa")}")
            .ToList();

        broken.Should().BeEmpty(
            because: "el cuestionario afirma cosas sobre el motor y todas tienen que ser ciertas. "
                     + "Si este test falla, o el modelo/escenario ha cambiado y hay que actualizar la "
                     + "lección, o la lección estaba mal desde el principio."
                     + Environment.NewLine
                     + string.Join(Environment.NewLine, broken));
    }

    [Fact]
    public async Task EveryPredictionQuestion_HasEvidenceBackingIt()
    {
        // Una pregunta de predicción sin sondas es una afirmación sin comprobar: exactamente el
        // problema que esta suite existe para evitar. Las de tipo «concept» sí pueden no
        // tenerlas, porque preguntan por criterios de diseño que no se ejecutan.
        var withoutEvidence = TourQuestions.Questions
            .Where(question => question.Kind == "prediction" && question.Probes.Count == 0)
            .Select(question => question.Code)
            .ToList();

        withoutEvidence.Should().BeEmpty(
            "una pregunta de predicción tiene que poder demostrarse ejecutando el motor");

        await Task.CompletedTask;
    }

    [Fact]
    public async Task EveryPredictionQuestion_HasAtLeastOneProbeWithADeclaredExpectation()
    {
        // No basta con ejecutar algo: hay que declarar QUÉ se espera, o el test anterior no
        // comprueba nada.
        var unverifiable = TourQuestions.Questions
            .Where(question => question.Kind == "prediction")
            .Where(question => question.Probes.All(probe =>
                probe.ExpectedAllowed is null
                && probe.ExpectedMinimumPaths is null
                && probe.ExpectedReverseReadsFewerTuples is null))
            .Select(question => question.Code)
            .ToList();

        unverifiable.Should().BeEmpty(
            "sin una expectativa declarada, la sonda se ejecuta pero no verifica nada");

        await Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Coherencia del propio cuestionario
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(QuestionCodes))]
    public void Question_IsWellFormed(string code)
    {
        var question = TourQuestions.Questions.Single(item => item.Code == code);

        question.Options.Should().HaveCountGreaterThanOrEqualTo(2, "una pregunta con una sola opción no pregunta nada");

        question.Options.Select(option => option.Key).Should().OnlyHaveUniqueItems();

        question.Options.Should().Contain(
            option => option.Key == question.CorrectOptionKey,
            $"la opción correcta declarada ('{question.CorrectOptionKey}') tiene que existir entre las opciones");

        question.Explanation.Should().NotBeNullOrWhiteSpace();
        question.Kind.Should().BeOneOf("prediction", "concept");
    }

    [Fact]
    public void QuestionCodes_AreUnique()
    {
        TourQuestions.Questions.Select(question => question.Code).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void EveryQuestion_BelongsToADeclaredLesson()
    {
        var lessons = TourQuestions.Lessons.Select(lesson => lesson.Code).ToHashSet();

        TourQuestions.Questions
            .Where(question => !lessons.Contains(question.LessonCode))
            .Should().BeEmpty();
    }

    [Fact]
    public void EveryProbe_UsesReferencesThatExistInTheScenario()
    {
        // Una errata en un sujeto («user:mario») produciría un DENY perfectamente plausible y
        // una lección que enseña justo lo contrario de lo que pretende.
        var known = PlaygroundScenario.Users.Select(user => $"user:{user.Id}")
            .Concat(PlaygroundScenario.AllEntities().Select(entity => $"{entity.Type}:{entity.Id}"))
            .ToHashSet(StringComparer.Ordinal);

        var unknown = TourQuestions.Questions
            .SelectMany(question => question.Probes.Select(probe => (question.Code, probe)))
            .Where(entry => !known.Contains(entry.probe.Subject.Split('#')[0]))
            .Select(entry => $"{entry.Code}: sujeto desconocido «{entry.probe.Subject}»")
            .ToList();

        unknown.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // La corrección funciona
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Answer_WithTheCorrectOption_IsMarkedAsCorrect()
    {
        var tour = CreateTour();
        var question = TourQuestions.Questions.First();

        var result = await tour.AnswerAsync(question.Code, question.CorrectOptionKey);

        result.Correct.Should().BeTrue();
        result.Evidence.Should().HaveCount(question.Probes.Count);
    }

    [Fact]
    public async Task Answer_WithAWrongOption_StillReturnsTheEvidence()
    {
        // Fallar es donde más se aprende, así que la evidencia y la explicación se devuelven
        // igual. El cuestionario no castiga el error escondiendo el porqué.
        var tour = CreateTour();
        var question = TourQuestions.Questions.First(item => item.Probes.Count > 0);
        var wrong = question.Options.First(option => option.Key != question.CorrectOptionKey);

        var result = await tour.AnswerAsync(question.Code, wrong.Key);

        result.Correct.Should().BeFalse();
        result.Evidence.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Answer_ForAnUnknownQuestion_Throws()
    {
        var tour = CreateTour();

        var act = () => tour.AnswerAsync("99.9", "a");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // La lección 9, que es la que tenía la afirmación sin comprobar
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Lesson9_ShowsThatReverseExpansionOnlyWinsWhenTheRelationIsMonotonic()
    {
        // Esta es la comprobación que el documento original daba por supuesta sin medirla.
        // «can_edit» sobre resource es monótona y la inversa gana; «can_view» arrastra la
        // exclusión y tiene que confirmar candidato por candidato.
        var tour = CreateTour();

        var results = await tour.RunAllProbesAsync();

        var monotonic = results.Single(entry =>
            entry.Question.Code == "9.2"
            && entry.Result.Probe.Relation == "can_edit");

        var nonMonotonic = results.First(entry =>
            entry.Question.Code == "9.3"
            && entry.Result.Probe.Relation == "can_view");

        monotonic.Result.Reverse!.Metrics.ConfirmationChecks.Should().Be(
            0, "can_edit no tiene reglas no monótonas en el camino");

        nonMonotonic.Result.Reverse!.Metrics.ConfirmationChecks.Should().BeGreaterThan(
            0, "can_view termina en «viewable but not blocked» y la exclusión obliga a confirmar");

        monotonic.Result.Reverse.Metrics.TuplesRead.Should().BeLessThan(
            monotonic.Result.Naive!.Metrics.TuplesRead,
            "con reglas monótonas la expansión inversa lee menos");
    }
}
