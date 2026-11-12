# BookTrack – Kütüphane Ödünç Takip API'si

.NET 8 (ASP.NET Core Web API), Entity Framework Core ve SQL Server ile geliştirilmiş kitap envanteri ve ödünç verme/iade takibi API'si.

> **Kapsam:** Bu repo yalnızca arka ucu (API) içerir; arayüz yoktur. API, Swagger üzerinden denenebilir.

## Özellikler
- Kitap CRUD (`/api/books`)
- **Ödünç verme** (`POST /api/books/checkout/{id}`): kitap zaten ödünçteyse reddedilir, teslim tarihi **15 iş günü** sonrası olarak hesaplanır (hafta sonları sayılmaz)
- **İade** (`POST /api/books/checkin/{id}`): gecikme iş günü sayısı üzerinden **günlük 5 ₺ ceza** hesaplanır
- Ödünç kaydı (CheckOut) geçmişi tutulur; girdiler doğrulanır (TCKN 11 hane, ad zorunlu)
- EF Core migration'ları, Swagger UI (geliştirme ortamında)

## Uç noktalar
| Yöntem | Yol | Açıklama |
|---|---|---|
| GET | `/api/books`, `/api/books/{id}` | Listele / getir |
| POST | `/api/books` | Kitap ekle |
| PUT | `/api/books/{id}` | Güncelle |
| DELETE | `/api/books/{id}` | Sil |
| POST | `/api/books/checkout/{id}` | Ödünç ver (gövde: ad, telefon, TCKN) |
| POST | `/api/books/checkin/{id}` | İade al, gecikme cezasını döndür |

## Çalıştırma
Gereksinimler: .NET 8 SDK ve SQL Server (varsayılan bağlantı: `(localdb)\mssqllocaldb`, Windows'ta Visual Studio ile gelir).
```bash
cd LibraryApp/LibraryApp
dotnet ef database update      # veritabanını oluşturur (dotnet tool install --global dotnet-ef)
dotnet run                     # Swagger: https://localhost:<port>/swagger
```
Farklı bir SQL Server için `ConnectionStrings:DefaultConnection` değerini değiştirin.

## Bilinen sınırlar
Kimlik doğrulama yok; otomatik test yok; resmi tatiller iş günü hesabında dikkate alınmaz. TCKN kişisel veridir: üretimde saklama/şifreleme politikası gerekir.

---
*Geliştirici: Emircan Kılıçaslan*