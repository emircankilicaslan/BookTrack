# BookTrack – Kütüphane Ödünç Takip API'si

.NET 8 (ASP.NET Core Web API), Entity Framework Core ve SQL Server ile geliştirilmiş kitap envanteri ve ödünç verme/iade takibi API'si.

![CI](https://github.com/emircankilicaslan/BookTrack/actions/workflows/ci.yml/badge.svg) ![Lisans: MIT](https://img.shields.io/badge/Lisans-MIT-green.svg)

API ile birlikte gelen web arayüzü (`/`) katalog, ödünç verme, iade ve geçmiş işlemlerini tek ekrandan yapmayı sağlar. API ayrıca Swagger üzerinden denenebilir.

![Katalog ve durum özeti](docs/screenshots/arayuz-katalog.jpg)

## Web arayüzü
Dosyalar `LibraryApp/LibraryApp/wwwroot` altındadır (sade HTML, CSS ve JavaScript; derleme adımı ve harici bağımlılık yok).

- Üstte özet kartları: toplam, rafta, ödünçte ve geciken kitap sayısı ile birikmiş tahmini ceza
- Başlık veya ISBN ile arama; Tümü / Rafta / Ödünçte / Geciken filtresi
- Kitap ekleme, düzenleme, silme (ödünçteki kitap silinemez)
- Ödünç verme: ad, telefon, T.C. kimlik no doğrulaması; iade alırken gecikme günü ve ceza önceden gösterilir
- Kitap bazında ödünç geçmişi (kişisel veri göstermez)
- Mobilde de kullanılabilir, koyu tema desteği

Güvenlik: metinler `textContent` ile yazılır (`innerHTML` yok), sayfa `Content-Security-Policy` ile sunulur ve satır içi betik/stil kullanmaz.

| İade ve gecikme cezası | Ödünç verme |
|---|---|
| ![İade](docs/screenshots/arayuz-iade.jpg) | ![Ödünç ver](docs/screenshots/arayuz-odunc-ver.jpg) |

| Geciken filtresi | Ödünç geçmişi | Mobil |
|---|---|---|
| ![Geciken](docs/screenshots/arayuz-geciken.jpg) | ![Geçmiş](docs/screenshots/arayuz-gecmis.jpg) | <img src="docs/screenshots/arayuz-mobil.jpg" width="200" alt="Mobil görünüm"> |

## Özellikler
- Kitap CRUD (`/api/books`); girdiler doğrulanır (başlık ve ISBN zorunlu, başlık en fazla 300, ISBN en fazla 20 karakter, yayın yılı 1000–2100, fiyat ≥ 0). **ISBN benzersizdir**: aynı ISBN ile ikinci kitap eklenemez veya güncellenemez (409). **Ödünç geçmişi olan kitap silinemez** (409); kayıtlar `CheckOut` → `Book` yabancı anahtarıyla bağlıdır ve geçmiş korunur
- **Ödünç verme** (`POST /api/books/checkout/{id}`): kitap zaten ödünçteyse reddedilir, teslim tarihi **15 iş günü** sonrası olarak hesaplanır (hafta sonları sayılmaz)
- **İade** (`POST /api/books/checkin/{id}`): gecikme iş günü sayısı üzerinden **günlük 5 ₺ ceza** hesaplanır; yanıtta `lateDays` ve `fine` alanları da döner. Son teslim günü içindeki iade zamanında sayılır; hafta sonları ve resmi tatiller ne teslim süresine ne de cezaya dahil edilir
- Ödünç kaydı (CheckOut) geçmişi tutulur; girdiler doğrulanır (ad zorunlu, T.C. kimlik no 11 hane ve resmi sağlama algoritmasına uygun olmalı)
- EF Core migration'ları, Swagger UI (geliştirme ortamında)

## Uç noktalar
| Yöntem | Yol | Açıklama |
|---|---|---|
| GET | `/api/books`, `/api/books/{id}` | Listele / getir |
| POST | `/api/books` | Kitap ekle |
| PUT | `/api/books/{id}` | Güncelle |
| DELETE | `/api/books/{id}` | Sil |
| POST | `/api/books/checkout/{id}` | Ödünç ver (gövde: ad, telefon, TCKN) |
| POST | `/api/books/checkin/{id}` | İade al, gecikme günü ve cezayı döndür |
| GET | `/api/books/{id}/fine-preview` | İade edilirse şu an uygulanacak gecikme günü ve ceza (iade etmeden önce gösterilir) |
| GET | `/api/books/{id}/history` | Ödünç geçmişi (ad ve tarihler; TCKN ve telefon dönmez) |

## Çalıştırma
Gereksinimler: .NET 8 SDK ve SQL Server (varsayılan bağlantı: `(localdb)\mssqllocaldb`, Windows'ta Visual Studio ile gelir).
```bash
cd LibraryApp/LibraryApp
dotnet ef database update      # veritabanını oluşturur (dotnet tool install --global dotnet-ef)
dotnet run                     # Arayüz: http://localhost:5210  ·  Swagger: /swagger
```
Örnek kitap ve ödünç kayıtlarıyla denemek için uygulama çalışırken: `node scripts/demo-verisi.mjs` (Node 18+; ISBN'ler örnek değerlerdir).

```bash
dotnet test LibraryApp/LibraryApp.sln   # 63 test (SQL Server testleri BOOKTRACK_TEST_SQL tanımlı değilse atlanır)
```
**Resmi tatiller** `appsettings.json` içindeki `Library:Holidays` listesinden okunur (yyyy-MM-dd); 2026 tatilleri tanımlıdır. Listede olmayan bir yıl için tatiller iş günü sayılır, bu yüzden her yıl başında güncelleyin.

Farklı bir SQL Server için `ConnectionStrings:DefaultConnection` değerini değiştirin.

## Testler
xUnit ile 63 test: ödünç süresi ve gecikme hesabı (hafta sonu, resmi tatil ve sınır durumları, `LoanCalculator`), kitap doğrulaması, ödünç verme/iade akışı, gecikme cezası, ödünçteki ve geçmişi olan kitabın silinememesi, ISBN tekrarı, alan uzunlukları, T.C. kimlik no algoritması, geçmiş uç noktasının kişisel veri sızdırmaması ve arayüzün güvenlik başlıkları (`WebApplicationFactory` + bellek içi veritabanı). **Eş zamanlılık:** aynı kitap aynı anda birden fazla kişiye ödünç verilemez. `Book` kaydında SQL Server `rowversion` sütunu vardır; çakışan istekten yalnızca biri başarılı olur, diğeri 409 (veya kitap artık ödünçte olduğu için 400) alır ve reddedilen isteğin ödünç kaydı yazılmaz. Bu davranış, unique index, yabancı anahtar ve veri taşıyan migration bellek içi sağlayıcıyla üretilemediği için gerçek SQL Server'a karşı test edilir (9 test, her biri kendi geçici veritabanını kurar ve siler):
```powershell
$env:BOOKTRACK_TEST_SQL = 'Server=(localdb)mssqllocaldb;Integrated Security=true;TrustServerCertificate=True'
dotnet test LibraryApp/LibraryApp.sln
```
GitHub Actions her push ve pull request'te bir SQL Server servisiyle derleme ve tüm testleri çalıştırır.

## Bilinen sınırlar
Kimlik doğrulama yok (arayüz ve API herkese açıktır; yalnızca güvenli bir ağda çalıştırın); dini bayram tarihleri her yıl değiştiği için resmi tatil listesi yılbaşında güncellenmelidir. TCKN kişisel veridir: üretimde saklama/şifreleme politikası gerekir.

## Mevcut veritabanını güncelleme (`VeriButunlugu` migration'ı)
`dotnet ef database update` ISBN benzersizliği, `CheckOut` → `Book` yabancı anahtarı ve alan uzunluklarını (başlık 300, ISBN 20) ekler. Eski veriyle güvenli çalışır:
- **Kitabı silinmiş (yetim) ödünç kayıtları** silinmez; `CheckOuts_Yetim` tablosuna taşınır, gerekirse oradan incelenebilir.
- **Tekrar eden ISBN** veya **limiti aşan başlık/ISBN** varsa migration, düzeltme sorgusunu içeren açık bir hata ile durur ve hiçbir şey değiştirmez. Önce kayıtları düzeltin, sonra yeniden çalıştırın:
```sql
SELECT ISBN, COUNT(*) FROM Books GROUP BY ISBN HAVING COUNT(*) > 1;
SELECT Id, LEN(Title), LEN(ISBN) FROM Books WHERE LEN(Title) > 300 OR LEN(ISBN) > 20;
```
Not: TCKN algoritması yalnızca yeni ödünç işlemlerini denetler; mevcut kayıtlar değiştirilmez.

---
*Geliştirici: Emircan Kılıçaslan*