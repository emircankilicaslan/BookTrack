using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LibraryApp.Tests;

/// <summary>Ödünç uzatma: iş günü hesabı, uzatma hakkı, süresi geçmiş ödünç ve sayaç sıfırlama.</summary>
public class UzatmaTests : ZamanliTestTemeli
{
    public UzatmaTests(ZamanliFabrika fabrika) : base(fabrika) { }

    private async Task<JsonElement> Uzat(int id, HttpStatusCode beklenen = HttpStatusCode.OK)
    {
        var r = await Yonetici.PostAsync($"/api/books/{id}/renew", null);
        Assert.Equal(beklenen, r.StatusCode);
        return await Json(r);
    }

    private async Task<int> OduncluKitap()
    {
        var id = await KitapEkle(Kitap("Uzatilacak " + Etiket()));
        await Odunc(id);
        return id;
    }

    [Fact]
    public async Task Uzatma_YediIsGunuEklerVeKalanHakkiVerir()
    {
        var id = await OduncluKitap(); // 2 Mart + 15 iş günü = 24 Mart (20 Mart tatil)
        var ilk = await Uzat(id);
        Assert.Equal(new DateTime(2026, 4, 2), ilk.GetProperty("dueDate").GetDateTime().Date); // +7 iş günü
        Assert.Equal(1, ilk.GetProperty("renewalCount").GetInt32());
        Assert.Equal(1, ilk.GetProperty("remainingRenewals").GetInt32());

        var ikinci = await Uzat(id);
        Assert.Equal(new DateTime(2026, 4, 13), ikinci.GetProperty("dueDate").GetDateTime().Date);
        Assert.Equal(0, ikinci.GetProperty("remainingRenewals").GetInt32());
    }

    [Fact]
    public async Task Uzatma_HakBitinceReddedilir()
    {
        var id = await OduncluKitap();
        await Uzat(id);
        await Uzat(id);
        var r = await Uzat(id, HttpStatusCode.Conflict);
        Assert.Contains("en fazla 2 kez", r.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Uzatma_KitapVeGecmisKaydinaYansir()
    {
        var id = await OduncluKitap();
        await Uzat(id);

        var kitap = await Json(await Yonetici.GetAsync($"/api/books/{id}"));
        Assert.Equal(1, kitap.GetProperty("renewalCount").GetInt32());
        Assert.Equal(new DateTime(2026, 4, 2), kitap.GetProperty("dueDate").GetDateTime().Date);

        var gecmis = (await Json(await Yonetici.GetAsync($"/api/books/{id}/history"))).EnumerateArray().Single();
        Assert.Equal(1, gecmis.GetProperty("renewalCount").GetInt32());
        Assert.Equal(new DateTime(2026, 4, 2), gecmis.GetProperty("dueDate").GetDateTime().Date);
    }

    [Fact]
    public async Task Uzatma_SonTeslimGunuIcindeYapilabilir()
    {
        var id = await OduncluKitap();
        SaatAyarla(new DateTime(2026, 3, 24, 17, 0, 0)); // son teslim günü
        await Uzat(id);
    }

    [Fact]
    public async Task Uzatma_SuresiGecmisOduncIcinReddedilir()
    {
        var id = await OduncluKitap();
        SaatAyarla(new DateTime(2026, 3, 25, 9, 0, 0));
        var r = await Uzat(id, HttpStatusCode.Conflict);
        Assert.Contains("Süresi geçmiş", r.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Uzatma_OduncteOlmayanKitapIcin400_OlmayanKitapIcin404()
    {
        var id = await KitapEkle(Kitap("Rafta " + Etiket()));
        await Uzat(id, HttpStatusCode.BadRequest);
        await Uzat(999999, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Uzatma_IadeVeYenidenOduncSayaciSifirlar()
    {
        var id = await OduncluKitap();
        await Uzat(id);
        Assert.Equal(HttpStatusCode.OK, (await Yonetici.PostAsync($"/api/books/checkin/{id}", null)).StatusCode);
        Assert.Equal(0, (await Json(await Yonetici.GetAsync($"/api/books/{id}"))).GetProperty("renewalCount").GetInt32());

        await Odunc(id);
        var yeni = await Uzat(id);
        Assert.Equal(1, yeni.GetProperty("renewalCount").GetInt32());
        Assert.Equal(1, yeni.GetProperty("remainingRenewals").GetInt32());
    }

    [Fact]
    public async Task Uzatma_GorevliYapabilirOturumsuzYapamaz()
    {
        var id = await OduncluKitap();
        var gorevli = await GorevliAc();
        Assert.Equal(HttpStatusCode.OK, (await gorevli.PostAsync($"/api/books/{id}/renew", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Fabrika.CreateClient().PostAsync($"/api/books/{id}/renew", null)).StatusCode);
    }

    [Fact]
    public async Task Uzatma_UzatilanOduncunCezasiYeniSonTeslimeGoreHesaplanir()
    {
        var id = await OduncluKitap();
        await Uzat(id); // yeni son teslim 2 Nisan
        SaatAyarla(new DateTime(2026, 3, 26, 9, 0, 0));
        var onizleme = await Json(await Yonetici.GetAsync($"/api/books/{id}/fine-preview"));
        Assert.Equal(0, onizleme.GetProperty("lateDays").GetInt32());

        SaatAyarla(new DateTime(2026, 4, 3, 9, 0, 0)); // 2 Nisan Perşembe + 1 iş günü
        var iade = await Json(await Yonetici.PostAsync($"/api/books/checkin/{id}", null));
        Assert.Equal(1, iade.GetProperty("lateDays").GetInt32());
        Assert.Equal(5m, iade.GetProperty("fine").GetDecimal());
    }

    [Fact]
    public async Task Iade_GecikmeVeCezaGecmiseKaydedilir()
    {
        var id = await OduncluKitap();
        SaatAyarla(new DateTime(2026, 3, 27, 9, 0, 0)); // 24 Mart + 25, 26, 27 = 3 iş günü
        await Yonetici.PostAsync($"/api/books/checkin/{id}", null);

        var gecmis = (await Json(await Yonetici.GetAsync($"/api/books/{id}/history"))).EnumerateArray().Single();
        Assert.Equal(3, gecmis.GetProperty("lateDays").GetInt32());
        Assert.Equal(15m, gecmis.GetProperty("fineAmount").GetDecimal());
    }

    [Fact]
    public async Task Iade_ZamanindaYapilinceCezaSifirKaydedilir()
    {
        var id = await OduncluKitap();
        await Yonetici.PostAsync($"/api/books/checkin/{id}", null);
        var gecmis = (await Json(await Yonetici.GetAsync($"/api/books/{id}/history"))).EnumerateArray().Single();
        Assert.Equal(0, gecmis.GetProperty("lateDays").GetInt32());
        Assert.Equal(0m, gecmis.GetProperty("fineAmount").GetDecimal());
    }
}
