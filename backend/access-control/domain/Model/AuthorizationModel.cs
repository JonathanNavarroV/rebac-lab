using System.Diagnostics.CodeAnalysis;

namespace Playground.AccessControl.Domain.Model;

/// <summary>
/// Definición de una relación dentro de un tipo: su nombre y la regla que determina quién
/// la tiene.
/// </summary>
/// <param name="Comment">
/// Explicación en lenguaje natural. No es un adorno: es lo que el motor usa para redactar
/// el "por qué" de cada decisión en el Authorization Explorer. Un modelo sin comentarios
/// produce explicaciones secas; con ellos, la traza se lee casi como una frase.
/// </param>
public sealed record RelationDefinition(
    string Name,
    UsersetRewrite Rewrite,
    string? Comment = null)
{
    /// <summary>
    /// <c>true</c> si la relación se puede escribir como tupla. Las relaciones puramente
    /// derivadas (<c>can_edit: editor or owner</c>) no admiten tuplas directas: intentar
    /// escribir <c>project:alpha#can_edit@user:juan</c> es un error de modelado, porque
    /// estarías materializando un permiso calculado.
    /// </summary>
    public bool IsDirectlyAssignable => ContainsThis(Rewrite);

    /// <summary>
    /// Asignaciones directas admitidas, recogidas de todas las ramas <c>_this</c> del árbol.
    /// Se usa para validar tuplas entrantes y para guiar la expansión inversa.
    /// </summary>
    public IReadOnlyList<UsersetRewrite.DirectAssignment> DirectAssignments =>
        CollectAssignments(Rewrite).ToList();

    private static bool ContainsThis(UsersetRewrite rewrite) => rewrite switch
    {
        UsersetRewrite.This => true,
        UsersetRewrite.Union u => u.Children.Any(ContainsThis),
        UsersetRewrite.Intersection i => i.Children.Any(ContainsThis),
        UsersetRewrite.Exclusion e => ContainsThis(e.Base),
        _ => false,
    };

    private static IEnumerable<UsersetRewrite.DirectAssignment> CollectAssignments(UsersetRewrite rewrite) =>
        rewrite switch
        {
            UsersetRewrite.This t => t.Allowed,
            UsersetRewrite.Union u => u.Children.SelectMany(CollectAssignments),
            UsersetRewrite.Intersection i => i.Children.SelectMany(CollectAssignments),
            UsersetRewrite.Exclusion e => CollectAssignments(e.Base).Concat(CollectAssignments(e.Subtract)),
            _ => [],
        };
}

/// <summary>
/// Un tipo del modelo: <c>project</c>, <c>folder</c>, <c>user</c>... con sus relaciones.
/// </summary>
public sealed record TypeDefinition(
    string Name,
    IReadOnlyDictionary<string, RelationDefinition> Relations,
    string? Comment = null)
{
    public bool TryGetRelation(string relation, [NotNullWhen(true)] out RelationDefinition? definition) =>
        Relations.TryGetValue(relation, out definition);
}

