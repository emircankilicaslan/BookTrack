using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace LibraryApp.Tests;

/// <summary>Özet, geciken ödünçler ve CSV/Excel dışa aktarma.</summary>
public class RaporTests : ZamanliTestTemeli
{
    public RaporTests(ZamanliFabrika fabrika) : base(fabrika) { }

    private async Task<JsonElement> Ozet() => await Json(await Yonetici.GetAsync("/api/reports/summary"));

    private async Task<(int id, string etiket)> GecikmisOdunc(string? baslik = null)
    {
        var e = Etiket();
        var id = await KitapEkle(Kitap((baslik ?? "Gecikmis") + " " + e, "Yazar " + e, "Kat" + e));
        await Odunc(id);
        return (id, e);
    }

    [Fact]
    public async Task Ozet_OduncVermeSayilariniArtirir()
    {
        var once = await Ozet();
        var id = await KitapEkle(Kitap("Ozet " + Etiket()));
        var sonra1 = await Ozet();
        await Odunc(id);
        var sonra2 = await Ozet();

        Assert.Equal(once.GetProperty("totalBooks").GetInt32() + 1, sonra1.GetProperty("totalBooks").GetInt32());
        Assert.Equal(sonra1.GetProperty("checkedOut").GetInt32() + 1, sonra2.GetProperty("checkedOut").GetInt32());
        Assert.Equal(sonra1.GetProperty("available").GetInt32() - 1, sonra2.GetProperty("available").GetInt32());
        Assert.Equal(sonra1.GetProperty("totalLoans").GetInt32() + 1, sonra2.GetProperty("totalLoans").GetInt32());
        Assert.Equal(sonra2.GetProperty("totalBooks").GetInt32(),
            sonra2.GetProperty("checkedOut").GetInt32() + sonra2.GetProperty("available").GetInt32());
    }

    [Fact]
    public async Task Ozet_GecikenVeYaklasanOduncleriAyirir()
    {
        var once = await Ozet();
        await GecikmisOdunc();
        SaatAyarla(new DateTime(2026, 3, 23, 9, 0, 0)); // son teslim 24 Mart: yarın, yaklaşan
        var yaklasan = await Ozet();
        Assert.True(yaklasan.GetProperty("dueSoon").GetInt32() > once.GetProperty("dueSoon").GetInt32());

        SaatAyarla(new DateTime(2026, 3, 25, 9, 0, 0));
        var gecikmis = await Ozet();
        Assert.True(gecikmis.GetProperty("overdue").GetInt32() > once.GetProperty("overdue").GetInt32());
    }

    [Fact]
    public async Task Ozet_CezaToplaminiVeAylikCezayiHesaplar()
    {
        var once = await Ozet();
        var (id, _) = await GecikmisOdunc();
        SaatAyarla(new DateTime(2026, 3, 26, 9, 0, 0)); // 2 iş günü gecikme
        await Yonetici.PostAsync($"/api/books/checkin/{id}", null);

        var sonra = await Ozet();
        Assert.Equal(once.GetProperty("finesTotal").GetDecimal() + 10m, sonra.GetProperty("finesTotal").GetDecimal());
        Assert.Equal(once.GetProperty("finesThisMonth").GetDecimal() + 10m, sonra.GetProperty("finesThisMonth").GetDecimal());
    }

    [Fact]
    public async Task Ozet_EnCokOduncAlinanKitaplariSiralar()
    {
        var e = Etiket();
        var sevilen = await KitapEkle(Kitap("Cok okunan " + e));
        for (var i = 0; i < 6; i++)
        {
            await Odunc(sevilen);
            await Yonetici.PostAsync($"/api/books/checkin/{sevilen}", null);
        }
        var populer = (await Ozet()).GetProperty("popular").EnumerateArray().ToList();
        Assert.True(populer.Count is > 0 and <= 5);
        Assert.Equal("Cok okunan " + e, populer[0].GetProperty("title").GetString());
        Assert.Equal(6, populer[0].GetProperty("loans").GetInt32());
        Assert.Equal(populer.Select(p => p.GetProperty("loans").GetInt32()).OrderByDescending(x => x), populer.Select(p => p.GetProperty("loans").GetInt32()));
    }

