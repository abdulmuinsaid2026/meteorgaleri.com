using KanvasProje.Core.Interfaces;
using KanvasProje.Core.Varliklar;
using KanvasProje.Data;
using KanvasProje.Service.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace KanvasProje.Web.Controllers
{
    [Authorize]
    public class ProfilController : Controller
    {
        private const int IadeHakkiGun = 14;

        private readonly UserManager<AppUser> _userManager;
        private readonly SignInManager<AppUser> _signInManager;
        private readonly KanvasDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ISiteSettingsService _siteSettingsService;
        private readonly ILogger<ProfilController> _logger;

        public ProfilController(
            UserManager<AppUser> userManager,
            SignInManager<AppUser> signInManager,
            KanvasDbContext context,
            IEmailService emailService,
            ISiteSettingsService siteSettingsService,
            ILogger<ProfilController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _context = context;
            _emailService = emailService;
            _siteSettingsService = siteSettingsService;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var siparislerim = await GetOwnedOrdersQuery(user)
                .OrderByDescending(x => x.OlusturulmaTarihi)
                .ToListAsync();

            ViewBag.SiparisSayisi = siparislerim.Count;
            var toplamHarcama = siparislerim
                .Where(x => x.Durum is >= 1 and < 4 && x.ToplamTutar > 0 && x.ToplamTutar <= 1_000_000)
                .Sum(x => x.ToplamTutar);
            ViewBag.ToplamHarcama = toplamHarcama.ToString("N2", new System.Globalization.CultureInfo("tr-TR"));
            return View(user);
        }

        public async Task<IActionResult> Siparislerim()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var siparislerim = await GetOwnedOrdersQuery(user)
                .OrderByDescending(x => x.OlusturulmaTarihi)
                .ToListAsync();

            return View(siparislerim);
        }

        public async Task<IActionResult> SiparisDetay(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var siparis = await GetOwnedOrdersQuery(user)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (siparis == null)
            {
                return RedirectToAction("ErisimEngellendi", "Hesap");
            }

            var iade = await _context.IadeTalepleri
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.SiparisId == id && !x.SilindiMi);

            if (iade != null)
            {
                ViewBag.IadeDurumu = iade.Durum switch
                {
                    0 => "Inceleniyor",
                    1 => "Onaylandi (Kargo Bekleniyor)",
                    2 => "Iade Tamamlandi",
                    _ => "Reddedildi"
                };
            }

            var siparisKalemleri = await _context.SiparisDetaylari
                .AsNoTracking()
                .Include(x => x.Urun)
                .Include(x => x.UrunSecenek)
                .Where(x => x.SiparisId == id && !x.SilindiMi)
                .ToListAsync();

            ViewBag.Urunler = siparisKalemleri
                .Where(x => x.Urun != null)
                .Select(x => new
                {
                    Resim = !string.IsNullOrWhiteSpace(x.UrunSecenek?.GorselUrl) ? x.UrunSecenek.GorselUrl : x.Urun!.AnaGorselUrl,
                    Baslik = x.Urun.Baslik,
                    Olcu = x.UrunSecenek?.Olcu ?? string.Empty,
                    Cerceve = x.UrunSecenek?.CerceveTipi ?? string.Empty,
                    Secenek = !string.IsNullOrWhiteSpace(x.SecenekAdi)
                        ? x.SecenekAdi
                        : (x.UrunSecenek == null
                            ? "Standart urun"
                            : (string.IsNullOrWhiteSpace(x.UrunSecenek.VaryantBasligi) ? "Varsayilan varyasyon" : x.UrunSecenek.VaryantBasligi)),
                    SecenekDetay = x.UrunSecenek?.VaryantOzeti ?? string.Empty,
                    CerceveModeli = x.CerceveModeli,
                    Adet = x.Adet,
                    Fiyat = x.BirimFiyat,
                    Toplam = x.Adet * x.BirimFiyat,
                    UrunId = x.Urun!.Id,
                    MusteriNotu = x.MusteriNotu
                })
                .ToList();

            return View(siparis);
        }

        [HttpGet]
        public async Task<IActionResult> IadeOlustur(int siparisId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var siparis = await GetOwnedOrdersQuery(user)
                .FirstOrDefaultAsync(x => x.Id == siparisId);

            if (siparis == null)
            {
                return NotFound();
            }

            if (siparis.Durum != 3)
            {
                TempData["Hata"] = "Sadece teslim edilmis siparisler icin iade talebi olusturabilirsiniz.";
                return RedirectToAction(nameof(SiparisDetay), new { id = siparisId });
            }

            if (!IadeSuresiDevamEdiyor(siparis))
            {
                TempData["Hata"] = $"{IadeHakkiGun} gunluk iade talebi suresi dolmustur.";
                return RedirectToAction(nameof(SiparisDetay), new { id = siparisId });
            }

            var mevcutTalep = await _context.IadeTalepleri
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.SiparisId == siparisId && !x.SilindiMi);

            if (mevcutTalep != null)
            {
                TempData["Bilgi"] = "Bu siparis icin zaten bir iade talebiniz bulunmaktadir.";
                return RedirectToAction(nameof(SiparisDetay), new { id = siparisId });
            }

            return View(siparis);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IadeOlustur([Bind("SiparisId,Neden,Aciklama,IBAN")] IadeTalebi talep)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var siparis = await GetOwnedOrdersQuery(user)
                .FirstOrDefaultAsync(x => x.Id == talep.SiparisId);

            if (siparis == null)
            {
                return RedirectToAction("ErisimEngellendi", "Hesap");
            }

            if (siparis.Durum != 3)
            {
                TempData["Hata"] = "Iade talebi sadece teslim edilen siparisler icin olusturulabilir.";
                return RedirectToAction(nameof(SiparisDetay), new { id = talep.SiparisId });
            }

            if (!IadeSuresiDevamEdiyor(siparis))
            {
                TempData["Hata"] = $"{IadeHakkiGun} gunluk iade talebi suresi dolmustur.";
                return RedirectToAction(nameof(SiparisDetay), new { id = talep.SiparisId });
            }

            talep.Neden = talep.Neden?.Trim() ?? string.Empty;
            talep.Aciklama = talep.Aciklama?.Trim();
            talep.IBAN = talep.IBAN?.Trim();

            if (string.IsNullOrWhiteSpace(talep.Neden))
            {
                TempData["Hata"] = "Iade nedeni zorunludur.";
                return RedirectToAction(nameof(IadeOlustur), new { siparisId = talep.SiparisId });
            }

            var mevcutTalep = await _context.IadeTalepleri
                .FirstOrDefaultAsync(x => x.SiparisId == talep.SiparisId && !x.SilindiMi);

            if (mevcutTalep != null)
            {
                TempData["Bilgi"] = "Bu siparis icin zaten bir iade talebi bulunuyor.";
                return RedirectToAction(nameof(SiparisDetay), new { id = talep.SiparisId });
            }

            talep.MusteriId = user.Id;
            talep.OlusturulmaTarihi = DateTime.UtcNow;
            talep.Durum = 0;
            talep.SilindiMi = false;

            _context.IadeTalepleri.Add(talep);
            siparis.Durum = 5;

            await _context.SaveChangesAsync();
            await SendReturnRequestEmailsAsync(talep, siparis);

            TempData["Basari"] = "Iade talebiniz alindi. Siparis durumu guncellendi.";
            return RedirectToAction(nameof(SiparisDetay), new { id = talep.SiparisId });
        }

        public async Task<IActionResult> Adreslerim()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var adreslerim = await _context.Adresler
                .AsNoTracking()
                .Where(x => x.AppUserId == user.Id && !x.SilindiMi)
                .OrderByDescending(x => x.OlusturulmaTarihi)
                .ToListAsync();

            return View(adreslerim);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdresEkle(Adres adres)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            adres.AppUserId = user.Id;
            adres.Baslik = adres.Baslik?.Trim() ?? string.Empty;
            adres.AdSoyad = adres.AdSoyad?.Trim() ?? string.Empty;
            adres.Telefon = adres.Telefon?.Trim() ?? string.Empty;
            adres.Sehir = adres.Sehir?.Trim() ?? string.Empty;
            adres.Ilce = adres.Ilce?.Trim() ?? string.Empty;
            adres.AcikAdres = adres.AcikAdres?.Trim() ?? string.Empty;
            adres.OlusturulmaTarihi = DateTime.UtcNow;
            adres.SilindiMi = false;

            _context.Adresler.Add(adres);
            await _context.SaveChangesAsync();

            TempData["Mesaj"] = "Adres basariyla eklendi.";
            return RedirectToAction(nameof(Adreslerim));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdresSil(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var adres = await _context.Adresler
                .FirstOrDefaultAsync(x => x.Id == id && x.AppUserId == user.Id && !x.SilindiMi);

            if (adres != null)
            {
                adres.SilindiMi = true;
                await _context.SaveChangesAsync();
                TempData["Mesaj"] = "Adres basariyla silindi.";
            }

            return RedirectToAction(nameof(Adreslerim));
        }

        [HttpGet]
        public IActionResult HesabiSil()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> HesabiSilOnayla(string sifre)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var passwordCheck = await _userManager.CheckPasswordAsync(user, sifre);
            if (!passwordCheck)
            {
                ViewBag.Hata = "Girdiginiz sifre yanlis. Lutfen tekrar deneyin.";
                return View("HesabiSil");
            }

            var result = await _userManager.DeleteAsync(user);
            if (result.Succeeded)
            {
                await _signInManager.SignOutAsync();
                TempData["Bilgi"] = "Hesabiniz basariyla silindi. Bizi tercih ettiginiz icin tesekkur ederiz.";
                return RedirectToAction("Index", "Home");
            }

            ViewBag.Hata = "Hesap silinirken bir hata olustu. Lutfen tekrar deneyin.";
            return View("HesabiSil");
        }

        private IQueryable<Siparis> GetOwnedOrdersQuery(AppUser user)
        {
            var normalizedEmail = user.Email ?? string.Empty;
            return _context.Siparisler.Where(x =>
                !x.SilindiMi &&
                (x.AppUserId == user.Id ||
                 (string.IsNullOrEmpty(x.AppUserId) && x.Eposta == normalizedEmail)));
        }

        private static bool IadeSuresiDevamEdiyor(Siparis siparis)
        {
            return DateTime.UtcNow <= siparis.OlusturulmaTarihi.ToUniversalTime().AddDays(IadeHakkiGun);
        }

        private async Task SendReturnRequestEmailsAsync(IadeTalebi talep, Siparis siparis)
        {
            var settings = _siteSettingsService.GetSettings();
            if (!settings.IadeTalebiMailBildirimi)
            {
                return;
            }

            var brandName = string.IsNullOrWhiteSpace(settings.MarkaAdi) ? settings.SiteAdi : settings.MarkaAdi;
            var siteUrl = _siteSettingsService.BuildAbsoluteUrl(string.Empty);
            var orderNumber = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(siparis.SiparisNo) ? $"#{siparis.Id}" : siparis.SiparisNo);
            var customerName = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(siparis.MusteriAdSoyad) ? "Degerli Musterimiz" : siparis.MusteriAdSoyad);
            var returnReason = WebUtility.HtmlEncode(talep.Neden);
            var returnDescription = string.IsNullOrWhiteSpace(talep.Aciklama)
                ? string.Empty
                : $"<p><strong>Aciklama:</strong> {WebUtility.HtmlEncode(talep.Aciklama)}</p>";
            var productRows = await BuildReturnRequestProductRowsAsync(siparis.Id);
            var profileUrl = Url.Action(nameof(SiparisDetay), "Profil", new { id = siparis.Id }, Request.Scheme)
                ?? $"{siteUrl}/Profil/SiparisDetay/{siparis.Id}";

            if (!string.IsNullOrWhiteSpace(siparis.Eposta))
            {
                try
                {
                    var customerContent = $@"
                        <p>Merhaba <strong>{customerName}</strong>,</p>
                        <p><strong>{orderNumber}</strong> numarali siparisiniz icin iade talebiniz alinmistir. Ekibimiz talebinizi inceleyip sonraki adimlar icin sizinle iletisime gececektir.</p>
                        <p><strong>Iade nedeni:</strong> {returnReason}</p>
                        {returnDescription}
                        <table role='presentation' width='100%' cellpadding='0' cellspacing='0' style='border:1px solid #e5e2dc; border-radius:10px; background:#fff; margin:16px 0;'>
                            <thead>
                                <tr style='background:#fafaf8;'>
                                    <th style='padding:10px; text-align:left; color:#313511;'>Urun</th>
                                    <th style='padding:10px; text-align:center; color:#313511;'>Adet</th>
                                    <th style='padding:10px; text-align:right; color:#313511;'>Tutar</th>
                                </tr>
                            </thead>
                            <tbody>{productRows}</tbody>
                        </table>
                        <p>Iade surecini hesabinizdaki siparis detayindan takip edebilirsiniz.</p>";

                    await _emailService.SendTemplateMailAsync(
                        siparis.Eposta,
                        $"{brandName} - Iade Talebiniz Alindi ({orderNumber})",
                        customerName,
                        customerContent,
                        profileUrl,
                        "Siparisi Goruntule");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Musteri iade talebi maili gonderilemedi. SiparisNo={SiparisNo}, Email={Email}", siparis.SiparisNo, siparis.Eposta);
                }
            }

            var recipientEmail = string.IsNullOrWhiteSpace(settings.BildirimAliciEmail)
                ? settings.Email
                : settings.BildirimAliciEmail;

            if (string.IsNullOrWhiteSpace(recipientEmail))
            {
                return;
            }

            try
            {
                var adminUrl = Url.Action("Detay", "Iade", new { area = "Admin", id = talep.Id }, Request.Scheme)
                    ?? $"{siteUrl}/Admin/Iade/Detay/{talep.Id}";
                var customerEmail = WebUtility.HtmlEncode(siparis.Eposta ?? "-");
                var customerPhone = WebUtility.HtmlEncode(siparis.Telefon ?? "-");

                var adminContent = $@"
                    <h3>Yeni iade talebi var</h3>
                    <p><strong>Siparis No:</strong> {orderNumber}</p>
                    <p><strong>Musteri:</strong> {customerName}</p>
                    <p><strong>E-posta:</strong> {customerEmail}</p>
                    <p><strong>Telefon:</strong> {customerPhone}</p>
                    <p><strong>Iade nedeni:</strong> {returnReason}</p>
                    {returnDescription}
                    <table role='presentation' width='100%' cellpadding='0' cellspacing='0' style='border:1px solid #e5e2dc; border-radius:10px; background:#fff; margin:16px 0;'>
                        <thead>
                            <tr style='background:#fafaf8;'>
                                <th style='padding:10px; text-align:left; color:#313511;'>Urun</th>
                                <th style='padding:10px; text-align:center; color:#313511;'>Adet</th>
                                <th style='padding:10px; text-align:right; color:#313511;'>Tutar</th>
                            </tr>
                        </thead>
                        <tbody>{productRows}</tbody>
                    </table>";

                await _emailService.SendTemplateMailAsync(
                    recipientEmail,
                    $"{brandName} - Yeni Iade Talebi {siparis.SiparisNo}",
                    "Operasyon Ekibi",
                    adminContent,
                    adminUrl,
                    "Iade Talebini Incele");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Admin iade talebi maili gonderilemedi. SiparisNo={SiparisNo}, TalepId={TalepId}", siparis.SiparisNo, talep.Id);
            }
        }

        private async Task<string> BuildReturnRequestProductRowsAsync(int siparisId)
        {
            var detaylar = await _context.SiparisDetaylari
                .Include(x => x.Urun)
                .Include(x => x.UrunSecenek)
                .Where(x => x.SiparisId == siparisId && !x.SilindiMi)
                .ToListAsync();

            var rows = new System.Text.StringBuilder();
            foreach (var item in detaylar)
            {
                var productName = WebUtility.HtmlEncode(item.Urun?.Baslik ?? "Urun");
                var variant = WebUtility.HtmlEncode(item.UrunSecenek?.VaryantOzeti ?? item.CerceveModeli ?? string.Empty);
                var note = string.IsNullOrWhiteSpace(item.MusteriNotu)
                    ? string.Empty
                    : $"<div style='margin-top:4px; font-size:12px; color:#b58735;'>Not: {WebUtility.HtmlEncode(item.MusteriNotu)}</div>";

                rows.Append($@"
                    <tr>
                        <td style='padding:12px; border-bottom:1px solid #e5e2dc; color:#47473d;'>
                            <div>{productName}</div>
                            {(string.IsNullOrWhiteSpace(variant) ? string.Empty : $"<div style='margin-top:4px; font-size:12px; color:#6f6a5e;'>{variant}</div>")}
                            {note}
                        </td>
                        <td style='padding:12px; border-bottom:1px solid #e5e2dc; text-align:center; color:#47473d;'>{item.Adet}</td>
                        <td style='padding:12px; border-bottom:1px solid #e5e2dc; text-align:right; color:#313511; font-weight:600;'>{(item.Adet * item.BirimFiyat):N2} TL</td>
                    </tr>");
            }

            return rows.Length > 0
                ? rows.ToString()
                : "<tr><td colspan='3' style='padding:12px; color:#6f6a5e;'>Urun detayi bulunamadi.</td></tr>";
        }
    }
}
