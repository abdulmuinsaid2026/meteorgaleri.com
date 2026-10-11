# METEORGALERİ (meteorgaleri.com) - Kapsamlı Proje Tanıtımı ve Devam Rehberi

Bu döküman, **MeteorGaleri** e-ticaret projesinin sıfırdan bugüne gelişimini, mimari kararlarını, tamamlanan ve bekleyen tüm adımlarını, ayrıca projeyi **başka bir bilgisayara veya yapay zeka modeline aktarırken** eksiksiz devam edebilmek için gereken tüm yönergeleri içerir.

---

## 1. Projenin Asıl Amacı ve Kapsamı

### 1.1. Arka Plan ve Amaç
* **Orijinal Sistem:** Proje, daha önce geliştirilen ve yayında olan **CANVASİA** e-ticaret platformu altyapısını kullanmaktadır.
* **White-Label Dönüşümü:** Müşteriye satılan yeni marka **MeteorGaleri** (`meteorgaleri.com`) adıyla bağımsız bir e-ticaret platformuna dönüştürülmüştür.
* **İş Modeli:** Kanvas tablo, temperli cam tablo, duvar kağıdı, cam kesme tahtası, dekoratif ayna, baskılı halı, ahşap dekorasyon ve lightbox gibi premium duvar sanatı ve dekorasyon ürünlerinin B2C online satışı.
* **Kritik Kural (Read-Only Mandate):** Orijinal CANVASİA Railway üretim veritabanına asla yazma/silme yapılmamış, sadece **READ-ONLY** okuma yapılarak 14.499 ürün, 223.856 seçenek, 34.519 görsel ve 5.143 yorum yerel veritabanına taşınmıştır.

### 1.2. Yeni Marka ve Görsel Kimlik (Retro / Vintage Aesthetic)
Projenin görsel kimliği "profesyonel, cıvıl cıvıl ve sıcak bir retro havası" verecek şekilde baştan aşağı yenilenmiştir:
* **Arka Plan (Body):** `#FFFBF0` (Sıcak krem rengi, göz yormayan nostaljik ton)
* **Yüzey / Kartlar (Surface):** `#FDF6E3` (Sıcak kum rengi)
* **Metin / Çerçeve Rengi (Ink):** `#1B2A4A` (Derin nostaljik lacivert)
* **Vurgu Rengi (Accent / Primary):** `#C0392B` (Retro kiremit / tuğla kırmızısı)
* **Derin Vurgu:** `#922B21` (Koyu retro kırmızı)
* **Açık Sınır Çizgileri:** `#F5E6C8` (Krem-bej sınır çizgileri)
* **Tipografi:** 
  * Başlıklar: **Playfair Display** (Zarif retro serif)
  * Gövde / UI: **Source Sans 3** (Okunaklı modern sans-serif)

---

## 2. Teknoloji Yığını ve Mimari Yapı

Proje **Clean Architecture** prensiplerine göre 4 katmanlı bir ASP.NET Core 8.0 MVC mimarisidir:

```
KanvasProje.sln
│
├── KanvasProje.Core         -> Varlıklar (Entities), DTO'lar, Arayüzler (Interfaces), Sabitler
├── KanvasProje.Data         -> KanvasDbContext, Entity Framework Core Migration'ları, Npgsql
├── KanvasProje.Service      -> İş Mantığı Servisleri, AutoMapper, SepetService, BrevoApiEmailService
└── KanvasProje.Web          -> MVC Controller'lar, Razor View'lar, Identity, Admin Area, wwwroot
```

### 2.1. Temel Bileşenler
1. **.NET 8.0 SDK / C# 12**
2. **PostgreSQL (Npgsql EF Core Provider):**
   * **ÖNEMLİ KURAL:** Tüm tablo ve kolon isimleri **Türkçe PascalCase** ve tırnak içinde tanımlıdır (`"Urunler"`, `"Kategoriler"`, `"UrunSecenekleri"`, `"Yorumlar"`, `"AspNetUsers"`).
   * Kolon adları standardı: Ürün başlığı `"Baslik"`tır (`Ad` veya `Name` değildir), resim yolu `"ResimYolu"`dur, kategori adı `"Ad"`dır.
3. **ASP.NET Core Identity:**
   * Özelleştirilmiş `AppUser` sınıfı (`AdSoyad`, `Sehir` zorunlu alanları içerir).
   * 30 günlük sliding cookie oturumu, 5 hatalı denemede lockout kilitlemesi.
   * Türkçe hata mesajları (`TurkceIdentityErrorDescriber`).
4. **Tailwind CSS (Standalone CLI):**
   * Renk paleti `KanvasProje.Web/tailwind.config.js` içinde `canvasia` nesnesi altında retro renklerle tanımlıdır.
   * Çıktı: `KanvasProje.Web/wwwroot/css/storefront.css`.
