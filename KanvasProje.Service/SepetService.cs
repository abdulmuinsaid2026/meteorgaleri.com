using System.Linq;
using KanvasProje.Core.Helpers;
using KanvasProje.Core.Interfaces;
using KanvasProje.Core.Varliklar;
using KanvasProje.Data;
using KanvasProje.Service.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KanvasProje.Service
{
    public class SepetService : ISepetService
    {
        private readonly KanvasDbContext _context;
        private readonly ILogger<SepetService> _logger;
        private readonly ISiteSettingsService _siteSettingsService;

        public SepetService(KanvasDbContext context, ILogger<SepetService> logger, ISiteSettingsService siteSettingsService)
        {
            _context = context;
            _logger = logger;
            _siteSettingsService = siteSettingsService;
        }

        public async Task<Sepet> GetOrCreateSepetAsync(string? userId, string sessionId)
        {
            Sepet? sepet = null;

            if (!string.IsNullOrEmpty(userId))
            {
                sepet = await _context.Sepetler
                    .Include(s => s.SepetItems.Where(i => !i.SilindiMi))
                        .ThenInclude(i => i.Urun)
                    .Include(s => s.SepetItems.Where(i => !i.SilindiMi))
                        .ThenInclude(i => i.UrunSecenek)
                    .FirstOrDefaultAsync(s => s.AppUserId == userId && !s.SilindiMi);
            }
            else
            {
                sepet = await _context.Sepetler
                    .Include(s => s.SepetItems.Where(i => !i.SilindiMi))
                        .ThenInclude(i => i.Urun)
                    .Include(s => s.SepetItems.Where(i => !i.SilindiMi))
                        .ThenInclude(i => i.UrunSecenek)
                    .FirstOrDefaultAsync(s => s.SessionId == sessionId && !s.SilindiMi);
            }

            if (sepet == null)
            {
                sepet = new Sepet
                {
                    AppUserId = userId,
                    SessionId = string.IsNullOrEmpty(userId) ? sessionId : null,
                    OlusturulmaTarihi = DateTime.UtcNow,
                    SonGuncellemeTarihi = DateTime.UtcNow,
                    SilindiMi = false
                };

                _context.Sepetler.Add(sepet);
                await _context.SaveChangesAsync();
            }

            return sepet;
        }

        public async Task<bool> SepeteEkleAsync(string? userId, string sessionId, int urunId, int? urunSecenekId, int adet, string? cerceveModeli = null, string? musteriNotu = null, decimal? cerceveFarki = null, int? ozelEn = null, int? ozelBoy = null)
        {
            try
            {
                var sepet = await GetOrCreateSepetAsync(userId, sessionId);
                var urun = await _context.Urunler
                    .AsNoTracking()
                    .Include(x => x.Kategori!)
                        .ThenInclude(x => x.ParentKategori)
                    .Include(x => x.UrunSecenek)
                    .FirstOrDefaultAsync(x => x.Id == urunId && x.AktifMi && !x.SilindiMi);

                if (urun == null)
                {
                    return false;
                }

                // Özel ölçü kontrolü (Sadece Baskılı Halı ve Duvar Kağıdı için geçerli)
                bool isOzelOlcu = ozelEn.HasValue && ozelBoy.HasValue && ozelEn.Value > 0 && ozelBoy.Value > 0;
                bool isHali = urun.KategoriId == 34 || urun.Kategori?.Slug == "baskili-hali" || urun.UrunTipi == "BaskiliHali";
                bool isDuvarKagidi = urun.KategoriId == 31 || urun.Kategori?.Slug == "duvar-kagidi" || urun.UrunTipi == "DuvarKagidi";

                if (isOzelOlcu && !isHali && !isDuvarKagidi)
                {
                    // Halı veya Duvar Kağıdı dışındaki ürünlerde özel ölçü izin verilmez
                    return false;
                }

                UrunSecenek? secenek = null;
                if (!isOzelOlcu)
                {
                    secenek = ResolveSelectedVariant(urun, urunSecenekId);
                    if (urunSecenekId.HasValue && secenek == null)
                    {
                        return false;
                    }
                }

                var requiresFrameSelection = !isOzelOlcu && RequiresFrameSelection(urun);
                var normalizedCerceveModeli = isOzelOlcu ? string.Empty : NormalizeFrameModel(cerceveModeli);
                if (requiresFrameSelection && string.IsNullOrWhiteSpace(normalizedCerceveModeli))
                {
                    normalizedCerceveModeli = "Çerçevesiz";
                }

                var hedefSecenekId = secenek?.Id;
                var normalizedMusteriNotu = NormalizeCustomerNote(musteriNotu);

                decimal fiyat;
                string secenekAdi;

                if (isOzelOlcu)
                {
                    decimal enMetre = (decimal)ozelEn!.Value / 100m;
                    decimal boyMetre = (decimal)ozelBoy!.Value / 100m;
                    decimal metrekare = Math.Round(enMetre * boyMetre, 2);
                    if (metrekare < 0.25m) metrekare = 0.25m; // Min 0.25 m²

                    var settings = _siteSettingsService.GetSettings();
                    decimal m2BirimFiyat = isHali ? settings.HaliMetrekareFiyati : settings.DuvarKagidiMetrekareFiyati;
                    if (m2BirimFiyat <= 0)
                    {
                        m2BirimFiyat = isHali ? 1250m : 450m;
                    }

                    fiyat = Math.Round(metrekare * m2BirimFiyat, 2);
                    secenekAdi = $"Özel Ölçü: {ozelEn.Value}x{ozelBoy.Value} cm ({metrekare:0.##} m²)";
                }
                else
                {
                    fiyat = ResolveCartPrice(urun, secenek, normalizedCerceveModeli, cerceveFarki);
                    secenekAdi = BuildCartOptionLabel(secenek != null ? BuildVariantLabel(secenek) : null, normalizedCerceveModeli);
                }

                SepetItem? mevcutItem;
                if (isOzelOlcu)
                {
                    mevcutItem = sepet.SepetItems.FirstOrDefault(i =>
                        i.UrunId == urunId &&
                        i.SecenekAdi == secenekAdi &&
                        NormalizeCustomerNote(i.MusteriNotu) == normalizedMusteriNotu &&
                        !i.SilindiMi);
                }
                else
                {
                    mevcutItem = sepet.SepetItems.FirstOrDefault(i =>
                        i.UrunId == urunId &&
                        i.UrunSecenekId == hedefSecenekId &&
                        i.CerceveModeli == normalizedCerceveModeli &&
                        NormalizeCustomerNote(i.MusteriNotu) == normalizedMusteriNotu &&
                        !i.SilindiMi);
                }

                var toplamAdet = (mevcutItem?.Adet ?? 0) + adet;
                if (!isOzelOlcu && !CanAddQuantity(urun, secenek, toplamAdet))
                {
                    return false;
                }

                if (mevcutItem != null)
                {
                    mevcutItem.Adet += adet;
                }
                else
                {
                    var gorsel = secenek != null && !string.IsNullOrWhiteSpace(secenek.GorselUrl)
                        ? secenek.GorselUrl
                        : urun.AnaGorselUrl;

                    var yeniItem = new SepetItem
                    {
                        SepetId = sepet.Id,
                        UrunId = urunId,
                        UrunSecenekId = isOzelOlcu ? null : hedefSecenekId,
                        Adet = adet,
                        Fiyat = fiyat,
                        UrunBaslik = urun.Baslik,
                        UrunResimUrl = gorsel,
                        SecenekAdi = secenekAdi,
                        CerceveModeli = normalizedCerceveModeli,
                        MusteriNotu = normalizedMusteriNotu,
                        OlusturulmaTarihi = DateTime.UtcNow,
                        SilindiMi = false
                    };

                    _context.SepetItems.Add(yeniItem);
                }

                MarkCartAsUpdated(sepet);
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sepete ekleme hatasi. UrunId={UrunId}, SecenekId={SecenekId}", urunId, urunSecenekId);
                return false;
            }
        }

        public async Task<bool> AdediGuncelleAsync(int sepetItemId, int yeniAdet)
        {
            try
            {
                var item = await _context.SepetItems
                    .Include(x => x.Sepet)
                    .Include(x => x.Urun)
                        .ThenInclude(x => x.Kategori!)
                            .ThenInclude(x => x.ParentKategori)
                    .Include(x => x.UrunSecenek)
                    .FirstOrDefaultAsync(x => x.Id == sepetItemId);
                if (item == null || item.SilindiMi)
                {
                    return false;
                }

                if (yeniAdet <= 0)
                {
                    return await SepettenCikarAsync(sepetItemId);
                }

                if (!CanAddQuantity(item.Urun, item.UrunSecenek, yeniAdet))
                {
                    return false;
                }

                item.Adet = yeniAdet;
                MarkCartAsUpdated(item.Sepet);
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Adet guncelleme hatasi. SepetItemId={SepetItemId}", sepetItemId);
                return false;
            }
        }

        public async Task<bool> SepettenCikarAsync(int sepetItemId)
        {
            try
            {
                var item = await _context.SepetItems
                    .Include(x => x.Sepet)
                    .FirstOrDefaultAsync(x => x.Id == sepetItemId);
                if (item == null)
                {
                    return false;
                }

                item.SilindiMi = true;
                MarkCartAsUpdated(item.Sepet);
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sepetten cikarma hatasi. SepetItemId={SepetItemId}", sepetItemId);
                return false;
            }
        }

        public async Task<List<SepetItem>> GetSepetItemsAsync(string? userId, string sessionId)
        {
            var sepet = await GetOrCreateSepetAsync(userId, sessionId);
            return sepet.SepetItems.Where(i => !i.SilindiMi).ToList();
        }

        public async Task<decimal> GetSepetToplamiAsync(string? userId, string sessionId)
        {
            var items = await GetSepetItemsAsync(userId, sessionId);
            return items.Sum(i => i.Toplam);
        }

        public async Task<int> GetSepetUrunSayisiAsync(string? userId, string sessionId)
        {
            var query = _context.Sepetler.AsNoTracking();
            query = !string.IsNullOrWhiteSpace(userId)
                ? query.Where(s => s.AppUserId == userId && !s.SilindiMi)
                : query.Where(s => s.SessionId == sessionId && !s.SilindiMi);

            return await query
                .SelectMany(s => s.SepetItems)
                .Where(i => !i.SilindiMi)
                .SumAsync(i => (int?)i.Adet) ?? 0;
        }

        public async Task<bool> SepetTemizleAsync(string? userId, string sessionId)
        {
            try
            {
                var sepet = await GetOrCreateSepetAsync(userId, sessionId);

                foreach (var item in sepet.SepetItems.Where(i => !i.SilindiMi))
                {
                    item.SilindiMi = true;
                }

                sepet.SonGuncellemeTarihi = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sepet temizleme hatasi");
                return false;
            }
        }

        public async Task MergeSepetlerAsync(string sessionId, string userId)
        {
            var anonymousSepet = await _context.Sepetler
                .Include(s => s.SepetItems.Where(i => !i.SilindiMi))
                .FirstOrDefaultAsync(s => s.SessionId == sessionId && string.IsNullOrEmpty(s.AppUserId) && !s.SilindiMi);

            if (anonymousSepet == null || !anonymousSepet.SepetItems.Any())
            {
                return;
            }

            var userSepet = await _context.Sepetler
                .Include(s => s.SepetItems.Where(i => !i.SilindiMi))
                .FirstOrDefaultAsync(s => s.AppUserId == userId && !s.SilindiMi);

            if (userSepet == null)
            {
                userSepet = new Sepet
                {
                    AppUserId = userId,
                    OlusturulmaTarihi = DateTime.UtcNow,
                    SonGuncellemeTarihi = DateTime.UtcNow,
                    SilindiMi = false
                };

                _context.Sepetler.Add(userSepet);
                await _context.SaveChangesAsync();
            }

            if (userSepet.Id == anonymousSepet.Id)
            {
                return;
            }

            foreach (var item in anonymousSepet.SepetItems.Where(i => !i.SilindiMi))
            {
                var mevcutItem = userSepet.SepetItems.FirstOrDefault(i =>
                    i.UrunId == item.UrunId &&
                    i.UrunSecenekId == item.UrunSecenekId &&
                    i.CerceveModeli == item.CerceveModeli &&
                    NormalizeCustomerNote(i.MusteriNotu) == NormalizeCustomerNote(item.MusteriNotu) &&
                    !i.SilindiMi);

                if (mevcutItem != null)
                {
                    mevcutItem.Adet += item.Adet;
                }
                else
                {
                    var yeniItem = new SepetItem
                    {
                        SepetId = userSepet.Id,
                        UrunId = item.UrunId,
                        UrunSecenekId = item.UrunSecenekId,
                        Adet = item.Adet,
                        Fiyat = item.Fiyat,
                        UrunBaslik = item.UrunBaslik,
                        UrunResimUrl = item.UrunResimUrl,
                        SecenekAdi = item.SecenekAdi,
                        CerceveModeli = item.CerceveModeli,
                        MusteriNotu = item.MusteriNotu,
                        OlusturulmaTarihi = DateTime.UtcNow,
                        SilindiMi = false
                    };
                    _context.SepetItems.Add(yeniItem);
                }

                item.SilindiMi = true;
            }

            anonymousSepet.SilindiMi = true;
            userSepet.SonGuncellemeTarihi = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        public async Task<bool> NotGuncelleAsync(int sepetItemId, string? musteriNotu)
        {
            try
            {
                var item = await _context.SepetItems
                    .Include(x => x.Sepet)
                    .FirstOrDefaultAsync(x => x.Id == sepetItemId);
                if (item == null || item.SilindiMi)
                {
                    return false;
                }

                item.MusteriNotu = NormalizeCustomerNote(musteriNotu);
                MarkCartAsUpdated(item.Sepet);
                await _context.SaveChangesAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void MarkCartAsUpdated(Sepet sepet)
        {
            sepet.SonGuncellemeTarihi = DateTime.UtcNow;
            sepet.HatirlatmaGonderildi = false;
            sepet.TerkEdildi = false;
            sepet.TerkEdilmeTarihi = null;
        }

        private static UrunSecenek? ResolveSelectedVariant(Urun urun, int? requestedVariantId)
        {
            var variants = urun.UrunSecenek
                .Where(x =>
                    !x.SilindiMi &&
                    x.AktifMi &&
                    (!x.TukeninceGizle || x.StokAdedi > 0 || x.OnSipariseAcikMi))
                .OrderByDescending(x => x.VarsayilanMi)
                .ThenBy(x => x.Sira)
                .ThenBy(x => x.SatisFiyati)
                .ToList();

            if (!variants.Any())
            {
                return null;
            }

            if (requestedVariantId.HasValue)
            {
                return variants.FirstOrDefault(x => x.Id == requestedVariantId.Value);
            }

            return variants.FirstOrDefault(x => x.SatinAlinabilirMi)
                ?? variants.FirstOrDefault();
        }

        private static bool CanAddQuantity(Urun urun, UrunSecenek? secenek, int toplamAdet)
        {
            if (toplamAdet < urun.MinSiparisAdedi)
            {
                return false;
            }

            if (urun.MaxSiparisAdedi.HasValue && toplamAdet > urun.MaxSiparisAdedi.Value)
            {
                return false;
            }

            if (secenek == null)
            {
                return true;
            }

            if (!secenek.SatinAlinabilirMi)
            {
                return false;
            }

            if (secenek.StokAdedi > 0 && toplamAdet > secenek.StokAdedi && !secenek.OnSipariseAcikMi)
            {
                return false;
            }

            return true;
        }

        private static string BuildVariantLabel(UrunSecenek secenek)
        {
            var baslikParcalari = new[]
                {
                    secenek.Olcu,
                    !string.IsNullOrWhiteSpace(secenek.CerceveTipi) &&
                    secenek.CerceveTipi.Contains("LED", StringComparison.OrdinalIgnoreCase)
                        ? secenek.CerceveTipi
                        : null,
                    secenek.MalzemeTuru,
                    secenek.Yon,
                    secenek.ParcaSayisi > 1 ? $"{secenek.ParcaSayisi} Parca" : null
                }
                .Where(IsMeaningfulVariantPart)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var detayParcalari = new List<string>();

            if (IsMeaningfulVariantPart(secenek.CerceveKalinligi))
            {
                detayParcalari.Add($"Kalinlik: {secenek.CerceveKalinligi.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(secenek.VaryantSku))
            {
                detayParcalari.Add($"SKU: {secenek.VaryantSku.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(secenek.KisilestirmeMetni))
            {
                detayParcalari.Add($"Kisisellestirme: {secenek.KisilestirmeMetni.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(secenek.OzelTasarimNotu))
            {
                detayParcalari.Add($"Not: {secenek.OzelTasarimNotu.Trim()}");
            }

            var baslik = string.Join(" / ", baslikParcalari);
            var detay = string.Join(" | ", detayParcalari);

            if (string.IsNullOrWhiteSpace(baslik) && string.IsNullOrWhiteSpace(detay))
            {
                return "Varsayilan varyasyon";
            }

            return string.Join(" | ", new[] { baslik, detay }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private static bool IsMeaningfulVariantPart(string? value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && !IsGenericVariantValue(value);
        }

        private static bool IsGenericVariantValue(string value)
        {
            var normalized = value.Trim().ToLowerInvariant();
            return normalized is "standart" or "standard" or "varsayilan" or "varsayılan";
        }

        private static string BuildCartOptionLabel(string? variantLabel, string frameModel)
        {
            if (string.IsNullOrWhiteSpace(frameModel) || frameModel == "Çerçevesiz")
            {
                return variantLabel ?? string.Empty;
            }

            return string.IsNullOrWhiteSpace(variantLabel)
                ? $"Çerçeve: {frameModel}"
                : $"{variantLabel} | Çerçeve: {frameModel}";
        }

        private static string NormalizeFrameModel(string? frameModel)
        {
            var value = (frameModel ?? string.Empty).Trim().ToLowerInvariant();
            return value switch
            {
                "çerçevesiz" => "Çerçevesiz",
                "cercevesiz" => "Çerçevesiz",
                "siyah" => "Siyah",
                "beyaz" => "Beyaz",
                "gold" => "Gold",
                "gümüş" or "gumus" => "Gümüş",
                "meşe" or "mese" => "Meşe",
                "ceviz" => "Ceviz",
                _ => string.Empty
            };
        }

        private static bool RequiresFrameSelection(Urun urun)
        {
            return ContainsKanvas(urun.UrunTipi)
                || ContainsKanvas(urun.Kategori?.Ad)
                || ContainsKanvas(urun.Kategori?.ParentKategori?.Ad);
        }

        private static decimal ResolveCartPrice(Urun urun, UrunSecenek? secenek, string cerceveModeli, decimal? cerceveFarki)
        {
            var basePrice = secenek?.SatisFiyati > 0 ? secenek.SatisFiyati : urun.EtkinFiyat;

            if (RequiresFrameSelection(urun) && secenek != null && KanvasFiyatListesi.TryGetFiyat(secenek.Olcu, out var fiyat))
            {
                return cerceveModeli == "Çerçevesiz" ? fiyat.CercevesizKargoDahil : fiyat.CerceveliKargoDahil;
            }

            return basePrice + (cerceveFarki ?? 0);
        }

        private static bool ContainsKanvas(string? value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Contains("kanvas", StringComparison.OrdinalIgnoreCase);
        }

        private static string? NormalizeCustomerNote(string? note)
        {
            if (string.IsNullOrWhiteSpace(note))
            {
                return null;
            }

            var trimmed = note.Trim();
            return trimmed.Length > 500 ? trimmed[..500] : trimmed;
        }
    }
}
