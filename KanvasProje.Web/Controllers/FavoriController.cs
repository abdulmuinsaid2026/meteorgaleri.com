using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using KanvasProje.Core.Varliklar;
using KanvasProje.Data;
using KanvasProje.Service.Services;
using Microsoft.AspNetCore.Authorization;

namespace KanvasProje.Web.Controllers
{
    // DİKKAT: [Authorize] buradan kaldırıldı, aşağıya özel olarak eklendi.
    public class FavoriController : Controller
    {
        private readonly KanvasDbContext _context;
        private readonly UserManager<AppUser> _userManager;
        private readonly IFavoriService _favoriService;

        public FavoriController(KanvasDbContext context, UserManager<AppUser> userManager, IFavoriService favoriService)
        {
            _context = context;
            _userManager = userManager;
            _favoriService = favoriService;
        }

        // 1. Favorilerim Sayfası (Burası Korumalı - Giriş yapmayan giremez)
        [Authorize]
        public async Task<IActionResult> Index()
        {
            var userId = User.Identity?.IsAuthenticated == true ? _userManager.GetUserId(User) : null;

            var favoriler = await _context.Favoriler
                .AsNoTracking()
                .Include(f => f.Urun)
                .Where(f => f.AppUserId == userId &&
                            !f.SilindiMi &&
                            f.Urun != null &&
                            f.Urun.AktifMi &&
                            !f.Urun.SilindiMi)
                .ToListAsync();

            return View(favoriler);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Toggle(int urunId)
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                return Json(new { success = false, message = "Lütfen önce giriş yapınız." });
            }

            if (urunId <= 0)
            {
                return Json(new { success = false, message = "Geçersiz ürün." });
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Json(new { success = false, message = "Kullanıcı bulunamadı." });

            var favori = await _context.Favoriler
                .FirstOrDefaultAsync(x => x.UrunId == urunId && x.AppUserId == user.Id);

            if (favori != null)
            {
                _context.Favoriler.Remove(favori);
                await _context.SaveChangesAsync();
                return Json(new { success = true, isAdded = false });
            }

            var urunVarMi = await _context.Urunler
                .AsNoTracking()
                .AnyAsync(u => u.Id == urunId && u.AktifMi && !u.SilindiMi);

            if (!urunVarMi)
            {
                return Json(new { success = false, message = "Ürün bulunamadı veya satışta değil." });
            }

            try
            {
                var yeniFavori = new Favori
                {
                    UrunId = urunId,
                    AppUserId = user.Id,
                    OlusturulmaTarihi = DateTime.UtcNow
                };
                _context.Favoriler.Add(yeniFavori);
                await _context.SaveChangesAsync();
                return Json(new { success = true, isAdded = true });
            }
            catch (DbUpdateException)
            {
                return Json(new { success = true, isAdded = true });
            }
        }

        /// <summary>
        /// Fiyat düşüş bildirimini aç/kapat
        /// </summary>
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TogglePriceNotification(int urunId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Json(new { success = false, message = "Kullanıcı bulunamadı." });

            var favoriVarMi = await _context.Favoriler
                .AsNoTracking()
                .AnyAsync(f => f.AppUserId == user.Id && f.UrunId == urunId && !f.SilindiMi);

            if (!favoriVarMi)
            {
                return Json(new { success = false, message = "Bu ürün favori listenizde bulunamadı." });
            }

            try
            {
                var isEnabled = await _favoriService.TogglePriceNotificationAsync(user.Id, urunId);
                return Json(new
                {
                    success = true,
                    isEnabled,
                    message = isEnabled
                        ? "Bu ürünün fiyatı düştüğünde size e-posta ile bildireceğiz."
                        : "Fiyat düşüş bildirimi kapatıldı."
                });
            }
            catch
            {
                return Json(new { success = false, message = "Bu ürün favori listenizde bulunamadı." });
            }
        }
    }
}