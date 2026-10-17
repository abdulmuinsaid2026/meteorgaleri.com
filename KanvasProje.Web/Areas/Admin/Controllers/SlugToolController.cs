using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KanvasProje.Data;
using KanvasProje.Core.Helpers;
using Microsoft.AspNetCore.Authorization;

namespace KanvasProje.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class SlugToolController : AdminBaseController
    {
        private readonly KanvasDbContext _context;
        private readonly ILogger<SlugToolController> _logger;
        private readonly Microsoft.AspNetCore.Antiforgery.IAntiforgery _antiforgery;

        public SlugToolController(
            KanvasDbContext context,
            ILogger<SlugToolController> logger,
            Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery)
        {
            _context = context;
            _logger = logger;
            _antiforgery = antiforgery;
        }

        [HttpGet]
        public async Task<IActionResult> GenerateAllSlugs()
        {
            var missingCount = await _context.Urunler
                .CountAsync(u => !u.SilindiMi && (u.Slug == null || u.Slug == ""));

            var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
            var html = $@"<!DOCTYPE html>
<html lang=""tr""><head><meta charset=""utf-8""><title>Slug Oluşturucu</title></head>
<body style=""font-family:sans-serif;padding:24px;"">
<h3>Eksik Slug Oluşturma Aracı</h3>
<p>Slug değeri eksik olan aktif ürün sayısı: <strong>{missingCount}</strong></p>
<form method=""post"" action=""/Admin/SlugTool/GenerateAllSlugs"">
    <input type=""hidden"" name=""{tokens.FormFieldName}"" value=""{tokens.RequestToken}"" />
    <button type=""submit"" style=""padding:10px 18px;background:#1B2A4A;color:#fff;border:none;border-radius:6px;cursor:pointer;"">Eksik Slugları Oluştur</button>
</form>
</body></html>";
            return Content(html, "text/html; charset=utf-8");
        }

        /// <summary>
        /// Mevcut tüm ürünler için slug generate eder (POST + AntiForgeryToken)
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [ActionName("GenerateAllSlugs")]
        public async Task<IActionResult> GenerateAllSlugsPost()
        {
            var urunler = await _context.Urunler
                .Where(u => !u.SilindiMi && (u.Slug == null || u.Slug == ""))
                .ToListAsync();

            if (!urunler.Any())
            {
                return Content("Tüm ürünlerde slug mevcut! İşlem gerekmiyor.");
            }

            var existingSlugs = await _context.Urunler
                .Where(u => u.Slug != null && u.Slug != "")
                .Select(u => u.Slug!)
                .ToListAsync();

            int updatedCount = 0;

            foreach (var urun in urunler)
            {
                try
                {
                    var slug = SlugHelper.GenerateSlug(urun.Baslik);
                    if (string.IsNullOrWhiteSpace(slug))
                    {
                        slug = $"urun-{urun.Id}";
                    }

                    slug = SlugHelper.EnsureUnique(slug, existingSlugs);
                    if (string.IsNullOrWhiteSpace(slug))
                    {
                        continue;
                    }

                    urun.Slug = slug;
                    existingSlugs.Add(slug);
                    updatedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Slug oluşturulurken hata oluştu. UrunId: {UrunId}", urun.Id);
                }
            }

            await _context.SaveChangesAsync();

            return Content($"✅ Başarılı! {updatedCount} adet ürün için slug oluşturuldu.\n\n" +
                          $"Artık ürün linkleri SEO-friendly olacak:\n" +
                          $"Örnek: /Urun/Detay/ataturk-portresi-kanvas-1355");
        }
    }
}
