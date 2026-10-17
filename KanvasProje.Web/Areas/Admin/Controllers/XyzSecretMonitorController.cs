using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KanvasProje.Data;
using KanvasProje.Web.Security;

namespace KanvasProje.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize]
    public class XyzSecretMonitorController : AdminBaseController
    {
        private readonly IWebHostEnvironment _env;
        private readonly KanvasDbContext _db;
        private static readonly string[] AllowedMonitorRoles =
        {
            AdminSecurityRoles.LegacyAdmin,
            AdminSecurityRoles.SuperAdmin,
            AdminSecurityRoles.Yonetici
        };

        public XyzSecretMonitorController(IWebHostEnvironment env, KanvasDbContext db)
        {
            _env = env;
            _db = db;
        }

        public async Task<IActionResult> Index(string type, int page = 1, int pageSize = 50)
        {
            if (!IsAuthorizedAdmin()) return Forbid();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 25, 200);
            type = (type ?? string.Empty).Trim().ToLowerInvariant();
            var logs = new List<LogEntry>();
            var ipLogCount = 0;
            var auditLogCount = 0;
            var siparisLogCount = 0;

            if (string.IsNullOrEmpty(type) || type == "all" || type == "ip")
            {
                var ipLogs = await _db.ZiyaretciLoglari
                    .OrderByDescending(x => x.OlusturulmaTarihi)
                    .Take(2000)
                    .ToListAsync();

                ipLogCount = ipLogs.Count;
                logs.AddRange(ipLogs.Select(e => new LogEntry
                {
                    Tarih = e.OlusturulmaTarihi.ToTurkeyString("yyyy-MM-dd HH:mm:ss"),
                    Tip = "IP",
                    Kullanci = e.KullaniciAdi ?? "-",
                    Ip = e.IpAdresi ?? "-",
                    Islem = e.Url?.Split('/').LastOrDefault() ?? "-",
                    Detay = $"{e.Tarayici} | {e.IsletimSistemi}"
                }));
            }

            var auditPath = Path.Combine(_env.ContentRootPath, "App_Data", "admin-security-audit.log");
            if (System.IO.File.Exists(auditPath) && (string.IsNullOrEmpty(type) || type == "all" || type == "audit"))
            {
                var lines = await System.IO.File.ReadAllLinesAsync(auditPath);
                var auditEntries = lines.Where(l => !string.IsNullOrWhiteSpace(l))
                    .Select(l => System.Text.Json.JsonSerializer.Deserialize<AuditEntry>(l))
                    .Where(e => e != null)
                    .ToList();

                auditLogCount = auditEntries.Count;
                logs.AddRange(auditEntries
                    .Select(e => new LogEntry
                    {
                        Tarih = e!.CreatedUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                        Tip = "AUDIT",
                        Kullanci = e.UserName,
                        Ip = e.IpAddress,
                        Islem = e.EventType,
                        Detay = $"{e.Message} → {e.Target}"
                    }));
            }

            if (string.IsNullOrEmpty(type) || type == "all" || type == "siparis")
            {
                var siparisler = await _db.Siparisler
                    .OrderByDescending(x => x.OlusturulmaTarihi)
                    .Take(100)
                    .ToListAsync();

                siparisLogCount = siparisler.Count;
                logs.AddRange(siparisler.Select(e => new LogEntry
                {
                    Tarih = e.OlusturulmaTarihi.ToString("yyyy-MM-dd HH:mm:ss"),
                    Tip = "SIPARIS",
                    Kullanci = e.AppUserId?.Length > 8 ? e.AppUserId[..8] + "..." : e.MusteriAdSoyad,
                    Ip = "-",
                    Islem = $"#{e.SiparisNo}",
                    Detay = $"{e.ToplamTutar:N2} TL - {e.Durum}"
                }));
            }

            var ipStats = logs.Where(l => l.Tip == "IP" && !string.IsNullOrWhiteSpace(l.Ip) && l.Ip != "-")
                .GroupBy(l => l.Ip)
                .Select(g => new IpStatEntry { Ip = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(20)
                .ToList();

            ViewBag.IpStats = ipStats;
            ViewBag.ToplamIp = ipStats.Count;
            ViewBag.ToplamHit = ipStats.Sum(x => x.Count);

            logs = logs.OrderByDescending(l => DateTime.TryParse(l.Tarih, out var dt) ? dt : DateTime.MinValue).ToList();
            ViewBag.Logs = logs.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            ViewBag.TotalCount = logs.Count;
            ViewBag.IpLogCount = ipLogCount;
            ViewBag.SiparisLogCount = siparisLogCount;
            ViewBag.AuditLogCount = auditLogCount;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = (int)Math.Ceiling(logs.Count / (double)pageSize);
            ViewBag.Type = type;

            return View();
        }

        private bool IsAuthorizedAdmin()
        {
            return User.Identity?.IsAuthenticated == true &&
                   AdminSecurityRoles.UserHasAnyRole(User, AllowedMonitorRoles);
        }

        public class LogEntry
        {
            public string Tarih { get; set; } = "";
            public string Tip { get; set; } = "";
            public string Kullanci { get; set; } = "";
            public string Ip { get; set; } = "";
            public string Islem { get; set; } = "";
            public string Detay { get; set; } = "";
        }

        public class AuditEntry
        {
            public string EventType { get; set; } = "";
            public string Message { get; set; } = "";
            public string Target { get; set; } = "";
            public string UserName { get; set; } = "";
            public string IpAddress { get; set; } = "";
            public DateTime CreatedUtc { get; set; }
        }

        public class IpStatEntry
        {
            public string Ip { get; set; } = "";
            public int Count { get; set; }
        }
    }
}
