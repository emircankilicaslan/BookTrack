"use strict";

// ---------------------------------------------------------------------------
// Yardımcılar
// ---------------------------------------------------------------------------

const $ = (s, k = document) => k.querySelector(s);

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

const para = (n) => Number(n).toLocaleString("tr-TR", { style: "currency", currency: "TRY" });
const tarih = (s) => (s ? new Date(s).toLocaleDateString("tr-TR", { day: "numeric", month: "short", year: "numeric" }) : "–");
const tarihSaat = (s) => (s ? new Date(s).toLocaleString("tr-TR", { dateStyle: "medium", timeStyle: "short" }) : "–");

/** Sunucudaki hesabın görüntü amaçlı kopyası: son tarihten bugüne gecikilen iş günü sayısı. */
function gecikenIsGunu(sonTarih, simdi = new Date()) {
  const bit = new Date(sonTarih);
  if (simdi <= bit) return 0;
  let g = 0;
  for (const d = new Date(bit); d < simdi; ) {
    d.setDate(d.getDate() + 1);
    if (d.getDay() !== 0 && d.getDay() !== 6) g++;
  }
  return g;
}
const GUNLUK_CEZA = 5;

class ApiHatasi extends Error {}

async function api(yol, { yontem = "GET", govde } = {}) {
  const r = await fetch(yol, {
    method: yontem,
    headers: govde !== undefined ? { "Content-Type": "application/json" } : {},
    body: govde !== undefined ? JSON.stringify(govde) : undefined,
  });
  if (!r.ok) {
    let mesaj = `İstek başarısız (${r.status}).`;
    try {
      const p = await r.json();
      if (p.message) mesaj = p.message;
      else if (p.errors) mesaj = Object.values(p.errors).flat().join(" ");
      else if (typeof p === "string") mesaj = p;
    } catch { /* gövde yok veya düz metin */ }
    throw new ApiHatasi(mesaj);
  }
  if (r.status === 204) return null;
  const metin = await r.text();
  return metin ? JSON.parse(metin) : null;
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
// Durum ve çizim
// ---------------------------------------------------------------------------

const durum = { kitaplar: [], filtre: "hepsi", ara: "" };

const gecikmis = (k) => k.isCheckedOut && k.dueDate && new Date(k.dueDate) < new Date();
const turkceKucult = (s) => (s || "").toLocaleLowerCase("tr-TR");

async function yukle() {
  try {
    durum.kitaplar = await api("/api/books");
    ciz();
  } catch (e) { hataGoster(e); }
}

function ozetCiz() {
  const k = durum.kitaplar;
  const oduncte = k.filter((x) => x.isCheckedOut).length;
  const geciken = k.filter(gecikmis);
  const ceza = geciken.reduce((t, x) => t + gecikenIsGunu(x.dueDate) * GUNLUK_CEZA, 0);
  const kart = (etiket, deger, sinif, not) => h("div", { class: "kart istatistik" },
    h("span", { class: "etiket" }, etiket), h("span", { class: `deger ${sinif}` }, String(deger)), h("span", { class: "not" }, not));
  $("#ozet").replaceChildren(
    kart("Toplam kitap", k.length, "d-ana", "envanterde"),
    kart("Rafta", k.length - oduncte, "d-yesil", "ödünç verilebilir"),
    kart("Ödünçte", oduncte, "d-sari", "okuyucuda"),
    kart("Geciken", geciken.length, geciken.length ? "d-kirmizi" : "d-yesil", geciken.length ? `birikmiş ceza ≈ ${para(ceza)}` : "gecikme yok"));
}

function durumRozeti(k) {
  if (!k.isCheckedOut) return h("span", { class: "rozet r-musait" }, "Rafta");
  const son = `iade: ${tarih(k.dueDate)}`;
  return h("div", {},
    h("span", { class: `rozet ${gecikmis(k) ? "r-gecikmis" : "r-odunc"}` }, gecikmis(k) ? "Gecikmiş" : "Ödünçte"),
    h("span", { class: "alt-not" }, `${k.checkedOutBy} · ${son}`));
}

function ciz() {
  ozetCiz();
  const q = turkceKucult(durum.ara);
  const liste = durum.kitaplar.filter((k) => {
    if (q && !turkceKucult(k.title).includes(q) && !turkceKucult(k.isbn).includes(q)) return false;
    if (durum.filtre === "musait") return !k.isCheckedOut;
    if (durum.filtre === "odunc") return k.isCheckedOut;
    if (durum.filtre === "gecikmis") return gecikmis(k);
    return true;
  });
  $("#bos").hidden = liste.length > 0;
  $("#govde").replaceChildren(...liste.map((k) => h("tr", {},
    h("td", { class: "kitap" }, h("strong", {}, k.title)),
    h("td", { class: "no-wrap" }, h("code", {}, k.isbn)),
    h("td", { class: "no-wrap" }, String(k.publicationYear)),
    h("td", { class: "no-wrap" }, para(k.price)),
    h("td", {}, durumRozeti(k)),
    h("td", { class: "eylemler" },
      k.isCheckedOut
        ? h("button", { class: "dugme birincil kucuk", onclick: () => iadeDialogu(k) }, "İade al")
        : h("button", { class: "dugme birincil kucuk", onclick: () => oduncDialogu(k) }, "Ödünç ver"),
      h("button", { class: "dugme ikincil kucuk", onclick: () => gecmisDialogu(k) }, "Geçmiş"),
      h("button", { class: "dugme ikincil kucuk", onclick: () => kitapDialogu(k) }, "Düzenle"),
      h("button", { class: "dugme tehlike kucuk", disabled: k.isCheckedOut, title: k.isCheckedOut ? "Ödünçteki kitap silinemez" : "", onclick: () => silDialogu(k) }, "Sil")))));
}

$("#ara").addEventListener("input", (e) => { durum.ara = e.target.value; ciz(); });
$("#filtre").addEventListener("click", (e) => {
  const b = e.target.closest("button[data-f]");
  if (!b) return;
  durum.filtre = b.dataset.f;
  document.querySelectorAll("#filtre button").forEach((x) => x.classList.toggle("aktif", x === b));
  ciz();
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
      alan("Başlık", h("input", { name: "title", required: true, value: k?.title ?? "" }), true),
      alan("ISBN", h("input", { name: "isbn", required: true, value: k?.isbn ?? "", placeholder: "978-..." })),
      alan("Yayın yılı", h("input", { name: "year", type: "number", min: "1000", max: "2100", required: true, value: k?.publicationYear ?? new Date().getFullYear() })),
      alan("Fiyat (₺)", h("input", { name: "price", type: "number", min: "0", step: "0.01", required: true, value: k?.price ?? "" })),
    ],
    gonder: async (f) => {
      const govde = { title: f.get("title").trim(), isbn: f.get("isbn").trim(), publicationYear: Number(f.get("year")), price: Number(f.get("price")) };
      if (!govde.title || !govde.isbn) throw new ApiHatasi("Başlık ve ISBN zorunludur.");
      if (yeni) await api("/api/books", { yontem: "POST", govde });
      else await api(`/api/books/${k.id}`, { yontem: "PUT", govde: { ...govde, id: k.id } });
      kapat();
      duyur("basari", yeni ? "Kitap eklendi." : "Kitap güncellendi.");
      await yukle();
    },
  });
}

