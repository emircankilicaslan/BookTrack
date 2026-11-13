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

        public IReadOnlySet<DateOnly> HolidaySet => Holidays.ToHashSet();
    }
}
