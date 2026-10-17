using KanvasProje.Core.Models;
using KanvasProje.Core.Interfaces;
using KanvasProje.Core.Varliklar;
using KanvasProje.Data;
using KanvasProje.Service.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Net;
using System.Net.Mail;

namespace KanvasProje.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class BultenController : AdminBaseController
    {
        private readonly KanvasDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ISiteSettingsService _siteSettingsService;
        private readonly ILogger<BultenController> _logger;

        public BultenController(
            KanvasDbContext context,
            IEmailService emailService,
            ISiteSettingsService siteSettingsService,
            ILogger<BultenController> logger)
        {
            _context = context;
            _emailService = emailService;
            _siteSettingsService = siteSettingsService;
            _logger = logger;
        }

        public async Task<IActionResult> Index(int durum = 1, string? q = null, int page = 1, int pageSize = 20)
        {
            page = Math.Max(page, 1);
            pageSize = pageSize is 20 or 50 or 100 ? pageSize : 20;

            var query = BuildSubscriberQuery(durum, q);
            var totalCount = await query.CountAsync();
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            page = Math.Min(page, totalPages);

            var aboneler = await query
                .OrderByDescending(x => x.KayitTarihi)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var model = await BuildSubscriberListAsync(aboneler);

            ViewBag.Durum = durum;
            ViewBag.Search = q?.Trim();
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;
            ViewBag.ActiveCount = await _context.BultenAbonelikleri.CountAsync(x => x.AktifMi);
            ViewBag.PassiveCount = await _context.BultenAbonelikleri.CountAsync(x => !x.AktifMi);

            var todayStart = DateTime.UtcNow.Date.AddHours(3);
            var tomorrowStart = todayStart.AddDays(1);
            ViewBag.TodayCount = await _context.BultenAbonelikleri.CountAsync(x => x.KayitTarihi >= todayStart && x.KayitTarihi < tomorrowStart);

            return View(model);
        }

        public async Task<IActionResult> Export(int durum = 1, string? q = null)
        {
            var aboneler = await BuildSubscriberQuery(durum, q)
                .OrderByDescending(x => x.KayitTarihi)
                .ToListAsync();
            var model = await BuildSubscriberListAsync(aboneler);

            ExcelPackage.License.SetNonCommercialOrganization("MeteorGaleri");
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("Bülten Aboneleri");
            var headers = new[] { "Id", "Durum", "E-Posta", "Kayıt Tarihi", "IP Adresi", "Şehir", "Ülke", "Cihaz", "Tarayıcı", "İşletim Sistemi" };

            for (var i = 0; i < headers.Length; i++)
            {
                worksheet.Cells[1, i + 1].Value = headers[i];
            }

            using (var range = worksheet.Cells[1, 1, 1, headers.Length])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(49, 53, 17));
                range.Style.Font.Color.SetColor(System.Drawing.Color.White);
            }

            for (var i = 0; i < model.Count; i++)
            {
                var item = model[i];
                var row = i + 2;
                worksheet.Cells[row, 1].Value = item.Id;
                worksheet.Cells[row, 2].Value = item.AktifMi ? "Aktif" : "Pasif";
                worksheet.Cells[row, 3].Value = item.Email;
                worksheet.Cells[row, 4].Value = item.KayitTarihi.ToString("dd.MM.yyyy HH:mm");
                worksheet.Cells[row, 5].Value = item.IpAdresi;
                worksheet.Cells[row, 6].Value = item.Sehir;
                worksheet.Cells[row, 7].Value = item.Ulke;
                worksheet.Cells[row, 8].Value = item.Cihaz;
                worksheet.Cells[row, 9].Value = item.Tarayici;
                worksheet.Cells[row, 10].Value = item.IsletimSistemi;
            }

            if (worksheet.Dimension != null)
            {
                worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
            }

            return File(
                package.GetAsByteArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"bulten-aboneleri-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PasifYap(int id, int durum = 1, string? q = null, int page = 1, int pageSize = 20)
        {
            var abone = await _context.BultenAbonelikleri.FirstOrDefaultAsync(x => x.Id == id);
            if (abone != null)
            {
                abone.AktifMi = false;
                await _context.SaveChangesAsync();
                TempData["Mesaj"] = "Bülten aboneliği pasif hale getirildi.";
                TempData["Durum"] = "success";
            }

            return RedirectToAction(nameof(Index), new { durum, q, page, pageSize });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AktifYap(int id, int durum = 0, string? q = null, int page = 1, int pageSize = 20)
        {
            var abone = await _context.BultenAbonelikleri.FirstOrDefaultAsync(x => x.Id == id);
            if (abone != null)
            {
                abone.AktifMi = true;
                await _context.SaveChangesAsync();
                TempData["Mesaj"] = "Bülten aboneliği tekrar aktif hale getirildi.";
                TempData["Durum"] = "success";
            }

            return RedirectToAction(nameof(Index), new { durum, q, page, pageSize });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TopluPasifYap(List<int>? aboneIds, int durum = 1, string? q = null, int page = 1, int pageSize = 20)
        {
            aboneIds = (aboneIds ?? new List<int>()).Where(x => x > 0).Distinct().ToList();
            if (!aboneIds.Any())
            {
                TempData["Hata"] = "İşlem yapmak için en az bir abone seçin.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index), new { durum, q, page, pageSize });
            }

            var aboneler = await _context.BultenAbonelikleri
                .Where(x => aboneIds.Contains(x.Id))
                .ToListAsync();

            foreach (var abone in aboneler)
            {
                abone.AktifMi = false;
            }

            await _context.SaveChangesAsync();
            TempData["Mesaj"] = $"{aboneler.Count} bülten aboneliği pasif hale getirildi.";
            TempData["Durum"] = "success";

            return RedirectToAction(nameof(Index), new { durum, q, page, pageSize });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TopluMailGonder(
            string baslik,
            string icerik,
            string? butonLink,
            string? butonYazi,
            string hedef = "aktif",
            List<int>? mailAboneIds = null,
            int durum = 1,
            string? q = null,
            int page = 1,
            int pageSize = 20)
        {
            baslik = (baslik ?? string.Empty).Trim();
            icerik = (icerik ?? string.Empty).Trim();
            butonLink = (butonLink ?? string.Empty).Trim();
            butonYazi = (butonYazi ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(baslik) || string.IsNullOrWhiteSpace(icerik))
            {
                TempData["Hata"] = "Mail başlığı ve mesaj içeriği zorunludur.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index), new { durum, q, page, pageSize });
            }

            if (baslik.Length > 200 || icerik.Length > 20000)
            {
                TempData["Hata"] = "Mail başlığı en fazla 200, içerik en fazla 20.000 karakter olabilir.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index), new { durum, q, page, pageSize });
            }

            IQueryable<BultenAboneligi> query;
            if (hedef == "secili")
            {
                mailAboneIds = (mailAboneIds ?? new List<int>()).Where(x => x > 0).Distinct().ToList();
                if (!mailAboneIds.Any())
                {
                    TempData["Hata"] = "Seçili abonelere mail göndermek için en az bir abone seçin.";
                    TempData["Durum"] = "warning";
                    return RedirectToAction(nameof(Index), new { durum, q, page, pageSize });
                }

                query = _context.BultenAbonelikleri.Where(x => x.AktifMi && mailAboneIds.Contains(x.Id));
            }
            else if (hedef == "filtre")
            {
                query = BuildSubscriberQuery(durum, q).Where(x => x.AktifMi);
            }
            else if (hedef == "aktif")
            {
                query = _context.BultenAbonelikleri.Where(x => x.AktifMi);
            }
            else
            {
                TempData["Hata"] = "Geçersiz hedef kitle seçimi.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index), new { durum, q, page, pageSize });
            }

            var aboneler = await query
                .AsNoTracking()
                .Select(x => x.Email)
                .Distinct()
                .ToListAsync();

            if (!aboneler.Any())
            {
                TempData["Hata"] = "Gönderilecek aktif bülten abonesi bulunamadı.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index), new { durum, q, page, pageSize });
            }

            var htmlIcerik = BuildNewsletterHtml(icerik);
            var normalizedButtonLink = string.IsNullOrWhiteSpace(butonLink)
                ? string.Empty
                : _siteSettingsService.BuildAbsoluteUrl(butonLink);
            var sent = 0;
            var failed = 0;

            foreach (var email in aboneler)
            {
                if (!IsValidEmail(email))
                {
                    failed++;
                    continue;
                }

                try
                {
                    await _emailService.SendTemplateMailAsync(
                        email,
                        baslik,
                        "Değerli MeteorGaleri Abonesi",
                        htmlIcerik,
                        normalizedButtonLink,
                        string.IsNullOrWhiteSpace(butonYazi) ? "Koleksiyonu İncele" : butonYazi);
                    sent++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogWarning(ex, "Bulten maili gonderilemedi: {Email}", email);
                }
            }

            TempData["Mesaj"] = failed == 0
                ? $"Bülten maili {sent} aktif aboneye gönderildi."
                : $"Bülten maili {sent} aboneye gönderildi, {failed} gönderim başarısız oldu.";
            TempData["Durum"] = failed == 0 ? "success" : "warning";

            return RedirectToAction(nameof(Index), new { durum, q, page, pageSize });
        }

        private IQueryable<BultenAboneligi> BuildSubscriberQuery(int durum, string? q)
        {
            var query = _context.BultenAbonelikleri.AsNoTracking().AsQueryable();

            query = durum switch
            {
                0 => query,
                2 => query.Where(x => !x.AktifMi),
                _ => query.Where(x => x.AktifMi)
            };

            if (!string.IsNullOrWhiteSpace(q))
            {
                var search = q.Trim().ToLower();
                query = query.Where(x =>
                    x.Email.ToLower().Contains(search) ||
                    (x.IpAdresi != null && x.IpAdresi.ToLower().Contains(search)));
            }

            return query;
        }

        private static string BuildNewsletterHtml(string content)
        {
            var encoded = WebUtility.HtmlEncode(content.Trim());
            var paragraphs = encoded
                .Replace("\r\n", "\n")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => $"<p>{x}</p>");

            return string.Join("", paragraphs);
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

        private async Task<List<BultenListViewModel>> BuildSubscriberListAsync(List<BultenAboneligi> aboneler)
        {
            var ips = aboneler
                .Select(x => x.IpAdresi)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            var logs = await _context.ZiyaretciLoglari
                .AsNoTracking()
                .Where(x => x.IpAdresi != null && ips.Contains(x.IpAdresi))
                .OrderByDescending(x => x.OlusturulmaTarihi)
                .ToListAsync();

            var latestLogsByIp = logs
                .GroupBy(x => x.IpAdresi)
                .ToDictionary(x => x.Key!, x => x.First());

            return aboneler.Select(abone =>
            {
                latestLogsByIp.TryGetValue(abone.IpAdresi ?? string.Empty, out var logDetay);

                return new BultenListViewModel
                {
                    Id = abone.Id,
                    Email = abone.Email,
                    KayitTarihi = abone.KayitTarihi,
                    AktifMi = abone.AktifMi,
                    IpAdresi = string.IsNullOrWhiteSpace(abone.IpAdresi) ? "Bilinmiyor" : abone.IpAdresi,
                    Sehir = string.IsNullOrWhiteSpace(logDetay?.Sehir) ? "-" : logDetay.Sehir,
                    Ulke = string.IsNullOrWhiteSpace(logDetay?.Ulke) ? "-" : logDetay.Ulke,
                    Cihaz = string.IsNullOrWhiteSpace(logDetay?.CihazModeli) ? "Bilinmiyor" : logDetay.CihazModeli,
                    IsletimSistemi = string.IsNullOrWhiteSpace(logDetay?.IsletimSistemi) ? "-" : logDetay.IsletimSistemi,
                    Tarayici = string.IsNullOrWhiteSpace(logDetay?.Tarayici) ? "-" : logDetay.Tarayici
                };
            }).ToList();
        }
    }
}
