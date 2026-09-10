using System.Diagnostics.CodeAnalysis;
using Playground.AccessControl.Api.Mapping;
using Playground.AccessControl.Application.Authorization.Seed;
using Playground.AccessControl.Application.Authorization.Tour;
using Playground.Contracts.AccessControl;

namespace Playground.AccessControl.Api.Endpoints;

/// <summary>
/// El cuestionario guiado: la versión ejecutable de <c>docs/07-tour-guiado.md</c>.
/// </summary>
[ExcludeFromCodeCoverage]
public static class TourEndpoints
{
    public static RouteGroupBuilder MapTourEndpoints(this RouteGroupBuilder group)
    {
        var tour = group.MapGroup("/tour").WithTags("Tour guiado");

        tour.MapGet("/", GetTour)
            .WithName("GetTour")
            .WithSummary("Las lecciones y sus preguntas, sin las respuestas")
            .WithDescription(
                "Deliberadamente NO incluye ni la opción correcta ni la explicación: el ejercicio "
                + "consiste en apostar antes de mirar, y con la respuesta en el JSON bastaría abrir "
                + "las herramientas de desarrollo para arruinarlo. Se resuelve al responder.")
            .Produces<IReadOnlyList<TourLessonDto>>();

        tour.MapPost("/answer", AnswerAsync)
            .WithName("AnswerTourQuestion")
            .WithSummary("Corrige una respuesta y ejecuta la evidencia contra el motor")
            .WithDescription(
                "Además de decirte si acertaste, ejecuta las comprobaciones que respaldan la "
                + "respuesta y te devuelve lo que el motor contesta AHORA MISMO, con su traza y sus "
                + "métricas. Si has cambiado relaciones y la lección ya no se cumple, te avisa.")
            .Produces<TourAnswerResponse>()
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }

    private static IResult GetTour()
    {
        var lessons = TourQuestions.Lessons
            .Select(lesson => new TourLessonDto(
                lesson.Code,
                lesson.Title,
                lesson.Intro,
                lesson.Facts,
                lesson.ModelSnippet,
                TourQuestions.ForLesson(lesson.Code)
                    .Select(question => new TourQuestionDto(
                        question.Code,
                        question.LessonCode,
                        question.Kind,
                        question.Statement,
                        question.Options.Select(option => new TourOptionDto(option.Key, option.Text)).ToList(),
                        question.Hint,
                        question.Probes.Count > 0))
                    .ToList()))
            .ToList();

        return Results.Ok(lessons);
    }

    private static async Task<IResult> AnswerAsync(
        TourAnswerRequest request,
        TourService tour,
        CancellationToken cancellationToken)
    {
        TourAnswerResult result;

        try
        {
            result = await tour.AnswerAsync(request.QuestionCode, request.OptionKey, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new { title = "Pregunta no encontrada", code = request.QuestionCode });
        }

        var evidence = result.Evidence.Select(ToDto).ToList();

        // Si alguna sonda con expectativa declarada ya no se cumple, el escenario ha cambiado
        // lo bastante como para que la lección haya dejado de ser cierta. Avisar es mejor que
        // enseñar una explicación que no encaja con lo que la persona acaba de ver en pantalla.
        var broken = result.Evidence.Count(probe => probe.MatchesExpectation == false);

        var warning = broken == 0
            ? null
            : $"Ojo: {broken} de las comprobaciones de esta pregunta ya no dan el resultado que la "
              + "lección da por supuesto. Has cambiado relaciones del escenario — que es justo para lo "
              + "que está el laboratorio. La explicación sigue siendo válida como mecanismo, pero los "
              + "números de abajo son los de tu escenario, no los del original. Para volver al punto "
              + "de partida, usa «Restaurar escenario» en la pantalla de Relaciones.";

        return Results.Ok(new TourAnswerResponse(
            result.Question.Code,
            result.Correct,
            result.ChosenOptionKey,
            result.Question.CorrectOptionKey,
            result.Question.Explanation,
            evidence,
            warning));
    }

    private static TourEvidenceDto ToDto(TourProbeResult probe)
    {
        if (probe.Probe.Kind == TourProbeKind.ListObjectsComparison)
        {
            TourListComparisonDto? comparison = null;

            if (probe.Naive is not null && probe.Reverse is not null)
            {
                var naiveIds = probe.Naive.Objects.Select(item => item.Object.ToString()).Order().ToList();
                var reverseIds = probe.Reverse.Objects.Select(item => item.Object.ToString()).Order().ToList();

                comparison = new TourListComparisonDto(
                    reverseIds,
                    probe.Naive.Metrics.ToDto(),
                    probe.Reverse.Metrics.ToDto(),
                    naiveIds.SequenceEqual(reverseIds),
                    BuildVerdict(probe));
            }

            return new TourEvidenceDto(
                "list-comparison",
                probe.Probe.Label,
                probe.Probe.Subject,
                probe.Probe.Relation,
                probe.Probe.Object,
                probe.MatchesExpectation,
                Check: null,
                Comparison: comparison);
        }

        return new TourEvidenceDto(
            "check",
            probe.Probe.Label,
            probe.Probe.Subject,
            probe.Probe.Relation,
            probe.Probe.Object,
            probe.MatchesExpectation,
            probe.Decision?.ToDto(),
            Comparison: null);
    }

    /// <summary>
    /// Redacta la conclusión de una comparación de estrategias con los números de esta
    /// ejecución concreta.
    /// </summary>
    private static string BuildVerdict(TourProbeResult probe)
    {
        if (probe.Naive is null || probe.Reverse is null)
            return string.Empty;

        var naive = probe.Naive.Metrics;
        var reverse = probe.Reverse.Metrics;

        var verdict = $"Ingenua: {naive.TuplesRead} tuplas leídas en {naive.StoreQueries} consultas. "
                      + $"Inversa: {reverse.TuplesRead} en {reverse.StoreQueries}. ";

        if (reverse.ConfirmationChecks > 0)
        {
            verdict += $"La inversa ha tenido que confirmar {reverse.ConfirmationChecks} candidato(s) con "
                       + "un Check real, porque esta relación tiene una regla no monótona que no se puede "
                       + "invertir. Ese es el coste de la exclusión. ";
        }
        else
        {
            verdict += "La inversa no ha necesitado confirmar nada: todas las reglas de esta relación son "
                       + "monótonas, así que la pertenencia al conjunto es concluyente por sí sola. ";
        }

        verdict += reverse.TuplesRead < naive.TuplesRead
            ? "Aquí gana la inversa, y la ventaja crece con el catálogo."
            : "Aquí NO gana la inversa: el catálogo es diminuto y el sujeto ve casi todo, así que no hay "
              + "nada que ahorrar. Su ventaja depende de la proporción entre lo que existe y lo que se "
              + "puede ver, no del algoritmo en sí.";

        return verdict;
    }
}
