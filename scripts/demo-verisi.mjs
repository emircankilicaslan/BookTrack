// Demo verisi yükler: örnek kitaplar ve bir kısmı için ödünç kayıtları (Node 18+).
// Kullanım: node scripts/demo-verisi.mjs [http://localhost:5210]
// ISBN'ler örnek değerlerdir; gerçek yayınlara ait değildir.
const B = process.argv[2] || 'http://localhost:5210';

async function api(yol, yontem = 'GET', govde) {
  const r = await fetch(B + yol, { method: yontem, headers: { 'Content-Type': 'application/json' }, body: govde ? JSON.stringify(govde) : undefined });
  const t = await r.text();
  if (!r.ok) throw new Error(`${yontem} ${yol} -> ${r.status} ${t.slice(0, 200)}`);
  return t ? JSON.parse(t) : null;
}

const kitaplar = [
  ['Suç ve Ceza', 1866, 185], ['Tutunamayanlar', 1971, 240], ['Kürk Mantolu Madonna', 1943, 95], ['İnce Memed', 1955, 160],
  ['Saatleri Ayarlama Enstitüsü', 1962, 175], ['Sefiller', 1862, 260], ['Simyacı', 1988, 110], ['1984', 1949, 120],
  ['Küçük Prens', 1943, 80], ['Beyaz Diş', 1906, 90], ['Yaprak Dökümü', 1930, 130], ['Çalıkuşu', 1922, 150],
  ['Dönüşüm', 1915, 70], ['Hayvan Çiftliği', 1945, 85],
];
const id = [];
for (const [i, [title, publicationYear, price]] of kitaplar.entries())
  id.push((await api('/api/books', 'POST', { title, isbn: `978-605-0000-${String(i + 1).padStart(3, '0')}-${i % 10}`, publicationYear, price })).id);

const okuyucular = [
  ['Ayşe Yılmaz', '05321112233', '10000000146'], ['Mehmet Kaya', '05334445566', '10000000278'], ['Zeynep Demir', null, '10000000392'],
  ['Can Öztürk', '05427778899', '10000000414'], ['Elif Şahin', null, '10000000536'],
];
for (const [k, [name, phoneNumber, tckn]] of [[0, okuyucular[0]], [2, okuyucular[1]], [5, okuyucular[2]], [7, okuyucular[3]], [9, okuyucular[4]]])
  await api(`/api/books/checkout/${id[k]}`, 'POST', { name, phoneNumber, tckn });

// Bir kitap ödünç verilip iade edilir (geçmişte kayıt oluşsun).
await api(`/api/books/checkout/${id[3]}`, 'POST', { name: 'Burak Arslan', phoneNumber: null, tckn: '10000000650' });
await api(`/api/books/checkin/${id[3]}`, 'POST');
console.log(`tamam: ${id.length} kitap`);
