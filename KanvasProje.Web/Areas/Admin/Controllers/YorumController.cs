using KanvasProje.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace KanvasProje.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class YorumController : AdminBaseController
    {
        private readonly KanvasDbContext _context;

        public YorumController(KanvasDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(int durum = 0, string? q = null, int page = 1, int pageSize = 20)
        {
            page = Math.Max(page, 1);
            pageSize = pageSize is 20 or 50 or 100 ? pageSize : 20;

            var query = BuildReviewQuery(durum, q);
            var totalCount = await query.CountAsync();
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            page = Math.Min(page, totalPages);

            var yorumlar = await query
                .OrderByDescending(x => x.OlusturulmaTarihi)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.Durum = durum;
            ViewBag.Search = q?.Trim();
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;
            ViewBag.PendingCount = await _context.Yorumlar.CountAsync(x => !x.SilindiMi && !x.OnayliMi);
            ViewBag.ApprovedCount = await _context.Yorumlar.CountAsync(x => !x.SilindiMi && x.OnayliMi);
            ViewBag.AverageRating = await _context.Yorumlar
                .Where(x => !x.SilindiMi && x.OnayliMi)
                .Select(x => (double?)x.Puan)
                .AverageAsync() ?? 0;

            return View(yorumlar);
        }

        public async Task<IActionResult> Export(int durum = 0, string? q = null)
        {
            var yorumlar = await BuildReviewQuery(durum, q)
                .OrderByDescending(x => x.OlusturulmaTarihi)
                .ToListAsync();

            ExcelPackage.License.SetNonCommercialOrganization("MeteorGaleri");
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("Yorumlar");
            var headers = new[] { "Id", "Durum", "Ürün", "Müşteri", "Puan", "Yorum", "Tarih" };

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

            for (var i = 0; i < yorumlar.Count; i++)
            {
                var yorum = yorumlar[i];
                var row = i + 2;
                worksheet.Cells[row, 1].Value = yorum.Id;
                worksheet.Cells[row, 2].Value = yorum.OnayliMi ? "Onaylandı" : "Bekliyor";
                worksheet.Cells[row, 3].Value = yorum.Urun?.Baslik ?? "-";
                worksheet.Cells[row, 4].Value = yorum.AdSoyad;
                worksheet.Cells[row, 5].Value = yorum.Puan;
                worksheet.Cells[row, 6].Value = yorum.YorumMetni;
                worksheet.Cells[row, 7].Value = yorum.OlusturulmaTarihi.ToTurkeyString();
            }

            if (worksheet.Dimension != null)
            {
                worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
            }

            return File(
                package.GetAsByteArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"yorumlar-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Onayla(int id)
        {
            var yorum = await _context.Yorumlar.FirstOrDefaultAsync(x => x.Id == id);
            if (yorum != null)
            {
                yorum.SilindiMi = false;
                yorum.OnayliMi = true;
                await _context.SaveChangesAsync();
                TempData["Mesaj"] = "Yorum onaylandı ve ürün sayfasında yayınlanabilir hale geldi.";
                TempData["Durum"] = "success";
            }

            return RedirectToAction(nameof(Index), new { durum = 0 });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OnayiKaldir(int id)
        {
            var yorum = await _context.Yorumlar.FirstOrDefaultAsync(x => x.Id == id && !x.SilindiMi);
            if (yorum != null)
            {
                yorum.OnayliMi = false;
                await _context.SaveChangesAsync();
                TempData["Mesaj"] = "Yorum onayı kaldırıldı.";
                TempData["Durum"] = "success";
            }

            return RedirectToAction(nameof(Index), new { durum = 1 });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TopluOnayla(List<int>? yorumIds)
        {
            yorumIds = (yorumIds ?? new List<int>()).Where(x => x > 0).Distinct().ToList();
            if (!yorumIds.Any())
            {
                TempData["Hata"] = "Onaylamak için en az bir yorum seçin.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index), new { durum = 0 });
            }

            var yorumlar = await _context.Yorumlar
                .Where(x => yorumIds.Contains(x.Id))
                .ToListAsync();

            foreach (var yorum in yorumlar)
            {
                yorum.SilindiMi = false;
                yorum.OnayliMi = true;
            }

            await _context.SaveChangesAsync();

            TempData["Mesaj"] = $"{yorumlar.Count} yorum onaylandı.";
            TempData["Durum"] = "success";
            return RedirectToAction(nameof(Index), new { durum = 0 });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Sil(int id)
        {
            var yorum = await _context.Yorumlar.FirstOrDefaultAsync(x => x.Id == id && !x.SilindiMi);
            if (yorum != null)
            {
                yorum.SilindiMi = true;
                yorum.OnayliMi = false;
                await _context.SaveChangesAsync();
                TempData["Mesaj"] = "Yorum arşive alındı.";
                TempData["Durum"] = "success";
            }

            return RedirectToAction(nameof(Index));
        }

        private IQueryable<KanvasProje.Core.Varliklar.Yorum> BuildReviewQuery(int durum, string? q)
        {
            var query = _context.Yorumlar
                .AsNoTracking()
                .Include(x => x.Urun)
                .AsQueryable();

            query = durum switch
            {
                1 => query.Where(x => !x.SilindiMi && x.OnayliMi),
                2 => query.Where(x => !x.SilindiMi),
                3 => query.Where(x => x.SilindiMi),
                _ => query.Where(x => !x.SilindiMi && !x.OnayliMi)
            };

            if (!string.IsNullOrWhiteSpace(q))
            {
                var search = q.Trim().ToLower();
                query = query.Where(x =>
                    x.AdSoyad.ToLower().Contains(search) ||
                    x.YorumMetni.ToLower().Contains(search) ||
                    (x.Urun != null && x.Urun.Baslik.ToLower().Contains(search)));
            }

            return query;
        }
    }
}
