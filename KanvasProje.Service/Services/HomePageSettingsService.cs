using System.Text.Json;
using KanvasProje.Core.Models;
using Microsoft.Extensions.Caching.Memory;

namespace KanvasProje.Service.Services
{
    public interface IHomePageSettingsService
    {
        HomePageSettingsModel GetSettings();
        void SaveSettings(HomePageSettingsModel settings);
    }

    public class HomePageSettingsService : IHomePageSettingsService
    {
        private const string CacheKey = "home-page-settings";

        private readonly IWebHostEnvironment _env;
        private readonly IMemoryCache _cache;
        private readonly JsonSerializerOptions _serializerOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private string SettingsPath => Path.Combine(_env.ContentRootPath, "App_Data", "home-page-settings.json");

        public HomePageSettingsService(IWebHostEnvironment env, IMemoryCache cache)
        {
            _env = env;
            _cache = cache;
        }

        public HomePageSettingsModel GetSettings()
        {
            return _cache.GetOrCreate(CacheKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                return LoadSettings();
            })!;
        }

        public void SaveSettings(HomePageSettingsModel settings)
        {
            var normalized = NormalizeSettings(settings);
            var directory = Path.GetDirectoryName(SettingsPath)!;

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(normalized, _serializerOptions);
            File.WriteAllText(SettingsPath, json);
            _cache.Remove(CacheKey);
        }

