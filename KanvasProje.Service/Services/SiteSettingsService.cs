using System.Text.Json;
using KanvasProje.Core.Models;
using KanvasProje.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace KanvasProje.Service.Services
{
    public interface ISiteSettingsService
    {
        SiteAyarlari GetSettings();
        void SaveSettings(SiteAyarlari settings);
        void SaveSettings(SiteAyarlari settings, string? paytrMerchantKey, string? paytrMerchantSalt);
        void SaveSettings(SiteAyarlari settings, string? paytrMerchantKey, string? paytrMerchantSalt, string? iyzicoSecretKey);
        bool HasPaytrMerchantKey();
        bool HasPaytrMerchantSalt();
        bool HasIyzicoSecretKey();
        string BuildAbsoluteUrl(string? path);
    }

    public class SiteSettingsService : ISiteSettingsService
    {
        private const string CacheKey = "site-settings";
        private const string PaytrProtectorPurpose = "Canvasia.PayTR.Settings.v1";
        private const string IyzicoProtectorPurpose = "MeteorGaleri.Iyzico.Settings.v1";

        private readonly KanvasDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly IDataProtector _paytrProtector;
        private readonly IDataProtector _iyzicoProtector;
        private readonly JsonSerializerOptions _serializerOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public SiteSettingsService(KanvasDbContext context, IMemoryCache cache, IDataProtectionProvider dataProtectionProvider)
        {
            _context = context;
            _cache = cache;
            _paytrProtector = dataProtectionProvider.CreateProtector(PaytrProtectorPurpose);
            _iyzicoProtector = dataProtectionProvider.CreateProtector(IyzicoProtectorPurpose);
        }

        public SiteAyarlari GetSettings()
        {
            return _cache.GetOrCreate(CacheKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                return LoadSettings();
            })!;
        }

        public void SaveSettings(SiteAyarlari settings)
        {
            SaveSettings(settings, null, null, null);
        }

        public void SaveSettings(SiteAyarlari settings, string? paytrMerchantKey, string? paytrMerchantSalt)
        {
            SaveSettings(settings, paytrMerchantKey, paytrMerchantSalt, null);
        }

        public void SaveSettings(SiteAyarlari settings, string? paytrMerchantKey, string? paytrMerchantSalt, string? iyzicoSecretKey)
        {
            var normalized = NormalizeSettings(settings);

            var existing = _context.SiteAyarlari.FirstOrDefault();
            if (existing != null)
            {
                existing.SiteAdi = normalized.SiteAdi;
                existing.MarkaAdi = normalized.MarkaAdi;
                existing.SiteBasligi = normalized.SiteBasligi;
                existing.SiteAciklamasi = normalized.SiteAciklamasi;
                existing.SiteLogoUrl = normalized.SiteLogoUrl;
                existing.FaviconUrl = normalized.FaviconUrl;
                existing.BaseUrl = normalized.BaseUrl;
                existing.TemaRengi = normalized.TemaRengi;
                existing.UstBarMesaji = normalized.UstBarMesaji;
                existing.KampanyaMesaji = normalized.KampanyaMesaji;
                existing.UstBarEtkin = normalized.UstBarEtkin;
                existing.UstBarHizi = normalized.UstBarHizi;
                existing.FooterAciklamasi = normalized.FooterAciklamasi;
                existing.Telefon = normalized.Telefon;
                existing.Email = normalized.Email;
                existing.Adres = normalized.Adres;
                existing.WhatsappNumarasi = normalized.WhatsappNumarasi;
                existing.CalismaSaatleri = normalized.CalismaSaatleri;
                existing.FacebookUrl = normalized.FacebookUrl;
                existing.InstagramUrl = normalized.InstagramUrl;
                existing.TwitterUrl = normalized.TwitterUrl;
                existing.YoutubeUrl = normalized.YoutubeUrl;
                existing.TiktokUrl = normalized.TiktokUrl;
                existing.PinterestUrl = normalized.PinterestUrl;
                existing.ParaBirimi = normalized.ParaBirimi;
                existing.KargoBedeli = normalized.KargoBedeli;
                existing.UcretsizKargoLimiti = normalized.UcretsizKargoLimiti;
                existing.StokUyariLimiti = normalized.StokUyariLimiti;
                existing.StoktaYokSatisIzni = normalized.StoktaYokSatisIzni;
                existing.HaliMetrekareFiyati = normalized.HaliMetrekareFiyati;
                existing.DuvarKagidiMetrekareFiyati = normalized.DuvarKagidiMetrekareFiyati;
                existing.PaytrAktifMi = normalized.PaytrAktifMi;
                existing.PaytrTestModu = normalized.PaytrTestModu;
                existing.PaytrMerchantId = normalized.PaytrMerchantId;
                existing.PaytrCallbackUrl = normalized.PaytrCallbackUrl;
                existing.PaytrBasariliDonusUrl = normalized.PaytrBasariliDonusUrl;
                existing.PaytrBasarisizDonusUrl = normalized.PaytrBasarisizDonusUrl;
                ApplyPaytrSecrets(existing, paytrMerchantKey, paytrMerchantSalt);
                
                // Iyzico Ayarları
                existing.IyzicoAktifMi = normalized.IyzicoAktifMi;
                existing.IyzicoTestModu = normalized.IyzicoTestModu;
                existing.IyzicoApiKey = normalized.IyzicoApiKey;
                existing.IyzicoBaseUrl = normalized.IyzicoBaseUrl;
                existing.IyzicoCallbackUrl = normalized.IyzicoCallbackUrl;
                if (!string.IsNullOrWhiteSpace(iyzicoSecretKey))
                {
                    existing.IyzicoSecretKeyProtected = _iyzicoProtector.Protect(iyzicoSecretKey.Trim());
                }

                existing.KargoFirmasi = normalized.KargoFirmasi;
                existing.KargoTakipUrl = normalized.KargoTakipUrl;
                existing.SiparisTeslimSuresiGun = normalized.SiparisTeslimSuresiGun;
                existing.IadeHakkiGun = normalized.IadeHakkiGun;
                existing.MetaTitle = normalized.MetaTitle;
                existing.MetaDescription = normalized.MetaDescription;
                existing.MetaKeywords = normalized.MetaKeywords;
                existing.GoogleAnalyticsId = normalized.GoogleAnalyticsId;
                existing.FacebookPixelId = normalized.FacebookPixelId;
                existing.VarsayilanSosyalPaylasimGorseliUrl = normalized.VarsayilanSosyalPaylasimGorseliUrl;
                existing.CookieMetni = normalized.CookieMetni;
                existing.YeniSiparisMailBildirimi = normalized.YeniSiparisMailBildirimi;
                existing.StokUyarisiMailBildirimi = normalized.StokUyarisiMailBildirimi;
                existing.IadeTalebiMailBildirimi = normalized.IadeTalebiMailBildirimi;
                existing.BildirimAliciEmail = normalized.BildirimAliciEmail;
                existing.BakimModuAktif = normalized.BakimModuAktif;
                existing.BakimModuMesaji = normalized.BakimModuMesaji;
            }
            else
            {
                normalized.Id = 1;
                ApplyPaytrSecrets(normalized, paytrMerchantKey, paytrMerchantSalt);
                if (!string.IsNullOrWhiteSpace(iyzicoSecretKey))
                {
                    normalized.IyzicoSecretKeyProtected = _iyzicoProtector.Protect(iyzicoSecretKey.Trim());
                }
                _context.SiteAyarlari.Add(normalized);
            }

            _context.SaveChanges();
            _cache.Remove(CacheKey);
        }

        public bool HasPaytrMerchantKey()
        {
            return _context.SiteAyarlari.Any(x => !string.IsNullOrWhiteSpace(x.PaytrMerchantKeyProtected));
        }

        public bool HasPaytrMerchantSalt()
        {
            return _context.SiteAyarlari.Any(x => !string.IsNullOrWhiteSpace(x.PaytrMerchantSaltProtected));
        }

        public bool HasIyzicoSecretKey()
        {
            return _context.SiteAyarlari.Any(x => !string.IsNullOrWhiteSpace(x.IyzicoSecretKeyProtected));
        }

        public string BuildAbsoluteUrl(string? path)
        {
            var baseUrl = NormalizeBaseUrl(GetSettings().BaseUrl);

            if (string.IsNullOrWhiteSpace(path))
            {
                return baseUrl;
            }

            if (Uri.TryCreate(path, UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri.ToString();
            }

            var relativePath = path.Trim();
            if (relativePath.StartsWith("~"))
            {
                relativePath = relativePath[1..];
            }

            return $"{baseUrl}/{relativePath.TrimStart('/')}";
        }

        private SiteAyarlari LoadSettings()
        {
            var settings = _context.SiteAyarlari.FirstOrDefault();
            return NormalizeSettings(settings ?? new SiteAyarlari());
        }

        private static SiteAyarlari NormalizeSettings(SiteAyarlari settings)
        {
            settings.SiteAdi = string.IsNullOrWhiteSpace(settings.SiteAdi) ? "MeteorGaleri" : settings.SiteAdi.Trim();
            settings.MarkaAdi = string.IsNullOrWhiteSpace(settings.MarkaAdi) ? settings.SiteAdi : settings.MarkaAdi.Trim();
            settings.SiteBasligi = string.IsNullOrWhiteSpace(settings.SiteBasligi) ? $"{settings.MarkaAdi} - Online Dekorasyon Mağazası" : settings.SiteBasligi.Trim();
            settings.SiteAciklamasi = string.IsNullOrWhiteSpace(settings.SiteAciklamasi)
                ? "Duvar dekorasyonu ve yaşam alanları için premium ürünler."
                : settings.SiteAciklamasi.Trim();
            settings.SiteLogoUrl = string.IsNullOrWhiteSpace(settings.SiteLogoUrl) ? "/logo_svg.svg" : settings.SiteLogoUrl.Trim();
            settings.FaviconUrl = string.IsNullOrWhiteSpace(settings.FaviconUrl) ? "/favicon.ico" : settings.FaviconUrl.Trim();
            settings.BaseUrl = NormalizeBaseUrl(settings.BaseUrl);
            settings.TemaRengi = string.IsNullOrWhiteSpace(settings.TemaRengi) ? "#313511" : settings.TemaRengi.Trim();
            settings.UstBarMesaji = settings.UstBarMesaji?.Trim() ?? string.Empty;
            settings.KampanyaMesaji = settings.KampanyaMesaji?.Trim() ?? string.Empty;
            settings.FooterAciklamasi = string.IsNullOrWhiteSpace(settings.FooterAciklamasi)
                ? settings.SiteAciklamasi
                : settings.FooterAciklamasi.Trim();

            settings.Telefon = settings.Telefon?.Trim() ?? string.Empty;
            settings.Email = settings.Email?.Trim() ?? string.Empty;
            settings.Adres = settings.Adres?.Trim() ?? string.Empty;
            settings.WhatsappNumarasi = settings.WhatsappNumarasi?.Trim() ?? string.Empty;
            settings.CalismaSaatleri = settings.CalismaSaatleri?.Trim() ?? string.Empty;

            settings.InstagramUrl = settings.InstagramUrl?.Trim() ?? string.Empty;
            settings.FacebookUrl = settings.FacebookUrl?.Trim() ?? string.Empty;
            settings.TwitterUrl = settings.TwitterUrl?.Trim() ?? string.Empty;
            settings.YoutubeUrl = settings.YoutubeUrl?.Trim() ?? string.Empty;
            settings.TiktokUrl = settings.TiktokUrl?.Trim() ?? string.Empty;
            settings.PinterestUrl = settings.PinterestUrl?.Trim() ?? string.Empty;

            settings.ParaBirimi = string.IsNullOrWhiteSpace(settings.ParaBirimi) ? "TL" : settings.ParaBirimi.Trim();
            settings.KargoBedeli = Math.Max(0, settings.KargoBedeli);
            settings.UcretsizKargoLimiti = Math.Max(0, settings.UcretsizKargoLimiti);
            settings.StokUyariLimiti = Math.Max(0, settings.StokUyariLimiti);
            settings.HaliMetrekareFiyati = settings.HaliMetrekareFiyati <= 0 ? 1250 : settings.HaliMetrekareFiyati;
            settings.DuvarKagidiMetrekareFiyati = settings.DuvarKagidiMetrekareFiyati <= 0 ? 450 : settings.DuvarKagidiMetrekareFiyati;
            settings.PaytrMerchantId = settings.PaytrMerchantId?.Trim() ?? string.Empty;
            settings.PaytrMerchantKeyProtected = settings.PaytrMerchantKeyProtected?.Trim() ?? string.Empty;
            settings.PaytrMerchantSaltProtected = settings.PaytrMerchantSaltProtected?.Trim() ?? string.Empty;
            settings.PaytrCallbackUrl = NormalizeOptionalUrl(settings.PaytrCallbackUrl);
            settings.PaytrBasariliDonusUrl = NormalizeOptionalUrl(settings.PaytrBasariliDonusUrl);
            settings.PaytrBasarisizDonusUrl = NormalizeOptionalUrl(settings.PaytrBasarisizDonusUrl);
            settings.KargoFirmasi = string.IsNullOrWhiteSpace(settings.KargoFirmasi) ? "Aras Kargo" : settings.KargoFirmasi.Trim();
            settings.KargoTakipUrl = settings.KargoTakipUrl?.Trim() ?? string.Empty;
            settings.SiparisTeslimSuresiGun = settings.SiparisTeslimSuresiGun <= 0 ? 5 : settings.SiparisTeslimSuresiGun;
            settings.IadeHakkiGun = settings.IadeHakkiGun <= 0 ? 14 : settings.IadeHakkiGun;

            settings.MetaTitle = string.IsNullOrWhiteSpace(settings.MetaTitle)
                ? $"{settings.MarkaAdi} - Premium Dekorasyon Ürünleri, Kanvas Tablo ve Duvar Sanatı"
                : settings.MetaTitle.Trim();
            settings.MetaDescription = string.IsNullOrWhiteSpace(settings.MetaDescription)
                ? $"{settings.MarkaAdi}; kanvas tablo, cam tablo, duvar dekorasyonu ve yaşam alanlarına özel premium dekorasyon ürünleri sunar."
                : settings.MetaDescription.Trim();
            settings.MetaKeywords = string.IsNullOrWhiteSpace(settings.MetaKeywords)
                ? "kanvas tablo, cam tablo, duvar dekorasyonu, duvar sanatı, tablo, dekorasyon ürünleri, MeteorGaleri"
                : settings.MetaKeywords.Trim();
            settings.GoogleAnalyticsId = settings.GoogleAnalyticsId?.Trim() ?? string.Empty;
            settings.FacebookPixelId = settings.FacebookPixelId?.Trim() ?? string.Empty;
            settings.VarsayilanSosyalPaylasimGorseliUrl = string.IsNullOrWhiteSpace(settings.VarsayilanSosyalPaylasimGorseliUrl)
                ? "/EmailTemplates/meteorgaleri-logo.png"
                : settings.VarsayilanSosyalPaylasimGorseliUrl.Trim();
            settings.CookieMetni = string.IsNullOrWhiteSpace(settings.CookieMetni)
                ? "Deneyiminizi iyileştirmek, sepetinizi korumak ve site trafiğini analiz etmek için çerezler kullanıyoruz."
                : settings.CookieMetni.Trim();

            settings.BildirimAliciEmail = settings.BildirimAliciEmail?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(settings.BildirimAliciEmail) ||
                settings.BildirimAliciEmail.Equals("admin@canvasia.com", StringComparison.OrdinalIgnoreCase) ||
                settings.BildirimAliciEmail.Equals("canvasia.com.tr@gmail.com", StringComparison.OrdinalIgnoreCase))
            {
                settings.BildirimAliciEmail = "meteor_medya@hotmail.com";
            }
            settings.BakimModuMesaji = string.IsNullOrWhiteSpace(settings.BakimModuMesaji)
                ? "Size daha iyi bir alışveriş deneyimi sunmak için kısa bir bakım çalışması yapıyoruz. Çok yakında premium dekorasyon ürünlerimizle yeniden yayında olacağız."
                : settings.BakimModuMesaji.Trim();

            settings.IyzicoApiKey = settings.IyzicoApiKey?.Trim() ?? string.Empty;
            settings.IyzicoBaseUrl = string.IsNullOrWhiteSpace(settings.IyzicoBaseUrl) ? "https://sandbox-api.iyzipay.com" : settings.IyzicoBaseUrl.Trim();
            settings.IyzicoCallbackUrl = NormalizeOptionalUrl(settings.IyzicoCallbackUrl);

            return settings;
        }

        private void ApplyPaytrSecrets(SiteAyarlari settings, string? paytrMerchantKey, string? paytrMerchantSalt)
        {
            if (!string.IsNullOrWhiteSpace(paytrMerchantKey))
            {
                settings.PaytrMerchantKeyProtected = _paytrProtector.Protect(paytrMerchantKey.Trim());
            }

            if (!string.IsNullOrWhiteSpace(paytrMerchantSalt))
            {
                settings.PaytrMerchantSaltProtected = _paytrProtector.Protect(paytrMerchantSalt.Trim());
            }
        }

        private static string NormalizeOptionalUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            return url.Trim();
        }

        private static string NormalizeBaseUrl(string? baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return "https://www.meteorgaleri.com";
            }

            var value = baseUrl.Trim().TrimEnd('/');
            if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                value = "https://" + value;
            }

            return value;
        }
    }
}