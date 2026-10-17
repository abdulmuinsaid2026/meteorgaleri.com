using KanvasProje.Core.Helpers;
using KanvasProje.Core.Varliklar;
using KanvasProje.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanvasProje.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class KargoController : AdminBaseController
    {
        private readonly KanvasDbContext _context;

        public KargoController(KanvasDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var firmalar = await _context.KargoFirmalari
                .IgnoreQueryFilters()
                .Where(x => !x.SilindiMi)
                .OrderByDescending(x => x.VarsayilanMi)
                .ThenBy(x => x.Ad)
                .ToListAsync();

            return View(firmalar);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Kaydet(KargoFirmasi model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model.Ad))
                {
                    TempData["Mesaj"] = "Kargo firması adı zorunludur.";
                    TempData["Durum"] = "danger";
                    return RedirectToAction(nameof(Index));
                }

                if (model.Fiyat < 0)
                {
                    TempData["Mesaj"] = "Kargo ücreti negatif olamaz.";
                    TempData["Durum"] = "danger";
                    return RedirectToAction(nameof(Index));
                }

                model.Ad = model.Ad.Trim();
                model.Kod = SlugHelper.GenerateSlug(string.IsNullOrWhiteSpace(model.Kod) ? model.Ad : model.Kod);
                if (string.IsNullOrWhiteSpace(model.Kod))
                {
                    model.Kod = "kargo-" + Guid.NewGuid().ToString("N")[..6];
                }

                var duplicateExists = await _context.KargoFirmalari
                    .IgnoreQueryFilters()
                    .AnyAsync(x => x.Id != model.Id && !x.SilindiMi && x.Kod == model.Kod);
                if (duplicateExists)
                {
                    TempData["Mesaj"] = $"'{model.Kod}' koduna sahip başka bir kargo firması zaten mevcut.";
                    TempData["Durum"] = "danger";
                    return RedirectToAction(nameof(Index));
                }

                if (model.VarsayilanMi)
                {
                    await _context.KargoFirmalari
                        .Where(x => x.Id != model.Id)
                        .ExecuteUpdateAsync(x => x.SetProperty(v => v.VarsayilanMi, false));
                }

                if (model.Id == 0)
                {
                    model.OlusturulmaTarihi = DateTime.UtcNow;
                    model.SilindiMi = false;
                    _context.KargoFirmalari.Add(model);
                    TempData["Mesaj"] = $"{model.Ad} kargo firması eklendi.";
                }
                else
                {
                    var firma = await _context.KargoFirmalari.FirstOrDefaultAsync(x => x.Id == model.Id && !x.SilindiMi);
                    if (firma == null)
                    {
                        TempData["Mesaj"] = "Kargo firması bulunamadı.";
                        TempData["Durum"] = "danger";
                        return RedirectToAction(nameof(Index));
                    }

                    firma.Ad = model.Ad;
                    firma.Kod = model.Kod;
                    firma.LogoUrl = model.LogoUrl;
                    firma.Telefon = model.Telefon;
                    firma.TakipUrl = model.TakipUrl;
                    firma.GondericiUnvan = model.GondericiUnvan;
                    firma.GondericiAdres = model.GondericiAdres;
                    firma.GondericiTelefon = model.GondericiTelefon;
                    firma.AktifMi = model.AktifMi;
                    firma.VarsayilanMi = model.VarsayilanMi;
                    firma.Fiyat = model.Fiyat;
                    TempData["Mesaj"] = $"{model.Ad} kargo firması güncellendi.";
                }

                await _context.SaveChangesAsync();
                TempData["Durum"] = "success";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["Mesaj"] = "Hata: " + ex.Message;
                TempData["Durum"] = "danger";
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Sil(int id)
        {
            var firma = await _context.KargoFirmalari.FirstOrDefaultAsync(x => x.Id == id && !x.SilindiMi);
            if (firma == null)
            {
                TempData["Mesaj"] = "Kargo firması bulunamadı.";
                TempData["Durum"] = "danger";
                return RedirectToAction(nameof(Index));
            }

            if (firma.VarsayilanMi)
            {
                TempData["Mesaj"] = "Varsayılan kargo firması silinemez. Önce başka bir firmayı varsayılan yapın.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index));
            }

            firma.SilindiMi = true;
            firma.AktifMi = false;
            await _context.SaveChangesAsync();

            TempData["Mesaj"] = $"{firma.Ad} kargo firması silindi.";
            TempData["Durum"] = "success";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VarsayilanYap(int id)
        {
            var firma = await _context.KargoFirmalari.FirstOrDefaultAsync(x => x.Id == id && !x.SilindiMi);
            if (firma == null)
            {
                TempData["Mesaj"] = "Kargo firması bulunamadı.";
                TempData["Durum"] = "danger";
                return RedirectToAction(nameof(Index));
            }

            await _context.KargoFirmalari.ExecuteUpdateAsync(x => x.SetProperty(v => v.VarsayilanMi, false));
            firma.VarsayilanMi = true;
            firma.AktifMi = true;
            await _context.SaveChangesAsync();

            TempData["Mesaj"] = $"{firma.Ad} varsayılan kargo firması yapıldı.";
            TempData["Durum"] = "success";
            return RedirectToAction(nameof(Index));
        }
    }
}
