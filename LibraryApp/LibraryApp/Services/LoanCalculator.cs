namespace LibraryApp.Services
{
    /// <summary>Ödünç süresi ve gecikme cezası hesapları. Saf fonksiyonlardır; saat ve veritabanına bağımlı değildir.</summary>
    public static class LoanCalculator
    {
        /// <summary>Ödünç süresi (iş günü).</summary>
        public const int LoanBusinessDays = 15;

        /// <summary>Gecikilen her iş günü için ceza (₺).</summary>
        public const decimal DailyFine = 5m;

        /// <summary><paramref name="from"/> tarihinden itibaren <paramref name="businessDays"/> iş günü sonrasını döndürür (hafta sonları sayılmaz).</summary>
        public static DateTime DueDate(DateTime from, int businessDays = LoanBusinessDays)
        {
            var due = from;
            while (businessDays > 0)
            {
                due = due.AddDays(1);
                if (IsBusinessDay(due)) businessDays--;
            }
            return due;
        }

        /// <summary>
        /// Son teslim gününden (hariç) iade gününe (dahil) kadar geçen iş günü sayısı; zamanında iadede 0.
        /// Saat dikkate alınmaz: son teslim günü içinde yapılan iade zamanında sayılır.
        /// </summary>
        public static int LateBusinessDays(DateTime dueDate, DateTime returnDate)
        {
            var late = 0;
            for (var d = dueDate.Date.AddDays(1); d <= returnDate.Date; d = d.AddDays(1))
            {
                if (IsBusinessDay(d)) late++;
            }
            return late;
        }

        public static decimal Fine(int lateBusinessDays) => lateBusinessDays * DailyFine;

        private static bool IsBusinessDay(DateTime d) => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
    }
}
