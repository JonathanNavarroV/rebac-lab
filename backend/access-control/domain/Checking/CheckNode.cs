using Playground.AccessControl.Domain.Model;

namespace Playground.AccessControl.Domain.Checking;

/// <summary>
/// Un nodo del árbol de evaluación de un Check. El árbol completo <b>es</b> la explicación.
/// </summary>
/// <remarks>
/// <para>
/// Esta clase es la razón por la que el proyecto tiene motor propio en lugar de llamar a
/// OpenFGA. Un <c>Check</c> convencional devuelve <c>true</c> o <c>false</c>; aquí devuelve
/// el recorrido entero, incluidas <b>las ramas que fallaron</b>. Con eso se alimentan tres
/// cosas distintas sin escribir tres veces la lógica:
/// </para>
/// <list type="bullet">
///   <item>el Authorization Explorer (la traza indentada y el "por qué");</item>
///   <item>el grafo de relaciones (los nodos y aristas que se resaltan);</item>
///   <item>la auditoría (el path que se guarda junto a la decisión).</item>
/// </list>
/// <para>
/// Es una clase mutable y no un <c>record</c>, a diferencia del resto del dominio, porque se
/// construye incrementalmente durante un recorrido en profundidad: el nodo se crea al entrar
/// en una rama y su resultado no se conoce hasta que se han evaluado los hijos. Modelarlo
/// como inmutable obligaría a construir el árbol al revés y complicaría gratuitamente la
/// parte que más importa que se entienda.
/// </para>
/// </remarks>
public sealed class CheckNode
{
    /// <summary>Variante de regla evaluada, en la terminología del paper. Ver <see cref="UsersetRewrite.Kind"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>Etiqueta legible para la UI: <c>editor</c>, <c>can_edit ← from parent</c>.</summary>
    public required string Label { get; init; }

    /// <summary>Objeto sobre el que se evaluaba esta rama.</summary>
    public required ObjectRef Object { get; init; }

    /// <summary>Relación que se evaluaba sobre <see cref="Object"/>.</summary>
    public required string Relation { get; init; }

    /// <summary>Sujeto por el que se preguntaba. Cambia al expandir un userset.</summary>
    public required SubjectRef Subject { get; init; }

    /// <summary>Resultado de esta rama.</summary>
    public bool Allowed { get; set; }

    /// <summary>
    /// Detalle de lo ocurrido: <c>"tupla directa encontrada"</c>,
    /// <c>"0 tuplas coincidentes"</c>, <c>"ciclo detectado"</c>,
    /// <c>"profundidad máxima alcanzada"</c>. Es lo que convierte la traza en algo legible.
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>La tupla concreta que se evaluó, cuando la rama es una asignación directa.</summary>
    public TupleKey? Tuple { get; set; }

    /// <summary>Profundidad de recursión, para indentar en la UI y para las métricas.</summary>
    public int Depth { get; init; }

    /// <summary>
    /// <c>true</c> si el resultado se sirvió desde la memoización del propio Check. Se
    /// muestra en la traza porque explica por qué un subárbol aparece resuelto sin hijos.
    /// </summary>
    public bool FromCache { get; set; }

    public List<CheckNode> Children { get; } = [];

    public CheckNode AddChild(CheckNode child)
    {
        Children.Add(child);
        return child;
    }

    /// <summary>
    /// Representación indentada del árbol, tal cual se muestra en el Explorer y en la
    /// salida de los tests cuando uno falla (que es donde más se agradece).
    /// </summary>
    public string ToTraceString(int indent = 0)
    {
        var pad = new string(' ', indent * 3);
        var mark = Allowed ? "✅" : "❌";
        var cache = FromCache ? " [memo]" : string.Empty;
        var detail = Detail is null ? string.Empty : $"  ← {Detail}";

        var lines = new List<string> { $"{pad}{mark} {Label}{cache}{detail}" };
        lines.AddRange(Children.Select(child => child.ToTraceString(indent + 1)));

        return string.Join(Environment.NewLine, lines);
    }

    public override string ToString() => ToTraceString();
}
