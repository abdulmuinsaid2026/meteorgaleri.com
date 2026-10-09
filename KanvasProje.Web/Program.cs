global using KanvasProje.Core.Helpers;
global using KanvasProje.Service.Helpers;
global using KanvasProje.Core.Models;
global using KanvasProje.Service.Interfaces;

using KanvasProje.Data;
using Microsoft.EntityFrameworkCore;
using KanvasProje.Core.Interfaces;
using KanvasProje.Data.Repositories;
using KanvasProje.Service.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using KanvasProje.Core.Varliklar;
using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using KanvasProje.Web.Attributes;
using KanvasProje.Web.Security;
using System.Net;
using System.Threading.RateLimiting;
using Serilog;
using Microsoft.AspNetCore.HttpOverrides;
using Hangfire;
using Hangfire.PostgreSql;
using Npgsql;
using System.Security.Claims;

// ============================================================
// EPPLUS LÄ°SANS AYARI (En Tepeye Eklenmeli)
// ============================================================
Environment.SetEnvironmentVariable("EPPlusLicenseContext", "NonCommercial");
// ============================================================

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("secrets.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

var startupWarnings = new List<string>();
var defaultConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrWhiteSpace(defaultConnectionString))
{
    defaultConnectionString = ConvertDatabaseUrlToNpgsql(defaultConnectionString);
}
else
{
    var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL")
        ?? builder.Configuration["DATABASE_URL"];
    if (!string.IsNullOrWhiteSpace(databaseUrl))
    {
        defaultConnectionString = ConvertDatabaseUrlToNpgsql(databaseUrl);
    }
}
var isDatabaseAvailableAtStartup = CanConnectToPostgres(defaultConnectionString, out var databaseAvailabilityError);

if (!isDatabaseAvailableAtStartup && !string.IsNullOrWhiteSpace(databaseAvailabilityError))
{
    startupWarnings.Add($"PostgreSQL baglantisi kurulamadi. Hangfire ve zamanlanmis isler kapatildi. Detay: {databaseAvailabilityError}");
}

// 1. Veritabanı Bağlantısı
builder.Services.AddDbContext<KanvasDbContext>(options =>
{
    options.UseNpgsql(defaultConnectionString, npgsqlOptions =>
    {
        npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
    });
    options.ConfigureWarnings(warnings =>
    {
        warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning);
        warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.MultipleCollectionIncludeWarning);
    });
});

builder.Services.AddDataProtection()
    .SetApplicationName("MeteorGaleri")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys")));

// 2. Identity (Ãœyelik) Servisi
builder.Services.AddIdentity<AppUser, IdentityRole>(options => 
{
    // Åifre KurallarÄ±
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 8;
    
    // Hesap Kilitleme (Brute-Force KorumasÄ±)
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.AllowedForNewUsers = true;
})
.AddErrorDescriber<KanvasProje.Core.Helpers.TurkceIdentityErrorDescriber>()
.AddEntityFrameworkStores<KanvasDbContext>()
.AddDefaultTokenProviders();

// Google ile Giriş (sadece ClientId tanımlıysa aktif)
var googleClientId = builder.Configuration["Authentication:Google:ClientId"]
    ?? builder.Configuration["Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]
    ?? builder.Configuration["Google:ClientSecret"];

if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    builder.Services.AddAuthentication()
        .AddGoogle(options =>
        {
            options.ClientId = googleClientId;
            options.ClientSecret = googleClientSecret;
            options.Scope.Add("profile");
            options.Scope.Add("email");
        });
}

// Facebook ile GiriÅŸ (sadece AppId tanÄ±mlÄ±ysa aktif)
var facebookAppId = builder.Configuration["Authentication:Facebook:AppId"] ?? "";
if (!string.IsNullOrWhiteSpace(facebookAppId))
{
    builder.Services.AddAuthentication()
        .AddFacebook(options =>
        {
            options.AppId = facebookAppId;
            options.AppSecret = builder.Configuration["Authentication:Facebook:AppSecret"] ?? "";
            options.Scope.Add("email");
            options.Scope.Add("public_profile");
        });
}

// 3. Cookie (Ã‡erez) AyarlarÄ±
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Hesap/GirisYap";
    options.LogoutPath = "/Hesap/CikisYap";
    options.AccessDeniedPath = "/Hesap/ErisimEngellendi";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.HttpOnly = true;
    options.Events = new CookieAuthenticationEvents
    {
        OnRedirectToLogin = context => HandleAuthRedirectAsync(
            context,
            StatusCodes.Status401Unauthorized,
            "admin_auth_required",
            "Admin paneline erismek icin giris yapilmasi gerekiyor."),
        OnRedirectToAccessDenied = context => HandleAuthRedirectAsync(
            context,
            StatusCodes.Status403Forbidden,
            "admin_access_denied",
            "Admin panelinde yetkisiz erisim denemesi tespit edildi."),
        OnValidatePrincipal = async context =>
        {
            if (context.Principal?.Identity?.IsAuthenticated != true)
            {
                return;
            }

            var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
            var signInManager = context.HttpContext.RequestServices.GetRequiredService<SignInManager<AppUser>>();
            var user = await userManager.GetUserAsync(context.Principal);

            if (user == null)
            {
                context.RejectPrincipal();
                await signInManager.SignOutAsync();
                return;
            }

            var databaseRoles = await userManager.GetRolesAsync(user);
            var cookieRoles = context.Principal
                .FindAll(ClaimTypes.Role)
                .Select(x => x.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var rolesChanged = databaseRoles.Count != cookieRoles.Count ||
                databaseRoles.Any(role => !cookieRoles.Contains(role));

            if (rolesChanged)
            {
                context.ReplacePrincipal(await signInManager.CreateUserPrincipalAsync(user));
                context.ShouldRenew = true;
            }
        }
    };
});
// Serilog KonfigÃ¼rasyonu (Aktif)
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/meteorgaleri-log-.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
    .CreateLogger();
