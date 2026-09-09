namespace Playground.AccessControl.Domain.Abstractions;

/// <summary>
/// Fuente de objetos candidatos de un tipo dado.
/// </summary>
/// <remarks>
/// <para>
/// <b>Esta interfaz existe por un problema real y muy instructivo</b>, y merece la pena
/// entenderlo porque le pasa a cualquiera que integre un sistema tipo Zanzibar.
/// </para>
/// <para>
/// El módulo de control de acceso <b>no conoce el catálogo de objetos del sistema</b>. Sabe
/// que existe <c>project:alpha</c> únicamente porque alguien escribió una tupla que lo
/// menciona. Si creas un proyecto y no le pones ninguna relación, para el módulo de control
/// de acceso ese proyecto <i>no existe</i>. Y es correcto que sea así: el catálogo es del
/// negocio, y están en bases de datos distintas (a propósito, ver <c>docker-compose.yml</c>).
/// </para>
/// <para>
/// La consecuencia práctica aparece justo en la estrategia
/// <c>ListObjectsStrategy.Naive</c>, que necesita enumerar candidatos para hacerles un Check
/// a cada uno. ¿De dónde saca la lista? Hay dos respuestas, y las dos son legítimas:
/// </para>
/// <list type="number">
///   <item>
///     <b>De las propias tuplas</b> (<c>TupleDerivedObjectCatalog</c>, el que se usa por
///     defecto): objetos distintos que aparecen en alguna tupla. Es autónomo pero
///     <i>incompleto</i>: se pierde los objetos huérfanos, que son justo los que nadie
///     puede ver. Para responder "qué puedo ver" da igual; para responder "qué NO puedo
///     ver" no sirve.
///   </item>
///   <item>
///     <b>Preguntando al negocio</b>: completo, pero acopla el módulo de control de acceso
///     al servicio que debía gobernar, e invierte la dependencia en el peor sentido.
///   </item>
/// </list>
/// <para>
/// Por eso en la práctica el patrón correcto no es ninguno de los dos: es que <b>el negocio
/// haga el filtrado</b>. El negocio enumera sus proyectos (que conoce) y pide un
/// <c>BatchCheck</c> o un <c>ListObjects</c> con el que intersecar. Ver <c>docs/04</c>.
/// </para>
/// </remarks>
public interface IObjectCatalog
{
    /// <summary>
    /// Identificadores conocidos de objetos del tipo indicado.
    /// </summary>
    Task<IReadOnlyList<string>> GetObjectIdsAsync(
        string objectType,
        CancellationToken cancellationToken = default);
}
