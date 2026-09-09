using Playground.AccessControl.Domain.Exceptions;
using Playground.AccessControl.Domain.Model;

namespace Playground.AccessControl.Application.Authorization.Model;

/// <summary>
/// Parser de la parte derecha de un <c>define</c>: convierte el texto de una expresión del
/// DSL en un árbol de <see cref="UsersetRewrite"/>.
/// </summary>
/// <remarks>
/// <para>
/// Es un descenso recursivo clásico, deliberadamente pequeño y sin dependencias, para que se
/// pueda leer de arriba abajo. La gramática completa que acepta es esta:
/// </para>
/// <code>
/// expresión   := exclusión
/// exclusión   := unión ( 'but not' unión )*
/// unión       := intersección ( 'or' intersección )*
/// intersección:= primario ( 'and' primario )*
/// primario    := '(' expresión ')'
///              | '[' asignación ( ',' asignación )* ']'      → _this
///              | identificador 'from' identificador          → tuple_to_userset
///              | identificador                               → computed_userset
/// asignación  := tipo | tipo '#' relación | tipo ':*'
/// </code>
/// <para>
/// <b>Precedencia:</b> <c>and</c> liga más fuerte que <c>or</c>, y <c>but not</c> es el más
/// débil. Así <c>a or b but not c</c> se lee <c>(a or b) but not c</c>, que es lo que la
/// gente espera al escribir "puede ver si es viewer o editor, pero no si está bloqueado".
/// </para>
/// <para>
/// El DSL de OpenFGA es más estricto: prohíbe mezclar operadores en el mismo nivel y no tiene
/// paréntesis. Aquí los admitimos porque es un laboratorio y poder escribir
/// <c>(a or b) and (c or d)</c> permite experimentar con modelos que OpenFGA obligaría a
/// descomponer en relaciones intermedias. Los modelos que se escriben sin paréntesis y sin
/// mezclar operadores son válidos en ambos, así que lo que aprendes sigue siendo portable.
/// </para>
/// </remarks>
internal sealed class DslExpressionParser
{
    private readonly List<string> _tokens;
    private readonly int _line;
    private int _position;

    private DslExpressionParser(List<string> tokens, int line)
    {
        _tokens = tokens;
        _line = line;
    }

    /// <summary>
    /// Parsea la expresión. <paramref name="line"/> es solo para poder señalar la línea
    /// exacta en los mensajes de error.
    /// </summary>
    public static UsersetRewrite Parse(string expression, int line)
    {
        var tokens = Tokenize(expression, line);
        if (tokens.Count == 0)
            throw new ModelValidationException(line, "La definición está vacía.",
                "Después de ':' hace falta una expresión, por ejemplo '[user]' o 'owner or editor'.");

        var parser = new DslExpressionParser(tokens, line);
        var rewrite = parser.ParseExpression();

        if (!parser.IsAtEnd)
            throw new ModelValidationException(line,
                $"Sobra texto tras la expresión: '{string.Join(" ", parser._tokens.Skip(parser._position))}'.",
                "Revisa si falta un operador ('or', 'and', 'but not') entre dos términos.");

        return rewrite;
    }

    // ── Análisis léxico ──────────────────────────────────────────────────────────

    /// <summary>
    /// Parte la expresión en tokens. Los caracteres estructurales (<c>[ ] , ( )</c>) son
    /// tokens por sí mismos; el resto se agrupa en identificadores.
    /// </summary>
    private static List<string> Tokenize(string expression, int line)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();

        void FlushCurrent()
        {
            if (current.Length == 0)
                return;

            tokens.Add(current.ToString());
            current.Clear();
        }

        foreach (var character in expression)
        {
            switch (character)
            {
                case '[' or ']' or ',' or '(' or ')':
                    FlushCurrent();
                    tokens.Add(character.ToString());
                    break;

                case var _ when char.IsWhiteSpace(character):
                    FlushCurrent();
                    break;

                // Caracteres válidos dentro de un identificador. Incluye '#' (userset),
                // ':' y '*' (comodín) y '_' / '-' (nombres compuestos).
                case var c when char.IsLetterOrDigit(c) || c is '_' or '-' or '#' or ':' or '*':
                    current.Append(character);
                    break;

                default:
                    throw new ModelValidationException(line,
                        $"Carácter no reconocido '{character}' en la expresión.",
                        "Los nombres admiten letras, dígitos, '_' y '-'; '#' marca un userset y ':*' un comodín.");
            }
        }

