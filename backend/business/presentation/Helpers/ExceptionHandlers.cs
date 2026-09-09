using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Playground.Business.Domain.Authorization;

namespace Playground.Business.Api.Helpers;

/// <summary>
/// Traduce <see cref="ValidationException"/> al <c>ValidationProblemDetails</c> que espera tu
/// frontend.
/// </summary>
public sealed class ValidationExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not ValidationException validationException)
            return false;

        var errors = validationException.Errors
            .GroupBy(failure => failure.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).ToArray());

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

        await httpContext.Response.WriteAsJsonAsync(
            new { title = "Validation failed", status = 400, errors },
            cancellationToken);

        return true;
    }
}

/// <summary>
/// Traduce un DENY del módulo de control de acceso a un 403 con el motivo.
/// </summary>
/// <remarks>
/// <para>
/// Devolver el motivo en el cuerpo es una decisión <b>de laboratorio</b> y conviene tenerlo
/// muy presente. En un sistema real, explicar por qué se ha denegado filtra información sobre
/// la estructura interna: «no eres administrador de Acme» ya confirma que Acme existe y que
/// tiene administradores, y quien esté sondeando aprende el organigrama a base de 403.
/// </para>
/// <para>
/// Lo correcto en producción es responder un 403 escueto al usuario y dejar el detalle
/// únicamente en la auditoría, que es exactamente para lo que sirve la tabla
/// <c>decision_audit</c> del módulo.
/// </para>
/// </remarks>
public sealed class ForbiddenExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not ForbiddenException forbidden)
            return false;

        httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;

        await httpContext.Response.WriteAsJsonAsync(
            new
            {
                title = "Forbidden",
                status = 403,
                detail = forbidden.Message,
                subject = forbidden.Subject,
                relation = forbidden.Relation,
                @object = forbidden.Object,

                // El "por qué", tal cual lo redactó el módulo de control de acceso.
                reason = forbidden.Reason,

                // Enlace directo para investigar el DENY en el Authorization Explorer.
                explorer = new
                {
                    subject = forbidden.Subject,
                    relation = forbidden.Relation,
                    @object = forbidden.Object,
                },
            },
            cancellationToken);

        return true;
    }
}

public sealed class NotFoundExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not NotFoundException notFound)
            return false;

        httpContext.Response.StatusCode = StatusCodes.Status404NotFound;

        await httpContext.Response.WriteAsJsonAsync(
            new { title = "Not found", status = 404, detail = notFound.Message },
            cancellationToken);

        return true;
    }
}
