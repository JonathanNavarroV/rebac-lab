namespace Playground.AccessControl.Domain.Model;

/// <summary>
/// Referencia a un objeto protegido, en la notación <c>tipo:id</c> del paper de Zanzibar.
/// Ejemplos: <c>project:alpha</c>, <c>folder:docs</c>, <c>organization:acme</c>.
/// </summary>
/// <remarks>
/// <para>
/// Este tipo es una de las ideas centrales de ReBAC y merece la pena detenerse en ella.
/// En un sistema RBAC clásico el modelo de datos tiene una tabla por cada cosa protegible
/// (<c>project_permissions</c>, <c>document_permissions</c>, <c>folder_permissions</c>...),
/// porque cada una tiene su propia FK. Aquí no: <b>todo objeto del sistema se identifica
/// con una pareja de cadenas</b>, y eso permite que exista una sola tabla de relaciones
/// para el sistema completo.
/// </para>
/// <para>
/// La consecuencia práctica es que añadir un tipo nuevo de recurso protegido no requiere
/// ninguna migración de base de datos: basta con declararlo en el modelo de autorización.
/// </para>
/// </remarks>
public sealed record ObjectRef(string Type, string Id)
{
    /// <summary>Separador entre tipo e identificador en la notación textual.</summary>
    public const char Separator = ':';

    public override string ToString() => $"{Type}{Separator}{Id}";

    /// <summary>
    /// Convierte el objeto en un sujeto sin relación. Se usa cuando un objeto aparece como
    /// sujeto de otra tupla, por ejemplo <c>project:alpha#parent@organization:acme</c>: allí
    /// <c>organization:acme</c> es a la vez un objeto del sistema y el sujeto de esa tupla.
    /// </summary>
    public SubjectRef AsSubject() => new(Type, Id, Relation: null);

    /// <summary>
    /// Parsea <c>tipo:id</c>. Lanza si el formato no es válido; para entrada de usuario
    /// usar <see cref="TryParse"/>.
    /// </summary>
    public static ObjectRef Parse(string value) =>
        TryParse(value, out var result)
            ? result
            : throw new FormatException(
                $"'{value}' no es una referencia de objeto válida. Se esperaba el formato 'tipo:id', por ejemplo 'project:alpha'.");

    public static bool TryParse(string? value, out ObjectRef result)
    {
        result = null!;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        // El id puede contener ':' (por ejemplo un GUID no, pero una ruta sí), así que
        // partimos solo en el PRIMER separador. El tipo nunca lo contiene.
        var separatorIndex = value.IndexOf(Separator);
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
            return false;

        var type = value[..separatorIndex].Trim();
        var id = value[(separatorIndex + 1)..].Trim();

        if (type.Length == 0 || id.Length == 0)
            return false;

        // Un objeto no puede llevar '#': eso solo tiene sentido en un sujeto, donde indica
        // "el conjunto de quienes tienen esta relación". Ver SubjectRef.
        if (type.Contains(SubjectRef.RelationSeparator) || id.Contains(SubjectRef.RelationSeparator))
            return false;

        result = new ObjectRef(type, id);
        return true;
    }

    public static ObjectRef Of(string type, string id) => new(type, id);
}
