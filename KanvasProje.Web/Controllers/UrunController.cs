using KanvasProje.Core.Varliklar;
using KanvasProje.Core.DTOs;
using KanvasProje.Core.Helpers;
using KanvasProje.Data;
using KanvasProje.Service.Helpers;
using KanvasProje.Web.Models;
using KanvasProje.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using KanvasProje.Service.Services;

namespace KanvasProje.Web.Controllers
{
    public class UrunController : Controller
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly KanvasDbContext _context;
        private readonly ISiteSettingsService _siteSettingsService;

        public UrunController(UserManager<AppUser> userManager, KanvasDbContext context, ISiteSettingsService siteSettingsService)
        {
            _userManager = userManager;
            _context = context;
            _siteSettingsService = siteSettingsService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            int? k,
            string? s,
            string? sort,
            decimal? min,
            decimal? max,
            string[]? ozellik,
            int page = 1,
            [FromQuery(Name = "sirala")] string? eskiSort = null,
            [FromQuery(Name = "sayfa")] int? eskiPage = null)
        {
            var settings = _siteSettingsService.GetSettings();

            if (string.IsNullOrWhiteSpace(sort) && !string.IsNullOrWhiteSpace(eskiSort))
            {
                sort = eskiSort;
            }

            if (page == 1 && eskiPage.HasValue)
            {
                page = eskiPage.Value;
            }

            var kategoriler = await _context.Kategoriler
                .AsNoTracking()
                .Where(x => x.AktifMi && !x.SilindiMi)
                .OrderBy(x => x.Sira)
                .ThenBy(x => x.Ad)
                .ToListAsync();

            var kategoriUrunSayilari = await BuildCategoryProductCountsAsync(kategoriler);
            ViewBag.Kategoriler = kategoriler;
            ViewBag.KategoriUrunSayilari = kategoriUrunSayilari;
            ViewBag.ToplamAktifUrunSayisi = kategoriUrunSayilari
                .Where(x => kategoriler.Any(kat => kat.Id == x.Key && !kat.ParentKategoriId.HasValue))
                .Sum(x => x.Value);

            var query = _context.Urunler
                .AsNoTracking()
                .Include(x => x.Kategori)
                .Include(x => x.UrunSecenek)
                .Where(x =>
                    x.AktifMi &&
                    !x.SilindiMi &&
                    x.Kategori != null &&
                    x.Kategori.AktifMi)
                .AsQueryable();

            if (k.HasValue)
            {
                var filtreKategorileri = await _context.Kategoriler
                    .AsNoTracking()
                    .Where(x => !x.SilindiMi)
                    .ToListAsync();

                var kategoriIdleri = CategoryTreeHelper.GetDescendantIds(filtreKategorileri, k.Value);
                if (kategoriIdleri.Count == 0)
                {
                    kategoriIdleri.Add(k.Value);
                }

                query = query.Where(x => kategoriIdleri.Contains(x.KategoriId));
                ViewBag.KategoriId = k.Value;
            }

            if (!string.IsNullOrWhiteSpace(s))
            {
                var aramaDeseni = $"%{s.Trim()}%";
                query = query.Where(x =>
                    EF.Functions.ILike(x.Baslik ?? string.Empty, aramaDeseni) ||
                    EF.Functions.ILike(x.KisaAd ?? string.Empty, aramaDeseni) ||
                    EF.Functions.ILike(x.SKU ?? string.Empty, aramaDeseni) ||
                    EF.Functions.ILike(x.Barkod ?? string.Empty, aramaDeseni) ||
                    EF.Functions.ILike(x.Marka ?? string.Empty, aramaDeseni) ||
                    EF.Functions.ILike(x.Etiketler ?? string.Empty, aramaDeseni));
                ViewBag.Search = s.Trim();
            }

            var genelCarpan = (100m - Math.Clamp(settings.GenelIndirimYuzdesi, 0m, 95m)) / 100m;

            if (min.HasValue)
            {
                var minVal = min.Value;
                query = query.Where(x =>
                    (x.IndirimliFiyat.HasValue && x.IndirimliFiyat > 0 && x.IndirimliFiyat < x.Fiyat
                        ? x.IndirimliFiyat.Value
                        : Math.Round(x.Fiyat * ((100m - (x.Kategori != null && x.Kategori.IndirimYuzdesi.HasValue ? x.Kategori.IndirimYuzdesi.Value : settings.GenelIndirimYuzdesi)) / 100m), 0)) >= minVal);
                ViewBag.Min = min.Value;
            }

            if (max.HasValue)
            {
                var maxVal = max.Value;
                query = query.Where(x =>
                    (x.IndirimliFiyat.HasValue && x.IndirimliFiyat > 0 && x.IndirimliFiyat < x.Fiyat
                        ? x.IndirimliFiyat.Value
                        : Math.Round(x.Fiyat * ((100m - (x.Kategori != null && x.Kategori.IndirimYuzdesi.HasValue ? x.Kategori.IndirimYuzdesi.Value : settings.GenelIndirimYuzdesi)) / 100m), 0)) <= maxVal);
                ViewBag.Max = max.Value;
            }

            var seciliOzellikler = ParseFeatureFilters(ozellik);
            ViewBag.SeciliOzellikler = seciliOzellikler;
            ViewBag.OzellikFiltreleri = await BuildFeatureFiltersAsync(query);

            foreach (var filter in seciliOzellikler)
            {
                var filterValue = filter.Value;
                query = query.Where(x => x.UrunOzellikleri.Any(v =>
                    !v.SilindiMi &&
                    v.UrunOzellikTanimiId == filter.Key &&
                    v.Deger == filterValue));
            }

            sort = sort switch
            {
                "price_asc" => "fiyat_artan",
                "price_desc" => "fiyat_azalan",
                _ => sort ?? "yeni"
            };

            switch (sort)
            {
                case "fiyat_artan":
                    query = query.OrderBy(x =>
                        x.IndirimliFiyat.HasValue && x.IndirimliFiyat > 0 && x.IndirimliFiyat < x.Fiyat
                            ? x.IndirimliFiyat.Value
                            : x.Fiyat * genelCarpan).ThenBy(x => x.Sira);
                    break;
                case "fiyat_azalan":
                    query = query.OrderByDescending(x =>
                        x.IndirimliFiyat.HasValue && x.IndirimliFiyat > 0 && x.IndirimliFiyat < x.Fiyat
                            ? x.IndirimliFiyat.Value
                            : x.Fiyat * genelCarpan).ThenBy(x => x.Sira);
                    break;
                case "cok_satan":
                case "popularity":
                    query = query.OrderByDescending(x => x.SatisSayisi)
                        .ThenByDescending(x => x.GoruntulenmeSayisi)
                        .ThenBy(x => x.Sira);
                    break;
                case "one_cikan":
                    query = query.OrderByDescending(x => x.OneCikanMi)
                        .ThenBy(x => x.Sira)
                        .ThenByDescending(x => x.OlusturulmaTarihi);
                    break;
                case "yeni":
                default:
                    query = query.OrderByDescending(x => x.YeniUrunMu)
                        .ThenByDescending(x => x.OlusturulmaTarihi)
                        .ThenBy(x => x.Sira);
                    break;
            }

            ViewBag.Sort = sort;

            const int pageSize = 28;
            var totalItems = await query.CountAsync();
            var totalPages = totalItems == 0 ? 1 : (int)Math.Ceiling(totalItems / (double)pageSize);

            if (page < 1)
            {
                page = 1;
            }

            if (page > totalPages)
            {
                page = totalPages;
            }

            var urunler = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalItems = totalItems;

            return View(urunler);
        }

