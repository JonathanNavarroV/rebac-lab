using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Playground.Business.Domain.Authorization;

namespace Playground.Business.Api.Identity;

/// <summary>
/// Quién está haciendo la petición, resuelto desde el JWT o desde la cabecera de laboratorio.
/// </summary>
/// <remarks>
/// <para>
/// Lo que importa de esta clase es lo que <b>no</b> hace: no consulta roles, ni permisos, ni
/// grupos, ni pertenencias. Solo resuelve <i>quién es</i>. En una aplicación con RBAC, este
/// sería el sitio donde se cargarían los roles del usuario en la sesión para poder decidir
/// después; aquí no hay nada que cargar, porque la identidad y la autorización están separadas
/// del todo.
/// </para>
/// <para>
/// Y esa separación tiene una ventaja concreta: no hay que cerrar la sesión ni refrescar nada
/// cuando cambian los permisos de alguien. En un sistema que mete los roles en el token, quitar
/// un permiso no surte efecto hasta que el token caduca — un agujero conocido y difícil de
/// cerrar.
/// </para>
/// </remarks>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    /// <summary>
    /// Cabecera de conveniencia del laboratorio: permite cambiar de identidad sin pedir un
    /// token, que es justo lo que hace cómodo experimentar. En un sistema real sería una
    /// vulnerabilidad de suplantación de manual.
    /// </summary>
    public const string ActAsHeader = "X-Act-As";

    public string Id
    {
        get
        {
            var context = accessor.HttpContext;

            if (context is null)
                return "anonymous";

            var header = context.Request.Headers[ActAsHeader].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(header))
                return header.Trim();

            return context.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                   ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? "anonymous";
        }
    }

    public bool IsAuthenticated => Id != "anonymous";
}

/// <summary>
/// Emisor de tokens del laboratorio: firma un JWT con el identificador que se le pida, sin
/// contraseña.
/// </summary>
/// <remarks>
/// Es deliberadamente inseguro y solo tiene sentido en un laboratorio. Existe para que el
/// sistema tenga un <c>ICurrentUser</c> de verdad en lugar de pasar el usuario como parámetro
/// por todas partes — que habría sido más simple, pero habría ocultado dónde entra la identidad
/// en el flujo.
/// </remarks>
public sealed class ActAsTokenService(IConfiguration configuration)
{
    public string IssueToken(string userId)
    {
        var key = configuration.GetValue<string>("Authentication:SigningKey")
                  ?? throw new InvalidOperationException("Falta 'Authentication:SigningKey'.");

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: configuration.GetValue<string>("Authentication:Issuer") ?? "playground",
            audience: configuration.GetValue<string>("Authentication:Audience") ?? "playground",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ],
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
