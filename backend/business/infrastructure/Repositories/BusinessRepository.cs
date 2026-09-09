using Microsoft.EntityFrameworkCore;
using Playground.Business.Domain.Entities;
using Playground.Business.Domain.Repositories;
using Playground.Business.Infrastructure.Persistence;

namespace Playground.Business.Infrastructure.Repositories;

/// <summary>Repositorio genérico sobre EF Core.</summary>
public sealed class BusinessRepository<TEntity>(BusinessDbContext context) : IBusinessRepository<TEntity>
    where TEntity : BusinessEntity
{
    public async Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await context.Set<TEntity>().AsNoTracking().OrderBy(entity => entity.Name).ToListAsync(cancellationToken);

    public Task<TEntity?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
        context.Set<TEntity>().FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TEntity>> GetByIdsAsync(
        IReadOnlyCollection<string> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
            return [];

        var list = ids.ToList();

        return await context.Set<TEntity>()
            .AsNoTracking()
            .Where(entity => list.Contains(entity.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        context.Set<TEntity>().Add(entity);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        context.Set<TEntity>().Update(entity);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        context.Set<TEntity>().Remove(entity);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        context.Set<TEntity>().CountAsync(cancellationToken);
}
