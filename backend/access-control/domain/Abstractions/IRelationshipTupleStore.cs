using Playground.AccessControl.Domain.Model;

namespace Playground.AccessControl.Domain.Abstractions;

/// <summary>
/// Almacén de tuplas de relación. El único estado persistente que el control de acceso
/// necesita.
/// </summary>
/// <remarks>
/// <para>
/// La interfaz es deliberadamente diminuta, y eso dice algo importante sobre el modelo:
/// <b>todo el sistema de autorización se sostiene sobre leer y escribir tuplas</b>. No hay
/// operaciones de "conceder permiso", "asignar rol" ni "revocar acceso". Solo hechos que se
/// añaden o se quitan.
/// </para>
/// <para>
/// Que el motor dependa de esta abstracción y no de EF Core tiene una consecuencia muy
/// concreta: la suite de conformidad ejecuta el motor real contra un store en memoria, sin
/// Docker ni base de datos. Los casos A–H del laboratorio son tests unitarios de
/// milisegundos.
/// </para>
/// </remarks>
public interface IRelationshipTupleStore
{
    /// <summary>
    /// Lee las tuplas que coinciden con el filtro. Es la única operación de lectura, y por
    /// tanto la que determina el coste de cualquier Check.
    /// </summary>
    Task<IReadOnlyList<RelationshipTuple>> ReadAsync(TupleFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lee tuplas cuyo sujeto sea alguno de los indicados y cuya relación esté entre las
    /// indicadas. Es la consulta que hace viable la expansión inversa.
    /// </summary>
    /// <remarks>
    /// Se expone como método propio en lugar de resolverse con N llamadas a
    /// <see cref="ReadAsync"/> porque la diferencia es justo la que se quiere medir: una
    /// consulta con <c>IN (...)</c> frente a N consultas sueltas. Con el módulo en modo
    /// remoto, esas N consultas serían además N saltos de red.
    /// </remarks>
    Task<IReadOnlyList<RelationshipTuple>> ReadReverseAsync(
        IReadOnlyCollection<SubjectRef> subjects,
        IReadOnlyCollection<string> relations,
        string? objectType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Escribe una tupla. Idempotente: si ya existe, devuelve la existente sin duplicar.
    /// </summary>
    /// <remarks>
    /// La idempotencia importa porque "invitar a alguien que ya estaba invitado" es una
    /// operación que pasa constantemente y no debe generar dos hechos idénticos.
    /// </remarks>
    Task<RelationshipTuple> WriteAsync(TupleKey key, CancellationToken cancellationToken = default);

    /// <summary>Escribe varias tuplas en una sola operación. Se usa en el seed.</summary>
    Task<IReadOnlyList<RelationshipTuple>> WriteManyAsync(
        IReadOnlyCollection<TupleKey> keys,
        CancellationToken cancellationToken = default);

    /// <summary>Borra por identificador. Devuelve <c>false</c> si no existía.</summary>
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Borra por clave. Devuelve <c>false</c> si no existía.</summary>
    Task<bool> DeleteAsync(TupleKey key, CancellationToken cancellationToken = default);

    /// <summary>Cuenta las tuplas que coinciden con el filtro, sin materializarlas.</summary>
    Task<int> CountAsync(TupleFilter filter, CancellationToken cancellationToken = default);
}
