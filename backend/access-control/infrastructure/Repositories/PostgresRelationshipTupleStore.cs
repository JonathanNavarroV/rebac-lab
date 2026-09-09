using Microsoft.EntityFrameworkCore;
using Playground.AccessControl.Domain.Abstractions;
using Playground.AccessControl.Domain.Model;
using Playground.AccessControl.Infrastructure.Persistence;
using Playground.AccessControl.Infrastructure.Persistence.Entities;

namespace Playground.AccessControl.Infrastructure.Repositories;

/// <summary>
/// Almacén de tuplas sobre PostgreSQL.
/// </summary>
/// <remarks>
/// Es la implementación "de verdad" del mismo contrato que cumple
/// <c>InMemoryRelationshipTupleStore</c>. Que las dos existan y sean intercambiables es lo
/// que permite que la suite de conformidad ejercite el motor real sin base de datos: el
/// motor nunca sabe cuál de las dos tiene delante.
/// </remarks>
public sealed class PostgresRelationshipTupleStore(AccessControlDbContext context) : IRelationshipTupleStore
{
    public async Task<IReadOnlyList<RelationshipTuple>> ReadAsync(
        TupleFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = context.RelationshipTuples.AsNoTracking();

        // Se filtra DENTRO de la consulta y antes de proyectar. Proyectar a dominio y filtrar
        // después traería la tabla entera a memoria: con esta tabla en concreto, eso no es una
        // ineficiencia, es una caída del servicio.
        if (filter.ObjectType is not null)
            query = query.Where(tuple => tuple.ObjectType == filter.ObjectType);

        if (filter.ObjectId is not null)
            query = query.Where(tuple => tuple.ObjectId == filter.ObjectId);

        if (filter.Relation is not null)
            query = query.Where(tuple => tuple.Relation == filter.Relation);

        if (filter.SubjectType is not null)
            query = query.Where(tuple => tuple.SubjectType == filter.SubjectType);

        if (filter.SubjectId is not null)
            query = query.Where(tuple => tuple.SubjectId == filter.SubjectId);

        if (filter.MatchSubjectRelationExactly)
            query = query.Where(tuple => tuple.SubjectRelation == filter.SubjectRelation);
        else if (filter.SubjectRelation is not null)
            query = query.Where(tuple => tuple.SubjectRelation == filter.SubjectRelation);

        var entities = await query.OrderBy(tuple => tuple.Id).ToListAsync(cancellationToken);

        return entities.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<RelationshipTuple>> ReadReverseAsync(
        IReadOnlyCollection<SubjectRef> subjects,
        IReadOnlyCollection<string> relations,
        string? objectType = null,
        CancellationToken cancellationToken = default)
    {
        if (subjects.Count == 0)
            return [];

        // Los sujetos llegan como tripletas (tipo, id, relación) y hay que buscarlos todos a
        // la vez. Traducirlo a un OR de tripletas generaría un SQL enorme y difícil de
        // planificar, así que se filtra por los tres componentes por separado — lo que el
        // índice inverso puede resolver — y se afina en memoria.
        //
        // El sobrecoste es pequeño porque los tres conjuntos son diminutos (los alias de UN
        // sujeto), y a cambio la consulta es una sola y usa el índice.
        var subjectTypes = subjects.Select(subject => subject.Type).Distinct().ToList();
        var subjectIds = subjects.Select(subject => subject.Id).Distinct().ToList();
        var subjectRelations = subjects.Select(subject => subject.Relation).Distinct().ToList();

        var query = context.RelationshipTuples.AsNoTracking()
            .Where(tuple => subjectTypes.Contains(tuple.SubjectType))
            .Where(tuple => subjectIds.Contains(tuple.SubjectId))
            .Where(tuple => subjectRelations.Contains(tuple.SubjectRelation));

        if (relations.Count > 0)
        {
            var relationList = relations.ToList();
            query = query.Where(tuple => relationList.Contains(tuple.Relation));
        }

        if (objectType is not null)
            query = query.Where(tuple => tuple.ObjectType == objectType);

        var entities = await query.OrderBy(tuple => tuple.Id).ToListAsync(cancellationToken);

        // Afinado en memoria: descarta las combinaciones cruzadas que el filtro por
        // componentes deja pasar (por ejemplo 'team:acme#member' cuando los sujetos reales
        // eran 'team:backend#member' y 'organization:acme#member').
        var exact = subjects.Select(subject => subject.ToString()).ToHashSet(StringComparer.Ordinal);

        return entities
            .Select(Map)
            .Where(tuple => exact.Contains(tuple.Subject.ToString()))
            .ToList();
    }

    public async Task<RelationshipTuple> WriteAsync(TupleKey key, CancellationToken cancellationToken = default)
    {
        var existing = await FindAsync(key, cancellationToken);

        if (existing is not null)
            return Map(existing);

        var entity = ToEntity(key);

        context.RelationshipTuples.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        return Map(entity);
    }

    public async Task<IReadOnlyList<RelationshipTuple>> WriteManyAsync(
        IReadOnlyCollection<TupleKey> keys,
        CancellationToken cancellationToken = default)
    {
        var entities = keys.Select(ToEntity).ToList();

        context.RelationshipTuples.AddRange(entities);
        await context.SaveChangesAsync(cancellationToken);

        return entities.Select(Map).ToList();
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default) =>
        await context.RelationshipTuples
            .Where(tuple => tuple.Id == id)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<bool> DeleteAsync(TupleKey key, CancellationToken cancellationToken = default)
    {
        var existing = await FindAsync(key, cancellationToken);

        if (existing is null)
            return false;

        return await DeleteAsync(existing.Id, cancellationToken);
    }

    public async Task<int> CountAsync(TupleFilter filter, CancellationToken cancellationToken = default)
    {
        var tuples = await ReadAsync(filter, cancellationToken);
        return tuples.Count;
    }

    private Task<RelationshipTupleEntity?> FindAsync(TupleKey key, CancellationToken cancellationToken) =>
        context.RelationshipTuples.FirstOrDefaultAsync(
            tuple => tuple.ObjectType == key.Object.Type
                     && tuple.ObjectId == key.Object.Id
                     && tuple.Relation == key.Relation
                     && tuple.SubjectType == key.Subject.Type
                     && tuple.SubjectId == key.Subject.Id
                     && tuple.SubjectRelation == key.Subject.Relation,
            cancellationToken);

    private static RelationshipTuple Map(RelationshipTupleEntity entity) =>
        new(entity.Id,
            new ObjectRef(entity.ObjectType, entity.ObjectId),
            entity.Relation,
            new SubjectRef(entity.SubjectType, entity.SubjectId, entity.SubjectRelation),
            entity.CreatedAt);

    private static RelationshipTupleEntity ToEntity(TupleKey key) => new()
    {
        ObjectType = key.Object.Type,
        ObjectId = key.Object.Id,
        Relation = key.Relation,
        SubjectType = key.Subject.Type,
        SubjectId = key.Subject.Id,
        SubjectRelation = key.Subject.Relation,
        CreatedAt = DateTime.UtcNow,
    };
}