/// <summary>
/// El modelo de autorización completo: la "configuración de namespaces" del paper de
/// Zanzibar.
/// </summary>
/// <remarks>
/// <para>
/// Conviene separar mentalmente las dos mitades del sistema, porque se comportan de forma
/// muy distinta:
/// </para>
/// <list type="table">
///   <listheader><term>Mitad</term><description>Naturaleza</description></listheader>
///   <item><term>El modelo (esta clase)</term><description>
///     Pequeño, cambia poquísimo (una release), lo escribe un desarrollador, cabe en un
///     fichero de texto. Define <i>qué relaciones existen y cómo se derivan</i>.
///   </description></item>
///   <item><term>Las tuplas</term><description>
///     Enormes (miles de millones en Google), cambian constantemente, las escriben los
///     usuarios al invitar a alguien o mover una carpeta. Son <i>los hechos</i>.
///   </description></item>
/// </list>
/// <para>
/// Esa asimetría es la que permite cachear el modelo en memoria de forma agresiva mientras
/// las tuplas se consultan en cada Check.
/// </para>
/// <para>
/// El modelo está <b>versionado</b> e inmutable: publicar un modelo nuevo no modifica el
/// anterior, crea otra versión. Así puedes responder la misma pregunta con dos modelos
/// distintos y comparar, que es exactamente lo que necesita el laboratorio para el toggle
/// de "¿y si la herencia por carpetas estuviera desactivada?".
/// </para>
/// </remarks>
public sealed record AuthorizationModel(
    string Id,
    string SchemaVersion,
    IReadOnlyDictionary<string, TypeDefinition> Types,
    string RawDsl,
    string? Name = null,
    string? Description = null)
{
    public bool TryGetType(string type, [NotNullWhen(true)] out TypeDefinition? definition) =>
        Types.TryGetValue(type, out definition);

    /// <summary>
    /// Busca la definición de <paramref name="relation"/> en <paramref name="type"/>.
    /// Devuelve <c>false</c> si el tipo o la relación no existen en el modelo, que es un
    /// caso perfectamente normal en un laboratorio: preguntar por una relación inexistente
    /// debe producir un DENY explicado, no una excepción.
    /// </summary>
    public bool TryGetRelation(string type, string relation, [NotNullWhen(true)] out RelationDefinition? definition)
    {
        definition = null;
        return Types.TryGetValue(type, out var typeDefinition)
            && typeDefinition.TryGetRelation(relation, out definition);
    }

    /// <summary>
    /// Todas las relaciones de <paramref name="objectType"/> que pueden acabar concediendo
    /// <paramref name="relation"/>, incluida ella misma.
    /// </summary>
    /// <remarks>
    /// Es la pieza que hace viable la expansión inversa de ListObjects: para responder "qué
    /// proyectos puede ver Juan" necesitamos saber que <c>can_view</c> se alimenta de
    /// <c>viewer</c>, <c>editor</c> y <c>owner</c>, y por tanto que basta buscar tuplas de
    /// esas tres relaciones. Sin esto habría que probar objeto por objeto.
    /// </remarks>
    public IReadOnlySet<string> GetContributingRelations(string objectType, string relation)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        Collect(objectType, relation, result);
        return result;
    }

    private void Collect(string objectType, string relation, HashSet<string> accumulator)
    {
        // Corta la recursión tanto en relaciones ya visitadas como en ciclos del modelo.
        if (!accumulator.Add(relation))
            return;

        if (!TryGetRelation(objectType, relation, out var definition))
            return;

        Walk(definition.Rewrite);

        void Walk(UsersetRewrite rewrite)
        {
            switch (rewrite)
            {
                case UsersetRewrite.This:
                    // Caso base: la propia relación ya está en el acumulador.
                    break;

                case UsersetRewrite.ComputedUserset computed:
                    Collect(objectType, computed.Relation, accumulator);
                    break;

                case UsersetRewrite.TupleToUserset ttu:
                    // La relación que se recorre (parent) también contribuye: sin tuplas de
                    // parent no hay herencia que seguir.
                    accumulator.Add(ttu.Tupleset);
                    // La relación computada se evalúa en OTRO tipo de objeto, que no
                    // conocemos aquí sin mirar las tuplas. La expansión inversa la resuelve
                    // propagando por la jerarquía; ver ListObjectsReverseStrategy.
                    break;

                case UsersetRewrite.Union union:
                    union.Children.ToList().ForEach(Walk);
                    break;

                case UsersetRewrite.Intersection intersection:
                    intersection.Children.ToList().ForEach(Walk);
                    break;

                case UsersetRewrite.Exclusion exclusion:
                    // Solo la base contribuye a conceder. La rama sustraída quita, no da.
                    Walk(exclusion.Base);
                    break;
            }
        }
    }
}
