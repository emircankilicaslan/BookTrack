using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LibraryApp.Data;
using LibraryApp.Models;
using LibraryApp.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Tests;

/// <summary>Uçtan uca API testleri: gerçek HTTP hattı, veritabanı yerine bellek içi sağlayıcı.</summary>
public class BooksApiTests : IClassFixture<BooksApiTests.Fabrika>
{
    public class Fabrika : WebApplicationFactory<Program>
    {
        private readonly string _ad = $"kutuphane-{Guid.NewGuid():N}";

        /// <summary>Testte tatil sayılan gün: yedi gün önceki ilk iş günü.</summary>
        public static readonly DateTime Tatil = SonIsGunu(DateTime.Today.AddDays(-7));
        public static readonly IReadOnlySet<DateOnly> TatilKumesi = new HashSet<DateOnly> { DateOnly.FromDateTime(Tatil) };

        private static DateTime SonIsGunu(DateTime d)
        {
            while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(-1);
            return d;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Library:Holidays:0", Tatil.ToString("yyyy-MM-dd"));
            builder.ConfigureServices(services =>
            {
                var eski = services.Where(d => d.ServiceType == typeof(DbContextOptions<LibraryContext>)).ToList();
                foreach (var d in eski) services.Remove(d);
                services.AddDbContext<LibraryContext>(o => o.UseInMemoryDatabase(_ad));
            });
        }
    }

    private readonly Fabrika _fabrika;
    private readonly HttpClient _istemci;

    public BooksApiTests(Fabrika fabrika)
    {
        _fabrika = fabrika;
        _istemci = fabrika.CreateClient();
    }

    private static object Kitap(string baslik = "Suç ve Ceza", int yil = 1866, decimal fiyat = 120m) =>
        new { title = baslik, isbn = "978-975-07-0000-0", publicationYear = yil, price = fiyat };

