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

/// <summary>SQL Server yoksa testi atlar. <c>BOOKTRACK_TEST_SQL</c>: örn. "Server=(localdb)\mssqllocaldb;Integrated Security=true".</summary>
public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (!SqlConcurrencyTests.Available) Skip = "BOOKTRACK_TEST_SQL tanımlı değil; SQL Server gerektiren test atlandı.";
    }
}

/// <summary>
/// Eş zamanlılık gerçek SQL Server'da (rowversion) doğrulanır; bellek içi sağlayıcı bu çakışmayı üretemez.
/// Her çalıştırma kendi geçici veritabanını oluşturur, migration'larla kurar ve sonunda siler.
/// </summary>
public sealed class SqlConcurrencyTests : IAsyncLifetime
{
    public static readonly string? ServerConnection = Environment.GetEnvironmentVariable("BOOKTRACK_TEST_SQL");
    public static bool Available => !string.IsNullOrWhiteSpace(ServerConnection);

    private readonly string _database = "BookTrack_T_" + Guid.NewGuid().ToString("N")[..10];
    private WebApplicationFactory<Program>? _fabrika;
    private HttpClient _istemci = default!;

    public async Task InitializeAsync()
    {
        if (!Available) return;

        var cs = new SqlConnectionStringBuilder(ServerConnection) { InitialCatalog = _database, MultipleActiveResultSets = true }.ConnectionString;
        _fabrika = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("ConnectionStrings:DefaultConnection", cs);
        });
        using (var kapsam = _fabrika.Services.CreateScope())
            await kapsam.ServiceProvider.GetRequiredService<LibraryContext>().Database.MigrateAsync();
        _istemci = _fabrika.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _fabrika?.Dispose();
        if (!Available) return;
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(ServerConnection);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"IF DB_ID(N'{_database}') IS NOT NULL BEGIN ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]; END";
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> KitapEkle(string ad)
    {
        var r = await _istemci.PostAsJsonAsync("/api/books", new { title = ad, isbn = "978-1", publicationYear = 2000, price = 10 });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private Task<HttpResponseMessage> Odunc(int id, int kisi) =>
        _istemci.PostAsJsonAsync($"/api/books/checkout/{id}", new { name = $"Kişi {kisi}", tckn = "12345678901" });

    private async Task<int> GecmisSayisi(int id) =>
        (await _istemci.GetFromJsonAsync<JsonElement>($"/api/books/{id}/history")).GetArrayLength();

    [SqlFact]
    public async Task AyniKitapAyniAndaBirdenFazlaKisiyeOduncVerilemez()
    {
        // Yarışın tutması için birkaç tur denenir; her turda tam olarak bir istek başarılı olmalı.
        for (var tur = 0; tur < 5; tur++)
        {
            var id = await KitapEkle($"Yarış {tur}");

            var yanitlar = await Task.WhenAll(Enumerable.Range(1, 8).Select(i => Odunc(id, i)));

            var basarili = yanitlar.Count(r => r.StatusCode == HttpStatusCode.NoContent);
            Assert.Equal(1, basarili);
            Assert.All(yanitlar.Where(r => r.StatusCode != HttpStatusCode.NoContent),
                r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict }));
            Assert.Equal(1, await GecmisSayisi(id)); // reddedilen isteklerin ödünç kaydı da yazılmamalı
        }
    }

    [SqlFact]
    public async Task AyniKitapAyniAndaBirdenFazlaKezIadeEdilirseYalnizcaBiriBasarili()
    {
        for (var tur = 0; tur < 5; tur++)
        {
            var id = await KitapEkle($"İade yarışı {tur}");
            Assert.Equal(HttpStatusCode.NoContent, (await Odunc(id, 1)).StatusCode);

            var yanitlar = await Task.WhenAll(Enumerable.Range(1, 6).Select(_ => _istemci.PostAsync($"/api/books/checkin/{id}", null)));

            Assert.Equal(1, yanitlar.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.All(yanitlar.Where(r => r.StatusCode != HttpStatusCode.OK),
                r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict, HttpStatusCode.NotFound }));
        }
    }

    [SqlFact]
    public async Task TekTekIslemlerSqlServerdaDaCalisir_SurumAlaniDisariSizmaz()
    {
        var id = await KitapEkle("Normal akış");

        var ham = await _istemci.GetStringAsync($"/api/books/{id}");
        Assert.DoesNotContain("rowversion", ham, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.NoContent, (await Odunc(id, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _istemci.PostAsync($"/api/books/checkin/{id}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Odunc(id, 2)).StatusCode);   // iade sonrası tekrar verilebilir
        Assert.Equal(2, await GecmisSayisi(id));
    }
}
