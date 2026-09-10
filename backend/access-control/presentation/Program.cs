using Microsoft.EntityFrameworkCore;
using Playground.AccessControl.Api.Endpoints;
using Playground.AccessControl.Api.Seeding;
using Playground.AccessControl.Domain.Exceptions;
using Playground.AccessControl.Infrastructure;
using Playground.AccessControl.Infrastructure.Persistence;
using Playground.AccessControl.Infrastructure.Seeding;
using Scalar.AspNetCore;

// ═════════════════════════════════════════════════════════════════════════════
// El MÓDULO DE CONTROL DE ACCESO
//
// Este servicio no sabe nada del negocio. No conoce proyectos, ni carpetas, ni
// usuarios: solo tuplas, un modelo de relaciones y cómo evaluarlo. Todo lo que
// expone se reduce a tres preguntas:
//
//     Check        ¿puede ESTE sujeto hacer ESTO sobre ESTE objeto?
//     Expand       ¿QUIÉNES pueden hacer esto sobre este objeto?
//     ListObjects  ¿sobre QUÉ objetos puede este sujeto hacer esto?
//
// Es, a escala de juguete, lo que Zanzibar es para Drive, YouTube o Calendar:
// un servicio central al que todo lo demás le pregunta.
// ═════════════════════════════════════════════════════════════════════════════

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAccessControlModule(builder.Configuration);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

// El frontend del laboratorio ataca a las dos APIs directamente. Sin gateway que
// unifique el origen, hace falta CORS.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:14200"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// ── Arranque: migraciones y carga inicial ───────────────────────────────────
await using (var scope = app.Services.CreateAsyncScope())
{
    var context = scope.ServiceProvider.GetService<AccessControlDbContext>();

    if (context is not null)
        await DatabaseBootstrapper.EnsureReadyAsync(context, app.Logger);

    await scope.ServiceProvider.GetRequiredService<AccessControlSeeder>().SeedAsync();
}

app.UseCors();

// Traduce los errores de compilación del modelo a ProblemDetails, con el mismo formato
// que usan tus otras APIs.
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();

    if (feature?.Error is ModelValidationException modelError)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;

        await context.Response.WriteAsJsonAsync(new
        {
            title = "Validation failed",
            status = 400,
            errors = modelError.Errors
                .GroupBy(error => error.Line > 0 ? $"Línea {error.Line}" : "Modelo")
                .ToDictionary(group => group.Key, group => group.Select(error => error.ToString()).ToArray()),
        });

        return;
    }

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new { title = "Unexpected error", status = 500 });
}));

if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("Playground · Módulo de control de acceso"));
}

// ── Endpoints ───────────────────────────────────────────────────────────────
var accessControl = app.MapGroup("/access-control").WithTags("Access control");

accessControl
    .MapCheckEndpoints()
    .MapRelationshipEndpoints()
    .MapModelEndpoints()
    .MapGraphEndpoints()
    .MapAuditEndpoints()
    .MapGuidedCaseEndpoints()
    .MapTourEndpoints();

accessControl.MapPost("/seed/reset", async (AccessControlSeeder seeder, CancellationToken cancellationToken) =>
    {
        var count = await seeder.ResetAsync(cancellationToken);
        return Results.Ok(new { reset = true, tuples = count });
    })
    .WithTags("Seed")
    .WithName("ResetSeed")
    .WithSummary("Borra todas las relaciones y restaura el escenario inicial")
    .WithDescription(
        "Para volver al punto de partida después de haber experimentado. No toca los modelos "
        + "publicados, solo las tuplas.");

app.MapGet("/", () => Results.Ok(new
{
    service = "playground-access-control",
    description = "Módulo de control de acceso estilo Zanzibar. No conoce el negocio: solo relaciones.",
    docs = "/scalar/v1",
}));

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

/// <summary>Expuesto para los tests de integración.</summary>
public partial class Program;
