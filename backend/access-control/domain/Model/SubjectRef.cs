namespace Playground.AccessControl.Domain.Model;

/// <summary>
/// Referencia a un sujeto: quién recibe una relación. Puede ser un individuo, un
/// <i>userset</i> (un conjunto definido por otra relación) o un comodín.
/// </summary>
/// <remarks>
/// <para>
/// <b>Este tipo es la diferencia entre RBAC y ReBAC.</b> Merece leerse con calma, porque
/// todo lo demás del proyecto se deriva de aquí.
/// </para>
/// <para>Un sujeto puede ser tres cosas distintas:</para>
/// <list type="table">
///   <listheader><term>Forma</term><description>Significado</description></listheader>
///   <item>
///     <term><c>user:juan</c></term>
///     <description>
///       Un individuo concreto. <see cref="Relation"/> es <c>null</c>. Esto es lo único
///       que un sistema RBAC sabe expresar.
///     </description>
///   </item>
///   <item>
///     <term><c>team:backend#member</c></term>
///     <description>
///       Un <b>userset</b>: "el conjunto de quienes tienen la relación <c>member</c> sobre
///       <c>team:backend</c>". No nombra a nadie. Cuando escribes
///       <c>project:alpha#editor@team:backend#member</c> no estás dando acceso a personas,
///       estás dando acceso a <i>un conjunto que se evalúa en el momento de preguntar</i>.
///       Metes a alguien en el equipo y hereda el acceso sin que nadie toque una sola tupla
///       de permisos. Ahí está la respuesta a "cómo se evita asignar permisos a millones
///       de usuarios de uno en uno".
///     </description>
///   </item>
///   <item>
///     <term><c>user:*</c></term>
///     <description>
///       Comodín: cualquier sujeto de ese tipo. Sirve para modelar "público". Es la forma
///       de decir "todo usuario autenticado puede ver esto" sin escribir N tuplas.
///     </description>
///   </item>
/// </list>
/// <para>
/// El userset es también lo que hace que la evaluación sea <b>recursiva</b>: para saber si
/// Juan pertenece a <c>team:backend#member</c> hay que hacer otro Check
/// (<c>Check(user:juan, member, team:backend)</c>), que a su vez puede toparse con otro
/// userset. Un grupo dentro de un grupo dentro de un grupo se resuelve con exactamente el
/// mismo código, sin casos especiales.
/// </para>
/// </remarks>
public sealed record SubjectRef(string Type, string Id, string? Relation)
{
    /// <summary>Separador que marca un userset: <c>team:backend#member</c>.</summary>
    public const char RelationSeparator = '#';

    /// <summary>Identificador que representa "cualquiera de este tipo".</summary>
    public const string WildcardId = "*";

    /// <summary>
    /// <c>true</c> si el sujeto es un conjunto definido por una relación
    /// (<c>team:backend#member</c>) en lugar de un individuo (<c>user:juan</c>).
    /// </summary>
    public bool IsUserset => Relation is not null;

    /// <summary><c>true</c> si es un comodín (<c>user:*</c>).</summary>
    public bool IsWildcard => Id == WildcardId;

    public override string ToString() =>
        IsUserset
            ? $"{Type}{ObjectRef.Separator}{Id}{RelationSeparator}{Relation}"
            : $"{Type}{ObjectRef.Separator}{Id}";

    /// <summary>
    /// El objeto sobre el que hay que evaluar <see cref="Relation"/> para expandir este
    /// userset. Para <c>team:backend#member</c> devuelve <c>team:backend</c>, que es contra
    /// quien el motor lanzará el Check recursivo.
    /// </summary>
    public ObjectRef AsObject() => new(Type, Id);

    public static SubjectRef User(string id) => new("user", id, null);

    public static SubjectRef Userset(string type, string id, string relation) =>
        new(type, id, relation);

    public static SubjectRef Wildcard(string type) => new(type, WildcardId, null);

    public static SubjectRef Parse(string value) =>
        TryParse(value, out var result)
            ? result
            : throw new FormatException(
                $"'{value}' no es una referencia de sujeto válida. Formatos admitidos: " +
                "'user:juan' (individuo), 'team:backend#member' (userset) o 'user:*' (comodín).");

    public static bool TryParse(string? value, out SubjectRef result)
    {
        result = null!;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();

        // Primero separamos la relación del userset, si la hay.
        string? relation = null;
        var hashIndex = trimmed.IndexOf(RelationSeparator);
        if (hashIndex >= 0)
        {
            relation = trimmed[(hashIndex + 1)..].Trim();
            trimmed = trimmed[..hashIndex];
            if (relation.Length == 0)
                return false;
        }

        if (!ObjectRef.TryParse(trimmed, out var objectPart))
            return false;

        // Un comodín con relación (user:*#member) no significa nada: el comodín ya es
        // "todos", no hay conjunto que expandir.
        if (objectPart.Id == WildcardId && relation is not null)
            return false;

        result = new SubjectRef(objectPart.Type, objectPart.Id, relation);
        return true;
    }
}
