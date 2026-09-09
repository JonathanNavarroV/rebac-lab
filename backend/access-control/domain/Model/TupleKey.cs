namespace Playground.AccessControl.Domain.Model;

/// <summary>
/// Una tupla de relación, sin metadatos de persistencia. Es <b>el único dato que el sistema
/// de control de acceso almacena</b>.
/// </summary>
/// <remarks>
/// <para>
/// El paper de Zanzibar la escribe <c>⟨object#relation@user⟩</c>. Aquí usamos la misma
/// notación textual que OpenFGA, que es la del paper sin los símbolos raros:
/// </para>
/// <code>
/// project:alpha#editor@team:backend#member
/// │             │      │
/// │             │      └── sujeto: el userset "miembros de backend"
/// │             └───────── relación
/// └─────────────────────── objeto
/// </code>
/// <para>
/// Y se lee: <i>"los miembros del equipo backend son editores del proyecto alpha"</i>.
/// </para>
/// <para>
/// <b>Lo que NO es una tupla:</b> nunca almacenamos una tupla para una relación derivada.
/// No existe <c>resource:a#can_edit@user:juan</c> en la base de datos, aunque Juan pueda
/// editar ese recurso. <c>can_edit</c> se calcula recorriendo el modelo. Las tuplas son
/// hechos declarados por alguien; las relaciones derivadas son conclusiones que el motor
/// obtiene de esos hechos. Confundir ambas cosas es el error más común al empezar con
/// ReBAC, y es justo lo que haría un sistema RBAC (materializar el permiso final).
/// </para>
/// </remarks>
public sealed record TupleKey(ObjectRef Object, string Relation, SubjectRef Subject)
{
    public override string ToString() =>
        $"{Object}{SubjectRef.RelationSeparator}{Relation}@{Subject}";

    public static TupleKey Of(string @object, string relation, string subject) =>
        new(ObjectRef.Parse(@object), relation, SubjectRef.Parse(subject));

    /// <summary>
    /// Parsea la notación <c>objeto#relación@sujeto</c>, tal y como se escribe en el seed,
    /// en la documentación y en el editor de relaciones del frontend.
    /// </summary>
    public static TupleKey Parse(string value) =>
        TryParse(value, out var result)
            ? result
            : throw new FormatException(
                $"'{value}' no es una tupla válida. Se esperaba 'objeto#relación@sujeto', " +
                "por ejemplo 'project:alpha#editor@team:backend#member'.");

    public static bool TryParse(string? value, out TupleKey result)
    {
        result = null!;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var atIndex = value.IndexOf('@');
        if (atIndex <= 0 || atIndex == value.Length - 1)
            return false;

        var objectAndRelation = value[..atIndex];
        var subjectPart = value[(atIndex + 1)..];

        // El '#' que buscamos es el que separa objeto de relación, y está antes del '@',
        // así que no puede confundirse con el '#' de un userset en el sujeto.
        var hashIndex = objectAndRelation.IndexOf(SubjectRef.RelationSeparator);
        if (hashIndex <= 0 || hashIndex == objectAndRelation.Length - 1)
            return false;

        if (!ObjectRef.TryParse(objectAndRelation[..hashIndex], out var @object))
            return false;

        var relation = objectAndRelation[(hashIndex + 1)..].Trim();
        if (relation.Length == 0)
            return false;

        if (!SubjectRef.TryParse(subjectPart, out var subject))
            return false;

        result = new TupleKey(@object, relation, subject);
        return true;
    }
}

/// <summary>
/// Una <see cref="TupleKey"/> tal y como está guardada: con identificador y fecha de alta.
/// </summary>
/// <remarks>
/// <see cref="CreatedAt"/> no es decoración. En Zanzibar cada escritura de tupla recibe una
/// marca temporal (un <i>zookie</i>) que permite pedir un Check "suficientemente reciente".
/// Aquí no implementamos zookies, pero conservar el instante permite ver en la auditoría el
/// orden real de los hechos y razonar sobre consistencia. Ver <c>docs/06</c>.
/// </remarks>
public sealed record RelationshipTuple(
    long Id,
    ObjectRef Object,
    string Relation,
    SubjectRef Subject,
    DateTime CreatedAt)
{
    public TupleKey Key => new(Object, Relation, Subject);

    public override string ToString() => Key.ToString();
}
