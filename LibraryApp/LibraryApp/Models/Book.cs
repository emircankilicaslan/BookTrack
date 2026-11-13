using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace LibraryApp.Models
{
    public class Book
    {
        public int Id { get; set; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "Başlık zorunludur.")]
        public string Title { get; set; } = string.Empty;

        [Required(AllowEmptyStrings = false, ErrorMessage = "ISBN zorunludur.")]
        public string ISBN { get; set; } = string.Empty;

        [Range(1000, 2100, ErrorMessage = "Yayın yılı 1000 ile 2100 arasında olmalıdır.")]
        public int PublicationYear { get; set; }

        [Range(0, 100000, ErrorMessage = "Fiyat 0 ile 100000 arasında olmalıdır.")]
        public decimal Price { get; set; }

        public bool IsCheckedOut { get; set; }
        public string? CheckedOutBy { get; set; }
        public DateTime? CheckOutDate { get; set; }
        public DateTime? DueDate { get; set; }

        /// <summary>
        /// Eş zamanlı değişiklik denetimi (SQL Server rowversion). İki istek aynı kitabı aynı anda değiştirmeye
        /// çalışırsa yalnızca biri başarılı olur; diğeri 409 alır. İstemciye gösterilmez ve istemciden alınmaz.
        /// </summary>
        [JsonIgnore]
        public byte[]? RowVersion { get; set; }
    }
}
