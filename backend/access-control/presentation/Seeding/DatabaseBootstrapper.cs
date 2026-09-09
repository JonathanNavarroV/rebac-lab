using Microsoft.EntityFrameworkCore;

namespace Playground.AccessControl.Api.Seeding;

/// <summary>
/// Prepara la base de datos al arrancar.
/// </summary>
/// <remarks>
/// <para>
/// Aplica las migraciones si existen y, si todavía no se han generado, crea el esquema
/// directamente con <c>EnsureCreated</c>.
/// </para>
/// <para>
/// Ese segundo camino es una concesión <b>exclusiva del laboratorio</b> y merece explicarse,
/// porque en un proyecto normal sería mala práctica: significa que el esquema se crea sin
/// dejar rastro en <c>__EFMigrationsHistory</c>, así que la primera migración que se genere
/// después no se podrá aplicar sobre esa base (habrá que tirarla y volver a crearla).
/// </para>
/// <para>
/// Está aquí para que <c>docker compose up</c> seguido de <c>dotnet run</c> deje el sistema
/// funcionando sin pasos intermedios. En cuanto se generen las migraciones de verdad, este
/// camino deja de usarse solo.
/// </para>
/// </remarks>
public static class DatabaseBootstrapper
{
    public static async Task EnsureReadyAsync(DbContext context, ILogger logger)
    {
        var pending = context.Database.GetMigrations().ToList();

        if (pending.Count > 0)
        {
            await context.Database.MigrateAsync();
            return;
        }

        logger.LogWarning(
            "No hay migraciones en el ensamblado; se crea el esquema con EnsureCreated (modo laboratorio). "
            + "Genera las migraciones con 'dotnet ef migrations add InitialCreate' y vuelve a crear la base "
            + "para tener un historial de verdad.");

        await context.Database.EnsureCreatedAsync();
    }
}
