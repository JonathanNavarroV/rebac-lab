using Playground.Business.Domain.Entities;

namespace Playground.Business.Domain.Repositories;

/// <summary>
/// Repositorio genérico del negocio.
/// </summary>
/// <remarks>
/// <para>
/// Fíjate en que <b>ninguna operación recibe el usuario actual</b>. No hay
/// <c>GetAllVisibleTo(user)</c> ni <c>GetByIdIfAllowed(user, id)</c>. El repositorio devuelve
/// datos; quien decide si se pueden ver es el handler, preguntando al puerto.
/// </para>
/// <para>
/// Mezclar ambas cosas es una tentación fuerte y muy habitual (un <c>WHERE owner_id = @user</c>
/// en cada consulta), y es exactamente lo que impide que el modelo de autorización evolucione:
/// el día que el acceso puede venir de un equipo, de una carpeta padre o de una compartición,
/// ese <c>WHERE</c> ya no se puede escribir.
/// </para>
/// </remarks>
public interface IBusinessRepository<TEntity> where TEntity : BusinessEntity
{
    Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<TEntity?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TEntity>> GetByIdsAsync(
        IReadOnlyCollection<string> ids,
        CancellationToken cancellationToken = default);

    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

    Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);
}
