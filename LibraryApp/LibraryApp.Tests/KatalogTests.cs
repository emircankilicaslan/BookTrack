using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LibraryApp.Tests;

/// <summary>Kitap listesi: arama, süzgeç, sıralama, sayfalama ve katalog alanları (yazar, kategori).</summary>
public class KatalogTests : ZamanliTestTemeli
{
    public KatalogTests(ZamanliFabrika fabrika) : base(fabrika) { }

    private async Task<List<JsonElement>> Liste(string sorgu)
    {
        var r = await Yonetici.GetAsync("/api/books?" + sorgu);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await Json(r)).EnumerateArray().ToList();
    }

    private static List<string> Basliklar(IEnumerable<JsonElement> kitaplar) =>
        kitaplar.Select(k => k.GetProperty("title").GetString()!).ToList();

    [Theory]
    [InlineData("baslik")]
    [InlineData("BASLIK")]
    [InlineData("aslik")]
    public async Task Arama_BaslikteBuyukKucukHarfDuyarsizdir(string aranan)
    {
        var e = Etiket();
        await KitapEkle(Kitap($"Baslik {e}"));
        var sonuc = await Liste($"q={Uri.EscapeDataString(aranan)}");
        Assert.Contains($"Baslik {e}", Basliklar(sonuc));
    }

    [Fact]
    public async Task Arama_YazarVeIsbndeAraNir()
    {
        var e = Etiket();
        var isbn = ("ISBN" + e + Guid.NewGuid().ToString("N"))[..20];
        await KitapEkle(Kitap("Yazarlı kitap " + e, yazar: "Orhan " + e));
        await KitapEkle(Kitap("Isbnli kitap " + e, isbn: isbn));

        Assert.Equal(["Yazarlı kitap " + e], Basliklar(await Liste($"q=orhan%20{e}")));
        Assert.Equal(["Isbnli kitap " + e], Basliklar(await Liste($"q={isbn}")));
    }

    [Fact]
    public async Task Arama_BulunamayinceBosListeDoner()
    {
        Assert.Empty(await Liste("q=" + Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public async Task Kategori_SuzgeciVeKategoriListesi()
    {
        var e = Etiket();
        await KitapEkle(Kitap("Roman " + e, kategori: "Roman" + e));
        await KitapEkle(Kitap("Tarih " + e, kategori: "Tarih" + e));

        Assert.Equal(["Roman " + e], Basliklar(await Liste("category=Roman" + e)));
        var kategoriler = (await Json(await Yonetici.GetAsync("/api/books/categories"))).EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Contains("Roman" + e, kategoriler);
        Assert.Contains("Tarih" + e, kategoriler);
    }

    [Fact]
    public async Task Musaitlik_SuzgeciRaftaVeOdunctekileriAyirir()
    {
        var e = Etiket();
        var rafta = await KitapEkle(Kitap("Rafta " + e));
        var oduncte = await KitapEkle(Kitap("Odunc " + e));
        await Odunc(oduncte);

        Assert.Equal(["Rafta " + e], Basliklar(await Liste($"q={e}&available=true")));
        Assert.Equal(["Odunc " + e], Basliklar(await Liste($"q={e}&available=false")));
        Assert.Equal(2, (await Liste($"q={e}")).Count);
        Assert.NotEqual(rafta, oduncte);
    }

    [Fact]
    public async Task Geciken_SuzgeciYalnizcaSuresiGecenleriGetirir()
    {
        var e = Etiket();
        var id = await KitapEkle(Kitap("Gecikecek " + e));
        await Odunc(id); // son teslim 24 Mart (20 Mart tatil)

        Assert.Empty(await Liste($"q={e}&overdue=true"));
        SaatAyarla(new DateTime(2026, 3, 24, 9, 0, 0));
        Assert.Empty(await Liste($"q={e}&overdue=true")); // son teslim günü içinde hâlâ zamanında
        SaatAyarla(new DateTime(2026, 3, 25, 9, 0, 0));
        Assert.Equal(["Gecikecek " + e], Basliklar(await Liste($"q={e}&overdue=true")));
    }

    [Theory]
    [InlineData("year", false, new[] { "A", "B", "C" })]
    [InlineData("year", true, new[] { "C", "B", "A" })]
    [InlineData("price", false, new[] { "C", "A", "B" })]
    [InlineData("price", true, new[] { "B", "A", "C" })]
    [InlineData("title", false, new[] { "A", "B", "C" })]
    [InlineData("title", true, new[] { "C", "B", "A" })]
    [InlineData("author", false, new[] { "B", "C", "A" })]
    [InlineData("bilinmeyen", false, new[] { "A", "B", "C" })]
    public async Task Siralama(string alan, bool azalan, string[] beklenen)
    {
        var e = Etiket();
        // A: yıl 1990, fiyat 50, yazar Zeki; B: yıl 2000, fiyat 90, yazar Ali; C: yıl 2010, fiyat 10, yazar Mert
        await KitapEkle(Kitap($"A {e}", "Zeki", null, 1990, 50m));
        await KitapEkle(Kitap($"B {e}", "Ali", null, 2000, 90m));
        await KitapEkle(Kitap($"C {e}", "Mert", null, 2010, 10m));

        var sonuc = Basliklar(await Liste($"q={e}&sort={alan}&desc={azalan.ToString().ToLowerInvariant()}"));
        Assert.Equal(beklenen.Select(x => $"{x} {e}").ToList(), sonuc);
    }

    [Fact]
    public async Task Sayfalama_ToplamSayiBaslikta_SayfalarBolunur()
    {
        var e = Etiket();
        for (var i = 1; i <= 5; i++) await KitapEkle(Kitap($"Sayfa {e} {i}"));

        var s1 = await Yonetici.GetAsync($"/api/books?q={e}&page=1&pageSize=2");
        var s3 = await Yonetici.GetAsync($"/api/books?q={e}&page=3&pageSize=2");
        var s4 = await Yonetici.GetAsync($"/api/books?q={e}&page=4&pageSize=2");

        Assert.Equal(5, Toplam(s1));
        Assert.Equal(2, (await Json(s1)).GetArrayLength());
        Assert.Equal(1, (await Json(s3)).GetArrayLength());
        Assert.Equal(0, (await Json(s4)).GetArrayLength());
        Assert.Equal(5, Toplam(s4));
    }

    [Theory]
    [InlineData("page=0&pageSize=2")]
    [InlineData("page=-3&pageSize=2")]
    public async Task Sayfalama_GecersizSayfaBireDuzeltilir(string sorgu)
    {
        var e = Etiket();
        for (var i = 1; i <= 3; i++) await KitapEkle(Kitap($"Duzelt {e} {i}"));
        Assert.Equal(2, (await Liste($"q={e}&{sorgu}")).Count);
    }

    [Fact]
    public async Task Sayfalama_SayfaBoyutuUstSinirlanir()
    {
        var e = Etiket();
        await KitapEkle(Kitap("Sinir " + e));
        var r = await Yonetici.GetAsync($"/api/books?q={e}&page=1&pageSize=100000");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var r2 = await Yonetici.GetAsync($"/api/books?q={e}&page=1&pageSize=0");
        Assert.Equal(1, (await Json(r2)).GetArrayLength());
    }

    [Fact]
    public async Task Sayfalama_OlmadanTumSonucDizisiDoner()
    {
        var e = Etiket();
        for (var i = 1; i <= 25; i++) await KitapEkle(Kitap($"Hepsi {e} {i:00}"));
        var r = await Yonetici.GetAsync($"/api/books?q={e}");
        Assert.Equal(25, (await Json(r)).GetArrayLength());
        Assert.Equal(25, Toplam(r));
    }

    [Fact]
    public async Task YazarVeKategori_KirpilirBosOlanNullOlur()
    {
        var e = Etiket();
        var id = await KitapEkle(Kitap("Kirp " + e, yazar: "  Sait Faik  ", kategori: "   "));
        var k = await Json(await Yonetici.GetAsync($"/api/books/{id}"));
        Assert.Equal("Sait Faik", k.GetProperty("author").GetString());
        Assert.Equal(JsonValueKind.Null, k.GetProperty("category").ValueKind);
    }

    [Theory]
    [InlineData(201, 10)]
    [InlineData(10, 101)]
    public async Task YazarVeKategori_UzunlukSiniri(int yazarUzunluk, int kategoriUzunluk)
    {
        var r = await Yonetici.PostAsJsonAsync("/api/books",
            Kitap("Uzun " + Etiket(), new string('y', yazarUzunluk), new string('k', kategoriUzunluk)));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Guncelleme_YazarVeKategoriyiDegistirir()
    {
        var e = Etiket();
        var id = await KitapEkle(Kitap("Guncel " + e, "Eski Yazar", "Eski"));
        var r = await Yonetici.PutAsJsonAsync($"/api/books/{id}",
            new { id, title = "Guncel " + e, isbn = YeniIsbn(), author = "Yeni Yazar", category = "Yeni", publicationYear = 2001, price = 5m });
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        var k = await Json(await Yonetici.GetAsync($"/api/books/{id}"));
        Assert.Equal("Yeni Yazar", k.GetProperty("author").GetString());
        Assert.Equal("Yeni", k.GetProperty("category").GetString());
    }

    [Fact]
    public async Task Gorevli_KatalogaErisirAncakKitapSilemez()
    {
        var gorevli = await GorevliAc();
        var e = Etiket();
        var id = await KitapEkle(gorevli, Kitap("Gorevli " + e));
        Assert.Equal(HttpStatusCode.OK, (await gorevli.GetAsync("/api/books?q=" + e)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await gorevli.DeleteAsync($"/api/books/{id}")).StatusCode);
    }

    [Fact]
    public async Task OturumsuzListeIstegiReddedilir()
    {
        var anonim = Fabrika.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonim.GetAsync("/api/books")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonim.GetAsync("/api/books/categories")).StatusCode);
    }
}
