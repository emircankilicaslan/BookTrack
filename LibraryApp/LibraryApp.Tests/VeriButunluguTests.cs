using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LibraryApp.Models;

namespace LibraryApp.Tests;

/// <summary>Issue #4: ISBN tekrarı, alan uzunlukları, geçmişi olan kitabın silinmesi ve TCKN algoritması.</summary>
public class VeriButunluguTests : IClassFixture<BooksApiTests.Fabrika>
{
    private readonly HttpClient _istemci;

    public VeriButunluguTests(BooksApiTests.Fabrika fabrika) => _istemci = TestAuth.Admin(fabrika);

    private static string YeniIsbn() => ("978-" + Guid.NewGuid().ToString("N"))[..20];

    private static object Kitap(string? isbn = null, string baslik = "Kitap") =>
        new { title = baslik, isbn = isbn ?? YeniIsbn(), publicationYear = 2000, price = 10 };

    private async Task<int> KitapEkle(string? isbn = null)
    {
        var r = await _istemci.PostAsJsonAsync("/api/books", Kitap(isbn));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private static readonly object Okuyucu = new { name = "Ayşe Yılmaz", tckn = "10000000146" };

    // ---- ISBN tekrarı ----

    [Fact]
    public async Task AyniIsbnIkinciKezEklenemez_409()
    {
        var isbn = YeniIsbn();
        await KitapEkle(isbn);
        var r = await _istemci.PostAsJsonAsync("/api/books", Kitap(isbn));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    [Fact]
    public async Task IsbnBosluklarDikkateAlinmadanKarsilastirilir()
    {
        var isbn = YeniIsbn()[..16];
        await KitapEkle(isbn);
        var r = await _istemci.PostAsJsonAsync("/api/books", Kitap("  " + isbn + " "));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    [Fact]
    public async Task GuncellemedeBaskaKitabinIsbnsiKullanilamaz_KendiIsbnsiKorunabilir()
    {
        var isbnA = YeniIsbn();
        var a = await KitapEkle(isbnA);
        var b = await KitapEkle();

        var cakisan = await _istemci.PutAsJsonAsync($"/api/books/{b}", new { id = b, title = "B", isbn = isbnA, publicationYear = 2000, price = 10 });
        Assert.Equal(HttpStatusCode.Conflict, cakisan.StatusCode);

        var kendi = await _istemci.PutAsJsonAsync($"/api/books/{a}", new { id = a, title = "Yeni başlık", isbn = isbnA, publicationYear = 2000, price = 10 });
        Assert.Equal(HttpStatusCode.NoContent, kendi.StatusCode);
    }

    // ---- Alan uzunlukları ----

    [Fact]
    public async Task BaslikVeIsbnUzunlukSiniriAsilamaz()
    {
        var uzunBaslik = await _istemci.PostAsJsonAsync("/api/books", Kitap(baslik: new string('a', 301)));
        Assert.Equal(HttpStatusCode.BadRequest, uzunBaslik.StatusCode);

        var uzunIsbn = await _istemci.PostAsJsonAsync("/api/books", Kitap(isbn: new string('9', 21)));
        Assert.Equal(HttpStatusCode.BadRequest, uzunIsbn.StatusCode);

        var sinirda = await _istemci.PostAsJsonAsync("/api/books", Kitap(isbn: new string('8', 20), baslik: new string('a', 300)));
        Assert.Equal(HttpStatusCode.Created, sinirda.StatusCode);
    }

    // ---- Geçmişi olan kitap silinemez ----

    [Fact]
    public async Task GecmisiOlanKitapSilinemez_GecmisKorunur()
    {
        var id = await KitapEkle();
        await _istemci.PostAsJsonAsync($"/api/books/checkout/{id}", Okuyucu);
        await _istemci.PostAsync($"/api/books/checkin/{id}", null); // iade edildi, kitap rafta ama geçmişi var

        var sil = await _istemci.DeleteAsync($"/api/books/{id}");
        Assert.Equal(HttpStatusCode.Conflict, sil.StatusCode);

        var gecmis = await _istemci.GetAsync($"/api/books/{id}/history");
        Assert.Equal(HttpStatusCode.OK, gecmis.StatusCode);
        Assert.Equal(1, (await gecmis.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
    }

    [Fact]
    public async Task GecmisiOlmayanKitapSilinebilir()
    {
        var id = await KitapEkle();
        Assert.Equal(HttpStatusCode.NoContent, (await _istemci.DeleteAsync($"/api/books/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _istemci.GetAsync($"/api/books/{id}")).StatusCode);
    }

    // ---- T.C. kimlik numarası ----

    [Theory]
    [InlineData("10000000146")]
    [InlineData("10000000214")]
    [InlineData("10000000382")]
    public void GecerliTckn_Kabul(string tckn) => Assert.True(TcknDogrulayici.Gecerli(tckn));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1000000014")]      // 10 hane
    [InlineData("100000001466")]    // 12 hane
    [InlineData("1000000014a")]     // rakam değil
    [InlineData("11111111111")]     // 10. hane tutmuyor
    [InlineData("12345678901")]     // 10. hane tutmuyor
    [InlineData("10000000147")]     // 11. hane tutmuyor
    [InlineData("00000000000")]     // ilk hane 0 (diğer kurallar tutsa bile)
    public void GecersizTckn_Red(string? tckn) => Assert.False(TcknDogrulayici.Gecerli(tckn));

    [Fact]
    public async Task OduncVer_AlgoritmayaUymayanTckn_400Doner()
    {
        var id = await KitapEkle();
        var r = await _istemci.PostAsJsonAsync($"/api/books/checkout/{id}", new { name = "Ayşe", tckn = "11111111111" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }
}
