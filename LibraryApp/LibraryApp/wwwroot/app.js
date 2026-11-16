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

// T.C. kimlik no sağlama algoritması (sunucudaki TcknDogrulayici ile aynı kural; sunucu yine de denetler).
function tcknGecerli(t) {
  if (!/^[0-9]{11}$/.test(t) || t[0] === "0") return false;
  const d = [...t].map(Number);
  const tek = d[0] + d[2] + d[4] + d[6] + d[8], cift = d[1] + d[3] + d[5] + d[7];
  if (((tek * 7 - cift) % 10 + 10) % 10 !== d[9]) return false;
  return d.slice(0, 10).reduce((a, b) => a + b, 0) % 10 === d[10];
}
const tarih = (s) => (s ? new Date(s).toLocaleDateString("tr-TR", { day: "numeric", month: "short", year: "numeric" }) : "–");
const tarihSaat = (s) => (s ? new Date(s).toLocaleString("tr-TR", { dateStyle: "medium", timeStyle: "short" }) : "–");

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

async function api(yol, { yontem = "GET", govde } = {}) {
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

const durum = { kitaplar: [], filtre: "hepsi", ara: "", birikmisCeza: 0, ben: null };
const yonetici = () => durum.ben?.role === "Admin";
const ROL_AD = { Admin: "Yönetici", Librarian: "Görevli" };

const gecikmis = (k) => k.isCheckedOut && k.dueDate && new Date(k.dueDate) < new Date();
const turkceKucult = (s) => (s || "").toLocaleLowerCase("tr-TR");

async function yukle() {
  try {
    durum.kitaplar = await api("/api/books");
    // Gecikmiş kitapların cezası sunucuda hesaplanır (hafta sonu ve resmi tatiller orada bilinir).
    const gecikenler = durum.kitaplar.filter(gecikmis);
    const onizlemeler = await Promise.all(gecikenler.map((k) => api(`/api/books/${k.id}/fine-preview`).catch(() => null)));
    durum.birikmisCeza = onizlemeler.reduce((t, o) => t + (o?.fine ?? 0), 0);
    ciz();
  } catch (e) { hataGoster(e); }
}

function ozetCiz() {
  const k = durum.kitaplar;
  const oduncte = k.filter((x) => x.isCheckedOut).length;
  const geciken = k.filter(gecikmis);
  const kart = (etiket, deger, sinif, not) => h("div", { class: "kart istatistik" },
    h("span", { class: "etiket" }, etiket), h("span", { class: `deger ${sinif}` }, String(deger)), h("span", { class: "not" }, not));
  $("#ozet").replaceChildren(
    kart("Toplam kitap", k.length, "d-ana", "envanterde"),
    kart("Rafta", k.length - oduncte, "d-yesil", "ödünç verilebilir"),
    kart("Ödünçte", oduncte, "d-sari", "okuyucuda"),
    kart("Geciken", geciken.length, geciken.length ? "d-kirmizi" : "d-yesil", geciken.length ? `birikmiş ceza ${para(durum.birikmisCeza)}` : "gecikme yok"));
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
      // Silme yalnızca Admin'e açıktır (sunucu da aynı kuralı uygular); görevli bu düğmeyi görmez.
      yonetici() ? h("button", { class: "dugme tehlike kucuk", disabled: k.isCheckedOut, title: k.isCheckedOut ? "Ödünçteki kitap silinemez" : "", onclick: () => silDialogu(k) }, "Sil") : null))));
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
      alan("Başlık", h("input", { name: "title", required: true, maxlength: "300", value: k?.title ?? "" }), true),
      alan("ISBN", h("input", { name: "isbn", required: true, maxlength: "20", value: k?.isbn ?? "", placeholder: "978-..." })),
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
      if (!tcknGecerli(tckn)) throw new ApiHatasi("Geçerli bir T.C. kimlik numarası giriniz.");
      await api(`/api/books/checkout/${k.id}`, { yontem: "POST", govde: { name: f.get("name").trim(), phoneNumber: f.get("phone").trim() || null, tckn } });
      kapat();
      duyur("basari", `“${k.title}” ödünç verildi.`);
      await yukle();
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
    aciklama: "Görevli kitap ekler/düzenler, ödünç verir ve iade alır; kitap silemez. Yönetici ayrıca kitap siler ve kullanıcıları yönetir.",
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
  $("#giris").hidden = true;
  $("#uygulama").hidden = false;
  await yukle();
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
