using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Playground.AccessControl.Infrastructure;
using Playground.Business.Domain.Authorization;
using Playground.Business.Domain.Repositories;
using Playground.Business.Infrastructure.Authorization;
using Playground.Business.Infrastructure.Persistence;
using Playground.Business.Infrastructure.Rbac;
using Playground.Business.Infrastructure.Repositories;
using Playground.Business.Domain.Rbac;

namespace Playground.Business.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registra el negocio y <b>decide cómo hablará con el control de acceso</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La condición del medio es el corazón del laboratorio. Según una sola línea de
    /// configuración, el negocio acaba con el motor dentro del proceso o hablando por HTTP con
    /// un servicio aparte. Lo demás del sistema —handlers, endpoints, frontend— no cambia
    /// absolutamente nada, porque todos ven el mismo <see cref="IAccessControlService"/>.
    /// </para>
    /// <para>
    /// Que ese cambio sea una línea es la prueba de que la abstracción es la correcta. Y que la
    /// latencia se multiplique al cambiarla es la prueba de que la abstracción no es gratis.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddBusinessInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BusinessDb")
                               ?? throw new InvalidOperationException(
                                   "Falta la cadena de conexión 'BusinessDb'.");

        services.AddDbContext<BusinessDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped(typeof(IBusinessRepository<>), typeof(BusinessRepository<>));
        services.AddScoped<IRbacEngine, RbacEngine>();
        services.AddScoped<BusinessSeeder>();
        services.AddScoped<RbacSeeder>();

        var mode = configuration.GetValue<string>("Authorization:Mode") ?? "InProcess";

        if (string.Equals(mode, "Remote", StringComparison.OrdinalIgnoreCase))
        {
            var baseUrl = configuration.GetValue<string>("Authorization:RemoteBaseUrl")
                          ?? "http://localhost:15101";

            services.AddHttpClient<IAccessControlService, RemoteAccessControlService>(client =>
            {
                client.BaseAddress = new Uri(baseUrl);

                // Un timeout corto a propósito. El control de acceso está en el camino crítico
                // de cada petición: si tarda cinco segundos, la aplicación entera tarda cinco
                // segundos. Es mejor fallar rápido y denegar que dejar colgado al usuario.
                client.Timeout = TimeSpan.FromSeconds(5);
            });
        }
        else
        {
            // Modo en proceso: el negocio monta el módulo de control de acceso completo dentro
            // de sí mismo, con su propia conexión a la base de tuplas. Sigue siendo la MISMA
            // base de datos separada — lo que desaparece es el salto de red, no la separación
            // de datos.
            services.AddAccessControlModule(configuration);
            services.AddScoped<IAccessControlService, InProcessAccessControlService>();
        }

        return services;
    }
}
