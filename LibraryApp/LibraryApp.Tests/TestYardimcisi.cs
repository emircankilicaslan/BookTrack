using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LibraryApp.Tests;

/// <summary>Zamanlı fabrika testleri için ortak kurulum: yönetici istemcisi, kitap ve okuyucu üretimi.</summary>
public abstract class ZamanliTestTemeli : IClassFixture<ZamanliFabrika>
{
    /// <summary>2026-03-02 Pazartesi 10:00. appsettings'teki resmî tatiller: 20-22 Mart.</summary>
    protected static readonly DateTime Baslangic = new(2026, 3, 2, 10, 0, 0);

    protected readonly ZamanliFabrika Fabrika;
    protected readonly HttpClient Yonetici;

    protected ZamanliTestTemeli(ZamanliFabrika fabrika)
    {
        Fabrika = fabrika;
        fabrika.Saat.Ayarla(Baslangic);
        Yonetici = TestAuth.Admin(fabrika);
    }

    /// <summary>Saati ilerletir ve token ömrü saate bağlı olduğu için yönetici oturumunu yeniler.</summary>
    protected void SaatAyarla(DateTime zaman)
    {
        Fabrika.Saat.Ayarla(zaman);
        var token = TestAuth.LoginTokenAsync(Fabrika.CreateClient(), TestAuth.AdminUser, TestAuth.AdminPassword).GetAwaiter().GetResult();
        Yonetici.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    protected static string Etiket() => Guid.NewGuid().ToString("N")[..8];
    protected static string YeniIsbn() => ("978-" + Guid.NewGuid().ToString("N"))[..20];

    protected static readonly object Okuyucu = new { name = "Ayşe Yılmaz", phoneNumber = "05550000000", tckn = "10000000146" };

    protected static object Kitap(string baslik, string? yazar = null, string? kategori = null, int yil = 2000, decimal fiyat = 100m, string? isbn = null) =>
        new { title = baslik, isbn = isbn ?? YeniIsbn(), author = yazar, category = kategori, publicationYear = yil, price = fiyat };

    protected async Task<int> KitapEkle(HttpClient? istemci, object kitap)
    {
        var r = await (istemci ?? Yonetici).PostAsJsonAsync("/api/books", kitap);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    protected Task<int> KitapEkle(object kitap) => KitapEkle(null, kitap);

    protected async Task Odunc(int id) =>
        Assert.Equal(HttpStatusCode.NoContent, (await Yonetici.PostAsJsonAsync($"/api/books/checkout/{id}", Okuyucu)).StatusCode);

    protected async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    protected async Task<HttpClient> GorevliAc()
    {
        var ad = "gorevli" + Etiket();
        var r = await Yonetici.PostAsJsonAsync("/api/users", new { username = ad, password = "Gorevli-Parola-1", role = "Librarian" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return await TestAuth.LoginAsync(Fabrika.CreateClient(), ad, "Gorevli-Parola-1");
    }

    protected static int Toplam(HttpResponseMessage r) => int.Parse(r.Headers.GetValues("X-Total-Count").Single());
}
