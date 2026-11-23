// Demo verisi yükler: yazarlı ve kategorili örnek kitaplar, ödünç kayıtları, iade geçmişi ve isteğe bağlı bir görevli hesabı (Node 18+).
// Kullanım: ADMIN_USERNAME=... ADMIN_PASSWORD=... node scripts/demo-verisi.mjs [http://localhost:5210]
// (uygulamayı başlatırken Auth__AdminUsername / Auth__AdminPassword ile verdiğiniz değerler)
// Görevli hesabı için GOREVLI_PAROLA (en az 8 karakter) verilebilir.
// ISBN'ler ve T.C. kimlik numaraları örnek değerlerdir; gerçek yayın ve kişilere ait değildir.
const B = process.argv[2] || 'http://localhost:5210';
const KULLANICI = process.env.ADMIN_USERNAME, PAROLA = process.env.ADMIN_PASSWORD;
if (!KULLANICI || !PAROLA) { console.error('ADMIN_USERNAME ve ADMIN_PASSWORD gerekli (uygulamayı başlatırken verdiğiniz yönetici bilgileri).'); process.exit(1); }

let token = '';
async function api(yol, yontem = 'GET', govde) {
  const r = await fetch(B + yol, { method: yontem, headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: 'Bearer ' + token } : {}) }, body: govde ? JSON.stringify(govde) : undefined });
  const t = await r.text();
  if (!r.ok) throw new Error(`${yontem} ${yol} -> ${r.status} ${t.slice(0, 200)}`);
  return t ? JSON.parse(t) : null;
}
token = (await api('/api/auth/login', 'POST', { username: KULLANICI, password: PAROLA })).token;

// [başlık, yazar, kategori, yıl, fiyat]
const kitaplar = [
  ['Suç ve Ceza', 'Fyodor Dostoyevski', 'Dünya klasikleri', 1866, 185], ['Tutunamayanlar', 'Oğuz Atay', 'Türk edebiyatı', 1971, 240],
  ['Kürk Mantolu Madonna', 'Sabahattin Ali', 'Türk edebiyatı', 1943, 95], ['İnce Memed', 'Yaşar Kemal', 'Türk edebiyatı', 1955, 160],
  ['Saatleri Ayarlama Enstitüsü', 'Ahmet Hamdi Tanpınar', 'Türk edebiyatı', 1962, 175], ['Sefiller', 'Victor Hugo', 'Dünya klasikleri', 1862, 260],
  ['Simyacı', 'Paulo Coelho', 'Roman', 1988, 110], ['1984', 'George Orwell', 'Bilim kurgu', 1949, 120],
  ['Küçük Prens', 'Antoine de Saint-Exupéry', 'Çocuk', 1943, 80], ['Beyaz Diş', 'Jack London', 'Macera', 1906, 90],
  ['Yaprak Dökümü', 'Reşat Nuri Güntekin', 'Türk edebiyatı', 1930, 130], ['Çalıkuşu', 'Reşat Nuri Güntekin', 'Türk edebiyatı', 1922, 150],
  ['Dönüşüm', 'Franz Kafka', 'Dünya klasikleri', 1915, 70], ['Hayvan Çiftliği', 'George Orwell', 'Bilim kurgu', 1945, 85],
  ['Vakıf', 'Isaac Asimov', 'Bilim kurgu', 1951, 140], ['Dune', 'Frank Herbert', 'Bilim kurgu', 1965, 210],
  ['Sapiens', 'Yuval Noah Harari', 'Tarih', 2011, 230], ['Nutuk', 'Mustafa Kemal Atatürk', 'Tarih', 1927, 165],
  ['Türklerin Tarihi', 'Ahmet Refik', 'Tarih', 1924, 145], ['Osmanlı Tarihi', 'İlber Ortaylı', 'Tarih', 2006, 195],
  ['Temiz Kod', 'Robert C. Martin', 'Bilişim', 2008, 420], ['Pragmatik Programcı', 'Andrew Hunt', 'Bilişim', 1999, 390],
  ['Algoritmalara Giriş', 'Thomas H. Cormen', 'Bilişim', 1990, 650], ['Veri Yoğun Uygulamalar', 'Martin Kleppmann', 'Bilişim', 2017, 580],
  ['Kozmos', 'Carl Sagan', 'Bilim', 1980, 175], ['Zamanın Kısa Tarihi', 'Stephen Hawking', 'Bilim', 1988, 150],
  ['Bencil Gen', 'Richard Dawkins', 'Bilim', 1976, 160], ['Beyin Kuralları', 'John Medina', 'Bilim', 2008, 135],
  ['Dede Korkut Hikâyeleri', 'Anonim', 'Halk edebiyatı', 1500, 75], ['Karamazov Kardeşler', 'Fyodor Dostoyevski', 'Dünya klasikleri', 1880, 280],
  ['Anna Karenina', 'Lev Tolstoy', 'Dünya klasikleri', 1877, 270], ['Savaş ve Barış', 'Lev Tolstoy', 'Dünya klasikleri', 1869, 320],
  ['Madam Bovary', 'Gustave Flaubert', 'Dünya klasikleri', 1856, 150], ['Yüzyıllık Yalnızlık', 'Gabriel García Márquez', 'Roman', 1967, 200],
  ['Körlük', 'José Saramago', 'Roman', 1995, 170], ['Şeker Portakalı', 'José Mauro de Vasconcelos', 'Roman', 1968, 95],
];
const id = [];
for (const [i, [title, author, category, publicationYear, price]] of kitaplar.entries())
  id.push((await api('/api/books', 'POST', { title, author, category, isbn: `978-605-0000-${String(i + 1).padStart(3, '0')}-${i % 10}`, publicationYear, price })).id);

const okuyucular = [
  ['Ayşe Yılmaz', '05321112233', '10000000146'], ['Mehmet Kaya', '05334445566', '10000000214'], ['Zeynep Demir', null, '10000000382'],
  ['Can Öztürk', '05427778899', '10000000450'], ['Elif Şahin', null, '10000000528'], ['Burak Arslan', null, '10000000696'],
  ['Selin Aydın', '05301234567', '10000000764'], ['Deniz Çelik', null, '10000000832'],
];
// Ödünçte kalacaklar (ilk üçü, ekran görüntüsü iş akışında geriye alınarak geciken gösterilir)
for (const [k, o] of [[0, 0], [2, 1], [5, 2], [7, 3], [9, 4], [14, 6], [20, 7], [26, 0]])
  await api(`/api/books/checkout/${id[k]}`, 'POST', { name: okuyucular[o][0], phoneNumber: okuyucular[o][1], tckn: okuyucular[o][2] });
await api(`/api/books/${id[14]}/renew`, 'POST'); // bir ödünç uzatılır

// Geçmiş: ödünç verilip iade edilenler (rapor grafikleri ve "en çok ödünç alınanlar" için)
const gecmis = [3, 3, 3, 11, 11, 15, 15, 17, 18, 21, 22, 24, 25, 27, 28, 29, 30, 31];
for (const [j, k] of gecmis.entries()) {
  const o = okuyucular[j % okuyucular.length];
  await api(`/api/books/checkout/${id[k]}`, 'POST', { name: o[0], phoneNumber: o[1], tckn: o[2] });
  await api(`/api/books/checkin/${id[k]}`, 'POST');
}

if (process.env.GOREVLI_PAROLA)
  await api('/api/users', 'POST', { username: 'gorevli', password: process.env.GOREVLI_PAROLA, role: 'Librarian' }).catch(() => {});
console.log(`tamam: ${id.length} kitap`);
