using Microsoft.Extensions.Logging;
using Playground.AccessControl.Application.Authorization.Model;
using Playground.AccessControl.Application.Authorization.Seed;
using Playground.AccessControl.Domain.Abstractions;
using Playground.AccessControl.Domain.Model;

namespace Playground.AccessControl.Infrastructure.Seeding;

/// <summary>
/// Carga inicial del módulo: publica los modelos y escribe las tuplas del escenario.
/// </summary>
/// <remarks>
/// Vive en infraestructura y no en la API porque el servicio de negocio, cuando corre en modo
/// <c>InProcess</c>, monta el módulo completo dentro de sí mismo y necesita poder sembrarlo
/// igual. Si estuviera en el proyecto de la API, arrancar en modo InProcess dejaría el sistema
/// sin modelo publicado y sin ninguna tupla.
/// </remarks>
public sealed class AccessControlSeeder(
    IAuthorizationModelStore models,
    IRelationshipTupleStore tuples,
    ILogger<AccessControlSeeder> logger)
{
    /// <summary>
    /// Publica los modelos y, si no hay ninguna tupla, escribe el escenario inicial.
    /// </summary>
    /// <remarks>
    /// Las tuplas solo se escriben si el almacén está vacío: si al reiniciar se sobrescribiera
    /// el estado, perderías todo lo que hubieras montado experimentando, que es justo el
    /// trabajo que interesa conservar. Para volver al punto de partida a propósito está
    /// <see cref="ResetAsync"/>.
    /// </remarks>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        // El orden importa: el ÚLTIMO publicado es el vigente. El completo va al final.
        await models.PublishAsync(
            PlaygroundModels.RbacEquivalent,
            "RBAC expresado en ReBAC",
            "Cinco líneas que demuestran que un modelo de roles es un caso particular de ReBAC.",
            cancellationToken);

        await models.PublishAsync(
            PlaygroundModels.WithoutFolderInheritance,
            "Sin herencia en carpetas",
            "El modelo completo menos la cláusula 'from parent' en folder. Publícalo para ver qué "
            + "hace exactamente un tuple_to_userset.",
            cancellationToken);

        await models.PublishAsync(
            PlaygroundModels.Full,
            "Modelo completo del laboratorio",
            "Organizaciones, equipos, grupos anidados, proyectos, carpetas recursivas y recursos.",
            cancellationToken);

        var existing = await tuples.CountAsync(new TupleFilter(), cancellationToken);

        if (existing > 0)
        {
            logger.LogInformation(
                "El almacén ya tiene {Count} tuplas; no se toca el escenario. "
                + "Usa POST /access-control/seed/reset para volver al estado inicial.",
                existing);

            return;
        }

        await WriteScenarioAsync(cancellationToken);
    }

    /// <summary>Borra todas las tuplas y vuelve a escribir el escenario inicial.</summary>
    public async Task<int> ResetAsync(CancellationToken cancellationToken = default)
    {
        var all = await tuples.ReadAsync(new TupleFilter(), cancellationToken);

        foreach (var tuple in all)
            await tuples.DeleteAsync(tuple.Id, cancellationToken);

        return await WriteScenarioAsync(cancellationToken);
    }

    private async Task<int> WriteScenarioAsync(CancellationToken cancellationToken)
    {
        var keys = PlaygroundScenario.ParsedTuples();

        await tuples.WriteManyAsync(keys, cancellationToken);

        logger.LogInformation(
            "Escenario inicial cargado: {Count} tuplas. Modelo vigente: el completo.",
            keys.Count);

        return keys.Count;
    }
}
