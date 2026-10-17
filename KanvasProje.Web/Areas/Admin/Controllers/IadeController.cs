using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KanvasProje.Data;
using KanvasProje.Core.Varliklar;
using KanvasProje.Core.Helpers;
using KanvasProje.Service.Services;

namespace KanvasProje.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class IadeController : AdminBaseController
    {
        private const int DefaultIadeHakkiGun = 14;

        private readonly KanvasDbContext _context;
        private readonly ISiteSettingsService _siteSettingsService;

        public IadeController(KanvasDbContext context, ISiteSettingsService siteSettingsService)
        {
            _context = context;
            _siteSettingsService = siteSettingsService;
        }

        private int GetIadeHakkiGun()
        {
            var configured = _siteSettingsService.GetSettings().IadeHakkiGun;
            return configured > 0 ? configured : DefaultIadeHakkiGun;
        }

        private int GetTeslimSuresiGun()
        {
            var configured = _siteSettingsService.GetSettings().SiparisTeslimSuresiGun;
            return configured >= 0 ? configured : 3;
        }

        public async Task<IActionResult> Index()
        {
            var talepler = await IadeTalebiQuery()
                .OrderByDescending(x => x.OlusturulmaTarihi)
                .ToListAsync();

            return View(talepler);
        }

        public async Task<IActionResult> Detay(int id)
        {
            var talep = await IadeTalebiQuery()
                .FirstOrDefaultAsync(x => x.Id == id);

            if (talep == null)
            {
                return NotFound();
            }

            var iadeHakkiGun = GetIadeHakkiGun();
            ViewBag.IadeHakkiGun = iadeHakkiGun;
            ViewBag.IadeSonTarih = GetIadeSonTarihUtc(talep);
            ViewBag.IadeTalepZamanindaMi = IadeTalebiZamanindaMi(talep);

            return View(talep);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DurumGuncelle(int id, int durum, string adminNotu)
        {
            var talep = await _context.IadeTalepleri
                .Include(x => x.Siparis)
                    .ThenInclude(s => s.SiparisDetaylari)
                        .ThenInclude(d => d.UrunSecenek)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (talep == null) return NotFound();

            if (durum is < 1 or > 3)
            {
                TempData["Hata"] = "Gecersiz iade durumu.";
                return RedirectToAction(nameof(Detay), new { id });
            }

            if (talep.Durum is 2 or 3 && talep.Durum != durum)
            {
                TempData["Hata"] = "Tamamlanmış veya reddedilmiş bir iade talebinin durumu değiştirilemez.";
                return RedirectToAction(nameof(Detay), new { id });
            }

            if (durum == 2 && talep.Durum != 1 && talep.Durum != 2)
            {
                TempData["Hata"] = "İade tamamlanmadan önce talep onaylanmalıdır.";
                return RedirectToAction(nameof(Detay), new { id });
            }

            var iadeHakkiGun = GetIadeHakkiGun();
            if (durum == 1 && !IadeTalebiZamanindaMi(talep))
            {
                TempData["Hata"] = $"{iadeHakkiGun} gunluk iade suresi disindaki talep onaylanamaz.";
                return RedirectToAction(nameof(Detay), new { id });
            }

            var eskiDurum = talep.Durum;
            talep.Durum = durum; // 1: Onay, 2: Tamamlandı, 3: Red
            talep.AdminNotu = adminNotu?.Trim();

            var siparis = talep.Siparis;
            if (siparis != null)
            {
                if (durum == 1)
                {
                    siparis.Durum = SiparisDurumHelper.IadeOnaylandi;
                }
                else if (durum == 2)
                {
                    siparis.Durum = SiparisDurumHelper.IadeTamamlandi;
                    if (eskiDurum != 2 && siparis.SiparisDetaylari != null)
                    {
                        foreach (var detay in siparis.SiparisDetaylari.Where(d => !d.SilindiMi))
                        {
                            if (detay.UrunSecenek != null)
                            {
                                detay.UrunSecenek.StokAdedi += detay.Adet;
                            }
                        }
                    }
                }
                else if (durum == 3 && SiparisDurumHelper.IsReturn(siparis.Durum))
                {
                    siparis.Durum = SiparisDurumHelper.TeslimEdildi;
                }
            }

            await _context.SaveChangesAsync();
            TempData["Mesaj"] = "Iade talebi guncellendi.";
            return RedirectToAction(nameof(Detay), new { id });
        }

        private IQueryable<IadeTalebi> IadeTalebiQuery()
        {
            return _context.IadeTalepleri
                .AsSplitQuery()
                .Include(x => x.Siparis)
                    .ThenInclude(x => x.SiparisDetaylari)
                        .ThenInclude(x => x.Urun)
                            .ThenInclude(x => x.UrunResimleri)
                .Include(x => x.Siparis)
                    .ThenInclude(x => x.SiparisDetaylari)
                        .ThenInclude(x => x.UrunSecenek)
                .Where(x => !x.SilindiMi);
        }

        private DateTime GetIadeSonTarihUtc(IadeTalebi talep)
        {
            var bazTarih = (talep.Siparis.OdemeOnayTarihi ?? talep.Siparis.OlusturulmaTarihi).ToUniversalTime();
            return bazTarih.AddDays(GetTeslimSuresiGun() + GetIadeHakkiGun());
        }

        private bool IadeTalebiZamanindaMi(IadeTalebi talep)
        {
            return talep.OlusturulmaTarihi.ToUniversalTime() <= GetIadeSonTarihUtc(talep);
        }
    }
}
