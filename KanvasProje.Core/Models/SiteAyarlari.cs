namespace KanvasProje.Core.Models
{
    public class SiteAyarlari
    {
        public int Id { get; set; } = 1;
        public string SiteAdi { get; set; } = "MeteorGaleri";
        public string MarkaAdi { get; set; } = "MeteorGaleri";
        public string SiteBasligi { get; set; } = "MeteorGaleri - Online Dekorasyon Mağazası";
        public string SiteAciklamasi { get; set; } = "Duvar dekorasyonu ve yaşam alanları için premium ürünler.";
        public string SiteLogoUrl { get; set; } = "/logo_svg.svg";
        public string FaviconUrl { get; set; } = "/favicon.ico";
        public string BaseUrl { get; set; } = "https://www.meteorgaleri.com";
        public string TemaRengi { get; set; } = "#C0392B";
        public string UstBarMesaji { get; set; } = "500 TL üzeri ücretsiz kargo";
        public string KampanyaMesaji { get; set; } = "Vade farksız 3 taksit";
        public bool UstBarEtkin { get; set; } = true;
        public double UstBarHizi { get; set; } = 34;
        public string FooterAciklamasi { get; set; } = "Premium duvar dekorasyonu ve özel tasarım ürünleri.";

        public string Telefon { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Adres { get; set; } = string.Empty;
        public string WhatsappNumarasi { get; set; } = string.Empty;
        public string CalismaSaatleri { get; set; } = string.Empty;

        public string FacebookUrl { get; set; } = string.Empty;
        public string InstagramUrl { get; set; } = string.Empty;
        public string TwitterUrl { get; set; } = string.Empty;
        public string YoutubeUrl { get; set; } = string.Empty;
        public string TiktokUrl { get; set; } = string.Empty;
        public string PinterestUrl { get; set; } = string.Empty;

        public string ParaBirimi { get; set; } = "TL";
        public decimal KargoBedeli { get; set; } = 0;
        public decimal UcretsizKargoLimiti { get; set; } = 500;
        public int StokUyariLimiti { get; set; } = 5;
        public bool StoktaYokSatisIzni { get; set; } = false;

        // Özel Ölçü Metrekare Birim Fiyatları
        public decimal HaliMetrekareFiyati { get; set; } = 1250;
        public decimal DuvarKagidiMetrekareFiyati { get; set; } = 450;

        public bool PaytrAktifMi { get; set; } = false;
        public bool PaytrTestModu { get; set; } = true;
        public string PaytrMerchantId { get; set; } = string.Empty;
        public string PaytrMerchantKeyProtected { get; set; } = string.Empty;
        public string PaytrMerchantSaltProtected { get; set; } = string.Empty;
        public string PaytrCallbackUrl { get; set; } = string.Empty;
        public string PaytrBasariliDonusUrl { get; set; } = string.Empty;
        public string PaytrBasarisizDonusUrl { get; set; } = string.Empty;

        // İyzico Ödeme Ayarları
        public bool IyzicoAktifMi { get; set; } = true;
        public bool IyzicoTestModu { get; set; } = true;
        public string IyzicoApiKey { get; set; } = "sandbox-placeholder-key";
        public string IyzicoSecretKeyProtected { get; set; } = string.Empty;
        public string IyzicoBaseUrl { get; set; } = "https://sandbox-api.iyzipay.com";
        public string IyzicoCallbackUrl { get; set; } = string.Empty;

        public string KargoFirmasi { get; set; } = "Aras Kargo";
        public string KargoTakipUrl { get; set; } = string.Empty;
        public int SiparisTeslimSuresiGun { get; set; } = 5;
        public int IadeHakkiGun { get; set; } = 14;

        public string MetaTitle { get; set; } = "MeteorGaleri - Premium Dekorasyon Ürünleri, Kanvas Tablo ve Duvar Sanatı";
        public string MetaDescription { get; set; } = "MeteorGaleri; kanvas tablo, cam tablo, duvar dekorasyonu ve yaşam alanlarına özel premium dekorasyon ürünleri sunar.";
        public string MetaKeywords { get; set; } = "kanvas tablo, cam tablo, duvar dekorasyonu, duvar sanatı, tablo, dekorasyon ürünleri, MeteorGaleri";
        public string GoogleAnalyticsId { get; set; } = string.Empty;
        public string FacebookPixelId { get; set; } = string.Empty;
        public string VarsayilanSosyalPaylasimGorseliUrl { get; set; } = "/EmailTemplates/meteorgaleri-logo.png";
        public string CookieMetni { get; set; } = "Deneyiminizi iyileştirmek, sepetinizi korumak ve site trafiğini analiz etmek için çerezler kullanıyoruz.";

        public bool YeniSiparisMailBildirimi { get; set; } = true;
        public bool StokUyarisiMailBildirimi { get; set; } = true;
        public bool IadeTalebiMailBildirimi { get; set; } = true;
        public string BildirimAliciEmail { get; set; } = string.Empty;

        public bool BakimModuAktif { get; set; } = false;
        public string BakimModuMesaji { get; set; } = "Size daha iyi bir alışveriş deneyimi sunmak için kısa bir bakım çalışması yapıyoruz. Çok yakında premium dekorasyon ürünlerimizle yeniden yayında olacağız.";
    }
}