builder.Host.UseSerilog();

// Cache AltyapÄ±sÄ± Ekleme
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ICacheService, CacheService>();

// Media AltyapÄ±sÄ±
builder.Services.AddScoped<IMediaService, LocalMediaService>();

// Hangfire AltyapÄ±sÄ±
if (isDatabaseAvailableAtStartup)
{
    builder.Services.AddHangfire(configuration => configuration
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(defaultConnectionString)));

    builder.Services.AddHangfireServer();
}

builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
builder.Services.AddScoped(typeof(IService<>), typeof(Service<>));


// Email Servisini Tanıtıyoruz — Railway'de SMTP portlari bloke oldugu icin HTTPS API kullanilir
builder.Services.AddHttpClient("BrevoApi", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<KanvasProje.Core.Interfaces.IEmailService, KanvasProje.Service.Services.BrevoApiEmailService>();


// 5. Session AyarlarÄ± - SADECE BÄ°R KERE EKLEYÄ°N!
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = ".KanvasProje.Session";
});

// SEO Servisleri
builder.Services.AddScoped<ISeoService, SeoService>();
builder.Services.AddScoped<ISepetService, KanvasProje.Service.SepetService>(); // ğŸ›’ Database Cart Service
if (isDatabaseAvailableAtStartup)
{
    builder.Services.AddHostedService<AbandonedCartService>(); // 📧 Abandoned Cart Background Job
    builder.Services.AddHostedService<FavoriPriceDropService>(); // 🔔 Favori Fiyat Düşüş Bildirimi
}

builder.Services.AddScoped<ZiyaretciTakipAttribute>();

// 7. HTTP Context Accessor
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ISiteSettingsService, SiteSettingsService>();
builder.Services.AddScoped<IHomePageSettingsService, HomePageSettingsService>();
builder.Services.AddScoped<IHomePageSectionService, HomePageSectionService>();
builder.Services.AddScoped<IFavoriService, FavoriService>();
builder.Services.AddScoped<IPaymentService, IyzicoPaymentService>();
builder.Services.AddScoped<IyzicoPaymentService>();
builder.Services.AddScoped<PaytrPaymentService>();
builder.Services.AddHttpClient("Paytr", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<IAdminSecurityAuditService, AdminSecurityAuditService>();
builder.Services.AddSingleton<IAdminSessionStateService, AdminSessionStateService>();
// Health Checks (Docker / Load Balancer / Monitoring)
builder.Services.AddHealthChecks();

// Response SÄ±kÄ±ÅŸtÄ±rma (Gzip/Brotli)
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
});

// Web Encoder (Türkçe karakterlerin &#x... şeklinde bozulmasını engeller)
builder.Services.AddSingleton<System.Text.Encodings.Web.HtmlEncoder>(
    System.Text.Encodings.Web.HtmlEncoder.Create(
        System.Text.Unicode.UnicodeRanges.BasicLatin,
        System.Text.Unicode.UnicodeRanges.All));

// 8. MVC ve Session
builder.Services.AddControllersWithViews(options =>
{
    // Bu satÄ±r sayesinde siteye giren herkes otomatik kaydedilir
    options.Filters.Add<ZiyaretciTakipAttribute>(); 
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    options.MaxModelValidationErrors = 5000;
});

// 9. TÃœRKÃ‡E DÄ°L AYARLARI
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AdminPolicyNames.AdminPanelAccess, policy =>
        policy.RequireRole(AdminSecurityRoles.AllAdminRoles));
});

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { new CultureInfo("tr-TR") };
    options.DefaultRequestCulture = new RequestCulture("tr-TR");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
});

// 10. Rate Limiting (Brute-force korumasÄ±)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    // GiriÅŸ/KayÄ±t iÃ§in brute-force korumasÄ±
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
    // Genel API istekleri iÃ§in
    options.AddPolicy("general", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

// 11. Antiforgery gÃ¼venli ayarlar
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

// ==========================================
// BUILD AÅAMASI - Service Collection ArtÄ±k Read-Only!
// ==========================================
var app = builder.Build();
var runningInContainer = string.Equals(
    Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
    "true",
    StringComparison.OrdinalIgnoreCase);

foreach (var startupWarning in startupWarnings)
{
    app.Logger.LogWarning(startupWarning);
}

// --- PIPELINE (SIRALAMA Ã–NEMLÄ°) ---

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

// Cloudflare proxy vs. ForwardedHeaders - PIPELINE'IN BASINDA OLMALI!
// Cloudflare HTTPS ile alip HTTP ile uygulamaya iletir.
// X-Forwarded-Proto header'ina guvenerek HTTPS oldugunu anlariz.
var forwardedOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedOptions.KnownIPNetworks.Clear();
forwardedOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedOptions);

// Container'da (Railway) Cloudflare proxy ile HTTPS zorla
app.Use(async (context, next) =>
{
    if (runningInContainer)
    {
        // Railway'de her zaman HTTPS (Cloudflare proxy ile)
        context.Request.Scheme = "https";
    }
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts(); // HTTPS zorunluluÄŸu (production)
}

// GÃœVENLÄ°K HEADER'LARI
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});