function oduncDialogu(k) {
  formPenceresi({
    baslik: "Ödünç ver", onay: "Ödünç ver",
    aciklama: `${k.title} · teslim süresi 15 iş günüdür (hafta sonları sayılmaz).`,
    alanlar: [
      alan("Ad soyad", h("input", { name: "name", required: true, maxlength: "100", autocomplete: "off" }), true),
      alan("Telefon (isteğe bağlı)", h("input", { name: "phone", maxlength: "20", inputmode: "tel", autocomplete: "off" })),
      alan("T.C. kimlik no", h("input", { name: "tckn", required: true, maxlength: "11", inputmode: "numeric", autocomplete: "off", placeholder: "11 haneli" })),
    ],
    gonder: async (f) => {
      const tckn = f.get("tckn").trim();
      if (!f.get("name").trim()) throw new ApiHatasi("Ad soyad zorunludur.");
      if (!/^\d{11}$/.test(tckn)) throw new ApiHatasi("T.C. kimlik numarası 11 haneli olmalıdır.");
      await api(`/api/books/checkout/${k.id}`, { yontem: "POST", govde: { name: f.get("name").trim(), phoneNumber: f.get("phone").trim() || null, tckn } });
      kapat();
      duyur("basari", `“${k.title}” ödünç verildi.`);
      await yukle();
    },
  });
}

function iadeDialogu(k) {
  const gun = gecikenIsGunu(k.dueDate);
  formPenceresi({
    baslik: "İade al", onay: "İade al",
    aciklama: `${k.title} · ${k.checkedOutBy} · son teslim ${tarih(k.dueDate)}`,
    alanlar: [h("div", { class: "tam ceza" },
      h("span", { class: "soluk" }, gun ? `${gun} iş günü gecikme` : "Zamanında iade"),
      h("strong", { class: gun ? "d-kirmizi" : "d-yesil" }, para(gun * GUNLUK_CEZA)))],
    gonder: async () => {
      const s = await api(`/api/books/checkin/${k.id}`, { yontem: "POST" });
      kapat();
      duyur("basari", s?.message ?? "Kitap iade alındı.");
      await yukle();
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
            h("div", { class: "soluk" }, `Son teslim: ${tarih(g.dueDate)}`))))
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
      await yukle();
    },
  });
}

$("#kitap-ekle").addEventListener("click", () => kitapDialogu(null));
yukle();
