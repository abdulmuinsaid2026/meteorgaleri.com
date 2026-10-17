using KanvasProje.Core.Varliklar;
using KanvasProje.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanvasProje.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class KuponController : AdminBaseController
    {
        private readonly KanvasDbContext _context;

        public KuponController(KanvasDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var kuponlar = await _context.Kuponlar
                .Where(x => !x.SilindiMi)
                .OrderByDescending(x => x.OlusturulmaTarihi)
                .ToListAsync();

            var now = DateTime.UtcNow;
            ViewBag.ToplamKupon = kuponlar.Count;
            ViewBag.AktifKupon = kuponlar.Count(x => IsCouponUsable(x, now));
            ViewBag.SuresiDolan = kuponlar.Count(x => x.SonKullanmaTarihi <= now);
            ViewBag.ToplamKullanim = kuponlar.Sum(x => x.KullanilanMiktar);

            return View(kuponlar);
        }

        [HttpGet]
        public IActionResult Ekle()
        {
            return View(new Kupon
            {
                AktifMi = true,
                KullanimLimiti = 100,
                SonKullanmaTarihi = DateTime.UtcNow.AddMonths(1)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Ekle(Kupon model)
        {
            NormalizeCouponForSave(model);

            if (!await ValidateCouponAsync(model))
            {
                return View(model);
            }

            model.OlusturulmaTarihi = DateTime.UtcNow;
            model.SilindiMi = false;
            model.KullanilanMiktar = 0;

            _context.Kuponlar.Add(model);
            await _context.SaveChangesAsync();

            TempData["Mesaj"] = $"{model.Kod} kuponu oluşturuldu.";
            TempData["Durum"] = "success";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Duzenle(int id)
        {
            var kupon = await _context.Kuponlar.FirstOrDefaultAsync(x => x.Id == id && !x.SilindiMi);
            if (kupon == null)
            {
                return NotFound();
            }

            return View("Ekle", kupon);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Duzenle(int id, Kupon model)
        {
            var kupon = await _context.Kuponlar.FirstOrDefaultAsync(x => x.Id == id && !x.SilindiMi);
            if (kupon == null)
            {
                return NotFound();
            }

            NormalizeCouponForSave(model);

            if (!await ValidateCouponAsync(model, id))
            {
                model.Id = id;
                model.KullanilanMiktar = kupon.KullanilanMiktar;
                model.OlusturulmaTarihi = kupon.OlusturulmaTarihi;
                return View("Ekle", model);
            }

            kupon.Kod = model.Kod;
            kupon.Tip = model.Tip;
            kupon.Deger = model.Deger;
            kupon.MinSepetTutari = model.MinSepetTutari;
            kupon.KullanimLimiti = model.KullanimLimiti;
            kupon.SonKullanmaTarihi = model.SonKullanmaTarihi;
            kupon.AktifMi = model.AktifMi;

            await _context.SaveChangesAsync();

            TempData["Mesaj"] = $"{kupon.Kod} kuponu güncellendi.";
            TempData["Durum"] = "success";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DurumDegistir(int id)
        {
            var kupon = await _context.Kuponlar.FirstOrDefaultAsync(x => x.Id == id && !x.SilindiMi);
            if (kupon == null)
            {
                return NotFound();
            }

            kupon.AktifMi = !kupon.AktifMi;
            await _context.SaveChangesAsync();

            TempData["Mesaj"] = kupon.AktifMi
                ? $"{kupon.Kod} kuponu aktif edildi."
                : $"{kupon.Kod} kuponu pasife alındı.";
            TempData["Durum"] = "success";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Sil(int id)
        {
            var kupon = await _context.Kuponlar.FirstOrDefaultAsync(x => x.Id == id && !x.SilindiMi);
            if (kupon == null)
            {
                return RedirectToAction(nameof(Index));
            }

            kupon.SilindiMi = true;
            kupon.AktifMi = false;
            await _context.SaveChangesAsync();

            TempData["Mesaj"] = $"{kupon.Kod} kuponu arşive alındı.";
            TempData["Durum"] = "success";
            return RedirectToAction(nameof(Index));
        }

        private async Task<bool> ValidateCouponAsync(Kupon model, int? currentId = null)
        {
            if (string.IsNullOrWhiteSpace(model.Kod))
            {
                ModelState.AddModelError(nameof(Kupon.Kod), "Kupon kodu zorunludur.");
            }

            if (model.Kod.Length > 20)
            {
                ModelState.AddModelError(nameof(Kupon.Kod), "Kupon kodu en fazla 20 karakter olabilir.");
            }

            if (model.Tip is not (0 or 1))
            {
                ModelState.AddModelError(nameof(Kupon.Tip), "Geçerli bir indirim tipi seçin.");
            }

            if (model.Deger <= 0)
            {
                ModelState.AddModelError(nameof(Kupon.Deger), "İndirim değeri sıfırdan büyük olmalıdır.");
            }

            if (model.Tip == 0 && model.Deger > 100)
            {
                ModelState.AddModelError(nameof(Kupon.Deger), "Yüzde indirim 100'den büyük olamaz.");
            }

            if (model.MinSepetTutari < 0)
            {
                ModelState.AddModelError(nameof(Kupon.MinSepetTutari), "Minimum sepet tutarı negatif olamaz.");
            }

            if (model.KullanimLimiti < 0)
            {
                ModelState.AddModelError(nameof(Kupon.KullanimLimiti), "Kullanım limiti negatif olamaz. Sınırsız için 0 yazabilirsiniz.");
            }

            if (model.SonKullanmaTarihi.Year < 2020)
            {
                ModelState.AddModelError(nameof(Kupon.SonKullanmaTarihi), "Geçerli bir son kullanım tarihi seçin.");
            }

            var duplicateExists = await _context.Kuponlar.AnyAsync(x =>
                !x.SilindiMi &&
                x.Kod == model.Kod &&
                (!currentId.HasValue || x.Id != currentId.Value));

            if (duplicateExists)
            {
                ModelState.AddModelError(nameof(Kupon.Kod), "Bu kupon kodu zaten kullanılıyor.");
            }

            return ModelState.IsValid;
        }

        private static void NormalizeCouponForSave(Kupon model)
        {
            model.Kod = NormalizeCouponCode(model.Kod);
            model.Deger = Math.Round(model.Deger, 2);
            model.MinSepetTutari = Math.Round(model.MinSepetTutari, 2);
            model.SonKullanmaTarihi = ToUtcEndOfDay(model.SonKullanmaTarihi);
        }

        private static string NormalizeCouponCode(string? code)
        {
            return (code ?? string.Empty)
                .Trim()
                .ToUpperInvariant()
                .Replace(" ", string.Empty);
        }

        private static DateTime ToUtcEndOfDay(DateTime date)
        {
            var turkeyDayEndUnspecified = DateTime.SpecifyKind(date.Date.AddDays(1).AddTicks(-1), DateTimeKind.Unspecified);
            return DateTime.SpecifyKind(turkeyDayEndUnspecified.AddHours(-3), DateTimeKind.Utc);
        }

        private static bool IsCouponUsable(Kupon kupon, DateTime now)
        {
            var hasUsageRight = kupon.KullanimLimiti <= 0 || kupon.KullanilanMiktar < kupon.KullanimLimiti;
            return kupon.AktifMi && kupon.SonKullanmaTarihi > now && hasUsageRight;
        }
    }
}
