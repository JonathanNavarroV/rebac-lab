namespace Playground.Business.Domain.Authorization;

/// <summary>Quién está haciendo la petición.</summary>
public interface ICurrentUser
{
    /// <summary>Identificador: <c>juan</c>.</summary>
    string Id { get; }

    /// <summary>Referencia como sujeto: <c>user:juan</c>. Es lo que se pasa al puerto.</summary>
    string SubjectRef => $"user:{Id}";

    bool IsAuthenticated { get; }
}

/// <summary>
/// Acceso denegado por el módulo de control de acceso.
/// </summary>
/// <remarks>
/// <para>
/// Lleva el motivo que devolvió el módulo. En un sistema real ese texto <b>no</b> se
/// devolvería al usuario final: explicar por qué no tiene acceso revela la estructura interna
/// de la organización ("no eres administrador de Acme" ya dice que Acme existe y que tiene
/// administradores). En el laboratorio sí se muestra, porque es donde está el aprendizaje.
/// </para>
/// </remarks>
public sealed class ForbiddenException(string subject, string relation, string @object, string reason)
    : Exception($"«{subject}» no puede «{relation}» sobre «{@object}».")
{
    public string Subject { get; } = subject;

    public string Relation { get; } = relation;

    public string Object { get; } = @object;

    public string Reason { get; } = reason;
}

/// <summary>La entidad de negocio no existe.</summary>
public sealed class NotFoundException(string entity, string id)
    : Exception($"No existe {entity} con id '{id}'.")
{
    public string Entity { get; } = entity;

    public string EntityId { get; } = id;
}
