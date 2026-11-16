using System.ComponentModel.DataAnnotations;
using LibraryApp.Data;
using LibraryApp.Models;
using LibraryApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Controllers
{
    public sealed record CreateUserRequest(
        [Required, StringLength(50, MinimumLength = 3, ErrorMessage = "Kullanıcı adı 3-50 karakter olmalıdır.")] string? Username,
        [Required, StringLength(PasswordRules.MaxLength, MinimumLength = PasswordRules.MinLength, ErrorMessage = "Parola 8-100 karakter olmalıdır.")] string? Password,
        [Required] string? Role);

    public sealed record UserView(int Id, string Username, string Role, bool IsActive, DateTime CreatedAt);

    /// <summary>Kullanıcı yönetimi: yalnızca Admin.</summary>
    [Route("api/users")]
    [ApiController]
    [Authorize(Roles = Roles.Admin)]
    public class UsersController : ControllerBase
    {
        private readonly LibraryContext _context;
        private readonly PasswordHasher<AppUser> _hasher = new();

        public UsersController(LibraryContext context) => _context = context;

        [HttpGet]
        public async Task<IEnumerable<UserView>> List() =>
            await _context.Users.AsNoTracking().OrderBy(u => u.Username)
                .Select(u => new UserView(u.Id, u.Username, u.Role, u.IsActive, u.CreatedAt)).ToListAsync();

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var role = request.Role!.Trim();
            if (!Roles.All.Contains(role))
                return BadRequest(new { message = $"Rol {string.Join(" veya ", Roles.All)} olmalıdır." });
            var username = request.Username!.Trim();
            if (username.Length < 3) return BadRequest(new { message = "Kullanıcı adı en az 3 karakter olmalıdır." });
            if (await _context.Users.AnyAsync(u => u.Username == username))
                return Conflict(new { message = "Bu kullanıcı adı zaten kayıtlı." });

            var user = new AppUser { Username = username, Role = role, IsActive = true, CreatedAt = DateTime.UtcNow };
            user.PasswordHash = _hasher.HashPassword(user, request.Password!);
            _context.Users.Add(user);
            try { await _context.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                // Denetim ile kayıt arasında aynı ad başka bir istekle eklendiyse unique index yakalar.
                _context.ChangeTracker.Clear();
                if (await _context.Users.AsNoTracking().AnyAsync(u => u.Username == username))
                    return Conflict(new { message = "Bu kullanıcı adı zaten kayıtlı." });
                throw;
            }
            return Created($"/api/users/{user.Id}", new UserView(user.Id, user.Username, user.Role, user.IsActive, user.CreatedAt));
        }

        /// <summary>Kullanıcıyı pasife alır (giriş yapamaz, elindeki token anında geçersiz olur). Kendini ve son aktif Admin'i pasife alamaz.</summary>
        [HttpPost("{id:int}/deactivate")]
        public async Task<IActionResult> Deactivate(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();
            if (user.IsActive && user.Role == Roles.Admin &&
                !await _context.Users.AnyAsync(u => u.Id != id && u.Role == Roles.Admin && u.IsActive))
                return Conflict(new { message = "Son aktif yönetici pasife alınamaz." });
            if (User.FindFirst("name")?.Value == user.Username)
                return Conflict(new { message = "Kendi hesabınızı pasife alamazsınız." });

            user.IsActive = false;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("{id:int}/activate")]
        public async Task<IActionResult> Activate(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();
            user.IsActive = true;
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
