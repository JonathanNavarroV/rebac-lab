using Playground.AccessControl.Application.Authorization.Seed;
using Playground.AccessControl.Domain.Abstractions;
using Playground.AccessControl.Domain.Checking;
using Playground.AccessControl.Domain.Model;

namespace Playground.AccessControl.Application.Authorization.Tour;

/// <summary>Lo que devolvió el motor al ejecutar una sonda.</summary>
public sealed record TourProbeResult(
    TourProbe Probe,
    CheckDecision? Decision = null,
    ListObjectsResult? Naive = null,
    ListObjectsResult? Reverse = null)
{
    /// <summary>
    /// <c>true</c> si la sonda declaraba una expectativa y el motor la ha cumplido;
    /// <c>null</c> si no declaraba ninguna.
    /// </summary>
    /// <remarks>
    /// Se calcula al vuelo y se devuelve al frontend, así que si has estado trasteando con las
    /// relaciones y una lección ha dejado de cumplirse, la pantalla te lo dice en lugar de
    /// enseñarte una explicación que ya no encaja con lo que acabas de ver.
    /// </remarks>
    public bool? MatchesExpectation
    {
        get
        {
            if (Probe.Kind == TourProbeKind.ListObjectsComparison)
            {
                if (Probe.ExpectedReverseReadsFewerTuples is not { } expected
                    || Naive is null
                    || Reverse is null)
                {
                    return null;
                }

                return Reverse.Metrics.TuplesRead < Naive.Metrics.TuplesRead == expected;
            }

            if (Decision is null)
                return null;

            if (Probe.ExpectedAllowed is { } expectedAllowed && Decision.Allowed != expectedAllowed)
                return false;

            if (Probe.ExpectedMinimumPaths is { } minimum && Decision.Paths.Count < minimum)
                return false;

            return Probe.ExpectedAllowed is not null || Probe.ExpectedMinimumPaths is not null
                ? true
                : null;
        }
    }
}

/// <summary>Resultado de responder una pregunta.</summary>
public sealed record TourAnswerResult(
    TourQuestion Question,
    string ChosenOptionKey,
    bool Correct,
    IReadOnlyList<TourProbeResult> Evidence);

/// <summary>
/// Corrige las respuestas del cuestionario y ejecuta la evidencia contra el motor real.
/// </summary>
/// <remarks>
/// <para>
/// La diferencia con el documento en markdown está aquí: la explicación no llega sola, llega
/// acompañada de lo que el motor acaba de contestar, con su traza y sus métricas. No es un
/// «la respuesta era (b)», es un «la respuesta era (b), y aquí tienes el árbol de evaluación
/// que lo demuestra sobre las tuplas que tienes ahora mismo».
/// </para>
/// <para>
/// Las sondas se ejecutan siempre con la explicación activada. Es más caro, y es a propósito:
/// el objetivo de esta pantalla no es ser rápida, es enseñar por qué.
/// </para>
/// </remarks>
public sealed class TourService(IAccessControlEngine engine)
{
    public async Task<TourAnswerResult> AnswerAsync(
        string questionCode,
        string chosenOptionKey,
        CancellationToken cancellationToken = default)
    {
        var question = TourQuestions.Questions.FirstOrDefault(item => item.Code == questionCode)
                       ?? throw new KeyNotFoundException($"No existe la pregunta '{questionCode}'.");

        var evidence = new List<TourProbeResult>();

        foreach (var probe in question.Probes)
            evidence.Add(await RunProbeAsync(probe, cancellationToken));

        var correct = string.Equals(chosenOptionKey, question.CorrectOptionKey, StringComparison.OrdinalIgnoreCase);

        return new TourAnswerResult(question, chosenOptionKey, correct, evidence);
    }

    /// <summary>
    /// Ejecuta todas las sondas de todas las preguntas. Lo usa el test que impide que el
    /// cuestionario mienta.
    /// </summary>
    public async Task<IReadOnlyList<(TourQuestion Question, TourProbeResult Result)>> RunAllProbesAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<(TourQuestion, TourProbeResult)>();

        foreach (var question in TourQuestions.Questions)
        {
            foreach (var probe in question.Probes)
                results.Add((question, await RunProbeAsync(probe, cancellationToken)));
        }

        return results;
    }

    private async Task<TourProbeResult> RunProbeAsync(TourProbe probe, CancellationToken cancellationToken)
    {
        if (probe.Kind == TourProbeKind.ListObjectsComparison)
        {
            var subject = SubjectRef.Parse(probe.Subject);

            var naive = await engine.ListObjectsAsync(
                subject, probe.Relation, probe.Object, ListObjectsStrategy.Naive,
                cancellationToken: cancellationToken);

            var reverse = await engine.ListObjectsAsync(
                subject, probe.Relation, probe.Object, ListObjectsStrategy.ReverseExpansion,
                cancellationToken: cancellationToken);

            return new TourProbeResult(probe, Naive: naive, Reverse: reverse);
        }

        var decision = await engine.CheckAsync(
            SubjectRef.Parse(probe.Subject),
            probe.Relation,
            ObjectRef.Parse(probe.Object),
            CheckOptions.Explain,
            cancellationToken);

        return new TourProbeResult(probe, Decision: decision);
    }
}
