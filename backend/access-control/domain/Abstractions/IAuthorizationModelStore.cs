using Playground.AccessControl.Domain.Model;

namespace Playground.AccessControl.Domain.Abstractions;

/// <summary>
/// Almacén de modelos de autorización. Inmutable y versionado: publicar no sobrescribe.
/// </summary>
/// <remarks>
/// <para>
/// Que los modelos sean inmutables no es purismo. Resuelve dos problemas concretos:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Despliegues sin ventana de incoherencia.</b> Si el modelo se editara en sitio,
///     durante unos segundos habría peticiones evaluándose con medio modelo viejo y medio
///     nuevo. Con versiones, cada Check indica con qué modelo se evaluó y la respuesta es
///     reproducible.
///   </item>
///   <item>
///     <b>Auditoría honesta.</b> Un registro que dice "ALLOW" sin decir con qué modelo no
///     sirve de nada seis meses después, cuando el modelo ya cambió tres veces.
///   </item>
/// </list>
/// <para>
/// En el laboratorio da además la posibilidad de tener varias variantes publicadas a la vez
/// (con herencia por carpetas y sin ella, por ejemplo) y responder la misma pregunta con
/// cada una para ver qué cambia.
/// </para>
/// </remarks>
public interface IAuthorizationModelStore
{
    /// <summary>El modelo publicado más reciente. Es el que se usa si no se indica otro.</summary>
    Task<AuthorizationModel> GetLatestAsync(CancellationToken cancellationToken = default);

    /// <summary>Un modelo concreto por identificador, o <c>null</c> si no existe.</summary>
    Task<AuthorizationModel?> GetByIdAsync(string modelId, CancellationToken cancellationToken = default);

    /// <summary>Todos los modelos publicados, del más reciente al más antiguo.</summary>
    Task<IReadOnlyList<AuthorizationModel>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Publica un modelo nuevo a partir de su DSL. Devuelve el modelo compilado.</summary>
    Task<AuthorizationModel> PublishAsync(
        string dsl,
        string? name = null,
        string? description = null,
        CancellationToken cancellationToken = default);
}
