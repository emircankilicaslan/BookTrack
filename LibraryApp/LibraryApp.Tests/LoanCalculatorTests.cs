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

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 5)]
    [InlineData(7, 35)]
    public void Fine_GunBasina5Lira(int gun, int beklenen)
    {
        Assert.Equal(beklenen, LoanCalculator.Fine(gun));
    }
}