        private HomePageSettingsModel LoadSettings()
        {
            if (!File.Exists(SettingsPath))
            {
                return NormalizeSettings(new HomePageSettingsModel());
            }

            try
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<HomePageSettingsModel>(json);
                return NormalizeSettings(settings ?? new HomePageSettingsModel());
            }
            catch
            {
                return NormalizeSettings(new HomePageSettingsModel());
            }
        }

        private static HomePageSettingsModel NormalizeSettings(HomePageSettingsModel settings)
        {
            settings.Hero ??= new HomeHeroBlockSettings();
            settings.Categories ??= new HomeSimpleBlockSettings();
            settings.FeaturedProducts ??= new HomeProductBlockSettings();
            settings.BestSellers ??= new HomeProductBlockSettings();
            settings.Deals ??= new HomeProductBlockSettings();
            settings.Features ??= new HomeFeaturesBlockSettings();
            settings.Newsletter ??= new HomeNewsletterBlockSettings();

            settings.Hero.Enabled = settings.Hero.Enabled;
            settings.Hero.SortOrder = NormalizeSortOrder(settings.Hero.SortOrder, 10);
            settings.Hero.Title = NormalizeText(settings.Hero.Title, "Yeni sezon koleksiyonlarını keşfedin");
            settings.Hero.Subtitle = NormalizeText(settings.Hero.Subtitle, "Markanıza uygun ürünleri, kampanyaları ve vitrinde öne çıkacak koleksiyonları tek bir noktadan yönetin.");
            settings.Hero.PrimaryButtonText = NormalizeText(settings.Hero.PrimaryButtonText, "Koleksiyonu Keşfet");
            settings.Hero.PrimaryButtonUrl = NormalizeText(settings.Hero.PrimaryButtonUrl, "/Urun/Index");
            settings.Hero.SecondaryButtonText = NormalizeText(settings.Hero.SecondaryButtonText, "İletişime Geç");
            settings.Hero.SecondaryButtonUrl = NormalizeText(settings.Hero.SecondaryButtonUrl, "/Kurumsal/Iletisim");
            settings.Hero.DesktopSlides = NormalizeSlides(
                settings.Hero.DesktopSlides,
                new[]
                {
                    ("/img/banner/slider-1.webp", "/video/slider1.mp4", "SADE\\nVE DOĞAL", "MeteorGaleri Koleksiyonu", "Yaşam alanlarınız için özenle seçilmiş kanvas eserler.", "Koleksiyonu Keşfet", "/Urun"),
                    ("/img/banner/slider-2.webp", "/video/slider2.mp4", "MODERN\\nESERLER", "Yeni Sezon", "Evinizi sanatın büyüleyici dünyasıyla buluşturun.", "Tüm Ürünleri İncele", "/Urun")
                });
            settings.Hero.MobileSlides = NormalizeSlides(
                settings.Hero.MobileSlides,
                new[]
                {
                    ("/img/banner/mobile-1.webp", "/video/slider1.mp4", "SADE\\nVE DOĞAL", "MeteorGaleri Koleksiyonu", "Yaşam alanlarınız için özenle seçilmiş kanvas eserler.", "Koleksiyonu Keşfet", "/Urun"),
                    ("/img/banner/mobile-2.webp", "/video/slider2.mp4", "MODERN\\nESERLER", "Yeni Sezon", "Evinizi sanatın büyüleyici dünyasıyla buluşturun.", "Tüm Ürünleri İncele", "/Urun")
                });

            settings.Categories.Enabled = settings.Categories.Enabled;
            settings.Categories.SortOrder = NormalizeSortOrder(settings.Categories.SortOrder, 20);
            settings.Categories.Title = NormalizeText(settings.Categories.Title, "Tüm Kategoriler");
            settings.Categories.Subtitle = NormalizeText(settings.Categories.Subtitle, "Mağazadaki tüm ürün ailelerini kategori bazında keşfedin.");

            NormalizeProductBlock(
                settings.FeaturedProducts,
                30,
                "Öne Çıkanlar",
                "En yeni ve en çok dikkat çeken ürünler",
                "Tümünü Gör",
                "/Urun/Index");
            NormalizeProductBlock(
                settings.BestSellers,
                40,
                "En Çok Satanlar",
                "Müşterilerin en çok tercih ettiği ürünler",
                "Tümünü Gör",
                "/Urun/Index?sort=popularity");
            NormalizeProductBlock(
                settings.Deals,
                50,
                "Fırsat Ürünleri",
                "Kaçırılmaması gereken kampanyalı ürünler",
                "Tümünü Gör",
                "/Urun/Index?sort=price_asc");

            settings.Features.Enabled = settings.Features.Enabled;
            settings.Features.SortOrder = NormalizeSortOrder(settings.Features.SortOrder, 60);
            settings.Features.Title = NormalizeText(settings.Features.Title, "Neden Bizi Tercih Etmelisiniz?");
            settings.Features.Subtitle = NormalizeText(settings.Features.Subtitle, "Güvenilir operasyon, ölçeklenebilir ürün yapısı ve net teslimat süreçleri.");
            settings.Features.Items = NormalizeFeatureItems(settings.Features.Items);

            settings.Newsletter.Enabled = settings.Newsletter.Enabled;
            settings.Newsletter.SortOrder = NormalizeSortOrder(settings.Newsletter.SortOrder, 70);
            settings.Newsletter.Title = NormalizeText(settings.Newsletter.Title, "Duyuruları ilk siz alın");
            settings.Newsletter.Subtitle = NormalizeText(settings.Newsletter.Subtitle, "Kampanyalar, yeni ürünler ve önemli güncellemeleri e-posta ile alın.");
            settings.Newsletter.PlaceholderText = NormalizeText(settings.Newsletter.PlaceholderText, "E-posta adresinizi yazın");
            settings.Newsletter.ButtonText = NormalizeText(settings.Newsletter.ButtonText, "Abone Ol");

            return settings;
        }

        private static void NormalizeProductBlock(
            HomeProductBlockSettings block,
            int defaultSortOrder,
            string defaultTitle,
            string defaultSubtitle,
            string defaultViewAllText,
            string defaultViewAllUrl)
        {
            block.Enabled = block.Enabled;
            block.SortOrder = NormalizeSortOrder(block.SortOrder, defaultSortOrder);
            block.Title = NormalizeText(block.Title, defaultTitle);
            block.Subtitle = NormalizeText(block.Subtitle, defaultSubtitle);
            block.ViewAllText = NormalizeText(block.ViewAllText, defaultViewAllText);
            block.ViewAllUrl = NormalizeText(block.ViewAllUrl, defaultViewAllUrl);
        }

        private static List<HomeHeroSlideSettings> NormalizeSlides(
            List<HomeHeroSlideSettings>? slides,
            IEnumerable<(string ImageUrl, string VideoUrl, string Title, string Subtitle, string Description, string ButtonText, string ButtonUrl)> defaults)
        {
            var normalized = slides ?? new List<HomeHeroSlideSettings>();
            var defaultList = defaults.ToList();
            var existingCount = normalized.Count;

            while (normalized.Count < defaultList.Count)
            {
                var fallback = defaultList[normalized.Count];
                normalized.Add(new HomeHeroSlideSettings
                {
                    ImageUrl = fallback.ImageUrl,
                    VideoUrl = fallback.VideoUrl,
                    Title = fallback.Title,
                    Subtitle = fallback.Subtitle,
                    Description = fallback.Description,
                    ButtonText = fallback.ButtonText,
                    ButtonUrl = fallback.ButtonUrl,
                    SortOrder = normalized.Count
                });
            }

            for (var i = 0; i < defaultList.Count; i++)
            {
                normalized[i] ??= new HomeHeroSlideSettings();
                normalized[i].Id = NormalizeText(normalized[i].Id, Guid.NewGuid().ToString("N"));
                normalized[i].ImageUrl = NormalizeText(normalized[i].ImageUrl, defaultList[i].ImageUrl);
                normalized[i].VideoUrl = NormalizeText(normalized[i].VideoUrl, defaultList[i].VideoUrl);

                if (i < existingCount)
                {
                    normalized[i].Title = CleanText(normalized[i].Title);
                    normalized[i].Subtitle = CleanText(normalized[i].Subtitle);
                    normalized[i].Description = CleanText(normalized[i].Description);
                    normalized[i].ButtonText = CleanText(normalized[i].ButtonText);
                    normalized[i].ButtonUrl = CleanText(normalized[i].ButtonUrl);
                }
                else
                {
                    normalized[i].Title = NormalizeText(normalized[i].Title, defaultList[i].Title);
                    normalized[i].Subtitle = NormalizeText(normalized[i].Subtitle, defaultList[i].Subtitle);
                    normalized[i].Description = NormalizeText(normalized[i].Description, defaultList[i].Description);
                    normalized[i].ButtonText = NormalizeText(normalized[i].ButtonText, defaultList[i].ButtonText);
                    normalized[i].ButtonUrl = NormalizeText(normalized[i].ButtonUrl, defaultList[i].ButtonUrl);
                }
            }

            for (var i = defaultList.Count; i < normalized.Count; i++)
            {
                normalized[i] ??= new HomeHeroSlideSettings();
                normalized[i].Id = NormalizeText(normalized[i].Id, Guid.NewGuid().ToString("N"));
                normalized[i].ImageUrl = CleanText(normalized[i].ImageUrl);
                normalized[i].VideoUrl = CleanText(normalized[i].VideoUrl);
                normalized[i].Title = CleanText(normalized[i].Title);
                normalized[i].Subtitle = CleanText(normalized[i].Subtitle);
                normalized[i].Description = CleanText(normalized[i].Description);
                normalized[i].ButtonText = CleanText(normalized[i].ButtonText);
                normalized[i].ButtonUrl = CleanText(normalized[i].ButtonUrl);
            }

            return normalized
                .OrderBy(slide => slide.SortOrder)
                .ToList();
        }

        private static List<HomeFeatureItemSettings> NormalizeFeatureItems(List<HomeFeatureItemSettings>? items)
        {
            var normalized = items ?? new List<HomeFeatureItemSettings>();
            var defaults = new[]
            {
                new HomeFeatureItemSettings
                {
                    Icon = "fas fa-truck",
                    Title = "Teslimat Avantajı",
                    Description = "Siparişlerinizi planlı operasyonla hızlı ve kontrollü şekilde yönetin."
                },
                new HomeFeatureItemSettings
                {
                    Icon = "fas fa-shield-alt",
                    Title = "Güvenli Alışveriş",
                    Description = "SSL, güvenlik başlıkları ve kontrollü ödeme akışlarıyla korunan deneyim."
                },
                new HomeFeatureItemSettings
                {
                    Icon = "fas fa-sliders-h",
                    Title = "Esnek Yapı",
                    Description = "Kategori, ürün, varyasyon ve içerik yapısı farklı markalara göre şekillenebilir."
                },
                new HomeFeatureItemSettings
                {
                    Icon = "fas fa-headset",
                    Title = "Satış Sonrası Destek",
                    Description = "Sipariş sonrası süreçleri ve müşteri iletişimi tek panelden yönetilir."
                }
            };

            while (normalized.Count < defaults.Length)
            {
                normalized.Add(new HomeFeatureItemSettings());
            }

            for (var i = 0; i < defaults.Length; i++)
            {
                normalized[i] ??= new HomeFeatureItemSettings();
                normalized[i].Icon = NormalizeText(normalized[i].Icon, defaults[i].Icon);
                normalized[i].Title = NormalizeText(normalized[i].Title, defaults[i].Title);
                normalized[i].Description = NormalizeText(normalized[i].Description, defaults[i].Description);
            }

            return normalized;
        }

        private static int NormalizeSortOrder(int currentValue, int defaultValue)
        {
            return currentValue < 0 ? defaultValue : currentValue;
        }

        private static string NormalizeText(string? value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string CleanText(string? value)
        {
            return value?.Trim() ?? string.Empty;
        }
    }
}
