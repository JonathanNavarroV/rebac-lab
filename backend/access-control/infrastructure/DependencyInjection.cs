using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Playground.AccessControl.Application.Authorization.Engine;
using Playground.AccessControl.Application.Authorization.Storage;
using Playground.AccessControl.Domain.Abstractions;
using Playground.AccessControl.Infrastructure.Persistence;
using Playground.AccessControl.Infrastructure.Repositories;

namespace Playground.AccessControl.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registra el módulo de control de acceso completo: persistencia, almacenes y motor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El registro es la demostración práctica de que el motor no sabe nada de bases de datos:
    /// se le inyectan tres abstracciones y punto. Cambiar
    /// <c>PostgresRelationshipTupleStore</c> por <c>InMemoryRelationshipTupleStore</c> aquí es
    /// suficiente para arrancar el módulo entero sin Postgres, y todo lo demás sigue
    /// funcionando igual.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddAccessControlModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AccessControlDb");
        var useInMemory = configuration.GetValue("AccessControl:UseInMemoryStore", false);

        if (useInMemory || string.IsNullOrWhiteSpace(connectionString))
        {
            // Modo efímero: el módulo arranca sin infraestructura. Útil para experimentar sin
            // levantar nada y para tests de extremo a extremo.
            services.AddSingleton<InMemoryRelationshipTupleStore>();
            services.AddSingleton<IRelationshipTupleStore>(provider =>
                provider.GetRequiredService<InMemoryRelationshipTupleStore>());
            services.AddSingleton<IAuthorizationModelStore, InMemoryAuthorizationModelStore>();
            services.AddSingleton<IDecisionAuditStore, InMemoryDecisionAuditStore>();
        }
        else
        {
            services.AddDbContext<AccessControlDbContext>(options => options.UseNpgsql(connectionString));

            services.AddScoped<IRelationshipTupleStore, PostgresRelationshipTupleStore>();
            services.AddScoped<IAuthorizationModelStore, PostgresAuthorizationModelStore>();
            services.AddScoped<IDecisionAuditStore, PostgresDecisionAuditStore>();
        }

        services.AddScoped<IObjectCatalog, TupleDerivedObjectCatalog>();
        services.AddScoped<IAccessControlEngine, AccessControlEngine>();
        services.AddScoped<Seeding.AccessControlSeeder>();

        return services;
    }
}
