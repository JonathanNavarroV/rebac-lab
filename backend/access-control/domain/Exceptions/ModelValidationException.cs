namespace Playground.AccessControl.Domain.Exceptions;

/// <summary>Un problema concreto detectado al compilar un modelo de autorización.</summary>
/// <param name="Line">Línea del DSL, 1-indexada. <c>0</c> si el problema no es de una línea concreta.</param>
/// <param name="Message">Descripción del problema.</param>
/// <param name="Hint">Cómo arreglarlo. Es lo que hace que el error enseñe algo.</param>
public sealed record ModelValidationError(int Line, string Message, string? Hint = null)
{
    public override string ToString() =>
        (Line > 0 ? $"Línea {Line}: {Message}" : Message)
        + (Hint is null ? string.Empty : $" — {Hint}");
}

/// <summary>
/// El DSL no se pudo compilar a un modelo válido.
/// </summary>
/// <remarks>
/// Acumula <b>todos</b> los errores en lugar de fallar en el primero, porque al escribir un
/// modelo a mano lo normal es equivocarse en varias relaciones a la vez y es mucho más
/// cómodo verlas todas juntas.
/// </remarks>
public sealed class ModelValidationException(IReadOnlyList<ModelValidationError> errors)
    : Exception(BuildMessage(errors))
{
    public IReadOnlyList<ModelValidationError> Errors { get; } = errors;

    public ModelValidationException(int line, string message, string? hint = null)
        : this([new ModelValidationError(line, message, hint)])
    {
    }

    private static string BuildMessage(IReadOnlyList<ModelValidationError> errors) =>
        errors.Count == 1
            ? $"El modelo de autorización no es válido. {errors[0]}"
            : $"El modelo de autorización tiene {errors.Count} errores:"
              + Environment.NewLine
              + string.Join(Environment.NewLine, errors.Select(error => $"  · {error}"));
}