    [Fact]
    public async Task Ozet_AltiAylikTabloVeKategoriDagilimiVerir()
    {
        var e = Etiket();
        var id = await KitapEkle(Kitap("Aylik " + e, kategori: "Kat" + e));
        await Odunc(id);
        var o = await Ozet();

        var aylik = o.GetProperty("monthly").EnumerateArray().ToList();
        Assert.Equal(6, aylik.Count);
        Assert.Equal("2025-10", aylik[0].GetProperty("month").GetString());
        Assert.Equal("2026-03", aylik[5].GetProperty("month").GetString());
        Assert.True(aylik[5].GetProperty("loans").GetInt32() >= 1);

        var kategoriler = o.GetProperty("categories").EnumerateArray().ToList();
        Assert.Contains(kategoriler, k => k.GetProperty("category").GetString() == "Kat" + e && k.GetProperty("books").GetInt32() == 1);
    }

    [Fact]
    public async Task Gecikenler_SureGecenleriCezasiyla_KisiselVeriOlmadanListeler()
    {
        var (id, e) = await GecikmisOdunc("Listede");
        SaatAyarla(new DateTime(2026, 3, 26, 9, 0, 0));

        var r = await Yonetici.GetAsync("/api/reports/overdue");
        var metin = await r.Content.ReadAsStringAsync();
        var satir = JsonDocument.Parse(metin).RootElement.EnumerateArray().Single(x => x.GetProperty("bookId").GetInt32() == id);

        Assert.Equal("Listede " + e, satir.GetProperty("title").GetString());
        Assert.Equal("Ayşe Yılmaz", satir.GetProperty("borrower").GetString());
        Assert.Equal(2, satir.GetProperty("lateDays").GetInt32());
        Assert.Equal(10m, satir.GetProperty("fine").GetDecimal());
        Assert.DoesNotContain("10000000146", metin);
        Assert.DoesNotContain("05550000000", metin);
    }

    [Fact]
    public async Task Gecikenler_IadeEdilenVeSuresiGecmeyenListedeYoktur()
    {
        var (iade, _) = await GecikmisOdunc("Iade");
        var (zamaninda, _) = await GecikmisOdunc("Zamaninda");
        SaatAyarla(new DateTime(2026, 3, 25, 9, 0, 0));
        await Yonetici.PostAsync($"/api/books/checkin/{iade}", null);
        var (yeniOdunc, _) = await GecikmisOdunc("Yeni");

        var ids = (await Json(await Yonetici.GetAsync("/api/reports/overdue"))).EnumerateArray().Select(x => x.GetProperty("bookId").GetInt32()).ToList();
        Assert.DoesNotContain(iade, ids);
        Assert.DoesNotContain(yeniOdunc, ids);
        Assert.Contains(zamaninda, ids);
    }

    [Fact]
    public async Task Gecikenler_EnCokGecikenUsttedir()
    {
        var (eski, _) = await GecikmisOdunc("Eski");
        SaatAyarla(new DateTime(2026, 3, 10, 10, 0, 0));
        var (yeni, _) = await GecikmisOdunc("Yeni");
        SaatAyarla(new DateTime(2026, 4, 6, 9, 0, 0));
        var liste = (await Json(await Yonetici.GetAsync("/api/reports/overdue"))).EnumerateArray().Select(x => x.GetProperty("lateDays").GetInt32()).ToList();
        Assert.Equal(liste.OrderByDescending(x => x), liste);
        Assert.NotEqual(eski, yeni);
    }

    // ------------------------------------------------------------ dışa aktarma

