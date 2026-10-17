using KanvasProje.Core.Models;
using KanvasProje.Core.Varliklar;
using KanvasProje.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace KanvasProje.Service.Services
{
    public interface IHomePageSectionService
    {
        Task<IList<HomePageSection>> GetActiveSectionsAsync();
        Task<HomePageSection?> GetSectionAsync(int id);
        Task CreateSectionAsync(HomePageSection section);
        Task UpdateSectionAsync(HomePageSection section);
        Task DeleteSectionAsync(int id);
        Task<bool?> ToggleSectionEnabledAsync(int id);
        Task SetSectionProductsAsync(int sectionId, IList<int> orderedProductIds);
    }

    public class HomePageSectionService : IHomePageSectionService
    {
        private const string CacheKey = "home-page-sections";
        private readonly KanvasDbContext _context;
        private readonly IMemoryCache _cache;

        public HomePageSectionService(KanvasDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        public async Task<IList<HomePageSection>> GetActiveSectionsAsync()
        {
            return await _cache.GetOrCreateAsync(CacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                var sections = await _context.HomePageSections
                    .AsNoTracking()
                    .Where(s => s.Enabled)
                    .OrderBy(s => s.SortOrder)
                    .Include(s => s.SectionProducts)
                    .ThenInclude(sp => sp.Urun)
                    .ToListAsync();
                return sections ?? new List<HomePageSection>();
            }) ?? new List<HomePageSection>();
        }

        public async Task<HomePageSection?> GetSectionAsync(int id)
        {
            return await _context.HomePageSections
                .AsNoTracking()
                .Include(s => s.SectionProducts)
                .ThenInclude(sp => sp.Urun)
                .FirstOrDefaultAsync(s => s.Id == id);
        }

        public async Task CreateSectionAsync(HomePageSection section)
        {
            _context.HomePageSections.Add(section);
            await _context.SaveChangesAsync();
            _cache.Remove(CacheKey);
        }

        public async Task UpdateSectionAsync(HomePageSection section)
        {
            _context.HomePageSections.Update(section);
            await _context.SaveChangesAsync();
            _cache.Remove(CacheKey);
        }

        public async Task DeleteSectionAsync(int id)
        {
            var entity = await _context.HomePageSections.FindAsync(id);
            if (entity != null)
            {
                _context.HomePageSections.Remove(entity);
                await _context.SaveChangesAsync();
                _cache.Remove(CacheKey);
            }
        }

        public async Task<bool?> ToggleSectionEnabledAsync(int id)
        {
            var entity = await _context.HomePageSections.FindAsync(id);
            if (entity == null)
            {
                return null;
            }

            entity.Enabled = !entity.Enabled;
            await _context.SaveChangesAsync();
            _cache.Remove(CacheKey);
            return entity.Enabled;
        }

        public async Task SetSectionProductsAsync(int sectionId, IList<int> orderedProductIds)
        {
            // Remove existing links
            var existing = await _context.HomePageSectionProducts
                .Where(sp => sp.SectionId == sectionId)
                .ToListAsync();
            _context.HomePageSectionProducts.RemoveRange(existing);

            // Add new links preserving order
            for (int i = 0; i < orderedProductIds.Count; i++)
            {
                var link = new HomePageSectionProduct
                {
                    SectionId = sectionId,
                    UrunId = orderedProductIds[i],
                    SortOrder = i
                };
                _context.HomePageSectionProducts.Add(link);
            }

            await _context.SaveChangesAsync();
            _cache.Remove(CacheKey);
        }
    }
}
