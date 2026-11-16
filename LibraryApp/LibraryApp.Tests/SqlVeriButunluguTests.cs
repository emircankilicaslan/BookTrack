using System.Net;
using System.Net.Http.Json;
using LibraryApp.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Tests;

/// <summary>
/// Issue #4 için gerçek SQL Server testleri: yabancı anahtar, unique index ve mevcut veriyi taşıyan migration.
/// Her test kendi geçici veritabanını kurar ve siler. <c>BOOKTRACK_TEST_SQL</c> yoksa atlanır.
/// </summary>
public sealed class SqlVeriButunluguTests : IAsyncLifetime
{
    private const string OncekiMigration = "20261113150918_BookRowVersion";

    private readonly string _database = "BookTrack_V_" + Guid.NewGuid().ToString("N")[..10];
    private string _cs = "";

    public Task InitializeAsync()
    {
        if (SqlConcurrencyTests.Available)
            _cs = new SqlConnectionStringBuilder(SqlConcurrencyTests.ServerConnection) { InitialCatalog = _database, MultipleActiveResultSets = true }.ConnectionString;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (!SqlConcurrencyTests.Available) return;
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(SqlConcurrencyTests.ServerConnection);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"IF DB_ID(N'{_database}') IS NOT NULL BEGIN ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]; END";
        await cmd.ExecuteNonQueryAsync();
    }

    private LibraryContext YeniContext() =>
        new(new DbContextOptionsBuilder<LibraryContext>().UseSqlServer(_cs).Options);

    private async Task Sql(string komut)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = komut;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> Sayi(string sorgu)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sorgu;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<Exception?> Hata(Func<Task> is_)
    {
        try { await is_(); return null; }
        catch (Exception e) { return e; }
    }

    private async Task EskiSemayiKur()
    {
        await using var db = YeniContext();
        await db.GetService<IMigrator>().MigrateAsync(OncekiMigration);
    }

    private const string KitapEkleSql =
        "INSERT INTO Books (Title, ISBN, PublicationYear, Price, IsCheckedOut) VALUES (N'{0}', N'{1}', 2000, 10, 0);";

    private const string OduncEkleSql =
        "INSERT INTO CheckOuts (BookId, Name, TCKN, CheckOutDate, DueDate) VALUES ({0}, N'Ayşe', N'10000000146', GETDATE(), GETDATE());";

    [SqlFact]
    public async Task Migration_YetimKayitlariArsivlerVeYabanciAnahtarEkler()
    {
        await EskiSemayiKur();
        await Sql(string.Format(KitapEkleSql, "Var", "111"));
        var varId = await Sayi("SELECT Id FROM Books WHERE ISBN = N'111'");
        await Sql(string.Format(OduncEkleSql, varId));        // sağlam kayıt
        await Sql(string.Format(OduncEkleSql, 99999));        // yetim kayıt (kitap yok)

        await using (var db = YeniContext()) await db.Database.MigrateAsync();

        Assert.Equal(1, await Sayi("SELECT COUNT(*) FROM CheckOuts"));                       // sağlam kayıt yerinde
        Assert.Equal(1, await Sayi("SELECT COUNT(*) FROM CheckOuts_Yetim WHERE BookId = 99999")); // yetim silinmedi, arşivde
        Assert.Equal(1, await Sayi("SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_CheckOuts_Books_BookId'"));
        Assert.Equal(1, await Sayi("SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_Books_ISBN' AND is_unique = 1"));
        Assert.Equal(300, await Sayi("SELECT max_length/2 FROM sys.columns WHERE object_id = OBJECT_ID('Books') AND name = 'Title'"));
        Assert.Equal(20, await Sayi("SELECT max_length/2 FROM sys.columns WHERE object_id = OBJECT_ID('Books') AND name = 'ISBN'"));
    }

