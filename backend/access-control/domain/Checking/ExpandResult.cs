using Playground.AccessControl.Domain.Model;

namespace Playground.AccessControl.Domain.Checking;

/// <summary>
/// Nodo del árbol de <c>Expand</c>: la respuesta a "¿quién tiene esta relación sobre este
/// objeto?".
/// </summary>
/// <remarks>
/// <para>
/// <c>Expand</c> es la tercera operación del paper, junto a <c>Check</c> y <c>Read</c>, y es
/// la que se suele olvidar. Responde a una pregunta distinta de las otras dos:
/// </para>
/// <list type="table">
///   <item><term><c>Check</c></term><description>
///     ¿<i>este</i> sujeto tiene <i>esta</i> relación? → un booleano. Una pregunta cerrada.
///   </description></item>
///   <item><term><c>Expand</c></term><description>
///     ¿<i>quiénes</i> tienen esta relación? → un árbol de usersets <b>sin resolver</b>.
///   </description></item>
///   <item><term><c>ListObjects</c></term><description>
///     ¿sobre <i>qué objetos</i> la tiene este sujeto? → una lista. El problema inverso.
///   </description></item>
/// </list>
/// <para>
/// El detalle importante de <c>Expand</c>: <b>no aplana los usersets a personas</b>. Si el
/// equipo backend tiene 500 miembros, devuelve el nodo <c>team:backend#member</c>, no 500
/// usuarios. Y es deliberado: aplanarlo sería carísimo y además la respuesta caducaría al
/// instante. Por eso las pantallas de "compartido con" de Drive muestran "Equipo Backend"
/// y no la lista completa de personas — están mostrando un Expand.
/// </para>
/// </remarks>
public sealed record ExpandNode
{
    /// <summary>Variante de regla que produjo este nodo.</summary>
    public required string Kind { get; init; }

    /// <summary>Etiqueta legible para la UI y el grafo.</summary>
    public required string Label { get; init; }

    /// <summary>Objeto y relación que este nodo expande.</summary>
    public ObjectRef? Object { get; init; }

    public string? Relation { get; init; }

    /// <summary>
    /// Sujeto concreto, si el nodo es una hoja. Puede ser un individuo
    /// (<c>user:juan</c>), un userset sin resolver (<c>team:backend#member</c>) o un
    /// comodín (<c>user:*</c>).
    /// </summary>
    public SubjectRef? Subject { get; init; }

    /// <summary>Tupla que originó este nodo, si aplica.</summary>
    public TupleKey? Tuple { get; init; }

    public List<ExpandNode> Children { get; init; } = [];
}

/// <summary>Resultado de un <c>Expand</c>.</summary>
public sealed record ExpandResult
{
    public required ObjectRef Object { get; init; }

    public required string Relation { get; init; }

    public required ExpandNode Root { get; init; }

    /// <summary>
    /// Sujetos hoja encontrados, usersets incluidos y sin expandir. Es la lista que se
    /// mostraría en un panel de "quién tiene acceso".
    /// </summary>
    public IReadOnlyList<SubjectRef> Leaves { get; init; } = [];

    public required CheckMetrics Metrics { get; init; }
}
