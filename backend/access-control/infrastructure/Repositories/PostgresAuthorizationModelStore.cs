using Microsoft.EntityFrameworkCore;
using Playground.AccessControl.Application.Authorization.Model;
using Playground.AccessControl.Domain.Abstractions;
using Playground.AccessControl.Domain.Model;
using Playground.AccessControl.Infrastructure.Persistence;
using Playground.AccessControl.Infrastructure.Persistence.Entities;

namespace Playground.AccessControl.Infrastructure.Repositories;

/// <summary>
/// Almacén de modelos sobre PostgreSQL, con caché en memoria de los modelos ya compilados.
/// </summary>
/// <remarks>
/// <para>
/// La caché es un <c>static</c> compartido por proceso y <b>no caduca nunca</b>, lo cual sería
/// un error grave en casi cualquier otra entidad. Aquí es correcto por una razón concreta: los
/// modelos son <b>inmutables</b>. El id es un hash del DSL, así que dos modelos con el mismo
/// id tienen literalmente el mismo texto y compilan al mismo árbol. Una entrada de esta caché
/// no puede quedar obsoleta porque el dato que cachea no puede cambiar.
/// </para>
/// <para>
/// Es el ejemplo más limpio de la asimetría del sistema: el modelo se cachea sin pensarlo y
/// las tuplas no se cachean en absoluto. Ver <c>docs/06</c>.
/// </para>
/// </remarks>
public sealed class PostgresAuthorizationModelStore(AccessControlDbContext context) : IAuthorizationModelStore
{
    private static readonly Dictionary<string, AuthorizationModel> CompiledCache = new(StringComparer.Ordinal);
    private static readonly Lock CacheGate = new();

    private readonly AuthorizationModelDslParser _parser = new();

    public async Task<AuthorizationModel> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        var entity = await context.AuthorizationModels
                         .AsNoTracking()
                         .OrderByDescending(model => model.Sequence)
                         .FirstOrDefaultAsync(cancellationToken)
                     ?? throw new InvalidOperationException(
                         "No hay ningún modelo de autorización publicado. Sin modelo no se puede evaluar nada: "
                         + "el modelo es el que dice qué relaciones existen y cómo se derivan. "
                         + "Arranca la API con el seed activado o publica uno en POST /access-control/models.");

        return Compile(entity);
    }

    public async Task<AuthorizationModel?> GetByIdAsync(string modelId, CancellationToken cancellationToken = default)
    {
        var entity = await context.AuthorizationModels
            .AsNoTracking()
            .FirstOrDefaultAsync(model => model.Id == modelId, cancellationToken);

        return entity is null ? null : Compile(entity);
    }

    public async Task<IReadOnlyList<AuthorizationModel>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entities = await context.AuthorizationModels
            .AsNoTracking()
            .OrderByDescending(model => model.Sequence)
            .ToListAsync(cancellationToken);

        return entities.Select(Compile).ToList();
    }

    public async Task<AuthorizationModel> PublishAsync(
        string dsl,
        string? name = null,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        // Se compila ANTES de tocar la base: publicar un modelo inválido dejaría el sistema
        // sin poder evaluar nada, así que la validación es la primera barrera.
        var model = _parser.Parse(dsl, modelId: null, name: name, description: description);

        var existing = await context.AuthorizationModels
            .FirstOrDefaultAsync(entity => entity.Id == model.Id, cancellationToken);

        if (existing is not null)
        {
            // Republicar un texto idéntico no crea una versión nueva. Evita que cada
            // despliegue genere una fila más y que la auditoría se llene de ids distintos
            // para el mismo modelo.
            return Compile(existing);
        }

        var entity = new AuthorizationModelEntity
        {
            Id = model.Id,
            SchemaVersion = model.SchemaVersion,
            RawDsl = model.RawDsl,
            Name = name,
            Description = description,
            PublishedAt = DateTime.UtcNow,
        };

        context.AuthorizationModels.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        return Compile(entity);
    }

    private AuthorizationModel Compile(AuthorizationModelEntity entity)
    {
        lock (CacheGate)
        {
            if (CompiledCache.TryGetValue(entity.Id, out var cached))
                return cached;
        }

        var model = _parser.Parse(entity.RawDsl, entity.Id, entity.Name, entity.Description);

        lock (CacheGate)
        {
            CompiledCache[entity.Id] = model;
        }

        return model;
    }
}
