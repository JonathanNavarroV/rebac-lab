using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Playground.Business.Application;

/// <summary>
/// Valida el request antes de que llegue al handler, como en tus otros proyectos.
/// </summary>
/// <remarks>
/// Nota deliberada sobre lo que este behavior <b>no</b> hace: no comprueba autorización. Sería
/// muy tentador añadir aquí un <c>AuthorizationBehavior</c> que leyera un atributo del comando
/// y llamara al puerto, y en un sistema real puede ser buena idea. Aquí no se hace porque
/// esconder el <c>CanAsync</c> en un pipeline haría desaparecer justo lo que se quiere ver: en
/// qué punto exacto el negocio le pregunta al control de acceso, y qué le pregunta.
/// </remarks>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var applicable = validators.ToList();

        if (applicable.Count == 0)
            return await next(cancellationToken);

        var context = new ValidationContext<TRequest>(request);

        var failures = (await Task.WhenAll(
                applicable.Select(validator => validator.ValidateAsync(context, cancellationToken))))
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next(cancellationToken);
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddBusinessApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        return services;
    }
}
