using LibraryApp.Data;
using LibraryApp.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Services
{
    /// <summary>
    /// Açık kayıt yoktur: kullanıcıları yalnızca Admin oluşturur. İlk Admin'in kilitli kalmaması için, veritabanında
    /// hiç aktif Admin yoksa ve <c>Auth:AdminUsername</c> ile <c>Auth:AdminPassword</c> verilmişse açılışta bir Admin oluşturulur.
    /// </summary>
    public static class AdminBootstrap
    {
        public static async Task EnsureAdminAsync(IServiceProvider services, IConfiguration config, ILogger logger)
        {
            var username = config["Auth:AdminUsername"]?.Trim();
            var password = config["Auth:AdminPassword"];
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) return;
            if (password.Length < PasswordRules.MinLength)
                throw new InvalidOperationException($"Auth:AdminPassword en az {PasswordRules.MinLength} karakter olmalıdır.");

            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LibraryContext>();
            if (await db.Users.AnyAsync(u => u.Role == Roles.Admin && u.IsActive)) return;

            var user = new AppUser { Username = username, Role = Roles.Admin, IsActive = true, CreatedAt = DateTime.UtcNow };
            user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, password);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            logger.LogInformation("İlk yönetici oluşturuldu: {Username}", username);
        }
    }

    public static class PasswordRules
    {
        public const int MinLength = 8;
        public const int MaxLength = 100;
    }
}
