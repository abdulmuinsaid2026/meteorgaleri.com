using KanvasProje.Core.Interfaces;
using KanvasProje.Core.Varliklar;
using KanvasProje.Data;
using KanvasProje.Web.Models;
using KanvasProje.Web.Security;
using KanvasProje.Service.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace KanvasProje.Web.Controllers
{
    public class HesapController : Controller
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly SignInManager<AppUser> _signInManager;
        private readonly KanvasDbContext _context;
        private readonly IEmailService _emailService;
        private readonly IAdminSessionStateService _adminSessionStateService;
        private readonly IAdminSecurityAuditService _adminSecurityAuditService;
        private readonly IConfiguration _configuration;

        public HesapController(
            UserManager<AppUser> userManager,
            SignInManager<AppUser> signInManager,
            KanvasDbContext context,
            IEmailService emailService,
            IAdminSessionStateService adminSessionStateService,
            IAdminSecurityAuditService adminSecurityAuditService,
            IConfiguration configuration)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _context = context;
            _emailService = emailService;
            _adminSessionStateService = adminSessionStateService;
            _adminSecurityAuditService = adminSecurityAuditService;
            _configuration = configuration;
        }

        [HttpGet]
        public IActionResult KayitOl()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
        public async Task<IActionResult> KayitOl(KayitViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = new AppUser
            {
                UserName = model.Eposta,
                Email = model.Eposta,
                AdSoyad = model.AdSoyad,
                Sehir = model.Sehir,
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(user, model.Sifre);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return View(model);
            }

            await _userManager.AddToRoleAsync(user, AdminSecurityRoles.Uye);
            await _signInManager.SignInAsync(user, isPersistent: true);

            try
            {
                var sessionId = HttpContext.Session.Id;
                var sepetService = HttpContext.RequestServices.GetRequiredService<ISepetService>();
                await sepetService.MergeSepetlerAsync(sessionId, user.Id);
            }
            catch
            {
            }

            TempData["Basari"] = "Üyeliğiniz oluşturuldu. Hoş geldiniz!";
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult EpostaOnayBilgilendirme()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> EpostaDogrula(string userId, string token)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
            {
                return RedirectToAction("Index", "Home");
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound("Kullanici bulunamadi.");
            }

            var result = await _userManager.ConfirmEmailAsync(user, token);
            if (result.Succeeded)
            {
                return View("DogrulamaBasarili");
            }

            return Content("Hata: E-posta dogrulanamadi.");
        }

        [HttpGet]
        public IActionResult GirisYap(string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
        public async Task<IActionResult> GirisYap(string eposta, string sifre, string? returnUrl = null)
        {
            var user = await _userManager.FindByEmailAsync(eposta);
            if (user != null)
            {
                var result = await _signInManager.PasswordSignInAsync(user, sifre, isPersistent: true, lockoutOnFailure: true);
                if (result.Succeeded)
                {
                    try
                    {
                        var sessionId = HttpContext.Session.Id;
                        var sepetService = HttpContext.RequestServices.GetRequiredService<ISepetService>();
                        await sepetService.MergeSepetlerAsync(sessionId, user.Id);
                    }
                    catch
                    {
                    }

                    HttpContext.Session.Remove(AdminSessionConstants.SessionKey);

                    var roles = await _userManager.GetRolesAsync(user);
                    if (roles.Any(AdminSecurityRoles.IsAdminRole))
                    {
                        var roleLabel = AdminSecurityRoles.GetPrimaryRoleLabel(roles);
                        var sessionState = await _adminSessionStateService.RegisterSessionAsync(
                            user,
                            roleLabel,
                            HttpContext.Connection.RemoteIpAddress?.ToString());

                        HttpContext.Session.SetString(AdminSessionConstants.SessionKey, sessionState.CurrentSessionToken);

                        await _adminSecurityAuditService.LogAsync(
                            HttpContext,
                            "admin_login_success",
                            "Admin hesabi basariyla giris yapti.",
                            "/Admin",
                            user.Id,
                            user.UserName ?? user.Email);
                    }

                    if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    {
                        return Redirect(returnUrl);
                    }

                    return RedirectToAction("Index", "Home");
                }
            }

            ViewBag.Hata = "E-posta veya şifre hatalı.";
            TempData["Hata"] = ViewBag.Hata;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CikisYap()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                var user = await _userManager.GetUserAsync(User);
                if (user != null)
                {
                    var roles = await _userManager.GetRolesAsync(user);
                    if (roles.Any(AdminSecurityRoles.IsAdminRole))
                    {
                        await _adminSecurityAuditService.LogAsync(
                            HttpContext,
                            "admin_logout",
                            "Admin oturumu kapatildi.",
                            "/Admin",
                            user.Id,
                            user.UserName ?? user.Email);

                        await _adminSessionStateService.ClearSessionAsync(user.Id);
                    }
                }
            }

            HttpContext.Session.Remove(AdminSessionConstants.SessionKey);
            await _signInManager.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult SifremiUnuttum()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SifremiUnuttum(string eposta)
        {
            ViewBag.Eposta = eposta;
            if (string.IsNullOrWhiteSpace(eposta))
            {
                ViewBag.Hata = "Lütfen kayıtlı e-posta adresinizi yazın.";
                TempData["Hata"] = ViewBag.Hata;
                return View();
            }

            var user = await _userManager.FindByEmailAsync(eposta);
            if (user == null)
            {
                ViewBag.Mesaj = "Eğer bu e-posta sistemde kayıtlıysa, şifre sıfırlama bağlantısı gönderildi.";
                TempData["Basari"] = ViewBag.Mesaj;
                return View();
            }

            user.SifreSifirlamaToken = Guid.NewGuid().ToString();
            user.SifreSifirlamaGecerlilik = DateTime.UtcNow.AddHours(1);
            await _userManager.UpdateAsync(user);

            var link = Url.Action("SifreSifirla", "Hesap", new { token = user.SifreSifirlamaToken }, Request.Scheme) ?? string.Empty;

            try
            {
                await _emailService.SendTemplateMailAsync(
                    user.Email ?? string.Empty,
                    "\u015Eifre s\u0131f\u0131rlama talebi",
                    user.AdSoyad,
                    "Hesab\u0131n\u0131z i\u00E7in bir \u015Fifre s\u0131f\u0131rlama talebi ald\u0131k. Bu i\u015Flemi siz yapmad\u0131ysan\u0131z mesaj\u0131 yok sayabilirsiniz.",
                    link,
                    "\u015Eifremi S\u0131f\u0131rla");
            }
            catch (Exception ex)
            {
                ViewBag.Hata = "Şifre sıfırlama e-postası gönderilemedi: " + ex.Message;
                TempData["Hata"] = ViewBag.Hata;
                return View();
            }

            ViewBag.Mesaj = "Şifre sıfırlama bağlantısı e-posta adresinize gönderildi.";
            TempData["Basari"] = ViewBag.Mesaj;
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> SifreSifirla(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return RedirectToAction("GirisYap");
            }

            var user = await _userManager.Users.FirstOrDefaultAsync(x => x.SifreSifirlamaToken == token && x.SifreSifirlamaGecerlilik > DateTime.UtcNow);
            if (user == null)
            {
                ViewBag.Hata = "Bu bağlantı geçersiz veya süresi dolmuş.";
                TempData["Hata"] = ViewBag.Hata;
                return RedirectToAction("SifremiUnuttum");
            }

            return View(new SifreSifirlaViewModel { Token = token });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SifreSifirla(SifreSifirlaViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.Users.FirstOrDefaultAsync(x => x.SifreSifirlamaToken == model.Token && x.SifreSifirlamaGecerlilik > DateTime.UtcNow);
            if (user == null)
            {
                ViewBag.Hata = "İşlem başarısız. Bağlantının süresi dolmuş olabilir.";
                TempData["Hata"] = ViewBag.Hata;
                return View(model);
            }

            await _userManager.RemovePasswordAsync(user);
            var result = await _userManager.AddPasswordAsync(user, model.YeniSifre);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return View(model);
            }

            user.SifreSifirlamaToken = null;
            user.SifreSifirlamaGecerlilik = null;
            await _userManager.UpdateAsync(user);

            TempData["Basari"] = "Şifreniz başarıyla güncellendi. Giriş yapabilirsiniz.";
            return RedirectToAction("GirisYap");
        }

        public IActionResult ErisimEngellendi()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> SosyalKayitTamamla()
        {
            if (TempData["SosyalKayitYeni"] == null || TempData["SosyalKayitYeni"]?.ToString() != "true")
                return RedirectToAction("Index", "Home");

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("GirisYap");

            return View(new SosyalKayitTamamlaViewModel { AdSoyad = user.AdSoyad, Eposta = user.Email ?? string.Empty });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SosyalKayitTamamla(SosyalKayitTamamlaViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("GirisYap");

            user.Sehir = model.Sehir;
            await _userManager.UpdateAsync(user);

            TempData["Basari"] = "Hesabınız tamamlandı!";
            return RedirectToAction("Index", "Home");
        }

        private bool IsExternalProviderConfigured(string? provider)
        {
            return provider switch
            {
                "Google" => !string.IsNullOrWhiteSpace(_configuration["Authentication:Google:ClientId"] ?? _configuration["Google:ClientId"])
                    && !string.IsNullOrWhiteSpace(_configuration["Authentication:Google:ClientSecret"] ?? _configuration["Google:ClientSecret"]),
                _ => false
            };
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ExternalLogin(string provider, string? returnUrl = null)
        {
            if (!IsExternalProviderConfigured(provider))
            {
                TempData["Hata"] = "Sosyal giriş şu anda aktif değil. Lütfen e-posta ve şifrenizle devam edin.";
                if (Uri.TryCreate(Request.Headers.Referer.ToString(), UriKind.Absolute, out var referer)
                    && referer.Host == Request.Host.Host
                    && referer.LocalPath.Contains("KayitOl", StringComparison.OrdinalIgnoreCase))
                {
                    return RedirectToAction("KayitOl");
                }

                return RedirectToAction("GirisYap");
            }

            var redirectUrl = Url.Action("ExternalLoginCallback", "Hesap", new { returnUrl });
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            return Challenge(properties, provider);
        }

        [HttpGet]
        public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = null, string? remoteError = null)
        {
            if (remoteError != null)
            {
                ViewBag.Hata = $"Harici giriş hatası: {remoteError}";
                return View("GirisYap");
            }

            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                ViewBag.Hata = "Harici giriş bilgisi alınamadı.";
                return View("GirisYap");
            }

            var result = await _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);
            if (result.Succeeded)
            {
                await _signInManager.UpdateExternalAuthenticationTokensAsync(info);

                try
                {
                    var sessionId = HttpContext.Session.Id;
                    var user = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
                    if (user != null)
                    {
                        var sepetService = HttpContext.RequestServices.GetRequiredService<ISepetService>();
                        await sepetService.MergeSepetlerAsync(sessionId, user.Id);
                    }
                }
                catch { }

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);
                return RedirectToAction("Index", "Home");
            }

            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            var name = info.Principal.FindFirstValue(ClaimTypes.Name);

            if (string.IsNullOrWhiteSpace(email))
            {
                ViewBag.Hata = "Harici giriş sağlayıcısı e-posta adresinizi paylaşmadı.";
                return View("GirisYap");
            }

            var existingUser = await _userManager.FindByEmailAsync(email);
            if (existingUser != null)
            {
                var addLoginResult = await _userManager.AddLoginAsync(existingUser, info);
                if (addLoginResult.Succeeded)
                {
                    await _signInManager.SignInAsync(existingUser, isPersistent: false);
                    try
                    {
                        var sessionId = HttpContext.Session.Id;
                        var sepetService = HttpContext.RequestServices.GetRequiredService<ISepetService>();
                        await sepetService.MergeSepetlerAsync(sessionId, existingUser.Id);
                    }
                    catch { }

                    if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                        return Redirect(returnUrl);
                    return RedirectToAction("Index", "Home");
                }
            }
            else
            {
                var user = new AppUser
                {
                    UserName = email,
                    Email = email,
                    AdSoyad = name ?? email,
                    EmailConfirmed = true
                };

                var createResult = await _userManager.CreateAsync(user);
                if (createResult.Succeeded)
                {
                    await _userManager.AddToRoleAsync(user, AdminSecurityRoles.Uye);
                    await _userManager.AddLoginAsync(user, info);
                    await _signInManager.SignInAsync(user, isPersistent: false);

                    try
                    {
                        var sessionId = HttpContext.Session.Id;
                        var sepetService = HttpContext.RequestServices.GetRequiredService<ISepetService>();
                        await sepetService.MergeSepetlerAsync(sessionId, user.Id);
                    }
                    catch { }

                    TempData["SosyalKayitYeni"] = "true";
                    return RedirectToAction("SosyalKayitTamamla");
                }
            }

            ViewBag.Hata = "Harici giriş sırasında bir hata oluştu.";
            return View("GirisYap");
        }
    }
}
