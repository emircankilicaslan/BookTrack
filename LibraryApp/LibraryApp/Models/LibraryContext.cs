using Microsoft.EntityFrameworkCore;
using LibraryApp.Models;

namespace LibraryApp.Data
{
    public class LibraryContext : DbContext
    {
        // DbContextOptions constructor
        public LibraryContext(DbContextOptions<LibraryContext> options)
            : base(options)
        {
        }

        
        public LibraryContext() { }

        public DbSet<Book> Books { get; set; }
        public DbSet<CheckOut> CheckOuts { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Book>()
                .Property(b => b.Price)
                .HasColumnType("decimal(18, 2)");

           
        }
    }
}
