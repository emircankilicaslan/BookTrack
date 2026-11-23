"use strict";

// ---------------------------------------------------------------------------
// Yardımcılar
// ---------------------------------------------------------------------------

const $ = (s, k = document) => k.querySelector(s);
const SVG = "http://www.w3.org/2000/svg";

/** Güvenli DOM üretimi: metinler textContent olarak eklenir, innerHTML kullanılmaz. */
function h(etiket, ozellikler = {}, ...cocuklar) {
  const e = document.createElement(etiket);
  for (const [k, v] of Object.entries(ozellikler || {})) {
    if (v === false || v == null) continue;
    if (k === "class") e.className = v;
    else if (k.startsWith("on")) e.addEventListener(k.slice(2), v);
    else if (k === "value" && "value" in e) e.value = v;
    else e.setAttribute(k, v === true ? "" : v);
  }
  for (const c of cocuklar.flat(Infinity)) if (c != null && c !== false) e.append(c.nodeType ? c : String(c));
  return e;
}

/** SVG öğesi üretimi (grafikler). */
function s(etiket, ozellikler = {}, ...cocuklar) {
  const e = document.createElementNS(SVG, etiket);
  for (const [k, v] of Object.entries(ozellikler)) e.setAttribute(k, v);
  for (const c of cocuklar.flat()) if (c != null) e.append(c.nodeType ? c : String(c));
  return e;
}

const para = (n) => Number(n).toLocaleString("tr-TR", { style: "currency", currency: "TRY" });

// T.C. kimlik no sağlama algoritması (sunucudaki TcknDogrulayici ile aynı kural; sunucu yine de denetler).
function tcknGecerli(t) {
  if (!/^[0-9]{11}$/.test(t) || t[0] === "0") return false;
  const d = [...t].map(Number);
  const tek = d[0] + d[2] + d[4] + d[6] + d[8], cift = d[1] + d[3] + d[5] + d[7];
  if (((tek * 7 - cift) % 10 + 10) % 10 !== d[9]) return false;
  return d.slice(0, 10).reduce((a, b) => a + b, 0) % 10 === d[10];
}
const tarih = (x) => (x ? new Date(x).toLocaleDateString("tr-TR", { day: "numeric", month: "short", year: "numeric" }) : "–");
const tarihSaat = (x) => (x ? new Date(x).toLocaleString("tr-TR", { dateStyle: "medium", timeStyle: "short" }) : "–");

class ApiHatasi extends Error {
  constructor(mesaj, durum) { super(mesaj); this.durum = durum; }
}

// Oturum: token yalnızca sekme açıkken tutulur (sessionStorage); sekme kapanınca silinir. Depolama kapalıysa bellekte kalır.
const Oturum = (() => {
  let bellek = null;
  return {
    oku() { try { return JSON.parse(sessionStorage.getItem("booktrack.oturum")) ?? bellek; } catch { return bellek; } },
    yaz(o) { bellek = o; try { sessionStorage.setItem("booktrack.oturum", JSON.stringify(o)); } catch { /* depolama kapalı */ } },
    sil() { bellek = null; try { sessionStorage.removeItem("booktrack.oturum"); } catch { /* depolama kapalı */ } },
  };
})();

/** tam: true ise {veri, toplam} döner (toplam: X-Total-Count başlığı). */
async function api(yol, { yontem = "GET", govde, tam = false } = {}) {
  const oturum = Oturum.oku();
  const basliklar = {};
  if (govde !== undefined) basliklar["Content-Type"] = "application/json";
  if (oturum?.token) basliklar.Authorization = `Bearer ${oturum.token}`;
  const r = await fetch(yol, {
    method: yontem,
    headers: basliklar,
    body: govde !== undefined ? JSON.stringify(govde) : undefined,
  });
  if (r.status === 401 && oturum?.token && yol !== "/api/auth/login") {
    cikis("Oturum süresi doldu ya da hesabın pasife alındı. Lütfen yeniden giriş yapın.");
    throw new ApiHatasi("Oturum süresi doldu.", 401);
  }
  if (r.status === 403) throw new ApiHatasi("Bu işlem için yetkiniz yok.", 403);
  if (!r.ok) {
    let mesaj = `İstek başarısız (${r.status}).`;
    try {
      const p = await r.json();
      if (p.message) mesaj = p.message;
      else if (p.errors) mesaj = Object.values(p.errors).flat().join(" ");
      else if (typeof p === "string") mesaj = p;
    } catch { /* gövde yok veya düz metin */ }
    throw new ApiHatasi(mesaj, r.status);
  }
  const metin = r.status === 204 ? "" : await r.text();
  const veri = metin ? JSON.parse(metin) : null;
  return tam ? { veri, toplam: Number(r.headers.get("X-Total-Count") ?? 0) } : veri;
}

