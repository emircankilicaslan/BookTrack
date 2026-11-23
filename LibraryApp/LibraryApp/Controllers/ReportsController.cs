using LibraryApp.Data;
using LibraryApp.Models;
using LibraryApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LibraryApp.Controllers
{
    public record PopularBook(int BookId, string Title, int Loans);
    public record MonthlyLoans(string Month, int Loans, int Returns);
    public record CategoryCount(string Category, int Books);

    public record SummaryView(
        int TotalBooks, int CheckedOut, int Available, int Overdue, int DueSoon,
        int TotalLoans, decimal FinesTotal, decimal FinesThisMonth,
        IReadOnlyList<PopularBook> Popular, IReadOnlyList<MonthlyLoans> Monthly, IReadOnlyList<CategoryCount> Categories);

    /// <summary>Süresi geçmiş ödünç satırı (kişisel veri içermez: yalnızca okuyucu adı).</summary>
    public record OverdueItem(int BookId, string Title, string Borrower, DateTime DueDate, int LateDays, decimal Fine, int RenewalCount);

    /// <summary>Özet, geciken ödünçler ve dışa aktarma. Admin ve Librarian erişir.</summary>
    [Route("api/reports")]
    [ApiController]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Librarian}")]
    public class ReportsController : ControllerBase
    {
        /// <summary>"Yaklaşan" sayılan kalan iş günü.</summary>
        public const int DueSoonBusinessDays = 3;

        private readonly LibraryContext _context;
        private readonly IReadOnlySet<DateOnly> _holidays;
        private readonly TimeProvider _time;
        private readonly AuditService _audit;

        public ReportsController(LibraryContext context, IOptions<LibraryOptions> options, TimeProvider time, AuditService audit)
        {
            _context = context;
            _holidays = options.Value.HolidaySet;
            _time = time;
            _audit = audit;
        }

        private DateTime Now => _time.GetLocalNow().DateTime;

        [HttpGet("summary")]
        public async Task<SummaryView> Summary()
        {
            var today = Now.Date;
            var monthStart = new DateTime(today.Year, today.Month, 1);
            var soon = LoanCalculator.DueDate(today, DueSoonBusinessDays, _holidays).Date.AddDays(1);

            var total = await _context.Books.CountAsync();
            var checkedOut = await _context.Books.CountAsync(b => b.IsCheckedOut);
            var overdue = await _context.Books.CountAsync(b => b.IsCheckedOut && b.DueDate < today);
            var dueSoon = await _context.Books.CountAsync(b => b.IsCheckedOut && b.DueDate >= today && b.DueDate < soon);
            var loans = await _context.CheckOuts.CountAsync();
            var finesTotal = await _context.CheckOuts.SumAsync(c => c.FineAmount) ?? 0m;
            var finesMonth = await _context.CheckOuts.Where(c => c.ActualReturnDate >= monthStart).SumAsync(c => c.FineAmount) ?? 0m;

            var top = await _context.CheckOuts.GroupBy(c => c.BookId)
                .Select(g => new { BookId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count).ThenBy(x => x.BookId).Take(5).ToListAsync();
            var ids = top.Select(t => t.BookId).ToList();
            var titles = await _context.Books.Where(b => ids.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Title);
            var popular = top.Select(t => new PopularBook(t.BookId, titles.GetValueOrDefault(t.BookId, "?"), t.Count)).ToList();

            var first = monthStart.AddMonths(-5);
            var recent = await _context.CheckOuts.Where(c => c.CheckOutDate >= first || c.ActualReturnDate >= first)
                .Select(c => new { c.CheckOutDate, c.ActualReturnDate }).ToListAsync();
            var monthly = Enumerable.Range(0, 6).Select(i =>
            {
                var m = first.AddMonths(i);
                var next = m.AddMonths(1);
                return new MonthlyLoans(m.ToString("yyyy-MM"),
                    recent.Count(c => c.CheckOutDate >= m && c.CheckOutDate < next),
                    recent.Count(c => c.ActualReturnDate >= m && c.ActualReturnDate < next));
            }).ToList();

            var categories = (await _context.Books.GroupBy(b => b.Category)
                .Select(g => new { Category = g.Key, Count = g.Count() }).ToListAsync())
                .Select(x => new CategoryCount(string.IsNullOrWhiteSpace(x.Category) ? "Kategorisiz" : x.Category, x.Count))
                .OrderByDescending(x => x.Books).ThenBy(x => x.Category).ToList();

            return new SummaryView(total, checkedOut, total - checkedOut, overdue, dueSoon, loans, finesTotal, finesMonth, popular, monthly, categories);
        }

        [HttpGet("overdue")]
        public async Task<IReadOnlyList<OverdueItem>> Overdue() => await OverdueRows();

        private async Task<List<OverdueItem>> OverdueRows()
        {
            var now = Now;
            var rows = await _context.CheckOuts.AsNoTracking()
                .Where(c => c.ActualReturnDate == null && c.DueDate < now.Date)
                .Join(_context.Books, c => c.BookId, b => b.Id, (c, b) => new { c.BookId, b.Title, c.Name, c.DueDate, c.RenewalCount })
                .ToListAsync();
            return rows
                .Select(r =>
                {
                    var late = LoanCalculator.LateBusinessDays(r.DueDate, now, _holidays);
                    return new OverdueItem(r.BookId, r.Title, r.Name, r.DueDate, late, LoanCalculator.Fine(late), r.RenewalCount);
                })
                .OrderByDescending(r => r.LateDays).ThenBy(r => r.Title).ToList();
        }

        /// <summary>kind: books | loans | overdue; format: csv (varsayılan) | xlsx. Kişisel veri (TCKN, telefon) dışa verilmez.</summary>
        [HttpGet("export/{kind}")]
        public async Task<IActionResult> Export(string kind, [FromQuery] string format = "csv")
        {
            var (name, headers, rows) = kind.ToLowerInvariant() switch
            {
                "books" => await BooksTable(),
                "loans" => await LoansTable(),
                "overdue" => await OverdueTable(),
                _ => default,
            };
            if (headers is null) return NotFound(new { message = "Bilinmeyen rapor. books, loans veya overdue olmalıdır." });

            var stamp = Now.ToString("yyyy-MM-dd");
            byte[] content;
            string type, file;
            switch (format.ToLowerInvariant())
            {
                case "csv": content = TableExport.Csv(headers, rows!); type = TableExport.CsvType; file = $"{name}-{stamp}.csv"; break;
                case "xlsx": content = TableExport.Xlsx(name, headers, rows!); type = TableExport.XlsxType; file = $"{name}-{stamp}.xlsx"; break;
                default: return BadRequest(new { message = "Biçim csv veya xlsx olmalıdır." });
            }

            _audit.Add("DISA_AKTAR", "Rapor", null, $"{kind}, {format.ToLowerInvariant()}, {rows!.Count} satır");
            await _context.SaveChangesAsync();
            Response.Headers["Cache-Control"] = "no-store";
            return File(content, type, file);
        }

        private async Task<(string, IReadOnlyList<string>, List<IReadOnlyList<object?>>)> BooksTable()
        {
            var books = await _context.Books.AsNoTracking().OrderBy(b => b.Title).ToListAsync();
            var rows = books.Select(b => (IReadOnlyList<object?>)new object?[]
            {
                b.Id, b.Title, b.Author, b.Category, b.ISBN, b.PublicationYear, b.Price,
                b.IsCheckedOut ? "Ödünçte" : "Rafta", b.CheckedOutBy, b.DueDate,
            }).ToList();
            return ("kitaplar", ["No", "Başlık", "Yazar", "Kategori", "ISBN", "Yayın yılı", "Fiyat (₺)", "Durum", "Ödünç alan", "Son teslim"], rows);
        }

        private async Task<(string, IReadOnlyList<string>, List<IReadOnlyList<object?>>)> LoansTable()
        {
            var loans = await _context.CheckOuts.AsNoTracking()
                .Join(_context.Books, c => c.BookId, b => b.Id, (c, b) => new { b.Title, c })
                .OrderByDescending(x => x.c.CheckOutDate).ToListAsync();
            var rows = loans.Select(x => (IReadOnlyList<object?>)new object?[]
            {
                x.Title, x.c.Name, x.c.CheckOutDate, x.c.DueDate, x.c.ActualReturnDate, x.c.RenewalCount, x.c.LateDays, x.c.FineAmount,
            }).ToList();
            return ("oduncler", ["Kitap", "Okuyucu", "Ödünç tarihi", "Son teslim", "İade tarihi", "Uzatma", "Gecikme (iş günü)", "Ceza (₺)"], rows);
        }

        private async Task<(string, IReadOnlyList<string>, List<IReadOnlyList<object?>>)> OverdueTable()
        {
            var rows = (await OverdueRows()).Select(o => (IReadOnlyList<object?>)new object?[]
            {
                o.Title, o.Borrower, o.DueDate, o.LateDays, o.Fine, o.RenewalCount,
            }).ToList();
            return ("geciken-oduncler", ["Kitap", "Okuyucu", "Son teslim", "Gecikme (iş günü)", "Ceza (₺)", "Uzatma"], rows);
        }
    }
}