// HttpsRedirection container icinde pasif:
// - Container'da: calismaz (runningInContainer=true)
// - Lokalde: calisir
// Cloudflare proxy ile calisirken ForwardedHeaders sayesinde HTTPS gorulur
if (!runningInContainer)
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

// Railway Persistent Storage static file serving
var persistentRoot = Environment.GetEnvironmentVariable("PERSISTENT_ROOT");
if (!string.IsNullOrWhiteSpace(persistentRoot))
{
    var storageImgPath = Path.Combine(persistentRoot, "img", "products");
    if (!Directory.Exists(storageImgPath))
    {
        try { Directory.CreateDirectory(storageImgPath); } catch { }
    }
    if (Directory.Exists(storageImgPath))
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(storageImgPath),
            RequestPath = "/img/products"
        });
    }

    var storageUploadsPath = Path.Combine(persistentRoot, "uploads");
    if (!Directory.Exists(storageUploadsPath))
    {
        try { Directory.CreateDirectory(storageUploadsPath); } catch { }
    }
    if (Directory.Exists(storageUploadsPath))
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(storageUploadsPath),
            RequestPath = "/uploads"
        });
    }
}

// Ozel Hata Sayfalari (404 vb.) - Guzel tasarimli sayfa gosterir
app.UseStatusCodePagesWithReExecute("/Hata/{0}");
app.Use(async (context, next) =>
{
    if (IsMaintenanceAllowedPath(context.Request.Path))
    {
        await next();
        return;
    }

    var siteSettingsService = context.RequestServices.GetRequiredService<ISiteSettingsService>();
    var siteSettings = siteSettingsService.GetSettings();

    if (!siteSettings.BakimModuAktif)
    {
        await next();
        return;
    }

    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
    context.Response.ContentType = "text/html; charset=utf-8";

    var siteTitle = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(siteSettings.MarkaAdi)
        ? siteSettings.SiteAdi
        : siteSettings.MarkaAdi);
    var siteMessage = WebUtility.HtmlEncode(siteSettings.BakimModuMesaji);
    var themeColor = WebUtility.HtmlEncode(siteSettings.TemaRengi);
    var logoUrl = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(siteSettings.SiteLogoUrl)
        ? "/logo_svg.svg"
        : siteSettings.SiteLogoUrl);

    await context.Response.WriteAsync($$"""
<!doctype html>
<html lang="tr">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>{{siteTitle}} | Bakım Modu</title>
    <style>
        * { box-sizing:border-box; }
        body { margin:0; font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,"Helvetica Neue",Arial,sans-serif; background:#FFFBF0; color:#1B2A4A; min-height:100vh; display:flex; align-items:center; justify-content:center; padding:24px; }
        body:before { content:""; position:fixed; inset:0; background:radial-gradient(circle at top left, rgba(1,173,211,.08), transparent 40%), linear-gradient(135deg, rgba(27,42,74,.04), transparent 50%); pointer-events:none; }
        .card { position:relative; width:min(680px,100%); background:rgba(255,255,255,.94); border:1px solid #EAE3D2; border-radius:24px; padding:48px 36px; box-shadow:0 20px 60px rgba(27,42,74,.08); text-align:center; }
        .logo { width:170px; max-width:60vw; height:auto; margin:0 auto 24px; display:block; }
        .badge { display:inline-flex; align-items:center; gap:8px; background:rgba(1,173,211,.1); color:#01ADD3; border:1px solid rgba(1,173,211,.25); padding:6px 16px; border-radius:999px; font-size:12px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; }
        .badge:before { content:""; width:7px; height:7px; border-radius:999px; background:#01ADD3; }
        h1 { margin:20px auto 14px; max-width:540px; font-size:28px; line-height:1.25; color:#1B2A4A; font-weight:700; font-family:Georgia,serif; }
        p { margin:0 auto; max-width:560px; color:#5D5B50; font-size:15px; line-height:1.75; }
        .note { margin-top:28px; padding-top:20px; border-top:1px solid #EAE3D2; color:#8C827A; font-size:13px; }
        @media (max-width:640px) { .card { padding:32px 20px; border-radius:18px; } h1 { font-size:22px; } p { font-size:14px; } }
    </style>
</head>
<body>
    <div class="card">
        <img src="{{logoUrl}}" alt="{{siteTitle}}" class="logo" onerror="this.style.display='none'">
        <span class="badge">Bakım Modu</span>
        <h1>{{siteTitle}} kısa süreliğine hazırlanıyor</h1>
        <p>{{siteMessage}}</p>
        <div class="note">Siparişleriniz, üyelik bilgileriniz ve sepetiniz güvenle korunur.</div>
    </div>
</body>
</html>
""", System.Text.Encoding.UTF8);
});
app.UseRequestLocalization();
if (!app.Environment.IsDevelopment())
{
    app.UseResponseCompression();
}
app.UseRouting();
app.UseRateLimiter();

// Ã–nce Session, Sonra Kimlik DoÄŸrulama
app.UseSession(); 
app.UseAuthentication(); // <--- GiriÅŸ yapmÄ±ÅŸ mÄ±?
app.UseAuthorization();  // <--- Yetkisi var mÄ±?

// Controller route'larÄ±
app.MapControllers();
app.MapHealthChecks("/health");

