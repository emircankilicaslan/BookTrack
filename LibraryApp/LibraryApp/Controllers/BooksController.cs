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

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Book>>> GetBooks()
        {
            return await _context.Books.ToListAsync();
        }

        [HttpPost("checkout/{id}")]
        public async Task<IActionResult> CheckOutBook(int id, [FromBody] string userName)
        {
            var book = await _context.Books.FindAsync(id);
            if (book == null || book.IsCheckedOut) return NotFound();

            book.IsCheckedOut = true;
            book.CheckedOutBy = userName;
            book.CheckOutDate = DateTime.Now;
            book.DueDate = DateTime.Now.AddDays(15); // 15 iş günü hesaplanmalı
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("checkin/{id}")]
        public async Task<IActionResult> CheckInBook(int id)
        {
            var book = await _context.Books.FindAsync(id);
            if (book == null || !book.IsCheckedOut) return NotFound();

            book.IsCheckedOut = false;
            book.CheckedOutBy = null;
            book.CheckOutDate = null;
            book.DueDate = null;
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}

