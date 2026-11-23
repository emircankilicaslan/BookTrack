using LibraryApp.Data;
using LibraryApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Controllers
{
    public record AuditView(int Id, DateTime Timestamp, string Username, string Action, string EntityType, int? EntityId, string? Details);

    /// <summary>Denetim kaydı yalnızca okunur; yalnızca Admin görür. Ekleme, güncelleme ve silme uç noktası yoktur.</summary>
    [Route("api/audit")]
    [ApiController]
    [Authorize(Roles = Roles.Admin)]
    public class AuditController : ControllerBase
    {
        public const int MaxPageSize = 200;
        private readonly LibraryContext _context;

        public AuditController(LibraryContext context) => _context = context;

        /// <summary>İşlem ve kullanıcıya göre süzülür; yeniden eskiye sıralanır. Toplam sayı <c>X-Total-Count</c> başlığındadır.</summary>
        [HttpGet]
        public async Task<IEnumerable<AuditView>> List([FromQuery] string? action, [FromQuery] string? user,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            var query = _context.AuditLogs.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(action)) { var a = action.Trim(); query = query.Where(x => x.Action == a); }
            if (!string.IsNullOrWhiteSpace(user)) { var u = user.Trim(); query = query.Where(x => x.Username == u); }

            Response.Headers["X-Total-Count"] = (await query.CountAsync()).ToString();
            var size = Math.Clamp(pageSize, 1, MaxPageSize);
            return await query.OrderByDescending(x => x.Id)
                .Skip((Math.Max(page, 1) - 1) * size).Take(size)
                .Select(x => new AuditView(x.Id, x.Timestamp, x.Username, x.Action, x.EntityType, x.EntityId, x.Details))
                .ToListAsync();
        }
    }
}
