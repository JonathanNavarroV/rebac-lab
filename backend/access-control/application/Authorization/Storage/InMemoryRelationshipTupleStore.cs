using Playground.AccessControl.Domain.Abstractions;
using Playground.AccessControl.Domain.Model;

namespace Playground.AccessControl.Application.Authorization.Storage;

/// <summary>
/// Almacén de tuplas en memoria.
/// </summary>
/// <remarks>
/// <para>
/// No es solo un doble de test: es la pieza que hace que la suite de conformidad pueda
/// ejercitar el <b>motor real</b> sin Docker, sin Postgres y sin migraciones. Los casos A–H
/// del laboratorio son tests de milisegundos porque el motor nunca supo si las tuplas venían
/// de aquí o de una base de datos.
/// </para>
/// <para>
/// Vive en la capa de aplicación y no en un proyecto de test porque también sirve para
/// arrancar el módulo en modo efímero y experimentar sin persistir nada.
/// </para>
/// </remarks>
public sealed class InMemoryRelationshipTupleStore : IRelationshipTupleStore
{
    private readonly Lock _gate = new();
    private readonly List<RelationshipTuple> _tuples = [];
    private long _nextId = 1;

    public InMemoryRelationshipTupleStore()
    {
    }

    public InMemoryRelationshipTupleStore(IEnumerable<TupleKey> seed)
    {
        seed.ToList().ForEach(key => WriteCore(key));
    }

    /// <summary>Conveniencia para los tests: acepta la notación textual de las tuplas.</summary>
    public InMemoryRelationshipTupleStore(params string[] seed)
        : this(seed.Select(TupleKey.Parse))
    {
    }

    public Task<IReadOnlyList<RelationshipTuple>> ReadAsync(
        TupleFilter filter,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<RelationshipTuple> result = _tuples.Where(filter.Matches).ToList();
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<RelationshipTuple>> ReadReverseAsync(
        IReadOnlyCollection<SubjectRef> subjects,
        IReadOnlyCollection<string> relations,
        string? objectType = null,
        CancellationToken cancellationToken = default)
    {
        // Se replica exactamente la semántica que tendrá la consulta SQL equivalente
        // (WHERE subject IN (...) AND relation IN (...) AND object_type = ...) para que el
        // motor no pueda comportarse distinto según el almacén.
        var subjectKeys = subjects.Select(subject => subject.ToString()).ToHashSet(StringComparer.Ordinal);
        var relationSet = relations.ToHashSet(StringComparer.Ordinal);

        lock (_gate)
        {
            IReadOnlyList<RelationshipTuple> result = _tuples
                .Where(tuple => subjectKeys.Contains(tuple.Subject.ToString()))
                .Where(tuple => relationSet.Count == 0 || relationSet.Contains(tuple.Relation))
                .Where(tuple => objectType is null || tuple.Object.Type == objectType)
                .ToList();

            return Task.FromResult(result);
        }
    }

    public Task<RelationshipTuple> WriteAsync(TupleKey key, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(WriteCore(key));
        }
    }

    public Task<IReadOnlyList<RelationshipTuple>> WriteManyAsync(
        IReadOnlyCollection<TupleKey> keys,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<RelationshipTuple> result = keys.Select(WriteCore).ToList();
            return Task.FromResult(result);
        }
    }

    public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_tuples.RemoveAll(tuple => tuple.Id == id) > 0);
        }
    }

    public Task<bool> DeleteAsync(TupleKey key, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_tuples.RemoveAll(tuple => tuple.Key == key) > 0);
        }
    }

    public Task<int> CountAsync(TupleFilter filter, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_tuples.Count(filter.Matches));
        }
    }

    /// <summary>Todas las tuplas. Para el grafo y para inspeccionar en los tests.</summary>
    public IReadOnlyList<RelationshipTuple> Snapshot()
    {
        lock (_gate)
        {
            return _tuples.ToList();
        }
    }

    private RelationshipTuple WriteCore(TupleKey key)
    {
        // Idempotencia: invitar dos veces a la misma persona no debe generar dos hechos
        // idénticos, porque luego la traza mostraría el mismo camino repetido.
        var existing = _tuples.FirstOrDefault(tuple => tuple.Key == key);

        if (existing is not null)
            return existing;

        var created = new RelationshipTuple(
            _nextId++,
            key.Object,
            key.Relation,
            key.Subject,
            DateTime.UtcNow);

        _tuples.Add(created);
        return created;
    }
}
