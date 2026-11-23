using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LibraryApp.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Tests;

/// <summary>
/// Arama, sıralama, sayfalama, rapor, dışa aktarma, uzatma ve denetim sorguları gerçek SQL Server'da çalışır:
/// bellek içi sağlayıcı SQL'e çevrilemeyen ifadeleri (ör. <c>ToLowerInvariant</c>) yakalayamaz.
/// Her çalıştırma kendi geçici veritabanını kurar ve siler. <c>BOOKTRACK_TEST_SQL</c> yoksa atlanır.
/// </summary>
public sealed class SqlKatalogRaporTests : IAsyncLifetime
{
    private readonly string _database = "BookTrack_K_" + Guid.NewGuid().ToString("N")[..10];
    private WebApplicationFactory<Program>? _fabrika;
    private HttpClient _istemci = default!;

    public async Task InitializeAsync()
    {
        if (!SqlConcurrencyTests.Available) return;
        var cs = new SqlConnectionStringBuilder(SqlConcurrencyTests.ServerConnection) { InitialCatalog = _database, MultipleActiveResultSets = true }.ConnectionString;
        _fabrika = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("ConnectionStrings:DefaultConnection", cs);
            b.UseSetting("Database:MigrateOnStartup", "true");
            TestAuth.Configure(b);
        });
        using (var kapsam = _fabrika.Services.CreateScope())
            await kapsam.ServiceProvider.GetRequiredService<LibraryContext>().Database.MigrateAsync();
        _istemci = await TestAuth.AdminAsync(_fabrika);
    }

    public async Task DisposeAsync()
    {
        _fabrika?.Dispose();
        if (!SqlConcurrencyTests.Available) return;
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(SqlConcurrencyTests.ServerConnection);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"IF DB_ID(N'{_database}') IS NOT NULL BEGIN ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]; END";
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> Ekle(string baslik, string? yazar, string? kategori, int yil, decimal fiyat)
    {
        var r = await _istemci.PostAsJsonAsync("/api/books", new
        {
            title = baslik, author = yazar, category = kategori, isbn = ("978-" + Guid.NewGuid().ToString("N"))[..20], publicationYear = yil, price = fiyat,
        });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private async Task<(List<string> basliklar, int toplam)> Liste(string sorgu)
    {
        var r = await _istemci.GetAsync("/api/books?" + sorgu);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var dizi = (await r.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Select(k => k.GetProperty("title").GetString()!).ToList();
        return (dizi, int.Parse(r.Headers.GetValues("X-Total-Count").Single()));
    }

    private async Task Hazirla()
    {
        await Ekle("Suç ve Ceza", "Fyodor Dostoyevski", "Dünya klasikleri", 1866, 185m);
        await Ekle("Karamazov Kardeşler", "Fyodor Dostoyevski", "Dünya klasikleri", 1880, 280m);
        await Ekle("Tutunamayanlar", "Oğuz Atay", "Türk edebiyatı", 1971, 240m);
        await Ekle("Dune", "Frank Herbert", "Bilim kurgu", 1965, 210m);
    }

    [SqlFact]
    public async Task Arama_Siralama_Sayfalama_SqlServerdeCalisir()
    {
        await Hazirla();

        Assert.Equal(2, (await Liste("q=DOSTOYEVSKI")).toplam);           // büyük/küçük harf duyarsız, yazarda arar
        Assert.Equal(["Tutunamayanlar"], (await Liste("q=tutuna")).basliklar);
        Assert.Equal(0, (await Liste("q=yok-boyle-bir-sey")).toplam);

        var (siraliBasliklar, _) = await Liste("q=dostoyevski&sort=year&desc=true");
        Assert.Equal(["Karamazov Kardeşler", "Suç ve Ceza"], siraliBasliklar);
        Assert.Equal(["Dune", "Karamazov Kardeşler", "Suç ve Ceza", "Tutunamayanlar"], (await Liste("sort=title")).basliklar);
        Assert.Equal(["Karamazov Kardeşler", "Tutunamayanlar"], (await Liste("sort=price&desc=true&page=1&pageSize=2")).basliklar);

        var sayfa = await Liste("page=2&pageSize=3");
        Assert.Equal(4, sayfa.toplam);
        Assert.Single(sayfa.basliklar);
    }

    [SqlFact]
    public async Task Kategori_SuzgeciVeListesi_SqlServerdeCalisir()
    {
        await Hazirla();
        Assert.Equal(2, (await Liste("category=" + Uri.EscapeDataString("Dünya klasikleri"))).toplam);
        var kategoriler = (await _istemci.GetFromJsonAsync<JsonElement>("/api/books/categories")).EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Equal(3, kategoriler.Count);
        Assert.Contains("Bilim kurgu", kategoriler);
    }

    [SqlFact]
    public async Task OduncUzatma_GecikenSuzgeci_SqlServerdeCalisir()
    {
        var id = await Ekle("Uzatılacak", null, null, 2000, 10m);
        var r = await _istemci.PostAsJsonAsync($"/api/books/checkout/{id}", new { name = "Ayşe Yılmaz", tckn = "10000000146" });
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);

        var uzat = await _istemci.PostAsync($"/api/books/{id}/renew", null);
        Assert.Equal(HttpStatusCode.OK, uzat.StatusCode);
        Assert.Equal(1, (await uzat.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("renewalCount").GetInt32());

        Assert.Equal(0, (await Liste("overdue=true")).toplam);
        Assert.Equal(1, (await Liste("available=false")).toplam);
    }

    [SqlFact]
    public async Task Raporlar_DisaAktarma_Denetim_SqlServerdeCalisir()
    {
        await Hazirla();
        var id = await Ekle("Rapor kitabı", "Yazar", "Kat", 2001, 50m);
        await _istemci.PostAsJsonAsync($"/api/books/checkout/{id}", new { name = "Ayşe Yılmaz", tckn = "10000000146" });
        await _istemci.PostAsync($"/api/books/checkin/{id}", null);

        var ozet = await _istemci.GetFromJsonAsync<JsonElement>("/api/reports/summary");
        Assert.Equal(5, ozet.GetProperty("totalBooks").GetInt32());
        Assert.Equal(1, ozet.GetProperty("totalLoans").GetInt32());
        Assert.Equal(6, ozet.GetProperty("monthly").GetArrayLength());
        Assert.Equal("Rapor kitabı", ozet.GetProperty("popular")[0].GetProperty("title").GetString());
        Assert.True(ozet.GetProperty("categories").GetArrayLength() >= 3);

        Assert.Equal(HttpStatusCode.OK, (await _istemci.GetAsync("/api/reports/overdue")).StatusCode);
        foreach (var tur in new[] { "books", "loans", "overdue" })
            foreach (var bicim in new[] { "csv", "xlsx" })
                Assert.Equal(HttpStatusCode.OK, (await _istemci.GetAsync($"/api/reports/export/{tur}?format={bicim}")).StatusCode);

        var denetim = await _istemci.GetFromJsonAsync<JsonElement>("/api/audit?pageSize=200");
        var islemler = denetim.EnumerateArray().Select(x => x.GetProperty("action").GetString()).ToList();
        Assert.Contains("KITAP_EKLE", islemler);
        Assert.Contains("ODUNC_VER", islemler);
        Assert.Contains("IADE_AL", islemler);
        Assert.Contains("DISA_AKTAR", islemler);
        Assert.Contains("GIRIS", islemler);
    }
}