    [SqlFact]
    public async Task Migration_TekrarEdenIsbnVarsaAcikHataVerirVeHicbirSeyDegistirmez()
    {
        await EskiSemayiKur();
        await Sql(string.Format(KitapEkleSql, "A", "222"));
        await Sql(string.Format(KitapEkleSql, "B", "222"));

        var hata = await Hata(async () => { await using var db = YeniContext(); await db.Database.MigrateAsync(); });

        Assert.NotNull(hata);
        Assert.Contains("ISBN", hata!.ToString());
        Assert.Equal(0, await Sayi("SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_Books_ISBN'"));
        Assert.Equal(2, await Sayi("SELECT COUNT(*) FROM Books"));
    }

    [SqlFact]
    public async Task Migration_LimitiAsanAlanVarsaAcikHataVerir()
    {
        await EskiSemayiKur();
        await Sql(string.Format(KitapEkleSql, new string('x', 5000), "333"));

        var hata = await Hata(async () => { await using var db = YeniContext(); await db.Database.MigrateAsync(); });

        Assert.NotNull(hata);
        Assert.Contains("300", hata!.ToString());
    }

    [SqlFact]
    public async Task Migration_AyniVeriylePekIkiKezCalistirilirsaYetimTablosuKorunur()
    {
        // Yetim arşivi tablosu önceden varsa (ör. yarım kalmış deneme) tekrar çalıştırma verisini kaybetmemeli.
        await EskiSemayiKur();
        await Sql(string.Format(OduncEkleSql, 77777));
        await Sql("SELECT * INTO dbo.CheckOuts_Yetim FROM dbo.CheckOuts WHERE 1 = 0");

        await using (var db = YeniContext()) await db.Database.MigrateAsync();

        Assert.Equal(1, await Sayi("SELECT COUNT(*) FROM CheckOuts_Yetim WHERE BookId = 77777"));
        Assert.Equal(0, await Sayi("SELECT COUNT(*) FROM CheckOuts"));
    }

    [SqlFact]
    public async Task YeniSemada_IsbnTekrariVeGecmisiOlanKitabinSilinmesiVeritabaniDaEngellenir()
    {
        await using (var db = YeniContext()) await db.Database.MigrateAsync();

        await Sql(string.Format(KitapEkleSql, "A", "444"));
        var tekrar = await Hata(() => Sql(string.Format(KitapEkleSql, "B", "444")));
        Assert.NotNull(tekrar); // unique index

        var id = await Sayi("SELECT Id FROM Books WHERE ISBN = N'444'");
        await Sql(string.Format(OduncEkleSql, id));
        var silme = await Hata(() => Sql($"DELETE FROM Books WHERE Id = {id}"));
        Assert.NotNull(silme); // yabancı anahtar (Restrict)

        var yetimEkle = await Hata(() => Sql(string.Format(OduncEkleSql, 88888)));
        Assert.NotNull(yetimEkle); // kitabı olmayan ödünç kaydı yazılamaz
    }

    [SqlFact]
    public async Task Api_SqlServerdaIsbnTekrariIcin409_GecmisiOlanKitapSilinirseDe409()
    {
        await using var fabrika = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("ConnectionStrings:DefaultConnection", _cs);
            b.UseSetting("Database:MigrateOnStartup", "true");
            TestAuth.Configure(b);
        });
        using (var kapsam = fabrika.Services.CreateScope())
            await kapsam.ServiceProvider.GetRequiredService<LibraryContext>().Database.MigrateAsync();
        var istemci = await TestAuth.AdminAsync(fabrika);

        var k = new { title = "Kitap", isbn = "555", publicationYear = 2000, price = 10 };
        var ilk = await istemci.PostAsJsonAsync("/api/books", k);
        Assert.Equal(HttpStatusCode.Created, ilk.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await istemci.PostAsJsonAsync("/api/books", k)).StatusCode);

        var id = await Sayi("SELECT Id FROM Books WHERE ISBN = N'555'");
        Assert.Equal(HttpStatusCode.NoContent, (await istemci.PostAsJsonAsync($"/api/books/checkout/{id}", new { name = "Ayşe", tckn = "10000000146" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await istemci.PostAsync($"/api/books/checkin/{id}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await istemci.DeleteAsync($"/api/books/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await istemci.GetAsync($"/api/books/{id}/history")).StatusCode);
    }
}
