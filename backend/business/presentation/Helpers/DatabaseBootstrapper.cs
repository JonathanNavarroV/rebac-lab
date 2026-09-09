using Microsoft.EntityFrameworkCore;

namespace Playground.Business.Api.Helpers;

/// <summary>
/// Prepara la base de datos al arrancar. Ver el comentario de la versión equivalente en el
/// módulo de control de acceso: aplica migraciones si existen y, si no, crea el esquema
/// directamente para que el laboratorio arranque sin pasos intermedios.
/// </summary>
public static class DatabaseBootstrapper
{
    public static async Task EnsureReadyAsync(DbContext context, ILogger logger)
    {
        var migrations = context.Database.GetMigrations().ToList();

        if (migrations.Count > 0)
        {
            await context.Database.MigrateAsync();
            return;
        }

        logger.LogWarning(
            "No hay migraciones en el ensamblado; se crea el esquema con EnsureCreated (modo laboratorio).");

        await context.Database.EnsureCreatedAsync();
    }
}
