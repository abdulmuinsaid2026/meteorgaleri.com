using System.Text;
using KanvasProje.Core.Models;
using KanvasProje.Core.Varliklar;
using KanvasProje.Data;
using KanvasProje.Service.Services;
using KanvasProje.Web.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanvasProje.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class KullaniciController : AdminBaseController
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly KanvasDbContext _context;
        private readonly IAdminSessionStateService _adminSessionStateService;
        private readonly IAdminSecurityAuditService _auditService;

        public KullaniciController(
            UserManager<AppUser> userManager,
            KanvasDbContext context,
            IAdminSessionStateService adminSessionStateService,
            IAdminSecurityAuditService auditService)
        {
            _userManager = userManager;
            _context = context;
            _adminSessionStateService = adminSessionStateService;
            _auditService = auditService;
        }

        public async Task<IActionResult> Index(string? search)
        {
            var query = _userManager.Users.AsNoTracking().AsQueryable();
            var term = search?.Trim();

            if (!string.IsNullOrWhiteSpace(term))
            {
                var normalized = term.ToLower();
                query = query.Where(u =>
                    (u.AdSoyad ?? string.Empty).ToLower().Contains(normalized) ||
                    (u.Email ?? string.Empty).ToLower().Contains(normalized) ||
                    (u.PhoneNumber ?? string.Empty).Contains(normalized));
            }

            var users = await query
                .OrderByDescending(u => u.Id)
                .ToListAsync();

            var userRolesLookup = await GetUserRolesLookupAsync(users.Select(x => x.Id));
            var states = await _adminSessionStateService.GetStatesAsync(users.Select(x => x.Id));
            var currentUserId = _userManager.GetUserId(User);
            var items = new List<KullaniciListItemViewModel>(users.Count);

            foreach (var user in users)
            {
                userRolesLookup.TryGetValue(user.Id, out var roles);
                var primaryRole = roles == null || roles.Count == 0
                    ? AdminSecurityRoles.Uye
                    : AdminSecurityRoles.GetPrimaryRole(roles);

                states.TryGetValue(user.Id, out var sessionState);

                items.Add(new KullaniciListItemViewModel
                {
                    Id = user.Id,
                    AdSoyad = string.IsNullOrWhiteSpace(user.AdSoyad) ? (user.Email ?? "Adsız kullanıcı") : user.AdSoyad,
                    Email = user.Email ?? string.Empty,
                    PhoneNumber = user.PhoneNumber ?? string.Empty,
                    Sehir = user.Sehir ?? string.Empty,
                    PrimaryRole = primaryRole,
                    RoleLabel = AdminSecurityRoles.GetRoleLabel(primaryRole),
                    IsAdmin = AdminSecurityRoles.IsAdminRole(primaryRole),
                    IsBanned = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow,
                    IsCurrentUser = string.Equals(user.Id, currentUserId, StringComparison.Ordinal),
                    LastAdminLoginUtc = sessionState?.CurrentLoginUtc,
                    PreviousAdminLoginUtc = sessionState?.PreviousLoginUtc
                });
            }

            var model = new KullaniciIndexViewModel
            {
                Search = term ?? string.Empty,
                TotalCount = items.Count,
                AdminCount = items.Count(x => x.IsAdmin),
                CustomerCount = items.Count(x => !x.IsAdmin),
                Kullanicilar = items
                    .OrderByDescending(x => x.IsAdmin)
                    .ThenBy(x => x.AdSoyad)
                    .ToList()
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DurumDegistir(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            if (string.Equals(user.Id, _userManager.GetUserId(User), StringComparison.Ordinal))
            {
                TempData["Hata"] = "Kendi hesabınızın durumunu bu ekrandan değiştiremezsiniz.";
                return RedirectToAction(nameof(Index));
            }

            var targetRoles = await _userManager.GetRolesAsync(user);
            if (IsSuperAdminRoleSet(targetRoles) && !IsCallerSuperAdmin())
            {
                TempData["Hata"] = "Süper Admin hesabının durumunu değiştirme yetkiniz yok.";
                return RedirectToAction(nameof(Index));
            }

            var isBanned = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow;
            if (!isBanned && IsSuperAdminRoleSet(targetRoles) && !await HasAnotherActiveSuperAdminAsync(user.Id))
            {
                TempData["Hata"] = "Sistemdeki son aktif Süper Admin hesabı engellenemez.";
                return RedirectToAction(nameof(Index));
            }

            user.LockoutEnabled = true;
            user.LockoutEnd = isBanned ? null : DateTimeOffset.UtcNow.AddYears(100);

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                TempData["Hata"] = string.Join(" ", result.Errors.Select(x => x.Description));
                return RedirectToAction(nameof(Index));
            }

            await _userManager.UpdateSecurityStampAsync(user);
            await _adminSessionStateService.ClearSessionAsync(user.Id);

            await _auditService.LogAsync(
                HttpContext,
                isBanned ? "user_unblocked" : "user_blocked",
                isBanned ? "Kullanıcı engeli kaldırıldı." : "Kullanıcı engellendi.",
                target: user.Id);

            TempData["Basari"] = isBanned
                ? "Kullanıcı engeli kaldırıldı."
                : "Kullanıcı engellendi.";

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> SifreSifirla(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                TempData["Hata"] = "Şifresi değiştirilecek kullanıcı seçilmedi.";
                return RedirectToAction(nameof(Index));
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                TempData["Hata"] = "Kullanıcı bulunamadı.";
                return RedirectToAction(nameof(Index));
            }

            var targetRoles = await _userManager.GetRolesAsync(user);
            if (IsSuperAdminRoleSet(targetRoles) && !IsCallerSuperAdmin())
            {
                TempData["Hata"] = "Süper Admin hesabının şifresini sıfırlama yetkiniz yok.";
                return RedirectToAction(nameof(Index));
            }

            return View(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SifreSifirla(string id, string yeniSifre, string sifreTekrar)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                TempData["Hata"] = "Kullanıcı bulunamadı.";
                return RedirectToAction(nameof(Index));
            }

            var targetRoles = await _userManager.GetRolesAsync(user);
            if (IsSuperAdminRoleSet(targetRoles) && !IsCallerSuperAdmin())
            {
                TempData["Hata"] = "Süper Admin hesabının şifresini sıfırlama yetkiniz yok.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(yeniSifre))
            {
                TempData["Hata"] = "Yeni şifre boş bırakılamaz.";
                return View(user);
            }

            if (!string.Equals(yeniSifre, sifreTekrar, StringComparison.Ordinal))
            {
                TempData["Hata"] = "Yeni şifre ve tekrar alanı aynı olmalıdır.";
                return View(user);
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, yeniSifre);

            if (!result.Succeeded)
            {
                TempData["Hata"] = string.Join(" ", result.Errors.Select(x => x.Description));
                return View(user);
            }

            await _userManager.UpdateSecurityStampAsync(user);
            await _adminSessionStateService.ClearSessionAsync(user.Id);

            await _auditService.LogAsync(
                HttpContext,
                "user_password_reset",
                "Kullanıcı şifresi admin tarafından sıfırlandı.",
                target: user.Id);

            TempData["Basari"] = "Şifre başarıyla güncellendi.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Duzenle(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                TempData["Hata"] = "Düzenlenecek kullanıcı seçilmedi.";
                return RedirectToAction(nameof(Index));
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                TempData["Hata"] = "Kullanıcı bulunamadı.";
                return RedirectToAction(nameof(Index));
            }

            var targetRoles = await _userManager.GetRolesAsync(user);
            if (IsSuperAdminRoleSet(targetRoles) && !IsCallerSuperAdmin())
            {
                TempData["Hata"] = "Süper Admin hesabını düzenleme yetkiniz yok.";
                return RedirectToAction(nameof(Index));
            }

            return View(await BuildEditModelAsync(user));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Duzenle(KullaniciDuzenleViewModel model)
        {
            var user = await _userManager.FindByIdAsync(model.Id);
            if (user == null)
            {
                return NotFound();
            }

            var currentRoles = await _userManager.GetRolesAsync(user);
            if (IsSuperAdminRoleSet(currentRoles) && !IsCallerSuperAdmin())
            {
                TempData["Hata"] = "Süper Admin hesabını düzenleme yetkiniz yok.";
                return RedirectToAction(nameof(Index));
            }

            var currentPrimaryRole = currentRoles.Count == 0
                ? AdminSecurityRoles.Uye
                : AdminSecurityRoles.GetPrimaryRole(currentRoles);
            var currentEditableRole = NormalizeEditableRole(currentPrimaryRole);

            var selectedRole = string.IsNullOrWhiteSpace(model.SelectedRole)
                ? AdminSecurityRoles.Uye
                : model.SelectedRole;
            var allowedRoles = GetCallerAssignableRoleOptions()
                .Select(x => x.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!allowedRoles.Contains(selectedRole))
            {
                ModelState.AddModelError(string.Empty, "Seçilen rol geçersiz veya bu rolü atama yetkiniz yok.");
                var invalidRoleModel = await BuildEditModelAsync(user);
                invalidRoleModel.SelectedRole = currentEditableRole;
                return View(invalidRoleModel);
            }

            var currentUserId = _userManager.GetUserId(User);
            if (string.Equals(user.Id, currentUserId, StringComparison.Ordinal) &&
                !string.Equals(selectedRole, currentEditableRole, StringComparison.OrdinalIgnoreCase))
            {
                TempData["Hata"] = "Kendi rolünüzü bu ekrandan değiştiremezsiniz.";
                return RedirectToAction(nameof(Duzenle), new { id = user.Id });
            }

            var roleChanged = !string.Equals(currentEditableRole, selectedRole, StringComparison.OrdinalIgnoreCase);
            if (roleChanged &&
                string.Equals(currentEditableRole, AdminSecurityRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase) &&
                !await HasAnotherActiveSuperAdminAsync(user.Id))
            {
                TempData["Hata"] = "Sistemdeki son aktif Süper Admin hesabının rolü düşürülemez.";
                return RedirectToAction(nameof(Duzenle), new { id = user.Id });
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            user.AdSoyad = model.AdSoyad?.Trim() ?? string.Empty;
            user.PhoneNumber = model.PhoneNumber?.Trim();
            user.Sehir = model.Sehir?.Trim() ?? string.Empty;

            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                await transaction.RollbackAsync();
                AddErrors(updateResult);
                var failedModel = await BuildEditModelAsync(user);
                failedModel.SelectedRole = selectedRole;
                return View(failedModel);
            }

            var rolesToRemove = currentRoles
                .Where(x => AdminSecurityRoles.IsAdminRole(x) || string.Equals(x, AdminSecurityRoles.Uye, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (rolesToRemove.Count > 0)
            {
                var removeResult = await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
                if (!removeResult.Succeeded)
                {
                    await transaction.RollbackAsync();
                    AddErrors(removeResult);
                    var failedModel = await BuildEditModelAsync(user);
                    failedModel.SelectedRole = currentEditableRole;
                    return View(failedModel);
                }
            }

            var addRoleResult = await _userManager.AddToRoleAsync(user, selectedRole);
            if (!addRoleResult.Succeeded)
            {
                await transaction.RollbackAsync();
                AddErrors(addRoleResult);
                var failedModel = await BuildEditModelAsync(user);
                failedModel.SelectedRole = currentEditableRole;
                return View(failedModel);
            }

            if (roleChanged)
            {
                await _userManager.UpdateSecurityStampAsync(user);
            }

            await transaction.CommitAsync();

            if (roleChanged)
            {
                await _adminSessionStateService.ClearSessionAsync(user.Id);

                await _auditService.LogAsync(
                    HttpContext,
                    "user_role_updated",
                    $"Kullanıcı rolü {AdminSecurityRoles.GetRoleLabel(currentEditableRole)} rolünden {AdminSecurityRoles.GetRoleLabel(selectedRole)} rolüne güncellendi.",
                    target: user.Id);
            }

            TempData["Basari"] = roleChanged
                ? "Kullanıcı bilgileri ve rolü güncellendi."
                : "Kullanıcı bilgileri güncellendi.";

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Sil(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return RedirectToAction(nameof(Index));
            }

            if (string.Equals(user.Id, _userManager.GetUserId(User), StringComparison.Ordinal))
            {
                TempData["Hata"] = "Kendi hesabınızı silemezsiniz.";
                return RedirectToAction(nameof(Index));
            }

            var targetRoles = await _userManager.GetRolesAsync(user);
            if (IsSuperAdminRoleSet(targetRoles))
            {
                if (!IsCallerSuperAdmin())
                {
                    TempData["Hata"] = "Süper Admin hesabını silme yetkiniz yok.";
                    return RedirectToAction(nameof(Index));
                }

                if (!await HasAnotherActiveSuperAdminAsync(user.Id))
                {
                    TempData["Hata"] = "Sistemdeki son aktif Süper Admin hesabı silinemez.";
                    return RedirectToAction(nameof(Index));
                }
            }

            await _adminSessionStateService.ClearSessionAsync(user.Id);
            var result = await _userManager.DeleteAsync(user);

            if (!result.Succeeded)
            {
                TempData["Hata"] = string.Join(" ", result.Errors.Select(x => x.Description));
                return RedirectToAction(nameof(Index));
            }

            await _auditService.LogAsync(
                HttpContext,
                "user_deleted",
                "Kullanıcı kaydı silindi.",
                target: user.Id);

            TempData["Basari"] = "Kullanıcı kaydı silindi.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> ExcelExport()
        {
            var users = await _userManager.Users.AsNoTracking().ToListAsync();
            var userRolesLookup = await GetUserRolesLookupAsync(users.Select(x => x.Id));
            var states = await _adminSessionStateService.GetStatesAsync(users.Select(x => x.Id));
            var builder = new StringBuilder();

            builder.AppendLine("Id,AdSoyad,Email,Telefon,Sehir,Rol,SonAdminGirisi");

            foreach (var user in users)
            {
                userRolesLookup.TryGetValue(user.Id, out var roles);
                var primaryRole = roles == null || roles.Count == 0
                    ? AdminSecurityRoles.Uye
                    : AdminSecurityRoles.GetPrimaryRole(roles);

                states.TryGetValue(user.Id, out var sessionState);
                var lastLogin = sessionState?.CurrentLoginUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;

                builder.AppendLine(string.Join(",",
                    EscapeCsv(user.Id),
                    EscapeCsv(user.AdSoyad),
                    EscapeCsv(user.Email),
                    EscapeCsv(user.PhoneNumber),
                    EscapeCsv(user.Sehir),
                    EscapeCsv(AdminSecurityRoles.GetRoleLabel(primaryRole)),
                    EscapeCsv(lastLogin)));
            }

            var preamble = Encoding.UTF8.GetPreamble();
            var payload = Encoding.UTF8.GetBytes(builder.ToString());
            var bytes = new byte[preamble.Length + payload.Length];
            Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
            Buffer.BlockCopy(payload, 0, bytes, preamble.Length, payload.Length);

            return File(bytes, "text/csv; charset=utf-8", "kullanicilar.csv");
        }

        private async Task<KullaniciDuzenleViewModel> BuildEditModelAsync(AppUser user)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var primaryRole = roles.Count == 0
                ? AdminSecurityRoles.Uye
                : AdminSecurityRoles.GetPrimaryRole(roles);
            var editableRole = NormalizeEditableRole(primaryRole);

            var roleOption = AdminSecurityRoles.GetRoleOption(editableRole);
            var sessionState = await _adminSessionStateService.GetStateAsync(user.Id);

            return new KullaniciDuzenleViewModel
            {
                Id = user.Id,
                AdSoyad = user.AdSoyad,
                Email = user.Email ?? string.Empty,
                PhoneNumber = user.PhoneNumber ?? string.Empty,
                Sehir = user.Sehir ?? string.Empty,
                SelectedRole = editableRole,
                CurrentRoleLabel = roleOption.Label,
                CurrentRoleDescription = roleOption.Description,
                IsCurrentUser = string.Equals(user.Id, _userManager.GetUserId(User), StringComparison.Ordinal),
                IsBanned = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow,
                LastAdminLoginUtc = sessionState?.CurrentLoginUtc,
                PreviousAdminLoginUtc = sessionState?.PreviousLoginUtc,
                RoleOptions = GetCallerAssignableRoleOptions().ToList()
            };
        }

        private bool IsCallerSuperAdmin()
        {
            return AdminSecurityRoles.UserHasAnyRole(
                User,
                new[] { AdminSecurityRoles.SuperAdmin, AdminSecurityRoles.LegacyAdmin });
        }

        private static bool IsSuperAdminRoleSet(IEnumerable<string> roles)
        {
            return roles.Any(r =>
                string.Equals(r, AdminSecurityRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r, AdminSecurityRoles.LegacyAdmin, StringComparison.OrdinalIgnoreCase));
        }

        private IReadOnlyList<AdminRoleOption> GetCallerAssignableRoleOptions()
        {
            var options = AdminSecurityRoles.GetAssignableRoleOptions();
            if (IsCallerSuperAdmin())
            {
                return options;
            }

            return options
                .Where(x => !string.Equals(x.Value, AdminSecurityRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private async Task<bool> HasAnotherActiveSuperAdminAsync(string excludedUserId)
        {
            var now = DateTimeOffset.UtcNow;
            return await (from ur in _context.UserRoles.AsNoTracking()
                          join r in _context.Roles.AsNoTracking() on ur.RoleId equals r.Id
                          join u in _context.Users.AsNoTracking() on ur.UserId equals u.Id
                          where u.Id != excludedUserId &&
                                (r.Name == AdminSecurityRoles.SuperAdmin || r.Name == AdminSecurityRoles.LegacyAdmin) &&
                                (!u.LockoutEnd.HasValue || u.LockoutEnd.Value <= now)
                          select u.Id)
                .AnyAsync();
        }

        private async Task<Dictionary<string, List<string>>> GetUserRolesLookupAsync(IEnumerable<string> userIds)
        {
            var ids = userIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
            if (ids.Count == 0)
            {
                return new Dictionary<string, List<string>>(StringComparer.Ordinal);
            }

            var pairs = await (from ur in _context.UserRoles.AsNoTracking()
                               join r in _context.Roles.AsNoTracking() on ur.RoleId equals r.Id
                               where ids.Contains(ur.UserId) && r.Name != null
                               select new { ur.UserId, RoleName = r.Name! })
                .ToListAsync();

            return pairs
                .GroupBy(x => x.UserId, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.RoleName).ToList(),
                    StringComparer.Ordinal);
        }

        private void AddErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }

        private static string NormalizeEditableRole(string roleName)
        {
            return string.Equals(roleName, AdminSecurityRoles.LegacyAdmin, StringComparison.OrdinalIgnoreCase)
                ? AdminSecurityRoles.SuperAdmin
                : roleName;
        }

        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var sanitized = value;
            if (sanitized.Length > 0 &&
                (sanitized[0] == '=' || sanitized[0] == '+' || sanitized[0] == '-' ||
                 sanitized[0] == '@' || sanitized[0] == '\t' || sanitized[0] == '\r'))
            {
                sanitized = "'" + sanitized;
            }

            return $"\"{sanitized.Replace("\"", "\"\"")}\"";
        }
    }
}