    private async Task<int> KitapEkle(object? kitap = null)
    {
        var r = await _istemci.PostAsJsonAsync("/api/books", kitap ?? Kitap());
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private static readonly object Okuyucu = new { name = "Ayşe Yılmaz", phoneNumber = "05550000000", tckn = "12345678901" };

    [Fact]
    public async Task KitapEkle_VeListele()
    {
        var id = await KitapEkle(Kitap("Tutunamayanlar", 1971, 150m));
        var liste = await _istemci.GetFromJsonAsync<List<JsonElement>>("/api/books");
        Assert.Contains(liste!, k => k.GetProperty("id").GetInt32() == id && k.GetProperty("title").GetString() == "Tutunamayanlar");
    }

    [Theory]
    [InlineData("", 2000, 10)]      // başlık boş
    [InlineData("Kitap", 500, 10)]  // yayın yılı aralık dışı
    [InlineData("Kitap", 2000, -1)] // negatif fiyat
    public async Task GecersizKitap_400Doner(string baslik, int yil, decimal fiyat)
    {
        var r = await _istemci.PostAsJsonAsync("/api/books", Kitap(baslik, yil, fiyat));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task KitapEklerken_OduncAlanlariIstemciden_Alinmaz()
    {
        var r = await _istemci.PostAsJsonAsync("/api/books", new
        {
            title = "Hileli", isbn = "1", publicationYear = 2000, price = 1, isCheckedOut = true, checkedOutBy = "Biri", dueDate = DateTime.Now
        });
        var k = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(k.GetProperty("isCheckedOut").GetBoolean());
        Assert.Equal(JsonValueKind.Null, k.GetProperty("checkedOutBy").ValueKind);
    }

    [Fact]
    public async Task OduncVer_IkinciKezReddedilir_VeIadeSonrasiTekrarVerilebilir()
    {
        var id = await KitapEkle();

        Assert.Equal(HttpStatusCode.NoContent, (await _istemci.PostAsJsonAsync($"/api/books/checkout/{id}", Okuyucu)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _istemci.PostAsJsonAsync($"/api/books/checkout/{id}", Okuyucu)).StatusCode);

        var iade = await _istemci.PostAsync($"/api/books/checkin/{id}", null);
        Assert.Equal(HttpStatusCode.OK, iade.StatusCode);
        var sonuc = await iade.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, sonuc.GetProperty("lateDays").GetInt32());
        Assert.Equal(0m, sonuc.GetProperty("fine").GetDecimal());
        Assert.Contains("Gecikme cezası", sonuc.GetProperty("message").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await _istemci.PostAsync($"/api/books/checkin/{id}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _istemci.PostAsJsonAsync($"/api/books/checkout/{id}", Okuyucu)).StatusCode);
    }

    [Theory]
    [InlineData("", "12345678901")]            // ad yok
    [InlineData("Ayşe", "123")]                // TCKN kısa
    [InlineData("Ayşe", "1234567890a")]        // TCKN rakam değil
    public async Task OduncVer_GecersizOkuyucu_400Doner(string ad, string tckn)
    {
        var id = await KitapEkle();
        var r = await _istemci.PostAsJsonAsync($"/api/books/checkout/{id}", new { name = ad, tckn });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task OduncteKiKitap_Silinemez()
    {
        var id = await KitapEkle();
        await _istemci.PostAsJsonAsync($"/api/books/checkout/{id}", Okuyucu);

        Assert.Equal(HttpStatusCode.Conflict, (await _istemci.DeleteAsync($"/api/books/{id}")).StatusCode);

        await _istemci.PostAsync($"/api/books/checkin/{id}", null);
        Assert.Equal(HttpStatusCode.NoContent, (await _istemci.DeleteAsync($"/api/books/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _istemci.GetAsync($"/api/books/{id}")).StatusCode);
    }

    [Fact]
    public async Task GecikenIade_IsGunuBasina5LiraCezaUygular()
    {
        var id = await KitapEkle();
        var son = DateTime.Now.AddDays(-14);

        using (var kapsam = _fabrika.Services.CreateScope())
        {
            var db = kapsam.ServiceProvider.GetRequiredService<LibraryContext>();
            var kitap = await db.Books.FindAsync(id);
            kitap!.IsCheckedOut = true;
            kitap.CheckedOutBy = "Geç Kalan";
            kitap.DueDate = son;
            db.CheckOuts.Add(new CheckOut { BookId = id, Name = "Geç Kalan", TCKN = "12345678901", CheckOutDate = son.AddDays(-21), DueDate = son });
            await db.SaveChangesAsync();
        }

        var beklenenGun = LoanCalculator.LateBusinessDays(son, DateTime.Now, Fabrika.TatilKumesi);
        var r = await _istemci.PostAsync($"/api/books/checkin/{id}", null);
        var sonuc = await r.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(beklenenGun >= 9);
        Assert.Equal(beklenenGun, sonuc.GetProperty("lateDays").GetInt32());
        Assert.Equal(beklenenGun * 5m, sonuc.GetProperty("fine").GetDecimal());
    }

    [Fact]
    public async Task CezaOnizleme_YapilandirilmisResmiTatiliSaymaz()
    {
        var id = await KitapEkle();
        var son = DateTime.Today.AddDays(-14).AddHours(10);

        using (var kapsam = _fabrika.Services.CreateScope())
        {
            var db = kapsam.ServiceProvider.GetRequiredService<LibraryContext>();
            var kitap = await db.Books.FindAsync(id);
            kitap!.IsCheckedOut = true;
            kitap.CheckedOutBy = "Geç Kalan";
            kitap.DueDate = son;
            await db.SaveChangesAsync();
        }

        var tatilsiz = LoanCalculator.LateBusinessDays(son, DateTime.Now);
        var tatilli = LoanCalculator.LateBusinessDays(son, DateTime.Now, Fabrika.TatilKumesi);
        Assert.Equal(tatilsiz - 1, tatilli); // yapılandırılan tatil, ceza gününü bir azaltır

        var yanit = await _istemci.GetFromJsonAsync<JsonElement>($"/api/books/{id}/fine-preview");
        Assert.Equal(tatilli, yanit.GetProperty("lateDays").GetInt32());
        Assert.Equal(tatilli * 5m, yanit.GetProperty("fine").GetDecimal());
    }

    [Fact]
    public async Task CezaOnizleme_OduncteOlmayanKitapIcin400_OlmayanIcin404()
    {
        var id = await KitapEkle();
        Assert.Equal(HttpStatusCode.BadRequest, (await _istemci.GetAsync($"/api/books/{id}/fine-preview")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _istemci.GetAsync("/api/books/99999/fine-preview")).StatusCode);
    }

    [Fact]
    public async Task Gecmis_KisiselVeriIcermez_VeSonOdunculerEnUsttedir()
    {
        var id = await KitapEkle();
        await _istemci.PostAsJsonAsync($"/api/books/checkout/{id}", Okuyucu);
        await _istemci.PostAsync($"/api/books/checkin/{id}", null);
        await Task.Delay(20); // sıralama için farklı zaman damgası
        await _istemci.PostAsJsonAsync($"/api/books/checkout/{id}", new { name = "Mehmet Kaya", tckn = "98765432109" });

        var ham = await _istemci.GetStringAsync($"/api/books/{id}/history");
        var liste = JsonSerializer.Deserialize<List<JsonElement>>(ham)!;

        Assert.Equal(2, liste.Count);
        Assert.Equal("Mehmet Kaya", liste[0].GetProperty("name").GetString());
        Assert.DoesNotContain("12345678901", ham);
        Assert.DoesNotContain("98765432109", ham);
        Assert.DoesNotContain("tckn", ham, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phone", ham, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, (await _istemci.GetAsync("/api/books/99999/history")).StatusCode);
    }

    [Fact]
    public async Task WebArayuzu_SunulurVeIcerikGuvenlikPolitikasiVardir()
    {
        var r = await _istemci.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var csp = string.Join(";", r.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'self'", csp);
        Assert.DoesNotContain("unsafe-inline", csp);

        var html = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain(" style=", html);
        Assert.DoesNotContain(" onclick=", html);

        Assert.Equal(HttpStatusCode.OK, (await _istemci.GetAsync("/app.js")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _istemci.GetAsync("/app.css")).StatusCode);
    }
}