        private async Task<Dictionary<int, int>> BuildCategoryProductCountsAsync(List<Kategori> kategoriler)
        {
            var kategoriIdleri = kategoriler.Select(x => x.Id).ToHashSet();
            var dogrudanUrunSayilari = await _context.Urunler
                .AsNoTracking()
                .Where(x =>
                    x.AktifMi &&
                    !x.SilindiMi &&
                    x.Kategori != null &&
                    x.Kategori.AktifMi &&
                    !x.Kategori.SilindiMi &&
                    kategoriIdleri.Contains(x.KategoriId))
                .GroupBy(x => x.KategoriId)
                .Select(x => new { KategoriId = x.Key, UrunSayisi = x.Count() })
                .ToDictionaryAsync(x => x.KategoriId, x => x.UrunSayisi);

            return kategoriler.ToDictionary(
                kategori => kategori.Id,
                kategori => CategoryTreeHelper.GetDescendantIds(kategoriler, kategori.Id)
                    .Sum(id => dogrudanUrunSayilari.GetValueOrDefault(id)));
        }

        [HttpGet]
        public async Task<IActionResult> CanliAra(string? q)
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return Json(null);
            }

            var temiz = q.Trim();
            if (temiz.Length < 2)
            {
                return Json(null);
            }

            _ = _siteSettingsService.GetSettings();
            var aramaDeseni = $"%{temiz}%";

            var adaylar = await _context.Urunler
                .AsNoTracking()
                .Include(x => x.Kategori)
                .Where(x =>
                    x.AktifMi &&
                    !x.SilindiMi &&
                    x.Kategori != null &&
                    x.Kategori.AktifMi &&
                    (EF.Functions.ILike(x.Baslik ?? string.Empty, aramaDeseni) ||
                     EF.Functions.ILike(x.KisaAd ?? string.Empty, aramaDeseni) ||
                     EF.Functions.ILike(x.SKU ?? string.Empty, aramaDeseni) ||
                     EF.Functions.ILike(x.Barkod ?? string.Empty, aramaDeseni) ||
                     EF.Functions.ILike(x.Marka ?? string.Empty, aramaDeseni) ||
                     EF.Functions.ILike(x.Etiketler ?? string.Empty, aramaDeseni)))
                .OrderByDescending(x => x.GoruntulenmeSayisi)
                .ThenByDescending(x => x.OlusturulmaTarihi)
                .Take(9)
                .ToListAsync();

            var sonuclar = adaylar.Select(x =>
            {
                var slugPart = string.IsNullOrWhiteSpace(x.Slug)
                    ? SlugHelper.GenerateSlug(x.Baslik)
                    : x.Slug;
                return new
                {
                    id = x.Id,
                    slug = $"{slugPart}-{x.Id}",
                    baslik = x.Baslik,
                    gorsel = x.AnaGorselUrl,
                    fiyat = x.EtkinFiyat.ToString("N0") + " TL"
                };
            }).ToList();

            return Json(sonuclar);
        }

        public async Task<IActionResult> Detay(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return NotFound();
            }

            _ = _siteSettingsService.GetSettings();

            var detaySorgusu = _context.Urunler
                .AsNoTracking()
                .Include(x => x.Kategori!)
                    .ThenInclude(x => x.ParentKategori)
                .Include(x => x.UrunResimleri)
                .Include(x => x.UrunSecenek)
                .Include(x => x.UrunOzellikleri)
                    .ThenInclude(x => x.UrunOzellikTanimi)
                .Where(x =>
                    x.AktifMi &&
                    !x.SilindiMi &&
                    x.Kategori != null &&
                    x.Kategori.AktifMi)
                .AsSplitQuery();

            Urun? urun = null;
            if (int.TryParse(id, out var urunId))
            {
                urun = await detaySorgusu.FirstOrDefaultAsync(x => x.Id == urunId);
            }
            else if (id.Contains('-'))
            {
                var lastDash = id.LastIndexOf('-');
                if (lastDash > 0 && int.TryParse(id[(lastDash + 1)..], out var cikarilanId))
                {
                    urun = await detaySorgusu.FirstOrDefaultAsync(x => x.Id == cikarilanId);
                }
            }

            if (urun == null)
            {
                urun = await detaySorgusu.FirstOrDefaultAsync(x => x.Slug == id);
            }

            if (urun == null)
            {
                return NotFound();
            }

            var beklenenSlug = string.IsNullOrWhiteSpace(urun.Slug)
                ? SlugHelper.GenerateSlug(urun.Baslik)
                : urun.Slug;
            var canonicalParam = $"{beklenenSlug}-{urun.Id}";

            if (!string.Equals(id, canonicalParam, StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToActionPermanent(nameof(Detay), new { id = canonicalParam });
            }

            await _context.Urunler
                .Where(x => x.Id == urun.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.GoruntulenmeSayisi, u => u.GoruntulenmeSayisi + 1));

            var tumMedya = urun.UrunResimleri
                .Where(x => !x.SilindiMi)
                .OrderByDescending(x => x.VarsayilanMi)
                .ThenBy(x => x.Sira)
                .ThenBy(x => x.Id)
                .ToList();

            if (!tumMedya.Any() && !string.IsNullOrWhiteSpace(urun.AnaGorselUrl))
            {
                tumMedya.Add(new UrunResim
                {
                    UrunId = urun.Id,
                    ResimYolu = urun.AnaGorselUrl,
                    ThumbnailYolu = urun.AnaGorselUrl,
                    Baslik = urun.Baslik,
                    AltMetin = urun.Baslik,
                    MedyaTipi = UrunMedyaCatalog.Gorsel,
                    MedyaAlani = UrunMedyaCatalog.Galeri,
                    VarsayilanMi = true
                });
            }

            ViewBag.TumMedya = tumMedya;
            ViewBag.Galeri = tumMedya
                .Where(x => !x.VideoMu && (x.MedyaAlani == UrunMedyaCatalog.Galeri || x.VarsayilanMi || x.MedyaAlani == UrunMedyaCatalog.YakinDetay))
                .ToList();
            ViewBag.VideoMedyalari = tumMedya.Where(x => x.VideoMu).ToList();
            ViewBag.YakinDetayMedyalari = tumMedya.Where(x => x.MedyaAlani == UrunMedyaCatalog.YakinDetay).ToList();
            ViewBag.UretimMedyalari = tumMedya.Where(x => x.MedyaAlani == UrunMedyaCatalog.Uretim).ToList();
            ViewBag.KullanimMedyalari = tumMedya
                .Where(x => x.MedyaAlani == UrunMedyaCatalog.MusteriKullanim || x.MedyaAlani == UrunMedyaCatalog.SizdenGelenler)
                .ToList();

            ViewBag.Secenekler = urun.UrunSecenek
                .Where(x =>
                    !x.SilindiMi &&
                    x.AktifMi &&
                    (!x.TukeninceGizle || x.StokAdedi > 0 || x.OnSipariseAcikMi))
                .OrderBy(x => x.Sira)
                .ThenBy(x => x.SatisFiyati)
                .ToList();

            var yorumlar = await _context.Yorumlar
                .AsNoTracking()
                .Where(x => x.UrunId == urun.Id && x.OnayliMi && !x.SilindiMi)
                .OrderByDescending(x => x.OlusturulmaTarihi)
                .ToListAsync();

            var ortalamaPuan = yorumlar.Count == 0 ? 0.0 : yorumlar.Average(x => x.Puan);

            ViewBag.Yorumlar = yorumlar;
            ViewBag.OrtalamaPuan = ortalamaPuan;
            ViewBag.YorumSayisi = yorumlar.Count;

            // Benzer ürünler - önce aynı kategori, yetersizse üst/kardeş kategoriden tamamla
            var benzerUrunler = await _context.Urunler
                .AsNoTracking()
                .Where(x =>
                    x.AktifMi &&
                    !x.SilindiMi &&
                    x.KategoriId == urun.KategoriId &&
                    x.Id != urun.Id)
                .OrderByDescending(x => x.SatisSayisi)
                .ThenByDescending(x => x.GoruntulenmeSayisi)
                .Take(6)
                .Select(x => new BenzerUrunDto
                {
                    Id = x.Id,
                    Baslik = x.Baslik,
                    Slug = x.Slug,
                    AnaGorselUrl = x.AnaGorselUrl,
                    Fiyat = x.Fiyat,
                    IndirimliFiyat = x.IndirimliFiyat,
                    KategoriId = x.KategoriId
                })
                .ToListAsync();

            if (benzerUrunler.Count < 6 && urun.Kategori?.ParentKategoriId != null)
            {
                var mevcutIdler = benzerUrunler.Select(b => b.Id).Append(urun.Id).ToHashSet();
                var ustKatId = urun.Kategori.ParentKategoriId.Value;
                var ekBenzerler = await _context.Urunler
                    .AsNoTracking()
                    .Where(x =>
                        x.AktifMi &&
                        !x.SilindiMi &&
                        x.Kategori != null &&
                        x.Kategori.AktifMi &&
                        (x.KategoriId == ustKatId || x.Kategori.ParentKategoriId == ustKatId) &&
                        !mevcutIdler.Contains(x.Id))
                    .OrderByDescending(x => x.SatisSayisi)
                    .ThenByDescending(x => x.GoruntulenmeSayisi)
                    .Take(6 - benzerUrunler.Count)
                    .Select(x => new BenzerUrunDto
                    {
                        Id = x.Id,
                        Baslik = x.Baslik,
                        Slug = x.Slug,
                        AnaGorselUrl = x.AnaGorselUrl,
                        Fiyat = x.Fiyat,
                        IndirimliFiyat = x.IndirimliFiyat,
                        KategoriId = x.KategoriId
                    })
                    .ToListAsync();

                benzerUrunler.AddRange(ekBenzerler);
            }

            ViewBag.BenzerUrunler = benzerUrunler;

            var siteSettings = _siteSettingsService.GetSettings();
            ViewBag.HaliMetrekareFiyati = siteSettings.HaliMetrekareFiyati;
            ViewBag.DuvarKagidiMetrekareFiyati = siteSettings.DuvarKagidiMetrekareFiyati;

            return View(urun);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> YorumYap([Bind("UrunId,Puan,YorumMetni,AdSoyad")] Yorum yorum)
        {
            var urun = await _context.Urunler
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == yorum.UrunId && x.AktifMi && !x.SilindiMi);
            if (urun == null)
            {
                return NotFound();
            }

            var canonicalId = $"{(string.IsNullOrWhiteSpace(urun.Slug) ? SlugHelper.GenerateSlug(urun.Baslik) : urun.Slug)}-{urun.Id}";

            yorum.YorumMetni = (yorum.YorumMetni ?? string.Empty).Trim();
            yorum.AdSoyad = (yorum.AdSoyad ?? string.Empty).Trim();
            yorum.Puan = Math.Clamp(yorum.Puan, 1, 5);

            if (string.IsNullOrWhiteSpace(yorum.YorumMetni) || yorum.YorumMetni.Length < 2)
            {
                TempData["Hata"] = "Lütfen geçerli bir yorum metni girin.";
                return RedirectToAction(nameof(Detay), new { id = canonicalId });
            }

            if (yorum.YorumMetni.Length > 2000)
            {
                yorum.YorumMetni = yorum.YorumMetni[..2000];
            }

            if (User.Identity?.IsAuthenticated == true)
            {
                var user = await _userManager.GetUserAsync(User);
                if (user != null)
                {
                    yorum.AppUserId = user.Id;
                    if (string.IsNullOrWhiteSpace(yorum.AdSoyad))
                    {
                        yorum.AdSoyad = string.IsNullOrWhiteSpace(user.AdSoyad) ? "Değerli Müşterimiz" : user.AdSoyad.Trim();
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(yorum.AdSoyad))
            {
                yorum.AdSoyad = "Misafir Kullanıcı";
            }

            if (yorum.AdSoyad.Length > 50)
            {
                yorum.AdSoyad = yorum.AdSoyad[..50];
            }

            yorum.OlusturulmaTarihi = DateTime.UtcNow;
            yorum.OnayliMi = false;
            yorum.SilindiMi = false;

            _context.Yorumlar.Add(yorum);
            await _context.SaveChangesAsync();

            TempData["Basari"] = "Yorumunuz alındı, onaylandıktan sonra yayınlanacaktır.";
            return RedirectToAction(nameof(Detay), new { id = canonicalId });
        }

        private static Dictionary<int, string> ParseFeatureFilters(IEnumerable<string>? filters)
        {
            var result = new Dictionary<int, string>();

            if (filters == null)
            {
                return result;
            }

            foreach (var item in filters)
            {
                if (string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                var separatorIndex = item.IndexOf(':');
                if (separatorIndex <= 0 || separatorIndex >= item.Length - 1)
                {
                    continue;
                }

                var definitionPart = item[..separatorIndex].Trim();
                var valuePart = item[(separatorIndex + 1)..].Trim();
                if (!int.TryParse(definitionPart, out var definitionId) || string.IsNullOrWhiteSpace(valuePart))
                {
                    continue;
                }

                result[definitionId] = valuePart;
            }

            return result;
        }

        private async Task<List<ProductFeatureFilterViewModel>> BuildFeatureFiltersAsync(IQueryable<Urun> query)
        {
            var productIds = query.Select(x => x.Id);

            var rawFilters = await _context.UrunOzellikDegerleri
                .AsNoTracking()
                .Where(x =>
                    !x.SilindiMi &&
                    productIds.Contains(x.UrunId) &&
                    x.UrunOzellikTanimi.AktifMi &&
                    x.UrunOzellikTanimi.FiltredeGoster &&
                    !string.IsNullOrWhiteSpace(x.Deger))
                .GroupBy(x => new
                {
                    x.UrunOzellikTanimiId,
                    x.UrunOzellikTanimi.Ad,
                    x.UrunOzellikTanimi.UrunTipi,
                    x.Deger
                })
                .Select(x => new
                {
                    x.Key.UrunOzellikTanimiId,
                    x.Key.Ad,
                    x.Key.UrunTipi,
                    Deger = x.Key.Deger,
                    Count = x.Select(v => v.UrunId).Distinct().Count()
                })
                .ToListAsync();

            return rawFilters
                .GroupBy(x => new { x.UrunOzellikTanimiId, x.Ad, x.UrunTipi })
                .OrderBy(x => x.Key.UrunTipi)
                .ThenBy(x => x.Key.Ad)
                .Select(x => new ProductFeatureFilterViewModel
                {
                    DefinitionId = x.Key.UrunOzellikTanimiId,
                    Title = x.Key.Ad,
                    ProductType = x.Key.UrunTipi,
                    Options = x
                        .OrderByDescending(v => v.Count)
                        .ThenBy(v => v.Deger)
                        .Select(v => new ProductFeatureFilterOptionViewModel
                        {
                            Value = v.Deger,
                            DisplayValue = NormalizeFeatureValueLabel(v.Deger),
                            Count = v.Count
                        })
                        .ToList()
                })
                .ToList();
        }

        private static string NormalizeFeatureValueLabel(string? value)
        {
            return value switch
            {
                "true" => "Evet",
                "false" => "Hayir",
                _ => value?.Trim() ?? string.Empty
            };
        }
    }
}
