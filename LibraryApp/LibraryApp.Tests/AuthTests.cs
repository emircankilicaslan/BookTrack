using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using LibraryApp.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace LibraryApp.Tests;

/// <summary>Kimlik doğrulama (JWT) ve rol bazlı yetki: Admin ve Librarian rolleri, pasif hesap, hız sınırı.</summary>
public class AuthTests : IClassFixture<BooksApiTests.Fabrika>
{
    private const string Parola = "Gecerli-Parola-1";

    private readonly BooksApiTests.Fabrika _fabrika;

    public AuthTests(BooksApiTests.Fabrika fabrika) => _fabrika = fabrika;

    private static string Ad(string onek) => $"{onek}-{Guid.NewGuid():N}"[..20];

    private static async Task<JsonElement> Govde(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<(int Id, string Ad)> KullaniciOlustur(HttpClient admin, string role, string? ad = null)
    {
        ad ??= Ad(role.ToLowerInvariant());
        var r = await admin.PostAsJsonAsync("/api/users", new { username = ad, password = Parola, role });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return ((await Govde(r)).GetProperty("id").GetInt32(), ad);
    }

    private HttpClient Anonim() => _fabrika.CreateClient();

    private async Task<HttpClient> Oturum(string ad, string parola = Parola) => await TestAuth.LoginAsync(_fabrika.CreateClient(), ad, parola);

    private static object Kitap() => new { title = "Kitap", isbn = ("978-" + Guid.NewGuid().ToString("N"))[..20], publicationYear = 2000, price = 10 };

    // ---------------------------------------------------------------- erişim

    [Fact]
    public async Task KimliksizIstekler401Doner_ArayuzDosyalariAcikKalir()
    {
        var c = Anonim();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/books")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/books", Kitap())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.DeleteAsync("/api/books/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/books/1/history")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/auth/me")).StatusCode);

        // Giriş sayfası ve betikleri kimlik doğrulaması olmadan sunulur; veri uçları korunur.
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/app.js")).StatusCode);
    }

    [Fact]
    public async Task SahteBozukVeBaskaAnahtarlaImzaliTokenReddedilir()
    {
        var admin = await TestAuth.AdminAsync(_fabrika);
        var gecerli = admin.DefaultRequestHeaders.Authorization!.Parameter!;

        var c = Anonim();
        foreach (var token in new[]
        {
            "sahte.token.degeri",
            gecerli[..^3] + (gecerli[^3] == 'A' ? "B" : "A") + gecerli[^2..], // imza bozuldu
            YabanciAnahtarlaToken("Admin", "yonetici"),
            YabanciAnahtarlaToken("Admin", "yonetici", anahtar: TestAuth.JwtSecret, Audience: "baska-hedef"),
        })
        {
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/books")).StatusCode);
        }
    }

