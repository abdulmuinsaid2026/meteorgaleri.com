using Microsoft.AspNetCore.Mvc;
using KanvasProje.Data;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;

namespace KanvasProje.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class SearchController : AdminBaseController
    {
        private readonly KanvasDbContext _context;

        public SearchController(KanvasDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? q)
        {
            if (string.IsNullOrWhiteSpace(q)) return RedirectToAction("Index", "Home");

            var rawQuery = q.Trim();
            var normalized = rawQuery.ToLower();
            var hasIntId = int.TryParse(rawQuery, out var parsedId);

            // 1. Ürünlerde Ara
            var urunler = await _context.Urunler
                .AsNoTracking()
                .Where(x => !x.SilindiMi &&
                            (x.Baslik.ToLower().Contains(normalized) ||
                             (x.SKU != null && x.SKU.ToLower().Contains(normalized)) ||
                             (hasIntId && x.Id == parsedId)))
                .OrderByDescending(x => x.Id)
                .Take(25)
                .Select(x => new SearchResult
                {
                    Baslik = x.Baslik,
                    Tip = "Ürün",
                    Url = $"/Admin/Urun/Duzenle/{x.Id}",
                    Detay = $"{x.Fiyat:N2} TL"
                })
                .ToListAsync();

            // 2. Siparişlerde Ara
            var siparisler = await _context.Siparisler
                .AsNoTracking()
                .Where(x => !x.SilindiMi &&
                            ((x.SiparisNo != null && x.SiparisNo.ToLower().Contains(normalized)) ||
                             x.MusteriAdSoyad.ToLower().Contains(normalized) ||
                             (x.Eposta != null && x.Eposta.ToLower().Contains(normalized)) ||
                             (hasIntId && x.Id == parsedId)))
                .OrderByDescending(x => x.Id)
                .Take(25)
                .Select(x => new SearchResult
                {
                    Baslik = $"#{x.SiparisNo} - {x.MusteriAdSoyad}",
                    Tip = "Sipariş",
                    Url = $"/Admin/Siparis/Detay/{x.Id}",
                    Detay = $"{x.ToplamTutar:N2} TL"
                })
                .ToListAsync();

            // 3. Müşterilerde Ara
            var musteriler = await _context.Users
                .AsNoTracking()
                .Where(x => (x.Email != null && x.Email.ToLower().Contains(normalized)) ||
                            (x.UserName != null && x.UserName.ToLower().Contains(normalized)) ||
                            (x.AdSoyad != null && x.AdSoyad.ToLower().Contains(normalized)))
                .Take(25)
                .Select(x => new SearchResult
                {
                    Baslik = x.AdSoyad ?? x.Email ?? "İsimsiz",
                    Tip = "Müşteri",
                    Url = $"/Admin/Kullanici/Duzenle/{x.Id}",
                    Detay = x.Email ?? ""
                })
                .ToListAsync();

            // 4. Kuponlarda Ara
            var kuponlar = await _context.Kuponlar
                .AsNoTracking()
                .Where(x => !x.SilindiMi && x.Kod.ToLower().Contains(normalized))
                .Take(15)
                .Select(x => new SearchResult
                {
                    Baslik = x.Kod,
                    Tip = "Kupon",
                    Url = $"/Admin/Kupon/Duzenle/{x.Id}",
                    Detay = x.Tip == 0 ? $"%{x.Deger:0.##}" : $"{x.Deger:N2} TL"
                })
                .ToListAsync();

            // 5. Sonuçları Birleştir
            var sonuclar = new List<SearchResult>();
            sonuclar.AddRange(urunler);
            sonuclar.AddRange(siparisler);
            sonuclar.AddRange(musteriler);
            sonuclar.AddRange(kuponlar);

            ViewBag.ArananKelime = rawQuery;
            return View(sonuclar);
        }
    }

    // Basit bir DTO sınıfı
    public class SearchResult
    {
        public string Baslik { get; set; } = string.Empty;
        public string Tip { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Detay { get; set; } = string.Empty;
    }
}
