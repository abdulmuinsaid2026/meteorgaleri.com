using KanvasProje.Data;
using KanvasProje.Core.Helpers;
using KanvasProje.Core.Models;
using KanvasProje.Service.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanvasProje.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class HomeController : AdminBaseController
    {
        private readonly KanvasDbContext _context;
        private readonly ISiteSettingsService _settingsService;

        public HomeController(KanvasDbContext context, ISiteSettingsService settingsService)
        {
            _context = context;
            _settingsService = settingsService;
        }

        public async Task<IActionResult> Index()
        {
            var now = DateTime.UtcNow;
            var today = now.Date;
            var weekStart = today.AddDays(-6);
            var monthStart = new DateTime(now.Year, now.Month, 1);
            var settings = _settingsService.GetSettings();
            var stokUyariLimiti = settings.StokUyariLimiti > 0 ? settings.StokUyariLimiti : 5;

            var siparisler = await _context.Siparisler
                .AsNoTracking()
                .Where(x => !x.SilindiMi)
                .ToListAsync();

            var urunler = await _context.Urunler
                .AsNoTracking()
                .Where(x => !x.SilindiMi)
                .ToListAsync();

            var stokOzeti = await _context.UrunSecenekleri
                .AsNoTracking()
                .Where(x => !x.SilindiMi && x.AktifMi)
                .GroupBy(x => x.UrunId)
                .Select(x => new
                {
                    UrunId = x.Key,
                    Stok = x.Sum(v => v.StokAdedi),
                    OnSiparis = x.Any(v => v.OnSipariseAcikMi)
                })
                .ToListAsync();

            var siparisDetaylari = await _context.SiparisDetaylari
                .AsNoTracking()
                .Include(x => x.Urun)
                .Where(x => !x.SilindiMi && x.Urun != null && !x.Urun.SilindiMi)
                .ToListAsync();

            var yorumlar = await _context.Yorumlar
                .AsNoTracking()
                .Include(x => x.Urun)
                .Where(x => !x.SilindiMi)
                .OrderByDescending(x => x.OlusturulmaTarihi)
                .Take(10)
                .ToListAsync();

            var iletisimMesajlari = await _context.IletisimMesajlari
                .AsNoTracking()
                .OrderByDescending(x => x.Tarih)
                .Take(20)
                .ToListAsync();

            var okunmamisMesajSayisi = await _context.IletisimMesajlari
                .AsNoTracking()
                .CountAsync(x => !x.OkunduMu);

            var ziyaretler = await _context.ZiyaretciLoglari
                .AsNoTracking()
                .Where(x => x.OlusturulmaTarihi >= weekStart)
                .ToListAsync();

            var odenmisSiparisler = siparisler
                .Where(x =>
                    (x.Durum == SiparisDurumHelper.UretimHazirlaniyor ||
                     x.Durum == SiparisDurumHelper.Paketleniyor ||
                     x.Durum == SiparisDurumHelper.KargoyaVerildi ||
                     x.Durum == SiparisDurumHelper.TeslimEdildi) &&
                    (!string.Equals(x.OdemeSaglayici, "PayTR", StringComparison.OrdinalIgnoreCase) || x.OdemeTamamlandiMi))
                .ToList();
            var odenmisSiparisIdSet = odenmisSiparisler.Select(x => x.Id).ToHashSet();
            var aktifUrunler = urunler.Where(x => x.AktifMi).ToList();
            var stokLookup = stokOzeti.ToDictionary(x => x.UrunId, x => x);
            var bekleyenSiparisSayisi = siparisler.Count(x => x.Durum == 0);

            var son7Gun = Enumerable.Range(0, 7)
                .Select(offset => weekStart.AddDays(offset))
                .ToList();

            var chartLabels = son7Gun
                .Select(x => x.ToString("dd MMM"))
                .ToList();

            var chartValues = son7Gun
                .Select(day => odenmisSiparisler
                    .Where(x => x.OlusturulmaTarihi.Date == day.Date)
                    .Sum(x => x.ToplamTutar))
                .ToList();

            var topSellingProducts = siparisDetaylari
                .Where(x => x.Urun != null && odenmisSiparisIdSet.Contains(x.SiparisId))
                .GroupBy(x => new { x.UrunId, x.Urun.Baslik, x.Urun.AnaGorselUrl })
                .Select(x => new DashboardProductStatItem
                {
                    ProductId = x.Key.UrunId,
                    ProductName = x.Key.Baslik,
                    ProductImageUrl = x.Key.AnaGorselUrl,
                    Amount = x.Sum(v => v.Adet),
                    AmountLabel = $"{x.Sum(v => v.Adet)} adet"
                })
                .OrderByDescending(x => x.Amount)
                .ThenBy(x => x.ProductName)
                .Take(5)
                .ToList();

            var mostViewedProducts = aktifUrunler
                .OrderByDescending(x => x.GoruntulenmeSayisi)
                .ThenByDescending(x => x.SatisSayisi)
                .Take(5)
                .Select(x => new DashboardProductStatItem
                {
                    ProductId = x.Id,
                    ProductName = x.Baslik,
                    ProductImageUrl = x.AnaGorselUrl,
                    Amount = x.GoruntulenmeSayisi,
                    AmountLabel = $"{x.GoruntulenmeSayisi} görüntülenme"
                })
                .ToList();

            var allLowStockProducts = aktifUrunler
                .Select(x =>
                {
                    stokLookup.TryGetValue(x.Id, out var stock);
                    return new DashboardLowStockItem
                    {
                        ProductId = x.Id,
                        ProductName = x.Baslik,
                        ProductImageUrl = x.AnaGorselUrl,
                        Stock = stock?.Stok ?? 0,
                        PreorderOpen = stock?.OnSiparis ?? false
                    };
                })
                .Where(x => stokLookup.ContainsKey(x.ProductId) && x.Stock <= stokUyariLimiti)
                .OrderBy(x => x.Stock)
                .ThenBy(x => x.ProductName)
                .ToList();

            var lowStockProducts = allLowStockProducts.Take(6).ToList();

            var alerts = new List<DashboardAlertItem>();
            if (okunmamisMesajSayisi > 0)
            {
                alerts.Add(new DashboardAlertItem
                {
                    Severity = "warning",
                    Title = "Okunmamış iletişim talepleri",
                    Description = $"{okunmamisMesajSayisi} mesaj yanıtlanmayı bekliyor.",
                    Link = "/Admin/Iletisim"
                });
            }

            if (bekleyenSiparisSayisi > 0)
            {
                alerts.Add(new DashboardAlertItem
                {
                    Severity = "danger",
                    Title = "Bekleyen siparişler var",
                    Description = $"{bekleyenSiparisSayisi} sipariş işleme alınmayı bekliyor.",
                    Link = "/Admin/Siparis?durum=0"
                });
            }

            var eksikGorselliUrunSayisi = aktifUrunler.Count(x => string.IsNullOrWhiteSpace(x.AnaGorselUrl));
            if (eksikGorselliUrunSayisi > 0)
            {
                alerts.Add(new DashboardAlertItem
                {
                    Severity = "secondary",
                    Title = "Eksik ürün verisi",
                    Description = $"{eksikGorselliUrunSayisi} aktif üründe ana görsel eksik.",
                    Link = "/Admin/Urun"
                });
            }

            var recentActivities = new List<DashboardActivityItem>();
            recentActivities.AddRange(siparisler
                .OrderByDescending(x => x.OlusturulmaTarihi)
                .Take(5)
                .Select(x => new DashboardActivityItem
                {
                    Type = "Siparis",
                    Title = $"Yeni sipariş #{x.Id}",
                    Detail = $"{x.MusteriAdSoyad} - {x.ToplamTutar:N2} TL",
                    OccurredAt = x.OlusturulmaTarihi
                }));
            recentActivities.AddRange(aktifUrunler
                .OrderByDescending(x => x.OlusturulmaTarihi)
                .Take(5)
                .Select(x => new DashboardActivityItem
                {
                    Type = "Urun",
                    Title = "Yeni ürün eklendi",
                    Detail = x.Baslik,
                    OccurredAt = x.OlusturulmaTarihi
                }));
            recentActivities.AddRange(iletisimMesajlari
                .Where(x => !x.OkunduMu)
                .Take(3)
                .Select(x => new DashboardActivityItem
                {
                    Type = "Mesaj",
                    Title = "Yeni iletişim mesajı",
                    Detail = $"{x.AdSoyad} - {x.Konu}",
                    OccurredAt = x.Tarih
                }));

            var model = new AdminDashboardViewModel
            {
                TotalRevenue = odenmisSiparisler.Sum(x => x.ToplamTutar),
                DailyRevenue = odenmisSiparisler.Where(x => x.OlusturulmaTarihi.Date == today).Sum(x => x.ToplamTutar),
                WeeklyRevenue = odenmisSiparisler.Where(x => x.OlusturulmaTarihi.Date >= weekStart).Sum(x => x.ToplamTutar),
                MonthlyRevenue = odenmisSiparisler.Where(x => x.OlusturulmaTarihi.Date >= monthStart).Sum(x => x.ToplamTutar),
                TotalOrders = siparisler.Count,
                TodayOrders = siparisler.Count(x => x.OlusturulmaTarihi.Date == today),
                PendingOrders = bekleyenSiparisSayisi,
                ReadyToShipOrders = siparisler.Count(x => x.Durum == SiparisDurumHelper.UretimHazirlaniyor || x.Durum == SiparisDurumHelper.Paketleniyor),
                ShippedOrders = siparisler.Count(x => x.Durum == SiparisDurumHelper.KargoyaVerildi),
                CancelledOrReturnedOrders = siparisler.Count(x => SiparisDurumHelper.IsCancelled(x.Durum) || SiparisDurumHelper.IsReturn(x.Durum)),
                ActiveProductCount = aktifUrunler.Count,
                LowStockProductCount = allLowStockProducts.Count,
                UnreadContactCount = okunmamisMesajSayisi,
                TotalVisitsLast7Days = ziyaretler.Count,
                UniqueVisitorsLast7Days = ziyaretler
                    .Select(x => x.IpAdresi)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                RevenueChartLabels = chartLabels,
                RevenueChartValues = chartValues,
                TopSellingProducts = topSellingProducts,
                MostViewedProducts = mostViewedProducts,
                LowStockProducts = lowStockProducts,
                RecentProducts = aktifUrunler
                    .OrderByDescending(x => x.OlusturulmaTarihi)
                    .Take(5)
                    .Select(x => new DashboardRecentEntityItem
                    {
                        Id = x.Id,
                        Title = x.Baslik,
                        Subtitle = $"{(string.IsNullOrWhiteSpace(x.SKU) ? "SKU yok" : x.SKU)} | {x.EtkinFiyat:N2} TL",
                        CreatedAt = x.OlusturulmaTarihi
                    })
                    .ToList(),
                RecentReviews = yorumlar
                    .Select(x => new DashboardReviewItem
                    {
                        ReviewId = x.Id,
                        ProductName = x.Urun?.Baslik ?? "Ürün",
                        CustomerName = x.AdSoyad,
                        Rating = x.Puan,
                        Comment = x.YorumMetni,
                        Approved = x.OnayliMi,
                        CreatedAt = x.OlusturulmaTarihi
                    })
                    .ToList(),
                RecentActivities = recentActivities
                    .OrderByDescending(x => x.OccurredAt)
                    .Take(8)
                    .ToList(),
                Alerts = alerts
            };

            return View(model);
        }
    }
}
