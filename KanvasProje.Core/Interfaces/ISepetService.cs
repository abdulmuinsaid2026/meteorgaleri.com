using KanvasProje.Core.Varliklar;

namespace KanvasProje.Core.Interfaces
{
    /// <summary>
    /// Sepet yönetimi için servis interface
    /// </summary>
    public interface ISepetService
    {
        /// <summary>
        /// Kullanıcı veya anonymous session için sepet getirir (yoksa oluşturur)
        /// </summary>
        Task<Sepet> GetOrCreateSepetAsync(string? userId, string sessionId);

        /// <summary>
        /// Sepete ürün ekler
        /// </summary>
        Task<bool> SepeteEkleAsync(string? userId, string sessionId, int urunId, int? urunSecenekId, int adet, string? cerceveModeli = null, string? musteriNotu = null, decimal? cerceveFarki = null, int? ozelEn = null, int? ozelBoy = null);

        /// <summary>
        /// Sepetteki ürün adedini günceller
        /// </summary>
        Task<bool> AdediGuncelleAsync(int sepetItemId, int yeniAdet);

        /// <summary>
        /// Sepetten ürün siler
        /// </summary>
        Task<bool> SepettenCikarAsync(int sepetItemId);

        /// <summary>
        /// Sepetitem notunu günceller
        /// </summary>
        Task<bool> NotGuncelleAsync(int sepetItemId, string? musteriNotu);

        /// <summary>
        /// Sepetteki tüm ürünleri getirir
        /// </summary>
        Task<List<SepetItem>> GetSepetItemsAsync(string? userId, string sessionId);

        /// <summary>
        /// Sepet toplamını hesaplar
        /// </summary>
        Task<decimal> GetSepetToplamiAsync(string? userId, string sessionId);

        /// <summary>
        /// Sepetteki ürün sayısını döndürür
        /// </summary>
        Task<int> GetSepetUrunSayisiAsync(string? userId, string sessionId);

        /// <summary>
        /// Sepeti temizler
        /// </summary>
        Task<bool> SepetTemizleAsync(string? userId, string sessionId);

        /// <summary>
        /// Anonymous kullanıcı login olduğunda sepetlerini birleştirir
        /// </summary>
        Task MergeSepetlerAsync(string sessionId, string userId);
    }
}
