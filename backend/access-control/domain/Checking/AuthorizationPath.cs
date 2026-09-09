using Playground.AccessControl.Domain.Model;

namespace Playground.AccessControl.Domain.Checking;

/// <summary>Tipo de salto dado dentro de un camino de autorización.</summary>
public enum PathStepKind
{
    /// <summary>Tupla escrita directamente para el sujeto: <c>resource:a#owner@user:juan</c>.</summary>
    DirectTuple,

    /// <summary>
    /// Tupla cuyo sujeto es un userset, que obliga a comprobar la pertenencia:
    /// <c>project:alpha#editor@team:backend#member</c>.
    /// </summary>
    UsersetTuple,

    /// <summary>Salto a otra relación del mismo objeto: <c>can_edit</c> → <c>editor</c>.</summary>
    ComputedRelation,

    /// <summary>Salto a un objeto padre siguiendo el tupleset: <c>can_view from parent</c>.</summary>
    ParentTraversal,

    /// <summary>Coincidencia por comodín: <c>resource:a#viewer@user:*</c>.</summary>
    Wildcard,
}

/// <summary>Un salto concreto dentro de un camino.</summary>
public sealed record PathStep(
    PathStepKind Kind,
    string Description,
    TupleKey? Tuple = null);

/// <summary>
/// Un camino completo que concede acceso, del sujeto al objeto.
/// </summary>
/// <remarks>
/// <para>
/// Un Check puede devolver <b>varios</b> caminos, y eso no es un detalle cosmético: es una
/// de las diferencias importantes con RBAC. En RBAC la pregunta "¿por qué tiene acceso?"
/// tiene una respuesta ("por el rol X"); en ReBAC puede tener tres a la vez, y las
/// implicaciones prácticas son grandes:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Revocar es más difícil.</b> Sacar a Juan del equipo backend no le quita el acceso
///     si además es owner del recurso. Si no ves todos los caminos, crees que revocaste y
///     no revocaste nada.
///   </item>
///   <item>
///     <b>El acceso es más resistente.</b> Justo lo contrario visto en positivo: el acceso
///     legítimo no se rompe porque alguien reorganice un equipo.
///   </item>
/// </list>
/// <para>
/// Por eso el motor tiene un modo <c>FindAllPaths</c>: en producción interesa cortocircuitar
/// en el primer ALLOW (es más rápido), pero para entender el sistema interesa ver todos.
/// </para>
/// </remarks>
public sealed record AuthorizationPath(IReadOnlyList<PathStep> Steps)
{
    /// <summary>Número de saltos. Un camino más corto suele ser un acceso más directo.</summary>
    public int Length => Steps.Count;

    /// <summary>
    /// Representación en cadena del camino, tal como se muestra en el Explorer:
    /// <c>user:juan --member--> team:backend --editor--> project:alpha</c>.
    /// </summary>
    public string ToChainString()
    {
        var tuples = Steps
            .Where(step => step.Tuple is not null)
            .Select(step => step.Tuple!)
            .ToList();

        if (tuples.Count == 0)
            return string.Join(" · ", Steps.Select(step => step.Description));

        // Se recorre en orden inverso porque la evaluación va del objeto al sujeto
        // (top-down), pero se lee mucho mejor del sujeto al objeto: primero quién es Juan,
        // luego a dónde llega.
        var chain = tuples
            .AsEnumerable()
            .Reverse()
            .Select(tuple => $"{tuple.Subject} --{tuple.Relation}--> {tuple.Object}")
            .ToList();

        return string.Join("  ", chain);
    }

    public override string ToString() => ToChainString();
}
