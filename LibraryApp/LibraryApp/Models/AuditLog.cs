using System.ComponentModel.DataAnnotations;

namespace LibraryApp.Models
{
    /// <summary>
    /// Denetim kaydı: kim, ne zaman, neyi yaptı. Kişisel veri (TCKN, telefon) ve parola içermez;
    /// uygulama bu tabloya yalnızca ekleme yapar, güncelleme ve silme uç noktası yoktur.
    /// </summary>
    public class AuditLog
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }

        [Required, StringLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required, StringLength(40)]
        public string Action { get; set; } = string.Empty;

        [Required, StringLength(30)]
        public string EntityType { get; set; } = string.Empty;

        public int? EntityId { get; set; }

        [StringLength(500)]
        public string? Details { get; set; }
    }
}
