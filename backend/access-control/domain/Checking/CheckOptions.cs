namespace Playground.AccessControl.Domain.Checking;

/// <summary>
/// Ajustes de una evaluación. Todos existen para poder <i>experimentar</i>: en un sistema
/// de producción varios de ellos serían constantes.
/// </summary>
public sealed record CheckOptions
{
    /// <summary>
    /// Configuración por defecto: la que usaría el negocio. Cortocircuita en el primer
    /// ALLOW y memoiza, porque lo único que necesita es un booleano rápido.
    /// </summary>
    public static readonly CheckOptions Default = new();

    /// <summary>
    /// Configuración del Authorization Explorer: explora todas las ramas para poder mostrar
    /// los múltiples caminos y las ramas fallidas. Más caro a propósito.
    /// </summary>
    public static readonly CheckOptions Explain = new()
    {
        FindAllPaths = true,
        IncludeFailedBranches = true,
        SuggestMissingTuples = true,
    };

    /// <summary>
    /// Profundidad máxima de recursión. Zanzibar usa un límite similar (~25) y devuelve
    /// error si se supera.
    /// </summary>
    /// <remarks>
    /// El límite no es una optimización, es una <b>necesidad de disponibilidad</b>: sin él,
    /// una jerarquía de carpetas muy profunda o un modelo mal escrito convierten un Check en
    /// una consulta ilimitada, y como el Check está en el camino crítico de cada petición,
    /// eso tumba el servicio entero. Bajarlo a 2 o 3 en el laboratorio permite ver cómo un
    /// recurso enterrado deja de ser accesible: la respuesta pasa a ser "no lo sé", no "no".
    /// </remarks>
    public int MaxDepth { get; init; } = 25;

    /// <summary>
    /// Si es <c>false</c>, la primera rama de una unión que conceda acceso termina la
    /// evaluación. Si es <c>true</c>, se exploran todas para recolectar todos los caminos.
    /// </summary>
    public bool FindAllPaths { get; init; }

    /// <summary>
    /// Incluir en la traza las ramas que denegaron. Imprescindible para explicar un DENY,
    /// que es donde más se aprende.
    /// </summary>
    public bool IncludeFailedBranches { get; init; } = true;

    /// <summary>
    /// Cachear resultados de subárboles dentro de <b>este</b> Check.
    /// </summary>
    /// <remarks>
    /// Es una caché de vida cortísima (un request) y por eso es segura: no puede quedar
    /// obsoleta porque no sobrevive a la petición. Poder apagarla sirve para ver en las
    /// métricas cuántos nodos repetidos tiene un modelo, que es una medida indirecta de lo
    /// enredado que está.
    /// </remarks>
    public bool EnableMemoization { get; init; } = true;

    /// <summary>
    /// Calcular qué tuplas bastaría crear para que un DENY pasara a ALLOW. Es la parte más
    /// didáctica del explicador y la que conecta con el botón "crear esta relación".
    /// </summary>
    public bool SuggestMissingTuples { get; init; }

    /// <summary>Identificador del modelo a usar. <c>null</c> = el último publicado.</summary>
    public string? ModelId { get; init; }
}