5. **Hangfire (Background Worker):**
   * PostgreSQL storage üzerinde çalışır. Terk edilmiş sepetlerin (`AbandonedCartService`) takibi ve temizlik görevleri için kullanılır.
6. **Brevo REST API (E-posta):**
   * Bulut sağlayıcılarında (Railway, Azure vb.) SMTP portları (587, 465) engellendiği için doğrudan `https://api.brevo.com/v3/smtp/email` HTTPS (Port 443) REST API ile gönderim yapar.
7. **İyzico Ödeme Entegrasyonu:**
   * `IyzicoPaymentService` (Şu anda sandbox modunda, canlı merchant bilgileri beklenmektedir).

---

## 3. Adım Adım Bugüne Kadar Neler Yapıldı?

### Adım 1: İzole Çalışma Alanı ve Yerel Veritabanı Kurulumu
1. Proje `E:\Projeler\MeteorGaleri` kaynağından `C:\meteorgalericom` bağımsız dizinine kopyalandı.
2. Yerel PostgreSQL üzerinde sıfır `meteorgaleridb` veritabanı açıldı.
3. Veritabanı şeması oluşturuldu (`__EFMigrationsHistory` ve tablolar).

### Adım 2: Veri Göçü (Data Migration - Read-Only)
Orijinal Railway veritabanına zarar vermeden aşağıdaki veriler yerel `meteorgaleridb`ye aktarıldı:
* **14.499** adet aktif Ürün (`Urunler`)
* **47** adet Kategori (`Kategoriler`)
* **34.519** adet Ürün Resmi (`UrunResimleri`)
* **223.856** adet Ürün Seçeneği, Ölçü ve Fiyatı (`UrunSecenekleri`) - *Postgres Identity sequence atlatılarak streaming COPY ile aktarıldı.*
* **5.143** adet Müşteri Yorumu (`Yorumlar`)
* Ana Sayfa Bölümleri, Slaytlar, Kargo Firmaları ve Kurumsal Sayfalar aktarıldı.

### Adım 3: Veri ve Marka Temizliği
1. Tüm eski kullanıcılar (`AspNetUsers`), eski müşteri siparişleri (`Siparisler`, `SiparisDetaylari`), sepetler (`Sepetler`, `SepetItems`), müşteri adresleri (`Adresler`) ve ziyaretçi logları sıfırlandı.
2. Müşteri yorumlarındaki ve kurumsal metinlerdeki tüm "Canvasia", "canvasia.store" ifadeleri SQL `REPLACE` ile "MeteorGaleri" olarak güncellendi.
3. `SiteAyarlari` tablosunda:
   * Site Adı & Marka Adı: `MeteorGaleri`
   * E-posta: `meteor_medya@hotmail.com`
   * Telefon: `+90 543 221 23 20`
   * Tema Rengi: `#C0392B`
   * Sosyal Medya: `https://www.instagram.com/meteorgaleri` olarak güncellendi.

### Adım 4: Retro Tema ve Görsel Dönüşüm
1. `tailwind.config.js` dosyası güncellendi:
   * Renk tokenları `#1B2A4A` (lacivert), `#C0392B` (retro kırmızı), `#FFFBF0` (krem), `#FDF6E3` (kum), `#F5E6C8` (sınır) olarak ayarlandı.
   * Fontlar `Playfair Display` ve `Source Sans 3` olarak tanımlandı.
2. `KanvasProje.Web/Views/Shared/_Layout.cshtml` güncellendi:
   * Google Fonts bağlantısı eklendi.
   * Header arka planı `#FFFBF0`, çerçeveler `#F5E6C8`, kayan bant (ticker) ve footer butonları retro kırmızı `#C0392B` yapıldı.
   * Telif hakkı `METEORGALERİ` olarak değiştirildi.
3. Kurumsal sayfalar (`Hakkimizda.cshtml`, `Gizlilik.cshtml`, `KullaniciSozlesmesi.cshtml`, `MesafeliSatis.cshtml`) ve giriş sayfaları retro stiline uyarlandı.
4. Vektör görseller oluşturuldu:
   * `logo_svg.svg` ve `yeni_canvasia_logo.svg`: Retro çerçeveli, 66px Playfair Display fontlu `METEORGALERİ` logosu.
   * `favicon.svg`: Lacivert zemin üzerine krem 'M' harfi ve retro kırmızı taban çizgisi.

### Adım 5: Kod Hataları ve İyileştirmeler
1. **BrevoApiEmailService.cs:** HTML template içindeki string interpolation çift tırnak çakışması (CS1002/CS1525 derleme hatası) giderildi.
2. **Program.cs (Google OAuth Guard):** `ClientId` ve `ClientSecret` tanımlı olmadığında uygulamanın çökmesi engellendi (`if (!string.IsNullOrWhiteSpace(...))` kontrolü eklendi).
3. **Hangfire PostgreSQL Kolon Çakışması:** `hangfire.lock.updatecount` migration çakışması `hangfire` şeması temizlenerek giderildi.

