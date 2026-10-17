using KanvasProje.Core.Interfaces;
using KanvasProje.Core.Varliklar;
using KanvasProje.Data;
using KanvasProje.Service.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanvasProje.Web.Controllers
{
    public class KurumsalController : Controller
    {
        private readonly KanvasDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ISiteSettingsService _siteSettingsService;
        private readonly ILogger<KurumsalController> _logger;

        public KurumsalController(
            KanvasDbContext context,
            IEmailService emailService,
            ISiteSettingsService siteSettingsService,
            ILogger<KurumsalController> logger)
        {
            _context = context;
            _emailService = emailService;
            _siteSettingsService = siteSettingsService;
            _logger = logger;
        }

        [Route("Kurumsal/Detay/{slug}")]
        public async Task<IActionResult> Detay(string slug)
        {
            var sayfa = await _context.KurumsalSayfalar
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.UrlSlug == slug && !x.SilindiMi);
            if (sayfa == null)
            {
                return NotFound();
            }

            ViewData["Title"] = sayfa.Baslik;
            return View(sayfa);
        }

        public IActionResult Hakkimizda() => View();

        [HttpGet]
        public IActionResult Iletisim()
        {
            var settings = _siteSettingsService.GetSettings();
            ViewBag.Telefon = settings.Telefon;
            ViewBag.Eposta = settings.Email;
            ViewBag.Adres = settings.Adres;
            ViewBag.CalismaSaatleri = settings.CalismaSaatleri;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
        public async Task<IActionResult> IletisimGonder([Bind("AdSoyad,Email,Konu,Mesaj")] IletisimMesaj model)
        {
            var isAjax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                || Request.Headers.Accept.Any(a => a != null && a.Contains("application/json", StringComparison.OrdinalIgnoreCase));

            model.AdSoyad = model.AdSoyad?.Trim() ?? string.Empty;
            model.Email = model.Email?.Trim() ?? string.Empty;
            model.Konu = model.Konu?.Trim() ?? string.Empty;
            model.Mesaj = model.Mesaj?.Trim() ?? string.Empty;

            if (!ModelState.IsValid ||
                string.IsNullOrWhiteSpace(model.AdSoyad) ||
                string.IsNullOrWhiteSpace(model.Email) ||
                string.IsNullOrWhiteSpace(model.Konu) ||
                string.IsNullOrWhiteSpace(model.Mesaj))
            {
                const string errMsg = "Lütfen tüm alanları eksiksiz doldurun.";
                if (isAjax)
                {
                    return Json(new { success = false, message = errMsg });
                }

                TempData["Hata"] = errMsg;
                return RedirectToAction(nameof(Iletisim));
            }

            model.Id = 0;
            model.Tarih = DateTime.UtcNow;
            model.IpAdresi = Request.HttpContext.Connection.RemoteIpAddress?.ToString();
            model.OkunduMu = false;

            _context.IletisimMesajlari.Add(model);
            await _context.SaveChangesAsync();

            var senderName = System.Net.WebUtility.HtmlEncode(model.AdSoyad);
            var senderEmail = System.Net.WebUtility.HtmlEncode(model.Email);
            var subject = System.Net.WebUtility.HtmlEncode(model.Konu);
            var message = System.Net.WebUtility.HtmlEncode(model.Mesaj).Replace(Environment.NewLine, "<br>");
            var ipAddress = System.Net.WebUtility.HtmlEncode(model.IpAdresi);

            var adminMailIcerik = $@"
                <h3>Yeni ileti&#351;im mesaj&#305; var</h3>
                <p><b>G&ouml;nderen:</b> {senderName} ({senderEmail})</p>
                <p><b>Konu:</b> {subject}</p>
                <p><b>Mesaj:</b><br>{message}</p>
                <hr>
                <small>IP: {ipAddress}</small>";

            var siteSettings = _siteSettingsService.GetSettings();
            var recipientEmail = string.IsNullOrWhiteSpace(siteSettings.BildirimAliciEmail)
                ? siteSettings.Email
                : siteSettings.BildirimAliciEmail;

            if (!string.IsNullOrWhiteSpace(recipientEmail))
            {
                try
                {
                    await _emailService.SendTemplateMailAsync(recipientEmail, "Yeni \u0130leti\u015Fim Formu: " + model.Konu, "Operasyon Ekibi", adminMailIcerik);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Iletisim formu mail bildirimi gonderilemedi. MesajId={MesajId}, Alici={Alici}", model.Id, recipientEmail);
                }
            }

            const string okMsg = "Mesajınız başarıyla iletildi. En kısa sürede dönüş yapacağız.";
            if (isAjax)
            {
                return Json(new { success = true, message = okMsg });
            }

            TempData["Basari"] = okMsg;
            return RedirectToAction(nameof(Iletisim));
        }

        public IActionResult SSS() => View();
        public IActionResult BankaHesaplari() => View();
        public IActionResult Gizlilik() => View();
        public IActionResult KullaniciSozlesmesi() => View();
        public IActionResult MesafeliSatis() => View();
        public IActionResult IadeKosullari() => View();
    }
}
