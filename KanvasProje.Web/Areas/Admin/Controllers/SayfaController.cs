using Microsoft.AspNetCore.Mvc;
using KanvasProje.Data;
using KanvasProje.Core.Varliklar;
using KanvasProje.Core.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

namespace KanvasProje.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class SayfaController : AdminBaseController
    {
        private readonly KanvasDbContext _context;

        public SayfaController(KanvasDbContext context)
        {
            _context = context;
        }

        // 1. LİSTELEME
        public async Task<IActionResult> Index()
        {
            var sayfalar = await _context.KurumsalSayfalar.OrderBy(x => x.Sira).ToListAsync();
            return View(sayfalar);
        }

        // 2. EKLEME VE DÜZENLEME (Tek Action'da halledelim)
        [HttpGet]
        public async Task<IActionResult> Form(int? id)
        {
            if (id.HasValue) // Düzenleme Modu
            {
                var sayfa = await _context.KurumsalSayfalar.FindAsync(id.Value);
                if (sayfa == null) return NotFound();
                return View(sayfa);
            }
            return View(new KurumsalSayfa()); // Ekleme Modu (Boş model)
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Form(KurumsalSayfa model)
        {
            if (ModelState.IsValid)
            {
                if (model.Id == 0) // Yeni Kayıt
                {
                    model.UrlSlug = await GenerateUniquePageSlugAsync(model.Baslik);
                    _context.KurumsalSayfalar.Add(model);
                }
                else // Güncelleme
                {
                    var mevcut = await _context.KurumsalSayfalar.FindAsync(model.Id);
                    if (mevcut != null)
                    {
                        mevcut.Baslik = model.Baslik;
                        mevcut.Icerik = model.Icerik;
                        mevcut.Sira = model.Sira;
                        // UrlSlug'ı güncellemiyoruz ki Google'daki linkler kırılmasın
                    }
                }
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(model);
        }

        // 3. SİLME
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Sil(int id)
        {
            var sayfa = await _context.KurumsalSayfalar.FindAsync(id);
            if(sayfa != null)
            {
                _context.KurumsalSayfalar.Remove(sayfa);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        private async Task<string> GenerateUniquePageSlugAsync(string? text)
        {
            var baseSlug = SlugHelper.GenerateSlug(text ?? string.Empty);
            if (string.IsNullOrWhiteSpace(baseSlug))
            {
                baseSlug = "sayfa";
            }

            var existingSlugs = await _context.KurumsalSayfalar
                .Where(x => x.UrlSlug != null && x.UrlSlug != "")
                .Select(x => x.UrlSlug!)
                .ToListAsync();

            return SlugHelper.EnsureUnique(baseSlug, existingSlugs);
        }
    }
}
