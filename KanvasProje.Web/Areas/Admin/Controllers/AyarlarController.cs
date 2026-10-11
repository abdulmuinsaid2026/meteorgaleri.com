using System.Net.Mail;
using KanvasProje.Core.Interfaces;
using KanvasProje.Core.Models;
using KanvasProje.Core.Varliklar;
using KanvasProje.Data;
using KanvasProje.Service.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanvasProje.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class AyarlarController : AdminBaseController
    {
        private readonly ISiteSettingsService _siteSettingsService;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _config;
        private readonly KanvasDbContext _context;

        public AyarlarController(
            ISiteSettingsService siteSettingsService,
            IEmailService emailService,
            IConfiguration config,
            KanvasDbContext context)
        {
            _siteSettingsService = siteSettingsService;
            _emailService = emailService;
            _config = config;
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            await HazirlaKargoFirmaSecenekleriAsync();
            HazirlaPaytrDurumu();
            return View(_siteSettingsService.GetSettings());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(SiteAyarlari model, string? paytrMerchantKey, string? paytrMerchantSalt)
        {
            try
            {
                var paytrKeyKayitli = _siteSettingsService.HasPaytrMerchantKey();
                var paytrSaltKayitli = _siteSettingsService.HasPaytrMerchantSalt();
                model.IyzicoAktifMi = false;

                if (model.PaytrAktifMi)
                {
                    if (string.IsNullOrWhiteSpace(model.PaytrMerchantId))
                    {
                        TempData["Hata"] = "PayTR aktifken Merchant ID zorunludur.";
                        TempData["Durum"] = "warning";
                        return RedirectToAction(nameof(Index));
                    }

                    if (!paytrKeyKayitli && string.IsNullOrWhiteSpace(paytrMerchantKey))
                    {
                        TempData["Hata"] = "PayTR aktifken Merchant Key zorunludur.";
                        TempData["Durum"] = "warning";
                        return RedirectToAction(nameof(Index));
                    }

                    if (!paytrSaltKayitli && string.IsNullOrWhiteSpace(paytrMerchantSalt))
                    {
                        TempData["Hata"] = "PayTR aktifken Merchant Salt zorunludur.";
                        TempData["Durum"] = "warning";
                        return RedirectToAction(nameof(Index));
                    }
                }

                await VarsayilanKargoFirmasiniGuncelleAsync(model.KargoFirmasi);
                _siteSettingsService.SaveSettings(model, paytrMerchantKey, paytrMerchantSalt);
                TempData["Basari"] = "Site ayarları başarıyla kaydedildi.";
                TempData["Durum"] = "success";
            }
            catch (Exception ex)
            {
                TempData["Hata"] = "Ayarlar kaydedilirken hata oluştu: " + ex.Message;
                TempData["Durum"] = "danger";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TestMail(SiteAyarlari model)
        {
            var recipientEmail = !string.IsNullOrWhiteSpace(model.BildirimAliciEmail)
                ? model.BildirimAliciEmail
                : model.Email;

            if (string.IsNullOrWhiteSpace(recipientEmail))
            {
                TempData["Hata"] = "Test maili için bildirim alıcı e-postası veya site e-postası doldurulmalıdır.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index));
            }

            var smtpUser = _config["EmailSettings:Username"];
            var smtpPassword = _config["EmailSettings:Password"];
            var fromEmail = _config["EmailSettings:FromEmail"];
            if (string.IsNullOrWhiteSpace(fromEmail))
            {
                fromEmail = model.Email;
            }

            if (!IsValidEmail(smtpUser) || string.IsNullOrWhiteSpace(smtpPassword) || !IsValidEmail(fromEmail))
            {
                TempData["Hata"] = "SMTP ayarları eksik. Brevo kullanıcı adı, SMTP anahtarı ve Brevo'da doğrulanmış gönderici e-posta adresi tanımlanmalıdır.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index));
            }

            if (IsBrevoSmtpLoginAddress(fromEmail))
            {
                TempData["Hata"] = "Brevo SMTP login adresi gönderici olarak kullanılamaz. Brevo panelinde doğrulanmış bir sender e-posta adresi tanımlayın.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index));
            }

            if (!IsValidEmail(recipientEmail))
            {
                TempData["Hata"] = "Test maili için geçerli bir alıcı e-posta adresi yazın.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                await _emailService.SendTemplateMailAsync(
                    recipientEmail,
                    "Test Maili",
                    string.IsNullOrWhiteSpace(model.MarkaAdi) ? "MeteorGaleri" : model.MarkaAdi,
                    "Mail altyapısı başarıyla çalışıyor. Sipariş, kargo, üyelik ve kampanya bildirimleri bu kanal üzerinden gönderilecektir.",
                    string.Empty,
                    string.Empty);

                TempData["Basari"] = $"Test maili gönderildi. Alıcı: {recipientEmail}. Gelen kutusunda görünmüyorsa Brevo Transactional Logs alanını kontrol edin.";
                TempData["Durum"] = "success";
            }
            catch (Exception ex)
            {
                TempData["Hata"] = ex is TimeoutException
                    ? "Test maili gönderimi 30 saniye içinde tamamlanamadı. Brevo SMTP bağlantısını ve internet erişimini kontrol edin."
                    : "Test maili gönderilemedi: " + ex.Message;
                TempData["Durum"] = "danger";
            }

            return RedirectToAction(nameof(Index));
        }

        private void HazirlaPaytrDurumu()
        {
            ViewBag.PaytrMerchantKeyKayitli = _siteSettingsService.HasPaytrMerchantKey();
            ViewBag.PaytrMerchantSaltKayitli = _siteSettingsService.HasPaytrMerchantSalt();
        }

        private async Task HazirlaKargoFirmaSecenekleriAsync()
        {
            var firmalar = await _context.KargoFirmalari
                .IgnoreQueryFilters()
                .Where(x => !x.SilindiMi && x.AktifMi)
                .OrderByDescending(x => x.VarsayilanMi)
                .ThenBy(x => x.Ad)
                .ToListAsync();

            if (!firmalar.Any())
            {
                firmalar = VarsayilanKargoFirmalari();
            }

            var seciliFirma = _siteSettingsService.GetSettings().KargoFirmasi;
            if (!string.IsNullOrWhiteSpace(seciliFirma) &&
                firmalar.All(x => !string.Equals(x.Ad, seciliFirma, StringComparison.OrdinalIgnoreCase)))
            {
                firmalar.Insert(0, new KargoFirmasi
                {
                    Ad = seciliFirma,
                    Kod = seciliFirma.ToLowerInvariant().Replace(" ", "-"),
                    AktifMi = true,
                    VarsayilanMi = true
                });
            }

            ViewBag.KargoFirmalari = firmalar;
        }

        private async Task VarsayilanKargoFirmasiniGuncelleAsync(string? firmaAdi)
        {
            if (string.IsNullOrWhiteSpace(firmaAdi))
            {
                return;
            }

            var firma = await _context.KargoFirmalari
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => !x.SilindiMi && x.Ad == firmaAdi.Trim());

            if (firma == null)
            {
                return;
            }

            await _context.KargoFirmalari
                .Where(x => x.Id != firma.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(v => v.VarsayilanMi, false));

            firma.VarsayilanMi = true;
            firma.AktifMi = true;
            await _context.SaveChangesAsync();
        }

        private static List<KargoFirmasi> VarsayilanKargoFirmalari()
        {
            return new List<KargoFirmasi>
            {
                new() { Ad = "Aras Kargo", Kod = "aras", TakipUrl = "https://www.araskargo.com.tr/tr/online-islemler/kargo-takip" },
                new() { Ad = "Yurtiçi Kargo", Kod = "yurtici", TakipUrl = "https://www.yurticikargo.com/tr/online-servisler/gonderi-sorgula" },
                new() { Ad = "MNG Kargo", Kod = "mng", TakipUrl = "https://www.mngkargo.com.tr/gonderi-takip" },
                new() { Ad = "PTT Kargo", Kod = "ptt", TakipUrl = "https://gonderitakip.ptt.gov.tr" },
                new() { Ad = "Sürat Kargo", Kod = "surat", TakipUrl = "https://www.suratkargo.com.tr/KargoTakip" }
            };
        }

        private static bool IsValidEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return false;
            }

            try
            {
                _ = new MailAddress(email.Trim());
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static bool IsBrevoSmtpLoginAddress(string? email)
        {
            return !string.IsNullOrWhiteSpace(email)
                && email.Trim().EndsWith("@smtp-brevo.com", StringComparison.OrdinalIgnoreCase);
        }
    }
}
