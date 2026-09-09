using Playground.AccessControl.Domain.Checking;
using Playground.AccessControl.Domain.Model;

namespace Playground.AccessControl.Api.Helpers;

/// <summary>
/// Parseo de las referencias que llegan por la API, con errores que enseñan la notación.
/// </summary>
/// <remarks>
/// Toda la API habla en cadenas (<c>user:juan</c>, <c>project:alpha#editor@team:backend#member</c>)
/// porque esa notación es la del paper y la que se escribe en la documentación y en el
/// frontend. El precio es tener que validarla en la frontera, y aprovechamos para que el
/// mensaje de error explique el formato en lugar de limitarse a rechazarlo.
/// </remarks>
public static class RequestParsing
{
    /// <summary>Acumula errores de validación en el formato que espera el frontend.</summary>
    public sealed class ValidationCollector
    {
        private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

        public bool HasErrors => _errors.Count > 0;

        public void Add(string field, string message)
        {
            if (!_errors.TryGetValue(field, out var messages))
                _errors[field] = messages = [];

            messages.Add(message);
        }

        /// <summary>
        /// Devuelve un <c>ValidationProblemDetails</c> con el mismo formato que usan tus otras
        /// APIs, para que el manejo de errores del frontend sea el de siempre.
        /// </summary>
        public IResult ToResult() =>
            Results.ValidationProblem(
                _errors.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray()),
                title: "Validation failed");
    }

    public static SubjectRef? ParseSubject(string? value, string field, ValidationCollector errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(field, "El sujeto es obligatorio.");
            return null;
        }

        if (SubjectRef.TryParse(value, out var subject))
            return subject;

        errors.Add(field,
            $"'{value}' no es un sujeto válido. Formatos admitidos: 'user:juan' (individuo), "
            + "'team:backend#member' (userset) o 'user:*' (comodín).");

        return null;
    }

    public static ObjectRef? ParseObject(string? value, string field, ValidationCollector errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(field, "El objeto es obligatorio.");
            return null;
        }

        if (ObjectRef.TryParse(value, out var @object))
            return @object;

        errors.Add(field, $"'{value}' no es un objeto válido. Se espera 'tipo:id', por ejemplo 'project:alpha'.");
        return null;
    }

    public static string? ParseRelation(string? value, string field, ValidationCollector errors)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim();

        errors.Add(field, "La relación es obligatoria, por ejemplo 'can_edit'.");
        return null;
    }

    public static TupleKey? ParseTuple(string? value, string field, ValidationCollector errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(field, "La tupla es obligatoria.");
            return null;
        }

        if (TupleKey.TryParse(value, out var tuple))
            return tuple;

        errors.Add(field,
            $"'{value}' no es una tupla válida. Se escribe 'objeto#relación@sujeto', "
            + "por ejemplo 'project:alpha#editor@team:backend#member'.");

        return null;
    }

    public static ListObjectsStrategy? ParseStrategy(string? value, string field, ValidationCollector errors)
    {
        switch ((value ?? "reverse").Trim().ToLowerInvariant())
        {
            case "naive":
                return ListObjectsStrategy.Naive;

            case "reverse" or "reverse-expansion" or "reverseexpansion":
                return ListObjectsStrategy.ReverseExpansion;

            default:
                errors.Add(field,
                    $"'{value}' no es una estrategia válida. Usa 'naive' (enumerar y comprobar uno a uno) "
                    + "o 'reverse' (expansión inversa desde el sujeto).");
                return null;
        }
    }

    /// <summary>Construye las opciones de evaluación a partir de los flags de la petición.</summary>
    public static CheckOptions BuildOptions(bool explain, string? modelId, int? maxDepth)
    {
        var options = explain ? CheckOptions.Explain : CheckOptions.Default;

        return options with
        {
            ModelId = string.IsNullOrWhiteSpace(modelId) ? null : modelId,
            MaxDepth = maxDepth is > 0 ? maxDepth.Value : options.MaxDepth,
        };
    }
}
