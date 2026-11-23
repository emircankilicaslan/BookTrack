using System.IO.Compression;
using System.Text;
using LibraryApp.Services;

namespace LibraryApp.Tests;

/// <summary>CSV ve Excel üreticisinin saf (veritabanısız) testleri.</summary>
public class TableExportTests
{
    private static string CsvMetni(IReadOnlyList<string> baslik, params object?[][] satirlar)
    {
        var bayt = TableExport.Csv(baslik, satirlar.Select(s => (IReadOnlyList<object?>)s));
        return Encoding.UTF8.GetString(bayt, 3, bayt.Length - 3);
    }

    private static string Oku(byte[] xlsx, string ad)
    {
        using var zip = new ZipArchive(new MemoryStream(xlsx));
        using var r = new StreamReader(zip.GetEntry(ad)!.Open());
        return r.ReadToEnd();
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(1, "B")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(27, "AB")]
    [InlineData(51, "AZ")]
    [InlineData(52, "BA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void HucreBasvurusu_SutunHarfleri(int sutun, string beklenen)
    {
        Assert.Equal(beklenen + "7", TableExport.Ref(sutun, 7));
    }

    [Fact]
    public void Format_DegerTurleri()
    {
        Assert.Equal("", TableExport.Format(null));
        Assert.Equal("02.03.2026", TableExport.Format(new DateTime(2026, 3, 2)));
        Assert.Equal("02.03.2026 14:05", TableExport.Format(new DateTime(2026, 3, 2, 14, 5, 0)));
        Assert.Equal("02.03.2026", TableExport.Format(new DateOnly(2026, 3, 2)));
        Assert.Equal("12,50", TableExport.Format(12.5m));
        Assert.Equal("0,00", TableExport.Format(0m));
        Assert.Equal("Evet", TableExport.Format(true));
        Assert.Equal("Hayır", TableExport.Format(false));
        Assert.Equal("42", TableExport.Format(42));
        Assert.Equal("metin", TableExport.Format("metin"));
    }

    [Fact]
    public void Csv_BomBasliklarVeSatirlarCRLFIleAyrilir()
    {
        var bayt = TableExport.Csv(["A", "B"], [new object?[] { "x", 1 }, new object?[] { "y", 2 }]);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bayt[..3]);
        Assert.Equal("A;B\r\nx;1\r\ny;2\r\n", Encoding.UTF8.GetString(bayt, 3, bayt.Length - 3));
    }

    [Theory]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("a\"b", "\"a\"\"b\"")]
    [InlineData("a\nb", "\"a\nb\"")]
    [InlineData("düz", "düz")]
    public void Csv_OzelKarakterlerTirnaklanir(string girdi, string beklenen)
    {
        Assert.Equal($"H\r\n{beklenen}\r\n", CsvMetni(["H"], [girdi]));
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+90", "'+90")]
    [InlineData("-5", "'-5")]
    [InlineData("@x", "'@x")]
    [InlineData("normal", "normal")]
    public void Csv_FormulBaslatanMetinlerEtkisizlestirilir(string girdi, string beklenen)
    {
        Assert.Equal($"H\r\n{beklenen}\r\n", CsvMetni(["H"], [girdi]));
    }

    [Fact]
    public void Csv_SayilarFormulSayilmaz()
    {
        // Eksi sayı metin değil sayıdır; kesme işareti konmaz.
        Assert.Equal("H\r\n-5\r\n", CsvMetni(["H"], [-5]));
    }

    [Fact]
    public void Csv_BosSatirListesiYalnizcaBaslikVerir()
    {
        Assert.Equal("A;B\r\n", CsvMetni(["A", "B"]));
    }

    [Fact]
    public void Xlsx_BasliklarKalinSatirlarVeriTurlerineGore()
    {
        var xlsx = TableExport.Xlsx("Test", ["Ad", "Adet", "Tutar"], [new object?[] { "Kalem", 3, 12.5m }]);
        var sayfa = Oku(xlsx, "xl/worksheets/sheet1.xml");
        Assert.Contains("<c r=\"A1\" t=\"inlineStr\" s=\"1\"><is><t>Ad</t></is></c>", sayfa);
        Assert.Contains("<c r=\"B2\"><v>3</v></c>", sayfa);
        Assert.Contains("<c r=\"C2\"><v>12.5</v></c>", sayfa);
        Assert.Contains("Kalem", sayfa);
    }

    [Fact]
    public void Xlsx_BosHucrelerAtlanir()
    {
        var sayfa = Oku(TableExport.Xlsx("T", ["A", "B"], [new object?[] { null, "x" }]), "xl/worksheets/sheet1.xml");
        Assert.DoesNotContain("r=\"A2\"", sayfa);
        Assert.Contains("r=\"B2\"", sayfa);
    }

    [Theory]
    [InlineData("Ödünçler [2026]: a/b*c?", "Ödünçler 2026 abc")]
    [InlineData("", "Sayfa1")]
    [InlineData("12345678901234567890123456789012345", "1234567890123456789012345678901")]
    public void Xlsx_SayfaAdiTemizlenir(string girdi, string beklenen)
    {
        var calismaKitabi = Oku(TableExport.Xlsx(girdi, ["A"], []), "xl/workbook.xml");
        Assert.Contains($"sheet name=\"{beklenen}\"", calismaKitabi);
    }

    [Fact]
    public void Xlsx_XmldeGecersizKarakterleriAtarVeKacislar()
    {
        var sayfa = Oku(TableExport.Xlsx("T", ["A"], [new object?[] { "a\u0001b<c>&\"d\"" }]), "xl/worksheets/sheet1.xml");
        Assert.False(sayfa.Contains((char)1)); // kontrol karakteri atılmış olmalı
        Assert.Contains("ab&lt;c&gt;&amp;&quot;d&quot;", sayfa);
        System.Xml.Linq.XDocument.Parse(sayfa);
    }

    [Fact]
    public void Xlsx_BesGerekliParcaVardir()
    {
        using var zip = new ZipArchive(new MemoryStream(TableExport.Xlsx("T", ["A"], [])));
        var adlar = zip.Entries.Select(e => e.FullName).OrderBy(x => x).ToList();
        var beklenen = new[] { "[Content_Types].xml", "_rels/.rels", "xl/_rels/workbook.xml.rels", "xl/styles.xml", "xl/workbook.xml", "xl/worksheets/sheet1.xml" }.OrderBy(x => x).ToList();
        Assert.Equal(beklenen, adlar);
    }
}
