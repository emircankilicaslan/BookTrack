using LibraryApp.Services;

namespace LibraryApp.Tests;

public class LoanCalculatorTests
{
    // 13 Kasım 2026 Cuma
    private static readonly DateTime Cuma = new(2026, 11, 13, 10, 0, 0);

    [Theory]
    [InlineData(1, "2026-11-16")]   // Cuma + 1 iş günü = Pazartesi
    [InlineData(5, "2026-11-20")]
    [InlineData(15, "2026-12-04")]  // 3 hafta sonrası Cuma
    public void DueDate_HaftaSonlariniAtlar(int isGunu, string beklenen)
    {
        Assert.Equal(DateTime.Parse(beklenen), LoanCalculator.DueDate(Cuma, isGunu).Date);
    }

    [Fact]
    public void DueDate_CumartesidenBaslayinca_IlkIsGunuPazartesidir()
    {
        var cumartesi = new DateTime(2026, 11, 14, 12, 0, 0);
        Assert.Equal(new DateTime(2026, 11, 16), LoanCalculator.DueDate(cumartesi, 1).Date);
    }

    [Fact]
    public void DueDate_VarsayilanSure15IsGunudur()
    {
        Assert.Equal(LoanCalculator.DueDate(Cuma, 15), LoanCalculator.DueDate(Cuma));
    }

    [Theory]
    [InlineData("2026-11-12", 0)]  // son tarihten önce
    [InlineData("2026-11-13", 0)]  // son teslim günü içinde (saat fark etmez)
    [InlineData("2026-11-14", 0)]  // Cumartesi: iş günü sayılmaz
    [InlineData("2026-11-15", 0)]  // Pazar
    [InlineData("2026-11-16", 1)]  // Pazartesi
    [InlineData("2026-11-20", 5)]
    [InlineData("2026-11-27", 10)] // iki hafta
    public void LateBusinessDays_YalnizcaIsGunleriniSayar(string iade, int beklenen)
    {
        Assert.Equal(beklenen, LoanCalculator.LateBusinessDays(Cuma, DateTime.Parse(iade).AddHours(23)));
    }

    [Fact]
    public void LateBusinessDays_SonTeslimGunuIcindeKiGecSaat_ZamanindaSayilir()
    {
        Assert.Equal(0, LoanCalculator.LateBusinessDays(Cuma, Cuma.AddHours(6)));
    }

    // ---- Resmi tatiller

    private static readonly IReadOnlySet<DateOnly> Tatiller = new HashSet<DateOnly> { new(2026, 11, 17), new(2026, 11, 18) }; // Salı, Çarşamba

    [Fact]
    public void DueDate_ResmiTatilleriAtlar()
    {
        // Cuma + 3 iş günü: Pzt 16 (1), Salı 17 ve Çarşamba 18 tatil, Per 19 (2), Cum 20 (3) → 20 Kasım
        Assert.Equal(new DateTime(2026, 11, 20), LoanCalculator.DueDate(Cuma, 3, Tatiller).Date);
        // Tatil yokken aynı hesap 18 Kasım'a düşer (Pzt 16, Salı 17, Çarşamba 18)
        Assert.Equal(new DateTime(2026, 11, 18), LoanCalculator.DueDate(Cuma, 3).Date);
    }

    [Fact]
    public void LateBusinessDays_ResmiTatilleriSaymaz()
    {
        var iade = new DateTime(2026, 11, 20, 12, 0, 0); // Cuma
        Assert.Equal(5, LoanCalculator.LateBusinessDays(Cuma, iade));               // Pzt-Cum
        Assert.Equal(3, LoanCalculator.LateBusinessDays(Cuma, iade, Tatiller));     // Salı ve Çarşamba sayılmaz
    }

    [Fact]
    public void IsBusinessDay_TatilGunuVeHaftaSonuFalseDoner()
    {
        Assert.False(LoanCalculator.IsBusinessDay(new DateTime(2026, 11, 17), Tatiller));
        Assert.False(LoanCalculator.IsBusinessDay(new DateTime(2026, 11, 14), Tatiller)); // Cumartesi
        Assert.True(LoanCalculator.IsBusinessDay(new DateTime(2026, 11, 19), Tatiller));
        Assert.True(LoanCalculator.IsBusinessDay(new DateTime(2026, 11, 17)));            // tatil listesi yokken
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 5)]
    [InlineData(7, 35)]
    public void Fine_GunBasina5Lira(int gun, int beklenen)
    {
        Assert.Equal(beklenen, LoanCalculator.Fine(gun));
    }
}
