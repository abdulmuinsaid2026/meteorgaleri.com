using System.Text.Json;
using System.Text.Json.Serialization;
using KanvasProje.Core.Varliklar;

namespace KanvasProje.Core.Helpers
{
    public static class SchemaGenerator
    {
        public static string GenerateProductSchema(
            Urun urun,
            double averageRating,
            int reviewCount,
            string imageUrl,
            string detailUrl,
            string brandName = "Marka")
        {
            var schema = new Dictionary<string, object?>
            {
                ["@context"] = "https://schema.org",
                ["@type"] = "Product",
                ["name"] = urun.Baslik,
                ["image"] = imageUrl,
                ["description"] = string.IsNullOrWhiteSpace(urun.KisaAciklama) ? urun.Aciklama : urun.KisaAciklama,
                ["sku"] = string.IsNullOrWhiteSpace(urun.SKU) ? urun.Id.ToString() : urun.SKU,
                ["brand"] = new Dictionary<string, object?>
                {
                    ["@type"] = "Brand",
                    ["name"] = string.IsNullOrWhiteSpace(urun.Marka) ? brandName : urun.Marka
                },
                ["offers"] = new Dictionary<string, object?>
                {
                    ["@type"] = "Offer",
                    ["url"] = detailUrl,
                    ["priceCurrency"] = "TRY",
                    ["price"] = urun.EtkinFiyat.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    ["availability"] = urun.StoktaVarMi ? "https://schema.org/InStock" : "https://schema.org/OutOfStock",
                    ["priceValidUntil"] = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                }
            };

            if (reviewCount > 0)
            {
                schema["aggregateRating"] = new Dictionary<string, object?>
                {
                    ["@type"] = "AggregateRating",
                    ["ratingValue"] = averageRating.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                    ["reviewCount"] = reviewCount
                };
            }

            return JsonSerializer.Serialize(schema, CreateSerializerOptions());
        }

        public static string GenerateOrganizationSchema(
            string brandName,
            string siteUrl,
            string logoUrl,
            string description,
            string? phone = null,
            params string[] socialLinks)
        {
            var schema = new Dictionary<string, object?>
            {
                ["@context"] = "https://schema.org",
                ["@type"] = "Organization",
                ["name"] = brandName,
                ["url"] = siteUrl,
                ["logo"] = logoUrl,
                ["description"] = description
            };

            if (!string.IsNullOrWhiteSpace(phone))
            {
                schema["contactPoint"] = new Dictionary<string, object?>
                {
                    ["@type"] = "ContactPoint",
                    ["telephone"] = phone,
                    ["contactType"] = "customer service",
                    ["areaServed"] = "TR",
                    ["availableLanguage"] = "Turkish"
                };
            }

            var cleanLinks = socialLinks.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            if (cleanLinks.Length > 0)
            {
                schema["sameAs"] = cleanLinks;
            }

            return JsonSerializer.Serialize(schema, CreateSerializerOptions());
        }

        public static string GenerateBreadcrumbSchema(List<(string name, string url)> breadcrumbs)
        {
            var itemList = breadcrumbs.Select((item, index) => new Dictionary<string, object?>
            {
                ["@type"] = "ListItem",
                ["position"] = index + 1,
                ["name"] = item.name,
                ["item"] = item.url
            }).ToArray();

            var schema = new Dictionary<string, object?>
            {
                ["@context"] = "https://schema.org",
                ["@type"] = "BreadcrumbList",
                ["itemListElement"] = itemList
            };

            return JsonSerializer.Serialize(schema, CreateSerializerOptions());
        }

        private static JsonSerializerOptions CreateSerializerOptions()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }
    }
}
