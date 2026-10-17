using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using KanvasProje.Core.Varliklar;
using KanvasProje.Data;
using KanvasProje.Core.Models;
using KanvasProje.Service.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using System.IO.Compression;

namespace KanvasProje.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class UrunController : AdminBaseController
    {
        private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp"
        };

        private static readonly HashSet<string> AllowedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4",
            ".webm",
            ".mov"
        };

        private const long MaxImageFileBytes = 10 * 1024 * 1024;
        private const long MaxVideoFileBytes = 150 * 1024 * 1024;
        private const long MaxExcelFileBytes = 20 * 1024 * 1024;
        private const long MaxCanvasArchiveBytes = 300 * 1024 * 1024;
        private const long MaxCanvasChunkBytes = 9 * 1024 * 1024;

        private readonly KanvasDbContext _context;
        private readonly IWebHostEnvironment _webHost;

        public UrunController(KanvasDbContext context, IWebHostEnvironment webHost)
        {
            _context = context;
            _webHost = webHost;
        }

        public async Task<IActionResult> Index(string? search, int? kategoriId, int page = 1, int pageSize = 20)
        {
            page = Math.Max(page, 1);
            var allowedPageSizes = new[] { 20, 50, 100 };
            if (!allowedPageSizes.Contains(pageSize))
            {
                pageSize = 20;
            }

            var urunlerQuery = _context.Urunler
                .Where(u => !u.SilindiMi)
                .Include(u => u.Kategori)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLowerInvariant();
                var hasParsedId = int.TryParse(term, out var parsedId);
                urunlerQuery = urunlerQuery.Where(x =>
                    x.Baslik.ToLower().Contains(term) ||
                    (x.SKU != null && x.SKU.ToLower().Contains(term)) ||
                    (x.Barkod != null && x.Barkod.ToLower().Contains(term)) ||
                    (hasParsedId && x.Id == parsedId));
            }

            if (kategoriId.HasValue)
            {
                var tumKategoriler = await _context.Kategoriler
                    .AsNoTracking()
                    .Where(x => !x.SilindiMi)
                    .ToListAsync();
                var kategoriIdSet = CategoryTreeHelper.GetDescendantIds(tumKategoriler, kategoriId.Value);
                urunlerQuery = urunlerQuery.Where(x => kategoriIdSet.Contains(x.KategoriId));
            }

            ViewBag.CurrentSearch = search;
            ViewBag.CurrentKategori = kategoriId;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.PageSizeOptions = allowedPageSizes;
            ViewBag.TotalCount = await urunlerQuery.CountAsync();
            ViewBag.TotalPages = (int)Math.Ceiling((double)ViewBag.TotalCount / pageSize);

            var urunler = await urunlerQuery
                .OrderBy(x => x.Sira)
                .ThenByDescending(x => x.OlusturulmaTarihi)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return View(urunler);
        }

        [HttpGet]
        public IActionResult KanvasGorselYukle()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(MaxCanvasChunkBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxCanvasChunkBytes)]
        public async Task<IActionResult> KanvasGorselParcaYukle(
            IFormFile? parca,
            string yuklemeId,
            int parcaNo,
            int toplamParca)
        {
            if (!Guid.TryParse(yuklemeId, out var uploadId) || parca == null || parca.Length == 0 ||
                parca.Length > MaxCanvasChunkBytes || toplamParca is < 1 or > 50 ||
                parcaNo < 0 || parcaNo >= toplamParca)
            {
                return BadRequest(new { error = "Geçersiz yükleme parçası." });
            }

            var uploadFolder = GetCanvasUploadFolder(uploadId);
            Directory.CreateDirectory(uploadFolder);
            var partPath = Path.Combine(uploadFolder, $"{parcaNo:D3}.part");
            await using (var output = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await parca.CopyToAsync(output);
            }

            return Json(new { success = true, parcaNo });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> KanvasGorselYuklemeyiTamamla(string yuklemeId, int toplamParca)
        {
            if (!Guid.TryParse(yuklemeId, out var uploadId) || toplamParca is < 1 or > 50)
            {
                return BadRequest(new { error = "Geçersiz yükleme bilgisi." });
            }

            var uploadFolder = GetCanvasUploadFolder(uploadId);
            var archivePath = Path.Combine(uploadFolder, "kanvas.zip");

            try
            {
                await using (var archiveOutput = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    for (var i = 0; i < toplamParca; i++)
                    {
                        var partPath = Path.Combine(uploadFolder, $"{i:D3}.part");
                        if (!System.IO.File.Exists(partPath))
                        {
                            return BadRequest(new { error = $"{i + 1}. yükleme parçası eksik." });
                        }

                        await using var part = System.IO.File.OpenRead(partPath);
                        await part.CopyToAsync(archiveOutput);
                        if (archiveOutput.Length > MaxCanvasArchiveBytes)
                        {
                            return BadRequest(new { error = "ZIP dosyası 300 MB sınırını aşıyor." });
                        }
                    }
                }

                var fileCount = await ExtractCanvasArchiveAsync(archivePath);
                return Json(new { success = true, message = $"{fileCount:N0} kanvas görseli başarıyla yüklendi." });
            }
            catch (InvalidDataException)
            {
                return BadRequest(new { error = "ZIP dosyası bozuk veya beklenen WebP klasör yapısına sahip değil." });
            }
            catch (Exception)
            {
                return StatusCode(500, new { error = "Görseller işlenirken beklenmeyen bir hata oluştu. Sunucu loglarını kontrol edin." });
            }
            finally
            {
                if (Directory.Exists(uploadFolder))
                {
                    Directory.Delete(uploadFolder, true);
                }
            }
        }

        private static string GetCanvasUploadFolder(Guid uploadId)
        {
            return Path.Combine(Path.GetTempPath(), "canvasia-kanvas-upload", uploadId.ToString("N"));
        }

        private async Task<int> ExtractCanvasArchiveAsync(string archivePath)
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(archivePath);
            var files = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToList();
            if (files.Count == 0 || files.Count > 5000 || files.Any(entry =>
                    !string.Equals(Path.GetExtension(entry.Name), ".webp", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("Geçersiz kanvas görsel arşivi.");
            }

            var persistentRoot = Environment.GetEnvironmentVariable("PERSISTENT_ROOT");
            var baseTarget = string.IsNullOrWhiteSpace(persistentRoot)
                ? Path.Combine(_webHost.WebRootPath, "img", "products")
                : Path.Combine(persistentRoot, "img", "products");
            baseTarget = Path.GetFullPath(baseTarget);
            Directory.CreateDirectory(baseTarget);

            var webRootBase = Path.GetFullPath(Path.Combine(_webHost.WebRootPath, "img", "products"));
            if (!string.Equals(baseTarget, webRootBase, StringComparison.OrdinalIgnoreCase))
            {
                try { Directory.CreateDirectory(webRootBase); } catch { }
            }

            var extractions = files.Select(entry =>
            {
                var normalized = entry.FullName.Replace('\\', '/').TrimStart('/');
                if (normalized.StartsWith("img/products/", StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized["img/products/".Length..];
                }
                else if (!normalized.StartsWith("kanvas-panoramik/", StringComparison.OrdinalIgnoreCase) &&
                         !normalized.StartsWith("hali/", StringComparison.OrdinalIgnoreCase) &&
                         !normalized.StartsWith("cam/", StringComparison.OrdinalIgnoreCase))
                {
                    normalized = "kanvas-panoramik/" + normalized;
                }

                var destination = Path.GetFullPath(Path.Combine(baseTarget, normalized.Replace('/', Path.DirectorySeparatorChar)));
                if (!destination.StartsWith(baseTarget + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("ZIP içinde güvenli olmayan bir dosya yolu bulundu.");
                }

                return (Entry: entry, Destination: destination, RelativePath: normalized);
            }).ToList();

            foreach (var extraction in extractions)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(extraction.Destination)!);
                await using (var output = new FileStream(extraction.Destination, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await using var input = extraction.Entry.Open();
                    await input.CopyToAsync(output);
                }

                if (!string.Equals(baseTarget, webRootBase, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var webDest = Path.Combine(webRootBase, extraction.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(webDest)!);
                        System.IO.File.Copy(extraction.Destination, webDest, true);
                    }
                    catch { }
                }
            }

            return files.Count;
        }

        [HttpGet]
        public async Task<IActionResult> Ekle()
        {
            await PopulateCategorySelectListAsync();
            await PopulateProductMetadataAsync(UrunOzellikCatalog.Genel);

            return View(new Urun
            {
                AktifMi = true,
                MinSiparisAdedi = 1,
                StokDurumu = "Stokta",
                KdvOrani = 20,
                UrunTipi = UrunOzellikCatalog.Genel
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Ekle(Urun urun, IFormFile? resimDosyasi, List<IFormFile>? galeriDosyalari)
        {
            NormalizeOptionalProductFieldsForValidation(urun);
            RemoveOptionalProductModelStateErrors();

            if (!await ValidateProductAsync(urun, resimDosyasi))
            {
                await PopulateCategorySelectListAsync(urun.KategoriId);
                await PopulateProductMetadataAsync(urun.UrunTipi);
                return View(urun);
            }

            var postedVariants = urun.UrunSecenek?.ToList() ?? new List<UrunSecenek>();
            var postedFeatureValues = urun.UrunOzellikleri?.ToList() ?? new List<UrunOzellikDegeri>();
            NormalizeProductInput(urun);
            var supportsCanvasOptions = await SupportsCanvasOptionsAsync(urun.KategoriId);
            if (!postedVariants.Any(IsMeaningfulVariant))
            {
                postedVariants.Add(BuildDefaultProductVariant(urun));
            }
            SanitizeVariantScope(postedVariants, supportsCanvasOptions);

            urun.OlusturulmaTarihi = DateTime.UtcNow;
            urun.SatisSayisi = 0;
            urun.GoruntulenmeSayisi = 0;
            urun.FavoriSayisi = 0;
            urun.SilindiMi = false;
            urun.Sira = await NormalizeProductOrderAsync(urun.Sira);
            urun.UrlYolu = SlugHelper.GenerateSlug(string.IsNullOrWhiteSpace(urun.UrlYolu) ? urun.Baslik : urun.UrlYolu);
            urun.Slug = await GenerateUniqueProductSlugAsync(urun.Slug, urun.Baslik, null);
            urun.UrunSecenek = new List<UrunSecenek>();
            urun.UrunOzellikleri = new List<UrunOzellikDegeri>();

            try
            {
                if (resimDosyasi != null)
                {
                    urun.AnaGorselUrl = await SaveImageAsync(resimDosyasi, urun.Baslik, "ana");
                }
                else
                {
                    urun.AnaGorselUrl = urun.AnaGorselUrl.Trim();
                }

                await using var tx = await _context.Database.BeginTransactionAsync();
                _context.Urunler.Add(urun);
                await _context.SaveChangesAsync();
                await EnsureProductSkuAsync(urun);
                await SyncVariantsAsync(urun, postedVariants);
                await SyncFeatureValuesAsync(urun, postedFeatureValues);
                await SaveGalleryImagesAsync(urun, galeriDosyalari);
                await _context.SaveChangesAsync();
                await EnsureVariantSkusAsync(urun.Id);
                await tx.CommitAsync();
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                await PopulateCategorySelectListAsync(urun.KategoriId);
                await PopulateProductMetadataAsync(urun.UrunTipi);
                return View(urun);
            }

            TempData["Mesaj"] = "Ürün başarıyla eklendi.";
            return RedirectToAction(nameof(Duzenle), new { id = urun.Id });
        }

        [HttpGet]
        public async Task<IActionResult> Duzenle(int id)
        {
            var urun = await _context.Urunler
                .Include(x => x.UrunSecenek)
                .Include(x => x.UrunOzellikleri)
                    .ThenInclude(x => x.UrunOzellikTanimi)
                .Include(x => x.UrunResimleri)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (urun == null)
            {
                return NotFound();
            }

            RepairProductTextForDisplay(urun);
            await PopulateCategorySelectListAsync(urun.KategoriId);
            await PopulateProductMetadataAsync(urun.UrunTipi);
            PopulateMediaMetadata(urun);
            return View(urun);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Duzenle(int id, Urun model, IFormFile? anaResimDosyasi, List<IFormFile>? galeriDosyalari)
        {
            if (id != model.Id)
            {
                return NotFound();
            }

            var urun = await _context.Urunler
                .Include(x => x.UrunSecenek)
                .Include(x => x.UrunOzellikleri)
                    .ThenInclude(x => x.UrunOzellikTanimi)
                .Include(x => x.UrunResimleri)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (urun == null)
            {
                return NotFound();
            }

            NormalizeOptionalProductFieldsForValidation(model);
            RemoveOptionalProductModelStateErrors();

            if (!await ValidateProductAsync(model, anaResimDosyasi, id))
            {
                await PopulateCategorySelectListAsync(model.KategoriId);
                await PopulateProductMetadataAsync(model.UrunTipi);
                model.UrunResimleri = urun.UrunResimleri;
                model.GoruntulenmeSayisi = urun.GoruntulenmeSayisi;
                model.SatisSayisi = urun.SatisSayisi;
                model.FavoriSayisi = urun.FavoriSayisi;
                RepairProductTextForDisplay(model);
                RepairProductTextForDisplay(urun);
                PopulateMediaMetadata(urun);
                return View(model);
            }

            var oldEffectivePrice = urun.IndirimliFiyat ?? urun.Fiyat;
            NormalizeProductInput(model);
            ApplyProductFields(model, urun);
            var supportsCanvasOptions = await SupportsCanvasOptionsAsync(model.KategoriId);
            SanitizeVariantScope(model.UrunSecenek, supportsCanvasOptions);
            urun.Sira = await NormalizeProductOrderAsync(model.Sira);
            urun.UrlYolu = SlugHelper.GenerateSlug(string.IsNullOrWhiteSpace(model.UrlYolu) ? model.Baslik : model.UrlYolu);
            urun.Slug = await GenerateUniqueProductSlugAsync(model.Slug, model.Baslik, id);

            try
            {
                var uploadedNewMainImage = false;
                if (anaResimDosyasi != null && anaResimDosyasi.Length > 0)
                {
                    urun.AnaGorselUrl = await SaveImageAsync(anaResimDosyasi, urun.Baslik, "ana");
                    uploadedNewMainImage = true;
                }
                else if (!string.IsNullOrWhiteSpace(model.AnaGorselUrl))
                {
                    urun.AnaGorselUrl = model.AnaGorselUrl.Trim();
                }

                await using var tx = await _context.Database.BeginTransactionAsync();
                await SyncVariantsAsync(urun, model.UrunSecenek);
                await EnsureProductSkuAsync(urun);
                await EnsureVariantSkusAsync(urun.Id);
                await SyncFeatureValuesAsync(urun, model.UrunOzellikleri);
                await SaveGalleryImagesAsync(urun, galeriDosyalari);
                await EnsureDefaultProductMediaAsync(urun, overwriteMainImage: !uploadedNewMainImage && string.IsNullOrWhiteSpace(urun.AnaGorselUrl));

                await _context.SaveChangesAsync();

                await SyncProductPricesWithVariantsAsync(urun, model, oldEffectivePrice);
                await tx.CommitAsync();
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                await PopulateCategorySelectListAsync(model.KategoriId);
                await PopulateProductMetadataAsync(model.UrunTipi);
                model.UrunResimleri = urun.UrunResimleri;
                model.GoruntulenmeSayisi = urun.GoruntulenmeSayisi;
                model.SatisSayisi = urun.SatisSayisi;
                model.FavoriSayisi = urun.FavoriSayisi;
                RepairProductTextForDisplay(model);
                PopulateMediaMetadata(urun);
                return View(model);
            }

            TempData["Mesaj"] = "Ürün başarıyla güncellendi.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GorselYukle(int id, IFormFile? anaResimDosyasi, List<IFormFile>? galeriDosyalari)
        {
            var urun = await _context.Urunler
                .Include(x => x.UrunResimleri)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (urun == null)
            {
                return NotFound();
            }

            var hasAnaResim = anaResimDosyasi != null && anaResimDosyasi.Length > 0;
            var hasGaleri = galeriDosyalari != null && galeriDosyalari.Any(x => x.Length > 0);
            if (!hasAnaResim && !hasGaleri)
            {
                TempData["Hata"] = "Lütfen yüklenecek en az bir görsel seçin.";
                return RedirectToAction(nameof(Duzenle), new { id });
            }

            try
            {
                if (hasAnaResim)
                {
                    urun.AnaGorselUrl = await SaveImageAsync(anaResimDosyasi!, urun.Baslik, "ana");
                }

                if (hasGaleri)
                {
                    await SaveGalleryImagesAsync(urun, galeriDosyalari);
                }

                await EnsureDefaultProductMediaAsync(urun, overwriteMainImage: !hasAnaResim && string.IsNullOrWhiteSpace(urun.AnaGorselUrl));
                await _context.SaveChangesAsync();
                TempData["Mesaj"] = "Seçilen görseller başarıyla yüklendi.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Hata"] = ex.Message;
            }

            return RedirectToAction(nameof(Duzenle), new { id });
        }

        private async Task SyncProductPricesWithVariantsAsync(Urun urun, Urun model, decimal? previousBasePrice = null)
        {
            var basePrice = model.IndirimliFiyat ?? model.Fiyat;
            var defaultVariant = urun.UrunSecenek
                .FirstOrDefault(x => x.VarsayilanMi && !x.SilindiMi)
                ?? urun.UrunSecenek.FirstOrDefault(x => !x.SilindiMi);

            if (defaultVariant != null && (defaultVariant.SatisFiyati == 0 || (previousBasePrice.HasValue && defaultVariant.SatisFiyati == previousBasePrice.Value)))
            {
                defaultVariant.SatisFiyati = basePrice > 0 ? basePrice : 0;
            }

            var otherVariants = urun.UrunSecenek.Where(x => !x.SilindiMi && x.Id != defaultVariant?.Id);
            foreach (var variant in otherVariants)
            {
                if ((variant.SatisFiyati == 0 || (previousBasePrice.HasValue && variant.SatisFiyati == previousBasePrice.Value + variant.FiyatFarki)) && variant.FiyatFarki != 0)
                {
                    variant.SatisFiyati = basePrice + variant.FiyatFarki;
                }
            }

            await _context.SaveChangesAsync();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Sil(int id)
        {
            var urun = await _context.Urunler.FirstOrDefaultAsync(x => x.Id == id);
            if (urun != null)
            {
                urun.SilindiMi = true;
                urun.AktifMi = false;
                await _context.SaveChangesAsync();
            }

            TempData["Mesaj"] = "Ürün arşive alındı.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TopluSil(List<int> urunIds)
        {
            urunIds = urunIds
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (!urunIds.Any())
            {
                TempData["Mesaj"] = "Silmek için en az bir ürün seçmelisiniz.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index));
            }

            var urunler = await _context.Urunler
                .Where(x => urunIds.Contains(x.Id))
                .ToListAsync();

            foreach (var urun in urunler)
            {
                urun.SilindiMi = true;
                urun.AktifMi = false;
            }

            await _context.SaveChangesAsync();

            TempData["Mesaj"] = $"{urunler.Count} ürün arşive alındı.";
            TempData["Durum"] = "success";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DurumGuncelle(int id, bool aktif)
        {
            var urun = await _context.Urunler.FirstOrDefaultAsync(x => x.Id == id);
            if (urun == null)
            {
                TempData["Mesaj"] = "Ürün bulunamadı.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index));
            }

            urun.AktifMi = aktif;
            await _context.SaveChangesAsync();

            TempData["Mesaj"] = aktif ? "Ürün aktif hale getirildi." : "Ürün pasif hale getirildi.";
            TempData["Durum"] = "success";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TopluDurumGuncelle(List<int> urunIds, bool aktif)
        {
            urunIds = urunIds
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (!urunIds.Any())
            {
                TempData["Mesaj"] = "İşlem için en az bir ürün seçmelisiniz.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index));
            }

            var urunler = await _context.Urunler
                .Where(x => urunIds.Contains(x.Id))
                .ToListAsync();

            foreach (var urun in urunler)
            {
                urun.AktifMi = aktif;
            }

            await _context.SaveChangesAsync();

            TempData["Mesaj"] = aktif
                ? $"{urunler.Count} ürün aktif hale getirildi."
                : $"{urunler.Count} ürün pasif hale getirildi.";
            TempData["Durum"] = "success";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TopluKategoriGuncelle(List<int> urunIds, int yeniKategoriId, string? returnUrl = null)
        {
            urunIds = urunIds?
                .Where(x => x > 0)
                .Distinct()
                .ToList() ?? new List<int>();

            IActionResult RedirectBack()
            {
                if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return LocalRedirect(returnUrl);
                }

                return RedirectToAction(nameof(Index));
            }

            if (!urunIds.Any())
            {
                TempData["Mesaj"] = "Kategori güncellemesi için en az bir ürün seçmelisiniz.";
                TempData["Durum"] = "warning";
                return RedirectBack();
            }

            if (yeniKategoriId <= 0)
            {
                TempData["Mesaj"] = "Lütfen geçerli bir hedef kategori seçiniz.";
                TempData["Durum"] = "warning";
                return RedirectBack();
            }

            var hedefKategori = await _context.Kategoriler.FirstOrDefaultAsync(x => x.Id == yeniKategoriId && x.AktifMi);
            if (hedefKategori == null)
            {
                TempData["Mesaj"] = "Hedef kategori bulunamadı veya pasif durumda.";
                TempData["Durum"] = "warning";
                return RedirectBack();
            }

            var urunler = await _context.Urunler
                .Where(x => urunIds.Contains(x.Id))
                .ToListAsync();

            if (!urunler.Any())
            {
                TempData["Mesaj"] = "Güncellenecek ürün bulunamadı.";
                TempData["Durum"] = "warning";
                return RedirectBack();
            }

            foreach (var urun in urunler)
            {
                urun.KategoriId = hedefKategori.Id;
            }

            await _context.SaveChangesAsync();

            TempData["Mesaj"] = $"{urunler.Count} adet ürünün kategorisi başarıyla \"{hedefKategori.Ad}\" olarak güncellendi.";
            TempData["Durum"] = "success";
            return RedirectBack();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> KaliciSil(int id)
        {
            var sonuc = await TryHardDeleteProductsAsync(new[] { id });

            TempData["Mesaj"] = sonuc.message;
            TempData["Durum"] = sonuc.deletedCount > 0 && sonuc.blockedCount == 0 ? "success" : "warning";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TopluKaliciSil(List<int> urunIds)
        {
            urunIds = urunIds
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (!urunIds.Any())
            {
                TempData["Mesaj"] = "Kalıcı silmek için en az bir ürün seçmelisiniz.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index));
            }

            var sonuc = await TryHardDeleteProductsAsync(urunIds);

            TempData["Mesaj"] = sonuc.message;
            TempData["Durum"] = sonuc.deletedCount > 0 && sonuc.blockedCount == 0 ? "success" : "warning";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BosSkulariOlustur()
        {
            var products = await _context.Urunler
                .IgnoreQueryFilters()
                .Include(x => x.UrunSecenek)
                .Where(x => !x.SilindiMi)
                .OrderBy(x => x.Id)
                .ToListAsync();

            var updatedProducts = 0;
            var updatedVariants = 0;
            var usedProductSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedVariantSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var product in products)
            {
                var originalSku = product.SKU?.Trim() ?? string.Empty;
                product.SKU = BuildUniqueProductSkuForBatch(product.Id, originalSku, usedProductSkus);
                if (!string.Equals(originalSku, product.SKU, StringComparison.Ordinal))
                {
                    updatedProducts++;
                }

                var variantIndex = 1;
                foreach (var variant in product.UrunSecenek.Where(x => !x.SilindiMi).OrderBy(x => x.Sira).ThenBy(x => x.Id))
                {
                    var originalVariantSku = variant.VaryantSku?.Trim() ?? string.Empty;
                    variant.VaryantSku = BuildUniqueVariantSkuForBatch(product.Id, variant.Id, variantIndex, originalVariantSku, usedVariantSkus);
                    if (!string.Equals(originalVariantSku, variant.VaryantSku, StringComparison.Ordinal))
                    {
                        updatedVariants++;
                    }

                    variantIndex++;
                }
            }

            await _context.SaveChangesAsync();

            TempData["Mesaj"] = $"{updatedProducts} ürün SKU ve {updatedVariants} varyasyon SKU tamamlandı.";
            TempData["Durum"] = "success";
            return RedirectToAction(nameof(Index));
        }

        private async Task<(int deletedCount, int blockedCount, string message)> TryHardDeleteProductsAsync(IEnumerable<int> productIds)
        {
            var ids = productIds
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (!ids.Any())
            {
                return (0, 0, "Silinecek ürün bulunamadı.");
            }

            var blockedIds = new HashSet<int>();

            var orderProductIds = await _context.SiparisDetaylari
                .Where(x => ids.Contains(x.UrunId))
                .Select(x => x.UrunId)
                .Distinct()
                .ToListAsync();

            foreach (var productId in orderProductIds)
            {
                blockedIds.Add(productId);
            }

            var cartProductIds = await _context.SepetItems
                .Where(x => ids.Contains(x.UrunId))
                .Select(x => x.UrunId)
                .Distinct()
                .ToListAsync();

            foreach (var productId in cartProductIds)
            {
                blockedIds.Add(productId);
            }

            var favoriteProductIds = await _context.Favoriler
                .Where(x => ids.Contains(x.UrunId))
                .Select(x => x.UrunId)
                .Distinct()
                .ToListAsync();

            foreach (var productId in favoriteProductIds)
            {
                blockedIds.Add(productId);
            }

            var reviewProductIds = await _context.Yorumlar
                .Where(x => ids.Contains(x.UrunId))
                .Select(x => x.UrunId)
                .Distinct()
                .ToListAsync();

            foreach (var productId in reviewProductIds)
            {
                blockedIds.Add(productId);
            }

            var deletableIds = ids.Except(blockedIds).ToList();
            if (!deletableIds.Any())
            {
                return (0, blockedIds.Count, "Seçili ürünler sipariş, sepet, favori veya yorum kayıtlarında kullanıldığı için kalıcı silinemedi. Bu ürünleri pasif yapabilirsiniz.");
            }

            var media = await _context.UrunResimleri
                .IgnoreQueryFilters()
                .Where(x => deletableIds.Contains(x.UrunId))
                .ToListAsync();
            var variants = await _context.UrunSecenekleri
                .Where(x => deletableIds.Contains(x.UrunId))
                .ToListAsync();
            var featureValues = await _context.UrunOzellikDegerleri
                .Where(x => deletableIds.Contains(x.UrunId))
                .ToListAsync();
            var products = await _context.Urunler
                .IgnoreQueryFilters()
                .Where(x => deletableIds.Contains(x.Id))
                .ToListAsync();

            var filesToDelete = media
                .SelectMany(m => new[] { m.ResimYolu, m.ThumbnailYolu, m.MobilResimYolu })
                .Concat(products.Select(p => p.AnaGorselUrl))
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _context.UrunResimleri.RemoveRange(media);
            _context.UrunSecenekleri.RemoveRange(variants);
            _context.UrunOzellikDegerleri.RemoveRange(featureValues);
            _context.Urunler.RemoveRange(products);

            await _context.SaveChangesAsync();

            foreach (var filePath in filesToDelete)
            {
                DeletePhysicalProductFile(filePath);
            }

            if (blockedIds.Count > 0)
            {
                return (products.Count, blockedIds.Count, $"{products.Count} ürün kalıcı silindi. {blockedIds.Count} ürün ilişkili kayıtları olduğu için silinemedi.");
            }

            return (products.Count, 0, $"{products.Count} ürün kalıcı olarak silindi.");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResimSil(int id)
        {
            var resim = await _context.UrunResimleri
                .Include(x => x.Urun)
                    .ThenInclude(x => x.UrunResimleri)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (resim != null)
            {
                var filesToDelete = new[] { resim.ResimYolu, resim.ThumbnailYolu, resim.MobilResimYolu };
                var wasMain = string.Equals(resim.Urun.AnaGorselUrl, resim.ResimYolu, StringComparison.OrdinalIgnoreCase);
                _context.UrunResimleri.Remove(resim);
                resim.Urun.UrunResimleri.Remove(resim);
                if (wasMain)
                {
                    resim.Urun.AnaGorselUrl = string.Empty;
                }
                await EnsureDefaultProductMediaAsync(resim.Urun, overwriteMainImage: wasMain || string.IsNullOrWhiteSpace(resim.Urun.AnaGorselUrl));
                await _context.SaveChangesAsync();
                foreach (var f in filesToDelete)
                {
                    DeletePhysicalProductFile(f);
                }
                return RedirectToAction(nameof(Duzenle), new { id = resim.UrunId });
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MedyaEkle(
            ProductMediaCreateInputModel model,
            IFormFile? medyaDosyasi,
            IFormFile? onizlemeDosyasi,
            IFormFile? mobilGorselDosyasi)
        {
            var urun = await _context.Urunler
                .Include(x => x.UrunResimleri)
                .Include(x => x.UrunSecenek)
                .FirstOrDefaultAsync(x => x.Id == model.UrunId);

            if (urun == null)
            {
                return NotFound();
            }

            var medya = new UrunResim
            {
                UrunId = urun.Id,
                OlusturulmaTarihi = DateTime.UtcNow,
                SilindiMi = false
            };

            var validationError = await ApplyProductMediaInputAsync(
                urun,
                medya,
                model,
                medyaDosyasi,
                onizlemeDosyasi,
                mobilGorselDosyasi);

            if (validationError != null)
            {
                TempData["Hata"] = validationError;
                return RedirectToAction(nameof(Duzenle), new { id = model.UrunId });
            }

            urun.UrunResimleri.Add(medya);
            await EnsureDefaultProductMediaAsync(urun, medya.VarsayilanMi ? medya : null);
            await _context.SaveChangesAsync();

            TempData["Mesaj"] = "Urun medyasi eklendi.";
            return RedirectToAction(nameof(Duzenle), new { id = model.UrunId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MedyaGuncelle(
            ProductMediaUpdateInputModel model,
            IFormFile? medyaDosyasi,
            IFormFile? onizlemeDosyasi,
            IFormFile? mobilGorselDosyasi)
        {
            var medya = await _context.UrunResimleri
                .Include(x => x.Urun)
                    .ThenInclude(x => x.UrunResimleri)
                .FirstOrDefaultAsync(x => x.Id == model.Id && x.UrunId == model.UrunId);

            if (medya == null)
            {
                return NotFound();
            }

            var validationError = await ApplyProductMediaInputAsync(
                medya.Urun,
                medya,
                model,
                medyaDosyasi,
                onizlemeDosyasi,
                mobilGorselDosyasi);

            if (validationError != null)
            {
                TempData["Hata"] = validationError;
                return RedirectToAction(nameof(Duzenle), new { id = model.UrunId });
            }

            await EnsureDefaultProductMediaAsync(medya.Urun, medya.VarsayilanMi ? medya : null);
            await _context.SaveChangesAsync();

            TempData["Mesaj"] = "Urun medyasi guncellendi.";
            return RedirectToAction(nameof(Duzenle), new { id = model.UrunId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MedyaSil(int id)
        {
            var medya = await _context.UrunResimleri
                .Include(x => x.Urun)
                    .ThenInclude(x => x.UrunResimleri)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (medya == null)
            {
                return RedirectToAction(nameof(Index));
            }

            var urunId = medya.UrunId;
            var filesToDelete = new[] { medya.ResimYolu, medya.ThumbnailYolu, medya.MobilResimYolu };
            var wasMain = string.Equals(medya.Urun.AnaGorselUrl, medya.ResimYolu, StringComparison.OrdinalIgnoreCase);
            _context.UrunResimleri.Remove(medya);
            medya.Urun.UrunResimleri.Remove(medya);
            if (wasMain)
            {
                medya.Urun.AnaGorselUrl = string.Empty;
            }
            await EnsureDefaultProductMediaAsync(medya.Urun, overwriteMainImage: wasMain || string.IsNullOrWhiteSpace(medya.Urun.AnaGorselUrl));
            await _context.SaveChangesAsync();

            foreach (var f in filesToDelete)
            {
                DeletePhysicalProductFile(f);
            }

            TempData["Mesaj"] = "Urun medyasi silindi.";
            return RedirectToAction(nameof(Duzenle), new { id = urunId });
        }

        [HttpGet]
        public IActionResult XmlImport()
        {
            return RedirectToAction(nameof(Excel));
        }

        [HttpPost]
        [ActionName("XmlImport")]
        [ValidateAntiForgeryToken]
        public IActionResult XmlImportPost()
        {
            return RedirectToAction(nameof(Excel));
        }

        [HttpGet]
        public async Task<IActionResult> Excel()
        {
            ViewBag.ImportHistory = await GetProductExcelImportHistoryAsync();
            return View("Excel");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Excel(IFormFile excelDosyasi, string islemTipi)
        {
            var history = await GetProductExcelImportHistoryAsync();
            var operation = NormalizeExcelOperation(islemTipi);

            if (excelDosyasi == null || excelDosyasi.Length == 0)
            {
                ViewBag.Hata = "Lütfen bir Excel dosyası seçin.";
                ViewBag.ImportHistory = history;
                return View("Excel");
            }

            if (excelDosyasi.Length > MaxExcelFileBytes)
            {
                ViewBag.Hata = "Excel dosyasi 20 MB'dan buyuk olamaz.";
                ViewBag.ImportHistory = history;
                return View("Excel");
            }

            var extension = Path.GetExtension(excelDosyasi.FileName);
            if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                ViewBag.Hata = "Sadece .xlsx formatında Excel dosyası yükleyebilirsiniz.";
                ViewBag.ImportHistory = history;
                return View("Excel");
            }

            var savedFileName = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.xlsx";
            var importFolder = GetProductExcelImportFolder();
            Directory.CreateDirectory(importFolder);
            var savedPath = Path.Combine(importFolder, savedFileName);

            await using (var fileStream = new FileStream(savedPath, FileMode.Create))
            {
                await excelDosyasi.CopyToAsync(fileStream);
            }

            var report = new ProductExcelImportReport
            {
                Id = Guid.NewGuid().ToString("N"),
                OriginalFileName = Path.GetFileName(excelDosyasi.FileName),
                StoredFileName = savedFileName,
                OperationType = operation,
                OperationLabel = GetExcelOperationLabel(operation),
                UploadedAt = DateTime.Now
            };

            try
            {
                await ProcessProductExcelImportAsync(savedPath, operation, report);
                report.Status = report.ErrorCount > 0 ? "Kısmi Başarılı" : "Başarılı";
                ViewBag.Mesaj = $"{report.OperationLabel} tamamlandı. Başarılı: {report.SuccessCount}, Hatalı: {report.ErrorCount}.";
            }
            catch (Exception ex)
            {
                report.Status = "Hatalı";
                report.Errors.Add("Dosya işlenemedi: " + ex.Message);
                ViewBag.Hata = "Dosya işlenemedi: " + ex.Message;
            }

            history.Insert(0, report);
            await SaveProductExcelImportHistoryAsync(history);
            ViewBag.ImportHistory = history;
            ViewBag.SonRapor = report;
            return View("Excel");
        }

        public async Task<IActionResult> ExcelSablon(string tip)
        {
            var operation = NormalizeExcelOperation(tip);
            ExcelPackage.License.SetNonCommercialOrganization("MeteorGaleri");
            using var package = new ExcelPackage();

            var infoSheet = package.Workbook.Worksheets.Add("Bilgilendirme");
            BuildInfoSheet(infoSheet, operation);

            var worksheet = package.Workbook.Worksheets.Add("Şablon");
            var headers = GetTemplateHeaders(operation);

            for (var i = 0; i < headers.Length; i++)
            {
                worksheet.Cells[1, i + 1].Value = headers[i];
            }

            FillTemplateSampleRow(worksheet, operation);
            StyleTemplateSampleRow(worksheet, headers.Length);
            StyleExcelHeader(worksheet, headers.Length);
            StyleRequiredTemplateHeaders(worksheet, headers, operation);
            AddTemplateNotes(worksheet, operation, headers.Length);
            await AddCategoryReferenceSheetAsync(package);

            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
            infoSheet.Column(1).Width = 32;
            infoSheet.Column(2).Width = 70;

            return File(
                package.GetAsByteArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"urun-{operation}-sablon.xlsx");
        }

        private void BuildInfoSheet(ExcelWorksheet sheet, string operation)
        {
            var (title, color, bgColor, sections) = operation switch
            {
                "update" => (
                    "■ ÜRÜN GÜNCELLEME ŞABLONU",
                    System.Drawing.Color.White,
                    System.Drawing.Color.FromArgb(75, 75, 75),
                    new[] {
                        ("NASIL KULLANILIR?", new[] {
                            "1. Şablon sayfasına gidin. 'Id' sütununa güncellemek istediğiniz ürünün numarasını yazın.",
                            "2. Güncellemek istediğiniz alanları aynı satırda doldurun.",
                            "3. Boş bıraktığınız alanlar DEĞİŞTİRİLMEZ — mevcut değer korunur.",
                            "4. Dosyayı kaydedin ve Excel İşlemleri sayfasından 'Ürün Güncelleme' seçeneğiyle yükleyin.",
                        }),
                        ("ZORUNLU ALAN", new[] {
                            "Id — Güncellenecek ürünün veritabanı numarası. Bu alan kesinlikle doldurulmalıdır.",
                            "Id boş bırakılırsa veya veritabanında bulunamazsa o satır ATLANIR.",
                            "Ürün Id'yi, ürün listesi sayfasından veya 'Tüm Ürünleri Excele Aktar' fonksiyonundan öğrenebilirsiniz.",
                        }),
                        ("AKTİF Mİ KULLANIMI", new[] {
                            "Değerler: 'Evet' veya 'Hayır' olarak yazılmalıdır.",
                            "Büyük/küçük harf duyarlılığı yoktur (evet, EVET, EvEt — hepsi geçerli).",
                            "Boş bırakılırsa güncellenmez.",
                        }),
                        ("KATEGORİ DEĞİŞTİRME", new[] {
                            "Kategori Id veya Kategori Adı'ndan en az biri doldurulmalıdır.",
                            "Her ikisi de doluysa Kategori Id öncelikli olarak kullanılır.",
                            "Geçersiz kategori bilgisi verilirse güncelleme HATA verir — dikkat edin!",
                            "Mevcut kategorileri görmek için 'Kategori Rehberi' sayfasına bakın.",
                        }),
                        ("FİYAT GÜNCELLEME KURALLARI", new[] {
                            "Satış Fiyatı sıfırdan büyük olmalıdır.",
                            "İndirimli Fiyat boş bırakılırsa ürünün indirimi kaldırılır.",
                            "İndirimli Fiyat, Satış Fiyatı'na eşit veya büyük olursa indirim uygulanmaz.",
                        }),
                    }
                ),
                "price" => (
                    "■ FİYAT GÜNCELLEME ŞABLONU",
                    System.Drawing.Color.White,
                    System.Drawing.Color.FromArgb(75, 75, 75),
                    new[] {
                        ("NASIL KULLANILIR?", new[] {
                            "1. Şablon sayfasına gidin.",
                            "2. 'Id' veya 'SKU' sütununa ürün bilgisini yazın.",
                            "3. Yeni 'Satış Fiyatı'nı yazın.",
                            "4. İndirimli fiyat kullanmak istiyorsanız 'İndirimli Fiyat' sütununu da doldurun.",
                            "5. Dosyayı kaydedin ve Excel İşlemleri sayfasından 'Fiyat Güncelleme' seçeneğiyle yükleyin.",
                        }),
                        ("ÜRÜN BULMA", new[] {
                            "Ürün Id veya SKU'dan biri mutlaka doldurulmalıdır.",
                            "Her ikisi de doluysa Id öncelikli olarak kullanılır.",
                            "Ne Id ne de SKU girilirse o satır HATA verir.",
                            "SKU: Ürünün stok yönetim kodu — ürün düzenleme sayfasından öğrenilebilir.",
                        }),
                        ("İNDİRİM KURALLARI", new[] {
                            "İndirimli Fiyat boş bırakılırsa: Ürünün mevcut indirimi varsa KALDIRILIR.",
                            "İndirimli Fiyat ≥ Satış Fiyatı olursa: İndirim UYGULANMAZ.",
                            "Geçerli örnek: Satış Fiyatı = 500, İndirimli Fiyat = 399 ✓",
                        }),
                        ("DİKKAT", new[] {
                            "Bu şablon sadece fiyat günceller — ürün adı, kategori, açıklama DEĞİŞMEZ.",
                            "Stok, görsel ve diğer alanlar etkilenmez.",
                            "Fiyat güncellemesi geri alınamaz — yüklemeden önce dosyayı kontrol edin.",
                        }),
                    }
                ),
                _ => (
                    "■ ÜRÜN YÜKLEME ŞABLONU",
                    System.Drawing.Color.White,
                    System.Drawing.Color.FromArgb(49, 53, 17),
                    new[] {
                        ("NASIL KULLANILIR?", new[] {
                            "1. Sarı renkli örnek satırı ASLA değiştirmeyin — bu referans satırıdır.",
                            "2. Örnek satırın ALTINA kendi verilerinizi ekleyin (satır 3'ten itibaren).",
                            "3. Zorunlu alanları mutlaka doldurun.",
                            "4. Kategori Id veya Kategori Adı'ndan en az birini mutlaka yazın.",
                            "5. Dosyayı kaydedin ve Excel İşlemleri sayfasından 'Ürün Yükleme' seçeneğiyle yükleyin.",
                        }),
                        ("ZORUNLU ALANLAR", new[] {
                            "Ürün Adı — Ürünün başlığı. Boş bırakılamaz.",
                            "Satış Fiyatı — Sıfırdan büyük bir sayı olmalıdır.",
                            "Ana Görsel URL — Ürünün ana görselinin URL'i. Boş bırakılamaz.",
                            "Kategori — Kategori Id veya Kategori Adı'ndan en az biri doldurulmalıdır.",
                        }),
                        ("KATEGORİ SİSTEMİ", new[] {
                            "Kategori Id: Sayısal ID (örn: 1, 2, 3). Veritabanı numarasıdır.",
                            "Kategori Adı: Kategorinin adı (örn: 'Soyut Kanvas', 'Modern Tablolar').",
                            "Her ikisi de doluysa Kategori Id öncelikli olarak kullanılır.",
                            "Geçersiz kategori verilirse ürün HATA verir.",
                            "Mevcut kategorileri görmek için 'Kategori Rehberi' sayfasını açın.",
                        }),
                        ("FİYAT VE İNDİRİM", new[] {
                            "Satış Fiyatı: Ana ürün fiyatı. Sıfırdan büyük olmalıdır.",
                            "İndirimli Fiyat: Opsiyoneldir. Satış Fiyatı'ndan KÜÇÜK olmalıdır.",
                            "İndirimli Fiyat ≥ Satış Fiyatı olursa indirim uygulanmaz.",
                            "Varyant Satış Fiyatı: Boş bırakılırsa ana ürün fiyatı varyant için kullanılır.",
                        }),
                        ("EBAT VE STOK", new[] {
                            "Ebat: Örn: '50x70cm', '40x30cm', '60x40 cm'. Boş bırakılırsa 'Standart' olarak kaydedilir.",
                            "Stok: Stok adedi. Boş bırakılırsa otomatik 100 adet olarak atanır.",
                            "Stok 0 veya negatif olursa 0 (stok yok) olarak kaydedilir.",
                        }),
                        ("ÇERÇEVE SİSTEMİ", new[] {
                            "Bu sistem sadece Kanvas ürünleri için çerçeve seçeneği sunar.",
                            "Cerceve Tipi: 'Çerçevesiz', 'Ahşap Çerçeve', 'Metal Çerçeve' — Boş bırakılırsa otomatik 'Çerçevesiz' atanır.",
                            "Cerceve Rengi: Sadece çerçevesi olan ürünlerde kullanılır. Değerler: 'Siyah', 'Beyaz', 'Gold', 'Gümüş', 'Meşe', 'Ceviz'.",
                            "Malzeme Turu: Çerçeve türüne göre opsiyoneldir.",
                            "NOT: Çerçeve seçimi ürün fiyatını ETKİLEMEZ — fiyat farkı ayrıca yönetilir.",
                        }),
                        ("AKTİF Mİ KULLANIMI", new[] {
                            "Değerler: 'Evet' veya 'Hayır' olarak yazılmalıdır.",
                            "Büyük/küçük harf duyarlılığı yoktur.",
                            "Boş bırakılırsa otomatik olarak 'Evet' (aktif) olarak kaydedilir.",
                            "Aktif olmayan ürünler site dışında görünmez.",
                        }),
                        ("VARYANT GÖRSEL", new[] {
                            "Varyant Görsel URL: Opsiyoneldir. Boş bırakılırsa Ana Görsel URL kullanılır.",
                            "Her varyant için farklı bir görsel URL'i girilebilir.",
                            "Görsel URL'si geçerli bir internet adresi olmalıdır.",
                        }),
                        ("VERİ KONTROLÜ", new[] {
                            "Yükleme sonrası rapor ekranında başarılı ve hatalı satırlar listelenir.",
                            "Hatalı satırlar için sebep açıklaması verilir.",
                            "Başarısız satırlardaki hataları düzeltip aynı dosyayla tekrar yükleyebilirsiniz.",
                        }),
                    }
                )
            };

            sheet.Cells["A1"].Value = title;
            sheet.Cells["A1"].Style.Font.Size = 16;
            sheet.Cells["A1"].Style.Font.Bold = true;
            sheet.Cells["A1"].Style.Font.Color.SetColor(color);
            sheet.Cells["A1"].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells["A1"].Style.Fill.BackgroundColor.SetColor(bgColor);
            sheet.Cells["A2"].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells["A2"].Style.Fill.BackgroundColor.SetColor(bgColor);
            sheet.Cells["B2"].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells["B2"].Style.Fill.BackgroundColor.SetColor(bgColor);

            int row = 3;
            foreach (var (sectionTitle, lines) in sections)
            {
                sheet.Cells[row, 1].Value = sectionTitle;
                sheet.Cells[row, 1].Style.Font.Size = 11;
                sheet.Cells[row, 1].Style.Font.Bold = true;
                sheet.Cells[row, 1].Style.Font.Color.SetColor(System.Drawing.Color.White);
                sheet.Cells[row, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                sheet.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(95, 110, 50));
                sheet.Cells[row, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                sheet.Cells[row, 1].Style.WrapText = true;
                sheet.Row(row).Height = 22;
                row++;

                foreach (var line in lines)
                {
                    sheet.Cells[row, 1].Value = "•";
                    sheet.Cells[row, 1].Style.Font.Size = 10;
                    sheet.Cells[row, 1].Style.Font.Color.SetColor(System.Drawing.Color.FromArgb(49, 53, 17));
                    sheet.Cells[row, 1].Style.Font.Bold = true;

                    sheet.Cells[row, 2].Value = line;
                    sheet.Cells[row, 2].Style.Font.Size = 10;
                    sheet.Cells[row, 2].Style.Font.Color.SetColor(System.Drawing.Color.FromArgb(45, 45, 45));
                    sheet.Cells[row, 2].Style.WrapText = true;
                    sheet.Row(row).Height = 18;
                    row++;
                }

                row++;
            }

            sheet.Cells[$"A1:B{row}"].Style.Border.Top.Style = ExcelBorderStyle.Thin;
            sheet.Cells[$"A1:B{row}"].Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            sheet.Cells[$"A1:B{row}"].Style.Border.Left.Style = ExcelBorderStyle.Thin;
            sheet.Cells[$"A1:B{row}"].Style.Border.Right.Style = ExcelBorderStyle.Thin;
            sheet.Cells[$"A1:B{row}"].Style.Border.Top.Color.SetColor(System.Drawing.Color.FromArgb(180, 175, 160));
            sheet.Cells[$"A1:B{row}"].Style.Border.Bottom.Color.SetColor(System.Drawing.Color.FromArgb(180, 175, 160));
            sheet.Cells[$"A1:B{row}"].Style.Border.Left.Color.SetColor(System.Drawing.Color.FromArgb(180, 175, 160));
            sheet.Cells[$"A1:B{row}"].Style.Border.Right.Color.SetColor(System.Drawing.Color.FromArgb(180, 175, 160));
        }

        public async Task<IActionResult> UrunExcelExport()
        {
            ExcelPackage.License.SetNonCommercialOrganization("MeteorGaleri");
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("Ürünler");

            var headers = new[]
            {
                "Id",
                "Ürün Adı",
                "Kategori Id",
                "Kategori",
                "Satış Fiyatı",
                "İndirimli Fiyat",
                "Ana Görsel URL",
                "Kısa Açıklama",
                "Detaylı Açıklama",
                "Aktif Mi",
                "Slug",
                "SEO URL",
                "SKU",
                "Barkod",
                "Marka",
                "Ürün Tipi",
                "Etiketler",
                "KDV Oranı",
                "Üretim Süresi Gün",
                "Kargoya Veriliş Gün",
                "Sıra",
                "Oluşturulma Tarihi"
            };

            for (var i = 0; i < headers.Length; i++)
            {
                worksheet.Cells[1, i + 1].Value = headers[i];
            }

            var products = await _context.Urunler
                .IgnoreQueryFilters()
                .Include(x => x.Kategori)
                .Where(x => !x.SilindiMi)
                .OrderBy(x => x.Id)
                .ToListAsync();

            var row = 2;
            foreach (var product in products)
            {
                worksheet.Cells[row, 1].Value = product.Id;
                worksheet.Cells[row, 2].Value = product.Baslik;
                worksheet.Cells[row, 3].Value = product.KategoriId;
                worksheet.Cells[row, 4].Value = product.Kategori?.Ad;
                worksheet.Cells[row, 5].Value = product.Fiyat;
                worksheet.Cells[row, 6].Value = product.IndirimliFiyat;
                worksheet.Cells[row, 7].Value = product.AnaGorselUrl;
                worksheet.Cells[row, 8].Value = product.KisaAciklama;
                worksheet.Cells[row, 9].Value = product.Aciklama;
                worksheet.Cells[row, 10].Value = product.AktifMi ? "Evet" : "Hayır";
                worksheet.Cells[row, 11].Value = product.Slug;
                worksheet.Cells[row, 12].Value = product.UrlYolu;
                worksheet.Cells[row, 13].Value = product.SKU;
                worksheet.Cells[row, 14].Value = product.Barkod;
                worksheet.Cells[row, 15].Value = product.Marka;
                worksheet.Cells[row, 16].Value = product.UrunTipi;
                worksheet.Cells[row, 17].Value = product.Etiketler;
                worksheet.Cells[row, 18].Value = product.KdvOrani;
                worksheet.Cells[row, 19].Value = product.UretimSuresiGun;
                worksheet.Cells[row, 20].Value = product.KargoyaVerilisSuresiGun;
                worksheet.Cells[row, 21].Value = product.Sira;
                worksheet.Cells[row, 22].Value = product.OlusturulmaTarihi.ToLocalTime();
                row++;
            }

            StyleExcelHeader(worksheet, headers.Length);
            worksheet.Column(5).Style.Numberformat.Format = "#,##0.00";
            worksheet.Column(6).Style.Numberformat.Format = "#,##0.00";
            worksheet.Column(22).Style.Numberformat.Format = "dd.mm.yyyy hh:mm";
            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();

            await AddCategoryReferenceSheetAsync(package);

            return File(
                package.GetAsByteArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"tum-urunler-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
        }

        private async Task ProcessProductExcelImportAsync(string filePath, string operation, ProductExcelImportReport report)
        {
            ExcelPackage.License.SetNonCommercialOrganization("MeteorGaleri");
            using var package = new ExcelPackage(new FileInfo(filePath));
            var worksheet = SelectProductImportWorksheet(package);
            if (worksheet?.Dimension == null)
            {
                throw new InvalidOperationException("Excel dosyasında okunabilir veri bulunamadı.");
            }

            var headers = BuildHeaderMap(worksheet);
            var categories = await _context.Kategoriler
                .IgnoreQueryFilters()
                .Where(x => !x.SilindiMi && x.AktifMi)
                .ToListAsync();

            for (var row = 2; row <= worksheet.Dimension.End.Row; row++)
            {
                if (IsExcelRowEmpty(worksheet, row) ||
                    IsTemplateHelperRow(worksheet, row, operation) ||
                    IsTemplateSampleRow(worksheet, headers, row, operation))
                {
                    continue;
                }

                report.TotalRows++;

                try
                {
                    var result = operation switch
                    {
                        "update" => await ImportProductUpdateRowAsync(worksheet, headers, categories, row),
                        "price" => await ImportProductPriceRowAsync(worksheet, headers, row),
                        _ => await ImportProductCreateRowAsync(worksheet, headers, categories, row)
                    };

                    if (result.Success)
                    {
                        report.SuccessCount++;
                    }
                    else
                    {
                        report.Errors.Add($"Satır {row}: {result.Message}");
                    }
                }
                catch (Exception ex)
                {
                    report.Errors.Add($"Satır {row}: {ex.Message}");
                }
            }
        }

        private async Task<(bool Success, string Message)> ImportProductCreateRowAsync(
            ExcelWorksheet worksheet,
            Dictionary<string, int> headers,
            List<Kategori> categories,
            int row)
        {
            var title = GetExcelString(worksheet, headers, row, "urunadi", "ürünadı", "urunadi");
            var price = GetExcelDecimal(worksheet, headers, row, "satisfiyati", "satışfiyatı");
            var imageUrl = GetExcelString(worksheet, headers, row, "anagorselurl", "anagörselurl");
            var categoryId = ResolveExcelCategoryId(worksheet, headers, categories, row);

            if (string.IsNullOrWhiteSpace(title))
            {
                return (false, "Ürün adı zorunludur.");
            }

            if (!categoryId.HasValue)
            {
                return (false, "Kategori Id veya kategori adı zorunludur.");
            }

            if (!price.HasValue || price.Value <= 0)
            {
                return (false, "Satış fiyatı sıfırdan büyük olmalıdır.");
            }

            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                return (false, "Ana Görsel URL zorunludur.");
            }

            var discount = GetExcelDecimal(worksheet, headers, row, "indirimlifiyat", "indirimlifiyatı");
            if (discount.HasValue && discount.Value >= price.Value)
            {
                return (false, "İndirimli fiyat satış fiyatından küçük olmalıdır.");
            }

            var product = new Urun
            {
                Baslik = title,
                KisaAd = title,
                KategoriId = categoryId.Value,
                Fiyat = price.Value,
                IndirimliFiyat = discount,
                AnaGorselUrl = imageUrl,
                KisaAciklama = GetExcelString(worksheet, headers, row, "kisaaciklama", "kısaaçıklama"),
                Aciklama = GetExcelString(worksheet, headers, row, "detayliaciklama", "detaylıaçıklama"),
                AktifMi = GetExcelBool(worksheet, headers, row, true, "aktifmi", "aktif"),
                MinSiparisAdedi = 1,
                KdvOrani = 20,
                StokDurumu = "Stokta",
                UrunTipi = UrunOzellikCatalog.Genel,
                UrlYolu = SlugHelper.GenerateSlug(title),
                Slug = await GenerateUniqueProductSlugAsync(null, title, null),
                Sira = await NormalizeProductOrderAsync(0),
                OlusturulmaTarihi = DateTime.UtcNow
            };

            var supportsCanvasOptions = SupportsCanvasOptions(categories, categoryId.Value);
            product.UrunSecenek.Add(BuildImportedProductVariant(product, worksheet, headers, row, supportsCanvasOptions));

            _context.Urunler.Add(product);
            await _context.SaveChangesAsync();
            await EnsureProductSkuAsync(product);
            await EnsureVariantSkusAsync(product.Id);
            return (true, string.Empty);
        }

        private static UrunSecenek BuildImportedProductVariant(
            Urun product,
            ExcelWorksheet worksheet,
            Dictionary<string, int> headers,
            int row,
            bool supportsCanvasOptions)
        {
            var salePrice = GetExcelDecimal(worksheet, headers, row, "varyantsatisfiyati", "varyantsatis", "varyasyonfiyati")
                ?? product.IndirimliFiyat
                ?? product.Fiyat;

            var olcu = supportsCanvasOptions
                ? GetExcelString(worksheet, headers, row, "olcu", "ebat", "varyasyon", "varyasyonadi")
                : string.Empty;
            var variantSku = GetExcelString(worksheet, headers, row, "varyantsku", "varyasyonsku");
            var stock = GetExcelInt(worksheet, headers, row, "stok", "stokadedi", "varyantstok") ?? 100;
            var cerceveTipi = supportsCanvasOptions
                ? GetExcelString(worksheet, headers, row, "cercevetipi", "cerceve")
                : string.Empty;
            if (string.IsNullOrWhiteSpace(cerceveTipi))
            {
                cerceveTipi = supportsCanvasOptions ? "Cercevesiz" : string.Empty;
            }

            return new UrunSecenek
            {
                Olcu = string.IsNullOrWhiteSpace(olcu) ? (supportsCanvasOptions ? "Standart" : string.Empty) : olcu,
                CerceveTipi = cerceveTipi,
                CerceveRengi = supportsCanvasOptions ? GetExcelString(worksheet, headers, row, "cerceverengi") : string.Empty,
                CerceveKalinligi = supportsCanvasOptions ? GetExcelString(worksheet, headers, row, "cercevekalinligi", "kalinlik") : string.Empty,
                MalzemeTuru = supportsCanvasOptions ? GetExcelString(worksheet, headers, row, "malzemeturu", "malzeme") : string.Empty,
                Yon = GetExcelString(worksheet, headers, row, "yon"),
                VaryantSku = string.IsNullOrWhiteSpace(variantSku) ? product.SKU : variantSku,
                SatisFiyati = salePrice,
                FiyatFarki = salePrice - product.Fiyat,
                StokAdedi = stock < 0 ? 0 : stock,
                UretimSuresiGun = GetExcelInt(worksheet, headers, row, "uretimsuresigun", "uretimgunu") ?? product.UretimSuresiGun,
                GorselUrl = GetExcelString(worksheet, headers, row, "varyantgorselurl", "varyasyongorselurl"),
                ParcaSayisi = supportsCanvasOptions ? Math.Max(1, GetExcelInt(worksheet, headers, row, "parcasayisi", "parca") ?? 1) : 1,
                AktifMi = true,
                VarsayilanMi = true,
                Sira = 1
            };
        }

        private static UrunSecenek BuildDefaultProductVariant(Urun product)
        {
            var salePrice = product.IndirimliFiyat.HasValue && product.IndirimliFiyat.Value > 0 && product.IndirimliFiyat.Value < product.Fiyat
                ? product.IndirimliFiyat.Value
                : product.Fiyat;

            return new UrunSecenek
            {
                Olcu = "Standart",
                CerceveTipi = "Cercevesiz",
                CerceveRengi = string.Empty,
                CerceveKalinligi = string.Empty,
                MalzemeTuru = string.Empty,
                SatisFiyati = salePrice,
                FiyatFarki = salePrice - product.Fiyat,
                StokAdedi = 100,
                UretimSuresiGun = product.UretimSuresiGun,
                GorselUrl = product.AnaGorselUrl,
                ParcaSayisi = 1,
                AktifMi = true,
                VarsayilanMi = true,
                Sira = 1
            };
        }

        private async Task EnsureProductSkuAsync(Urun product)
        {
            product.SKU = await BuildUniqueProductSkuAsync(product.Id, product.SKU);
            await _context.SaveChangesAsync();
        }

        private async Task EnsureVariantSkusAsync(int productId)
        {
            var product = await _context.Urunler
                .IgnoreQueryFilters()
                .Include(x => x.UrunSecenek)
                .FirstOrDefaultAsync(x => x.Id == productId);

            if (product == null)
            {
                return;
            }

            var variantIndex = 1;
            foreach (var variant in product.UrunSecenek.Where(x => !x.SilindiMi).OrderBy(x => x.Sira).ThenBy(x => x.Id))
            {
                if (string.IsNullOrWhiteSpace(variant.VaryantSku))
                {
                    variant.VaryantSku = await BuildUniqueVariantSkuAsync(product.Id, variant.Id, variantIndex, variant.VaryantSku);
                }
                else
                {
                    variant.VaryantSku = await BuildUniqueVariantSkuAsync(product.Id, variant.Id, variantIndex, variant.VaryantSku);
                }

                variantIndex++;
            }

            await _context.SaveChangesAsync();
        }

        private static string BuildProductSku(int productId)
        {
            return $"URN-{productId:D6}";
        }

        private static string BuildVariantSku(int productId, int variantId, int variantIndex)
        {
            var suffix = variantId > 0 ? variantId : variantIndex;
            return $"{BuildProductSku(productId)}-VAR-{suffix:D6}";
        }

        private async Task<string> BuildUniqueProductSkuAsync(int productId, string? requestedSku)
        {
            var candidate = requestedSku?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(candidate) && !await ProductSkuExistsAsync(candidate, productId))
            {
                return candidate;
            }

            var baseSku = BuildProductSku(productId);
            candidate = baseSku;
            var counter = 2;
            while (await ProductSkuExistsAsync(candidate, productId))
            {
                candidate = $"{baseSku}-{counter++}";
            }

            return candidate;
        }

        private async Task<string> BuildUniqueVariantSkuAsync(int productId, int variantId, int variantIndex, string? requestedSku)
        {
            var candidate = requestedSku?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(candidate) && !await VariantSkuExistsAsync(candidate, variantId))
            {
                return candidate;
            }

            var baseSku = BuildVariantSku(productId, variantId, variantIndex);
            candidate = baseSku;
            var counter = 2;
            while (await VariantSkuExistsAsync(candidate, variantId))
            {
                candidate = $"{baseSku}-{counter++}";
            }

            return candidate;
        }

        private async Task<bool> ProductSkuExistsAsync(string sku, int excludedProductId)
        {
            var normalizedSku = sku.ToLowerInvariant();
            return await _context.Urunler
                .IgnoreQueryFilters()
                .AnyAsync(x => x.Id != excludedProductId && x.SKU != null && x.SKU.ToLower() == normalizedSku);
        }

        private async Task<bool> VariantSkuExistsAsync(string sku, int excludedVariantId)
        {
            var normalizedSku = sku.ToLowerInvariant();
            return await _context.UrunSecenekleri
                .AnyAsync(x => x.Id != excludedVariantId && x.VaryantSku != null && x.VaryantSku.ToLower() == normalizedSku);
        }

        private static string BuildUniqueProductSkuForBatch(int productId, string currentSku, HashSet<string> usedSkus)
        {
            var candidate = currentSku.Trim();
            if (string.IsNullOrWhiteSpace(candidate) || usedSkus.Contains(candidate))
            {
                candidate = BuildProductSku(productId);
                var counter = 2;
                while (usedSkus.Contains(candidate))
                {
                    candidate = $"{BuildProductSku(productId)}-{counter++}";
                }
            }

            usedSkus.Add(candidate);
            return candidate;
        }

        private static string BuildUniqueVariantSkuForBatch(int productId, int variantId, int variantIndex, string currentSku, HashSet<string> usedSkus)
        {
            var candidate = currentSku.Trim();
            if (string.IsNullOrWhiteSpace(candidate) || usedSkus.Contains(candidate))
            {
                var baseSku = BuildVariantSku(productId, variantId, variantIndex);
                candidate = baseSku;
                var counter = 2;
                while (usedSkus.Contains(candidate))
                {
                    candidate = $"{baseSku}-{counter++}";
                }
            }

            usedSkus.Add(candidate);
            return candidate;
        }

        private async Task<(bool Success, string Message)> ImportProductUpdateRowAsync(
            ExcelWorksheet worksheet,
            Dictionary<string, int> headers,
            List<Kategori> categories,
            int row)
        {
            var id = GetExcelInt(worksheet, headers, row, "id", "urunid", "ürünid");
            if (!id.HasValue)
            {
                return (false, "Id zorunludur.");
            }

            var product = await _context.Urunler.FirstOrDefaultAsync(x => x.Id == id.Value);
            if (product == null)
            {
                return (false, $"#{id.Value} Id değerine sahip ürün bulunamadı.");
            }

            var title = GetExcelString(worksheet, headers, row, "urunadi", "ürünadı");
            if (!string.IsNullOrWhiteSpace(title))
            {
                product.Baslik = title;
                product.KisaAd = string.IsNullOrWhiteSpace(product.KisaAd) ? title : product.KisaAd;
                product.UrlYolu = SlugHelper.GenerateSlug(title);
                product.Slug = await GenerateUniqueProductSlugAsync(product.Slug, title, product.Id);
            }

            var categoryId = ResolveExcelCategoryId(worksheet, headers, categories, row);
            if (categoryId.HasValue)
            {
                product.KategoriId = categoryId.Value;
            }

            var price = GetExcelDecimal(worksheet, headers, row, "satisfiyati", "satışfiyatı");
            if (price.HasValue)
            {
                if (price.Value <= 0)
                {
                    return (false, "Satış fiyatı sıfırdan büyük olmalıdır.");
                }
                product.Fiyat = price.Value;
            }

            var discount = GetExcelDecimal(worksheet, headers, row, "indirimlifiyat", "indirimlifiyatı");
            if (discount.HasValue)
            {
                product.IndirimliFiyat = discount.Value <= 0 ? null : discount.Value;
            }

            if (product.IndirimliFiyat.HasValue && product.IndirimliFiyat.Value >= product.Fiyat)
            {
                return (false, "İndirimli fiyat satış fiyatından küçük olmalıdır.");
            }

            SetProductStringIfPresent(product, worksheet, headers, row, "anagorselurl", (p, v) => p.AnaGorselUrl = v, "anagörselurl");
            SetProductStringIfPresent(product, worksheet, headers, row, "kisaaciklama", (p, v) => p.KisaAciklama = v, "kısaaçıklama");
            SetProductStringIfPresent(product, worksheet, headers, row, "detayliaciklama", (p, v) => p.Aciklama = v, "detaylıaçıklama");

            if (TryGetExcelCell(worksheet, headers, row, out var activeValue, "aktifmi", "aktif"))
            {
                product.AktifMi = ParseExcelBool(activeValue, product.AktifMi);
            }

            await _context.SaveChangesAsync();
            return (true, string.Empty);
        }

        private async Task<(bool Success, string Message)> ImportProductPriceRowAsync(
            ExcelWorksheet worksheet,
            Dictionary<string, int> headers,
            int row)
        {
            var id = GetExcelInt(worksheet, headers, row, "id", "urunid", "ürünid");
            var sku = GetExcelString(worksheet, headers, row, "sku");

            var product = id.HasValue
                ? await _context.Urunler.FirstOrDefaultAsync(x => x.Id == id.Value)
                : await _context.Urunler.FirstOrDefaultAsync(x => x.SKU == sku);

            if (product == null)
            {
                return (false, "Ürün bulunamadı. Id veya SKU doğru olmalıdır.");
            }

            var price = GetExcelDecimal(worksheet, headers, row, "satisfiyati", "satışfiyatı");
            if (!price.HasValue || price.Value <= 0)
            {
                return (false, "Satış fiyatı sıfırdan büyük olmalıdır.");
            }

            var discount = GetExcelDecimal(worksheet, headers, row, "indirimlifiyat", "indirimlifiyatı");
            if (discount.HasValue && discount.Value >= price.Value)
            {
                return (false, "İndirimli fiyat satış fiyatından küçük olmalıdır.");
            }

            product.Fiyat = price.Value;
            product.IndirimliFiyat = discount.HasValue && discount.Value > 0 ? discount.Value : null;
            await _context.SaveChangesAsync();
            return (true, string.Empty);
        }

        private async Task PopulateCategorySelectListAsync(int? selectedId = null)
        {
            ViewBag.Kategoriler = await BuildCategorySelectListAsync(selectedId);
        }

        private static string NormalizeExcelOperation(string? operation)
        {
            return operation?.Trim().ToLowerInvariant() switch
            {
                "update" => "update",
                "price" => "price",
                _ => "create"
            };
        }

        private static string GetExcelOperationLabel(string operation)
        {
            return operation switch
            {
                "update" => "Toplu Ürün Güncelleme",
                "price" => "Toplu Fiyat Güncelleme",
                _ => "Toplu Ürün Yükleme"
            };
        }

        private static string[] GetTemplateHeaders(string operation)
        {
            return operation switch
            {
                "update" => new[]
                {
                    "Id",
                    "Ürün Adı",
                    "Kategori Id",
                    "Kategori Adı",
                    "Satış Fiyatı",
                    "İndirimli Fiyat",
                    "Ana Görsel URL",
                    "Kısa Açıklama",
                    "Detaylı Açıklama",
                    "Aktif Mi"
                },
                "price" => new[]
                {
                    "Id",
                    "SKU",
                    "Satış Fiyatı",
                    "İndirimli Fiyat"
                },
                _ => new[]
                {
                    "Ürün Adı",
                    "Kategori Id",
                    "Kategori Adı",
                    "Satış Fiyatı",
                    "İndirimli Fiyat",
                    "Ana Görsel URL",
                    "Kısa Açıklama",
                    "Detaylı Açıklama",
                    "Aktif Mi",
                    "Ebat",
                    "Stok",
                    "Varyant Satis Fiyati",
                    "Varyant SKU",
                    "Varyant Gorsel URL",
                    "Cerceve Tipi",
                    "Cerceve Rengi",
                    "Malzeme Turu"
                }
            };
        }

        private static void FillTemplateSampleRow(ExcelWorksheet worksheet, string operation)
        {
            if (operation == "price")
            {
                worksheet.Cells[2, 1].Value = 1358;
                worksheet.Cells[2, 2].Value = "SKU-001";
                worksheet.Cells[2, 3].Value = 799.90;
                worksheet.Cells[2, 4].Value = 699.90;
                return;
            }

            if (operation == "update")
            {
                worksheet.Cells[2, 1].Value = 1358;
                worksheet.Cells[2, 2].Value = "Modern Soyut Kanvas Tablo";
                worksheet.Cells[2, 3].Value = 1;
                worksheet.Cells[2, 4].Value = "Soyut Kanvas";
                worksheet.Cells[2, 5].Value = 799.90;
                worksheet.Cells[2, 6].Value = 699.90;
                worksheet.Cells[2, 7].Value = "/img/products/ornek.webp";
                worksheet.Cells[2, 8].Value = "Salon ve ofis dekorasyonu için premium tablo.";
                worksheet.Cells[2, 9].Value = "Ürünün detaylı açıklaması bu alana yazılır.";
                worksheet.Cells[2, 10].Value = "Evet";
                return;
            }

            worksheet.Cells[2, 1].Value = "Modern Soyut Kanvas Tablo";
            worksheet.Cells[2, 2].Value = 1;
            worksheet.Cells[2, 3].Value = "Soyut Kanvas";
            worksheet.Cells[2, 4].Value = 799.90;
            worksheet.Cells[2, 5].Value = 699.90;
            worksheet.Cells[2, 6].Value = "/img/products/ornek.webp";
            worksheet.Cells[2, 7].Value = "Salon ve ofis dekorasyonu için premium tablo.";
            worksheet.Cells[2, 8].Value = "Ürünün detaylı açıklaması bu alana yazılır.";
            worksheet.Cells[2, 9].Value = "Evet";
            worksheet.Cells[2, 10].Value = "60cm x 40cm";
            worksheet.Cells[2, 11].Value = 100;
            worksheet.Cells[2, 12].Value = 799.90;
            worksheet.Cells[2, 13].Value = "SKU-001-60X40";
            worksheet.Cells[2, 14].Value = "/img/products/ornek-60x40.webp";
            worksheet.Cells[2, 15].Value = "Cercevesiz";
            worksheet.Cells[2, 16].Value = "";
            worksheet.Cells[2, 17].Value = "Kanvas";
        }

        private async Task AddCategoryReferenceSheetAsync(ExcelPackage package)
        {
            var categorySheet = package.Workbook.Worksheets.Add("Kategori Rehberi");
            categorySheet.Cells[1, 1].Value = "Kategori Id";
            categorySheet.Cells[1, 2].Value = "Kategori Adı";
            categorySheet.Cells[1, 3].Value = "Üst Kategori";

            var categories = await _context.Kategoriler
                .IgnoreQueryFilters()
                .Where(x => !x.SilindiMi && x.AktifMi)
                .Include(x => x.ParentKategori)
                .OrderBy(x => x.Sira)
                .ThenBy(x => x.Ad)
                .ToListAsync();

            for (var i = 0; i < categories.Count; i++)
            {
                categorySheet.Cells[i + 2, 1].Value = categories[i].Id;
                categorySheet.Cells[i + 2, 2].Value = categories[i].Ad;
                categorySheet.Cells[i + 2, 3].Value = categories[i].ParentKategori?.Ad ?? string.Empty;
            }

            StyleExcelHeader(categorySheet, 3);
            categorySheet.Cells[categorySheet.Dimension.Address].AutoFitColumns();
        }

        private static ExcelWorksheet? SelectProductImportWorksheet(ExcelPackage package)
        {
            return package.Workbook.Worksheets.FirstOrDefault(x =>
                    x.Name.Contains("ablon", StringComparison.OrdinalIgnoreCase) &&
                    x.Dimension != null)
                ?? package.Workbook.Worksheets.FirstOrDefault(x =>
                    !x.Name.Contains("Bilgilendirme", StringComparison.OrdinalIgnoreCase) &&
                    !x.Name.Contains("Rehber", StringComparison.OrdinalIgnoreCase) &&
                    x.Dimension != null)
                ?? package.Workbook.Worksheets.FirstOrDefault(x => x.Dimension != null);
        }

        private static void StyleExcelHeader(ExcelWorksheet worksheet, int columnCount)
        {
            using var range = worksheet.Cells[1, 1, 1, columnCount];
            range.Style.Font.Bold = true;
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(49, 53, 17));
            range.Style.Font.Color.SetColor(System.Drawing.Color.White);
        }

        private static void StyleTemplateSampleRow(ExcelWorksheet worksheet, int columnCount)
        {
            using var range = worksheet.Cells[2, 1, 2, columnCount];
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(255, 243, 205));
            range.Style.Font.Color.SetColor(System.Drawing.Color.FromArgb(75, 75, 75));
        }

        private static void StyleRequiredTemplateHeaders(ExcelWorksheet worksheet, string[] headers, string operation)
        {
            var required = GetRequiredTemplateColumnIndexes(operation);
            for (var i = 0; i < headers.Length; i++)
            {
                if (!required.Contains(i + 1))
                {
                    continue;
                }

                var cell = worksheet.Cells[1, i + 1];
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(183, 28, 28));
                cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
                cell.Style.Font.Bold = true;
            }
        }

        private static HashSet<int> GetRequiredTemplateColumnIndexes(string operation)
        {
            var indexes = operation switch
            {
                "update" => new[] { 1 },
                "price" => new[] { 1, 2, 3 },
                _ => new[] { 1, 2, 3, 4, 6 }
            };

            return indexes.ToHashSet();
        }

        private static void AddTemplateNotes(ExcelWorksheet worksheet, string operation, int columnCount)
        {
            if (!string.Equals(operation, "__legacy_notes__", StringComparison.Ordinal))
            {
                return;
            }

            var notesRow = worksheet.Dimension.End.Row + 3;
            var notes = operation switch
            {
                "create" => new[]
                {
                    "■ ŞABLON KULLANIM NOTLARI",
                    "• Zorunlu alanlar: Ürün Adı, Kategori Id veya Kategori Adı, Satış Fiyatı, Ana Görsel URL",
                    "• Kategori Id veya Kategori Adı'ndan en az biri doldurulmalıdır.",
                    "• Satış Fiyatı sıfırdan büyük olmalıdır. İndirimli Fiyat, Satış Fiyatı'ndan küçük olmalıdır.",
                    "• Aktif Mi: 'Evet' veya 'Hayır' olarak yazılmalıdır. Boş bırakılırsa 'Evet' kabul edilir.",
                    "• Ebat: Örn: '50x70cm', '40x30cm' — Boş bırakılırsa otomatik 'Standart' olarak kaydedilir.",
                    "• Stok: Boş bırakılırsa 100 adet olarak kabul edilir.",
                    "• Cerceve Tipi: 'Cercevesiz', 'Ahşap Çerçeve', 'Metal Çerçeve' — Boş bırakılırsa otomatik 'Cercevesiz' atanır.",
                    "• Cerceve Rengi: Sadece çerçevesi olan ürünlerde 'Siyah', 'Beyaz', 'Gold', 'Gümüş', 'Meşe', 'Ceviz' olarak yazılabilir.",
                    "• Malzeme Turu: Çerçeve türüne göre opsiyoneldir.",
                    "• Varyant Satış Fiyatı boş bırakılırsa ana ürün fiyatı kullanılır.",
                    "• Varyant Görsel URL boş bırakılırsa Ana Görsel URL kullanılır.",
                    "• Kategoriler için 'Kategori Rehberi' sayfasına bakınız."
                },
                "update" => new[]
                {
                    "■ ŞABLON KULLANIM NOTLARI",
                    "• Zorunlu alan: Id (ürünün veritabanı numarası)",
                    "• Id ile bulunan ürün güncellenir. Id boş veya geçersizse satır atlanır.",
                    "• Boş bırakılan alanlar güncellenmez (değişiklik yapılmaz).",
                    "• Aktif Mi: 'Evet' veya 'Hayır' olarak yazılmalıdır.",
                    "• Kategori Id veya Kategori Adı'ndan en az biri doldurulmalıdır.",
                    "• Kategoriler için 'Kategori Rehberi' sayfasına bakınız."
                },
                "price" => new[]
                {
                    "■ ŞABLON KULLANIM NOTLARI",
                    "• Zorunlu alanlar: Id veya SKU + Satış Fiyatı",
                    "• Id veya SKU ile ürün bulunur. Her ikisi de doluysa Id öncelikli olarak kullanılır.",
                    "• İndirimli Fiyat boş bırakılırsa indirim kaldırılır.",
                    "• İndirimli Fiyat, Satış Fiyatı'ndan büyük veya eşit olursa indirim uygulanmaz."
                },
                _ => Array.Empty<string>()
            };

            worksheet.Cells[notesRow, 1].Value = notes[0];
            for (var i = 1; i < notes.Length; i++)
            {
                worksheet.Cells[notesRow + i, 1].Value = notes[i];
            }

            using var range = worksheet.Cells[notesRow, 1, notesRow + notes.Length - 1, 1];
            range.Style.Font.Size = 10;
            range.Style.Font.Color.SetColor(System.Drawing.Color.FromArgb(75, 75, 75));
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(245, 242, 235));
            range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Top.Color.SetColor(System.Drawing.Color.FromArgb(200, 195, 180));
            range.Style.Border.Bottom.Color.SetColor(System.Drawing.Color.FromArgb(200, 195, 180));
            range.Style.Border.Left.Color.SetColor(System.Drawing.Color.FromArgb(200, 195, 180));
            range.Style.Border.Right.Color.SetColor(System.Drawing.Color.FromArgb(200, 195, 180));
        }

        private static Dictionary<string, int> BuildHeaderMap(ExcelWorksheet worksheet)
        {
            var result = new Dictionary<string, int>();
            for (var col = 1; col <= worksheet.Dimension.End.Column; col++)
            {
                var header = NormalizeExcelKey(worksheet.Cells[1, col].Text);
                if (!string.IsNullOrWhiteSpace(header) && !result.ContainsKey(header))
                {
                    result[header] = col;
                }
            }

            return result;
        }

        private static bool IsExcelRowEmpty(ExcelWorksheet worksheet, int row)
        {
            for (var col = 1; col <= worksheet.Dimension.End.Column; col++)
            {
                if (!string.IsNullOrWhiteSpace(worksheet.Cells[row, col].Text))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsTemplateHelperRow(ExcelWorksheet worksheet, int row, string operation)
        {
            var firstCell = worksheet.Cells[row, 1].Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(firstCell))
            {
                return false;
            }

            if (firstCell.Contains("SABLON", StringComparison.OrdinalIgnoreCase) ||
                firstCell.Contains("ÅABLON", StringComparison.OrdinalIgnoreCase) ||
                firstCell.StartsWith("â– ", StringComparison.OrdinalIgnoreCase) ||
                firstCell.StartsWith("â€¢", StringComparison.OrdinalIgnoreCase) ||
                firstCell.StartsWith("-", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var normalized = NormalizeExcelKey(firstCell);
            return normalized.Contains("sablon") ||
                normalized.Contains("zorunlualan") ||
                normalized.Contains("kategoriid") ||
                normalized.Contains("satisfiyati") ||
                normalized.Contains("indirimlifiyat") ||
                normalized.Contains("aktifmi") ||
                normalized.Contains("varyant") ||
                normalized.Contains("kategorilericin") ||
                normalized.Contains("bosbirakilirsa");
        }

        private static bool IsTemplateSampleRow(
            ExcelWorksheet worksheet,
            Dictionary<string, int> headers,
            int row,
            string operation)
        {
            if (row != 2)
            {
                return false;
            }

            var title = NormalizeExcelKey(GetExcelString(worksheet, headers, row, "urunadi"));
            var imageUrl = NormalizeExcelKey(GetExcelString(worksheet, headers, row, "anagorselurl"));
            var sku = NormalizeExcelKey(GetExcelString(worksheet, headers, row, "sku", "varyantsku"));

            return operation switch
            {
                "price" => sku == "sku001",
                "update" => title == "modernsoyutkanvastablo" && imageUrl.Contains("ornekwebp"),
                _ => title == "modernsoyutkanvastablo" && imageUrl.Contains("ornekwebp")
            };
        }

        private static string NormalizeExcelKey(string? value)
        {
            var text = (value ?? string.Empty).Trim().ToLower(new CultureInfo("tr-TR"));
            var map = new Dictionary<char, char>
            {
                ['ç'] = 'c',
                ['ğ'] = 'g',
                ['ı'] = 'i',
                ['i'] = 'i',
                ['ö'] = 'o',
                ['ş'] = 's',
                ['ü'] = 'u'
            };

            var normalized = new string(text.Select(ch => map.TryGetValue(ch, out var replacement) ? replacement : ch).ToArray());
            return new string(normalized.Where(char.IsLetterOrDigit).ToArray());
        }

        private static string GetExcelString(ExcelWorksheet worksheet, Dictionary<string, int> headers, int row, params string[] keys)
        {
            return TryGetExcelCell(worksheet, headers, row, out var value, keys)
                ? value.Trim()
                : string.Empty;
        }

        private static bool TryGetExcelCell(ExcelWorksheet worksheet, Dictionary<string, int> headers, int row, out string value, params string[] keys)
        {
            foreach (var key in keys.Select(NormalizeExcelKey))
            {
                if (headers.TryGetValue(key, out var column))
                {
                    value = worksheet.Cells[row, column].Text ?? string.Empty;
                    return true;
                }
            }

            value = string.Empty;
            return false;
        }

        private static int? GetExcelInt(ExcelWorksheet worksheet, Dictionary<string, int> headers, int row, params string[] keys)
        {
            var value = GetExcelString(worksheet, headers, row, keys);
            return int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ||
                   int.TryParse(value, NumberStyles.Any, new CultureInfo("tr-TR"), out parsed)
                ? parsed
                : null;
        }

        private static decimal? GetExcelDecimal(ExcelWorksheet worksheet, Dictionary<string, int> headers, int row, params string[] keys)
        {
            var value = GetExcelString(worksheet, headers, row, keys);
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            value = value.Replace("TL", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            return decimal.TryParse(value, NumberStyles.Any, new CultureInfo("tr-TR"), out var parsed) ||
                   decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : null;
        }

        private static bool GetExcelBool(ExcelWorksheet worksheet, Dictionary<string, int> headers, int row, bool defaultValue, params string[] keys)
        {
            return TryGetExcelCell(worksheet, headers, row, out var value, keys)
                ? ParseExcelBool(value, defaultValue)
                : defaultValue;
        }

        private static bool ParseExcelBool(string value, bool defaultValue)
        {
            var normalized = NormalizeExcelKey(value);
            return normalized switch
            {
                "evet" or "true" or "1" or "aktif" or "yayinda" => true,
                "hayir" or "false" or "0" or "pasif" => false,
                _ => defaultValue
            };
        }

        private static int? ResolveExcelCategoryId(ExcelWorksheet worksheet, Dictionary<string, int> headers, List<Kategori> categories, int row)
        {
            var categoryId = GetExcelInt(worksheet, headers, row, "kategoriid");
            if (categoryId.HasValue && categories.Any(x => x.Id == categoryId.Value))
            {
                return categoryId;
            }

            var categoryName = GetExcelString(worksheet, headers, row, "kategoriadi", "kategori");
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                return null;
            }

            var normalizedName = NormalizeExcelKey(categoryName);
            return categories.FirstOrDefault(x => NormalizeExcelKey(x.Ad) == normalizedName)?.Id;
        }

        private static void SetProductStringIfPresent(
            Urun product,
            ExcelWorksheet worksheet,
            Dictionary<string, int> headers,
            int row,
            string key,
            Action<Urun, string> setter,
            params string[] aliases)
        {
            var keys = new[] { key }.Concat(aliases).ToArray();
            if (!TryGetExcelCell(worksheet, headers, row, out var value, keys) || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            setter(product, value.Trim());
        }

        private string GetProductExcelImportFolder()
        {
            return Path.Combine(_webHost.ContentRootPath, "App_Data", "urun-excel-imports");
        }

        private string GetProductExcelImportHistoryPath()
        {
            return Path.Combine(GetProductExcelImportFolder(), "history.json");
        }

        private async Task<List<ProductExcelImportReport>> GetProductExcelImportHistoryAsync()
        {
            var path = GetProductExcelImportHistoryPath();
            if (!System.IO.File.Exists(path))
            {
                return new List<ProductExcelImportReport>();
            }

            var json = await System.IO.File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<List<ProductExcelImportReport>>(json) ?? new List<ProductExcelImportReport>();
        }

        private async Task SaveProductExcelImportHistoryAsync(List<ProductExcelImportReport> history)
        {
            var folder = GetProductExcelImportFolder();
            Directory.CreateDirectory(folder);
            var path = GetProductExcelImportHistoryPath();
            var keptHistory = history
                .OrderByDescending(x => x.UploadedAt)
                .Take(50)
                .ToList();

            var json = JsonSerializer.Serialize(keptHistory, new JsonSerializerOptions { WriteIndented = true });
            await System.IO.File.WriteAllTextAsync(path, json);
        }

        public sealed class ProductExcelImportReport
        {
            public string Id { get; set; } = string.Empty;
            public string OriginalFileName { get; set; } = string.Empty;
            public string StoredFileName { get; set; } = string.Empty;
            public string OperationType { get; set; } = string.Empty;
            public string OperationLabel { get; set; } = string.Empty;
            public DateTime UploadedAt { get; set; }
            public int TotalRows { get; set; }
            public int SuccessCount { get; set; }
            public int ErrorCount => Errors.Count;
            public string Status { get; set; } = string.Empty;
            public List<string> Errors { get; set; } = new();
        }

        private async Task PopulateProductMetadataAsync(string? selectedProductType = null)
        {
            ViewBag.UrunTipleri = UrunOzellikCatalog.GetProductTypeSelectList(selectedProductType);
            ViewBag.OzellikTanimlari = await _context.UrunOzellikTanimlari
                .AsNoTracking()
                .Where(x => x.AktifMi)
                .OrderBy(x => x.Sira)
                .ThenBy(x => x.Ad)
                .ToListAsync();
        }

        private void PopulateMediaMetadata(Urun urun)
        {
            ViewBag.MedyaTipleri = UrunMedyaCatalog.GetTypeSelectList();
            ViewBag.MedyaAlanlari = UrunMedyaCatalog.GetAreaSelectList();
            ViewBag.MedyaVaryantlari = (urun.UrunSecenek ?? new List<UrunSecenek>())
                .Where(x => !x.SilindiMi)
                .OrderByDescending(x => x.VarsayilanMi)
                .ThenBy(x => x.Sira)
                .Select(x => new SelectListItem
                {
                    Value = x.Id.ToString(),
                    Text = string.IsNullOrWhiteSpace(x.VaryantBasligi) ? $"Varyasyon #{x.Id}" : x.VaryantBasligi
                })
                .ToList();
        }

        private async Task<List<SelectListItem>> BuildCategorySelectListAsync(int? selectedId)
        {
            var kategoriler = await _context.Kategoriler
                .Where(x => x.AktifMi)
                .OrderBy(x => x.Sira)
                .ThenBy(x => x.Ad)
                .ToListAsync();

            ViewBag.CanvasOptionCategoryIds = GetCanvasOptionCategoryIds(kategoriler);
            var items = new List<SelectListItem>();
            foreach (var (category, depth) in CategoryTreeHelper.FlattenHierarchy(kategoriler))
            {
                items.Add(new SelectListItem
                {
                    Value = category.Id.ToString(),
                    Text = $"{new string('-', depth * 2)}{(depth > 0 ? " " : string.Empty)}{category.Ad}",
                    Selected = selectedId == category.Id
                });
            }

            return items;
        }

        private async Task<bool> SupportsCanvasOptionsAsync(int categoryId)
        {
            var categories = await _context.Kategoriler
                .IgnoreQueryFilters()
                .ToListAsync();

            return SupportsCanvasOptions(categories, categoryId);
        }

        private static bool SupportsCanvasOptions(IEnumerable<Kategori> categories, int categoryId)
        {
            return GetCanvasOptionCategoryIds(categories).Contains(categoryId);
        }

        private static HashSet<int> GetCanvasOptionCategoryIds(IEnumerable<Kategori> categories)
        {
            var categoryList = categories.ToList();
            var canvasRoots = categoryList
                .Where(x => NormalizeExcelKey(x.Ad).Contains("kanvastablo"))
                .Select(x => x.Id)
                .ToList();

            var result = new HashSet<int>();
            foreach (var rootId in canvasRoots)
            {
                foreach (var id in CategoryTreeHelper.GetDescendantIds(categoryList, rootId))
                {
                    result.Add(id);
                }
            }

            return result;
        }

        private void RemoveOptionalProductModelStateErrors()
        {
            var optionalProductFields = new HashSet<string>
            {
                nameof(Urun.KisaAd),
                nameof(Urun.SKU),
                nameof(Urun.Barkod),
                nameof(Urun.Marka),
                nameof(Urun.UrunTipi),
                nameof(Urun.Etiketler),
                nameof(Urun.KisaAciklama),
                nameof(Urun.Aciklama),
                nameof(Urun.TeknikOzellikler),
                nameof(Urun.MalzemeBilgisi),
                nameof(Urun.BakimTalimati),
                nameof(Urun.PaketlemeBilgisi),
                nameof(Urun.AnaGorselUrl),
                nameof(Urun.StokDurumu),
                nameof(Urun.UrlYolu),
                nameof(Urun.SeoTitle),
                nameof(Urun.SeoDescription),
                nameof(Urun.SeoKeywords),
                nameof(Urun.AktifMi)
            };

            foreach (var key in optionalProductFields)
            {
                ModelState.Remove(key);
            }

            var optionalVariantFields = new[]
            {
                nameof(UrunSecenek.Olcu),
                nameof(UrunSecenek.CerceveTipi),
                nameof(UrunSecenek.CerceveRengi),
                nameof(UrunSecenek.CerceveKalinligi),
                nameof(UrunSecenek.MalzemeTuru),
                nameof(UrunSecenek.Yon),
                nameof(UrunSecenek.VaryantSku),
                nameof(UrunSecenek.KisilestirmeMetni),
                nameof(UrunSecenek.OzelTasarimNotu),
                nameof(UrunSecenek.GorselUrl),
                nameof(UrunSecenek.Urun),
                nameof(UrunSecenek.AktifMi),
                nameof(UrunSecenek.VarsayilanMi),
                nameof(UrunSecenek.TukeninceGizle),
                nameof(UrunSecenek.OnSipariseAcikMi)
            };

            foreach (var key in ModelState.Keys.ToList())
            {
                if (key.StartsWith("UrunSecenek[", StringComparison.Ordinal) &&
                    optionalVariantFields.Any(field => key.EndsWith("." + field, StringComparison.Ordinal)))
                {
                    ModelState.Remove(key);
                }
            }
        }

        private static void NormalizeOptionalProductFieldsForValidation(Urun urun)
        {
            urun.KisaAd ??= string.Empty;
            urun.SKU ??= string.Empty;
            urun.Barkod ??= string.Empty;
            urun.Marka ??= string.Empty;
            urun.UrunTipi = UrunOzellikCatalog.NormalizeProductType(urun.UrunTipi);
            urun.Etiketler ??= string.Empty;
            urun.KisaAciklama ??= string.Empty;
            urun.Aciklama ??= string.Empty;
            urun.TeknikOzellikler ??= string.Empty;
            urun.MalzemeBilgisi ??= string.Empty;
            urun.BakimTalimati ??= string.Empty;
            urun.PaketlemeBilgisi ??= string.Empty;
            urun.AnaGorselUrl ??= string.Empty;
            urun.StokDurumu = NormalizeStockStatus(urun.StokDurumu);
            urun.UrlYolu ??= string.Empty;
            urun.SeoTitle ??= string.Empty;
            urun.SeoDescription ??= string.Empty;
            urun.SeoKeywords ??= string.Empty;

            foreach (var variant in urun.UrunSecenek ?? Enumerable.Empty<UrunSecenek>())
            {
                variant.Olcu ??= string.Empty;
                variant.CerceveTipi ??= string.Empty;
                variant.CerceveRengi ??= string.Empty;
                variant.CerceveKalinligi ??= string.Empty;
                variant.MalzemeTuru ??= string.Empty;
                variant.Yon ??= string.Empty;
                variant.VaryantSku ??= string.Empty;
                variant.KisilestirmeMetni ??= string.Empty;
                variant.OzelTasarimNotu ??= string.Empty;
                variant.GorselUrl ??= string.Empty;
            }
        }

        private async Task<bool> ValidateProductAsync(Urun urun, IFormFile? imageFile, int? currentId = null)
        {
            if (string.IsNullOrWhiteSpace(urun.Baslik))
            {
                ModelState.AddModelError(nameof(Urun.Baslik), "Ürün adı zorunludur.");
            }

            if (urun.KategoriId <= 0 || !await _context.Kategoriler.AnyAsync(x => x.Id == urun.KategoriId))
            {
                ModelState.AddModelError(nameof(Urun.KategoriId), "Geçerli bir kategori seçmelisiniz.");
            }

            if (urun.Fiyat <= 0)
            {
                ModelState.AddModelError(nameof(Urun.Fiyat), "Satış fiyatı sıfırdan büyük olmalıdır.");
            }

            if (urun.IndirimliFiyat.HasValue &&
                (urun.IndirimliFiyat.Value <= 0 || urun.IndirimliFiyat.Value >= urun.Fiyat))
            {
                ModelState.AddModelError(nameof(Urun.IndirimliFiyat), "İndirimli fiyat ana fiyattan küçük olmalıdır.");
            }

            if (urun.MinSiparisAdedi < 1)
            {
                ModelState.AddModelError(nameof(Urun.MinSiparisAdedi), "Minimum sipariş adedi en az 1 olmalıdır.");
            }

            if (urun.MaxSiparisAdedi.HasValue && urun.MaxSiparisAdedi.Value < urun.MinSiparisAdedi)
            {
                ModelState.AddModelError(nameof(Urun.MaxSiparisAdedi), "Maksimum sipariş adedi minimum sipariş adedinden küçük olamaz.");
            }

            if (currentId == null && imageFile == null && string.IsNullOrWhiteSpace(urun.AnaGorselUrl))
            {
                ModelState.AddModelError(nameof(Urun.AnaGorselUrl), "Ana görsel yükleyin veya görsel URL girin.");
            }

            ValidateVariants(urun.UrunSecenek);

            return ModelState.IsValid;
        }

        private void NormalizeProductInput(Urun urun)
        {
            urun.Baslik = TurkishTextRepairHelper.RepairKnownBrokenTurkish(urun.Baslik);
            urun.KisaAd = string.IsNullOrWhiteSpace(urun.KisaAd) ? urun.Baslik : TurkishTextRepairHelper.RepairKnownBrokenTurkish(urun.KisaAd);
            urun.SKU = urun.SKU?.Trim() ?? string.Empty;
            urun.Barkod = urun.Barkod?.Trim() ?? string.Empty;
            urun.Marka = TurkishTextRepairHelper.RepairKnownBrokenTurkish(urun.Marka);
            urun.UrunTipi = UrunOzellikCatalog.NormalizeProductType(urun.UrunTipi);
            urun.Etiketler = TurkishTextRepairHelper.RepairKnownBrokenTurkish(urun.Etiketler);
            urun.KisaAciklama = TurkishTextRepairHelper.RepairKnownBrokenTurkish(urun.KisaAciklama);
            urun.Aciklama = TurkishTextRepairHelper.RepairKnownBrokenTurkish(urun.Aciklama);
            urun.TeknikOzellikler = TurkishTextRepairHelper.RepairKnownBrokenTurkish(urun.TeknikOzellikler);
            urun.MalzemeBilgisi = TurkishTextRepairHelper.RepairKnownBrokenTurkish(urun.MalzemeBilgisi);
            urun.BakimTalimati = TurkishTextRepairHelper.RepairKnownBrokenTurkish(urun.BakimTalimati);
            urun.PaketlemeBilgisi = TurkishTextRepairHelper.RepairKnownBrokenTurkish(urun.PaketlemeBilgisi);
            urun.StokDurumu = NormalizeStockStatus(urun.StokDurumu);
            urun.KdvOrani = urun.KdvOrani < 0 ? 0 : urun.KdvOrani;
            urun.MinSiparisAdedi = urun.MinSiparisAdedi < 1 ? 1 : urun.MinSiparisAdedi;
            urun.MaxSiparisAdedi = urun.MaxSiparisAdedi.HasValue && urun.MaxSiparisAdedi.Value < 1 ? null : urun.MaxSiparisAdedi;
            urun.IndirimliFiyat = urun.IndirimliFiyat.HasValue && urun.IndirimliFiyat.Value <= 0
                ? null
                : urun.IndirimliFiyat;
            urun.SeoTitle = TurkishTextRepairHelper.RepairKnownBrokenTurkish(urun.SeoTitle);
            urun.SeoDescription = TurkishTextRepairHelper.RepairKnownBrokenTurkish(urun.SeoDescription);
            urun.SeoKeywords = TurkishTextRepairHelper.RepairKnownBrokenTurkish(urun.SeoKeywords);
        }

        private static void RepairProductTextForDisplay(Urun urun)
        {
            urun.Baslik = TurkishTextRepairHelper.RepairKnownBrokenTurkish(urun.Baslik);
            urun.KisaAd = TurkishTextRepairHelper.RepairKnownBrokenTurkish(urun.KisaAd);
            urun.Marka = TurkishTextRepairHelper.RepairKnownBrokenTurkish(urun.Marka);
            urun.Etiketler = TurkishTextRepairHelper.RepairKnownBrokenTurkish(urun.Etiketler);
            urun.KisaAciklama = TurkishTextRepairHelper.RepairKnownBrokenTurkish(urun.KisaAciklama);
            urun.Aciklama = TurkishTextRepairHelper.RepairKnownBrokenTurkish(urun.Aciklama);
            urun.TeknikOzellikler = TurkishTextRepairHelper.RepairKnownBrokenTurkish(urun.TeknikOzellikler);
            urun.MalzemeBilgisi = TurkishTextRepairHelper.RepairKnownBrokenTurkish(urun.MalzemeBilgisi);
            urun.BakimTalimati = TurkishTextRepairHelper.RepairKnownBrokenTurkish(urun.BakimTalimati);
            urun.PaketlemeBilgisi = TurkishTextRepairHelper.RepairKnownBrokenTurkish(urun.PaketlemeBilgisi);

            foreach (var variant in urun.UrunSecenek ?? Enumerable.Empty<UrunSecenek>())
            {
                variant.Olcu = TurkishTextRepairHelper.RepairKnownBrokenTurkish(variant.Olcu);
                variant.CerceveTipi = TurkishTextRepairHelper.RepairKnownBrokenTurkish(variant.CerceveTipi);
                variant.CerceveRengi = TurkishTextRepairHelper.RepairKnownBrokenTurkish(variant.CerceveRengi);
                variant.CerceveKalinligi = TurkishTextRepairHelper.RepairKnownBrokenTurkish(variant.CerceveKalinligi);
                variant.MalzemeTuru = TurkishTextRepairHelper.RepairKnownBrokenTurkish(variant.MalzemeTuru);
                variant.Yon = TurkishTextRepairHelper.RepairKnownBrokenTurkish(variant.Yon);
                variant.KisilestirmeMetni = TurkishTextRepairHelper.RepairKnownBrokenTurkish(variant.KisilestirmeMetni);
                variant.OzelTasarimNotu = TurkishTextRepairHelper.RepairKnownBrokenTurkish(variant.OzelTasarimNotu);
            }

            foreach (var media in urun.UrunResimleri ?? Enumerable.Empty<UrunResim>())
            {
                media.Baslik = TurkishTextRepairHelper.RepairKnownBrokenTurkish(media.Baslik);
                media.AltMetin = TurkishTextRepairHelper.RepairKnownBrokenTurkish(media.AltMetin);
                media.Etiketler = TurkishTextRepairHelper.RepairKnownBrokenTurkish(media.Etiketler);
            }

            foreach (var feature in urun.UrunOzellikleri ?? Enumerable.Empty<UrunOzellikDegeri>())
            {
                feature.Deger = TurkishTextRepairHelper.RepairKnownBrokenTurkish(feature.Deger);
            }
        }

        
        private async Task<int> NormalizeProductOrderAsync(int currentOrder)
        {
            if (currentOrder > 0)
            {
                return currentOrder;
            }

            var lastOrder = await _context.Urunler
                .OrderByDescending(x => x.Sira)
                .Select(x => (int?)x.Sira)
                .FirstOrDefaultAsync();

            return (lastOrder ?? 0) + 1;
        }

        private static string NormalizeStockStatus(string? stockStatus)
        {
            var value = stockStatus?.Trim().ToLowerInvariant();
            return value switch
            {
                "tukendi" => "Tukendi",
                "onsiparis" => "OnSiparis",
                "on siparis" => "OnSiparis",
                _ => "Stokta"
            };
        }

        private async Task<string> GenerateUniqueProductSlugAsync(string? requestedSlug, string title, int? excludedId)
        {
            var baseSlug = SlugHelper.GenerateSlug(string.IsNullOrWhiteSpace(requestedSlug) ? title : requestedSlug);
            var existingSlugs = await _context.Urunler
                .Where(x => x.Id != excludedId && x.Slug != null)
                .Select(x => x.Slug!)
                .ToListAsync();

            return SlugHelper.EnsureUnique(baseSlug, existingSlugs);
        }

        private async Task<string> SaveImageAsync(IFormFile imageFile, string title, string suffix, int maxWidth = 2000, int maxHeight = 2000)
        {
            var extension = Path.GetExtension(imageFile.FileName);
            if (!AllowedImageExtensions.Contains(extension))
            {
                throw new InvalidOperationException("Desteklenmeyen gorsel formati.");
            }

            if (imageFile.Length <= 0 || imageFile.Length > MaxImageFileBytes)
            {
                throw new InvalidOperationException("Gorsel dosyasi 10 MB'dan buyuk olamaz.");
            }

            var folder = Path.Combine(_webHost.WebRootPath, "img", "products");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var slug = SlugHelper.GenerateSlug(title);
            var token = Guid.NewGuid().ToString("N")[..8];
            var fileName = $"{slug}-{suffix}-{token}.webp";
            var fullPath = Path.Combine(folder, fileName);

            await using var imageStream = imageFile.OpenReadStream();
            using var image = await Image.LoadAsync(imageStream);
            image.Mutate(x =>
            {
                x.AutoOrient();
                x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(maxWidth, maxHeight)
                });
            });

            await image.SaveAsWebpAsync(fullPath, new WebpEncoder
            {
                Quality = 82,
                FileFormat = WebpFileFormatType.Lossy
            });

            return "/img/products/" + fileName;
        }

        private async Task<string> SaveVideoAsync(IFormFile videoFile, string title, string suffix)
        {
            var extension = Path.GetExtension(videoFile.FileName);
            if (!AllowedVideoExtensions.Contains(extension))
            {
                throw new InvalidOperationException("Desteklenmeyen video formati.");
            }

            if (videoFile.Length <= 0 || videoFile.Length > MaxVideoFileBytes)
            {
                throw new InvalidOperationException("Video dosyasi 150 MB'dan buyuk olamaz.");
            }

            var folder = Path.Combine(_webHost.WebRootPath, "media", "products", "videos");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var slug = SlugHelper.GenerateSlug(title);
            var token = Guid.NewGuid().ToString("N")[..8];
            var fileName = $"{slug}-{suffix}-{token}{extension.ToLowerInvariant()}";
            var fullPath = Path.Combine(folder, fileName);

            await using var stream = new FileStream(fullPath, FileMode.Create);
            await videoFile.CopyToAsync(stream);

            return "/media/products/videos/" + fileName;
        }

        private async Task SaveGalleryImagesAsync(Urun urun, List<IFormFile>? galeriDosyalari)
        {
            if (galeriDosyalari == null || galeriDosyalari.Count == 0)
            {
                return;
            }

            var nextOrder = await GetNextMediaOrderAsync(urun.Id);

            foreach (var file in galeriDosyalari.Where(x => x.Length > 0))
            {
                var imagePath = await SaveImageAsync(file, urun.Baslik, "galeri");
                urun.UrunResimleri.Add(new UrunResim
                {
                    UrunId = urun.Id,
                    ResimYolu = imagePath,
                    ThumbnailYolu = imagePath,
                    Baslik = urun.Baslik,
                    AltMetin = urun.Baslik,
                    MedyaTipi = UrunMedyaCatalog.Gorsel,
                    MedyaAlani = UrunMedyaCatalog.Galeri,
                    Sira = nextOrder++,
                    VarsayilanMi = !urun.UrunResimleri.Any(x => !x.SilindiMi && x.VarsayilanMi)
                });
            }
        }

        private async Task<int> GetNextMediaOrderAsync(int urunId)
        {
            var lastOrder = await _context.UrunResimleri
                .Where(x => x.UrunId == urunId)
                .Select(x => (int?)x.Sira)
                .OrderByDescending(x => x)
                .FirstOrDefaultAsync();

            return (lastOrder ?? 0) + 1;
        }

        private async Task<string?> ApplyProductMediaInputAsync(
            Urun urun,
            UrunResim medya,
            ProductMediaInputModelBase model,
            IFormFile? medyaDosyasi,
            IFormFile? onizlemeDosyasi,
            IFormFile? mobilGorselDosyasi)
        {
            medya.Baslik = TurkishTextRepairHelper.RepairKnownBrokenTurkish(model.Baslik);
            medya.AltMetin = TurkishTextRepairHelper.RepairKnownBrokenTurkish(model.AltMetin);
            medya.MedyaTipi = string.Equals(model.MedyaTipi, UrunMedyaCatalog.Video, StringComparison.OrdinalIgnoreCase)
                ? UrunMedyaCatalog.Video
                : UrunMedyaCatalog.Gorsel;
            medya.MedyaAlani = string.IsNullOrWhiteSpace(model.MedyaAlani) ? UrunMedyaCatalog.Galeri : model.MedyaAlani.Trim();
            medya.VideoUrl = model.VideoUrl?.Trim() ?? string.Empty;
            medya.Etiketler = TurkishTextRepairHelper.RepairKnownBrokenTurkish(model.Etiketler);
            medya.Sira = model.Sira > 0 ? model.Sira : (medya.Sira > 0 ? medya.Sira : await GetNextMediaOrderAsync(urun.Id));
            medya.VarsayilanMi = model.VarsayilanMi;
            medya.UrunSecenekId = model.UrunSecenekId;

            if (medya.UrunSecenekId.HasValue && !urun.UrunSecenek.Any(x => x.Id == medya.UrunSecenekId.Value && !x.SilindiMi))
            {
                return "Secilen varyasyona ait gecersiz medya baglantisi.";
            }

            try
            {
                if (medya.VideoMu)
                {
                    if (medyaDosyasi != null && medyaDosyasi.Length > 0)
                    {
                        medya.ResimYolu = await SaveVideoAsync(medyaDosyasi, urun.Baslik, "video");
                    }

                    if (string.IsNullOrWhiteSpace(medya.VideoUrl) && string.IsNullOrWhiteSpace(medya.ResimYolu))
                    {
                        return "Video medyasi icin URL veya dosya yuklemelisiniz.";
                    }

                    if (onizlemeDosyasi != null && onizlemeDosyasi.Length > 0)
                    {
                        medya.ThumbnailYolu = await SaveImageAsync(onizlemeDosyasi, urun.Baslik, "video-thumb", 1400, 1400);
                    }
                }
                else
                {
                    if (medyaDosyasi != null && medyaDosyasi.Length > 0)
                    {
                        medya.ResimYolu = await SaveImageAsync(medyaDosyasi, urun.Baslik, "media", 2000, 2000);
                    }

                    if (string.IsNullOrWhiteSpace(medya.ResimYolu))
                    {
                        return "Gorsel medyasi icin dosya yuklemelisiniz.";
                    }

                    medya.VideoUrl = string.Empty;

                    if (onizlemeDosyasi != null && onizlemeDosyasi.Length > 0)
                    {
                        medya.ThumbnailYolu = await SaveImageAsync(onizlemeDosyasi, urun.Baslik, "thumb", 1200, 1200);
                    }
                }

                if (mobilGorselDosyasi != null && mobilGorselDosyasi.Length > 0)
                {
                    medya.MobilResimYolu = await SaveImageAsync(mobilGorselDosyasi, urun.Baslik, "mobile", 900, 1200);
                }
            }
            catch (InvalidOperationException ex)
            {
                return ex.Message;
            }

            if (string.IsNullOrWhiteSpace(medya.Baslik))
            {
                medya.Baslik = UrunMedyaCatalog.GetAreaLabel(medya.MedyaAlani);
            }

            if (string.IsNullOrWhiteSpace(medya.AltMetin))
            {
                medya.AltMetin = $"{urun.Baslik} - {medya.Baslik}";
            }

            if (string.IsNullOrWhiteSpace(medya.ThumbnailYolu))
            {
                medya.ThumbnailYolu = medya.VideoMu
                    ? (string.IsNullOrWhiteSpace(medya.MobilResimYolu) ? string.Empty : medya.MobilResimYolu)
                    : medya.ResimYolu;
            }

            return null;
        }

        private void DeletePhysicalProductFile(string? relativeUrl)
        {
            if (string.IsNullOrWhiteSpace(relativeUrl))
            {
                return;
            }

            var trimmed = relativeUrl.Trim();
            if (!trimmed.StartsWith("/img/products/", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("/media/products/videos/", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                var relativePath = trimmed.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var fullPath = Path.GetFullPath(Path.Combine(_webHost.WebRootPath, relativePath));
                var webRootFullPath = Path.GetFullPath(_webHost.WebRootPath);
                if (fullPath.StartsWith(webRootFullPath, StringComparison.Ordinal) && System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Delete(fullPath);
                }
            }
            catch
            {
                // Fiziksel dosya silme hatası ana işlemi kesmemeli.
            }
        }

        private Task EnsureDefaultProductMediaAsync(Urun urun, UrunResim? requestedDefault = null, bool overwriteMainImage = true)
        {
            var activeMedia = urun.UrunResimleri
                .Where(x => !x.SilindiMi)
                .OrderByDescending(x => x.VarsayilanMi)
                .ThenBy(x => x.Sira)
                .ThenBy(x => x.Id)
                .ToList();

            if (activeMedia.Count == 0)
            {
                return Task.CompletedTask;
            }

            var defaultMedia = requestedDefault
                ?? activeMedia.FirstOrDefault(x => x.VarsayilanMi)
                ?? activeMedia.FirstOrDefault(x => !x.VideoMu && !string.IsNullOrWhiteSpace(x.ResimYolu))
                ?? activeMedia.First();

            foreach (var item in activeMedia)
            {
                item.VarsayilanMi = item == defaultMedia;
            }

            var previewImage = !string.IsNullOrWhiteSpace(defaultMedia.ResimYolu) && !defaultMedia.VideoMu
                ? defaultMedia.ResimYolu
                : (!string.IsNullOrWhiteSpace(defaultMedia.ThumbnailYolu)
                    ? defaultMedia.ThumbnailYolu
                    : defaultMedia.MobilResimYolu);

            if (!string.IsNullOrWhiteSpace(previewImage) && (overwriteMainImage || requestedDefault != null || string.IsNullOrWhiteSpace(urun.AnaGorselUrl)))
            {
                urun.AnaGorselUrl = previewImage;
            }

            return Task.CompletedTask;
        }

        private Task SyncVariantsAsync(Urun urun, ICollection<UrunSecenek>? incomingVariants)
        {
            incomingVariants ??= new List<UrunSecenek>();
            var validIncoming = incomingVariants
                .Where(IsMeaningfulVariant)
                .ToList();

            for (var i = 0; i < validIncoming.Count; i++)
            {
                NormalizeVariantInput(validIncoming[i], i);
                validIncoming[i].SatisFiyati = ResolveVariantSalePrice(urun, validIncoming[i]);
            }

            var defaultVariant = validIncoming
                .FirstOrDefault(x => x.VarsayilanMi && x.AktifMi)
                ?? validIncoming.FirstOrDefault(x => x.AktifMi && x.StokAdedi > 0)
                ?? validIncoming.FirstOrDefault(x => x.AktifMi)
                ?? validIncoming.FirstOrDefault();

            foreach (var variant in validIncoming)
            {
                variant.VarsayilanMi = defaultVariant != null && ReferenceEquals(variant, defaultVariant);
            }

            var incomingIds = validIncoming
                .Where(x => x.Id > 0)
                .Select(x => x.Id)
                .ToHashSet();

            var toRemove = urun.UrunSecenek
                .Where(x => x.Id > 0 && !incomingIds.Contains(x.Id))
                .ToList();

            if (toRemove.Count > 0)
            {
                _context.UrunSecenekleri.RemoveRange(toRemove);
            }

            foreach (var incoming in validIncoming)
            {
                if (incoming.Id == 0)
                {
                    var yeniVaryant = new UrunSecenek
                    {
                        UrunId = urun.Id,
                        OlusturulmaTarihi = DateTime.UtcNow,
                        SilindiMi = false
                    };

                    ApplyVariantFields(yeniVaryant, incoming);
                    urun.UrunSecenek.Add(yeniVaryant);
                    continue;
                }

                var existing = urun.UrunSecenek.FirstOrDefault(x => x.Id == incoming.Id);
                if (existing == null)
                {
                    continue;
                }

                ApplyVariantFields(existing, incoming);
            }

            return Task.CompletedTask;
        }

        private static void SanitizeVariantScope(ICollection<UrunSecenek>? variants, bool supportsCanvasOptions)
        {
            if (supportsCanvasOptions || variants == null)
            {
                return;
            }

            foreach (var variant in variants)
            {
                variant.Olcu = string.Empty;
                variant.CerceveTipi = string.Empty;
                variant.CerceveRengi = string.Empty;
                variant.CerceveKalinligi = string.Empty;
                variant.MalzemeTuru = string.Empty;
                variant.ParcaSayisi = 1;
            }
        }

        private async Task SyncFeatureValuesAsync(Urun urun, ICollection<UrunOzellikDegeri>? incomingValues)
        {
            incomingValues ??= new List<UrunOzellikDegeri>();

            var allowedDefinitionIds = (await _context.UrunOzellikTanimlari
                .AsNoTracking()
                .Where(x =>
                    x.AktifMi &&
                    (x.UrunTipi == urun.UrunTipi || x.UrunTipi == UrunOzellikCatalog.Genel))
                .Select(x => x.Id)
                .ToListAsync())
                .ToHashSet();

            var validIncoming = incomingValues
                .Where(x => allowedDefinitionIds.Contains(x.UrunOzellikTanimiId))
                .Select(x => new
                {
                    x.UrunOzellikTanimiId,
                    Deger = NormalizeFeatureValue(x.Deger)
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Deger))
                .GroupBy(x => x.UrunOzellikTanimiId)
                .Select(x => x.First())
                .ToList();

            var incomingDefinitionIds = validIncoming
                .Select(x => x.UrunOzellikTanimiId)
                .ToHashSet();

            var toRemove = urun.UrunOzellikleri
                .Where(x => allowedDefinitionIds.Contains(x.UrunOzellikTanimiId) && !incomingDefinitionIds.Contains(x.UrunOzellikTanimiId))
                .ToList();

            if (toRemove.Count > 0)
            {
                _context.UrunOzellikDegerleri.RemoveRange(toRemove);
            }

            foreach (var incoming in validIncoming)
            {
                var existing = urun.UrunOzellikleri.FirstOrDefault(x => x.UrunOzellikTanimiId == incoming.UrunOzellikTanimiId);
                if (existing == null)
                {
                    urun.UrunOzellikleri.Add(new UrunOzellikDegeri
                    {
                        UrunId = urun.Id,
                        UrunOzellikTanimiId = incoming.UrunOzellikTanimiId,
                        Deger = incoming.Deger,
                        OlusturulmaTarihi = DateTime.UtcNow,
                        SilindiMi = false
                    });
                    continue;
                }

                existing.Deger = incoming.Deger;
            }
        }

        private void ValidateVariants(ICollection<UrunSecenek>? variants)
        {
            if (variants == null || variants.Count == 0)
            {
                return;
            }

            var validVariants = variants.Where(IsMeaningfulVariant).ToList();
            for (var i = 0; i < validVariants.Count; i++)
            {
                var variant = validVariants[i];
                var row = i + 1;

                if (variant.SatisFiyati < 0)
                {
                    ModelState.AddModelError(string.Empty, $"Varyasyon {row}: satis fiyati negatif olamaz.");
                }

                if (variant.MaliyetFiyati < 0)
                {
                    ModelState.AddModelError(string.Empty, $"Varyasyon {row}: maliyet negatif olamaz.");
                }

                if (variant.StokAdedi < 0)
                {
                    ModelState.AddModelError(string.Empty, $"Varyasyon {row}: stok adedi negatif olamaz.");
                }

                if (variant.ParcaSayisi < 0)
                {
                    ModelState.AddModelError(string.Empty, $"Varyasyon {row}: parca sayisi negatif olamaz.");
                }

                if (variant.UretimSuresiGun < 0)
                {
                    ModelState.AddModelError(string.Empty, $"Varyasyon {row}: uretim suresi negatif olamaz.");
                }

                if (variant.Desi < 0)
                {
                    ModelState.AddModelError(string.Empty, $"Varyasyon {row}: desi negatif olamaz.");
                }
            }
        }

        private static bool IsMeaningfulVariant(UrunSecenek? variant)
        {
            if (variant == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(variant.Olcu)
                || !string.IsNullOrWhiteSpace(variant.CerceveTipi)
                || !string.IsNullOrWhiteSpace(variant.CerceveRengi)
                || !string.IsNullOrWhiteSpace(variant.CerceveKalinligi)
                || !string.IsNullOrWhiteSpace(variant.MalzemeTuru)
                || !string.IsNullOrWhiteSpace(variant.Yon)
                || !string.IsNullOrWhiteSpace(variant.VaryantSku)
                || !string.IsNullOrWhiteSpace(variant.KisilestirmeMetni)
                || !string.IsNullOrWhiteSpace(variant.OzelTasarimNotu)
                || !string.IsNullOrWhiteSpace(variant.GorselUrl)
                || variant.SatisFiyati > 0
                || variant.MaliyetFiyati > 0
                || variant.FiyatFarki != 0
                || variant.StokAdedi > 0
                || variant.ParcaSayisi > 1
                || variant.UretimSuresiGun > 0
                || variant.Desi > 0;
        }

        private static void NormalizeVariantInput(UrunSecenek variant, int index)
        {
            variant.Olcu = TurkishTextRepairHelper.RepairKnownBrokenTurkish(variant.Olcu);
            variant.CerceveTipi = TurkishTextRepairHelper.RepairKnownBrokenTurkish(variant.CerceveTipi);
            variant.CerceveRengi = TurkishTextRepairHelper.RepairKnownBrokenTurkish(variant.CerceveRengi);
            variant.CerceveKalinligi = TurkishTextRepairHelper.RepairKnownBrokenTurkish(variant.CerceveKalinligi);
            variant.MalzemeTuru = TurkishTextRepairHelper.RepairKnownBrokenTurkish(variant.MalzemeTuru);
            variant.Yon = TurkishTextRepairHelper.RepairKnownBrokenTurkish(variant.Yon);
            variant.VaryantSku = variant.VaryantSku?.Trim() ?? string.Empty;
            variant.KisilestirmeMetni = TurkishTextRepairHelper.RepairKnownBrokenTurkish(variant.KisilestirmeMetni);
            variant.OzelTasarimNotu = TurkishTextRepairHelper.RepairKnownBrokenTurkish(variant.OzelTasarimNotu);
            variant.GorselUrl = variant.GorselUrl?.Trim() ?? string.Empty;
            variant.ParcaSayisi = variant.ParcaSayisi < 1 ? 1 : variant.ParcaSayisi;
            variant.StokAdedi = variant.StokAdedi < 0 ? 0 : variant.StokAdedi;
            variant.UretimSuresiGun = variant.UretimSuresiGun < 0 ? 0 : variant.UretimSuresiGun;
            variant.Desi = variant.Desi < 0 ? 0 : variant.Desi;
            variant.Sira = variant.Sira > 0 ? variant.Sira : index + 1;
        }

        private static decimal ResolveVariantSalePrice(Urun urun, UrunSecenek variant)
        {
            var basePrice = urun.IndirimliFiyat ?? urun.Fiyat;
            if (variant.SatisFiyati > 0)
            {
                return variant.SatisFiyati;
            }

            var resolvedPrice = basePrice + variant.FiyatFarki;
            return resolvedPrice > 0 ? resolvedPrice : basePrice;
        }

        private static void ApplyVariantFields(UrunSecenek target, UrunSecenek source)
        {
            target.Olcu = source.Olcu;
            target.CerceveTipi = source.CerceveTipi;
            target.CerceveRengi = source.CerceveRengi;
            target.CerceveKalinligi = source.CerceveKalinligi;
            target.MalzemeTuru = source.MalzemeTuru;
            target.Yon = source.Yon;
            target.ParcaSayisi = source.ParcaSayisi;
            target.VaryantSku = source.VaryantSku;
            target.KisilestirmeMetni = source.KisilestirmeMetni;
            target.OzelTasarimNotu = source.OzelTasarimNotu;
            target.FiyatFarki = source.FiyatFarki;
            target.SatisFiyati = source.SatisFiyati;
            target.MaliyetFiyati = source.MaliyetFiyati;
            target.StokAdedi = source.StokAdedi;
            target.UretimSuresiGun = source.UretimSuresiGun;
            target.Desi = source.Desi;
            target.GorselUrl = source.GorselUrl;
            target.AktifMi = source.AktifMi;
            target.VarsayilanMi = source.VarsayilanMi;
            target.TukeninceGizle = source.TukeninceGizle;
            target.OnSipariseAcikMi = source.OnSipariseAcikMi;
            target.Sira = source.Sira;
        }

        private static void ApplyProductFields(Urun source, Urun target)
        {
            target.Baslik = source.Baslik;
            target.KisaAd = source.KisaAd;
            target.SKU = source.SKU;
            target.Barkod = source.Barkod;
            target.Marka = source.Marka;
            target.UrunTipi = source.UrunTipi;
            target.Etiketler = source.Etiketler;
            target.KisaAciklama = source.KisaAciklama;
            target.Aciklama = source.Aciklama;
            target.TeknikOzellikler = source.TeknikOzellikler;
            target.MalzemeBilgisi = source.MalzemeBilgisi;
            target.BakimTalimati = source.BakimTalimati;
            target.PaketlemeBilgisi = source.PaketlemeBilgisi;
            target.StokDurumu = source.StokDurumu;
            target.Fiyat = source.Fiyat;
            target.IndirimliFiyat = source.IndirimliFiyat;
            target.Maliyet = source.Maliyet;
            target.KdvOrani = source.KdvOrani;
            target.UretimSuresiGun = source.UretimSuresiGun;
            target.KargoyaVerilisSuresiGun = source.KargoyaVerilisSuresiGun;
            target.TahminiTeslimSuresiGun = source.TahminiTeslimSuresiGun;
            target.AktifMi = source.AktifMi;
            target.OneCikanMi = source.OneCikanMi;
            target.YeniUrunMu = source.YeniUrunMu;
            target.KampanyaliMi = source.KampanyaliMi;
            target.AnaSayfadaGoster = source.AnaSayfadaGoster;
            target.MinSiparisAdedi = source.MinSiparisAdedi;
            target.MaxSiparisAdedi = source.MaxSiparisAdedi;
            target.KategoriId = source.KategoriId;
            target.SeoTitle = source.SeoTitle;
            target.SeoDescription = source.SeoDescription;
            target.SeoKeywords = source.SeoKeywords;
        }

        private static string NormalizeFeatureValue(string? rawValue)
        {
            var value = TurkishTextRepairHelper.RepairKnownBrokenTurkish(rawValue);
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.ToLowerInvariant() switch
            {
                "evet" => "true",
                "hayir" => "false",
                _ => value
            };
        }

        private static decimal TryParsePrice(string? rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return 0;
            }

            var normalized = rawValue.Replace(".", ",");
            return decimal.TryParse(normalized, out var price) ? price : 0;
        }
    }
}
