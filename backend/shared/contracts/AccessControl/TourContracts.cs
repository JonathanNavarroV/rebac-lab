namespace Playground.Contracts.AccessControl;

/// <summary>Una opción de respuesta.</summary>
public sealed record TourOptionDto(string Key, string Text);

/// <summary>
/// Una pregunta, tal y como se sirve ANTES de responderla.
/// </summary>
/// <remarks>
/// Fíjate en lo que no lleva: ni la opción correcta ni la explicación. No es paranoia, es que
/// el ejercicio consiste en apostar antes de mirar, y con la respuesta viajando en el JSON
/// bastaría abrir las herramientas de desarrollo para arruinarlo. Se resuelve en el servidor,
/// al responder.
/// </remarks>
public sealed record TourQuestionDto(
    string Code,
    string LessonCode,
    string Kind,
    string Statement,
    IReadOnlyList<TourOptionDto> Options,
    string? Hint,
    bool HasEvidence);

public sealed record TourLessonDto(
    string Code,
    string Title,
    string Intro,
    IReadOnlyList<string> Facts,
    string? ModelSnippet,
    IReadOnlyList<TourQuestionDto> Questions);

public sealed record TourAnswerRequest(string QuestionCode, string OptionKey);

/// <summary>
/// Un listado resuelto con las dos estrategias, para las preguntas de la lección 9.
/// </summary>
public sealed record TourListComparisonDto(
    IReadOnlyList<string> Objects,
    CheckMetricsDto NaiveMetrics,
    CheckMetricsDto ReverseMetrics,
    bool SameResult,
    string Verdict);

/// <summary>
/// La evidencia de una respuesta: lo que el motor contestó de verdad, ahora mismo.
/// </summary>
/// <param name="MatchesExpectation">
/// <c>null</c> si la sonda no declaraba expectativa. <c>false</c> significa que el escenario ha
/// cambiado lo bastante como para que esta lección ya no se cumpla — normalmente porque has
/// estado trasteando con las relaciones, que es exactamente para lo que está el laboratorio.
/// </param>
public sealed record TourEvidenceDto(
    string Kind,
    string Label,
    string Subject,
    string Relation,
    string Object,
    bool? MatchesExpectation,
    CheckResponse? Check,
    TourListComparisonDto? Comparison);

/// <summary>Resultado de responder: si acertaste, cuál era, por qué, y la prueba.</summary>
public sealed record TourAnswerResponse(
    string QuestionCode,
    bool Correct,
    string ChosenOptionKey,
    string CorrectOptionKey,
    string Explanation,
    IReadOnlyList<TourEvidenceDto> Evidence,
    string? ScenarioWarning);
