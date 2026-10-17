using System.Text.Json;
using KanvasProje.Core.Varliklar;
using KanvasProje.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanvasProje.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class UrunImportController : AdminBaseController
    {
        private readonly KanvasDbContext _context;
        private readonly IWebHostEnvironment _env;

        public UrunImportController(KanvasDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        public async Task<IActionResult> Index()
        {
            var kategoriler = await _context.Kategoriler
                .Where(x => !x.SilindiMi && x.AktifMi)
                .OrderBy(x => x.Sira)
                .Select(x => new { x.Id, x.Ad, x.ParentKategoriId })
                .ToListAsync();
            
            ViewBag.Kategoriler = kategoriler;
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetCategories()
        {
            var kategoriler = await _context.Kategoriler
                .Where(x => !x.SilindiMi)
                .OrderBy(x => x.Sira)
                .Select(x => new { x.Id, x.Ad, x.ParentKategoriId })
                .ToListAsync();
            return Json(kategoriler);
        }

        [HttpGet]
        public async Task<IActionResult> UrunleriGetir()
        {
            var jsonCategoryIds = new List<int> { 8, 10, 11, 9, 26, 15, 4, 17, 21, 19, 23, 20, 13, 12 };
            var urunler = await _context.Urunler
                .Where(x => jsonCategoryIds.Contains(x.KategoriId))
                .GroupBy(x => x.KategoriId)
                .Select(g => new { KategoriId = g.Key, UrunSayisi = g.Count() })
                .ToListAsync();
            return Json(new { toplam = urunler.Sum(x => x.UrunSayisi), dagilim = urunler });
        }

        [HttpGet]
        public async Task<IActionResult> Istatistik()
        {
            var toplamUrun = await _context.Urunler.CountAsync();
            var toplamVaryasyon = await _context.UrunSecenekleri.CountAsync();
            var aktifUrun = await _context.Urunler.CountAsync(x => x.AktifMi && !x.SilindiMi);
            var jsonCategoryIds = new List<int> { 8, 10, 11, 9, 26, 15, 4, 17, 21, 19, 23, 20, 13, 12 };
            var jsonUrun = await _context.Urunler.CountAsync(x => jsonCategoryIds.Contains(x.KategoriId));
            var jsonVaryasyon = await _context.UrunSecenekleri
                .Where(x => jsonCategoryIds.Contains(x.Urun.KategoriId))
                .CountAsync();
            
            return Json(new { 
                toplamUrun, 
                toplamVaryasyon, 
                aktifUrun,
                jsonUrun,
                jsonVaryasyon
            });
        }

        [HttpGet]
        public async Task<IActionResult> Kontrol()
        {
            var jsonCategoryIds = new List<int> { 8, 10, 11, 9, 26, 15, 4, 17, 21, 19, 23, 20, 13, 12 };
            
            var cerceveTipleri = await _context.UrunSecenekleri
                .Where(x => jsonCategoryIds.Contains(x.Urun.KategoriId))
                .Select(x => x.CerceveTipi)
                .Distinct()
                .ToListAsync();
            
            var olculer = await _context.UrunSecenekleri
                .Where(x => jsonCategoryIds.Contains(x.Urun.KategoriId))
                .Select(x => x.Olcu)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();
            
            return Json(new { cerceveTipleri, olculer });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Duzelt()
        {
            var jsonCategoryIds = new List<int> { 8, 10, 11, 9, 26, 15, 4, 17, 21, 19, 23, 20, 13, 12 };
            
            var secenekler = await _context.UrunSecenekleri
                .Where(x => jsonCategoryIds.Contains(x.Urun.KategoriId))
                .ToListAsync();
            
            int duzeltilen = 0;
            foreach (var sec in secenekler)
            {
                var degisti = false;
                if (sec.CerceveTipi == "Ahsap Cerceve")
                {
                    sec.CerceveTipi = "Ahşap Çerçeve";
                    degisti = true;
                }
                if (sec.CerceveRengi == "Natural")
                {
                    sec.CerceveRengi = "Naturel";
                    degisti = true;
                }
                if (degisti) duzeltilen++;
            }
            
            await _context.SaveChangesAsync();
            return Json(new { success = true, message = duzeltilen + " kayıt düzeltildi" });
        }

        [HttpGet]
        public async Task<IActionResult> DebugCategories()
        {
            var kategoriler = await _context.Kategoriler
                .OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Ad, x.Sira })
                .ToListAsync();
            return Content("Kategoriler:\n" + string.Join("\n", kategoriler.Select(k => $"{k.Id}: {k.Ad} (Sira: {k.Sira})")));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAllProducts([FromBody] DeleteProductsRequest? request)
        {
            try
            {
                if (request?.kategoriIds != null && request.kategoriIds.Any())
                {
                    var urunler = await _context.Urunler
                        .Where(x => request.kategoriIds.Contains(x.KategoriId))
                        .ToListAsync();

                    if (!urunler.Any())
                    {
                        return Json(new { success = true, message = "Silinecek ürün bulunamadı" });
                    }

                    var urunIdleri = urunler.Select(x => x.Id).ToList();

                    var secenekler = await _context.UrunSecenekleri
                        .Where(x => urunIdleri.Contains(x.UrunId))
                        .ToListAsync();
                    var sepetItems = await _context.SepetItems
                        .Where(x => urunIdleri.Contains(x.UrunId))
                        .ToListAsync();
                    foreach (var item in sepetItems)
                    {
                        item.SilindiMi = true;
                    }

                    var favoriler = await _context.Favoriler
                        .Where(x => urunIdleri.Contains(x.UrunId))
                        .ToListAsync();
                    foreach (var favori in favoriler)
                    {
                        favori.SilindiMi = true;
                    }

                    foreach (var secenek in secenekler)
                    {
                        secenek.SilindiMi = true;
                        secenek.AktifMi = false;
                    }

                    var resimler = await _context.UrunResimleri
                        .Where(x => urunIdleri.Contains(x.UrunId))
                        .ToListAsync();
                    foreach (var resim in resimler)
                    {
                        resim.SilindiMi = true;
                    }

                    var ozellikDegerleri = await _context.UrunOzellikDegerleri
                        .Where(x => urunIdleri.Contains(x.UrunId))
                        .ToListAsync();
                    foreach (var ozellikDegeri in ozellikDegerleri)
                    {
                        ozellikDegeri.SilindiMi = true;
                    }

                    var yorumlar = await _context.Yorumlar
                        .Where(x => urunIdleri.Contains(x.UrunId))
                        .ToListAsync();
                    foreach (var yorum in yorumlar)
                    {
                        yorum.SilindiMi = true;
                        yorum.OnayliMi = false;
                    }

                    foreach (var urun in urunler)
                    {
                        urun.SilindiMi = true;
                        urun.AktifMi = false;
                    }
                    await _context.SaveChangesAsync();

                    return Json(new { success = true, message = urunler.Count + " ürün, " + secenekler.Count + " varyasyon, " + resimler.Count + " resim, " + sepetItems.Count + " sepet, " + yorumlar.Count + " yorum, " + favoriler.Count + " favori silindi" });
                }
                return Json(new { success = false, message = "Kategori ID'leri gerekli" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Hata: " + (ex.InnerException?.Message ?? ex.Message) });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportJson([FromBody] ImportJsonRequest? request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.categoryName))
                {
                    return Json(new { success = false, message = "Kategori adı gerekli." });
                }
                var jsonPath = GetImportJsonPath(request.categoryName);
                
                if (!System.IO.File.Exists(jsonPath))
                {
                    return Json(new { success = false, message = "Dosya bulunamadı: " + jsonPath });
                }

                var jsonContent = await System.IO.File.ReadAllTextAsync(jsonPath);
                var urunler = JsonSerializer.Deserialize<List<JsonUrun>>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (urunler == null || !urunler.Any())
                {
                    return Json(new { success = false, message = "Ürün bulunamadı" });
                }

                var kategori = await _context.Kategoriler.FirstOrDefaultAsync(x => x.Ad == request.categoryName);
                if (kategori == null)
                {
                    var slug = request.categoryName.ToLower()
                        .Replace("ı", "i").Replace("ş", "s").Replace("ğ", "g")
                        .Replace("ç", "c").Replace("ü", "u").Replace("ö", "o")
                        .Replace(" ", "-");
                    
                    var maxSira = await _context.Kategoriler.MaxAsync(x => (int?)x.Sira) ?? 0;
                    kategori = new Kategori
                    {
                        Ad = request.categoryName,
                        Slug = slug,
                        KisaAciklama = "",
                        Aciklama = "",
                        SeoTitle = request.categoryName + " - MeteorGaleri",
                        SeoDescription = request.categoryName + " kategorisindeki kanvas tablolar",
                        Sira = maxSira + 1,
                        AktifMi = true,
                        SilindiMi = false,
                        OlusturulmaTarihi = DateTime.UtcNow
                    };
                    _context.Kategoriler.Add(kategori);
                    await _context.SaveChangesAsync();
                }

                int eklenen = 0;
                int varyasyonEklendi = 0;
                int mevcutAtlandi = 0;
                var kategoriId = kategori.Id;

                foreach (var urun in urunler)
                {
                    var existingUrun = await _context.Urunler
                        .FirstOrDefaultAsync(x => x.Baslik == urun.baslik && x.KategoriId == kategoriId);

                    if (existingUrun != null)
                    {
                        mevcutAtlandi++;
                        continue;
                    }

                    var yeniUrun = new Urun
                    {
                        Baslik = urun.baslik,
                        KisaAd = urun.baslik.Length > 50 ? urun.baslik.Substring(0, 50) : urun.baslik,
                        KisaAciklama = urun.kisaAciklama ?? "",
                        Aciklama = urun.aciklama ?? "",
                        Fiyat = (decimal)(urun.fiyat > 0 ? urun.fiyat : 849),
                        IndirimliFiyat = null,
                        Maliyet = (decimal)(urun.fiyat * 0.6),
                        KdvOrani = 20,
                        UrunTipi = "Kanvas Tablo",
                        KategoriId = kategoriId,
                        AnaGorselUrl = urun.anaGorselUrl ?? "",
                        AktifMi = true,
                        OneCikanMi = false,
                        YeniUrunMu = true,
                        StokDurumu = "Stokta",
                        MinSiparisAdedi = 1,
                        UretimSuresiGun = 3,
                        KargoyaVerilisSuresiGun = 3,
                        TahminiTeslimSuresiGun = 6,
                        SeoTitle = urun.baslik,
                        SeoDescription = urun.kisaAciklama?.Substring(0, Math.Min(150, urun.kisaAciklama?.Length ?? 0)) ?? string.Empty,
                        SeoKeywords = ""
                    };

                    _context.Urunler.Add(yeniUrun);
                    await _context.SaveChangesAsync();

                    if (urun.varyasyonlar != null && urun.varyasyonlar.Any())
                    {
                        int varyasyonSira = 1;
                        foreach (var varY in urun.varyasyonlar)
                        {
                            var olcu = NormalizeOlcu(varY.olcu);
                            olcu = olcu.Replace("cmcm", "cm");

                            string gorselUrl = varY.gorselUrl ?? urun.anaGorselUrl ?? "";

                            var varyasyon = new UrunSecenek
                            {
                                UrunId = yeniUrun.Id,
                                Olcu = olcu,
                                CerceveTipi = NormalizeCerceveTipi(varY.cerceveTipi),
                                CerceveRengi = NormalizeCerceveRengi(varY.cerceveRengi),
                                CerceveKalinligi = string.Empty,
                                MalzemeTuru = string.Empty,
                                SatisFiyati = (decimal)(varY.satisFiyati > 0 ? varY.satisFiyati : urun.fiyat),
                                MaliyetFiyati = (decimal)((varY.satisFiyati > 0 ? varY.satisFiyati : urun.fiyat) * 0.6),
                                StokAdedi = 100,
                                AktifMi = true,
                                GorselUrl = gorselUrl,
                                VarsayilanMi = varyasyonSira == 1,
                                ParcaSayisi = 1,
                                Sira = varyasyonSira
                            };

                            _context.UrunSecenekleri.Add(varyasyon);
                            varyasyonEklendi++;
                            varyasyonSira++;
                        }
                    }
                    else
                    {
                        _context.UrunSecenekleri.Add(BuildDefaultImportedVariant(yeniUrun.Id, urun.fiyat, urun.anaGorselUrl));
                        varyasyonEklendi++;
                    }

                    await _context.SaveChangesAsync();
                    eklenen++;
                }

                return Json(new { 
                    success = true, 
                    message = $"✅ {eklenen} ürün eklendi, {varyasyonEklendi} varyasyon eklendi, {mevcutAtlandi} ürün atlandı" 
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Hata: " + ex.Message + "\n" + ex.StackTrace });
            }
        }

        public class ImportJsonRequest
        {
            public int kategoriId { get; set; }
            public string categoryName { get; set; } = "";
        }

        private string GetImportJsonPath(string categoryName)
        {
            var safeFileName = Path.GetFileName(categoryName) + ".json";
            var repoRoot = Directory.GetParent(_env.ContentRootPath)?.FullName ?? _env.ContentRootPath;
            return Path.Combine(repoRoot, "veriler", safeFileName);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportAtaturk()
        {
            try
            {
                var jsonPath = GetImportJsonPath("Atatürk ve Türkiye");
                
                if (!System.IO.File.Exists(jsonPath))
                {
                    return Json(new { success = false, message = "Dosya bulunamadı: " + jsonPath });
                }

                var jsonContent = await System.IO.File.ReadAllTextAsync(jsonPath);
                var urunler = JsonSerializer.Deserialize<List<AtaturkUrun>>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (urunler == null || !urunler.Any())
                {
                    return Json(new { success = false, message = "Ürün bulunamadı" });
                }

                int eklenen = 0;
                int varyasyonEklendi = 0;
                int mevcutAtlandi = 0;
                var kategoriId = 10;

                foreach (var urun in urunler)
                {
                    var existingUrun = await _context.Urunler
                        .FirstOrDefaultAsync(x => x.Baslik == urun.baslik && x.KategoriId == kategoriId);

                    if (existingUrun != null)
                    {
                        mevcutAtlandi++;
                        continue;
                    }

                    var yeniUrun = new Urun
                    {
                        Baslik = urun.baslik,
                        KisaAd = urun.baslik.Length > 50 ? urun.baslik.Substring(0, 50) : urun.baslik,
                        KisaAciklama = urun.kisaAciklama ?? "",
                        Aciklama = urun.aciklama ?? "",
                        Fiyat = (decimal)(urun.fiyat > 0 ? urun.fiyat : 849),
                        IndirimliFiyat = null,
                        Maliyet = (decimal)(urun.fiyat * 0.6),
                        KdvOrani = 20,
                        UrunTipi = "Kanvas Tablo",
                        KategoriId = kategoriId,
                        AnaGorselUrl = urun.anaGorselUrl ?? "",
                        AktifMi = true,
                        OneCikanMi = false,
                        YeniUrunMu = true,
                        StokDurumu = "Stokta",
                        MinSiparisAdedi = 1,
                        UretimSuresiGun = 3,
                        KargoyaVerilisSuresiGun = 3,
                        TahminiTeslimSuresiGun = 6,
                        SeoTitle = urun.baslik,
                        SeoDescription = urun.kisaAciklama?.Substring(0, Math.Min(150, urun.kisaAciklama.Length)) ?? string.Empty,
                        SeoKeywords = ""
                    };

                    _context.Urunler.Add(yeniUrun);
                    await _context.SaveChangesAsync();

                    if (urun.varyasyonlar != null && urun.varyasyonlar.Any())
                    {
                        int varyasyonSira = 1;
                        foreach (var varY in urun.varyasyonlar)
                        {
                            var olcu = NormalizeOlcu(varY.olcu);

                            var varyasyon = new UrunSecenek
                            {
                                UrunId = yeniUrun.Id,
                                Olcu = olcu,
                                CerceveTipi = NormalizeCerceveTipi(varY.cerceveTipi),
                                CerceveRengi = NormalizeCerceveRengi(varY.cerceveRengi),
                                CerceveKalinligi = string.Empty,
                                MalzemeTuru = string.Empty,
                                SatisFiyati = (decimal)(varY.satisFiyati > 0 ? varY.satisFiyati : urun.fiyat),
                                MaliyetFiyati = (decimal)((varY.satisFiyati > 0 ? varY.satisFiyati : urun.fiyat) * 0.6),
                                StokAdedi = 100,
                                AktifMi = true,
                                GorselUrl = varY.gorselUrl ?? urun.anaGorselUrl ?? "",
                                VarsayilanMi = varyasyonSira == 1,
                                ParcaSayisi = 1,
                                Sira = varyasyonSira
                            };

                            _context.UrunSecenekleri.Add(varyasyon);
                            varyasyonEklendi++;
                            varyasyonSira++;
                        }
                    }
                    else
                    {
                        _context.UrunSecenekleri.Add(BuildDefaultImportedVariant(yeniUrun.Id, urun.fiyat, urun.anaGorselUrl));
                        varyasyonEklendi++;
                    }

                    await _context.SaveChangesAsync();
                    eklenen++;
                }

                return Json(new { 
                    success = true, 
                    message = $"✅ {eklenen} ürün eklendi, {varyasyonEklendi} varyasyon eklendi, {mevcutAtlandi} ürün atlandı" 
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Hata: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportDini()
        {
            try
            {
                var jsonPath = GetImportJsonPath("Dini ve Hat Sanatı");
                
                if (!System.IO.File.Exists(jsonPath))
                {
                    return Json(new { success = false, message = "Dosya bulunamadı: " + jsonPath });
                }

                var jsonContent = await System.IO.File.ReadAllTextAsync(jsonPath);
                var urunler = JsonSerializer.Deserialize<List<DiniUrun>>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (urunler == null || !urunler.Any())
                {
                    return Json(new { success = false, message = "Ürün bulunamadı" });
                }

                int eklenen = 0;
                int varyasyonEklendi = 0;
                int mevcutAtlandi = 0;
                var kategoriId = 8;

                foreach (var urun in urunler)
                {
                    var existingUrun = await _context.Urunler
                        .FirstOrDefaultAsync(x => x.Baslik == urun.baslik && x.KategoriId == kategoriId);

                    if (existingUrun != null)
                    {
                        mevcutAtlandi++;
                        continue;
                    }

                    var yeniUrun = new Urun
                    {
                        Baslik = urun.baslik,
                        KisaAd = urun.baslik.Length > 50 ? urun.baslik.Substring(0, 50) : urun.baslik,
                        KisaAciklama = urun.kisaAciklama ?? "",
                        Aciklama = urun.aciklama ?? "",
                        Fiyat = (decimal)(urun.fiyat > 0 ? urun.fiyat : 849),
                        IndirimliFiyat = null,
                        Maliyet = (decimal)(urun.fiyat * 0.6),
                        KdvOrani = 20,
                        UrunTipi = "Kanvas Tablo",
                        KategoriId = kategoriId,
                        AnaGorselUrl = urun.anaGorselUrl ?? "",
                        AktifMi = true,
                        OneCikanMi = false,
                        YeniUrunMu = true,
                        StokDurumu = "Stokta",
                        MinSiparisAdedi = 1,
                        UretimSuresiGun = 3,
                        KargoyaVerilisSuresiGun = 3,
                        TahminiTeslimSuresiGun = 6,
                        SeoTitle = urun.baslik,
                        SeoDescription = urun.kisaAciklama?.Substring(0, Math.Min(150, urun.kisaAciklama?.Length ?? 0)) ?? string.Empty,
                        SeoKeywords = ""
                    };

                    _context.Urunler.Add(yeniUrun);
                    await _context.SaveChangesAsync();

                    if (urun.varyasyonlar != null && urun.varyasyonlar.Any())
                    {
                        int varyasyonSira = 1;
                        foreach (var varY in urun.varyasyonlar)
                        {
                            var olcu = NormalizeOlcu(varY.olcu);
                            olcu = olcu.Replace("cmcm", "cm");

                            var varyasyon = new UrunSecenek
                            {
                                UrunId = yeniUrun.Id,
                                Olcu = olcu,
                                CerceveTipi = NormalizeCerceveTipi(varY.cerceveTipi),
                                CerceveRengi = NormalizeCerceveRengi(varY.cerceveRengi),
                                CerceveKalinligi = string.Empty,
                                MalzemeTuru = string.Empty,
                                SatisFiyati = (decimal)(varY.satisFiyati > 0 ? varY.satisFiyati : urun.fiyat),
                                MaliyetFiyati = (decimal)((varY.satisFiyati > 0 ? varY.satisFiyati : urun.fiyat) * 0.6),
                                StokAdedi = 100,
                                AktifMi = true,
                                GorselUrl = varY.gorselUrl ?? urun.anaGorselUrl ?? "",
                                VarsayilanMi = varyasyonSira == 1,
                                ParcaSayisi = 1,
                                Sira = varyasyonSira
                            };

                            _context.UrunSecenekleri.Add(varyasyon);
                            varyasyonEklendi++;
                            varyasyonSira++;
                        }
                    }
                    else
                    {
                        _context.UrunSecenekleri.Add(BuildDefaultImportedVariant(yeniUrun.Id, urun.fiyat, urun.anaGorselUrl));
                        varyasyonEklendi++;
                    }

                    await _context.SaveChangesAsync();
                    eklenen++;
                }

                return Json(new { 
                    success = true, 
                    message = $"✅ {eklenen} ürün eklendi, {varyasyonEklendi} varyasyon eklendi, {mevcutAtlandi} ürün atlandı" 
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Hata: " + ex.Message });
            }
        }

        private static UrunSecenek BuildDefaultImportedVariant(int urunId, double fiyat, string? gorselUrl)
        {
            var salePrice = (decimal)(fiyat > 0 ? fiyat : 849);
            return new UrunSecenek
            {
                UrunId = urunId,
                Olcu = "Standart",
                CerceveTipi = "Çerçevesiz",
                CerceveRengi = string.Empty,
                CerceveKalinligi = string.Empty,
                MalzemeTuru = string.Empty,
                SatisFiyati = salePrice,
                MaliyetFiyati = salePrice * 0.6m,
                StokAdedi = 100,
                AktifMi = true,
                GorselUrl = gorselUrl ?? string.Empty,
                VarsayilanMi = true,
                ParcaSayisi = 1,
                Sira = 1
            };
        }

        private string NormalizeOlcu(string olcu)
        {
            if (string.IsNullOrWhiteSpace(olcu)) return olcu;
            
            var idx = olcu.IndexOf('(');
            if (idx > 0)
                olcu = olcu.Substring(0, idx).Trim();

            olcu = System.Text.RegularExpressions.Regex.Replace(olcu, @"\s+", " ");
            olcu = olcu.Replace("cm ", "cm").Replace(" cm", "cm");
            olcu = System.Text.RegularExpressions.Regex.Replace(olcu, @"(\d+)cm\s*x\s*(\d+)", "$1cm x $2cm");
            
            return olcu.Trim();
        }

        private string NormalizeCerceveTipi(string? cerceve)
        {
            if (string.IsNullOrWhiteSpace(cerceve)) return "Cercevesiz";

            cerceve = cerceve.Trim().ToLowerInvariant();

            if (cerceve.Equals("cercevesiz", StringComparison.OrdinalIgnoreCase) ||
                cerceve.Equals("cerçevesiz", StringComparison.OrdinalIgnoreCase))
            {
                return "Çerçevesiz";
            }

            if (cerceve.Equals("ahsap cerceve", StringComparison.OrdinalIgnoreCase) ||
                cerceve.Equals("ahşap çerçeve", StringComparison.OrdinalIgnoreCase))
            {
                return "Ahşap Çerçeve";
            }

            if (cerceve.Equals("metal cerceve", StringComparison.OrdinalIgnoreCase))
            {
                return "Metal Çerçeve";
            }

            return cerceve;
        }

        private string NormalizeCerceveRengi(string? renk)
        {
            if (string.IsNullOrWhiteSpace(renk)) return "Naturel";
            
            renk = renk.Trim();
            
            if (renk.Equals("Naturel", StringComparison.OrdinalIgnoreCase) ||
                renk.Equals("Natural", StringComparison.OrdinalIgnoreCase))
            {
                return "Naturel";
            }
            
            return renk;
        }
    }

    public class AtaturkUrun
    {
        public string baslik { get; set; } = "";
        public string? kisaAciklama { get; set; }
        public string? aciklama { get; set; }
        public double fiyat { get; set; }
        public string urunTipi { get; set; } = "";
        public string anaGorselUrl { get; set; } = "";
        public List<string>? ekGorselUrls { get; set; }
        public string? kaynak_url { get; set; }
        public List<AtaturkVaryasyon> varyasyonlar { get; set; } = new();
    }

    public class AtaturkVaryasyon
    {
        public string olcu { get; set; } = "";
        public string? cerceveTipi { get; set; }
        public string? cerceveRengi { get; set; }
        public double satisFiyati { get; set; }
        public string? gorselUrl { get; set; }
    }

    public class DiniUrun
    {
        public string baslik { get; set; } = "";
        public string? kisaAciklama { get; set; }
        public string? aciklama { get; set; }
        public double fiyat { get; set; }
        public string urunTipi { get; set; } = "";
        public string anaGorselUrl { get; set; } = "";
        public List<string>? ekGorselUrls { get; set; }
        public string? kaynak_url { get; set; }
        public List<DiniVaryasyon> varyasyonlar { get; set; } = new();
    }

    public class DiniVaryasyon
    {
        public string olcu { get; set; } = "";
        public string? cerceveTipi { get; set; }
        public string? cerceveRengi { get; set; }
        public double satisFiyati { get; set; }
        public string? gorselUrl { get; set; }
    }

    public class DeleteProductsRequest
    {
        public List<int> kategoriIds { get; set; } = new();
    }

    public class JsonUrun
    {
        public string baslik { get; set; } = "";
        public string? kisaAciklama { get; set; }
        public string? aciklama { get; set; }
        public double fiyat { get; set; }
        public string urunTipi { get; set; } = "";
        public string anaGorselUrl { get; set; } = "";
        public List<string>? ekGorselUrls { get; set; }
        public string? kaynak_url { get; set; }
        public List<JsonVaryasyon> varyasyonlar { get; set; } = new();
    }

    public class JsonVaryasyon
    {
        public string olcu { get; set; } = "";
        public string? cerceveTipi { get; set; }
        public string? cerceveRengi { get; set; }
        public double satisFiyati { get; set; }
        public string? gorselUrl { get; set; }
    }
}
