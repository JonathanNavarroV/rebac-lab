using Playground.AccessControl.Application.Authorization.Model;
using Playground.AccessControl.Domain.Abstractions;
using Playground.AccessControl.Domain.Model;

namespace Playground.AccessControl.Application.Authorization.Storage;

/// <summary>
/// Almacén de modelos en memoria, con el modelo compilado una sola vez.
/// </summary>
/// <remarks>
/// <para>
/// Que el modelo esté cacheado en memoria y las tuplas no lo esté es la decisión de
/// rendimiento más rentable de todo el sistema, y responde a la asimetría descrita en
/// <see cref="AuthorizationModel"/>: el modelo cabe en un fichero y cambia una vez por
/// release; las tuplas son millones y cambian a cada rato.
/// </para>
/// <para>
/// Cachear el modelo de forma agresiva es por tanto seguro y elimina una consulta por Check.
/// Cachear tuplas es lo que obliga a resolver el problema de la invalidación, que es
/// exactamente donde Zanzibar se complica. Ver <c>docs/06</c>.
/// </para>
/// </remarks>
public sealed class InMemoryAuthorizationModelStore : IAuthorizationModelStore
{
    private readonly AuthorizationModelDslParser _parser = new();
    private readonly List<AuthorizationModel> _models = [];
    private readonly Lock _gate = new();

    public InMemoryAuthorizationModelStore()
    {
    }

    /// <summary>Publica los modelos indicados en orden; el último queda como el vigente.</summary>
    public InMemoryAuthorizationModelStore(params string[] dsl)
    {
        dsl.ToList().ForEach(model => Publish(model, null, null));
    }

    public Task<AuthorizationModel> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_models.Count == 0)
                throw new InvalidOperationException(
                    "No hay ningún modelo de autorización publicado. Sin modelo no se puede evaluar nada: "
                    + "el modelo es el que dice qué relaciones existen y cómo se derivan.");

            return Task.FromResult(_models[^1]);
        }
    }

    public Task<AuthorizationModel?> GetByIdAsync(string modelId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_models.LastOrDefault(model => model.Id == modelId));
        }
    }

    public Task<IReadOnlyList<AuthorizationModel>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<AuthorizationModel> result = _models.AsEnumerable().Reverse().ToList();
            return Task.FromResult(result);
        }
    }

    public Task<AuthorizationModel> PublishAsync(
        string dsl,
        string? name = null,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(Publish(dsl, name, description));
        }
    }

    private AuthorizationModel Publish(string dsl, string? name, string? description)
    {
        var model = _parser.Parse(dsl, modelId: null, name: name, description: description);

        // Republicar un modelo idéntico no crea una versión nueva: el id se deriva del
        // contenido, así que reordenar la lista dejaría dos entradas indistinguibles.
        var existing = _models.FindIndex(published => published.Id == model.Id);

        if (existing >= 0)
        {
            _models.RemoveAt(existing);
        }

        _models.Add(model);
        return model;
    }
}
