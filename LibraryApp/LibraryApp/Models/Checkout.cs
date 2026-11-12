using System.ComponentModel.DataAnnotations;

namespace LibraryApp.Models
{
    public class CheckOut
    {
        public int Id { get; set; }
        public int BookId { get; set; }
        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;
        [StringLength(20)]
        public string? PhoneNumber { get; set; }
        /// <summary>T.C. kimlik numarası 11 hanelidir; int'e sığmadığı için metin olarak saklanır.</summary>
        [Required, StringLength(11, MinimumLength = 11), RegularExpression(@"^\d{11}$", ErrorMessage = "TCKN 11 haneli olmalıdır.")]
        public string TCKN { get; set; } = string.Empty;
        public DateTime CheckOutDate { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime? ActualReturnDate { get; set; }
    }
}
