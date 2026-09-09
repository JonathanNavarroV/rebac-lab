using Playground.AccessControl.Domain.Exceptions;
using Playground.AccessControl.Domain.Model;

namespace Playground.AccessControl.Application.Authorization.Model;

/// <summary>
/// Compila el DSL de autorización a un <see cref="AuthorizationModel"/> y lo valida.
/// </summary>
/// <remarks>
/// <para>
/// El DSL que acepta es el de OpenFGA (schema 1.1), con dos añadidos que solo facilitan el
/// aprendizaje y no rompen la compatibilidad: paréntesis en las expresiones y comentarios
/// con <c>//</c> que se conservan y se muestran en la pantalla del modelo.
/// </para>
/// <para>
/// La <b>validación</b> es la parte que más se agradece al experimentar, y merece explicarse.
/// Un modelo mal escrito no falla al escribirlo: falla mucho más tarde, cuando un Check
/// devuelve DENY por una razón que no tiene nada que ver con las tuplas. Ejemplos reales de
/// cosas que este parser rechaza en el momento de publicar:
/// </para>
/// <list type="bullet">
///   <item>
///     <c>define can_edit: editorr</c> — un typo en una relación computada. Sin validación,
///     esto es una rama que <b>siempre</b> deniega y nunca sabrás por qué.
///   </item>
///   <item>
///     <c>define viewer: [teams#member]</c> — un tipo que no existe. Las tuplas que
///     escribieras contra él nunca coincidirían con nada.
///   </item>
///   <item>
///     <c>define can_view: can_view from parent</c> cuando <c>parent</c> no está definida en
///     ese tipo. La herencia estaría desconectada en silencio.
///   </item>
/// </list>
/// <para>
/// Fallar temprano y con un mensaje que dice cómo arreglarlo es, en un laboratorio, más
/// valioso que cualquier otra funcionalidad.
/// </para>
/// </remarks>
public sealed class AuthorizationModelDslParser
{
    private const string DefaultSchemaVersion = "1.1";

    /// <summary>
    /// Compila el DSL. <paramref name="modelId"/> se usa tal cual si se indica; si no, se
    /// deriva de un hash estable del propio DSL, de forma que el mismo texto produce siempre
    /// el mismo identificador (útil para el seed y para los tests).
    /// </summary>
    /// <exception cref="ModelValidationException">Si el DSL no es válido.</exception>
    public AuthorizationModel Parse(string dsl, string? modelId = null, string? name = null, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dsl);

