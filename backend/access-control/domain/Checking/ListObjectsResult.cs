using Playground.AccessControl.Domain.Model;

namespace Playground.AccessControl.Domain.Checking;

/// <summary>Estrategia con la que resolver un listado de objetos autorizados.</summary>
/// <remarks>
/// Que la estrategia sea un parámetro es el núcleo del punto 11 del laboratorio: las dos
/// implementaciones son <b>semánticamente equivalentes</b> y <b>abismalmente distintas</b>
/// en coste. Poder pedir la misma respuesta por las dos vías y comparar métricas es la
/// forma más rápida de entender por qué ListObjects es el problema difícil de Zanzibar.
/// </remarks>
public enum ListObjectsStrategy
{
    /// <summary>
    /// Enumerar todos los objetos candidatos del tipo y hacer un Check por cada uno.
    /// </summary>
    /// <remarks>
    /// Trivialmente correcta y por eso sirve de oráculo en los tests. Y trivialmente
    /// inviable: <c>O(N)</c> checks, donde N es el <b>tamaño del catálogo</b>, no el de la
    /// respuesta. Con 20 proyectos no se nota; con 200.000 documentos, la pantalla no carga.
    /// Es el error que casi todo el mundo comete al implementar su primer "lista lo que
    /// puedo ver", y conviene haberlo medido una vez.
    /// </remarks>
    Naive,

    /// <summary>
    /// Partir del sujeto y recorrer el índice inverso hasta los objetos.
    /// </summary>
    /// <remarks>
    /// Es lo que hace de verdad el <c>ListObjects</c> de OpenFGA. El coste pasa a ser
    /// proporcional a <b>lo que el sujeto puede ver</b>, no al catálogo. Tiene tres
    /// complicaciones reales que el laboratorio deja a la vista: la exclusión
    /// (<c>but not</c>) no se puede resolver hacia atrás y obliga a un Check de
    /// confirmación, la paginación no puede ser un <c>LIMIT</c> ingenuo, y el cierre
    /// transitivo de grupos muy grandes necesita un índice aparte (el <i>Leopard</i> del
    /// paper).
    /// </remarks>
    ReverseExpansion,
}

/// <summary>Un objeto autorizado, con el porqué.</summary>
public sealed record AuthorizedObject(
    ObjectRef Object,
    string Reason,
    AuthorizationPath? Path = null);

/// <summary>Resultado de un listado de objetos autorizados.</summary>
public sealed record ListObjectsResult
{
    public required SubjectRef Subject { get; init; }

    public required string Relation { get; init; }

    public required string ObjectType { get; init; }

    public required ListObjectsStrategy Strategy { get; init; }

    public IReadOnlyList<AuthorizedObject> Objects { get; init; } = [];

    /// <summary>
    /// Objetos del tipo que quedaron fuera, con el motivo. Solo se rellena en la estrategia
    /// <see cref="ListObjectsStrategy.Naive"/>, que es la única que los conoce todos.
    /// </summary>
    /// <remarks>
    /// Y ese "solo" es en sí mismo una lección: la expansión inversa <b>no puede</b> decirte
    /// qué te falta por ver, porque nunca llega a mirar los objetos a los que no tienes
    /// acceso. Responder "¿por qué NO puedo ver el proyecto Delta?" requiere un Check
    /// concreto sobre Delta, no un listado.
    /// </remarks>
    public IReadOnlyList<AuthorizedObject> Denied { get; init; } = [];

    public required CheckMetrics Metrics { get; init; }
}
