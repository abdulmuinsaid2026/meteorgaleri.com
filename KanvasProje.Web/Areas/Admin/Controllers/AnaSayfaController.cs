using KanvasProje.Core.Models;
using KanvasProje.Service.Services;
using Microsoft.AspNetCore.Mvc;

namespace KanvasProje.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class AnaSayfaController : AdminBaseController
    {
        private readonly IHomePageSettingsService _homePageSettingsService;
        private readonly IWebHostEnvironment _env;

        private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp"
        };

        private static readonly HashSet<string> AllowedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".webm", ".mov"
        };

        private const long MaxImageBytes = 10 * 1024 * 1024;
        private const long MaxVideoBytes = 60 * 1024 * 1024;

        public AnaSayfaController(IHomePageSettingsService homePageSettingsService, IWebHostEnvironment env)
        {
            _homePageSettingsService = homePageSettingsService;
            _env = env;
        }

        public IActionResult Index()
        {
            return View(_homePageSettingsService.GetSettings());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(HomePageSettingsModel model)
        {
            try
            {
                string uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "slider");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                if (model.Hero?.DesktopSlides != null)
                {
                    for (int i = 0; i < model.Hero.DesktopSlides.Count; i++)
                    {
                        var file = Request.Form.Files[$"SlideImage_{i}"];
                        if (file != null && file.Length > 0)
                        {
                            var ext = Path.GetExtension(file.FileName);
                            if (string.IsNullOrWhiteSpace(ext) || !AllowedImageExtensions.Contains(ext))
                            {
                                throw new InvalidOperationException("Desteklenmeyen görsel formatı (JPG, PNG, WebP).");
                            }
                            if (file.Length > MaxImageBytes)
                            {
                                throw new InvalidOperationException("Görsel boyutu en fazla 10 MB olabilir.");
                            }

                            DeleteLocalSliderFile(model.Hero.DesktopSlides[i].ImageUrl);
                            string uniqueFileName = Guid.NewGuid().ToString("N") + ext.ToLowerInvariant();
                            string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                            await using (var fileStream = new FileStream(filePath, FileMode.Create))
                            {
                                await file.CopyToAsync(fileStream);
                            }

                            model.Hero.DesktopSlides[i].ImageUrl = $"/uploads/slider/{uniqueFileName}";
                        }

                        var videoFile = Request.Form.Files[$"SlideVideo_{i}"];
                        if (videoFile != null && videoFile.Length > 0)
                        {
                            var ext = Path.GetExtension(videoFile.FileName);
                            if (string.IsNullOrWhiteSpace(ext) || !AllowedVideoExtensions.Contains(ext))
                            {
                                throw new InvalidOperationException("Desteklenmeyen video formatı (MP4, WebM, MOV).");
                            }
                            if (videoFile.Length > MaxVideoBytes)
                            {
                                throw new InvalidOperationException("Video boyutu en fazla 60 MB olabilir.");
                            }

                            DeleteLocalSliderFile(model.Hero.DesktopSlides[i].VideoUrl);
                            string uniqueVideoName = Guid.NewGuid().ToString("N") + ext.ToLowerInvariant();
                            string videoPath = Path.Combine(uploadsFolder, uniqueVideoName);
                            await using (var fileStream = new FileStream(videoPath, FileMode.Create))
                            {
                                await videoFile.CopyToAsync(fileStream);
                            }

                            model.Hero.DesktopSlides[i].VideoUrl = $"/uploads/slider/{uniqueVideoName}";
                        }
                    }
                }

                if (model.Hero?.DesktopSlides != null && model.Hero.MobileSlides != null)
                {
                    for (var i = 0; i < model.Hero.DesktopSlides.Count && i < model.Hero.MobileSlides.Count; i++)
                    {
                        if (string.IsNullOrWhiteSpace(model.Hero.MobileSlides[i].Title))
                            model.Hero.MobileSlides[i].Title = model.Hero.DesktopSlides[i].Title;
                        if (string.IsNullOrWhiteSpace(model.Hero.MobileSlides[i].Subtitle))
                            model.Hero.MobileSlides[i].Subtitle = model.Hero.DesktopSlides[i].Subtitle;
                        if (string.IsNullOrWhiteSpace(model.Hero.MobileSlides[i].Description))
                            model.Hero.MobileSlides[i].Description = model.Hero.DesktopSlides[i].Description;
                        if (string.IsNullOrWhiteSpace(model.Hero.MobileSlides[i].ButtonText))
                            model.Hero.MobileSlides[i].ButtonText = model.Hero.DesktopSlides[i].ButtonText;
                        if (string.IsNullOrWhiteSpace(model.Hero.MobileSlides[i].ButtonUrl))
                            model.Hero.MobileSlides[i].ButtonUrl = model.Hero.DesktopSlides[i].ButtonUrl;
                        if (string.IsNullOrWhiteSpace(model.Hero.MobileSlides[i].VideoUrl))
                            model.Hero.MobileSlides[i].VideoUrl = model.Hero.DesktopSlides[i].VideoUrl;
                    }
                }

                _homePageSettingsService.SaveSettings(model);
                TempData["Basari"] = "Ana sayfa slider ayarları kaydedildi.";
            }
            catch (Exception ex)
            {
                TempData["Hata"] = "Ana sayfa ayarları kaydedilirken hata oluştu: " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        private void DeleteLocalSliderFile(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            var trimmed = url.Trim();
            if (!trimmed.StartsWith("/uploads/slider/", StringComparison.OrdinalIgnoreCase)) return;

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
