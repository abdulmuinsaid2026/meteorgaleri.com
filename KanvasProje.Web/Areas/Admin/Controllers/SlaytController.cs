using System.IO;
using KanvasProje.Core.Varliklar;
using KanvasProje.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanvasProje.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class SlaytController : AdminBaseController
    {
        private readonly KanvasDbContext _db;
        private readonly IWebHostEnvironment _env;

        private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp"
        };

        private static readonly HashSet<string> AllowedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".webm", ".mov"
        };

        private const long MaxSliderImageBytes = 10 * 1024 * 1024;
        private const long MaxSliderVideoBytes = 60 * 1024 * 1024;

        public SlaytController(KanvasDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        public async Task<IActionResult> Index()
        {
            var slaytlar = await _db.Slaytlar
                .OrderBy(s => s.Sira)
                .ThenBy(s => s.OlusturmaTarihi)
                .ToListAsync();

            return View(slaytlar);
        }

        public IActionResult Ekle()
        {
            return View(new Slayt { Sira = 1, AktifMi = true, Tur = "Resim" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(209_715_200)]
        [RequestFormLimits(MultipartBodyLengthLimit = 209_715_200)]
        public async Task<IActionResult> Ekle(Slayt model, IFormFile? Resim, IFormFile? Video, IFormFile? MobilResim, IFormFile? MobilVideo)
        {
            if (!ModelState.IsValid)
                return View(model);

            if (model.Tur == "Video" && Video == null && string.IsNullOrWhiteSpace(model.VideoUrl) && MobilVideo == null && string.IsNullOrWhiteSpace(model.MobilVideoUrl))
            {
                ModelState.AddModelError("Video", "Video tipinde slayt için video yüklemeniz veya video URL'si girmeniz gerekir.");
                return View(model);
            }

            if (model.Tur == "Resim" && Resim == null && string.IsNullOrWhiteSpace(model.ResimUrl) && MobilResim == null && string.IsNullOrWhiteSpace(model.MobilResimUrl))
            {
                ModelState.AddModelError("Resim", "Resim tipinde slayt için görsel yüklemeniz veya görsel URL'si girmeniz gerekir.");
                return View(model);
            }

            try
            {
                if (Resim != null)
                {
                    model.ResimUrl = await SaveFileAsync(Resim, "uploads/slider", isVideo: false);
                }

                if (Video != null)
                {
                    model.VideoUrl = await SaveFileAsync(Video, "uploads/slider", isVideo: true);
                }

                if (MobilResim != null)
                {
                    model.MobilResimUrl = await SaveFileAsync(MobilResim, "uploads/slider", isVideo: false);
                }

                if (MobilVideo != null)
                {
                    model.MobilVideoUrl = await SaveFileAsync(MobilVideo, "uploads/slider", isVideo: true);
                }
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(model);
            }

            if (model.Sira <= 0)
            {
                var maxSira = await _db.Slaytlar.MaxAsync(s => (int?)s.Sira) ?? 0;
                model.Sira = maxSira + 1;
            }
            model.OlusturmaTarihi = DateTime.UtcNow;

            _db.Slaytlar.Add(model);
            await _db.SaveChangesAsync();

            TempData["Basari"] = "Slayt başarıyla eklendi.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Duzenle(int id)
        {
            var slayt = await _db.Slaytlar.FindAsync(id);
            if (slayt == null)
            {
                TempData["Hata"] = "Slayt bulunamadı.";
                return RedirectToAction(nameof(Index));
            }
            return View(slayt);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(209_715_200)]
        [RequestFormLimits(MultipartBodyLengthLimit = 209_715_200)]
        public async Task<IActionResult> Duzenle(int id, Slayt model, IFormFile? Resim, IFormFile? Video, IFormFile? MobilResim, IFormFile? MobilVideo, bool? ResimSil, bool? VideoSil, bool? MobilResimSil, bool? MobilVideoSil)
        {
            var slayt = await _db.Slaytlar.FindAsync(id);
            if (slayt == null)
            {
                TempData["Hata"] = "Slayt bulunamadı.";
                return RedirectToAction(nameof(Index));
            }

            if (!ModelState.IsValid)
            {
                model.ResimUrl ??= slayt.ResimUrl;
                model.VideoUrl ??= slayt.VideoUrl;
                model.MobilResimUrl ??= slayt.MobilResimUrl;
                model.MobilVideoUrl ??= slayt.MobilVideoUrl;
                return View(model);
            }

            var oldResimUrl = slayt.ResimUrl;
            var oldVideoUrl = slayt.VideoUrl;
            var oldMobilResimUrl = slayt.MobilResimUrl;
            var oldMobilVideoUrl = slayt.MobilVideoUrl;

            slayt.Baslik = model.Baslik;
            slayt.AltBaslik = model.AltBaslik;
            slayt.Aciklama = model.Aciklama;
            slayt.ButonYazi = model.ButonYazi;
            slayt.ButonUrl = model.ButonUrl;
            slayt.Tur = model.Tur;
            slayt.Sira = model.Sira > 0 ? model.Sira : slayt.Sira;
            slayt.AktifMi = model.AktifMi;

            try
            {
                if (Resim != null)
                {
                    var newPath = await SaveFileAsync(Resim, "uploads/slider", isVideo: false);
                    if (!string.IsNullOrEmpty(slayt.ResimUrl))
                        DeleteFile(slayt.ResimUrl);
                    slayt.ResimUrl = newPath;
                }
                else if (ResimSil == true && !string.IsNullOrEmpty(slayt.ResimUrl))
                {
                    DeleteFile(slayt.ResimUrl);
                    slayt.ResimUrl = null;
                }
                else if (!string.IsNullOrWhiteSpace(model.ResimUrl) && model.ResimUrl != oldResimUrl)
                {
                    slayt.ResimUrl = model.ResimUrl.Trim();
                }

                if (Video != null)
                {
                    var newPath = await SaveFileAsync(Video, "uploads/slider", isVideo: true);
                    if (!string.IsNullOrEmpty(slayt.VideoUrl))
                        DeleteFile(slayt.VideoUrl);
                    slayt.VideoUrl = newPath;
                }
                else if (VideoSil == true && !string.IsNullOrEmpty(slayt.VideoUrl))
                {
                    DeleteFile(slayt.VideoUrl);
                    slayt.VideoUrl = null;
                }
                else if (!string.IsNullOrWhiteSpace(model.VideoUrl) && model.VideoUrl != oldVideoUrl)
                {
                    slayt.VideoUrl = model.VideoUrl.Trim();
                }

                if (MobilResim != null)
                {
                    var newPath = await SaveFileAsync(MobilResim, "uploads/slider", isVideo: false);
                    if (!string.IsNullOrEmpty(slayt.MobilResimUrl))
                        DeleteFile(slayt.MobilResimUrl);
                    slayt.MobilResimUrl = newPath;
                }
                else if (MobilResimSil == true && !string.IsNullOrEmpty(slayt.MobilResimUrl))
                {
                    DeleteFile(slayt.MobilResimUrl);
                    slayt.MobilResimUrl = null;
                }
                else if (!string.IsNullOrWhiteSpace(model.MobilResimUrl) && model.MobilResimUrl != oldMobilResimUrl)
                {
                    slayt.MobilResimUrl = model.MobilResimUrl.Trim();
                }

                if (MobilVideo != null)
                {
                    var newPath = await SaveFileAsync(MobilVideo, "uploads/slider", isVideo: true);
                    if (!string.IsNullOrEmpty(slayt.MobilVideoUrl))
                        DeleteFile(slayt.MobilVideoUrl);
                    slayt.MobilVideoUrl = newPath;
                }
                else if (MobilVideoSil == true && !string.IsNullOrEmpty(slayt.MobilVideoUrl))
                {
                    DeleteFile(slayt.MobilVideoUrl);
                    slayt.MobilVideoUrl = null;
                }
                else if (!string.IsNullOrWhiteSpace(model.MobilVideoUrl) && model.MobilVideoUrl != oldMobilVideoUrl)
                {
                    slayt.MobilVideoUrl = model.MobilVideoUrl.Trim();
                }
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(slayt);
            }

            await _db.SaveChangesAsync();

            TempData["Basari"] = "Slayt başarıyla güncellendi.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AktiflikDegistir(int id)
        {
            var slayt = await _db.Slaytlar.FindAsync(id);
            if (slayt != null)
            {
                slayt.AktifMi = !slayt.AktifMi;
                await _db.SaveChangesAsync();
                TempData["Basari"] = slayt.AktifMi ? "Slayt aktif edildi." : "Slayt pasif edildi.";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> JSONdanAl()
        {
            var jsonPath = System.IO.Path.Combine(_env.ContentRootPath, "App_Data", "home-page-settings.json");
            if (!System.IO.File.Exists(jsonPath))
            {
                TempData["Hata"] = "JSON dosyası bulunamadı.";
                return RedirectToAction(nameof(Index));
            }

            var json = await System.IO.File.ReadAllTextAsync(jsonPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var hero = doc.RootElement.GetProperty("Hero");
            var slides = hero.GetProperty("DesktopSlides");

            int sira = 1;
            foreach (var slide in slides.EnumerateArray())
            {
                var imageUrl = slide.GetProperty("ImageUrl").GetString() ?? "";
                var videoUrl = slide.GetProperty("VideoUrl").GetString() ?? "";
                var title = slide.GetProperty("Title").GetString() ?? "";
                var subtitle = slide.GetProperty("Subtitle").GetString() ?? "";
                var description = slide.GetProperty("Description").GetString() ?? "";

                var tur = !string.IsNullOrEmpty(videoUrl) ? "Video" : "Resim";

                var mevcut = await _db.Slaytlar
                    .FirstOrDefaultAsync(s => s.ResimUrl == imageUrl || s.VideoUrl == videoUrl);

                if (mevcut == null)
                {
                    var cleanTitle = title.Replace("\\n", " ").Replace("\n", "");
                    _db.Slaytlar.Add(new Slayt
                    {
                        Baslik = cleanTitle,
                        AltBaslik = subtitle ?? "",
                        Aciklama = description ?? "",
                        ResimUrl = imageUrl,
                        VideoUrl = videoUrl,
                        Tur = tur,
                        Sira = sira,
                        AktifMi = true,
                        OlusturmaTarihi = DateTime.UtcNow
                    });
                    sira++;
                }
            }

            await _db.SaveChangesAsync();
            TempData["Basari"] = "JSON'dan " + (sira - 1) + " slayt içe aktarıldı.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Sil(int id)
        {
            var slayt = await _db.Slaytlar.FindAsync(id);
            if (slayt == null)
            {
                return Json(new { success = false, message = "Slayt bulunamadı." });
            }

            var totalCount = await _db.Slaytlar.CountAsync();
            if (totalCount <= 1)
            {
                return Json(new { success = false, message = "En az 1 slayt kalmalıdır. Tüm slaytları silemezsiniz." });
            }

            if (!string.IsNullOrEmpty(slayt.ResimUrl))
                DeleteFile(slayt.ResimUrl);

            if (!string.IsNullOrEmpty(slayt.VideoUrl))
                DeleteFile(slayt.VideoUrl);

            if (!string.IsNullOrEmpty(slayt.MobilResimUrl))
                DeleteFile(slayt.MobilResimUrl);

            if (!string.IsNullOrEmpty(slayt.MobilVideoUrl))
                DeleteFile(slayt.MobilVideoUrl);

            _db.Slaytlar.Remove(slayt);
            await _db.SaveChangesAsync();

            return Json(new { success = true, message = "Slayt silindi." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Siralama([FromBody] List<int>? ids)
        {
            if (ids == null || ids.Count == 0)
            {
                return Json(new { success = false, message = "Geçersiz sıralama verisi." });
            }

            var slaytlar = await _db.Slaytlar.ToListAsync();
            for (int i = 0; i < ids.Count; i++)
            {
                var slayt = slaytlar.FirstOrDefault(s => s.Id == ids[i]);
                if (slayt != null)
                    slayt.Sira = i + 1;
            }
            await _db.SaveChangesAsync();
            return Json(new { success = true });
        }

        private async Task<string> SaveFileAsync(IFormFile file, string subFolder, bool isVideo)
        {
            var ext = Path.GetExtension(file.FileName);
            if (isVideo)
            {
                if (string.IsNullOrWhiteSpace(ext) || !AllowedVideoExtensions.Contains(ext))
                {
                    throw new InvalidOperationException("Desteklenmeyen video formatı. Sadece MP4, WebM veya MOV yüklenebilir.");
                }
                if (file.Length <= 0 || file.Length > MaxSliderVideoBytes)
                {
                    throw new InvalidOperationException("Video dosyası en fazla 60 MB olabilir.");
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(ext) || !AllowedImageExtensions.Contains(ext))
                {
                    throw new InvalidOperationException("Desteklenmeyen görsel formatı. Sadece JPG, PNG veya WebP yüklenebilir.");
                }
                if (file.Length <= 0 || file.Length > MaxSliderImageBytes)
                {
                    throw new InvalidOperationException("Görsel dosyası en fazla 10 MB olabilir.");
                }
            }

            var uploadsFolder = Path.Combine(_env.WebRootPath, subFolder);
            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            var safeExt = ext.ToLowerInvariant();
            var uniqueFileName = Guid.NewGuid().ToString("N") + safeExt;
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            await using var stream = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream);

            return "/" + subFolder.Replace("\\", "/") + "/" + uniqueFileName;
        }

        private void DeleteFile(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            var trimmed = url.Trim();
            if (!trimmed.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                var relativePath = trimmed.TrimStart('/').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                var fullPath = Path.GetFullPath(Path.Combine(_env.WebRootPath, relativePath));
                var webRootFullPath = Path.GetFullPath(_env.WebRootPath);
                if (fullPath.StartsWith(webRootFullPath, StringComparison.Ordinal) && System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Delete(fullPath);
                }
            }
            catch
            {
            }
        }
    }
}