/** Kimlik doğrulamalı dosya indirme (CSV/Excel). */
async function indir(yol) {
  const oturum = Oturum.oku();
  const r = await fetch(yol, { headers: { Authorization: `Bearer ${oturum?.token}` } });
  if (!r.ok) throw new ApiHatasi(`Dosya indirilemedi (${r.status}).`, r.status);
  const ad = /filename=([^;]+)/i.exec(r.headers.get("Content-Disposition") ?? "")?.[1]?.replaceAll('"', "") ?? "rapor";
  const url = URL.createObjectURL(await r.blob());
  const a = h("a", { href: url, download: ad });
  document.body.append(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

// ---------------------------------------------------------------------------
// Bildirimler
// ---------------------------------------------------------------------------

let zamanlayici;
function duyur(tur, mesaj) {
  $("#hata").hidden = true;
  $("#basari").hidden = true;
  const el = $(tur === "hata" ? "#hata" : "#basari");
  el.textContent = mesaj;
  el.hidden = false;
  clearTimeout(zamanlayici);
  if (tur !== "hata") zamanlayici = setTimeout(() => { el.hidden = true; }, 5000);
}
const hataGoster = (e) => duyur("hata", e.message || String(e));

// ---------------------------------------------------------------------------
// Durum
// ---------------------------------------------------------------------------

const ROL_AD = { Admin: "Yönetici", Librarian: "Görevli" };
const ISLEM_AD = {
  GIRIS: "Giriş", GIRIS_BASARISIZ: "Başarısız giriş", KITAP_EKLE: "Kitap eklendi", KITAP_GUNCELLE: "Kitap güncellendi",
  KITAP_SIL: "Kitap silindi", ODUNC_VER: "Ödünç verildi", ODUNC_UZAT: "Ödünç uzatıldı", IADE_AL: "İade alındı",
  KULLANICI_OLUSTUR: "Kullanıcı eklendi", KULLANICI_PASIF: "Kullanıcı pasife alındı", KULLANICI_AKTIF: "Kullanıcı etkinleştirildi",
  DISA_AKTAR: "Dışa aktarıldı",
};
const MAX_UZATMA = 2; // sunucudaki varsayılan; kesin kural sunucudadır

const durum = {
  sekme: "kitaplar",
  ben: null,
  ozet: null,
  kitap: { ara: "", filtre: "hepsi", kategori: "", sirala: "title", sayfa: 1, boyut: 10, toplam: 0 },
  denetim: { sayfa: 1, boyut: 25, toplam: 0, islem: "", kullanici: "" },
};
const yonetici = () => durum.ben?.role === "Admin";

/** Bugünün başlangıcı: son teslim günü içinde iade zamanında sayılır (sunucuyla aynı kural). */
const bugun = () => { const d = new Date(); d.setHours(0, 0, 0, 0); return d; };
const gecikmis = (k) => k.isCheckedOut && k.dueDate && new Date(k.dueDate) < bugun();

// ---------------------------------------------------------------------------
// Özet kartları
// ---------------------------------------------------------------------------

const kart = (etiket, deger, sinif, not) => h("div", { class: "kart istatistik" },
  h("span", { class: "etiket" }, etiket), h("span", { class: `deger ${sinif}` }, String(deger)), h("span", { class: "not" }, not));

async function ozetYukle() {
  durum.ozet = await api("/api/reports/summary");
  const o = durum.ozet;
  $("#ozet").replaceChildren(
    kart("Toplam kitap", o.totalBooks, "d-ana", "envanterde"),
    kart("Rafta", o.available, "d-yesil", "ödünç verilebilir"),
    kart("Ödünçte", o.checkedOut, "d-sari", o.dueSoon ? `${o.dueSoon} tanesinin süresi 3 iş gününde doluyor` : "okuyucuda"),
    kart("Geciken", o.overdue, o.overdue ? "d-kirmizi" : "d-yesil", o.overdue ? "iade bekleniyor" : "gecikme yok"));
}

// ---------------------------------------------------------------------------
// Kitaplar
// ---------------------------------------------------------------------------

function sorguOlustur() {
  const k = durum.kitap;
  const [alan, yon] = k.sirala.split(":");
  const p = new URLSearchParams({ page: k.sayfa, pageSize: k.boyut, sort: alan });
  if (yon === "desc") p.set("desc", "true");
  if (k.ara.trim()) p.set("q", k.ara.trim());
  if (k.kategori) p.set("category", k.kategori);
  if (k.filtre === "musait") p.set("available", "true");
  if (k.filtre === "odunc") p.set("available", "false");
  if (k.filtre === "gecikmis") p.set("overdue", "true");
  return p;
}

async function kitaplariYukle() {
  const k = durum.kitap;
  try {
    let { veri, toplam } = await api(`/api/books?${sorguOlustur()}`, { tam: true });
    const sonSayfa = Math.max(1, Math.ceil(toplam / k.boyut));
    if (k.sayfa > sonSayfa) { // silme/süzgeç sonrası boş kalan sayfadan bir öncekine dön
      k.sayfa = sonSayfa;
      ({ veri, toplam } = await api(`/api/books?${sorguOlustur()}`, { tam: true }));
    }
    k.toplam = toplam;
    kitapTablosuCiz(veri);
  } catch (e) { hataGoster(e); }
}

async function kategorileriYukle() {
  try {
    const liste = await api("/api/books/categories");
    const secili = durum.kitap.kategori;
    $("#kategori").replaceChildren(h("option", { value: "" }, "Tüm kategoriler"),
      ...liste.map((c) => h("option", { value: c, selected: c === secili }, c)));
    $("#kategori-oneri")?.replaceChildren(...liste.map((c) => h("option", { value: c })));
  } catch { /* kategori listesi olmadan da çalışır */ }
}

function durumRozeti(k) {
  if (!k.isCheckedOut) return h("span", { class: "rozet r-musait" }, "Rafta");
  const uzatma = k.renewalCount ? ` · ${k.renewalCount}× uzatıldı` : "";
  return h("div", {},
    h("span", { class: `rozet ${gecikmis(k) ? "r-gecikmis" : "r-odunc"}` }, gecikmis(k) ? "Gecikmiş" : "Ödünçte"),
    h("span", { class: "alt-not" }, `${k.checkedOutBy} · iade: ${tarih(k.dueDate)}${uzatma}`));
}

function kitapTablosuCiz(liste) {
  $("#bos").hidden = liste.length > 0;
  $("#govde").replaceChildren(...liste.map((k) => h("tr", {},
    h("td", { class: "kitap" }, h("strong", {}, k.title), k.author ? h("span", { class: "alt-not" }, k.author) : null),
    h("td", {}, k.category ? h("span", { class: "etiket-kategori" }, k.category) : h("span", { class: "soluk" }, "–")),
    h("td", { class: "no-wrap" }, h("code", {}, k.isbn)),
    h("td", { class: "no-wrap" }, String(k.publicationYear)),
    h("td", { class: "no-wrap" }, para(k.price)),
    h("td", {}, durumRozeti(k)),
    h("td", { class: "eylemler" },
      k.isCheckedOut
        ? [h("button", { class: "dugme birincil kucuk", onclick: () => iadeDialogu(k) }, "İade al"),
          !gecikmis(k) && k.renewalCount < MAX_UZATMA
            ? h("button", { class: "dugme ikincil kucuk", onclick: () => uzatDialogu(k) }, "Uzat") : null]
        : h("button", { class: "dugme birincil kucuk", onclick: () => oduncDialogu(k) }, "Ödünç ver"),
      h("button", { class: "dugme ikincil kucuk", onclick: () => gecmisDialogu(k) }, "Geçmiş"),
      h("button", { class: "dugme ikincil kucuk", onclick: () => kitapDialogu(k) }, "Düzenle"),
      // Silme yalnızca Admin'e açıktır (sunucu da aynı kuralı uygular); görevli bu düğmeyi görmez.
      yonetici() ? h("button", { class: "dugme tehlike kucuk", disabled: k.isCheckedOut, title: k.isCheckedOut ? "Ödünçteki kitap silinemez" : "", onclick: () => silDialogu(k) }, "Sil") : null))));
  sayfalamaCiz($("#sayfalama"), durum.kitap, (n) => { durum.kitap.sayfa = n; kitaplariYukle(); });
}

function sayfalamaCiz(kap, d, git) {
  const son = Math.max(1, Math.ceil(d.toplam / d.boyut));
  if (d.toplam === 0) { kap.replaceChildren(); return; }
  const ilk = (d.sayfa - 1) * d.boyut + 1, bitis = Math.min(d.sayfa * d.boyut, d.toplam);
  kap.replaceChildren(
    h("span", { class: "soluk" }, `${ilk}–${bitis} / ${d.toplam}`),
    h("div", { class: "sayfa-dugmeleri" },
      h("button", { class: "dugme ikincil kucuk", disabled: d.sayfa <= 1, onclick: () => git(d.sayfa - 1) }, "‹ Önceki"),
      h("span", { class: "soluk" }, `Sayfa ${d.sayfa} / ${son}`),
      h("button", { class: "dugme ikincil kucuk", disabled: d.sayfa >= son, onclick: () => git(d.sayfa + 1) }, "Sonraki ›")));
}

let aramaZamanlayici;
$("#ara").addEventListener("input", (e) => {
  clearTimeout(aramaZamanlayici);
  aramaZamanlayici = setTimeout(() => { durum.kitap.ara = e.target.value; durum.kitap.sayfa = 1; kitaplariYukle(); }, 250);
});
$("#kategori").addEventListener("change", (e) => { durum.kitap.kategori = e.target.value; durum.kitap.sayfa = 1; kitaplariYukle(); });
$("#sirala").addEventListener("change", (e) => { durum.kitap.sirala = e.target.value; durum.kitap.sayfa = 1; kitaplariYukle(); });
$("#filtre").addEventListener("click", (e) => {
  const b = e.target.closest("button[data-f]");
  if (!b) return;
  durum.kitap.filtre = b.dataset.f;
  durum.kitap.sayfa = 1;
  document.querySelectorAll("#filtre button").forEach((x) => x.classList.toggle("aktif", x === b));
  kitaplariYukle();
});
document.addEventListener("click", async (e) => {
  const b = e.target.closest("button[data-indir]");
  if (!b) return;
  const [tur, bicim] = b.dataset.indir.split(":");
  b.disabled = true;
  try { await indir(`/api/reports/export/${tur}?format=${bicim}`); duyur("basari", "Dosya indirildi."); }
  catch (hata) { hataGoster(hata); }
  finally { b.disabled = false; }
});

async function tumunuYenile() {
  await Promise.all([ozetYukle().catch(hataGoster), kitaplariYukle(), kategorileriYukle()]);
  if (durum.sekme === "panel") await panelCiz();
  if (durum.sekme === "geciken") await gecikenCiz();
}

// ---------------------------------------------------------------------------
// Panel (raporlar)
// ---------------------------------------------------------------------------

const AY_AD = ["Oca", "Şub", "Mar", "Nis", "May", "Haz", "Tem", "Ağu", "Eyl", "Eki", "Kas", "Ara"];

function aylikGrafik(aylik) {
  const W = 520, H = 220, alt = 28, ust = 20, kolon = (W - 20) / aylik.length;
  const en = Math.max(1, ...aylik.flatMap((a) => [a.loans, a.returns]));
  const yuk = (n) => ((H - alt - ust) * n) / en;
  const parcalar = [s("line", { x1: 10, y1: H - alt, x2: W - 10, y2: H - alt, class: "g-cizgi" })];
  aylik.forEach((a, i) => {
    const x = 10 + i * kolon, y1 = yuk(a.loans), y2 = yuk(a.returns);
    parcalar.push(
      s("rect", { x: x + kolon * 0.12, y: H - alt - y1, width: kolon * 0.36, height: y1, rx: 3, class: "g-odunc" }),
      s("rect", { x: x + kolon * 0.52, y: H - alt - y2, width: kolon * 0.36, height: y2, rx: 3, class: "g-iade" }),
      a.loans ? s("text", { x: x + kolon * 0.3, y: H - alt - y1 - 4, "text-anchor": "middle", class: "g-deger" }, a.loans) : null,
      a.returns ? s("text", { x: x + kolon * 0.7, y: H - alt - y2 - 4, "text-anchor": "middle", class: "g-deger" }, a.returns) : null,
      s("text", { x: x + kolon / 2, y: H - 8, "text-anchor": "middle", class: "g-eksen" }, AY_AD[Number(a.month.slice(5)) - 1]));
  });
  return h("div", {},
    h("div", { class: "lejant" }, h("span", { class: "l-odunc" }, "Ödünç"), h("span", { class: "l-iade" }, "İade")),
    s("svg", { viewBox: `0 0 ${W} ${H}`, class: "grafik", role: "img", "aria-label": "Son altı ayın ödünç ve iade sayıları" }, parcalar));
}

function yatayCubuklar(satirlar, etiketAlani, degerAlani) {
  if (!satirlar.length) return h("p", { class: "soluk" }, "Veri yok.");
  const en = Math.max(1, ...satirlar.map((x) => x[degerAlani]));
  return h("ul", { class: "cubuklar" }, satirlar.map((x) => h("li", {},
    h("div", { class: "cubuk-ust" }, h("span", {}, x[etiketAlani]), h("strong", {}, String(x[degerAlani]))),
    h("div", { class: "cubuk-iz" }, h("div", { class: "cubuk-dolgu", "data-yuzde": String(Math.round((x[degerAlani] / en) * 100)) })))));
}

function cubuklariBoya(kap) {
  // CSP satır içi stile izin vermez; genişlik CSSOM ile atanır.
  kap.querySelectorAll(".cubuk-dolgu").forEach((e) => { e.style.width = `${e.dataset.yuzde}%`; });
}

async function panelCiz() {
  try {
    if (!durum.ozet) await ozetYukle();
    const o = durum.ozet;
    $("#panel-kartlar").replaceChildren(
      kart("Toplam ödünç", o.totalLoans, "d-ana", "bugüne kadar"),
      kart("Toplanan ceza", para(o.finesTotal), "d-sari", "tüm zamanlar"),
      kart("Bu ay ceza", para(o.finesThisMonth), "d-mavi", "bu ay iade edilenler"),
      kart("Süresi yaklaşan", o.dueSoon, o.dueSoon ? "d-sari" : "d-yesil", "3 iş günü içinde"));
    $("#grafik-aylik").replaceChildren(aylikGrafik(o.monthly));
    $("#liste-populer").replaceChildren(o.popular.length ? yatayCubuklar(o.popular, "title", "loans") : h("p", { class: "soluk" }, "Henüz ödünç verilmedi."));
    $("#grafik-kategori").replaceChildren(yatayCubuklar(o.categories, "category", "books"));
    cubuklariBoya($("#bolum-panel"));
  } catch (e) { hataGoster(e); }
}

// ---------------------------------------------------------------------------
// Geciken ödünçler
// ---------------------------------------------------------------------------

async function gecikenCiz() {
  try {
    const liste = await api("/api/reports/overdue");
    $("#bos-geciken").hidden = liste.length > 0;
    $("#govde-geciken").replaceChildren(...liste.map((g) => h("tr", {},
      h("td", { class: "kitap" }, h("strong", {}, g.title)),
      h("td", {}, g.borrower),
      h("td", { class: "no-wrap" }, tarih(g.dueDate)),
      h("td", { class: "no-wrap" }, h("span", { class: "rozet r-gecikmis" }, `${g.lateDays} iş günü`)),
      h("td", { class: "no-wrap" }, h("strong", { class: "d-kirmizi" }, para(g.fine))),
      h("td", { class: "eylemler" }, h("button", { class: "dugme birincil kucuk", onclick: () => iadeDialogu({ id: g.bookId, title: g.title, checkedOutBy: g.borrower, dueDate: g.dueDate }) }, "İade al")))));
  } catch (e) { hataGoster(e); }
}

// ---------------------------------------------------------------------------
// Denetim kaydı (yalnızca Admin)
// ---------------------------------------------------------------------------

async function denetimCiz() {
  const d = durum.denetim;
  try {
    const p = new URLSearchParams({ page: d.sayfa, pageSize: d.boyut });
    if (d.islem) p.set("action", d.islem);
    if (d.kullanici.trim()) p.set("user", d.kullanici.trim());
    const { veri, toplam } = await api(`/api/audit?${p}`, { tam: true });
    d.toplam = toplam;
    $("#bos-denetim").hidden = veri.length > 0;
    $("#govde-denetim").replaceChildren(...veri.map((k) => h("tr", {},
      h("td", { class: "no-wrap" }, tarihSaat(k.timestamp + (k.timestamp.endsWith("Z") ? "" : "Z"))),
      h("td", {}, k.username),
      h("td", {}, h("span", { class: `rozet ${k.action === "GIRIS_BASARISIZ" ? "r-gecikmis" : "r-odunc"}` }, ISLEM_AD[k.action] ?? k.action)),
      h("td", { class: "no-wrap" }, k.entityId ? `${k.entityType} #${k.entityId}` : k.entityType),
      h("td", { class: "soluk" }, k.details ?? "–"))));
    sayfalamaCiz($("#sayfalama-denetim"), d, (n) => { d.sayfa = n; denetimCiz(); });
  } catch (e) { hataGoster(e); }
}

function denetimSuzgeclerini_kur() {
  $("#denetim-islem").replaceChildren(h("option", { value: "" }, "Tüm işlemler"),
    ...Object.entries(ISLEM_AD).map(([k, ad]) => h("option", { value: k }, ad)));
  $("#denetim-islem").addEventListener("change", (e) => { durum.denetim.islem = e.target.value; durum.denetim.sayfa = 1; denetimCiz(); });
  let z;
  $("#denetim-kullanici").addEventListener("input", (e) => {
    clearTimeout(z);
    z = setTimeout(() => { durum.denetim.kullanici = e.target.value; durum.denetim.sayfa = 1; denetimCiz(); }, 250);
  });
}
denetimSuzgeclerini_kur();

// ---------------------------------------------------------------------------
// Sekmeler
// ---------------------------------------------------------------------------

async function sekmeAc(ad) {
  durum.sekme = ad;
  document.querySelectorAll("#sekmeler button").forEach((b) => b.classList.toggle("aktif", b.dataset.sekme === ad));
  for (const b of ["kitaplar", "panel", "geciken", "denetim"]) $(`#bolum-${b}`).hidden = b !== ad;
  $("#ozet").hidden = ad === "denetim" || ad === "panel";
  if (ad === "panel") await panelCiz();
  if (ad === "geciken") await gecikenCiz();
  if (ad === "denetim") await denetimCiz();
}
$("#sekmeler").addEventListener("click", (e) => {
  const b = e.target.closest("button[data-sekme]");
  if (b) sekmeAc(b.dataset.sekme);
});

// ---------------------------------------------------------------------------
// Pencereler
// ---------------------------------------------------------------------------

const dialog = $("#dialog");
dialog.addEventListener("click", (e) => { if (e.target === dialog) dialog.close(); });
const kapat = () => dialog.close();

/** Form penceresi: gönderirken düğmeyi kilitler, sunucu hatasını pencerede gösterir. */
function formPenceresi({ baslik, aciklama, alanlar, onay, tehlike = false, gonder }) {
  const hata = h("p", { class: "hata", role: "alert", hidden: true });
  const form = h("form", { class: "dialog-icerik", novalidate: true },
    h("h2", {}, baslik),
    aciklama ? h("p", { class: "ipucu-kutu" }, aciklama) : null,
    alanlar ? h("div", { class: "form-izgara" }, alanlar) : null,
    hata,
    h("div", { class: "dialog-alt" },
      h("button", { type: "button", class: "dugme ikincil", onclick: kapat }, "Vazgeç"),
      h("button", { type: "submit", class: `dugme ${tehlike ? "tehlike" : "birincil"}` }, onay)));
  form.addEventListener("submit", async (ev) => {
    ev.preventDefault();
    const dugme = $("[type=submit]", form);
    hata.hidden = true;
    dugme.disabled = true;
    try { await gonder(new FormData(form)); }
    catch (e) { hata.textContent = e.message; hata.hidden = false; }
    finally { dugme.disabled = false; }
  });
  $("#dialog-icerik").replaceChildren(form);
  dialog.showModal();
  const ilk = $("input", form);
  if (ilk) ilk.focus();
}

const alan = (etiket, girdi, tam = false) => h("label", { class: tam ? "tam" : "" }, etiket, girdi);

function kitapDialogu(k) {
  const yeni = !k;
  formPenceresi({
    baslik: yeni ? "Kitap ekle" : "Kitabı düzenle", onay: yeni ? "Ekle" : "Kaydet",
    alanlar: [
      alan("Başlık", h("input", { name: "title", required: true, maxlength: "300", value: k?.title ?? "" }), true),
      alan("Yazar", h("input", { name: "author", maxlength: "200", value: k?.author ?? "", autocomplete: "off" })),
      alan("Kategori", h("input", { name: "category", maxlength: "100", value: k?.category ?? "", list: "kategori-oneri", autocomplete: "off" })),
      h("datalist", { id: "kategori-oneri" }, [...$("#kategori").options].filter((o) => o.value).map((o) => h("option", { value: o.value }))),
      alan("ISBN", h("input", { name: "isbn", required: true, maxlength: "20", value: k?.isbn ?? "", placeholder: "978-..." })),
      alan("Yayın yılı", h("input", { name: "year", type: "number", min: "1000", max: "2100", required: true, value: k?.publicationYear ?? new Date().getFullYear() })),
      alan("Fiyat (₺)", h("input", { name: "price", type: "number", min: "0", step: "0.01", required: true, value: k?.price ?? "" })),
    ],
    gonder: async (f) => {
      const govde = {
        title: f.get("title").trim(), isbn: f.get("isbn").trim(), author: f.get("author").trim() || null,
        category: f.get("category").trim() || null, publicationYear: Number(f.get("year")), price: Number(f.get("price")),
      };
      if (!govde.title || !govde.isbn) throw new ApiHatasi("Başlık ve ISBN zorunludur.");
      if (yeni) await api("/api/books", { yontem: "POST", govde });
      else await api(`/api/books/${k.id}`, { yontem: "PUT", govde: { ...govde, id: k.id } });
      kapat();
      duyur("basari", yeni ? "Kitap eklendi." : "Kitap güncellendi.");
      await tumunuYenile();
    },
  });
}

function oduncDialogu(k) {
  formPenceresi({
    baslik: "Ödünç ver", onay: "Ödünç ver",
    aciklama: `${k.title} · teslim süresi 15 iş günüdür (hafta sonları ve resmî tatiller sayılmaz); en fazla ${MAX_UZATMA} kez uzatılabilir.`,
    alanlar: [
      alan("Ad soyad", h("input", { name: "name", required: true, maxlength: "100", autocomplete: "off" }), true),
      alan("Telefon (isteğe bağlı)", h("input", { name: "phone", maxlength: "20", inputmode: "tel", autocomplete: "off" })),
      alan("T.C. kimlik no", h("input", { name: "tckn", required: true, maxlength: "11", inputmode: "numeric", autocomplete: "off", placeholder: "11 haneli" })),
    ],
    gonder: async (f) => {
      const tckn = f.get("tckn").trim();
      if (!f.get("name").trim()) throw new ApiHatasi("Ad soyad zorunludur.");
      if (!/^\d{11}$/.test(tckn)) throw new ApiHatasi("T.C. kimlik numarası 11 haneli olmalıdır.");
      if (!tcknGecerli(tckn)) throw new ApiHatasi("Geçerli bir T.C. kimlik numarası giriniz.");
      await api(`/api/books/checkout/${k.id}`, { yontem: "POST", govde: { name: f.get("name").trim(), phoneNumber: f.get("phone").trim() || null, tckn } });
      kapat();
      duyur("basari", `“${k.title}” ödünç verildi.`);
      await tumunuYenile();
    },
  });
}

function uzatDialogu(k) {
  formPenceresi({
    baslik: "Ödüncü uzat", onay: "Uzat",
    aciklama: `${k.title} · ${k.checkedOutBy} · mevcut son teslim ${tarih(k.dueDate)}. Uzatma 7 iş günü ekler; ${MAX_UZATMA - k.renewalCount} uzatma hakkı kaldı.`,
    gonder: async () => {
      const s2 = await api(`/api/books/${k.id}/renew`, { yontem: "POST" });
      kapat();
      duyur("basari", `Yeni son teslim: ${tarih(s2.dueDate)} (kalan uzatma hakkı: ${s2.remainingRenewals}).`);
      await tumunuYenile();
    },
  });
}

async function iadeDialogu(k) {
  let onizleme;
  try { onizleme = await api(`/api/books/${k.id}/fine-preview`); } catch (e) { hataGoster(e); return; }
  const gun = onizleme.lateDays;
  formPenceresi({
    baslik: "İade al", onay: "İade al",
    aciklama: `${k.title} · ${k.checkedOutBy} · son teslim ${tarih(k.dueDate)}`,
    alanlar: [h("div", { class: "tam ceza" },
      h("span", { class: "soluk" }, gun ? `${gun} iş günü gecikme` : "Zamanında iade"),
      h("strong", { class: gun ? "d-kirmizi" : "d-yesil" }, para(onizleme.fine)))],
    gonder: async () => {
      const sonuc = await api(`/api/books/checkin/${k.id}`, { yontem: "POST" });
      kapat();
      duyur("basari", sonuc?.message ?? "Kitap iade alındı.");
      await tumunuYenile();
    },
  });
}

async function gecmisDialogu(k) {
  $("#dialog-icerik").replaceChildren(h("p", { class: "soluk" }, "Yükleniyor…"));
  dialog.showModal();
  try {
    const liste = await api(`/api/books/${k.id}/history`);
    $("#dialog-icerik").replaceChildren(
      h("h2", {}, "Ödünç geçmişi"), h("p", { class: "soluk" }, k.title),
      liste.length
        ? h("ul", { class: "gecmis" }, liste.map((g) => h("li", {},
            h("strong", {}, g.name),
            h("div", { class: "soluk" }, `${tarihSaat(g.checkOutDate)} → ${g.actualReturnDate ? tarihSaat(g.actualReturnDate) : "henüz iade edilmedi"}`),
            h("div", { class: "soluk" }, `Son teslim: ${tarih(g.dueDate)}${g.renewalCount ? ` · ${g.renewalCount} kez uzatıldı` : ""}`),
            g.lateDays ? h("div", { class: "d-kirmizi" }, `${g.lateDays} iş günü gecikme · ceza ${para(g.fineAmount ?? 0)}`) : null)))
        : h("p", { class: "soluk" }, "Bu kitap henüz ödünç verilmemiş."),
      h("div", { class: "dialog-alt" }, h("button", { class: "dugme ikincil", onclick: kapat }, "Kapat")));
  } catch (e) { kapat(); hataGoster(e); }
}

function silDialogu(k) {
  formPenceresi({
    baslik: "Kitabı sil", onay: "Sil", tehlike: true,
    aciklama: `“${k.title}” envanterden silinecek. Bu işlem geri alınamaz.`,
    gonder: async () => {
      await api(`/api/books/${k.id}`, { yontem: "DELETE" });
      kapat();
      duyur("basari", "Kitap silindi.");
      await tumunuYenile();
    },
  });
}

// ---------------------------------------------------------------------------
// Kullanıcı yönetimi (yalnızca Admin)
// ---------------------------------------------------------------------------

async function kullanicilarDialogu() {
  $("#dialog-icerik").replaceChildren(h("p", { class: "soluk" }, "Yükleniyor…"));
  if (!dialog.open) dialog.showModal();
  try {
    const liste = await api("/api/users");
    const degistir = async (u) => {
      try { await api(`/api/users/${u.id}/${u.isActive ? "deactivate" : "activate"}`, { yontem: "POST" }); await kullanicilarDialogu(); }
      catch (e) { hataGoster(e); }
    };
    $("#dialog-icerik").replaceChildren(
      h("h2", {}, "Kullanıcılar"),
      h("p", { class: "soluk" }, "Kayıt yoktur: hesapları yalnızca yönetici açar. Pasife alınan kullanıcı anında oturumunu kaybeder."),
      h("ul", { class: "gecmis" }, liste.map((u) => h("li", {},
        h("strong", {}, u.username), " ", h("span", { class: `rozet ${u.isActive ? "r-musait" : "r-gecikmis"}` }, u.isActive ? ROL_AD[u.role] ?? u.role : "Pasif"),
        h("div", { class: "eylemler" },
          u.username === durum.ben.username ? h("span", { class: "soluk" }, "(sen)")
            : h("button", { class: `dugme ${u.isActive ? "tehlike" : "ikincil"} kucuk`, onclick: () => degistir(u) }, u.isActive ? "Pasife al" : "Etkinleştir"))))),
      h("div", { class: "dialog-alt" },
        h("button", { class: "dugme ikincil", onclick: kapat }, "Kapat"),
        h("button", { class: "dugme birincil", onclick: kullaniciEkleDialogu }, "+ Kullanıcı ekle")));
  } catch (e) { kapat(); hataGoster(e); }
}

function kullaniciEkleDialogu() {
  formPenceresi({
    baslik: "Kullanıcı ekle", onay: "Ekle",
    aciklama: "Görevli kitap ekler/düzenler, ödünç verir, uzatır ve iade alır; kitap silemez. Yönetici ayrıca kitap siler, kullanıcıları yönetir ve denetim kaydını görür.",
    alanlar: [
      alan("Kullanıcı adı", h("input", { name: "username", required: true, minlength: "3", maxlength: "50", autocomplete: "off" }), true),
      alan("Parola (en az 8 karakter)", h("input", { name: "password", type: "password", required: true, minlength: "8", maxlength: "100", autocomplete: "new-password" }), true),
      alan("Rol", h("select", { name: "role" }, h("option", { value: "Librarian" }, "Görevli"), h("option", { value: "Admin" }, "Yönetici")), true),
    ],
    gonder: async (f) => {
      const username = f.get("username").trim(), password = f.get("password");
      if (username.length < 3) throw new ApiHatasi("Kullanıcı adı en az 3 karakter olmalıdır.");
      if (password.length < 8) throw new ApiHatasi("Parola en az 8 karakter olmalıdır.");
      await api("/api/users", { yontem: "POST", govde: { username, password, role: f.get("role") } });
      duyur("basari", "Kullanıcı eklendi.");
      await kullanicilarDialogu();
    },
  });
}

// ---------------------------------------------------------------------------
// Giriş / çıkış
// ---------------------------------------------------------------------------

function cikis(mesaj = "") {
  Oturum.sil();
  durum.ben = null;
  durum.ozet = null;
  if (dialog.open) dialog.close();
  $("#uygulama").hidden = true;
  $("#giris").hidden = false;
  const hata = $("#giris-hata");
  hata.textContent = mesaj;
  hata.hidden = !mesaj;
}

async function uygulamayiAc() {
  const o = Oturum.oku();
  durum.ben = { username: o.username, role: o.role };
  $("#kullanici").textContent = `${o.username} · ${ROL_AD[o.role] ?? o.role}`;
  $("#kullanicilar-ac").hidden = !yonetici();
  $("#sekme-denetim").hidden = !yonetici();
  $("#giris").hidden = true;
  $("#uygulama").hidden = false;
  await sekmeAc("kitaplar");
  await tumunuYenile();
}

$("#giris-form").addEventListener("submit", async (ev) => {
  ev.preventDefault();
  const f = ev.target, hata = $("#giris-hata"), dugme = $("#giris-gonder");
  hata.hidden = true;
  dugme.disabled = true;
  try {
    const y = await api("/api/auth/login", { yontem: "POST", govde: { username: f.username.value.trim(), password: f.password.value } });
    Oturum.yaz({ token: y.token, username: y.username, role: y.role });
    f.reset();
    await uygulamayiAc();
  } catch (e) {
    hata.textContent = e.durum === 429 ? "Çok fazla deneme. Biraz bekleyip tekrar deneyin." : e.message;
    hata.hidden = false;
  } finally { dugme.disabled = false; }
});

$("#cikis").addEventListener("click", () => cikis());
$("#kullanicilar-ac").addEventListener("click", kullanicilarDialogu);
$("#kitap-ekle").addEventListener("click", () => kitapDialogu(null));

// Açılışta: kayıtlı oturum varsa sunucuda doğrulanır (süresi dolmuş/pasif hesap için giriş ekranına döner).
if (Oturum.oku()?.token) {
  api("/api/auth/me").then((me) => { Oturum.yaz({ ...Oturum.oku(), username: me.username, role: me.role }); return uygulamayiAc(); }).catch(() => cikis());
} else {
  $("#giris").hidden = false;
}
