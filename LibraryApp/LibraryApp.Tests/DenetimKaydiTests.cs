using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LibraryApp.Tests;

/// <summary>Denetim kaydı: hangi işlemler yazılır, kimler görür, kişisel veri sızar mı.</summary>
public class DenetimKaydiTests : ZamanliTestTemeli
{
    public DenetimKaydiTests(ZamanliFabrika fabrika) : base(fabrika) { }

    private async Task<List<JsonElement>> Kayitlar(string sorgu = "pageSize=200")
    {
        var r = await Yonetici.GetAsync("/api/audit?" + sorgu);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await Json(r)).EnumerateArray().ToList();
    }

    private static bool Var(IEnumerable<JsonElement> kayitlar, string islem, int? varlik = null) =>
        kayitlar.Any(k => k.GetProperty("action").GetString() == islem
            && (varlik is null || (k.GetProperty("entityId").ValueKind == JsonValueKind.Number && k.GetProperty("entityId").GetInt32() == varlik)));

    [Fact]
    public async Task KitapIslemleriDenetimeYazilir()
    {
        var e = Etiket();
        var id = await KitapEkle(Kitap("Denetim " + e));
        await Odunc(id);
        await Yonetici.PostAsync($"/api/books/{id}/renew", null);
        await Yonetici.PostAsync($"/api/books/checkin/{id}", null);
        var silinecek = await KitapEkle(Kitap("Silinecek " + e));
        await Yonetici.DeleteAsync($"/api/books/{silinecek}");
        await Yonetici.PutAsJsonAsync($"/api/books/{id}", new { id, title = "Denetim " + e, isbn = YeniIsbn(), publicationYear = 2000, price = 1m });

        var k = await Kayitlar();
        Assert.True(Var(k, "KITAP_EKLE", id));
        Assert.True(Var(k, "ODUNC_VER", id));
        Assert.True(Var(k, "ODUNC_UZAT", id));
        Assert.True(Var(k, "IADE_AL", id));
        Assert.True(Var(k, "KITAP_GUNCELLE", id));
        Assert.True(Var(k, "KITAP_SIL", silinecek));
    }

    [Fact]
    public async Task Kayit_KimYaptiginiVeZamaniniTasir()
    {
        var id = await KitapEkle(Kitap("Kim " + Etiket()));
        var kayit = (await Kayitlar()).First(k => k.GetProperty("action").GetString() == "KITAP_EKLE" && k.GetProperty("entityId").GetInt32() == id);
        Assert.Equal(TestAuth.AdminUser, kayit.GetProperty("username").GetString());
        Assert.Equal("Kitap", kayit.GetProperty("entityType").GetString());
        Assert.Equal(new DateTime(2026, 3, 2, 10, 0, 0), kayit.GetProperty("timestamp").GetDateTime());
    }

    [Fact]
    public async Task Gorevli_KendiIslemleriniYazarAmaKaydiGoremez()
    {
        var gorevli = await GorevliAc();
        var id = await KitapEkle(gorevli, Kitap("Gorevli " + Etiket()));
        Assert.Equal(HttpStatusCode.Forbidden, (await gorevli.GetAsync("/api/audit")).StatusCode);

        var kayit = (await Kayitlar()).First(k => k.GetProperty("action").GetString() == "KITAP_EKLE" && k.GetProperty("entityId").GetInt32() == id);
        Assert.StartsWith("gorevli", kayit.GetProperty("username").GetString());
    }

    [Fact]
    public async Task Oturumsuzlar_DenetimKaydiniGoremez()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Fabrika.CreateClient().GetAsync("/api/audit")).StatusCode);
    }

    [Fact]
    public async Task GirisVeBasarisizGirisKaydedilir_ParolaYazilmaz()
    {
        var anonim = Fabrika.CreateClient();
        var yanlis = "Yanlis-Parola-" + Etiket();
        var kullanici = "olmayan" + Etiket();
        await anonim.PostAsJsonAsync("/api/auth/login", new { username = kullanici, password = yanlis });
        await anonim.PostAsJsonAsync("/api/auth/login", new { username = TestAuth.AdminUser, password = TestAuth.AdminPassword });

        var ham = await (await Yonetici.GetAsync("/api/audit?pageSize=200")).Content.ReadAsStringAsync();
        var k = JsonDocument.Parse(ham).RootElement.EnumerateArray().ToList();
        Assert.Contains(k, x => x.GetProperty("action").GetString() == "GIRIS_BASARISIZ" && x.GetProperty("username").GetString() == kullanici);
        Assert.Contains(k, x => x.GetProperty("action").GetString() == "GIRIS" && x.GetProperty("username").GetString() == TestAuth.AdminUser);
        Assert.DoesNotContain(yanlis, ham);
        Assert.DoesNotContain(TestAuth.AdminPassword, ham);
    }

    [Fact]
    public async Task OduncKaydi_TcknVeTelefonuIcermez()
    {
        var id = await KitapEkle(Kitap("Kisisel " + Etiket()));
        await Odunc(id);
        var ham = await (await Yonetici.GetAsync("/api/audit?pageSize=200")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("10000000146", ham);
        Assert.DoesNotContain("05550000000", ham);
        Assert.DoesNotContain("Ayşe", ham);
    }

    [Fact]
    public async Task KullaniciYonetimiDenetimeYazilir()
    {
        var ad = "denetimli" + Etiket();
        var r = await Yonetici.PostAsJsonAsync("/api/users", new { username = ad, password = "Denetimli-Parola-1", role = "Librarian" });
        var id = (await Json(r)).GetProperty("id").GetInt32();
        await Yonetici.PostAsync($"/api/users/{id}/deactivate", null);
        await Yonetici.PostAsync($"/api/users/{id}/activate", null);

        var k = await Kayitlar();
        Assert.True(Var(k, "KULLANICI_OLUSTUR", id));
        Assert.True(Var(k, "KULLANICI_PASIF", id));
        Assert.True(Var(k, "KULLANICI_AKTIF", id));
    }

    [Fact]
    public async Task Liste_YenidenEskiyeSiralanirIslemVeKullaniciyaGoreSuzulur()
    {
        var e = Etiket();
        var a = await KitapEkle(Kitap("Sira A " + e));
        var b = await KitapEkle(Kitap("Sira B " + e));

        var eklemeler = await Kayitlar("action=KITAP_EKLE&pageSize=200");
        Assert.All(eklemeler, x => Assert.Equal("KITAP_EKLE", x.GetProperty("action").GetString()));
        var idler = eklemeler.Select(x => x.GetProperty("id").GetInt32()).ToList();
        Assert.Equal(idler.OrderByDescending(x => x), idler);
        Assert.True(idler.IndexOf(eklemeler.First(x => x.GetProperty("entityId").GetInt32() == b).GetProperty("id").GetInt32())
                    < idler.IndexOf(eklemeler.First(x => x.GetProperty("entityId").GetInt32() == a).GetProperty("id").GetInt32()));

        var yoneticiKayitlari = await Kayitlar($"user={TestAuth.AdminUser}&pageSize=200");
        Assert.All(yoneticiKayitlari, x => Assert.Equal(TestAuth.AdminUser, x.GetProperty("username").GetString()));
        Assert.Empty(await Kayitlar("user=yok-boyle-biri&pageSize=200"));
    }

    [Fact]
    public async Task Liste_SayfalanirToplamBaslikta()
    {
        for (var i = 0; i < 3; i++) await KitapEkle(Kitap("Sayfa " + Etiket()));
        var r = await Yonetici.GetAsync("/api/audit?action=KITAP_EKLE&page=1&pageSize=2");
        Assert.Equal(2, (await Json(r)).GetArrayLength());
        Assert.True(Toplam(r) >= 3);

        var buyuk = await Yonetici.GetAsync("/api/audit?pageSize=100000");
        Assert.Equal(HttpStatusCode.OK, buyuk.StatusCode);
        Assert.True((await Json(buyuk)).GetArrayLength() <= 200);
    }

    [Fact]
    public async Task DisaAktarmaDenetimeYazilir()
    {
        await Yonetici.GetAsync("/api/reports/export/books?format=csv");
        var kayit = (await Kayitlar("action=DISA_AKTAR&pageSize=200")).First();
        Assert.Contains("books, csv", kayit.GetProperty("details").GetString());
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task DenetimKaydiDegistirilemez_YazmaUcNoktasiYoktur(string yontem)
    {
        var r = await Yonetici.SendAsync(new HttpRequestMessage(new HttpMethod(yontem), "/api/audit/1")
        {
            Content = JsonContent.Create(new { action = "X" }),
        });
        Assert.True(r.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
    }
}
