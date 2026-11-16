using System.ComponentModel.DataAnnotations;

namespace LibraryApp.Models
{
    public static class Roles
    {
        /// <summary>Tüm işlemler: kitap silme ve kullanıcı yönetimi dahil.</summary>
        public const string Admin = "Admin";

        /// <summary>Kitap ekleme/düzenleme, ödünç verme ve iade; kitap silemez, kullanıcı yönetemez.</summary>
        public const string Librarian = "Librarian";

        public static readonly string[] All = [Admin, Librarian];
    }

    /// <summary>Sisteme giriş yapabilen kullanıcı (kütüphane görevlisi). Parola yalnızca özet (PBKDF2) olarak saklanır.</summary>
    public class AppUser
    {
        public int Id { get; set; }

        [Required, StringLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required, StringLength(200)]
        public string PasswordHash { get; set; } = string.Empty;

        [Required, StringLength(20)]
        public string Role { get; set; } = Roles.Librarian;

        /// <summary>Pasif kullanıcı giriş yapamaz; elindeki geçerli token da bir sonraki istekte reddedilir.</summary>
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; }
    }
}