app.MapGet("/Admin", (HttpContext context) =>
{
    // Admin route'ı için yetki kontrolü - admin rolü olan kullanıcılar erişebilir
    if (context.User?.Identity?.IsAuthenticated != true)
    {
        var returnUrl = Uri.EscapeDataString("/Admin/Home/Index");
        return Results.Redirect($"/Hesap/GirisYap?returnUrl={returnUrl}");
    }

    // Kullanıcının admin rolü var mı kontrol et
    var isAdmin = AdminSecurityRoles.AllAdminRoles.Any(role => context.User.IsInRole(role));
    if (!isAdmin)
    {
        return Results.Redirect("/Hesap/ErisimEngellendi");
    }

    return Results.Redirect("/Admin/Home/Index");
});

// Hangfire ArayÃ¼zÃ¼ (Åimdilik yetkisiz eriÅŸim aÃ§Ä±k; daha sonra yetkilendirilecek)
if (isDatabaseAvailableAtStartup)
{
    app.UseHangfireDashboard("/admin/hangfire", new DashboardOptions
    {
    // Authorization filter'Ä± ÅŸimdilik null veya basit tutuyoruz ki gÃ¶rebilelim.
    Authorization = new[] { new Hangfire.Dashboard.LocalRequestsOnlyAuthorizationFilter() } 
    });
}

// 1. Admin RotasÄ±
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

// 2. Standart Rota
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// --- VERÄ°TABANI GÃœNCELLEME VE BAÅLANGIÃ‡ VERÄ°LERÄ° ---
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        // 1. VeritabanÄ± Context'ini al
        var context = services.GetRequiredService<KanvasDbContext>();

        if (isDatabaseAvailableAtStartup)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            await EnsureKnownSchemaDriftAsync(context, logger);
            
            // 3. DbSeeder sÄ±nÄ±fÄ± iÃ§in eksik katalog ÅŸemasÄ±nÄ± Ã¶nceden ekle (EF Migrations'ın ihtiyaÃ§ duyduÄŸu Slug/CerceveModeli vb. kolonları garanti eder)
            await EnsureMissingMarch2026SchemaAsync(
                context,
                logger);

            // 2. OTOMATÄ°K MIGRATION (Sihirli Kod BurasÄ±) ğŸš€
            // EÄŸer veritabanÄ± yoksa oluÅŸturur, varsa ve yeni migrationlar eklenmiÅŸse onlarÄ± uygular.
            
            // SeoDescription kolonu zaten varsa migration history'ye ekle (tekrar Ã§alÄ±ÅŸmasÄ±nÄ± engellemek iÃ§in)
            try 
            {
                await context.Database.ExecuteSqlRawAsync(@"
                    INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"") 
                    SELECT '20260504111249_AddProductSeoFields', '8.0.0' 
                    WHERE NOT EXISTS (SELECT 1 FROM ""__EFMigrationsHistory"" WHERE ""MigrationId"" = '20260504111249_AddProductSeoFields')
                    AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Urunler' AND column_name = 'SeoDescription')
                ");
            }
            catch { /* Kolon yoksa veya history zaten varsa Ã¶nemsiz */ }
            
            try 
            { 
                context.Database.Migrate(); 
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Otomatik EF Core migrate bildirim verdi. Veritabanı şeması EnsureMissingMarch2026Schema ile başarıyla doğrulandı.");
            }

            await KanvasProje.Web.Data.DbSeeder.VerileriYukle(app);
        }
        else
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogWarning("VeritabanÄ±na eriÅŸilemediÄŸi iÃ§in migration ve seed adÄ±mlarÄ± atlandÄ±.");
        }
    }
    catch (Exception ex)
    {
        // OlasÄ± bir hatada konsola yazdÄ±ralÄ±m ki gÃ¶rebilelim
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "VeritabanÄ± migration iÅŸlemi sÄ±rasÄ±nda bir hata oluÅŸtu!");
    }
}

app.Run();

static bool IsMaintenanceAllowedPath(PathString path)
{
    var value = path.Value ?? string.Empty;

    return value.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("/hesap", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("/api/admin", StringComparison.OrdinalIgnoreCase);
}

static bool IsAdminSecuredPath(PathString path)
{
    var value = path.Value ?? string.Empty;

    return value.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("/api/admin", StringComparison.OrdinalIgnoreCase);
}

static bool IsAdminApiPath(PathString path)
{
    var value = path.Value ?? string.Empty;

    return value.StartsWith("/api/admin", StringComparison.OrdinalIgnoreCase);
}

static async Task HandleAuthRedirectAsync(
    RedirectContext<CookieAuthenticationOptions> context,
    int statusCode,
    string eventType,
    string message)
{
    if (IsAdminSecuredPath(context.Request.Path))
    {
        var auditService = context.HttpContext.RequestServices.GetService<IAdminSecurityAuditService>();
        if (auditService != null)
        {
            await auditService.LogAsync(
                context.HttpContext,
                eventType,
                message,
                context.Request.Path.Value);
        }

        if (IsAdminApiPath(context.Request.Path))
        {
            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsJsonAsync(new
            {
                error = statusCode == StatusCodes.Status401Unauthorized ? "auth_required" : "forbidden"
            });
            return;
        }
    }

    context.Response.Redirect(context.RedirectUri);
}

static string ConvertDatabaseUrlToNpgsql(string? databaseUrl)
{
    if (string.IsNullOrWhiteSpace(databaseUrl)) return "";

    var cleaned = databaseUrl.Trim().Trim('"', '\'').Trim();
    if (cleaned.StartsWith("Host=", StringComparison.OrdinalIgnoreCase) ||
        cleaned.StartsWith("Server=", StringComparison.OrdinalIgnoreCase))
    {
        return cleaned;
    }

    try
    {
        var uri = new Uri(cleaned);
        var userInfo = uri.UserInfo.Split(':');
        var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "";
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 5432;
        var database = uri.AbsolutePath.TrimStart('/');
        return $"Host={host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Prefer;Trust Server Certificate=true;Include Error Detail=true";
    }
    catch
    {
        return cleaned;
    }
}

static bool CanConnectToPostgres(string? connectionString, out string? errorMessage)
{
    errorMessage = null;

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        errorMessage = "DefaultConnection ayarlanmamis.";
        return false;
    }

    try
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        return true;
    }
    catch (Exception ex)
    {
        errorMessage = ex.Message;
        return false;
    }
}

