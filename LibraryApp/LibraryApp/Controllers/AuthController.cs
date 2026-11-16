using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using LibraryApp.Data;
using LibraryApp.Models;
using LibraryApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Controllers
{
    public sealed record LoginRequest([Required] string? Username, [Required] string? Password);

    public sealed record LoginResponse(string Token, DateTime ExpiresAt, string Username, string Role);

    [Route("api/auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly LibraryContext _context;
        private readonly TokenService _tokens;
        private readonly PasswordHasher<AppUser> _hasher = new();

        // Kullanıcı yoksa da bir özet karşılaştırması yapılır: yanıt süresiyle hesap varlığı sızmasın.
        private static readonly AppUser Dummy = new() { Username = "-" };
        private static readonly string DummyHash = new PasswordHasher<AppUser>().HashPassword(Dummy, "dummy-password-1");

        public AuthController(LibraryContext context, TokenService tokens)
        {
            _context = context;
            _tokens = tokens;
        }

        [HttpPost("login")]
        [AllowAnonymous]
        [EnableRateLimiting("login")]
        public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
        {
            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
                return BadRequest(new { message = "Kullanıcı adı ve parola zorunludur." });

            var username = request.Username.Trim();
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username);
            var result = _hasher.VerifyHashedPassword(user ?? Dummy, user?.PasswordHash ?? DummyHash, request.Password);
            if (user is null || !user.IsActive || result == PasswordVerificationResult.Failed)
                return Unauthorized(new { message = "Kullanıcı adı veya parola hatalı." });

            var (token, expires) = _tokens.Create(user);
            return new LoginResponse(token, expires, user.Username, user.Role);
        }

        /// <summary>Oturumdaki kullanıcıyı döndürür (arayüz rol bazlı seçenekleri buna göre gösterir).</summary>
        [HttpGet("me")]
        [Authorize]
        public ActionResult<object> Me() => new
        {
            username = User.FindFirstValue("name"),
            role = User.FindFirstValue("role"),
        };
    }
}
