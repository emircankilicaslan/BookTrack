namespace LibraryApp.Models
{
    public class CheckOut
    {
        public int Id { get; set; }
        public int BookId { get; set; }
        public string Name { get; set; }
        public int PhoneNumber { get; set; }
        public int TCKN { get; set; }
        public DateTime CheckOutDate { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime? ActualReturnDate { get; set; }
    }
}
