using KanvasProje.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanvasProje.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class IletisimController : AdminBaseController
    {
        private readonly KanvasDbContext _context;

        public IletisimController(KanvasDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(bool unreadOnly = false, string? q = null)
        {
            var query = _context.IletisimMesajlari
                .AsNoTracking()
                .AsQueryable();

            if (unreadOnly)
            {
                query = query.Where(x => !x.OkunduMu);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var search = q.Trim().ToLower();
                query = query.Where(x =>
                    x.AdSoyad.ToLower().Contains(search) ||
                    x.Email.ToLower().Contains(search) ||
                    x.Konu.ToLower().Contains(search) ||
                    x.Mesaj.ToLower().Contains(search));
            }

            ViewBag.UnreadOnly = unreadOnly;
            ViewBag.Search = q?.Trim();
            ViewBag.TotalCount = await _context.IletisimMesajlari.CountAsync();
            ViewBag.UnreadCount = await _context.IletisimMesajlari.CountAsync(x => !x.OkunduMu);
            ViewBag.ReadCount = await _context.IletisimMesajlari.CountAsync(x => x.OkunduMu);
            var turkeyTodayDate = DateTime.UtcNow.AddHours(3).Date;
            var todayStartUtc = DateTime.SpecifyKind(turkeyTodayDate.AddHours(-3), DateTimeKind.Utc);
            var todayStartLocal = DateTime.SpecifyKind(turkeyTodayDate, DateTimeKind.Utc);
            ViewBag.TodayCount = await _context.IletisimMesajlari.CountAsync(x => x.Tarih >= todayStartUtc || x.Tarih >= todayStartLocal);

            var messages = await query
                .OrderBy(x => x.OkunduMu)
                .ThenByDescending(x => x.Tarih)
                .Take(500)
                .ToListAsync();

            return View(messages);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OkunduYap(int id)
        {
            var message = await _context.IletisimMesajlari.FirstOrDefaultAsync(x => x.Id == id);
            if (message != null)
            {
                message.OkunduMu = true;
                await _context.SaveChangesAsync();
                TempData["Mesaj"] = "İletişim mesajı okundu olarak işaretlendi.";
                TempData["Durum"] = "success";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OkunmadiYap(int id)
        {
            var message = await _context.IletisimMesajlari.FirstOrDefaultAsync(x => x.Id == id);
            if (message != null)
            {
                message.OkunduMu = false;
                await _context.SaveChangesAsync();
                TempData["Mesaj"] = "İletişim mesajı tekrar okunmamış olarak işaretlendi.";
                TempData["Durum"] = "success";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TopluOkunduYap(List<int>? mesajIds)
        {
            mesajIds = (mesajIds ?? new List<int>()).Where(x => x > 0).Distinct().ToList();
            if (!mesajIds.Any())
            {
                TempData["Hata"] = "İşlem yapmak için en az bir mesaj seçin.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index));
            }

            var messages = await _context.IletisimMesajlari
                .Where(x => mesajIds.Contains(x.Id))
                .ToListAsync();

            foreach (var message in messages)
            {
                message.OkunduMu = true;
            }

            await _context.SaveChangesAsync();

            TempData["Mesaj"] = $"{messages.Count} mesaj okundu olarak işaretlendi.";
            TempData["Durum"] = "success";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Detay(int id)
        {
            var message = await _context.IletisimMesajlari.FirstOrDefaultAsync(x => x.Id == id);
            if (message == null)
            {
                TempData["Hata"] = "Mesaj bulunamadı.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index));
            }

            if (!message.OkunduMu)
            {
                message.OkunduMu = true;
                await _context.SaveChangesAsync();
            }

            return View(message);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Sil(int id)
        {
            var message = await _context.IletisimMesajlari.FirstOrDefaultAsync(x => x.Id == id);
            if (message != null)
            {
                _context.IletisimMesajlari.Remove(message);
                await _context.SaveChangesAsync();
                TempData["Mesaj"] = "İletişim mesajı silindi.";
                TempData["Durum"] = "success";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
