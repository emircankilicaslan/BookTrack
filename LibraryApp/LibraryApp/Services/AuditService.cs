using LibraryApp.Data;
using LibraryApp.Models;

namespace LibraryApp.Services
{
    /// <summary>
    /// Denetim kaydı yazar. <see cref="Add"/> kaydı yalnızca bağlama ekler; çağıranın <c>SaveChanges</c> çağrısıyla
    /// işlemle birlikte kaydedilir (iş geri alınırsa kayıt da yazılmaz). Kişisel veri ve parola asla yazılmaz.
    /// </summary>
    public class AuditService
    {
        private readonly LibraryContext _context;
        private readonly IHttpContextAccessor _http;
        private readonly TimeProvider _time;

        public AuditService(LibraryContext context, IHttpContextAccessor http, TimeProvider time)
        {
            _context = context;
            _http = http;
            _time = time;
        }

        private string CurrentUser => Truncate(_http.HttpContext?.User.FindFirst("name")?.Value ?? "sistem", 50);

        public void Add(string action, string entityType, int? entityId = null, string? details = null, string? username = null)
        {
            _context.AuditLogs.Add(new AuditLog
            {
                Timestamp = _time.GetUtcNow().UtcDateTime,
                Username = Truncate(username ?? CurrentUser, 50),
                Action = Truncate(action, 40),
                EntityType = Truncate(entityType, 30),
                EntityId = entityId,
                Details = details is null ? null : Truncate(details, 500),
            });
        }

        /// <summary>Başka bir değişiklikle birlikte kaydedilmeyecek olaylar için (ör. başarısız giriş).</summary>
        public async Task LogAsync(string action, string entityType, int? entityId = null, string? details = null, string? username = null)
        {
            Add(action, entityType, entityId, details, username);
            await _context.SaveChangesAsync();
        }

        private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
    }
}