static async Task EnsureKnownSchemaDriftAsync(KanvasDbContext context, Microsoft.Extensions.Logging.ILogger<Program> logger)
{
    const string sql = """
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

DO $$
BEGIN
    IF to_regclass('public."BultenAbonelikleri"') IS NULL THEN
        CREATE TABLE "BultenAbonelikleri" (
            "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            "Email" text NOT NULL,
            "KayitTarihi" timestamp with time zone NOT NULL,
            "AktifMi" boolean NOT NULL
        );
    END IF;

    IF to_regclass('public."BultenAbonelikleri"') IS NOT NULL THEN
        ALTER TABLE "BultenAbonelikleri" ADD COLUMN IF NOT EXISTS "IpAdresi" text NULL;
    END IF;

    IF to_regclass('public."Urunler"') IS NOT NULL AND EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'Urunler' AND column_name = 'Slug'
    ) THEN
        ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "SeoTitle" text NOT NULL DEFAULT '';
        ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "SeoDescription" text NOT NULL DEFAULT '';
        ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "SeoKeywords" text NOT NULL DEFAULT '';
        EXECUTE 'CREATE INDEX IF NOT EXISTS "IX_Urunler_Slug" ON "Urunler" ("Slug") WHERE "Slug" IS NOT NULL AND "Slug" <> ''''';
    END IF;

    IF to_regclass('public."Kategoriler"') IS NOT NULL AND EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'Kategoriler' AND column_name = 'Slug'
    ) THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS "IX_Kategoriler_Slug" ON "Kategoriler" ("Slug") WHERE "Slug" IS NOT NULL AND "Slug" <> ''''';
    END IF;

    IF to_regclass('public."Siparisler"') IS NOT NULL THEN
        ALTER TABLE "Siparisler" ADD COLUMN IF NOT EXISTS "KargoFirmasi" text NULL;
        ALTER TABLE "Siparisler" ADD COLUMN IF NOT EXISTS "KargoFirmasiId" integer NULL;
    END IF;

    IF to_regclass('public."KargoFirmalari"') IS NULL THEN
        CREATE TABLE "KargoFirmalari" (
            "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            "Ad" text NOT NULL,
            "Kod" text NOT NULL,
            "LogoUrl" text NULL,
            "Telefon" text NULL,
            "TakipUrl" text NULL,
            "GondericiUnvan" text NOT NULL DEFAULT 'MeteorGaleri',
            "GondericiAdres" text NOT NULL DEFAULT '',
            "GondericiTelefon" text NOT NULL DEFAULT '',
            "AktifMi" boolean NOT NULL DEFAULT true,
            "VarsayilanMi" boolean NOT NULL DEFAULT false,
            "OlusturulmaTarihi" timestamp with time zone NOT NULL DEFAULT NOW(),
            "SilindiMi" boolean NOT NULL DEFAULT false
        );
    END IF;

    IF to_regclass('public."KargoFirmalari"') IS NOT NULL THEN
        EXECUTE 'CREATE UNIQUE INDEX IF NOT EXISTS "IX_KargoFirmalari_Kod" ON "KargoFirmalari" ("Kod")';
    END IF;

    IF to_regclass('public."SepetItems"') IS NOT NULL THEN
        ALTER TABLE "SepetItems" ADD COLUMN IF NOT EXISTS "MusteriNotu" character varying(500) NULL;
    END IF;


    IF to_regclass('public."SiparisDetaylari"') IS NOT NULL THEN
        IF EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_name = 'SiparisDetaylari' AND column_name = 'siparisId'
        ) AND NOT EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_name = 'SiparisDetaylari' AND column_name = 'SiparisId'
        ) THEN
            ALTER TABLE "SiparisDetaylari" RENAME COLUMN "siparisId" TO "SiparisId";
        END IF;

        ALTER TABLE "SiparisDetaylari" ADD COLUMN IF NOT EXISTS "MusteriNotu" character varying(500) NULL;
        ALTER TABLE "SiparisDetaylari" ALTER COLUMN "UrunSecenekId" DROP NOT NULL;
    END IF;
END
$$;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260131211352_BultenTablosu', '8.0.0'
WHERE NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260131211352_BultenTablosu')
  AND EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'BultenAbonelikleri');

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260131213049_BultenIpEklendi', '8.0.0'
WHERE NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260131213049_BultenIpEklendi')
  AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'BultenAbonelikleri' AND column_name = 'IpAdresi');

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260504111249_AddProductSeoFields', '8.0.0'
WHERE NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260504111249_AddProductSeoFields')
  AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Urunler' AND column_name = 'SeoTitle')
  AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Urunler' AND column_name = 'SeoDescription')
  AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Urunler' AND column_name = 'SeoKeywords')
  AND EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'KargoFirmalari');

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260508175141_MusteriNotuAlanlariEklendi', '8.0.0'
WHERE NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260508175141_MusteriNotuAlanlariEklendi')
  AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'SepetItems' AND column_name = 'MusteriNotu')
  AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'SiparisDetaylari' AND column_name = 'MusteriNotu');

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260508204726_Fix_NullableUrunSecenekId_And_SlugIndex', '8.0.0'
WHERE NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260508204726_Fix_NullableUrunSecenekId_And_SlugIndex')
  AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'SiparisDetaylari' AND column_name = 'UrunSecenekId' AND is_nullable = 'YES');

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260523223159_AddFavoriPriceDropFields', '8.0.0'
WHERE NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260523223159_AddFavoriPriceDropFields')
  AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Favoriler' AND column_name = 'FiyatDustugundaBildir')
  AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Favoriler' AND column_name = 'EskiFiyat')
  AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Favoriler' AND column_name = 'SonBildirimTarihi');
""";

    await context.Database.ExecuteSqlRawAsync(sql);
    logger.LogInformation("Bilinen schema drift kontrolleri tamamlandi.");
}

