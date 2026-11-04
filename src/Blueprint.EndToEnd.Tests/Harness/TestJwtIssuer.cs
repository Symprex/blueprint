using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Blueprint.EndToEnd.Tests.Harness;

/// <summary>
/// An in-process JWT issuer that every app in a <see cref="MultiAppHost"/> trusts, so a test can mint a
/// caller's token and have it validated by real <c>JwtBearer</c> authentication on each hop.
/// </summary>
/// <remarks>
/// The signing key is a fresh random symmetric key per instance. Two issuers therefore never trust each
/// other's tokens, which is what lets a test prove a forged token is refused. Nothing is fetched from a
/// metadata endpoint: the apps are handed the key directly, so the suite needs no network beyond loopback.
/// </remarks>
public sealed class TestJwtIssuer
{
    /// <summary>The <c>iss</c> every token carries and every app requires.</summary>
    public const string Issuer = "https://issuer.blueprint-e2e.test";

    /// <summary>The <c>aud</c> every token carries and every app requires.</summary>
    public const string Audience = "blueprint-e2e";

    private readonly SymmetricSecurityKey _signingKey = new(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Mints a signed token carrying <paramref name="claims"/>. Repeated claim types, such as several
    /// <c>scope</c> claims, are written as a JSON array and read back as separate claims.
    /// </summary>
    /// <param name="claims">The caller's claims, written to the token unmapped.</param>
    /// <param name="lifetime">How long the token is valid for. Defaults to ten minutes.</param>
    /// <returns>The compact serialised token, ready for an <c>Authorization: Bearer</c> header.</returns>
    public string CreateToken(IEnumerable<Claim> claims, TimeSpan? lifetime = null)
    {
        var now = DateTime.UtcNow;

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now,
            IssuedAt = now,
            Expires = now + (lifetime ?? TimeSpan.FromMinutes(10)),
            SigningCredentials = new SigningCredentials(this._signingKey, SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>
    /// Configures an app's <c>JwtBearer</c> scheme to accept this issuer's tokens, and only them.
    /// </summary>
    /// <param name="options">The app's bearer options.</param>
    public void ConfigureBearer(JwtBearerOptions options)
    {
        // Claims keep their JWT names (sub, scope), which is what the sample apps' authorisers read.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = Audience,
            IssuerSigningKey = this._signingKey,
            ValidateIssuerSigningKey = true,
            NameClaimType = "sub",
        };
    }
}
