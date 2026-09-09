using Playground.AccessControl.Domain.Abstractions;
using Playground.AccessControl.Domain.Model;

namespace Playground.AccessControl.Application.Authorization.Storage;

/// <summary>
/// Catálogo de objetos derivado de las propias tuplas: un objeto "existe" si aparece en
/// alguna relación.
/// </summary>
/// <remarks>
/// <para>
/// Es la única forma que tiene el módulo de control de acceso de enumerar objetos sin
/// preguntarle al negocio, y por tanto lo que permite que la estrategia naive funcione de
/// forma autónoma. Pero tiene una limitación que conviene tener muy presente y que es en sí
/// misma la lección: <b>es incompleto</b>.
/// </para>
/// <para>
/// Un proyecto recién creado, sin ninguna relación todavía, no aparece aquí. Para responder
/// "qué puede ver Juan" da exactamente igual (no puede verlo). Para responder "cuántos
/// proyectos hay en total" o "qué proyectos NO puede ver Juan", no sirve — y no debería
/// servir, porque esas dos preguntas son del negocio, no del control de acceso.
/// </para>
/// <para>
/// De ahí la regla práctica al integrar un sistema tipo Zanzibar: <b>el catálogo lo enumera
/// el negocio, la decisión la toma el control de acceso</b>. El negocio pide la lista de
/// autorizados y la intersecta con lo que él conoce.
/// </para>
/// </remarks>
public sealed class TupleDerivedObjectCatalog(IRelationshipTupleStore tuples) : IObjectCatalog
{
    public async Task<IReadOnlyList<string>> GetObjectIdsAsync(
        string objectType,
        CancellationToken cancellationToken = default)
    {
        var asObject = await tuples.ReadAsync(
            new TupleFilter { ObjectType = objectType }, cancellationToken);

        // Un objeto también puede aparecer solo como SUJETO de una tupla: una carpeta que es
        // padre de un recurso aparece como 'folder:x' en el sujeto de
        // 'resource:a#parent@folder:x'. Si no se mirara aquí, esa carpeta sería invisible
        // para el catálogo aunque el sistema la conozca perfectamente.
        var asSubject = await tuples.ReadAsync(
            new TupleFilter { SubjectType = objectType }, cancellationToken);

        return asObject
            .Select(tuple => tuple.Object.Id)
            .Concat(asSubject
                .Where(tuple => !tuple.Subject.IsWildcard)
                .Select(tuple => tuple.Subject.Id))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}