static async Task EnsureMissingMarch2026SchemaAsync(KanvasDbContext context, Microsoft.Extensions.Logging.ILogger<Program> logger)
{
    const string sql = """
DO $$
BEGIN
    ALTER TABLE "Kategoriler" ADD COLUMN IF NOT EXISTS "AltMetin" text NOT NULL DEFAULT '';
    ALTER TABLE "Kategoriler" ADD COLUMN IF NOT EXISTS "BannerUrl" text NULL;
    ALTER TABLE "Kategoriler" ADD COLUMN IF NOT EXISTS "KampanyaEtiketi" text NOT NULL DEFAULT '';
    ALTER TABLE "Kategoriler" ADD COLUMN IF NOT EXISTS "KisaAciklama" text NOT NULL DEFAULT '';
    ALTER TABLE "Kategoriler" ADD COLUMN IF NOT EXISTS "ParentKategoriId" integer NULL;
    ALTER TABLE "Kategoriler" ADD COLUMN IF NOT EXISTS "SeoDescription" text NOT NULL DEFAULT '';
    ALTER TABLE "Kategoriler" ADD COLUMN IF NOT EXISTS "SeoTitle" text NOT NULL DEFAULT '';
    ALTER TABLE "Kategoriler" ADD COLUMN IF NOT EXISTS "Sira" integer NOT NULL DEFAULT 0;
    ALTER TABLE "Kategoriler" ADD COLUMN IF NOT EXISTS "Slug" text NULL;
    ALTER TABLE "Kategoriler" ADD COLUMN IF NOT EXISTS "UrunSiralamaTipi" text NOT NULL DEFAULT 'manual';
    ALTER TABLE "Kategoriler" ADD COLUMN IF NOT EXISTS "UstMetin" text NOT NULL DEFAULT '';

    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "UrunTipi" text NOT NULL DEFAULT 'Genel';
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "AktifMi" boolean NOT NULL DEFAULT true;
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "AnaSayfadaGoster" boolean NOT NULL DEFAULT false;
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "BakimTalimati" text NOT NULL DEFAULT '';
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "Barkod" text NOT NULL DEFAULT '';
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "Etiketler" text NOT NULL DEFAULT '';
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "FavoriSayisi" integer NOT NULL DEFAULT 0;
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "Fiyat" numeric NOT NULL DEFAULT 0;
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "GoruntulenmeSayisi" integer NOT NULL DEFAULT 0;
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "IndirimliFiyat" numeric NULL;
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "KargoyaVerilisSuresiGun" integer NOT NULL DEFAULT 0;
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "KisaAciklama" text NOT NULL DEFAULT '';
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "KisaAd" text NOT NULL DEFAULT '';
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "KdvOrani" numeric NOT NULL DEFAULT 20;
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "KampanyaliMi" boolean NOT NULL DEFAULT false;
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "Maliyet" numeric NOT NULL DEFAULT 0;
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "MalzemeBilgisi" text NOT NULL DEFAULT '';
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "Marka" text NOT NULL DEFAULT '';
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "MaxSiparisAdedi" integer NULL;
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "MinSiparisAdedi" integer NOT NULL DEFAULT 1;
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "OneCikanMi" boolean NOT NULL DEFAULT false;
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "PaketlemeBilgisi" text NOT NULL DEFAULT '';
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "SatisSayisi" integer NOT NULL DEFAULT 0;
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "SKU" text NOT NULL DEFAULT '';
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "Sira" integer NOT NULL DEFAULT 0;
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "StokDurumu" text NOT NULL DEFAULT 'Stokta';
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "TeknikOzellikler" text NOT NULL DEFAULT '';
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "TahminiTeslimSuresiGun" integer NOT NULL DEFAULT 0;
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "UretimSuresiGun" integer NOT NULL DEFAULT 0;
    ALTER TABLE "Urunler" ADD COLUMN IF NOT EXISTS "YeniUrunMu" boolean NOT NULL DEFAULT false;

    ALTER TABLE "UrunSecenekleri" ADD COLUMN IF NOT EXISTS "AktifMi" boolean NOT NULL DEFAULT true;
    ALTER TABLE "UrunSecenekleri" ADD COLUMN IF NOT EXISTS "CerceveKalinligi" text NOT NULL DEFAULT '';
    ALTER TABLE "UrunSecenekleri" ADD COLUMN IF NOT EXISTS "CerceveRengi" text NOT NULL DEFAULT '';
    ALTER TABLE "UrunSecenekleri" ADD COLUMN IF NOT EXISTS "Desi" numeric NOT NULL DEFAULT 0;
    ALTER TABLE "UrunSecenekleri" ADD COLUMN IF NOT EXISTS "FiyatFarki" numeric NOT NULL DEFAULT 0;
    ALTER TABLE "UrunSecenekleri" ADD COLUMN IF NOT EXISTS "GorselUrl" text NOT NULL DEFAULT '';
    ALTER TABLE "UrunSecenekleri" ADD COLUMN IF NOT EXISTS "KisilestirmeMetni" text NOT NULL DEFAULT '';
    ALTER TABLE "UrunSecenekleri" ADD COLUMN IF NOT EXISTS "MalzemeTuru" text NOT NULL DEFAULT '';
    ALTER TABLE "UrunSecenekleri" ADD COLUMN IF NOT EXISTS "OnSipariseAcikMi" boolean NOT NULL DEFAULT false;
    ALTER TABLE "UrunSecenekleri" ADD COLUMN IF NOT EXISTS "OzelTasarimNotu" text NOT NULL DEFAULT '';
    ALTER TABLE "UrunSecenekleri" ADD COLUMN IF NOT EXISTS "ParcaSayisi" integer NOT NULL DEFAULT 1;
    ALTER TABLE "UrunSecenekleri" ADD COLUMN IF NOT EXISTS "Sira" integer NOT NULL DEFAULT 0;
    ALTER TABLE "UrunSecenekleri" ADD COLUMN IF NOT EXISTS "TukeninceGizle" boolean NOT NULL DEFAULT false;
    ALTER TABLE "UrunSecenekleri" ADD COLUMN IF NOT EXISTS "UretimSuresiGun" integer NOT NULL DEFAULT 0;
    ALTER TABLE "UrunSecenekleri" ADD COLUMN IF NOT EXISTS "VaryantSku" text NOT NULL DEFAULT '';
    ALTER TABLE "UrunSecenekleri" ADD COLUMN IF NOT EXISTS "VarsayilanMi" boolean NOT NULL DEFAULT false;
    ALTER TABLE "UrunSecenekleri" ADD COLUMN IF NOT EXISTS "Yon" text NOT NULL DEFAULT '';

    ALTER TABLE "SepetItems" ADD COLUMN IF NOT EXISTS "CerceveModeli" text NOT NULL DEFAULT '';
    ALTER TABLE "SiparisDetaylari" ADD COLUMN IF NOT EXISTS "CerceveModeli" text NOT NULL DEFAULT '';
    ALTER TABLE "SiparisDetaylari" ADD COLUMN IF NOT EXISTS "SecenekAdi" text;
    ALTER TABLE "SiteAyarlari" ADD COLUMN IF NOT EXISTS "HaliMetrekareFiyati" numeric NOT NULL DEFAULT 1250;
    ALTER TABLE "SiteAyarlari" ADD COLUMN IF NOT EXISTS "DuvarKagidiMetrekareFiyati" numeric NOT NULL DEFAULT 450;

    CREATE TABLE IF NOT EXISTS "UrunOzellikTanimlari" (
        "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
        "Ad" text NOT NULL,
        "Kod" text NOT NULL,
        "UrunTipi" text NOT NULL,
        "AlanTipi" text NOT NULL,
        "YardimMetni" text NOT NULL,
        "Secenekler" text NOT NULL,
        "FiltredeGoster" boolean NOT NULL,
        "DetaySayfasindaGoster" boolean NOT NULL,
        "TeknikTablodaGoster" boolean NOT NULL,
        "AktifMi" boolean NOT NULL,
        "Sira" integer NOT NULL,
        "OlusturulmaTarihi" timestamp with time zone NOT NULL,
        "SilindiMi" boolean NOT NULL
    );

    CREATE TABLE IF NOT EXISTS "UrunOzellikDegerleri" (
        "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
        "UrunId" integer NOT NULL REFERENCES "Urunler"("Id") ON DELETE CASCADE,
        "UrunOzellikTanimiId" integer NOT NULL REFERENCES "UrunOzellikTanimlari"("Id") ON DELETE CASCADE,
        "Deger" text NOT NULL,
        "OlusturulmaTarihi" timestamp with time zone NOT NULL,
        "SilindiMi" boolean NOT NULL
    );

    ALTER TABLE "UrunResimleri" ADD COLUMN IF NOT EXISTS "AltMetin" text NOT NULL DEFAULT '';
    ALTER TABLE "UrunResimleri" ADD COLUMN IF NOT EXISTS "Baslik" text NOT NULL DEFAULT '';
    ALTER TABLE "UrunResimleri" ADD COLUMN IF NOT EXISTS "Etiketler" text NOT NULL DEFAULT '';
    ALTER TABLE "UrunResimleri" ADD COLUMN IF NOT EXISTS "MedyaAlani" text NOT NULL DEFAULT 'Galeri';
    ALTER TABLE "UrunResimleri" ADD COLUMN IF NOT EXISTS "MedyaTipi" text NOT NULL DEFAULT 'Gorsel';
    ALTER TABLE "UrunResimleri" ADD COLUMN IF NOT EXISTS "MobilResimYolu" text NOT NULL DEFAULT '';
    ALTER TABLE "UrunResimleri" ADD COLUMN IF NOT EXISTS "Sira" integer NOT NULL DEFAULT 0;
    ALTER TABLE "UrunResimleri" ADD COLUMN IF NOT EXISTS "ThumbnailYolu" text NOT NULL DEFAULT '';
    ALTER TABLE "UrunResimleri" ADD COLUMN IF NOT EXISTS "UrunSecenekId" integer NULL;
    ALTER TABLE "UrunResimleri" ADD COLUMN IF NOT EXISTS "VideoUrl" text NOT NULL DEFAULT '';

    -- Favori fiyat düşüş bildirimi alanları
    ALTER TABLE "Favoriler" ADD COLUMN IF NOT EXISTS "FiyatDustugundaBildir" boolean NOT NULL DEFAULT false;
    ALTER TABLE "Favoriler" ADD COLUMN IF NOT EXISTS "EskiFiyat" numeric NULL;
    ALTER TABLE "Favoriler" ADD COLUMN IF NOT EXISTS "SonBildirimTarihi" timestamp with time zone NULL;

    -- Slayt mobil medya ve CTA alanlari
    ALTER TABLE "Slaytlar" ADD COLUMN IF NOT EXISTS "MobilResimUrl" text NULL;
    ALTER TABLE "Slaytlar" ADD COLUMN IF NOT EXISTS "MobilVideoUrl" text NULL;
    ALTER TABLE "Slaytlar" ADD COLUMN IF NOT EXISTS "ButonYazi" text NULL;
    ALTER TABLE "Slaytlar" ADD COLUMN IF NOT EXISTS "ButonUrl" text NULL;

    -- Iyzico Odeme Ayarlari
    ALTER TABLE "SiteAyarlari" ADD COLUMN IF NOT EXISTS "IyzicoAktifMi" boolean NOT NULL DEFAULT false;
    ALTER TABLE "SiteAyarlari" ADD COLUMN IF NOT EXISTS "IyzicoTestModu" boolean NOT NULL DEFAULT true;
    ALTER TABLE "SiteAyarlari" ADD COLUMN IF NOT EXISTS "IyzicoApiKey" text NOT NULL DEFAULT '';
    ALTER TABLE "SiteAyarlari" ADD COLUMN IF NOT EXISTS "IyzicoSecretKeyProtected" text NOT NULL DEFAULT '';
    ALTER TABLE "SiteAyarlari" ADD COLUMN IF NOT EXISTS "IyzicoBaseUrl" text NOT NULL DEFAULT 'https://sandbox-api.iyzipay.com';
    ALTER TABLE "SiteAyarlari" ADD COLUMN IF NOT EXISTS "IyzicoCallbackUrl" text NOT NULL DEFAULT '';


    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.table_constraints
        WHERE constraint_schema = 'public'
          AND table_name = 'Kategoriler'
          AND constraint_name = 'FK_Kategoriler_Kategoriler_ParentKategoriId'
    ) THEN
        ALTER TABLE "Kategoriler"
            ADD CONSTRAINT "FK_Kategoriler_Kategoriler_ParentKategoriId"
            FOREIGN KEY ("ParentKategoriId") REFERENCES "Kategoriler"("Id") ON DELETE RESTRICT;
    END IF;
END
$$;

CREATE INDEX IF NOT EXISTS "IX_Kategoriler_ParentKategoriId" ON "Kategoriler" ("ParentKategoriId");
CREATE INDEX IF NOT EXISTS "IX_Kategoriler_Slug" ON "Kategoriler" ("Slug");
CREATE INDEX IF NOT EXISTS "IX_Urunler_SKU" ON "Urunler" ("SKU");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_UrunOzellikTanimlari_UrunTipi_Kod" ON "UrunOzellikTanimlari" ("UrunTipi", "Kod");
CREATE INDEX IF NOT EXISTS "IX_UrunOzellikDegerleri_UrunId" ON "UrunOzellikDegerleri" ("UrunId");
CREATE INDEX IF NOT EXISTS "IX_UrunOzellikDegerleri_UrunOzellikTanimiId" ON "UrunOzellikDegerleri" ("UrunOzellikTanimiId");
CREATE INDEX IF NOT EXISTS "IX_UrunResimleri_UrunId_Sira" ON "UrunResimleri" ("UrunId", "Sira");

UPDATE "UrunResimleri"
SET
    "Baslik" = CASE WHEN COALESCE("Baslik", '') = '' THEN 'Galeri' ELSE "Baslik" END,
    "ThumbnailYolu" = CASE WHEN COALESCE("ThumbnailYolu", '') = '' THEN "ResimYolu" ELSE "ThumbnailYolu" END,
    "Sira" = CASE WHEN "Sira" = 0 THEN "Id" ELSE "Sira" END;

UPDATE "Urunler" u
SET "Fiyat" = src."Fiyat"
FROM (
    SELECT "UrunId", MIN("SatisFiyati") AS "Fiyat"
    FROM "UrunSecenekleri"
    WHERE "SatisFiyati" > 0
    GROUP BY "UrunId"
) src
WHERE src."UrunId" = u."Id"
  AND COALESCE(u."Fiyat", 0) = 0;
""";

    await context.Database.ExecuteSqlRawAsync(sql);
    logger.LogInformation("Eksik Mart 2026 katalog semasi kontrol edildi.");
}
