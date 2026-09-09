namespace Playground.AccessControl.Infrastructure.Persistence.Entities;

/// <summary>
/// Un modelo de autorización publicado. Inmutable: publicar crea una fila, nunca actualiza.
/// </summary>
/// <remarks>
/// El contraste con <see cref="RelationshipTupleEntity"/> es lo interesante de esta tabla:
/// aquí hay un puñado de filas que cambian una vez por release, mientras que la otra tiene
/// millones que cambian a cada rato. Esa asimetría es la que justifica cachear el modelo en
/// memoria de forma agresiva y no cachear las tuplas.
/// </remarks>
public sealed class AuthorizationModelEntity
{
    /// <summary>
    /// Hash del DSL normalizado. Republicar un texto idéntico da el mismo id, así que no se
    /// generan versiones duplicadas por volver a desplegar.
    /// </summary>
    public string Id { get; set; } = null!;

    /// <summary>Orden de publicación. Determina cuál es el modelo vigente.</summary>
    public long Sequence { get; set; }

    public string SchemaVersion { get; set; } = null!;

    /// <summary>
    /// El DSL tal cual se escribió, comentarios incluidos.
    /// </summary>
    /// <remarks>
    /// Se guarda el texto y no el árbol compilado a propósito: el texto es lo que una persona
    /// puede leer y comparar entre versiones, y el árbol se reconstruye en milisegundos al
    /// arrancar. Guardar el árbol serializado ataría el formato de la base de datos a la
    /// estructura interna del parser.
    /// </remarks>
    public string RawDsl { get; set; } = null!;

    public string? Name { get; set; }

    public string? Description { get; set; }

    public DateTime PublishedAt { get; set; }
}
