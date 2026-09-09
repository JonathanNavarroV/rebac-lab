using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Playground.Business.Api.Endpoints;
using Playground.Business.Api.Helpers;
using Playground.Business.Api.Identity;
using Playground.Business.Application;
using Playground.Business.Domain.Authorization;
using Playground.Business.Infrastructure;
using Playground.Business.Infrastructure.Persistence;
using Playground.Business.Infrastructure.Rbac;
using Scalar.AspNetCore;

// ═════════════════════════════════════════════════════════════════════════════
// El SERVICIO DE NEGOCIO
//
// Gestiona organizaciones, equipos, proyectos, carpetas y recursos. Y no sabe
// absolutamente nada de cómo se decide quién puede tocar qué: cada vez que hace
// falta, pregunta al módulo de control de acceso a través de un puerto con un
// solo método útil.
//
// El modo de esa conversación se decide en configuración:
//
//   "Authorization": { "Mode": "InProcess" }   el motor corre aquí dentro
//   "Authorization": { "Mode": "Remote"    }   se pregunta por HTTP a :15101
//
// Mismo modelo, mismas tuplas, mismas respuestas. Distinta latencia y distintas
// garantías. Cambiar entre los dos y mirar los números es el ejercicio.
// ═════════════════════════════════════════════════════════════════════════════

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBusinessApplication();
builder.Services.AddBusinessInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddSingleton<ActAsTokenService>();

// Los handlers van en orden: el primero que reconozca la excepción la atiende.
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddExceptionHandler<ForbiddenExceptionHandler>();
builder.Services.AddExceptionHandler<NotFoundExceptionHandler>();
builder.Services.AddProblemDetails();

var signingKey = builder.Configuration.GetValue<string>("Authentication:SigningKey")!;

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration.GetValue<string>("Authentication:Issuer") ?? "playground",
        ValidAudience = builder.Configuration.GetValue<string>("Authentication:Audience") ?? "playground",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
        ClockSkew = TimeSpan.FromSeconds(30),
    });

builder.Services.AddAuthorization();

builder.Services.AddOpenApi();

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:14200"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// ── Arranque ────────────────────────────────────────────────────────────────
await using (var scope = app.Services.CreateAsyncScope())
{
    var context = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
    await DatabaseBootstrapper.EnsureReadyAsync(context, app.Logger);

    // En modo InProcess el negocio monta también el módulo de control de acceso, así que
    // tiene que preparar SU base de datos igualmente: son dos bases distintas.
    var accessControlContext = scope.ServiceProvider
        .GetService<Playground.AccessControl.Infrastructure.Persistence.AccessControlDbContext>();

    if (accessControlContext is not null)
        await DatabaseBootstrapper.EnsureReadyAsync(accessControlContext, app.Logger);

    // En modo Remote este servicio no existe: el módulo se siembra a sí mismo al arrancar.
    var accessControlSeeder = scope.ServiceProvider
        .GetService<Playground.AccessControl.Infrastructure.Seeding.AccessControlSeeder>();

    if (accessControlSeeder is not null)
        await accessControlSeeder.SeedAsync();

    await scope.ServiceProvider.GetRequiredService<BusinessSeeder>().SeedAsync();
    await scope.ServiceProvider.GetRequiredService<RbacSeeder>().SeedAsync();
}

app.UseExceptionHandler();
app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("Playground · Servicio de negocio"));
}

app.MapBusinessEndpoints();

app.MapGet("/", (IAccessControlService accessControl) => Results.Ok(new
{
    service = "playground-business",
    description = "Servicio de negocio. No conoce el modelo de autorización: solo pregunta.",
    authorizationMode = accessControl.Mode,
    docs = "/scalar/v1",
}));

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

/// <summary>Expuesto para los tests de integración.</summary>
public partial class Program;
