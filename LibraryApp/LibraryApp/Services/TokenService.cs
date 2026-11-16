using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LibraryApp.Models;
using Microsoft.IdentityModel.Tokens;

namespace LibraryApp.Services
{
    /// <summary>JWT (HS256) üretir. İmza anahtarı yapılandırmadan gelir (<c>Jwt:Secret</c>, en az 32 karakter) ve repoda tutulmaz.</summary>
    public sealed class TokenService
    {
        public const string Issuer = "BookTrack";
        public const string Audience = "BookTrack.Istemci";

        private readonly SymmetricSecurityKey _key;
        private readonly TimeSpan _lifetime;
        private readonly TimeProvider _clock;

        public TokenService(IConfiguration configuration, TimeProvider clock)
        {
            _key = SigningKey(configuration["Jwt:Secret"]);
            _lifetime = TimeSpan.FromMinutes(configuration.GetValue("Jwt:TtlMinutes", 480));
            _clock = clock;
        }

        public static SymmetricSecurityKey SigningKey(string? secret)
        {
            if (string.IsNullOrEmpty(secret) || secret.Length < 32)
                throw new InvalidOperationException("Jwt:Secret tanımlı olmalı ve en az 32 karakter olmalıdır. Ortam değişkeni: Jwt__Secret");
            return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        }

        public (string Token, DateTime ExpiresAt) Create(AppUser user)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var expires = now + _lifetime;
            var token = new JwtSecurityToken(
                issuer: Issuer, audience: Audience,
                claims:
                [
                    new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                    new Claim("name", user.Username),
                    new Claim("role", user.Role),
                ],
                notBefore: now, expires: expires,
                signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));
            return (new JwtSecurityTokenHandler().WriteToken(token), expires);
        }
    }
}
