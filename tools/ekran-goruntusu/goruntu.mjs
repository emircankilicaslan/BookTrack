// Çalışan uygulamadan README ekran görüntülerini alır (demo-verisi.mjs ve gecikme.sql'den sonra çalıştırılır).
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const BASE = process.env.BASE || 'http://localhost:5210';
const KULLANICI = process.env.ADMIN_USERNAME, PAROLA = process.env.ADMIN_PASSWORD;
if (!KULLANICI || !PAROLA) throw new Error('ADMIN_USERNAME ve ADMIN_PASSWORD tanımlı olmalı');

const klasor = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../docs/screenshots');
mkdirSync(klasor, { recursive: true });

const tarayici = await chromium.launch();

async function sayfaAc(secenek = {}) {
  const b = await tarayici.newContext({ viewport: { width: 1280, height: 800 }, locale: 'tr-TR', timezoneId: 'Europe/Istanbul', colorScheme: 'light', ...secenek });
  return b.newPage();
}

async function girisYap(s) {
  await s.goto(BASE + '/');
  await s.fill('input[name=username]', KULLANICI);
  await s.fill('input[name=password]', PAROLA);
  await s.click('#giris-gonder');
  await s.waitForSelector('#govde tr');
  await s.waitForLoadState('networkidle');
}

async function cek(s, ad, secenek = {}) {
  await s.screenshot({ path: path.join(klasor, ad + '.jpg'), type: 'jpeg', quality: 88, ...secenek });
  console.log('Kaydedildi:', ad);
}

/** Aramayla tek kitaba iner ve satırını döndürür. */
/** Tabloda tam n satır görünene kadar bekler (CSP 'unsafe-eval' vermediği için waitForFunction kullanılmaz). */
async function satirSayisi(s, n) {
  for (let i = 0; i < 60; i++) {
    if ((await s.locator('#govde tr').count()) === n) return;
    await s.waitForTimeout(200);
  }
  throw new Error('Tablo satır sayısı ' + n + ' olmadı');
}

/** Aramayla tek kitaba iner ve satırını döndürür. */
async function satir(s, baslik, arama = baslik) {
  await s.fill('#ara', arama);
  await satirSayisi(s, 1);
  await s.locator('#govde strong', { hasText: baslik }).first().waitFor();
  return s.locator('#govde tr').first();
}

async function sekme(s, ad, bekle) {
  await s.click(`#sekmeler button[data-sekme=${ad}]`);
  await s.waitForSelector(bekle);
  await s.waitForLoadState('networkidle');
}

// 1) Giriş
{
  const s = await sayfaAc({ viewport: { width: 1280, height: 640 } });
  await s.goto(BASE + '/');
  await s.waitForSelector('input[name=username]');
  await cek(s, 'arayuz-giris');
  await s.context().close();
}

// 2) Yönetici: katalog, arama/sıralama, pencereler, panel, geciken, denetim
{
  const s = await sayfaAc();
  await girisYap(s);
  await cek(s, 'arayuz-katalog', { fullPage: true });

  // Arama + sıralama + kategori
  await s.fill('#ara', 'dostoyevski');
  await s.selectOption('#sirala', 'year:desc');
  await satirSayisi(s, 2);
  await cek(s, 'arayuz-arama', { fullPage: true });
  await s.fill('#ara', '');
  await satirSayisi(s, 10);
  await s.selectOption('#sirala', 'title');

  // Geciken filtresi
  await s.click('#filtre button[data-f=gecikmis]');
  await satirSayisi(s, 3);
  await cek(s, 'arayuz-geciken', { fullPage: true });
  await s.click('#filtre button[data-f=hepsi]');
  await satirSayisi(s, 10);

  // İade (gecikmiş kitap: ceza gösterilir)
  await (await satir(s, 'Sefiller')).getByRole('button', { name: 'İade al' }).click();
  await s.waitForSelector('dialog[open] .ceza');
  await cek(s, 'arayuz-iade');
  await s.keyboard.press('Escape');

  // Uzatma
  await (await satir(s, 'Beyaz Diş')).getByRole('button', { name: 'Uzat' }).click();
  await s.waitForSelector('dialog[open]');
  await cek(s, 'arayuz-uzat');
  await s.keyboard.press('Escape');

  // Ödünç verme
  await (await satir(s, 'Sapiens')).getByRole('button', { name: 'Ödünç ver' }).click();
  await s.waitForSelector('dialog[open] input[name=name]');
  await s.fill('dialog input[name=name]', 'Ahmet Yıldız');
  await s.fill('dialog input[name=tckn]', '10000000146');
  await cek(s, 'arayuz-odunc-ver');
  await s.keyboard.press('Escape');

  // Geçmiş
  await (await satir(s, 'İnce Memed', 'Memed')).getByRole('button', { name: 'Geçmiş' }).click();
  await s.waitForSelector('dialog[open] .gecmis');
  await cek(s, 'arayuz-gecmis');
  await s.keyboard.press('Escape');
  await s.fill('#ara', '');

  // Panel
  await sekme(s, 'panel', '#grafik-aylik svg');
  await cek(s, 'arayuz-panel', { fullPage: true });

  // Geciken ödünçler
  await sekme(s, 'geciken', '#govde-geciken tr');
  await cek(s, 'arayuz-geciken-odunc', { fullPage: true });

  // Denetim kaydı
  await sekme(s, 'denetim', '#govde-denetim tr');
  await cek(s, 'arayuz-denetim', { fullPage: true });

  // Kullanıcı yönetimi
  await s.click('#kullanicilar-ac');
  await s.waitForSelector('dialog[open] .gecmis li');
  await cek(s, 'arayuz-kullanici-yonetimi');
  await s.context().close();
}

// 3) Mobil
{
  const s = await sayfaAc({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
  await girisYap(s);
  await cek(s, 'arayuz-mobil');
  await s.context().close();
}

// 4) Koyu tema (panel)
{
  const s = await sayfaAc({ colorScheme: 'dark' });
  await girisYap(s);
  await sekme(s, 'panel', '#grafik-aylik svg');
  await cek(s, 'arayuz-koyu-tema', { fullPage: true });
  await s.context().close();
}

// 5) Swagger
{
  const s = await sayfaAc({ viewport: { width: 1280, height: 1100 } });
  await s.goto(BASE + '/swagger');
  await s.waitForSelector('.swagger-ui .opblock-tag');
  await s.waitForLoadState('networkidle');
  await cek(s, 'swagger');
  await s.context().close();
}

await tarayici.close();
