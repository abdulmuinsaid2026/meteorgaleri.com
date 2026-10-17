using Microsoft.AspNetCore.Mvc;
using KanvasProje.Data;
using Microsoft.EntityFrameworkCore;
using System.Text;
using KanvasProje.Service.Services;
using System.Net;
using Microsoft.Extensions.Caching.Memory;

namespace KanvasProje.Web.Controllers
{
    public class SitemapController : Controller
    {
        private readonly KanvasDbContext _context;
        private readonly ISiteSettingsService _siteSettingsService;
        private readonly IMemoryCache _cache;

        public SitemapController(KanvasDbContext context, ISiteSettingsService siteSettingsService, IMemoryCache cache)
        {
            _context = context;
            _siteSettingsService = siteSettingsService;
            _cache = cache;
        }

        [Route("sitemap.xml")]
        public async Task<IActionResult> Index()
        {
            var baseUrl = _siteSettingsService.BuildAbsoluteUrl(string.Empty).TrimEnd('/');
            var cacheKey = $"SitemapXml_{baseUrl}";

            if (!_cache.TryGetValue(cacheKey, out string? xmlContent) || string.IsNullOrEmpty(xmlContent))
            {
                var sitemap = new StringBuilder();
                sitemap.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                sitemap.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

                var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

                // Ana sayfa
                sitemap.AppendLine("  <url>");
                sitemap.AppendLine($"    <loc>{XmlEncode(baseUrl + "/")}</loc>");
                sitemap.AppendLine($"    <lastmod>{today}</lastmod>");
                sitemap.AppendLine("    <changefreq>daily</changefreq>");
                sitemap.AppendLine("    <priority>1.0</priority>");
                sitemap.AppendLine("  </url>");

                var staticPaths = new[]
                {
                    "/Urun",
                    "/Kurumsal/Hakkimizda",
                    "/Kurumsal/Iletisim",
                    "/Kurumsal/SSS",
                    "/Kurumsal/Gizlilik",
                    "/Kurumsal/KullaniciSozlesmesi",
                    "/Kurumsal/MesafeliSatis",
                    "/Kurumsal/IadeKosullari"
                };

                foreach (var path in staticPaths)
                {
                    sitemap.AppendLine("  <url>");
                    sitemap.AppendLine($"    <loc>{XmlEncode(baseUrl + path)}</loc>");
                    sitemap.AppendLine($"    <lastmod>{today}</lastmod>");
                    sitemap.AppendLine("    <changefreq>weekly</changefreq>");
                    sitemap.AppendLine("    <priority>0.6</priority>");
                    sitemap.AppendLine("  </url>");
                }

                // Kategoriler
                var kategoriler = await _context.Kategoriler
                    .AsNoTracking()
                    .Where(k => !k.SilindiMi && k.AktifMi)
                    .Select(k => new { k.Id, k.OlusturulmaTarihi })
                    .ToListAsync();

                foreach (var kategori in kategoriler)
                {
                    var katLastMod = kategori.OlusturulmaTarihi.ToString("yyyy-MM-dd");
                    sitemap.AppendLine("  <url>");
                    sitemap.AppendLine($"    <loc>{XmlEncode($"{baseUrl}/Urun?k={kategori.Id}")}</loc>");
                    sitemap.AppendLine($"    <lastmod>{katLastMod}</lastmod>");
                    sitemap.AppendLine("    <changefreq>weekly</changefreq>");
                    sitemap.AppendLine("    <priority>0.8</priority>");
                    sitemap.AppendLine("  </url>");
                }

                // Aktif ürünler (aktif kategoride olanlar)
                var urunler = await _context.Urunler
                    .AsNoTracking()
                    .Where(u => !u.SilindiMi && u.AktifMi && u.Kategori != null && u.Kategori.AktifMi && !u.Kategori.SilindiMi)
                    .OrderByDescending(u => u.OlusturulmaTarihi)
                    .Select(u => new { u.Id, u.Slug, u.Baslik, u.OlusturulmaTarihi })
                    .Take(5000)
                    .ToListAsync();

                foreach (var urun in urunler)
                {
                    var slugPart = string.IsNullOrWhiteSpace(urun.Slug)
                        ? KanvasProje.Core.Helpers.SlugHelper.GenerateSlug(urun.Baslik ?? string.Empty)
                        : urun.Slug;
                    var detailSegment = string.IsNullOrWhiteSpace(slugPart) ? urun.Id.ToString() : $"{slugPart}-{urun.Id}";
                    var urunLastMod = urun.OlusturulmaTarihi.ToString("yyyy-MM-dd");
                    sitemap.AppendLine("  <url>");
                    sitemap.AppendLine($"    <loc>{XmlEncode($"{baseUrl}/Urun/Detay/{detailSegment}")}</loc>");
                    sitemap.AppendLine($"    <lastmod>{urunLastMod}</lastmod>");
                    sitemap.AppendLine("    <changefreq>weekly</changefreq>");
                    sitemap.AppendLine("    <priority>0.7</priority>");
                    sitemap.AppendLine("  </url>");
                }

                sitemap.AppendLine("</urlset>");
                xmlContent = sitemap.ToString();
                _cache.Set(cacheKey, xmlContent, TimeSpan.FromMinutes(30));
            }

            return Content(xmlContent, "application/xml", Encoding.UTF8);
        }

        private static string XmlEncode(string value) => WebUtility.HtmlEncode(value);
    }
}