        var errors = new List<ModelValidationError>();
        var types = new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);

        var schemaVersion = DefaultSchemaVersion;
        string? currentTypeName = null;
        Dictionary<string, RelationDefinition>? currentRelations = null;
        var currentTypeComment = (string?)null;
        var pendingComment = (string?)null;
        var insideRelationsBlock = false;

        void CloseCurrentType()
        {
            if (currentTypeName is null)
                return;

            types[currentTypeName] = new TypeDefinition(
                currentTypeName,
                currentRelations ?? new Dictionary<string, RelationDefinition>(StringComparer.Ordinal),
                currentTypeComment);
        }

        var lines = dsl.ReplaceLineEndings("\n").Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var (content, comment) = SplitComment(lines[index]);

            if (content.Length == 0)
            {
                // Una línea que solo tiene comentario se guarda para adjuntarla a la
                // siguiente declaración. Así 'define' y 'type' heredan la explicación que
                // el autor escribió encima.
                if (comment is not null)
                    pendingComment = pendingComment is null ? comment : $"{pendingComment} {comment}";

                continue;
            }

            // El comentario al final de la misma línea tiene prioridad sobre el de arriba.
            var attachedComment = comment ?? pendingComment;
            pendingComment = null;

            try
            {
                if (content.Equals("model", StringComparison.OrdinalIgnoreCase))
                {
                    insideRelationsBlock = false;
                    continue;
                }

                if (content.StartsWith("schema ", StringComparison.OrdinalIgnoreCase))
                {
                    schemaVersion = content["schema ".Length..].Trim();
                    continue;
                }

                if (content.StartsWith("type ", StringComparison.OrdinalIgnoreCase))
                {
                    CloseCurrentType();

                    var typeName = content["type ".Length..].Trim();

                    if (!IsValidIdentifier(typeName))
                    {
                        errors.Add(new ModelValidationError(lineNumber,
                            $"'{typeName}' no es un nombre de tipo válido.",
                            "Se admiten letras, dígitos, '_' y '-', empezando por letra."));
                        currentTypeName = null;
                        currentRelations = null;
                        continue;
                    }

                    if (types.ContainsKey(typeName))
                    {
                        errors.Add(new ModelValidationError(lineNumber,
                            $"El tipo '{typeName}' está declarado dos veces.",
                            "Junta todas sus relaciones en un solo bloque 'type'."));
                    }

                    currentTypeName = typeName;
                    currentRelations = new Dictionary<string, RelationDefinition>(StringComparer.Ordinal);
                    currentTypeComment = attachedComment;
                    insideRelationsBlock = false;
                    continue;
                }

                if (content.Equals("relations", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentTypeName is null)
                    {
                        errors.Add(new ModelValidationError(lineNumber,
                            "'relations' aparece antes de declarar ningún 'type'.",
                            "Cada bloque 'relations' pertenece al 'type' que lo precede."));
                        continue;
                    }

                    insideRelationsBlock = true;
                    continue;
                }

                if (content.StartsWith("define ", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentTypeName is null || currentRelations is null)
                    {
                        errors.Add(new ModelValidationError(lineNumber,
                            "'define' aparece fuera de un 'type'.",
                            "Las relaciones se declaran dentro de 'type X' / 'relations'."));
                        continue;
                    }

                    if (!insideRelationsBlock)
                    {
                        errors.Add(new ModelValidationError(lineNumber,
                            $"'define' aparece sin un bloque 'relations' en el tipo '{currentTypeName}'.",
                            "Añade una línea 'relations' entre el 'type' y sus 'define'."));
                        continue;
                    }

                    var body = content["define ".Length..];
                    var colonIndex = body.IndexOf(':');

                    if (colonIndex <= 0)
                    {
                        errors.Add(new ModelValidationError(lineNumber,
                            "Falta ':' en la definición de la relación.",
                            "Se escribe 'define <relación>: <expresión>'."));
                        continue;
                    }

                    var relationName = body[..colonIndex].Trim();
                    var expression = body[(colonIndex + 1)..].Trim();

                    if (!IsValidIdentifier(relationName))
                    {
                        errors.Add(new ModelValidationError(lineNumber,
                            $"'{relationName}' no es un nombre de relación válido."));
                        continue;
                    }

                    if (currentRelations.ContainsKey(relationName))
                    {
                        errors.Add(new ModelValidationError(lineNumber,
                            $"La relación '{relationName}' está definida dos veces en '{currentTypeName}'.",
                            "Si querías combinar dos formas de concederla, únelas con 'or' en una sola definición."));
                        continue;
                    }

                    var rewrite = DslExpressionParser.Parse(expression, lineNumber);
                    currentRelations[relationName] = new RelationDefinition(relationName, rewrite, attachedComment);
                    continue;
                }

                errors.Add(new ModelValidationError(lineNumber,
                    $"No se entiende la línea: '{content}'.",
                    "Las líneas válidas son 'model', 'schema X.Y', 'type <nombre>', 'relations' y 'define <relación>: <expresión>'."));
            }
            catch (ModelValidationException exception)
            {
                // Se acumulan los errores de expresión en lugar de abortar, para poder
                // reportar todas las líneas malas de una vez.
                errors.AddRange(exception.Errors);
            }
        }

        CloseCurrentType();

        if (types.Count == 0)
            errors.Add(new ModelValidationError(0, "El modelo no declara ningún tipo."));

        errors.AddRange(Validate(types));

        if (errors.Count > 0)
            throw new ModelValidationException(errors);

        return new AuthorizationModel(
            Id: modelId ?? BuildDeterministicId(dsl),
            SchemaVersion: schemaVersion,
            Types: types,
            RawDsl: dsl.Trim(),
            Name: name,
            Description: description);
    }

    /// <summary>
    /// Comprobaciones que no se pueden hacer línea a línea, porque necesitan el modelo
    /// completo: toda referencia a un tipo o a una relación debe resolverse.
    /// </summary>
    private static IEnumerable<ModelValidationError> Validate(Dictionary<string, TypeDefinition> types)
    {
        var errors = new List<ModelValidationError>();

        foreach (var type in types.Values)
        {
            foreach (var relation in type.Relations.Values)
                Walk(type, relation, relation.Rewrite);
        }

        return errors;

        void Walk(TypeDefinition type, RelationDefinition relation, UsersetRewrite rewrite)
        {
            switch (rewrite)
            {
                case UsersetRewrite.This direct:
                    direct.Allowed.ToList().ForEach(assignment => ValidateAssignment(type, relation, assignment));
                    break;

                case UsersetRewrite.ComputedUserset computed:
                    if (!type.Relations.ContainsKey(computed.Relation))
                    {
                        errors.Add(new ModelValidationError(0,
                            $"En '{type.Name}.{relation.Name}' se referencia la relación '{computed.Relation}', que no existe en el tipo '{type.Name}'.",
                            $"Relaciones disponibles en '{type.Name}': {FormatAvailable(type)}."));
                    }
                    else if (computed.Relation == relation.Name)
                    {
                        errors.Add(new ModelValidationError(0,
                            $"'{type.Name}.{relation.Name}' se define en términos de sí misma sin pasar por otro objeto.",
                            "Una relación computada sobre el MISMO objeto no puede ser recursiva porque no termina nunca. " +
                            "Si lo que quieres es herencia, usa 'from': 'define can_view: can_view from parent'."));
                    }

                    break;

                case UsersetRewrite.TupleToUserset ttu:
                    ValidateTupleToUserset(type, relation, ttu);
                    break;

                case UsersetRewrite.Union union:
                    union.Children.ToList().ForEach(child => Walk(type, relation, child));
                    break;

                case UsersetRewrite.Intersection intersection:
                    intersection.Children.ToList().ForEach(child => Walk(type, relation, child));
                    break;

                case UsersetRewrite.Exclusion exclusion:
                    Walk(type, relation, exclusion.Base);
                    Walk(type, relation, exclusion.Subtract);
                    break;
            }
        }

        void ValidateAssignment(TypeDefinition type, RelationDefinition relation, UsersetRewrite.DirectAssignment assignment)
        {
            if (!types.TryGetValue(assignment.SubjectType, out var subjectType))
            {
                errors.Add(new ModelValidationError(0,
                    $"En '{type.Name}.{relation.Name}' se admite el tipo '{assignment.SubjectType}', que no está declarado.",
                    $"Tipos declarados: {string.Join(", ", types.Keys.Order())}."));
                return;
            }

            if (assignment.SubjectRelation is not null && !subjectType.Relations.ContainsKey(assignment.SubjectRelation))
            {
                errors.Add(new ModelValidationError(0,
                    $"En '{type.Name}.{relation.Name}' se admite el userset '{assignment.ToDsl()}', pero el tipo '{assignment.SubjectType}' no tiene la relación '{assignment.SubjectRelation}'.",
                    $"Relaciones disponibles en '{assignment.SubjectType}': {FormatAvailable(subjectType)}."));
            }
        }

        void ValidateTupleToUserset(TypeDefinition type, RelationDefinition relation, UsersetRewrite.TupleToUserset ttu)
        {
            // El tupleset (normalmente 'parent') tiene que ser una relación de ESTE tipo:
            // es la que se recorre para encontrar los objetos de arriba.
            if (!type.Relations.TryGetValue(ttu.Tupleset, out var tuplesetRelation))
            {
                errors.Add(new ModelValidationError(0,
                    $"En '{type.Name}.{relation.Name}' se recorre '{ttu.Tupleset}', que no es una relación de '{type.Name}'.",
                    $"'X from Y' exige que 'Y' sea una relación del propio tipo. Relaciones disponibles en '{type.Name}': {FormatAvailable(type)}."));
                return;
            }

            var parentTypes = tuplesetRelation.DirectAssignments
                .Where(assignment => !assignment.Wildcard && assignment.SubjectRelation is null)
                .Select(assignment => assignment.SubjectType)
                .Distinct()
                .ToList();

            if (parentTypes.Count == 0)
            {
                errors.Add(new ModelValidationError(0,
                    $"En '{type.Name}.{relation.Name}' se recorre '{ttu.Tupleset}', pero esa relación no admite ningún objeto asignable directamente.",
                    $"Para poder subir por la jerarquía, '{ttu.Tupleset}' debe declararse como '[<tipo>]', por ejemplo 'define parent: [folder, project]'."));
                return;
            }

            // La relación computada se evalúa en los objetos de arriba, así que tiene que
            // existir en al menos uno de esos tipos. Si no existe en ninguno, la rama está
            // muerta y nunca concederá nada.
            var typesWithRelation = parentTypes
                .Where(parentType => types.TryGetValue(parentType, out var parent) && parent.Relations.ContainsKey(ttu.ComputedRelation))
                .ToList();

            if (typesWithRelation.Count == 0)
            {
                errors.Add(new ModelValidationError(0,
                    $"En '{type.Name}.{relation.Name}' se pregunta '{ttu.ComputedRelation}' en el padre, pero ninguno de los tipos padre ({string.Join(", ", parentTypes)}) tiene esa relación.",
                    "Esa rama nunca concedería acceso. Comprueba el nombre de la relación o añádela al tipo padre."));
            }
        }

        string FormatAvailable(TypeDefinition type) =>
            type.Relations.Count == 0
                ? "(ninguna)"
                : string.Join(", ", type.Relations.Keys.Order());
    }

    /// <summary>
    /// Separa el contenido del comentario. Se admite <c>//</c> en cualquier posición y
    /// <c>#</c> solo al principio de la línea.
    /// </summary>
    /// <remarks>
    /// La asimetría es a propósito: en el DSL de OpenFGA el comentario es <c>#</c>, pero ese
    /// mismo carácter separa el userset en <c>team#member</c>. Aceptarlo en cualquier
    /// posición obligaría a llevar la cuenta de los corchetes en el tokenizador para saber si
    /// un <c>#</c> es comentario o no. Restringirlo al inicio de línea evita toda la
    /// ambigüedad y deja <c>//</c> para los comentarios al final.
    /// </remarks>
    private static (string Content, string? Comment) SplitComment(string line)
    {
        var trimmed = line.Trim();

        if (trimmed.StartsWith('#'))
            return (string.Empty, Clean(trimmed[1..]));

        var index = trimmed.IndexOf("//", StringComparison.Ordinal);

        return index < 0
            ? (trimmed, null)
            : (trimmed[..index].Trim(), Clean(trimmed[(index + 2)..]));

        static string? Clean(string comment)
        {
            var value = comment.Trim();
            return value.Length == 0 ? null : value;
        }
    }

    private static bool IsValidIdentifier(string value) =>
        value.Length > 0
        && char.IsLetter(value[0])
        && value.All(character => char.IsLetterOrDigit(character) || character is '_' or '-');

    /// <summary>
    /// Identificador derivado del contenido: el mismo DSL produce siempre el mismo id.
    /// </summary>
    /// <remarks>
    /// Facilita mucho el seed y los tests: republicar un modelo idéntico no genera una
    /// versión nueva ni invalida las decisiones auditadas con la anterior.
    /// </remarks>
    private static string BuildDeterministicId(string dsl)
    {
        var normalized = string.Join('\n',
            dsl.ReplaceLineEndings("\n")
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0));

        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized));

        return Convert.ToHexStringLower(hash)[..16];
    }
}
