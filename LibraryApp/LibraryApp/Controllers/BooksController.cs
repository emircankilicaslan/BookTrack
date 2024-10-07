using LibraryApp.Data;
using LibraryApp.Models;
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
            return await _context.Books.ToListAsync();
        }

        // ID ile kitap getir (GET metodu)
        [HttpGet("{id}")]
        public async Task<ActionResult<Book>> GetBook(int id)
        {
            var book = await _context.Books.FindAsync(id);
            if (book == null) return NotFound();
            return book;
        }

        // Kitap ekle (POST metodu)
        [HttpPost]
        public async Task<IActionResult> AddBook([FromBody] Book newBook)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

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

            _context.Books.Remove(book);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // Kitap teslim alma (Check-Out) - POST metodu
        [HttpPost("checkout/{id}")]
        public async Task<IActionResult> CheckOutBook(int id, [FromBody] CheckOut checkoutDetails)
        {
            var book = await _context.Books.FindAsync(id);
            if (book == null || book.IsCheckedOut)
                return BadRequest(new { message = "Kitap bulunamadı veya zaten teslim alınmış." });

            book.IsCheckedOut = true;
            book.CheckedOutBy = checkoutDetails.Name;
            book.CheckOutDate = DateTime.Now;
            book.DueDate = CalculateDueDate(DateTime.Now, 15); // 15 iş günü

            // CheckOut kaydı oluştur
            checkoutDetails.BookId = id;
            checkoutDetails.CheckOutDate = DateTime.Now;
            checkoutDetails.DueDate = book.DueDate.Value;
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
            var lateDays = CalculateLateDays(book.DueDate.Value, DateTime.Now);
            var fine = lateDays * 5; // 5 ₺ / gün gecikme

            // Teslim işlemi güncelle
            book.IsCheckedOut = false;
            book.CheckedOutBy = null;
            book.CheckOutDate = null;
            book.DueDate = null;

            // CheckOut kaydı güncelle
            checkoutRecord.ActualReturnDate = DateTime.Now;

            await _context.SaveChangesAsync();
            return Ok(new { message = $"Kitap başarıyla teslim edildi. Gecikme cezası: {fine} ₺" });
        }

        // 15 iş günü hesaplayan yardımcı metod
        private DateTime CalculateDueDate(DateTime checkOutDate, int businessDays)
        {
            var dueDate = checkOutDate;
            while (businessDays > 0)
            {
                dueDate = dueDate.AddDays(1);
                if (dueDate.DayOfWeek != DayOfWeek.Saturday && dueDate.DayOfWeek != DayOfWeek.Sunday)
                    businessDays--;
            }
            return dueDate;
        }

        // Gecikme günlerini hesaplama metodu
        private int CalculateLateDays(DateTime dueDate, DateTime returnDate)
        {
            if (returnDate <= dueDate) return 0;

            int lateDays = 0;
            var date = dueDate;

            while (date < returnDate)
            {
                date = date.AddDays(1);
                if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
                    lateDays++;
            }

            return lateDays;
        }
    }
}

