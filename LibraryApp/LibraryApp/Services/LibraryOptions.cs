namespace LibraryApp.Services
{
    /// <summary><c>Library</c> yapılandırma bölümü.</summary>
    public class LibraryOptions
    {
        public const string SectionName = "Library";

        /// <summary>
        /// Resmi tatiller (yyyy-MM-dd). Ödünç süresi ve gecikme cezası hesabında hafta sonları gibi iş günü sayılmaz.
        /// Dini bayram tarihleri her yıl değiştiği için yılbaşında güncellenmelidir.
        /// </summary>
        public List<DateOnly> Holidays { get; set; } = new();

        /// <summary>Bir ödüncün en fazla kaç kez uzatılabileceği.</summary>
        public int MaxRenewals { get; set; } = 2;

        /// <summary>Her uzatmada eklenen iş günü.</summary>
        public int RenewalBusinessDays { get; set; } = 7;

        public IReadOnlySet<DateOnly> HolidaySet => Holidays.ToHashSet();
    }
}
