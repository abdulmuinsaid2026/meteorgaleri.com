using KanvasProje.Core.Varliklar;
using KanvasProje.Data;
using KanvasProje.Service.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanvasProje.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class HomeSectionsController : AdminBaseController
    {
        private readonly IHomePageSectionService _sectionService;
        private readonly KanvasDbContext _context;

        public HomeSectionsController(IHomePageSectionService sectionService, KanvasDbContext context)
        {
            _sectionService = sectionService;
            _context = context;
        }

        // GET: /admin/homesections
        public async Task<IActionResult> Index()
        {
            var sections = await _sectionService.GetActiveSectionsAsync();
            // Aktif+pasif hepsini göster (GetActiveSectionsAsync sadece Enabled olanları döner)
            // Tüm bölümleri sıralayarak göster
            var allSections = await _context.HomePageSections
                .AsNoTracking()
                .OrderBy(s => s.SortOrder)
                .ToListAsync();
            return View(allSections);
        }

        // GET: /admin/homesections/create
        public IActionResult Create()
        {
            var model = new HomePageSection
            {
                Enabled = true,
                SortOrder = 0
            };
            return View(model);
        }

        // POST: /admin/homesections/create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(HomePageSection model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            await _sectionService.CreateSectionAsync(model);
            TempData["Basari"] = "Yeni bölüm başarıyla oluşturuldu.";
            return RedirectToAction(nameof(Index));
        }

        // GET: /admin/homesections/edit/5
        public async Task<IActionResult> Edit(int id)
        {
            var section = await _sectionService.GetSectionAsync(id);
            if (section == null)
            {
                return NotFound();
            }

            // Ürün listesini view'a gönder (ürün seçimi için)
            ViewBag.TumUrunler = await _context.Urunler
                .AsNoTracking()
                .Where(u => u.AktifMi && !u.SilindiMi)
                .OrderBy(u => u.Baslik)
                .Select(u => new { u.Id, u.Baslik })
                .ToListAsync();

            return View(section);
        }

        // POST: /admin/homesections/edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, HomePageSection model, int[]? selectedProducts)
        {
            if (id != model.Id)
            {
                return BadRequest();
            }

            if (!ModelState.IsValid)
            {
                ViewBag.TumUrunler = await _context.Urunler
                    .AsNoTracking()
                    .Where(u => u.AktifMi && !u.SilindiMi)
                    .OrderBy(u => u.Baslik)
                    .Select(u => new { u.Id, u.Baslik })
                    .ToListAsync();
                return View(model);
            }

            await _sectionService.UpdateSectionAsync(model);

            // Ürün listesini güncelle (ProductBlock tipi için)
            if (model.SectionType == HomePageSectionType.ProductBlock && selectedProducts != null)
            {
                await _sectionService.SetSectionProductsAsync(id, selectedProducts.ToList());
            }

            TempData["Basari"] = "Bölüm başarıyla güncellendi.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /admin/homesections/delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            await _sectionService.DeleteSectionAsync(id);
            TempData["Basari"] = "Bölüm silindi.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /admin/homesections/toggleenabled
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleEnabled(int id)
        {
            var enabled = await _sectionService.ToggleSectionEnabledAsync(id);
            if (enabled == null) return NotFound();

            return Json(new { success = true, enabled = enabled.Value });
        }
    }
}
