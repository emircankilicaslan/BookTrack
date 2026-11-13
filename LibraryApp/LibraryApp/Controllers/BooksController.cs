using LibraryApp.Data;
using LibraryApp.Models;
using LibraryApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BooksController : ControllerBase
    {
        private readonly LibraryContext _context;

        public BooksController(LibraryContext context)
        {
            _context = context;
        }

        // Tüm kitapları listele (GET metodu)
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Book>>> GetBooks()
        {
            return await _context.Books.AsNoTracking().OrderBy(b => b.Title).ToListAsync();
        }

        // ID ile kitap getir (GET metodu)
        [HttpGet("{id}")]
        public async Task<ActionResult<Book>> GetBook(int id)
        {
            var book = await _context.Books.FindAsync(id);
            if (book == null) return NotFound();
            return book;
        }

        // Kitabın ödünç geçmişi. Kişisel veri (TCKN, telefon) dışarıya verilmez.
        [HttpGet("{id}/history")]
        public async Task<ActionResult<IEnumerable<LoanHistoryItem>>> GetHistory(int id)
        {
            if (!await _context.Books.AnyAsync(b => b.Id == id)) return NotFound();

            return await _context.CheckOuts.AsNoTracking()
                .Where(c => c.BookId == id)
                .OrderByDescending(c => c.CheckOutDate)
                .Select(c => new LoanHistoryItem(c.Name, c.CheckOutDate, c.DueDate, c.ActualReturnDate))
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

            _context.Books.Add(newBook);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetBook), new { id = newBook.Id }, newBook);
        }

        // Kitap güncelle (PUT metodu)
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateBook(int id, [FromBody] Book updatedBook)
        {
            if (id != updatedBook.Id) return BadRequest("Kitap ID'leri uyuşmuyor.");
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var book = await _context.Books.FindAsync(id);
            if (book == null) return NotFound();

            book.Title = updatedBook.Title;
            book.ISBN = updatedBook.ISBN;
            book.PublicationYear = updatedBook.PublicationYear;
            book.Price = updatedBook.Price;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // Kitap sil (DELETE metodu)
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteBook(int id)
        {
            var book = await _context.Books.FindAsync(id);
            if (book == null) return NotFound();
            if (book.IsCheckedOut)
                return Conflict(new { message = "Ödünçte olan kitap silinemez; önce iade alınmalıdır." });

            _context.Books.Remove(book);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // Kitap teslim alma (Check-Out) - POST metodu
        [HttpPost("checkout/{id}")]
        public async Task<IActionResult> CheckOutBook(int id, [FromBody] CheckOut checkoutDetails)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var book = await _context.Books.FindAsync(id);
            if (book == null || book.IsCheckedOut)
                return BadRequest(new { message = "Kitap bulunamadı veya zaten teslim alınmış." });

            var now = DateTime.Now;
            book.IsCheckedOut = true;
            book.CheckedOutBy = checkoutDetails.Name;
            book.CheckOutDate = now;
            book.DueDate = LoanCalculator.DueDate(now);

            // CheckOut kaydı oluştur
            checkoutDetails.Id = 0;
            checkoutDetails.BookId = id;
            checkoutDetails.CheckOutDate = now;
            checkoutDetails.DueDate = book.DueDate.Value;
            checkoutDetails.ActualReturnDate = null;
            _context.CheckOuts.Add(checkoutDetails);

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // Kitap teslim etme (Check-In) - POST metodu
        [HttpPost("checkin/{id}")]
        public async Task<IActionResult> CheckInBook(int id)
        {
            var book = await _context.Books.FindAsync(id);
            if (book == null || !book.IsCheckedOut)
                return BadRequest(new { message = "Kitap bulunamadı veya zaten teslim edilmiş." });

            var checkoutRecord = await _context.CheckOuts.FirstOrDefaultAsync(c => c.BookId == id && c.ActualReturnDate == null);
            if (checkoutRecord == null) return NotFound();

            // Ceza hesaplama
            var now = DateTime.Now;
            var lateDays = LoanCalculator.LateBusinessDays(book.DueDate ?? now, now);
            var fine = LoanCalculator.Fine(lateDays);

            // Teslim işlemi güncelle
            book.IsCheckedOut = false;
            book.CheckedOutBy = null;
            book.CheckOutDate = null;
            book.DueDate = null;

            // CheckOut kaydı güncelle
            checkoutRecord.ActualReturnDate = now;

            await _context.SaveChangesAsync();
            return Ok(new CheckInResult($"Kitap başarıyla teslim edildi. Gecikme cezası: {fine} ₺", lateDays, fine));
        }
    }

    /// <summary>Ödünç geçmişi satırı (kişisel veri içermez).</summary>
    public record LoanHistoryItem(string Name, DateTime CheckOutDate, DateTime DueDate, DateTime? ActualReturnDate);

    /// <summary>İade yanıtı. <c>message</c> geriye dönük uyumluluk için korunur.</summary>
    public record CheckInResult(string Message, int LateDays, decimal Fine);
}