### Adım 6: Yetkilendirme ve Canlı Testler
1. **Sistem Yöneticisi (Admin) Hesabı:**
   * E-posta: `meteor_medya@hotmail.com`
   * Şifre: `MeteorAdmin2026-37!`
   * Headless Playwright ile `/Hesap/GirisYap` üzerinden giriş test edildi. `/Admin` yönetim paneline başarıyla erişildi, 14.499 ürünün listelendiği doğrulandı.
2. **Test Müşteri Hesabı:**
   * E-posta: `musteri@meteorgaleri.com`
   * Şifre: `MeteorUser2024!`
   * Ad Soyad: Ahmet Yılmaz (İstanbul)
   * `/Hesap/KayitOl` ile kayıt olundu, e-posta onaylandı, `/Profil` hesabı başarıyla görüntülendi.

### Adım 7: Git ve GitHub Entegrasyonu
1. `https://github.com/abdulmuinsaid2026/meteorgalericom.git` reposu yapılandırıldı.
2. Hassas dosyalar, büyük loglar ve geçici scriptler `.gitignore` ile dışlandı.
3. Temiz veritabanı yedeği `meteorgaleridb_clean.dump` (6.9 MB sıkıştırılmış) oluşturuldu.
4. Tüm kaynak kodlar `master` branch'ine pushlandı.

---

## 4. Neler Yapılmadı? Neler Kaldı? (Bekleyen İşler)

1. **Özel Grafik / Banner Varlıkları:**
   * Kullanıcı tarafından tasarlanacak nihai SVG/PNG logolar ve ana sayfa slider banner'ları `wwwroot` altına eklenecek.
2. **Canlı Ödeme Sağlayıcı Anahtarları:**
   * Müşteri İyzico veya PayTR canlı hesap anahtarlarını aldığında `appsettings.json` veya Railway environment variables içerisine girilecek:
     * `PaymentSettings:Iyzico:ApiKey`
     * `PaymentSettings:Iyzico:SecretKey`
     * `PaymentSettings:Iyzico:BaseUrl` (Canlı: `https://api.iyzipay.com`)
3. **Canlı E-posta Gönderimi (Brevo API Key):**
   * `EmailSettings:Password` alanına canlı Brevo API Key tanımlanacak (`xkeysib-...`).
4. **Sosyal Medya Girişleri (Opsiyonel):**
   * Google Cloud Console ve Facebook Developer üzerinden `https://www.meteorgaleri.com/signin-google` yönlendirme URI'si ile canlı anahtarlar oluşturulup eklenecek.
5. **Railway Production Dağıtımı:**
   * Yeni bir Railway projesi açılacak, managed PostgreSQL kurulacak ve web uygulaması canlıya alınacak.
6. **Domain & SSL Yapılandırması:**
   * `meteorgaleri.com` ve `www.meteorgaleri.com` alan adları DNS üzerinden Railway CNAME adresine yönlendirilecek.

---

## 5. Başka Bilgisayarda Projeyi Devam Ettirme Rehberi

Projeyi yeni bir bilgisayara kurup yapay zekayla geliştirmeye devam etmek için aşağıdaki adımları sırasıyla uygulayın:

### 1. Adım: Projeyi Klonlayın
```bash
git clone https://github.com/abdulmuinsaid2026/meteorgalericom.git
cd meteorgalericom
```

### 2. Adım: Yerel PostgreSQL Veritabanını Kurun ve Yedeği Geri Yükleyin
Bilgisayarınızda PostgreSQL (v14, v15 veya v16) kurulu olmalıdır:
```bash
# 1. meteorgaleridb adında veritabanı oluşturun:
createdb -U postgres meteorgaleridb

# 2. Proje kök dizinindeki 6.9 MB'lık temiz yedeği restore edin:
pg_restore -U postgres -d meteorgaleridb -v meteorgaleridb_clean.dump
```
*(Eğer pg_restore şema veya tablo uyarıları verirse normaldir; tüm veriler eksiksiz yüklenir).*

### 3. Adım: Yerel Ayarları Yapılandırın (secrets.json)
`KanvasProje.Web` klasörü içinde bir `secrets.json` oluşturun veya `appsettings.Development.json` dosyasını düzenleyin:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=meteorgaleridb;Username=postgres;Password=SIZIN_POSTGRES_SIFRENIZ"
  },
  "AdminSettings": {
    "SeedDefaultAdmin": true,
    "Email": "meteor_medya@hotmail.com",
    "Password": "MeteorAdmin2026-37!"
  },
  "EmailSettings": {
    "FromEmail": "meteor_medya@hotmail.com",
    "FromName": "MeteorGaleri",
    "Password": ""
  }
}
```

### 4. Adım: Projeyi Derleyin ve Çalıştırın
```bash
# Bağımlılıkları geri yükleyin ve derleyin:
dotnet build KanvasProje.sln

