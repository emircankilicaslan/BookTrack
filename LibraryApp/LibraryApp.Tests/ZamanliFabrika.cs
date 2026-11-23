using LibraryApp.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LibraryApp.Tests;

/// <summary>Elle ilerletilebilen saat: gecikme ve uzatma senaryolarını gerçek zamanı beklemeden sınar.</summary>
public sealed class AyarliSaat : TimeProvider
{
    private DateTimeOffset _simdi;

    public AyarliSaat(DateTime baslangic) => _simdi = new DateTimeOffset(DateTime.SpecifyKind(baslangic, DateTimeKind.Utc));

    public override DateTimeOffset GetUtcNow() => _simdi;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public void Ayarla(DateTime zaman) => _simdi = new DateTimeOffset(DateTime.SpecifyKind(zaman, DateTimeKind.Utc));
    public void GunIlerlet(int gun) => _simdi = _simdi.AddDays(gun);
}

/// <summary>Bellek içi veritabanı ve ayarlanabilir saatle çalışan test fabrikası. 2026-03-02 Pazartesi 10:00'da başlar.</summary>
public class ZamanliFabrika : WebApplicationFactory<Program>
{
    private readonly string _ad = $"zamanli-{Guid.NewGuid():N}";

    public AyarliSaat Saat { get; } = new(new DateTime(2026, 3, 2, 10, 0, 0));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        TestAuth.Configure(builder);
        builder.ConfigureServices(services =>
        {
            foreach (var d in services.Where(d => d.ServiceType == typeof(DbContextOptions<LibraryContext>)).ToList())
                services.Remove(d);
            services.AddDbContext<LibraryContext>(o => o.UseInMemoryDatabase(_ad));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Saat);
            // Token ömrü de ayarlı saate göre denetlenir (gerçek saat ile sahte saat arasındaki fark tokenı geçersiz kılmasın).
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                o.TokenValidationParameters.LifetimeValidator = (notBefore, expires, _, _) =>
                {
                    var t = Saat.GetUtcNow().UtcDateTime;
                    return (notBefore is null || notBefore <= t.AddMinutes(1)) && (expires is null || expires >= t.AddMinutes(-1));
                });
        });
    }
}
