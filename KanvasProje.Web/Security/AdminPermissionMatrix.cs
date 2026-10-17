using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace KanvasProje.Web.Security
{
    public static class AdminPermissionMatrix
    {
        private static readonly string[] FullAccessRoles =
        {
            AdminSecurityRoles.LegacyAdmin,
            AdminSecurityRoles.SuperAdmin,
            AdminSecurityRoles.Yonetici
        };

        private static readonly string[] DashboardRoles =
        {
            AdminSecurityRoles.LegacyAdmin,
            AdminSecurityRoles.SuperAdmin,
            AdminSecurityRoles.Yonetici,
            AdminSecurityRoles.SiparisYoneticisi,
            AdminSecurityRoles.UrunYoneticisi,
            AdminSecurityRoles.IcerikYoneticisi,
            AdminSecurityRoles.KargoYoneticisi,
            AdminSecurityRoles.Goruntuleyici
        };

        private static readonly IReadOnlyDictionary<string, string[]> ControllerPermissions =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Home"] = DashboardRoles,
                ["Search"] = DashboardRoles,
                ["Rapor"] = DashboardRoles,
                ["Ziyaretci"] = DashboardRoles,
                ["Siparis"] = new[]
                {
                    AdminSecurityRoles.LegacyAdmin,
                    AdminSecurityRoles.SuperAdmin,
                    AdminSecurityRoles.Yonetici,
                    AdminSecurityRoles.SiparisYoneticisi,
                    AdminSecurityRoles.KargoYoneticisi
                },
                ["Iade"] = new[]
                {
                    AdminSecurityRoles.LegacyAdmin,
                    AdminSecurityRoles.SuperAdmin,
                    AdminSecurityRoles.Yonetici,
                    AdminSecurityRoles.SiparisYoneticisi,
                    AdminSecurityRoles.KargoYoneticisi
                },
                ["Kargo"] = new[]
                {
                    AdminSecurityRoles.LegacyAdmin,
                    AdminSecurityRoles.SuperAdmin,
                    AdminSecurityRoles.Yonetici,
                    AdminSecurityRoles.SiparisYoneticisi,
                    AdminSecurityRoles.KargoYoneticisi
                },
                ["Urun"] = new[]
                {
                    AdminSecurityRoles.LegacyAdmin,
                    AdminSecurityRoles.SuperAdmin,
                    AdminSecurityRoles.Yonetici,
                    AdminSecurityRoles.UrunYoneticisi
                },
                ["Kategori"] = new[]
                {
                    AdminSecurityRoles.LegacyAdmin,
                    AdminSecurityRoles.SuperAdmin,
                    AdminSecurityRoles.Yonetici,
                    AdminSecurityRoles.UrunYoneticisi
                },
                ["TopluFiyatGuncelle"] = new[]
                {
                    AdminSecurityRoles.LegacyAdmin,
                    AdminSecurityRoles.SuperAdmin,
                    AdminSecurityRoles.Yonetici,
                    AdminSecurityRoles.UrunYoneticisi
                },
                ["CamTabloFiyatGuncelle"] = new[]
                {
                    AdminSecurityRoles.LegacyAdmin,
                    AdminSecurityRoles.SuperAdmin,
                    AdminSecurityRoles.Yonetici,
                    AdminSecurityRoles.UrunYoneticisi
                },
                ["SlugTool"] = new[]
                {
                    AdminSecurityRoles.LegacyAdmin,
                    AdminSecurityRoles.SuperAdmin,
                    AdminSecurityRoles.Yonetici,
                    AdminSecurityRoles.UrunYoneticisi,
                    AdminSecurityRoles.IcerikYoneticisi
                },
                ["Kupon"] = new[]
                {
                    AdminSecurityRoles.LegacyAdmin,
                    AdminSecurityRoles.SuperAdmin,
                    AdminSecurityRoles.Yonetici,
                    AdminSecurityRoles.IcerikYoneticisi
                },
                ["Yorum"] = new[]
                {
                    AdminSecurityRoles.LegacyAdmin,
                    AdminSecurityRoles.SuperAdmin,
                    AdminSecurityRoles.Yonetici,
                    AdminSecurityRoles.IcerikYoneticisi
                },
                ["Sayfa"] = new[]
                {
                    AdminSecurityRoles.LegacyAdmin,
                    AdminSecurityRoles.SuperAdmin,
                    AdminSecurityRoles.Yonetici,
                    AdminSecurityRoles.IcerikYoneticisi
                },
                ["Slayt"] = new[]
                {
                    AdminSecurityRoles.LegacyAdmin,
                    AdminSecurityRoles.SuperAdmin,
                    AdminSecurityRoles.Yonetici,
                    AdminSecurityRoles.IcerikYoneticisi
                },
                ["AnaSayfa"] = new[]
                {
                    AdminSecurityRoles.LegacyAdmin,
                    AdminSecurityRoles.SuperAdmin,
                    AdminSecurityRoles.Yonetici,
                    AdminSecurityRoles.IcerikYoneticisi
                },
                ["Bulten"] = new[]
                {
                    AdminSecurityRoles.LegacyAdmin,
                    AdminSecurityRoles.SuperAdmin,
                    AdminSecurityRoles.Yonetici,
                    AdminSecurityRoles.IcerikYoneticisi
                },
                ["Iletisim"] = new[]
                {
                    AdminSecurityRoles.LegacyAdmin,
                    AdminSecurityRoles.SuperAdmin,
                    AdminSecurityRoles.Yonetici,
                    AdminSecurityRoles.IcerikYoneticisi
                },
                ["Ayarlar"] = new[]
                {
                    AdminSecurityRoles.LegacyAdmin,
                    AdminSecurityRoles.SuperAdmin,
                    AdminSecurityRoles.Yonetici
                },
                ["Kullanici"] = new[]
                {
                    AdminSecurityRoles.LegacyAdmin,
                    AdminSecurityRoles.SuperAdmin,
                    AdminSecurityRoles.Yonetici
                },
                ["UrunOzellik"] = new[]
                {
                    AdminSecurityRoles.LegacyAdmin,
                    AdminSecurityRoles.SuperAdmin,
                    AdminSecurityRoles.Yonetici,
                    AdminSecurityRoles.UrunYoneticisi
                },
                ["UrunImport"] = new[]
                {
                    AdminSecurityRoles.LegacyAdmin,
                    AdminSecurityRoles.SuperAdmin,
                    AdminSecurityRoles.Yonetici,
                    AdminSecurityRoles.UrunYoneticisi
                },
                ["HomeSections"] = new[]
                {
                    AdminSecurityRoles.LegacyAdmin,
                    AdminSecurityRoles.SuperAdmin,
                    AdminSecurityRoles.Yonetici,
                    AdminSecurityRoles.IcerikYoneticisi
                },
                ["XyzSecretMonitor"] = FullAccessRoles
            };

        public static bool CanAccess(ClaimsPrincipal user, string? controllerName, string? httpMethod)
        {
            if (!AdminSecurityRoles.IsAdminUser(user))
            {
                return false;
            }

            if (AdminSecurityRoles.UserHasAnyRole(user, FullAccessRoles))
            {
                return true;
            }

            var allowedRoles = GetAllowedRoles(controllerName);
            if (!AdminSecurityRoles.UserHasAnyRole(user, allowedRoles))
            {
                return false;
            }

            if (!AdminSecurityRoles.IsReadOnlyAdmin(user))
            {
                return true;
            }

            var requestMethod = httpMethod ?? string.Empty;
            return HttpMethods.IsGet(requestMethod) ||
                HttpMethods.IsHead(requestMethod) ||
                HttpMethods.IsOptions(requestMethod);
        }

        public static string[] GetAllowedRoles(string? controllerName)
        {
            if (string.IsNullOrWhiteSpace(controllerName))
            {
                return DashboardRoles;
            }

            if (ControllerPermissions.TryGetValue(controllerName, out var roles))
            {
                return roles;
            }

            return FullAccessRoles;
        }

        public static bool CanViewDashboard(ClaimsPrincipal user) => CanAccess(user, "Home", HttpMethods.Get);
        public static bool CanManageOrders(ClaimsPrincipal user) => CanAccess(user, "Siparis", HttpMethods.Get);
        public static bool CanManageProducts(ClaimsPrincipal user) => CanAccess(user, "Urun", HttpMethods.Get);
        public static bool CanManageContent(ClaimsPrincipal user) => CanAccess(user, "Sayfa", HttpMethods.Get);
        public static bool CanManageUsers(ClaimsPrincipal user) => CanAccess(user, "Kullanici", HttpMethods.Get);
        public static bool CanViewReports(ClaimsPrincipal user) => CanAccess(user, "Rapor", HttpMethods.Get);
        public static bool CanManageSettings(ClaimsPrincipal user) => CanAccess(user, "Ayarlar", HttpMethods.Get);
    }
}
