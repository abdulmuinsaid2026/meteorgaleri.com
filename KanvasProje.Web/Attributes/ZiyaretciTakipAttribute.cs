using System.IO;
using System.Text.RegularExpressions;
using KanvasProje.Core.Varliklar;
using KanvasProje.Data;
using Microsoft.AspNetCore.Mvc.Filters;

namespace KanvasProje.Web.Attributes
{
    public class ZiyaretciTakipAttribute : ActionFilterAttribute
    {
        private readonly KanvasDbContext _context;

        public ZiyaretciTakipAttribute(KanvasDbContext context)
        {
            _context = context;
        }

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var request = context.HttpContext.Request;
            if (ShouldSkip(request))
            {
                await next();
                return;
            }

            try
            {
                var ipAdresi = ResolveIpAddress(context);
                var userAgent = request.Headers.UserAgent.ToString();
                var cihazBilgi = CihazModeliBul(userAgent);
                var ulke = request.Headers["CF-IPCountry"].FirstOrDefault();
                var sehir = request.Headers["CF-IPCity"].FirstOrDefault();

                _context.ZiyaretciLoglari.Add(new ZiyaretciLog
                {
                    IpAdresi = ipAdresi,
                    Url = request.Path.Value ?? string.Empty,
                    Metod = request.Method,
                    ReferansUrl = request.Headers.Referer.ToString(),
                    CihazBilgisi = userAgent,
                    Tarayici = cihazBilgi.Tarayici,
                    IsletimSistemi = cihazBilgi.OS,
                    CihazModeli = cihazBilgi.Model,
                    Sehir = string.IsNullOrWhiteSpace(sehir) ? "-" : sehir,
                    Ulke = string.IsNullOrWhiteSpace(ulke) ? "-" : ulke,
                    OlusturulmaTarihi = DateTime.UtcNow,
                    KullaniciAdi = context.HttpContext.User.Identity?.IsAuthenticated == true
                        ? context.HttpContext.User.Identity.Name
                        : "Misafir"
                });

                await _context.SaveChangesAsync();
            }
            catch
            {
                // Ziyaretçi log hatası sayfa açılışını engellememeli
            }

            await next();
        }

        private static bool ShouldSkip(HttpRequest request)
        {
            var path = request.Path.Value ?? string.Empty;
            var isAjax = string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                || request.Headers.Accept.Any(a => a != null && a.Contains("application/json", StringComparison.OrdinalIgnoreCase));

            return isAjax
                || path.Contains("/admin", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/Urun/CanliAra", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/sitemap.xml", StringComparison.OrdinalIgnoreCase)
                || !HttpMethods.IsGet(request.Method)
                || Path.HasExtension(path);
        }

        private static string ResolveIpAddress(FilterContext context)
        {
            var forwardedFor = context.HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwardedFor))
            {
                return forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            }

            return context.HttpContext.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
                ?? context.HttpContext.Connection.RemoteIpAddress?.ToString()
                ?? "Bilinmiyor";
        }

        private static (string Tarayici, string OS, string Model) CihazModeliBul(string agent)
        {
            var os = "Bilinmiyor";
            var browser = "Bilinmiyor";
            var model = "PC / Bilinmiyor";

            if (agent.Contains("Windows", StringComparison.OrdinalIgnoreCase)) os = "Windows";
            else if (agent.Contains("Android", StringComparison.OrdinalIgnoreCase)) os = "Android";
            else if (agent.Contains("iPhone", StringComparison.OrdinalIgnoreCase) || agent.Contains("iPad", StringComparison.OrdinalIgnoreCase)) os = "iOS";
            else if (agent.Contains("Mac", StringComparison.OrdinalIgnoreCase)) os = "MacOS";
            else if (agent.Contains("Linux", StringComparison.OrdinalIgnoreCase)) os = "Linux";

            if (agent.Contains("Edg", StringComparison.OrdinalIgnoreCase)) browser = "Edge";
            else if (agent.Contains("Chrome", StringComparison.OrdinalIgnoreCase)) browser = "Chrome";
            else if (agent.Contains("Firefox", StringComparison.OrdinalIgnoreCase)) browser = "Firefox";
            else if (agent.Contains("Safari", StringComparison.OrdinalIgnoreCase)) browser = "Safari";
            else if (agent.Contains("Opera", StringComparison.OrdinalIgnoreCase) || agent.Contains("OPR", StringComparison.OrdinalIgnoreCase)) browser = "Opera";

            if (os == "Android")
            {
                var match = Regex.Match(agent, @";\s?([^;]+)\sBuild");
                model = match.Success ? match.Groups[1].Value.Trim() : "Android Cihaz";
            }
            else if (os == "iOS")
            {
                model = agent.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "iPad" : "iPhone";
            }
            else if (os == "Windows")
            {
                model = "PC (Windows)";
            }
            else if (os == "MacOS")
            {
                model = "Macbook / iMac";
            }

            return (browser, os, model);
        }
    }
}
