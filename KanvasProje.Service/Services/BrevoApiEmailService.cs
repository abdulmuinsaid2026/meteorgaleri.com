using System.Net;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KanvasProje.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KanvasProje.Service.Services
{
    /// <summary>
    /// Brevo (Sendinblue) REST API ile e-posta gönderimi.
    /// SMTP yerine HTTPS (port 443) kullandigi icin Railway gibi
    /// cloud ortamlarinda SMTP port blokesinden etkilenmez.
    /// </summary>
    public class BrevoApiEmailService : IEmailService
    {
        private const string BrevoApiUrl = "https://api.brevo.com/v3/smtp/email";

        private readonly IConfiguration _config;
        private readonly ISiteSettingsService _siteSettingsService;
        private readonly ILogger<BrevoApiEmailService> _logger;
        private readonly HttpClient _httpClient;

        public BrevoApiEmailService(
            IConfiguration config,
            ISiteSettingsService siteSettingsService,
            ILogger<BrevoApiEmailService> logger,
            IHttpClientFactory httpClientFactory)
        {
            _config = config;
            _siteSettingsService = siteSettingsService;
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient("BrevoApi");
        }

        public async Task SendMailAsync(string to, string subject, string body)
        {
            var fromEmail = GetFromEmail();
            var fromName = GetFromName();

            if (string.IsNullOrWhiteSpace(fromEmail) || string.IsNullOrWhiteSpace(to))
                throw new InvalidOperationException("Gönderici veya alici e-posta adresi eksik.");

            var payload = new
            {
                sender = new { email = fromEmail, name = fromName },
                to = new[] { new { email = to } },
                subject,
                htmlContent = body
            };

            await SendViaApiAsync(payload);
        }

        public async Task SendTemplateMailAsync(string to, string baslik, string adSoyad, string icerik, string btnLink = "", string btnYazi = "")
        {
            var siteSettings = _siteSettingsService.GetSettings();
            var brandName = string.IsNullOrWhiteSpace(siteSettings.MarkaAdi) ? siteSettings.SiteAdi : siteSettings.MarkaAdi;
            var siteUrl = _siteSettingsService.BuildAbsoluteUrl(string.Empty);
            var logoUrl = _siteSettingsService.BuildAbsoluteUrl("/EmailTemplates/meteorgaleri-logo.png");
            var instagramUrl = string.IsNullOrWhiteSpace(siteSettings.InstagramUrl) ? siteUrl : siteSettings.InstagramUrl;
            var contactSeparator = !string.IsNullOrWhiteSpace(siteSettings.Email) && !string.IsNullOrWhiteSpace(siteSettings.Telefon)
                ? "|"
                : string.Empty;

            var emailHtml = $@"<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='margin:0; padding:0; background-color:#FDF6E3;'>
    <table role='presentation' width='100%' cellpadding='0' cellspacing='0' style='background-color:#FDF6E3; padding:30px 10px;'>
        <tr>
            <td align='center'>
                <table role='presentation' width='100%' style='max-width:600px; background-color:#ffffff; border-radius:4px; overflow:hidden; box-shadow:0 2px 12px rgba(27,42,74,.08); border-top:4px solid #01ADD3;'>
                    <tr><td style='padding:36px 40px 0; text-align:center;'>
                        <img src='{logoUrl}' alt='{brandName}' style='max-height:60px; margin-bottom:20px;' />
                        <br/>
                    </td></tr>
                    <tr><td style='padding:0 40px 20px; text-align:center;'>
                        <h1 style='font-family:Playfair Display,Georgia,serif; font-size:28px; color:#1B2A4A; margin:0;'>{WebUtility.HtmlEncode(baslik)}</h1>
                    </td></tr>
                    <tr><td style='padding:0 40px 20px;'>
                        <p style='font-family:system-ui,sans-serif; font-size:15px; color:#333333; line-height:1.7; margin:0;'>
                            Merhaba <strong>{WebUtility.HtmlEncode(adSoyad)}</strong>,
                        </p>
                        <div style='font-family:system-ui,sans-serif; font-size:15px; color:#333333; line-height:1.7; margin-top:16px;'>
                            {icerik}
                        </div>
                    </td></tr>
                    {(string.IsNullOrEmpty(btnLink) ? "" : $@"
                    <tr><td style='padding:0 40px 30px; text-align:center;'>
                        <a href='{btnLink}' style='display:inline-block; background-color:#01ADD3; color:#ffffff; padding:14px 32px; text-decoration:none; border-radius:3px; font-size:13px; font-weight:700; letter-spacing:.08em; text-transform:uppercase;'>
                            {WebUtility.HtmlEncode(btnYazi)}
                        </a>
                    </td></tr>")}
                    <tr><td style='padding:30px 40px; background-color:#FDF6E3; border-top:1px solid #F5E6C8;'>
                        <table role='presentation' width='100%' cellpadding='0' cellspacing='0'>
                            <tr>
                                <td style='text-align:center; font-family:system-ui,sans-serif; font-size:12px; color:#666666;'>
                                    <a href='{siteUrl}' style='color:#01ADD3; text-decoration:none;'>{brandName}</a>
                                    <br/>
                                    <span style='color:#999999;'>{siteSettings.Email} {contactSeparator} {siteSettings.Telefon}</span>
                                    <br/><br/>
                                    <a href='{instagramUrl}' style='color:#999999; text-decoration:none;'>Instagram</a>
                                </td>
                            </tr>
                        </table>
                    </td></tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>";

            var fromEmail = GetFromEmail();
            var fromName = GetFromName();
            if (string.IsNullOrWhiteSpace(fromEmail))
                throw new InvalidOperationException("Gönderici e-posta adresi ayarlanmamis.");

            var payload = new
            {
                sender = new { email = fromEmail, name = fromName },
                to = new[] { new { email = to, name = adSoyad } },
                subject = baslik,
                htmlContent = emailHtml
            };

            await SendViaApiAsync(payload);
        }

        public async Task<bool> SendKargoNotificationEmail(string toEmail, string musteriAdi, string siparisNo, string kargoFirmasi, string kargoTakipNo)
        {
            try
            {
                var subject = $"Siparişiniz Kargoya Verildi - {siparisNo}";
                var safeSiparisNo = WebUtility.HtmlEncode(siparisNo);
                var safeKargoFirmasi = WebUtility.HtmlEncode(kargoFirmasi);
                var safeKargoTakipNo = WebUtility.HtmlEncode(kargoTakipNo);
                var content = $@"
                    <p>Sipariş numaranız <strong>{safeSiparisNo}</strong> olan ürününüz kargoya verildi.</p>
                    <table role='presentation' width='100%' cellpadding='0' cellspacing='0' style='border:1px solid #E8DFD0; border-radius:12px; background:#FFFBF0; margin:18px 0;'>
                        <tr>
                            <td style='padding:16px; border-bottom:1px solid #e5e2dc; color:#47473d;'>
                                <strong style='color:#1B2A4A;'>Kargo Firması:</strong> {safeKargoFirmasi}
                            </td>
                        </tr>
                        <tr>
                            <td style='padding:16px; color:#47473d;'>
                                <strong style='color:#1B2A4A;'>Takip Numarası:</strong> <span style='font-size:18px; color:#01ADD3; font-weight:700;'>{safeKargoTakipNo}</span>
                            </td>
                        </tr>
                    </table>
                    <p>Kargonuzu aşağıdaki bağlantıdan takip edebilirsiniz.</p>
                    <div style='text-align:center; margin:24px 0;'>{GetKargoTrackingLink(kargoFirmasi, kargoTakipNo)}</div>";

                await SendTemplateMailAsync(toEmail, subject, musteriAdi, content, "", "");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kargo maili gonderilemedi: {SiparisNo}", siparisNo);
                return false;
            }
        }

        public async Task<bool> SendInvoiceEmailAsync(string toEmail, string musteriAdi, string siparisNo, string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    _logger.LogWarning("Fatura dosyasi bulunamadi: {FilePath}", filePath);
                    return false;
                }

                var fromEmail = GetFromEmail();
                var fromName = GetFromName();
                if (string.IsNullOrWhiteSpace(fromEmail))
                    return false;

                var fileBytes = await File.ReadAllBytesAsync(filePath);
                var fileBase64 = Convert.ToBase64String(fileBytes);
                var fileName = Path.GetFileName(filePath);

                var body = $@"
                    <div style='font-family: system-ui, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px;'>
                        <h2 style='color: #1B2A4A; margin-bottom: 20px;'>Merhaba {WebUtility.HtmlEncode(musteriAdi)},</h2>
                        <p style='color: #47473d; line-height: 1.6;'>
                            Siparişiniz için fatura hazırlanmıştır. Ekte fatura belgesini bulabilirsiniz.
                        </p>
                        <div style='background: #FFFBF0; border-radius: 12px; padding: 20px; margin: 20px 0; border: 1px solid #E8DFD0;'>
                            <p style='margin: 0;'><strong>Sipariş No:</strong> {WebUtility.HtmlEncode(siparisNo)}</p>
                        </div>
                        <p style='color: #7a766a; font-size: 14px;'>
                            Herhangi bir sorunuz olursa bizimle iletişime geçebilirsiniz.
                        </p>
                    </div>";

                var payload = new
                {
                    sender = new { email = fromEmail, name = fromName },
                    to = new[] { new { email = toEmail, name = musteriAdi } },
                    subject = $"Sipariş Faturanız Hazır - {siparisNo}",
                    htmlContent = body,
                    attachment = new[]
                    {
                        new
                        {
                            content = fileBase64,
                            name = fileName
                        }
                    }
                };

                await SendViaApiAsync(payload);
                _logger.LogInformation("Fatura maili basariyla gonderildi. SiparisNo={SiparisNo}", siparisNo);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatura maili gonderim hatasi. SiparisNo={SiparisNo}", siparisNo);
                return false;
            }
        }

        private async Task SendViaApiAsync(object payload)
        {
            var apiKey = _config["EmailSettings:Password"]
                ?? _config["EmailSettings:ApiKey"]
                ?? _config["Brevo:ApiKey"]
                ?? _config["BREVO_API_KEY"];
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Brevo API anahtari (EmailSettings:Password) eksik.");

            var json = JsonSerializer.Serialize(payload);
            var request = new HttpRequestMessage(HttpMethod.Post, BrevoApiUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("api-key", apiKey);

            _logger.LogInformation("Brevo API mail gonderiliyor...");

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Brevo API hatasi: {StatusCode} {Body}", response.StatusCode, responseBody);
                throw new InvalidOperationException($"Brevo API hatasi: {response.StatusCode} - {responseBody}");
            }

            _logger.LogInformation("Brevo API mail basariyla gonderildi.");
        }

        private string GetFromEmail()
        {
            var fromEmail = _config["EmailSettings:FromEmail"];
            if (string.IsNullOrWhiteSpace(fromEmail))
            {
                fromEmail = _siteSettingsService.GetSettings().Email;
            }
            return fromEmail;
        }

        private string GetFromName()
        {
            var fromName = _config["EmailSettings:FromName"];
            if (string.IsNullOrWhiteSpace(fromName))
            {
                var siteSettings = _siteSettingsService.GetSettings();
                fromName = string.IsNullOrWhiteSpace(siteSettings.MarkaAdi) ? siteSettings.SiteAdi : siteSettings.MarkaAdi;
            }
            return fromName;
        }

        private string GetKargoTrackingLink(string kargoFirmasi, string takipNo)
        {
            var firma = (kargoFirmasi ?? string.Empty).ToLowerInvariant();
            var encodedTakipNo = Uri.EscapeDataString(takipNo ?? string.Empty);
            var safeTakipNo = WebUtility.HtmlEncode(takipNo);

            if (firma.Contains("aras"))
                return $"<a href='https://kargotakip.araskargo.com.tr/mainpage.aspx?code={encodedTakipNo}' style='display:inline-block; background:#01ADD3; color:#ffffff; padding:14px 28px; text-decoration:none; border-radius:999px; font-size:12px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; box-shadow:0 3px 10px rgba(1,173,211,0.3);'>Aras Kargo'da Takip Et</a>";

            if (firma.Contains("yurtici") || firma.Contains("yurtiçi"))
                return $"<a href='https://www.yurticikargo.com/tr/online-servisler/gonderi-sorgula?code={encodedTakipNo}' style='display:inline-block; background:#01ADD3; color:#ffffff; padding:14px 28px; text-decoration:none; border-radius:999px; font-size:12px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; box-shadow:0 3px 10px rgba(1,173,211,0.3);'>Yurtiçi Kargo'da Takip Et</a>";

            if (firma.Contains("mng"))
                return $"<a href='https://www.mngkargo.com.tr/tracking?q={encodedTakipNo}' style='display:inline-block; background:#01ADD3; color:#ffffff; padding:14px 28px; text-decoration:none; border-radius:999px; font-size:12px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; box-shadow:0 3px 10px rgba(1,173,211,0.3);'>MNG Kargo'da Takip Et</a>";

            if (firma.Contains("ptt"))
                return $"<a href='https://gonderitakip.ptt.gov.tr/Track/Verify?q={encodedTakipNo}' style='display:inline-block; background:#01ADD3; color:#ffffff; padding:14px 28px; text-decoration:none; border-radius:999px; font-size:12px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; box-shadow:0 3px 10px rgba(1,173,211,0.3);'>PTT Kargo'da Takip Et</a>";

            return $"<div style='background:#FFFBF0; border:1px solid #E8DFD0; padding:15px; border-radius:12px; text-align:center;'><strong style='color:#1B2A4A;'>Takip No:</strong> <span style='color:#01ADD3; font-weight:700;'>{safeTakipNo}</span></div>";
        }
    }
}
