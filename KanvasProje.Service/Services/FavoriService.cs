using KanvasProje.Core.Varliklar;
using KanvasProje.Data;
using Microsoft.EntityFrameworkCore;

namespace KanvasProje.Service.Services
{
    public interface IFavoriService
    {
        Task<List<Favori>> GetFavorilerWithPriceNotificationAsync();
        Task<Favori?> GetByIdAsync(int id);
        Task UpdatePriceAsync(int id, decimal yeniFiyat);
        Task<bool> TogglePriceNotificationAsync(string userId, int urunId);
    }

    public class FavoriService : IFavoriService
    {
        private readonly KanvasDbContext _context;

        public FavoriService(KanvasDbContext context)
        {
            _context = context;
        }

        public async Task<List<Favori>> GetFavorilerWithPriceNotificationAsync()
        {
            return await _context.Favoriler
                .Include(f => f.Urun)
                .Include(f => f.AppUser)
                .Where(f => f.FiyatDustugundaBildir &&
                            !f.SilindiMi &&
                            f.Urun != null &&
                            f.Urun.AktifMi &&
                            !f.Urun.SilindiMi &&
                            f.AppUser.EmailConfirmed)
                .ToListAsync();
        }

        public async Task<Favori?> GetByIdAsync(int id)
        {
            return await _context.Favoriler
                .Include(f => f.Urun)
                .FirstOrDefaultAsync(f => f.Id == id);
        }

        public async Task UpdatePriceAsync(int id, decimal yeniFiyat)
        {
            var favori = await _context.Favoriler.FindAsync(id);
            if (favori != null)
            {
                if (favori.EskiFiyat != yeniFiyat)
                {
                    favori.EskiFiyat = yeniFiyat;
                    await _context.SaveChangesAsync();
                }
            }
        }

        public async Task<bool> TogglePriceNotificationAsync(string userId, int urunId)
        {
            var favori = await _context.Favoriler
                .FirstOrDefaultAsync(f => f.AppUserId == userId && f.UrunId == urunId && !f.SilindiMi);

            if (favori == null) return false;

            favori.FiyatDustugundaBildir = !favori.FiyatDustugundaBildir;

            if (favori.FiyatDustugundaBildir)
            {
                var urun = await _context.Urunler.FindAsync(urunId);
                if (urun != null)
                {
                    favori.EskiFiyat = urun.EtkinFiyat;
                }
            }
            else
            {
                favori.EskiFiyat = null;
                favori.SonBildirimTarihi = null;
            }

            await _context.SaveChangesAsync();
            return favori.FiyatDustugundaBildir;
        }
    }
}