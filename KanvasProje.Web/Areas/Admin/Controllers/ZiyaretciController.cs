using KanvasProje.Core.Models;
using KanvasProje.Core.Varliklar;
using KanvasProje.Data;
using KanvasProje.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace KanvasProje.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class ZiyaretciController : AdminBaseController
    {
        private readonly KanvasDbContext _context;

        public ZiyaretciController(KanvasDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(
            string? q,
            string? metod,
            string? cihaz,
            DateTime? baslangic,
            DateTime? bitis,
            int page = 1,
            int pageSize = 20)
        {
            page = Math.Max(1, page);
            pageSize = NormalizePageSize(pageSize);

            var query = BuildFilteredQuery(q, metod, cihaz, baslangic, bitis);
            var totalCount = await query.CountAsync();
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            page = Math.Min(page, totalPages);

            var kayitlar = await query
                .OrderByDescending(x => x.OlusturulmaTarihi)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var utcNow = DateTime.UtcNow;
            var turkeyTodayDate = utcNow.AddHours(3).Date;
            var todayStart = DateTime.SpecifyKind(turkeyTodayDate.AddHours(-3), DateTimeKind.Utc);
            var onlineStart = utcNow.AddMinutes(-5);

            var allLogsQuery = _context.ZiyaretciLoglari.AsNoTracking();
            var filteredLogs = await query
                .OrderByDescending(x => x.OlusturulmaTarihi)
                .Take(10000)
                .ToListAsync();

            var topPages = filteredLogs
                .Where(x => string.Equals(x.Metod, "GET", StringComparison.OrdinalIgnoreCase))
                .GroupBy(x => string.IsNullOrWhiteSpace(x.Url) ? "/" : x.Url)
                .Select(g => new ZiyaretciStatItem
                {
                    Etiket = g.Key,
                    Adet = g.Count(),
                    Tekil = g.Select(x => x.IpAdresi).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Count()
                })
                .OrderByDescending(x => x.Adet)
                .Take(5)
                .ToList();

            var topSources = filteredLogs
                .GroupBy(x => NormalizeReferer(x.ReferansUrl))
                .Select(g => new ZiyaretciStatItem
                {
                    Etiket = g.Key,
                    Adet = g.Count(),
                    Tekil = g.Select(x => x.IpAdresi).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Count()
                })
                .OrderByDescending(x => x.Adet)
                .Take(5)
                .ToList();

            var model = new ZiyaretciIndexViewModel
            {
                Kayitlar = kayitlar,
                Arama = q?.Trim() ?? string.Empty,
                Metod = metod?.Trim() ?? string.Empty,
                Cihaz = cihaz?.Trim() ?? string.Empty,
                Baslangic = baslangic,
                Bitis = bitis,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                OnlineSayisi = await allLogsQuery
                    .Where(x => x.OlusturulmaTarihi >= onlineStart)
                    .Select(x => x.IpAdresi)
                    .Distinct()
                    .CountAsync(),
                BugunTekil = await allLogsQuery
                    .Where(x => x.OlusturulmaTarihi >= todayStart)
                    .Select(x => x.IpAdresi)
                    .Distinct()
                    .CountAsync(),
                BugunZiyaret = await allLogsQuery.CountAsync(x => x.OlusturulmaTarihi >= todayStart),
                ToplamTekil = filteredLogs.Select(x => x.IpAdresi).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Count(),
                MobilZiyaret = filteredLogs.Count(IsMobile),
                MasaustuZiyaret = filteredLogs.Count(x => !IsMobile(x)),
                EnCokGezilenSayfa = topPages.FirstOrDefault()?.Etiket ?? "-",
                EnGucluKaynak = topSources.FirstOrDefault()?.Etiket ?? "-",
                TopSayfalar = topPages,
                TopKaynaklar = topSources
            };

            return View(model);
        }

        public async Task<IActionResult> Export(string? q, string? metod, string? cihaz, DateTime? baslangic, DateTime? bitis)
        {
            ExcelPackage.License.SetNonCommercialOrganization("MeteorGaleri");
            var kayitlar = await BuildFilteredQuery(q, metod, cihaz, baslangic, bitis)
                .OrderByDescending(x => x.OlusturulmaTarihi)
                .Take(10000)
                .ToListAsync();

            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("Ziyaretçi Trafiği");
            var headers = new[]
            {
                "Tarih",
                "IP Adresi",
                "Ülke",
                "Şehir",
                "Metod",
                "URL",
                "Referans",
                "Kullanıcı",
                "Cihaz",
                "Tarayıcı",
                "İşletim Sistemi"
            };

            for (var i = 0; i < headers.Length; i++)
            {
                worksheet.Cells[1, i + 1].Value = headers[i];
            }

            var row = 2;
            foreach (var item in kayitlar)
            {
                worksheet.Cells[row, 1].Value = item.OlusturulmaTarihi;
                worksheet.Cells[row, 2].Value = item.IpAdresi;
                worksheet.Cells[row, 3].Value = item.Ulke;
                worksheet.Cells[row, 4].Value = item.Sehir;
                worksheet.Cells[row, 5].Value = item.Metod;
                worksheet.Cells[row, 6].Value = item.Url;
                worksheet.Cells[row, 7].Value = item.ReferansUrl;
                worksheet.Cells[row, 8].Value = item.KullaniciAdi;
                worksheet.Cells[row, 9].Value = item.CihazModeli;
                worksheet.Cells[row, 10].Value = item.Tarayici;
                worksheet.Cells[row, 11].Value = item.IsletimSistemi;
                row++;
            }

            using (var range = worksheet.Cells[1, 1, 1, headers.Length])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(49, 53, 17));
                range.Style.Font.Color.SetColor(System.Drawing.Color.White);
            }

            worksheet.Column(1).Style.Numberformat.Format = "dd.mm.yyyy hh:mm:ss";
            if (worksheet.Dimension != null)
            {
                worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
            }

            return File(
                package.GetAsByteArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"ziyaretci-trafigi-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
        }

        public Task<IActionResult> Indir(string? q, string? metod, string? cihaz, DateTime? baslangic, DateTime? bitis)
        {
            return Task.FromResult<IActionResult>(RedirectToAction(nameof(Export), new { q, metod, cihaz, baslangic, bitis }));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Temizle()
        {
            if (!AdminSecurityRoles.UserHasAnyRole(
                    User,
                    new[] { AdminSecurityRoles.SuperAdmin, AdminSecurityRoles.LegacyAdmin, AdminSecurityRoles.Yonetici }))
            {
                TempData["Hata"] = "Ziyaretçi trafik geçmişini temizleme yetkiniz yok.";
                return RedirectToAction(nameof(Index));
            }

            await _context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"ZiyaretciLoglari\"");
            TempData["Basari"] = "Ziyaretçi trafik geçmişi temizlendi.";
            TempData["Mesaj"] = "Ziyaretçi trafik geçmişi temizlendi.";
            TempData["Durum"] = "success";
            return RedirectToAction(nameof(Index));
        }

        private IQueryable<ZiyaretciLog> BuildFilteredQuery(string? q, string? metod, string? cihaz, DateTime? baslangic, DateTime? bitis)
        {
            var query = _context.ZiyaretciLoglari.AsNoTracking().AsQueryable();
            var term = q?.Trim();

            if (!string.IsNullOrWhiteSpace(term))
            {
                var normalized = term.ToLower();
                query = query.Where(x =>
                    (x.IpAdresi ?? string.Empty).ToLower().Contains(normalized) ||
                    (x.Url ?? string.Empty).ToLower().Contains(normalized) ||
                    (x.ReferansUrl ?? string.Empty).ToLower().Contains(normalized) ||
                    (x.KullaniciAdi ?? string.Empty).ToLower().Contains(normalized) ||
                    (x.Sehir ?? string.Empty).ToLower().Contains(normalized) ||
                    (x.Ulke ?? string.Empty).ToLower().Contains(normalized) ||
                    (x.Tarayici ?? string.Empty).ToLower().Contains(normalized) ||
                    (x.IsletimSistemi ?? string.Empty).ToLower().Contains(normalized) ||
                    (x.CihazModeli ?? string.Empty).ToLower().Contains(normalized));
            }

            if (!string.IsNullOrWhiteSpace(metod) && !string.Equals(metod, "Tumu", StringComparison.OrdinalIgnoreCase))
            {
                var methodValue = metod.Trim().ToUpperInvariant();
                query = query.Where(x => x.Metod.ToUpper() == methodValue);
            }

            if (!string.IsNullOrWhiteSpace(cihaz) && !string.Equals(cihaz, "Tumu", StringComparison.OrdinalIgnoreCase))
            {
                var deviceValue = cihaz.Trim().ToLowerInvariant();
                if (deviceValue == "mobil")
                {
                    query = query.Where(x =>
                        ((x.CihazModeli ?? string.Empty) + " " + (x.IsletimSistemi ?? string.Empty)).ToLower().Contains("iphone") ||
                        ((x.CihazModeli ?? string.Empty) + " " + (x.IsletimSistemi ?? string.Empty)).ToLower().Contains("android") ||
                        ((x.CihazModeli ?? string.Empty) + " " + (x.IsletimSistemi ?? string.Empty)).ToLower().Contains("mobile"));
                }
                else if (deviceValue == "masaustu")
                {
                    query = query.Where(x =>
                        !(((x.CihazModeli ?? string.Empty) + " " + (x.IsletimSistemi ?? string.Empty)).ToLower().Contains("iphone") ||
                          ((x.CihazModeli ?? string.Empty) + " " + (x.IsletimSistemi ?? string.Empty)).ToLower().Contains("android") ||
                          ((x.CihazModeli ?? string.Empty) + " " + (x.IsletimSistemi ?? string.Empty)).ToLower().Contains("mobile")));
                }
            }

            if (baslangic.HasValue)
            {
                var start = DateTime.SpecifyKind(baslangic.Value.Date, DateTimeKind.Utc);
                query = query.Where(x => x.OlusturulmaTarihi >= start);
            }

            if (bitis.HasValue)
            {
                var end = DateTime.SpecifyKind(bitis.Value.Date.AddDays(1), DateTimeKind.Utc);
                query = query.Where(x => x.OlusturulmaTarihi < end);
            }

            return query;
        }

        private static int NormalizePageSize(int pageSize)
        {
            return pageSize is 50 or 100 ? pageSize : 20;
        }

        private static bool IsMobile(ZiyaretciLog log)
        {
            var text = $"{log.CihazModeli} {log.IsletimSistemi}".ToLowerInvariant();
            return text.Contains("iphone") || text.Contains("android") || text.Contains("mobile");
        }

        private static string NormalizeReferer(string? referer)
        {
            if (string.IsNullOrWhiteSpace(referer))
            {
                return "Direkt / Bilinmiyor";
            }

            if (Uri.TryCreate(referer, UriKind.Absolute, out var uri))
            {
                return uri.Host.Replace("www.", string.Empty);
            }

            return referer.Length > 48 ? referer[..48] : referer;
        }
    }
}
