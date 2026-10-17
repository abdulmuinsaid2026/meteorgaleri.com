using System.Threading.Tasks;

namespace KanvasProje.Service.Interfaces
{
    /// <summary>
    /// PayTR ödeme altyapısı için soyut arayüz.
    /// </summary>
    public interface IPaymentService
    {
        /// <summary>
        /// PayTR iframe token'ı alır ve ödeme formunu başlatır.
        /// </summary>
        Task<PaymentInitResult> InitializeCheckoutAsync(PaymentInitRequest request);

        /// <summary>
        /// PayTR bildirim (callback) sonrası hash doğrulaması yapar.
        /// </summary>
        Task<PaymentVerifyResult> VerifyPaymentAsync(PaymentVerifyRequest request);
    }

    public class PaymentInitRequest
    {
        public string OrderId { get; set; } = string.Empty;
        public decimal TotalPrice { get; set; }
        public decimal BasketPrice { get; set; }
        public string CallbackUrl { get; set; } = string.Empty;
        public string OkUrl { get; set; } = string.Empty;
        public string FailUrl { get; set; } = string.Empty;

        // Alıcı bilgileri
        public string BuyerName { get; set; } = string.Empty;
        public string BuyerEmail { get; set; } = string.Empty;
        public string BuyerPhone { get; set; } = string.Empty;
        public string BuyerAddress { get; set; } = string.Empty;
        public string BuyerCity { get; set; } = string.Empty;
        public string BuyerIp { get; set; } = string.Empty;

        // Sepet kalemleri
        public List<PaymentBasketItem> BasketItems { get; set; } = new();
    }

    public class PaymentBasketItem
    {
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Quantity { get; set; } = 1;
    }

    public class PaymentInitResult
    {
        public bool Success { get; set; }
        public string? Token { get; set; }
        public string? IframeUrl { get; set; }
        public string? CheckoutFormContent { get; set; }
        public string? PaymentPageUrl { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class PaymentVerifyRequest
    {
        public string MerchantOid { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string TotalAmount { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
    }

    public class PaymentVerifyResult
    {
        public bool SignatureValid { get; set; }
        public bool PaymentSuccessful { get; set; }
        public string? PaymentType { get; set; }
        public string? Currency { get; set; }
        public decimal PaidPrice { get; set; }
        public string? TransactionId { get; set; }
        public string? OrderId { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
