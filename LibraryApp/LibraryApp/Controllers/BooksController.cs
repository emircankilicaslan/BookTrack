using LibraryApp.Data;
using LibraryApp.Models;
using LibraryApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LibraryApp.Controllers
{
    /// <summary>
    /// Kitap listesi için isteğe bağlı arama, süzgeç, sıralama ve sayfalama.
    /// <c>page</c> verilmezse tüm sonuçlar dizi olarak döner (geriye dönük uyum); toplam sayı <c>X-Total-Count</c> başlığındadır.
    /// </summary>
    public class BookQuery
    {
        /// <summary>Başlık, yazar veya ISBN içinde aranır (büyük/küçük harf duyarsız).</summary>
        public string? Q { get; set; }
        public string? Category { get; set; }

        /// <summary>true: yalnızca rafta olanlar, false: yalnızca ödünçte olanlar.</summary>
        public bool? Available { get; set; }

        /// <summary>true: yalnızca süresi geçmiş ödünçteki kitaplar.</summary>
        public bool? Overdue { get; set; }

        /// <summary>title (varsayılan), author, year, price.</summary>
        public string? Sort { get; set; }
        public bool Desc { get; set; }
        public int? Page { get; set; }
        public int? PageSize { get; set; }
    }

    /// <summary>
    /// Tüm uç noktalar giriş gerektirir. Admin ve Librarian kitap ekleyebilir/düzenleyebilir, ödünç verip iade alabilir;
    /// kitap silme yalnızca Admin'e açıktır. Değiştiren her işlem denetim kaydına yazılır.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Librarian}")]
    public class BooksController : ControllerBase
    {
        private const string IsbnVar = "Bu ISBN ile kayıtlı başka bir kitap zaten var.";
        private const string Cakisma = "Kitap aynı anda başka bir işlemle değiştirildi. Güncel durumu görmek için yenileyin.";
        public const int MaxPageSize = 100;

        private readonly LibraryContext _context;
        private readonly LibraryOptions _options;
        private readonly IReadOnlySet<DateOnly> _holidays;
        private readonly TimeProvider _time;
        private readonly AuditService _audit;

        public BooksController(LibraryContext context, IOptions<LibraryOptions> options, TimeProvider time, AuditService audit)
        {
            _context = context;
            _options = options.Value;
            _holidays = _options.HolidaySet;
            _time = time;
            _audit = audit;
        }

        private DateTime Now => _time.GetLocalNow().DateTime;

        // Kitapları listele; arama, süzgeç, sıralama ve sayfalama isteğe bağlıdır.
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Book>>> GetBooks([FromQuery] BookQuery query)
        {
            var books = _context.Books.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.Q))
            {
                var term = query.Q.Trim().ToLowerInvariant();
                books = books.Where(b => b.Title.ToLowerInvariant().Contains(term)
                    || b.ISBN.ToLowerInvariant().Contains(term)
                    || (b.Author != null && b.Author.ToLowerInvariant().Contains(term)));
            }
            if (!string.IsNullOrWhiteSpace(query.Category))
            {
                var category = query.Category.Trim();
                books = books.Where(b => b.Category == category);
            }
            if (query.Available is { } available)
                books = books.Where(b => b.IsCheckedOut == !available);
            if (query.Overdue == true)
            {
                var today = Now.Date;
                books = books.Where(b => b.IsCheckedOut && b.DueDate < today);
            }

            books = (query.Sort?.ToLowerInvariant(), query.Desc) switch
            {
                ("author", false) => books.OrderBy(b => b.Author).ThenBy(b => b.Title),
                ("author", true) => books.OrderByDescending(b => b.Author).ThenBy(b => b.Title),
                ("year", false) => books.OrderBy(b => b.PublicationYear).ThenBy(b => b.Title),
                ("year", true) => books.OrderByDescending(b => b.PublicationYear).ThenBy(b => b.Title),
                ("price", false) => books.OrderBy(b => b.Price).ThenBy(b => b.Title),
                ("price", true) => books.OrderByDescending(b => b.Price).ThenBy(b => b.Title),
                (_, true) => books.OrderByDescending(b => b.Title),
                _ => books.OrderBy(b => b.Title),
            };

            Response.Headers["X-Total-Count"] = (await books.CountAsync()).ToString();

            if (query.Page is { } page)
            {
                var size = Math.Clamp(query.PageSize ?? 20, 1, MaxPageSize);
                books = books.Skip((Math.Max(page, 1) - 1) * size).Take(size);
            }
            return await books.ToListAsync();
        }

        // Kayıtlı kategoriler (süzgeç seçimi için).
        [HttpGet("categories")]
        public async Task<ActionResult<IEnumerable<string>>> GetCategories() =>
            await _context.Books.AsNoTracking().Where(b => b.Category != null && b.Category != "")
                .Select(b => b.Category!).Distinct().OrderBy(c => c).ToListAsync();

        // ID ile kitap getir (GET metodu)
        [HttpGet("{id:int}")]
        public async Task<ActionResult<Book>> GetBook(int id)
        {
            var book = await _context.Books.FindAsync(id);
            if (book == null) return NotFound();
            return book;
        }

        // Kitabın ödünç geçmişi. Kişisel veri (TCKN, telefon) dışarıya verilmez.
        [HttpGet("{id:int}/history")]
        public async Task<ActionResult<IEnumerable<LoanHistoryItem>>> GetHistory(int id)
        {
            if (!await _context.Books.AnyAsync(b => b.Id == id)) return NotFound();

            return await _context.CheckOuts.AsNoTracking()
                .Where(c => c.BookId == id)
                .OrderByDescending(c => c.CheckOutDate)
                .Select(c => new LoanHistoryItem(c.Name, c.CheckOutDate, c.DueDate, c.ActualReturnDate, c.RenewalCount, c.LateDays, c.FineAmount))
                .ToListAsync();
        }

        // Kitap ekle (POST metodu)
        [HttpPost]
        public async Task<IActionResult> AddBook([FromBody] Book newBook)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            // Ödünç alanları yalnızca ödünç verme işlemiyle değişir; istemciden gelenler yok sayılır.
            newBook.Id = 0;
            newBook.IsCheckedOut = false;
            newBook.CheckedOutBy = null;
            newBook.CheckOutDate = null;
            newBook.DueDate = null;
            newBook.RenewalCount = 0;
            newBook.ISBN = newBook.ISBN.Trim();
            newBook.Author = Temizle(newBook.Author);
            newBook.Category = Temizle(newBook.Category);

            if (await _context.Books.AnyAsync(b => b.ISBN == newBook.ISBN))
                return Conflict(new { message = IsbnVar });

            _context.Books.Add(newBook);
            try
            {
                await _context.SaveChangesAsync();
                _audit.Add("KITAP_EKLE", "Kitap", newBook.Id, $"ISBN {newBook.ISBN}");
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Denetim ile kayıt arasında aynı ISBN başka bir istekle eklendiyse unique index yakalar.
                _context.ChangeTracker.Clear();
                if (await _context.Books.AsNoTracking().AnyAsync(b => b.ISBN == newBook.ISBN))
                    return Conflict(new { message = IsbnVar });
                throw;
            }
            return CreatedAtAction(nameof(GetBook), new { id = newBook.Id }, newBook);
        }

        // Kitap güncelle (PUT metodu)
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateBook(int id, [FromBody] Book updatedBook)
        {
            if (id != updatedBook.Id) return BadRequest("Kitap ID'leri uyuşmuyor.");
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var book = await _context.Books.FindAsync(id);
            if (book == null) return NotFound();

            var isbn = updatedBook.ISBN.Trim();
            if (await _context.Books.AnyAsync(b => b.ISBN == isbn && b.Id != id))
                return Conflict(new { message = IsbnVar });

            book.Title = updatedBook.Title;
            book.ISBN = isbn;
            book.Author = Temizle(updatedBook.Author);
            book.Category = Temizle(updatedBook.Category);
            book.PublicationYear = updatedBook.PublicationYear;
            book.Price = updatedBook.Price;
            _audit.Add("KITAP_GUNCELLE", "Kitap", id, $"ISBN {isbn}");

            try
            {
                if (!await TrySaveAsync()) return Conflict(new { message = Cakisma });
            }
            catch (DbUpdateException)
            {
                _context.ChangeTracker.Clear();
                if (await _context.Books.AsNoTracking().AnyAsync(b => b.ISBN == isbn && b.Id != id))
                    return Conflict(new { message = IsbnVar });
                throw;
            }
            return NoContent();
        }

        // Kitap sil (DELETE metodu)
        [HttpDelete("{id:int}")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> DeleteBook(int id)
        {
            var book = await _context.Books.FindAsync(id);
            if (book == null) return NotFound();
            if (book.IsCheckedOut)
                return Conflict(new { message = "Ödünçte olan kitap silinemez; önce iade alınmalıdır." });
            if (await _context.CheckOuts.AnyAsync(c => c.BookId == id))
                return Conflict(new { message = "Ödünç geçmişi olan kitap silinemez; geçmiş kayıtları korunur." });

            _context.Books.Remove(book);
            _audit.Add("KITAP_SIL", "Kitap", id, $"ISBN {book.ISBN}");
            if (!await TrySaveAsync()) return Conflict(new { message = Cakisma });
            return NoContent();
        }

        /// <summary>Kaydeder; eş zamanlı değişiklik çakışmasında false döner.</summary>
        private async Task<bool> TrySaveAsync()
        {
            try
            {
                await _context.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                return false;
            }
        }

        private static string? Temizle(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        // İade edilirse şu an uygulanacak gecikme ve ceza (iade etmeden önce göstermek için).
        [HttpGet("{id:int}/fine-preview")]
        public async Task<ActionResult<FinePreview>> GetFinePreview(int id)
        {
            var book = await _context.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
            if (book == null) return NotFound();
            if (!book.IsCheckedOut || book.DueDate == null)
                return BadRequest(new { message = "Kitap ödünçte değil." });

            var lateDays = LoanCalculator.LateBusinessDays(book.DueDate.Value, Now, _holidays);
            return new FinePreview(lateDays, LoanCalculator.Fine(lateDays), book.DueDate.Value);
        }

        // Kitap teslim alma (Check-Out) - POST metodu
        [HttpPost("checkout/{id:int}")]
        public async Task<IActionResult> CheckOutBook(int id, [FromBody] CheckOut checkoutDetails)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var book = await _context.Books.FindAsync(id);
            if (book == null || book.IsCheckedOut)
                return BadRequest(new { message = "Kitap bulunamadı veya zaten teslim alınmış." });

            var now = Now;
            book.IsCheckedOut = true;
            book.CheckedOutBy = checkoutDetails.Name;
            book.CheckOutDate = now;
            book.DueDate = LoanCalculator.DueDate(now, LoanCalculator.LoanBusinessDays, _holidays);
            book.RenewalCount = 0;

            // CheckOut kaydı oluştur
            checkoutDetails.Id = 0;
            checkoutDetails.BookId = id;
            checkoutDetails.CheckOutDate = now;
            checkoutDetails.DueDate = book.DueDate.Value;
            checkoutDetails.ActualReturnDate = null;
            checkoutDetails.RenewalCount = 0;
            checkoutDetails.LateDays = null;
            checkoutDetails.FineAmount = null;
            _context.CheckOuts.Add(checkoutDetails);
            // Kişisel veri (TCKN, telefon) denetim kaydına yazılmaz.
            _audit.Add("ODUNC_VER", "Kitap", id, $"Son teslim {book.DueDate.Value:dd.MM.yyyy}");

            // Aynı kitap aynı anda iki kişiye verilemez: kitap kaydı bu arada değiştiyse (başka bir ödünç gibi)
            // kayıt reddedilir ve ödünç kaydı da yazılmaz (SaveChanges tek işlemdir).
            if (!await TrySaveAsync()) return Conflict(new { message = Cakisma });
            return NoContent();
        }

        // Kitap teslim etme (Check-In) - POST metodu
        [HttpPost("checkin/{id:int}")]
        public async Task<IActionResult> CheckInBook(int id)
        {
            var book = await _context.Books.FindAsync(id);
            if (book == null || !book.IsCheckedOut)
                return BadRequest(new { message = "Kitap bulunamadı veya zaten teslim edilmiş." });

            var checkoutRecord = await _context.CheckOuts.FirstOrDefaultAsync(c => c.BookId == id && c.ActualReturnDate == null);
            if (checkoutRecord == null) return NotFound();

            // Ceza hesaplama
            var now = Now;
            var lateDays = LoanCalculator.LateBusinessDays(book.DueDate ?? now, now, _holidays);
            var fine = LoanCalculator.Fine(lateDays);

            // Teslim işlemi güncelle
            book.IsCheckedOut = false;
            book.CheckedOutBy = null;
            book.CheckOutDate = null;
            book.DueDate = null;
            book.RenewalCount = 0;

            // CheckOut kaydı güncelle
            checkoutRecord.ActualReturnDate = now;
            checkoutRecord.LateDays = lateDays;
            checkoutRecord.FineAmount = fine;
            _audit.Add("IADE_AL", "Kitap", id, lateDays > 0 ? $"{lateDays} iş günü gecikme, ceza {fine} ₺" : "Zamanında iade");

            if (!await TrySaveAsync()) return Conflict(new { message = Cakisma });
            return Ok(new CheckInResult($"Kitap başarıyla teslim edildi. Gecikme cezası: {fine} ₺", lateDays, fine));
        }

        /// <summary>
        /// Ödünç süresini uzatır: her uzatmada <c>RenewalBusinessDays</c> iş günü eklenir, en çok <c>MaxRenewals</c> kez.
        /// Süresi geçmiş ödünç uzatılamaz (önce iade alınır ve ceza uygulanır).
        /// </summary>
        [HttpPost("{id:int}/renew")]
        public async Task<IActionResult> Renew(int id)
        {
            var book = await _context.Books.FindAsync(id);
            if (book == null) return NotFound();
            if (!book.IsCheckedOut || book.DueDate == null)
                return BadRequest(new { message = "Kitap ödünçte değil." });
            if (book.DueDate.Value.Date < Now.Date)
                return Conflict(new { message = "Süresi geçmiş ödünç uzatılamaz; önce iade alınmalıdır." });
            if (book.RenewalCount >= _options.MaxRenewals)
                return Conflict(new { message = $"Bir ödünç en fazla {_options.MaxRenewals} kez uzatılabilir." });

            var record = await _context.CheckOuts.FirstOrDefaultAsync(c => c.BookId == id && c.ActualReturnDate == null);
            if (record == null) return NotFound();

            var newDue = LoanCalculator.DueDate(book.DueDate.Value, _options.RenewalBusinessDays, _holidays);
            book.DueDate = newDue;
            book.RenewalCount++;
            record.DueDate = newDue;
            record.RenewalCount = book.RenewalCount;
            _audit.Add("ODUNC_UZAT", "Kitap", id, $"Yeni son teslim {newDue:dd.MM.yyyy} ({book.RenewalCount}. uzatma)");

            if (!await TrySaveAsync()) return Conflict(new { message = Cakisma });
            return Ok(new RenewResult(newDue, book.RenewalCount, _options.MaxRenewals - book.RenewalCount));
        }
    }

    /// <summary>Ödünç geçmişi satırı (kişisel veri içermez).</summary>
    public record LoanHistoryItem(string Name, DateTime CheckOutDate, DateTime DueDate, DateTime? ActualReturnDate, int RenewalCount = 0, int? LateDays = null, decimal? FineAmount = null);

    /// <summary>İade edilirse uygulanacak gecikme ve ceza.</summary>
    public record FinePreview(int LateDays, decimal Fine, DateTime DueDate);

    /// <summary>İade yanıtı. <c>message</c> geriye dönük uyumluluk için korunur.</summary>
    public record CheckInResult(string Message, int LateDays, decimal Fine);

    /// <summary>Uzatma yanıtı: yeni son teslim, yapılan uzatma sayısı ve kalan hak.</summary>
    public record RenewResult(DateTime DueDate, int RenewalCount, int RemainingRenewals);
}