        FlushCurrent();
        return tokens;
    }

    // ── Análisis sintáctico ──────────────────────────────────────────────────────

    private bool IsAtEnd => _position >= _tokens.Count;

    private string? Peek => IsAtEnd ? null : _tokens[_position];

    private string Next() => _tokens[_position++];

    private bool TryConsumeKeyword(string keyword)
    {
        if (!string.Equals(Peek, keyword, StringComparison.OrdinalIgnoreCase))
            return false;

        _position++;
        return true;
    }

    /// <summary>Detecta el operador de dos palabras <c>but not</c>.</summary>
    private bool TryConsumeButNot()
    {
        if (!string.Equals(Peek, "but", StringComparison.OrdinalIgnoreCase))
            return false;

        if (_position + 1 >= _tokens.Count
            || !string.Equals(_tokens[_position + 1], "not", StringComparison.OrdinalIgnoreCase))
        {
            throw new ModelValidationException(_line, "Se encontró 'but' sin 'not'.",
                "El operador de exclusión se escribe 'but not', por ejemplo 'viewer but not banned'.");
        }

        _position += 2;
        return true;
    }

    private UsersetRewrite ParseExpression() => ParseExclusion();

    private UsersetRewrite ParseExclusion()
    {
        var left = ParseUnion();

        // Varias exclusiones se anidan por la izquierda: 'a but not b but not c'
        // equivale a '(a but not b) but not c'.
        while (TryConsumeButNot())
            left = new UsersetRewrite.Exclusion(left, ParseUnion());

        return left;
    }

    private UsersetRewrite ParseUnion()
    {
        var children = new List<UsersetRewrite> { ParseIntersection() };

        while (TryConsumeKeyword("or"))
            children.Add(ParseIntersection());

        // Una unión de un solo hijo no aporta nada al árbol y ensuciaría la traza con un
        // nodo 'union' que no decide nada, así que se colapsa.
        return children.Count == 1 ? children[0] : new UsersetRewrite.Union(children);
    }

    private UsersetRewrite ParseIntersection()
    {
        var children = new List<UsersetRewrite> { ParsePrimary() };

        while (TryConsumeKeyword("and"))
            children.Add(ParsePrimary());

        return children.Count == 1 ? children[0] : new UsersetRewrite.Intersection(children);
    }

    private UsersetRewrite ParsePrimary()
    {
        if (IsAtEnd)
            throw new ModelValidationException(_line, "La expresión termina de forma inesperada.",
                "Falta un término después del último operador.");

        var token = Next();

        if (token == "(")
        {
            var inner = ParseExpression();

            if (IsAtEnd || Next() != ")")
                throw new ModelValidationException(_line, "Falta el paréntesis de cierre.");

            return inner;
        }

        if (token == "[")
            return ParseDirectAssignments();

        if (token is "]" or "," or ")")
            throw new ModelValidationException(_line, $"'{token}' aparece fuera de lugar.");

        if (IsReservedKeyword(token))
            throw new ModelValidationException(_line,
                $"'{token}' es un operador, no un nombre de relación.",
                "Comprueba que no falte un término a un lado del operador.");

        // Un identificador suelto es una relación del mismo objeto, salvo que le siga
        // 'from', en cuyo caso es un salto al objeto padre.
        if (TryConsumeKeyword("from"))
        {
            if (IsAtEnd)
                throw new ModelValidationException(_line, "Falta el nombre del tupleset después de 'from'.",
                    "Se escribe '<relación> from <relación_que_se_recorre>', por ejemplo 'can_view from parent'.");

            var tupleset = Next();

            if (IsReservedKeyword(tupleset))
                throw new ModelValidationException(_line,
                    $"Se esperaba un nombre de relación después de 'from', pero llegó el operador '{tupleset}'.");

            if (tupleset.Contains(SubjectRef.RelationSeparator) || tupleset.Contains(ObjectRef.Separator))
                throw new ModelValidationException(_line,
                    $"'{tupleset}' no es un nombre de relación válido para un tupleset.",
                    "Después de 'from' va el nombre simple de una relación de este mismo tipo, como 'parent'.");

            return new UsersetRewrite.TupleToUserset(tupleset, token);
        }

        if (token.Contains(SubjectRef.RelationSeparator))
            throw new ModelValidationException(_line,
                $"'{token}' usa '#' fuera de una lista de asignación directa.",
                "Los usersets como 'team#member' solo se escriben dentro de corchetes: '[team#member]'.");

        return new UsersetRewrite.ComputedUserset(token);
    }

    private UsersetRewrite ParseDirectAssignments()
    {
        var assignments = new List<UsersetRewrite.DirectAssignment>();

        // Lista vacía: '[]'. Es válida y significa "esta relación no admite tuplas directas,
        // solo se deriva". Poco habitual, pero útil al experimentar.
        if (Peek == "]")
        {
            Next();
            return new UsersetRewrite.This(assignments);
        }

        while (true)
        {
            if (IsAtEnd)
                throw new ModelValidationException(_line, "Falta el corchete de cierre ']'.");

            var token = Next();

            if (token is "[" or "]" or "," or "(" or ")")
                throw new ModelValidationException(_line,
                    $"Se esperaba un tipo de sujeto pero llegó '{token}'.",
                    "La lista se escribe '[user, team#member]'.");

            assignments.Add(ParseAssignment(token));

            if (IsAtEnd)
                throw new ModelValidationException(_line, "Falta el corchete de cierre ']'.");

            var separator = Next();

            if (separator == "]")
                break;

            if (separator != ",")
                throw new ModelValidationException(_line,
                    $"Se esperaba ',' o ']' pero llegó '{separator}'.");
        }

        return new UsersetRewrite.This(assignments);
    }

    private UsersetRewrite.DirectAssignment ParseAssignment(string token)
    {
        // Comodín: 'user:*' → cualquier sujeto de ese tipo.
        if (token.EndsWith(":*", StringComparison.Ordinal))
        {
            var type = token[..^2];

            if (type.Length == 0 || type.Contains(SubjectRef.RelationSeparator))
                throw new ModelValidationException(_line, $"'{token}' no es un comodín válido.",
                    "Se escribe '<tipo>:*', por ejemplo 'user:*'.");

            return UsersetRewrite.DirectAssignment.OfWildcard(type);
        }

        // Userset: 'team#member' → el conjunto de quienes tienen esa relación.
        var hashIndex = token.IndexOf(SubjectRef.RelationSeparator);
        if (hashIndex >= 0)
        {
            var type = token[..hashIndex];
            var relation = token[(hashIndex + 1)..];

            if (type.Length == 0 || relation.Length == 0)
                throw new ModelValidationException(_line, $"'{token}' no es un userset válido.",
                    "Se escribe '<tipo>#<relación>', por ejemplo 'team#member'.");

            if (relation.Contains(SubjectRef.RelationSeparator))
                throw new ModelValidationException(_line, $"'{token}' tiene más de un '#'.");

            return UsersetRewrite.DirectAssignment.OfUserset(type, relation);
        }

        if (token.Contains(ObjectRef.Separator))
            throw new ModelValidationException(_line,
                $"'{token}' no debería llevar ':'.",
                "En la lista de asignación se escribe el TIPO, no un objeto concreto: '[organization]', no '[organization:acme]'. " +
                "La única excepción es el comodín 'user:*'.");

        // Individuo de un tipo: 'user'.
        return UsersetRewrite.DirectAssignment.OfType(token);
    }

    private static bool IsReservedKeyword(string token) =>
        token.Equals("or", StringComparison.OrdinalIgnoreCase)
        || token.Equals("and", StringComparison.OrdinalIgnoreCase)
        || token.Equals("but", StringComparison.OrdinalIgnoreCase)
        || token.Equals("not", StringComparison.OrdinalIgnoreCase)
        || token.Equals("from", StringComparison.OrdinalIgnoreCase);
}