# Web uygulamasını başlatın:
cd KanvasProje.Web
dotnet watch run
```
* Uygulama varsayılan olarak `http://localhost:5002` veya `https://localhost:5001` portunda ayağa kalkacaktır.

### 5. Adım: Giriş Bilgileriyle Test Edin
* **Yönetici Girişi:** `http://localhost:5002/Hesap/GirisYap`
  * E-posta: `meteor_medya@hotmail.com`
  * Şifre: `MeteorAdmin2026-37!`
  * Panel URL: `http://localhost:5002/Admin`
* **Müşteri Girişi:**
  * E-posta: `musteri@meteorgaleri.com`
  * Şifre: `MeteorUser2024!`
  * Profil URL: `http://localhost:5002/Profil`

---

## 6. Railway Canlı Yayına Alma Rehberi (Production Deployment)

MeteorGaleri'yi canlıya taşımak için izlenecek kesin adımlar:

1. **Railway Projesi Oluşturma:**
   * [Railway.app](https://railway.app) üzerinde **New Project** açın.
   * **Provision PostgreSQL** seçeneğiyle bir managed Postgres veritabanı ekleyin.
2. **Veritabanı Yedeğini Railway'e Aktarma:**
   * Railway Postgres dashboard'undan `DATABASE_URL` veya Connection Parameters (Host, Port, User, Password) bilgilerini alın.
   * Yerel terminalinizden şu komutu çalıştırın:
   ```bash
   pg_restore -h <RAILWAY_HOST> -p <RAILWAY_PORT> -U postgres -d railway -v meteorgaleridb_clean.dump
   ```
3. **Web Servisini Bağlama:**
   * Railway üzerinde **New Service -> GitHub Repo** seçin.
   * `abdulmuinsaid2026/meteorgalericom` reposunu seçin.
4. **Environment Variables (Ortam Değişkenleri):**
   Railway Web servisine şu değişkenleri tanımlayın:
   * `ConnectionStrings__DefaultConnection`: `${{Postgres.DATABASE_URL}}`
   * `AppUrl`: `https://www.meteorgaleri.com`
   * `ASPNETCORE_ENVIRONMENT`: `Production`
   * `DOTNET_RUNNING_IN_CONTAINER`: `true`
   * `EmailSettings__FromEmail`: `meteor_medya@hotmail.com`
   * `EmailSettings__FromName`: `MeteorGaleri`
   * `EmailSettings__Password`: `<BREVO_API_KEY>`
   * `PaymentSettings__Iyzico__ApiKey`: `<IYZICO_API_KEY>`
   * `PaymentSettings__Iyzico__SecretKey`: `<IYZICO_SECRET_KEY>`
   * `PaymentSettings__Iyzico__BaseUrl`: `https://api.iyzipay.com`
5. **Özel Alan Adı (Custom Domain):**
   * Railway Settings -> Networking -> Custom Domain: `meteorgaleri.com` ve `www.meteorgaleri.com` ekleyin.
   * Alan adı sağlayıcınızda (GoDaddy, Natro, Turhost vb.) verilen DNS CNAME kayıtlarını girin.

---

## 7. Yeni Yapay Zeka Oturumu İçin Hazır Başlangıç Promptu

Yeni bilgisayarda veya modelde çalışmaya başladığınızda aşağıdaki promptu doğrudan yapıştırabilirsiniz:

```text
Selam, MeteorGaleri (meteorgaleri.com) projesine devam ediyoruz.
Proje: ASP.NET Core 8.0 MVC + PostgreSQL Clean Architecture tabanlı, CANVASİA'dan white-label dönüştürülen kanvas/cam tablo e-ticaret platformudur.

DETAYLI PROJE DÖKÜMANI: proje_tanitimi.md dosyasında eksiksiz olarak mevcuttur.
GITHUB REPO: https://github.com/abdulmuinsaid2026/meteorgalericom (master branch)
TEMİZ DB DUMP: meteorgaleridb_clean.dump (14.499 ürün, 223.856 seçenek, 5.143 yorum hazır)
TEMA: Retro / Vintage (#FFFBF0 krem, #1B2A4A lacivert, #C0392B kiremit kırmızısı)
TEST HESAPLARI:
- Admin: meteor_medya@hotmail.com / MeteorAdmin2026-37!
- Müşteri: musteri@meteorgaleri.com / MeteorUser2024!

Kaldığımız yerden devam etmek için proje_tanitimi.md dosyasını incele ve sıradaki adımları yürütelim.
```
