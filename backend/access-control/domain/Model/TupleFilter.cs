namespace Playground.AccessControl.Domain.Model;

/// <summary>
/// Criterio de búsqueda de tuplas. Cualquier campo en <c>null</c> significa "cualquiera".
/// </summary>
/// <remarks>
/// <para>
/// El motor solo hace <b>dos</b> tipos de consulta contra el almacén, y conviene tenerlas
/// presentes porque son las que determinan los índices de la tabla y, en último término,
/// el rendimiento de todo el sistema:
/// </para>
/// <list type="number">
///   <item>
///     <b>Hacia delante</b> (la que usa Check): fijados <c>ObjectType</c>, <c>ObjectId</c> y
///     <c>Relation</c>, ¿quiénes son los sujetos? Es decir: <i>"¿quién es editor de
///     project:alpha?"</i>. Índice <c>ix_tuples_forward</c>.
///   </item>
///   <item>
///     <b>Hacia atrás</b> (la que usa ListObjects por expansión inversa): fijado el sujeto,
///     ¿sobre qué objetos tiene relaciones? Es decir: <i>"¿de qué es editor
///     team:backend#member?"</i>. Índice <c>ix_tuples_reverse</c>.
///   </item>
/// </list>
/// <para>
/// Que existan justo estas dos direcciones no es casualidad: es la razón por la que Check y
/// ListObjects son problemas de dificultad muy distinta. Check baja por el árbol desde el
/// objeto; ListObjects tiene que subir desde el sujeto sin conocer los objetos de antemano.
/// </para>
/// </remarks>
public sealed record TupleFilter
{
    public string? ObjectType { get; init; }
    public string? ObjectId { get; init; }
    public string? Relation { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public string? SubjectRelation { get; init; }

    /// <summary>
    /// Cuando es <c>true</c>, <see cref="SubjectRelation"/> a <c>null</c> significa
    /// literalmente "sin relación" (un individuo) en lugar de "cualquiera". Hace falta para
    /// poder buscar exactamente <c>@user:juan</c> y no también <c>@user:juan#algo</c>.
    /// </summary>
    public bool MatchSubjectRelationExactly { get; init; }

    /// <summary>
    /// Consulta hacia delante: ¿quién tiene <paramref name="relation"/> sobre
    /// <paramref name="object"/>? Es la consulta que domina el coste de un Check.
    /// </summary>
    public static TupleFilter Forward(ObjectRef @object, string relation) => new()
    {
        ObjectType = @object.Type,
        ObjectId = @object.Id,
        Relation = relation,
    };

    /// <summary>
    /// Consulta hacia atrás: ¿sobre qué objetos tiene <paramref name="subject"/> alguna de
    /// las relaciones indicadas? Base de la expansión inversa de ListObjects.
    /// </summary>
    public static TupleFilter Reverse(SubjectRef subject, string? relation = null, string? objectType = null) => new()
    {
        SubjectType = subject.Type,
        SubjectId = subject.Id,
        SubjectRelation = subject.Relation,
        MatchSubjectRelationExactly = true,
        Relation = relation,
        ObjectType = objectType,
    };

    /// <summary>Todas las tuplas de un objeto, sin filtrar relación. Para Expand y el grafo.</summary>
    public static TupleFilter ForObject(ObjectRef @object) => new()
    {
        ObjectType = @object.Type,
        ObjectId = @object.Id,
    };

    public bool Matches(RelationshipTuple tuple) =>
        (ObjectType is null || ObjectType == tuple.Object.Type)
        && (ObjectId is null || ObjectId == tuple.Object.Id)
        && (Relation is null || Relation == tuple.Relation)
        && (SubjectType is null || SubjectType == tuple.Subject.Type)
        && (SubjectId is null || SubjectId == tuple.Subject.Id)
        && MatchesSubjectRelation(tuple);

    private bool MatchesSubjectRelation(RelationshipTuple tuple) =>
        MatchSubjectRelationExactly
            ? SubjectRelation == tuple.Subject.Relation
            : SubjectRelation is null || SubjectRelation == tuple.Subject.Relation;
}
