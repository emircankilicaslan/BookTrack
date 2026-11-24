using System.Net;
using LibraryApp.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Tests;

/// <summary>Sağlık denetimi uç noktaları: kimlik doğrulamasız erişilir, veritabanı kapalıyken ready 503 döner.</summary>
public class SaglikDenetimiTests
{
    private sealed class BellekFabrikasi : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            TestAuth.Configure(builder);
            builder.ConfigureServices(services =>
            {
                foreach (var d in services.Where(d => d.ServiceType == typeof(DbContextOptions<LibraryContext>)).ToList())
                    services.Remove(d);
                services.AddDbContext<LibraryContext>(o => o.UseInMemoryDatabase($"saglik-{Guid.NewGuid():N}"));
            });
        }
    }

    /// <summary>Ulaşılamayan bir SQL Server'a işaret eder; bağlantı denemesi hızlı başarısız olsun diye zaman aşımı kısadır.</summary>
    private sealed class KapaliVeritabaniFabrikasi : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            TestAuth.Configure(builder);
            // Açılışta yönetici oluşturma adımı veritabanına bağlanır; bu senaryoda atlanması için bilgiler boş bırakılır.
            builder.UseSetting("Auth:AdminUsername", "");
            builder.UseSetting("Auth:AdminPassword", "");
            builder.ConfigureServices(services =>
            {
                foreach (var d in services.Where(d => d.ServiceType == typeof(DbContextOptions<LibraryContext>)).ToList())
                    services.Remove(d);
                services.AddDbContext<LibraryContext>(o => o.UseSqlServer(
                    "Server=127.0.0.1,1;Database=Yok;User Id=sa;Password=x;Connect Timeout=1;TrustServerCertificate=True"));
            });
        }
    }

    [Fact]
    public async Task Live_KimlikDogrulamasiz_200Doner()
    {
        using var fabrika = new BellekFabrikasi();
        var r = await fabrika.CreateClient().GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Healthy", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ready_VeritabaniAcikken_200Doner()
    {
        using var fabrika = new BellekFabrikasi();
        var r = await fabrika.CreateClient().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Ready_VeritabaniKapaliyken_503DonerLiveEtkilenmez()
    {
        using var fabrika = new KapaliVeritabaniFabrikasi();
        var istemci = fabrika.CreateClient();

        var ready = await istemci.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        // Hata ayrıntısı (sunucu adı, bağlantı dizesi) gövdeye sızmamalı.
        var govde = await ready.Content.ReadAsStringAsync();
        Assert.Equal("Unhealthy", govde);

        Assert.Equal(HttpStatusCode.OK, (await istemci.GetAsync("/health/live")).StatusCode);
    }
}
