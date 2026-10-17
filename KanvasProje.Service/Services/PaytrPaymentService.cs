using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KanvasProje.Core.Models;
using KanvasProje.Service.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace KanvasProje.Service.Services
{
    /// <summary>
    /// PayTR ödeme altyapısı implementasyonu.
    /// IPaymentService arayüzünü uygular — Controller doğrudan PayTR'a bağımlı olmaz.
    ///
    /// PayTR IFrame API akışı:
    ///   1. Server-side POST ile token al (get-token)
    ///   2. Token ile iframe göster
    ///   3. Asenkron callback (bildirim URL) ile ödeme sonucu bildirimi
    ///
    /// Dökümantasyon: https://dev.paytr.com/iframe-api
    /// </summary>
    public class PaytrPaymentService : IPaymentService
    {
        private const string PaytrApiUrl = "https://www.paytr.com/odeme/api/get-token";
        private const string PaytrTestApiUrl = "https://www.paytr.com/odeme/api/get-token";
        private const string IframeBaseUrl = "https://www.paytr.com/odeme/guvenli/";
        private const string ProtectorPurpose = "Canvasia.PayTR.Settings.v1";

        private readonly ISiteSettingsService _siteSettingsService;
        private readonly IDataProtector _paytrProtector;
        private readonly ILogger<PaytrPaymentService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public PaytrPaymentService(
            ISiteSettingsService siteSettingsService,
            IDataProtectionProvider dataProtectionProvider,
            ILogger<PaytrPaymentService> logger,
            IHttpClientFactory httpClientFactory)
        {
            _siteSettingsService = siteSettingsService;
            _paytrProtector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// PayTR iframe token'ı alır.
        /// 1. Merchant bilgilerini yükler
        /// 2. Hash string oluşturur (HMAC-SHA256)
        /// 3. POST ile PayTR API'den token alır
        /// </summary>
        public async Task<PaymentInitResult> InitializeCheckoutAsync(PaymentInitRequest request)
        {
            try
            {
                var settings = _siteSettingsService.GetSettings();

                if (!settings.PaytrAktifMi)
                {
                    return new PaymentInitResult
                    {
                        Success = false,
                        ErrorMessage = "PayTR ödeme yöntemi aktif değil."
                    };
                }

                var (merchantKey, merchantSalt) = GetMerchantCredentials(settings);
                if (string.IsNullOrWhiteSpace(merchantKey) || string.IsNullOrWhiteSpace(merchantSalt))
                {
                    return new PaymentInitResult
                    {
                        Success = false,
                        ErrorMessage = "PayTR mağaza anahtarları eksik. Lütfen admin panelinden kontrol edin."
                    };
                }

                var merchantId = settings.PaytrMerchantId;
                var userIp = request.BuyerIp;
                var merchantOid = request.OrderId;
                var email = request.BuyerEmail;
                var paymentAmount = Convert.ToInt64(Math.Round(request.TotalPrice * 100));
                var userBasket = BuildUserBasket(request.BasketItems);
                var noInstallment = 0;
                var maxInstallment = 0;
                var currency = "TL";
                var testMode = settings.PaytrTestModu ? 1 : 0;

                // Hash string: merchant_id + user_ip + merchant_oid + email + payment_amount + user_basket + no_installment + max_installment + currency + test_mode
                var hashStr = string.Concat(
                    merchantId,
                    userIp,
                    merchantOid,
                    email,
                    paymentAmount.ToString(),
                    userBasket,
                    noInstallment.ToString(),
                    maxInstallment.ToString(),
                    currency,
                    testMode.ToString()
                );

                var paytrToken = ComputeHash(hashStr, merchantKey, merchantSalt);

                var formData = new Dictionary<string, string>
                {
                    ["merchant_id"] = merchantId,
                    ["user_ip"] = userIp,
                    ["merchant_oid"] = merchantOid,
                    ["email"] = email,
                    ["payment_amount"] = paymentAmount.ToString(),
                    ["currency"] = currency,
                    ["user_basket"] = userBasket,
                    ["no_installment"] = noInstallment.ToString(),
                    ["max_installment"] = maxInstallment.ToString(),
                    ["test_mode"] = testMode.ToString(),
                    ["paytr_token"] = paytrToken,
                    ["user_name"] = Truncate(request.BuyerName, 60),
                    ["user_address"] = Truncate(request.BuyerAddress, 400),
                    ["user_phone"] = Truncate(NormalizePhone(request.BuyerPhone), 20),
                    ["merchant_ok_url"] = Truncate(request.OkUrl, 400),
                    ["merchant_fail_url"] = Truncate(request.FailUrl, 400),
                    ["timeout_limit"] = "30",
                    ["lang"] = "tr",
                    ["debug_on"] = testMode.ToString(),
                };

                // Callback URL opsiyonel olarak formda gönderilmez;
                // Mağaza Panelinde ayarlanır. Ama yine de gönderelim:
                if (!string.IsNullOrWhiteSpace(settings.PaytrCallbackUrl))
                {
                    formData["callback_url"] = settings.PaytrCallbackUrl;
                }

                using var client = _httpClientFactory.CreateClient("Paytr");
                using var content = new FormUrlEncodedContent(formData);
                using var response = await client.PostAsync(PaytrApiUrl, content);

                var responseBody = await response.Content.ReadAsStringAsync();

                _logger.LogInformation(
                    "PayTR token response. OrderId={OrderId}, StatusCode={StatusCode}",
                    request.OrderId, response.StatusCode);

                var result = JsonSerializer.Deserialize<PaytrTokenResponse>(responseBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (result?.Status == "success" && !string.IsNullOrWhiteSpace(result.Token))
                {
                    _logger.LogInformation(
                        "PayTR token alındı. OrderId={OrderId}",
                        request.OrderId);

                    return new PaymentInitResult
                    {
                        Success = true,
                        Token = result.Token,
                        IframeUrl = $"{IframeBaseUrl}{result.Token}"
                    };
                }

                var errorMsg = result?.Reason ?? "PayTR token alınamadı.";
                _logger.LogWarning(
                    "PayTR token hatası. OrderId={OrderId}, Hata={Error}",
                    request.OrderId, errorMsg);

                return new PaymentInitResult
                {
                    Success = false,
                    ErrorMessage = $"PayTR: {errorMsg}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PayTR token alınırken beklenmeyen hata. OrderId={OrderId}", request.OrderId);
                return new PaymentInitResult
                {
                    Success = false,
                    ErrorMessage = "Ödeme sistemiyle bağlantı kurulamadı. Lütfen daha sonra tekrar deneyin."
                };
            }
        }

        /// <summary>
        /// PayTR bildirim (callback) hash doğrulaması.
        ///
        /// PayTR ödeme sonucunu şu formatta POST eder:
        ///   merchant_oid + merchant_salt + status + total_amount
        /// HMAC-SHA256(merchant_key, hash_str) → base64 → gelen hash ile karşılaştır
        /// </summary>
        public Task<PaymentVerifyResult> VerifyPaymentAsync(PaymentVerifyRequest request)
        {
            try
            {
                var settings = _siteSettingsService.GetSettings();
                var (merchantKey, merchantSalt) = GetMerchantCredentials(settings);

                if (string.IsNullOrWhiteSpace(merchantKey) || string.IsNullOrWhiteSpace(merchantSalt))
                {
                    _logger.LogError("PayTR doğrulama: Merchant anahtarları bulunamadı.");
                    return Task.FromResult(new PaymentVerifyResult
                    {
                        SignatureValid = false,
                        ErrorMessage = "Merchant anahtarları eksik."
                    });
                }

                // Hash doğrulama: merchant_oid + merchant_salt + status + total_amount
                var hashStr = string.Concat(
                    request.MerchantOid,
                    merchantSalt,
                    request.Status,
                    request.TotalAmount
                );

                var expectedHash = ComputeHashRaw(hashStr, merchantKey);

                if (!HashesMatch(expectedHash, request.Hash))
                {
                    _logger.LogWarning(
                        "PayTR hash doğrulama BAŞARISIZ! OrderId={OrderId}",
                        request.MerchantOid);

                    return Task.FromResult(new PaymentVerifyResult
                    {
                        SignatureValid = false,
                        ErrorMessage = "Hash doğrulaması başarısız."
                    });
                }

                _logger.LogInformation(
                    "PayTR hash doğrulama başarılı. OrderId={OrderId}, Status={Status}",
                    request.MerchantOid, request.Status);

                var isSuccess = request.Status == "success";
                var paidPrice = decimal.TryParse(request.TotalAmount, NumberStyles.Any, CultureInfo.InvariantCulture, out var paid)
                    ? paid / 100
                    : 0;

                return Task.FromResult(new PaymentVerifyResult
                {
                    SignatureValid = true,
                    PaymentSuccessful = isSuccess,
                    PaidPrice = paidPrice,
                    PaymentType = request.Status,
                    Currency = "TL"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PayTR doğrulama sırasında hata. OrderId={OrderId}", request.MerchantOid);
                return Task.FromResult(new PaymentVerifyResult
                {
                    SignatureValid = false,
                    ErrorMessage = "Ödeme doğrulama sırasında bir hata oluştu."
                });
            }
        }

        /// <summary>
        /// PayTR token oluşturma hash'i:
        /// HMAC-SHA256(merchant_key, hash_str + merchant_salt) → base64
        /// </summary>
        private static string ComputeHash(string hashStr, string merchantKey, string merchantSalt)
        {
            var data = Encoding.UTF8.GetBytes(hashStr + merchantSalt);
            var key = Encoding.UTF8.GetBytes(merchantKey);

            using var hmac = new HMACSHA256(key);
            var hashBytes = hmac.ComputeHash(data);
            return Convert.ToBase64String(hashBytes);
        }

        /// <summary>
        /// Callback doğrulama hash'i:
        /// HMAC-SHA256(merchant_key, hash_str) → base64
        /// </summary>
        private static string ComputeHashRaw(string hashStr, string merchantKey)
        {
            var data = Encoding.UTF8.GetBytes(hashStr);
            var key = Encoding.UTF8.GetBytes(merchantKey);

            using var hmac = new HMACSHA256(key);
            var hashBytes = hmac.ComputeHash(data);
            return Convert.ToBase64String(hashBytes);
        }

        private static bool HashesMatch(string expectedHash, string receivedHash)
        {
            var expectedBytes = Encoding.UTF8.GetBytes(expectedHash);
            var receivedBytes = Encoding.UTF8.GetBytes(receivedHash ?? string.Empty);

            return expectedBytes.Length == receivedBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(expectedBytes, receivedBytes);
        }

        /// <summary>
        /// Sepet içeriğini PayTR formatında base64 JSON yapar.
        /// Format: [["Ürün Adı", "birimFiyatTL", adet], ...]
        /// </summary>
        private static string BuildUserBasket(List<PaymentBasketItem> items)
        {
            var basketArray = new List<object[]>();
            foreach (var item in items)
            {
                // PayTR user_basket birim fiyati TL cinsinden ve noktali ondalik
                // formatta bekler. payment_amount alani ise ayri olarak kurustur.
                var priceInTl = item.Price.ToString("0.00", CultureInfo.InvariantCulture);
                basketArray.Add(new object[] { item.Name, priceInTl, item.Quantity });
            }

            var json = JsonSerializer.Serialize(basketArray);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        }

        /// <summary>
        /// Merchant Key ve Salt'ı DataProtection ile çözerek döndürür.
        /// </summary>
        private (string key, string salt) GetMerchantCredentials(SiteAyarlari settings)
        {
            try
            {
                var key = string.IsNullOrWhiteSpace(settings.PaytrMerchantKeyProtected)
                    ? string.Empty
                    : _paytrProtector.Unprotect(settings.PaytrMerchantKeyProtected);

                var salt = string.IsNullOrWhiteSpace(settings.PaytrMerchantSaltProtected)
                    ? string.Empty
                    : _paytrProtector.Unprotect(settings.PaytrMerchantSaltProtected);

                return (key, salt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PayTR merchant bilgileri çözülürken hata. Veritabanındaki şifreli anahtarlar bozulmuş olabilir.");
                return (string.Empty, string.Empty);
            }
        }

        private static string NormalizePhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return string.Empty;
            var digits = new string(phone.Where(char.IsDigit).ToArray());
            return digits;
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maxLength ? value : value[..maxLength];
        }

        /// <summary>
        /// PayTR /api/get-token endpoint'inden dönen JSON cevabı
        /// </summary>
        private class PaytrTokenResponse
        {
            public string Status { get; set; } = string.Empty;
            public string? Token { get; set; }
            public string? Reason { get; set; }
        }
    }
}