    [Fact]
    public async Task Csv_KitaplarBomluVeBasliklidir()
    {
        var e = Etiket();
        await KitapEkle(Kitap("Disa " + e, "Yazar; Noktalı", "Kat"));
        var r = await Yonetici.GetAsync("/api/reports/export/books?format=csv");
        var bayt = await r.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.StartsWith("text/csv", r.Content.Headers.ContentType!.ToString());
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bayt[..3]);
        var metin = Encoding.UTF8.GetString(bayt, 3, bayt.Length - 3);
        Assert.StartsWith("No;Başlık;Yazar;Kategori;ISBN;Yayın yılı;Fiyat (₺);Durum;Ödünç alan;Son teslim", metin);
        Assert.Contains("Disa " + e, metin);
        Assert.Contains("\"Yazar; Noktalı\"", metin); // ayraç içeren hücre tırnaklanır
        Assert.Contains("filename=kitaplar-2026-03-02.csv", r.Content.Headers.ContentDisposition!.ToString());
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")")]
    [InlineData("+1+1")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1)")]
    public async Task Csv_FormulEnjeksiyonunaKarsiOnuneKesmeKonur(string kotu)
    {
        var e = Etiket();
        await KitapEkle(Kitap(kotu + e));
        var metin = Encoding.UTF8.GetString(await (await Yonetici.GetAsync("/api/reports/export/books?format=csv")).Content.ReadAsByteArrayAsync());
        Assert.Contains("'" + kotu.Replace("\"", "\"\"") + e, metin);
    }

    [Fact]
    public async Task Csv_OduncVeGecikenRaporlariKisiselVeriIcermez()
    {
        var (id, e) = await GecikmisOdunc("Rapor");
        SaatAyarla(new DateTime(2026, 3, 26, 9, 0, 0));
        foreach (var tur in new[] { "loans", "overdue" })
        {
            var metin = Encoding.UTF8.GetString(await (await Yonetici.GetAsync($"/api/reports/export/{tur}?format=csv")).Content.ReadAsByteArrayAsync());
            Assert.Contains("Rapor " + e, metin);
            Assert.Contains("Ayşe Yılmaz", metin);
            Assert.DoesNotContain("10000000146", metin);
            Assert.DoesNotContain("05550000000", metin);
        }
        Assert.True(id > 0);
    }

    [Fact]
    public async Task Csv_GecikenRaporundaGecikmeVeCezaYazar()
    {
        var (_, e) = await GecikmisOdunc("Ceza");
        SaatAyarla(new DateTime(2026, 3, 26, 9, 0, 0));
        var metin = Encoding.UTF8.GetString(await (await Yonetici.GetAsync("/api/reports/export/overdue?format=csv")).Content.ReadAsByteArrayAsync());
        Assert.Contains($"Ceza {e};Ayşe Yılmaz;24.03.2026 10:00;2;10,00;0", metin.Replace("\r", ""));
    }

    [Fact]
    public async Task Xlsx_GecerliZipVeIcerikVardir()
    {
        var e = Etiket();
        await KitapEkle(Kitap("Excel " + e + " & <özel>", "Yazar", "Kat", 1999, 12.5m));
        var r = await Yonetici.GetAsync("/api/reports/export/books?format=xlsx");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", r.Content.Headers.ContentType!.MediaType);

        using var zip = new ZipArchive(new MemoryStream(await r.Content.ReadAsByteArrayAsync()));
        var adlar = zip.Entries.Select(x => x.FullName).ToList();
        Assert.Contains("xl/workbook.xml", adlar);
        Assert.Contains("xl/worksheets/sheet1.xml", adlar);
        Assert.Contains("[Content_Types].xml", adlar);

        string Oku(string ad) { using var s = new StreamReader(zip.GetEntry(ad)!.Open()); return s.ReadToEnd(); }
        var sayfa = Oku("xl/worksheets/sheet1.xml");
        Assert.Contains("Excel " + e + " &amp; &lt;özel&gt;", sayfa); // XML kaçışı
        Assert.Contains("<v>1999</v>", sayfa);                      // sayılar sayı olarak yazılır
        Assert.Contains("<v>12.5</v>", sayfa);
        Assert.Contains("sheet name=\"kitaplar\"", Oku("xl/workbook.xml"));
        // iyi biçimli XML
        System.Xml.Linq.XDocument.Parse(sayfa);
        System.Xml.Linq.XDocument.Parse(Oku("xl/workbook.xml"));
    }

    [Theory]
    [InlineData("books")]
    [InlineData("loans")]
    [InlineData("overdue")]
    public async Task Xlsx_UcRaporTuruDeIndirilir(string tur)
    {
        var r = await Yonetici.GetAsync($"/api/reports/export/{tur}?format=xlsx");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.StartsWith("PK", Encoding.ASCII.GetString((await r.Content.ReadAsByteArrayAsync())[..2]));
    }

    [Fact]
    public async Task Disa_Aktarma_BilinmeyenRaporVeBicimReddedilir()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Yonetici.GetAsync("/api/reports/export/yok")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Yonetici.GetAsync("/api/reports/export/books?format=pdf")).StatusCode);
    }

    [Fact]
    public async Task Disa_Aktarma_VarsayilanBicimCsvdir()
    {
        var r = await Yonetici.GetAsync("/api/reports/export/books");
        Assert.StartsWith("text/csv", r.Content.Headers.ContentType!.ToString());
    }

    [Fact]
    public async Task Raporlar_GorevliyeAcikOturumsuzaKapalidir()
    {
        var gorevli = await GorevliAc();
        foreach (var yol in new[] { "summary", "overdue", "export/books" })
        {
            Assert.Equal(HttpStatusCode.OK, (await gorevli.GetAsync("/api/reports/" + yol)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Fabrika.CreateClient().GetAsync("/api/reports/" + yol)).StatusCode);
        }
    }
}