    private static string YabanciAnahtarlaToken(string rol, string ad, string anahtar = "baska-bir-anahtar-baska-bir-anahtar-123456789", string Audience = "BookTrack.Istemci")
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(anahtar));
        var jwt = new JwtSecurityToken("BookTrack", Audience,
            [new Claim("sub", "1"), new Claim("name", ad), new Claim("role", rol)],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddHours(1),
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    // ---------------------------------------------------------------- giriş

    [Fact]
    public async Task Giris_BasariliVeBasarisizDurumlar()
    {
        var c = Anonim();
        var yanlis = await c.PostAsJsonAsync("/api/auth/login", new { username = TestAuth.AdminUser, password = "yanlis-parola-9" });
        var yok = await c.PostAsJsonAsync("/api/auth/login", new { username = "yok-boyle-biri", password = "yanlis-parola-9" });
        Assert.Equal(HttpStatusCode.Unauthorized, yanlis.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, yok.StatusCode);
        // Hesap varlığı mesajdan anlaşılmaz.
        Assert.Equal(await yanlis.Content.ReadAsStringAsync(), await yok.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/auth/login", new { username = "", password = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/auth/login", new { username = TestAuth.AdminUser })).StatusCode);

        var ok = await c.PostAsJsonAsync("/api/auth/login", new { username = TestAuth.AdminUser, password = TestAuth.AdminPassword });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var g = await Govde(ok);
        Assert.Equal("Admin", g.GetProperty("role").GetString());
        Assert.Equal(TestAuth.AdminUser, g.GetProperty("username").GetString());
        Assert.True(g.GetProperty("expiresAt").GetDateTime() > DateTime.UtcNow);
        Assert.DoesNotContain("passwordHash", await ok.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Me_OturumdakiKullaniciyiDondurur()
    {
        var admin = await TestAuth.AdminAsync(_fabrika);
        var me = await Govde(await admin.GetAsync("/api/auth/me"));
        Assert.Equal(TestAuth.AdminUser, me.GetProperty("username").GetString());
        Assert.Equal("Admin", me.GetProperty("role").GetString());
    }

    [Fact]
    public async Task IlkYoneticiYalnizcaBirKezOlusturulur()
    {
        var admin = await TestAuth.AdminAsync(_fabrika);
        var liste = (await Govde(await admin.GetAsync("/api/users"))).EnumerateArray().ToList();
        Assert.Single(liste, u => u.GetProperty("username").GetString() == TestAuth.AdminUser);
        Assert.DoesNotContain("passwordHash", await admin.GetStringAsync("/api/users"), StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- roller

    [Fact]
    public async Task Gorevli_KitapYonetirAmaSilemezVeKullaniciYonetemez()
    {
        var admin = await TestAuth.AdminAsync(_fabrika);
        var (_, ad) = await KullaniciOlustur(admin, "Librarian");
        var gorevli = await Oturum(ad);

        var ekle = await gorevli.PostAsJsonAsync("/api/books", Kitap());
        Assert.Equal(HttpStatusCode.Created, ekle.StatusCode);
        var id = (await Govde(ekle)).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await gorevli.GetAsync("/api/books")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await gorevli.PostAsJsonAsync($"/api/books/checkout/{id}", new { name = "Ayşe", tckn = "10000000146" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await gorevli.PostAsync($"/api/books/checkin/{id}", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await gorevli.DeleteAsync($"/api/books/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await gorevli.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await gorevli.PostAsJsonAsync("/api/users", new { username = "yeni-kisi", password = Parola, role = "Admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await gorevli.PostAsync("/api/users/1/deactivate", null)).StatusCode);

        // Aynı kitabı yönetici silemez çünkü ödünç geçmişi var (409); geçmişsiz bir kitabı siler (204).
        var gecmissiz = (await Govde(await admin.PostAsJsonAsync("/api/books", Kitap()))).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/books/{gecmissiz}")).StatusCode);
    }

    [Fact]
    public async Task KullaniciOlusturma_Dogrulamalar()
    {
        var admin = await TestAuth.AdminAsync(_fabrika);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/users", new { username = Ad("kisa"), password = "kisa", role = "Librarian" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/users", new { username = "ab", password = Parola, role = "Librarian" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/users", new { username = Ad("rol"), password = Parola, role = "Patron" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/users", new { username = Ad("rolyok"), password = Parola })).StatusCode);

        var (_, ad) = await KullaniciOlustur(admin, "Librarian");
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/users", new { username = ad, password = Parola, role = "Librarian" })).StatusCode);
    }

    // ---------------------------------------------------------------- pasif hesap

    [Fact]
    public async Task PasifeAlinanKullanicininTokeniAninda_GecersizOlur_VeYenidenEtkinlestirilebilir()
    {
        var admin = await TestAuth.AdminAsync(_fabrika);
        var (id, ad) = await KullaniciOlustur(admin, "Librarian");
        var gorevli = await Oturum(ad);
        Assert.Equal(HttpStatusCode.OK, (await gorevli.GetAsync("/api/books")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/users/{id}/deactivate", null)).StatusCode);

        // Süresi dolmamış token bile bir sonraki istekte reddedilir; yeniden giriş de yapılamaz.
        Assert.Equal(HttpStatusCode.Unauthorized, (await gorevli.GetAsync("/api/books")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonim().PostAsJsonAsync("/api/auth/login", new { username = ad, password = Parola })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/users/{id}/activate", null)).StatusCode);
        var yeni = await Oturum(ad);
        Assert.Equal(HttpStatusCode.OK, (await yeni.GetAsync("/api/books")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/users/999999/deactivate", null)).StatusCode);
    }

    [Fact]
    public async Task KendiniVeSonAktifYoneticiyiPasifeAlmaEngellenir()
    {
        var admin = await TestAuth.AdminAsync(_fabrika);
        var (idB, adB) = await KullaniciOlustur(admin, "Admin");
        var adminB = await Oturum(adB);
        var benimId = (await Govde(await admin.GetAsync("/api/users"))).EnumerateArray()
            .Single(u => u.GetProperty("username").GetString() == TestAuth.AdminUser).GetProperty("id").GetInt32();

        // Kendini pasife alamaz.
        Assert.Equal(HttpStatusCode.Conflict, (await adminB.PostAsync($"/api/users/{idB}/deactivate", null)).StatusCode);

        // İkinci yönetici ilkini pasife alabilir (bir aktif yönetici kalır) ...
        Assert.Equal(HttpStatusCode.NoContent, (await adminB.PostAsync($"/api/users/{benimId}/deactivate", null)).StatusCode);
        // ... ve ilk yönetici yeniden etkinleştirilmeden son aktif yönetici bırakılamaz.
        Assert.Equal(HttpStatusCode.Conflict, (await adminB.PostAsync($"/api/users/{idB}/deactivate", null)).StatusCode);

        // Diğer testlerin yöneticisini geri aç.
        Assert.Equal(HttpStatusCode.NoContent, (await adminB.PostAsync($"/api/users/{benimId}/activate", null)).StatusCode);
    }

    // ---------------------------------------------------------------- yapılandırma ve hız sınırı

    private static WebApplicationFactory<Program> BaskaFabrika(Action<IWebHostBuilder> ayar) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            var ad = $"auth-{Guid.NewGuid():N}";
            b.UseEnvironment("Development");
            ayar(b);
            b.ConfigureServices(s =>
            {
                foreach (var d in s.Where(d => d.ServiceType == typeof(DbContextOptions<LibraryContext>)).ToList()) s.Remove(d);
                s.AddDbContext<LibraryContext>(o => o.UseInMemoryDatabase(ad));
            });
        });

    [Fact]
    public async Task GirisHizSiniri_LimiteTakilinca429Doner()
    {
        using var f = BaskaFabrika(b => TestAuth.Configure(b, loginPerMinute: 3));
        var c = f.CreateClient();
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/auth/login", new { username = "x-kisi", password = "yanlis-parola-1" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await c.PostAsJsonAsync("/api/auth/login", new { username = "x-kisi", password = "yanlis-parola-1" })).StatusCode);
    }

    [Fact]
    public void JwtAnahtariOlmadanUygulamaBaslamaz()
    {
        using var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Production"));
        var hata = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains("Jwt:Secret", hata.ToString());
    }

    [Fact]
    public void KisaJwtAnahtariReddedilir()
    {
        using var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("Jwt:Secret", "kisa-anahtar");
        });
        var hata = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains("32", hata.ToString());
    }
}
