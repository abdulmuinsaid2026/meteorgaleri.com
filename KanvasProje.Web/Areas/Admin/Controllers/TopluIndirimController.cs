using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KanvasProje.Core.Helpers;
using KanvasProje.Data;
using KanvasProje.Service.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanvasProje.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class TopluIndirimController : AdminBaseController
    {
        private readonly KanvasDbContext _context;
        private readonly ISiteSettingsService _siteSettingsService;

        public TopluIndirimController(KanvasDbContext context, ISiteSettingsService siteSettingsService)
        {
            _context = context;
            _siteSettingsService = siteSettingsService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var settings = _siteSettingsService.GetSettings();
            var kategoriler = await _context.Kategoriler
                .AsNoTracking()
                .Include(k => k.ParentKategori)
                .Where(k => !k.SilindiMi)
                .OrderBy(k => k.ParentKategoriId.HasValue ? 1 : 0)
                .ThenBy(k => k.Sira)
                .ThenBy(k => k.Ad)
                .Select(k => new KategoriIndirimSatirViewModel
                {
                    Id = k.Id,
                    Ad = k.Ad,
                    UstKategoriAdi = k.ParentKategori != null ? k.ParentKategori.Ad : null,
                    AktifMi = k.AktifMi,
                    IndirimYuzdesi = k.IndirimYuzdesi,
                    UrunSayisi = _context.Urunler.Count(u => !u.SilindiMi && u.KategoriId == k.Id)
                })
                .ToListAsync();

            var vm = new TopluIndirimViewModel
            {
                GenelIndirimYuzdesi = settings.GenelIndirimYuzdesi,
                Kategoriler = kategoriler
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult GenelIndirimGuncelle(decimal genelIndirimYuzdesi)
        {
            var clamped = Math.Clamp(genelIndirimYuzdesi, 0m, 95m);
            var settings = _siteSettingsService.GetSettings();
            settings.GenelIndirimYuzdesi = clamped;
            _siteSettingsService.SaveSettings(settings);

            TempData["Basari"] = $"Tüm mağaza genel indirim oranı %{clamped:0.##} olarak güncellendi.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> KategoriIndirimleriniGuncelle(List<KategoriIndirimGirdiModel> girdiler)
        {
            if (girdiler == null || girdiler.Count == 0)
            {
                TempData["Hata"] = "Güncellenecek kategori bilgisi alınamadı.";
                return RedirectToAction(nameof(Index));
            }

            var kategoriler = await _context.Kategoriler
                .Where(k => !k.SilindiMi)
                .ToListAsync();

            var girdiMap = girdiler
                .GroupBy(x => x.KategoriId)
                .ToDictionary(g => g.Key, g => g.First().IndirimYuzdesi);

            int degisen = 0;
            foreach (var kat in kategoriler)
            {
                if (girdiMap.TryGetValue(kat.Id, out var yeniOran))
                {
                    decimal? normalized = yeniOran.HasValue
                        ? Math.Clamp(yeniOran.Value, 0m, 95m)
                        : null;

                    if (kat.IndirimYuzdesi != normalized)
                    {
                        kat.IndirimYuzdesi = normalized;
                        degisen++;
                    }
                }
            }

            await _context.SaveChangesAsync();

            var settings = _siteSettingsService.GetSettings();
            var katIndirimleri = kategoriler
                .Where(k => k.IndirimYuzdesi.HasValue)
                .ToDictionary(k => k.Id, k => k.IndirimYuzdesi!.Value);
            FiyatPolitikasiHelper.Configure(settings.GenelIndirimYuzdesi, katIndirimleri);

            TempData["Basari"] = $"{degisen} kategorinin özel indirim oranı güncellendi.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> KategoriIndirimiSifirla(int kategoriId)
        {
            var kategori = await _context.Kategoriler.FirstOrDefaultAsync(k => k.Id == kategoriId && !k.SilindiMi);
            if (kategori == null)
            {
                TempData["Hata"] = "Kategori bulunamadı.";
                return RedirectToAction(nameof(Index));
            }

            kategori.IndirimYuzdesi = null;
            await _context.SaveChangesAsync();

            var settings = _siteSettingsService.GetSettings();
            var katIndirimleri = await _context.Kategoriler
                .AsNoTracking()
                .Where(k => !k.SilindiMi && k.IndirimYuzdesi.HasValue)
                .ToDictionaryAsync(k => k.Id, k => k.IndirimYuzdesi!.Value);
            FiyatPolitikasiHelper.Configure(settings.GenelIndirimYuzdesi, katIndirimleri);

            TempData["Basari"] = $"'{kategori.Ad}' kategorisi genel mağaza indirim oranına (%{settings.GenelIndirimYuzdesi:0.##}) bağlandı.";
            return RedirectToAction(nameof(Index));
        }
    }

    public class TopluIndirimViewModel
    {
        public decimal GenelIndirimYuzdesi { get; set; } = 20m;
        public List<KategoriIndirimSatirViewModel> Kategoriler { get; set; } = new();
    }

    public class KategoriIndirimSatirViewModel
    {
        public int Id { get; set; }
        public string Ad { get; set; } = string.Empty;
        public string? UstKategoriAdi { get; set; }
        public bool AktifMi { get; set; }
        public decimal? IndirimYuzdesi { get; set; }
        public int UrunSayisi { get; set; }
    }

    public class KategoriIndirimGirdiModel
    {
        public int KategoriId { get; set; }
        public decimal? IndirimYuzdesi { get; set; }
    }
}
