using System;
using System.Collections.Generic;
using System.Threading;
using KanvasProje.Core.Varliklar;

namespace KanvasProje.Core.Helpers
{
    /// <summary>
    /// Site genelinde ve kategori bazında yüzdelik indirim hesaplama yardımcısı.
    /// Admin panelindeki Toplu İndirim Yönetimi ile senkronize çalışır.
    /// </summary>
    public static class FiyatPolitikasiHelper
    {
        private static decimal _genelIndirimYuzdesi = 20m;
        private static IReadOnlyDictionary<int, decimal> _kategoriIndirimleri = new Dictionary<int, decimal>();
        private static readonly object _syncRoot = new();

        public static decimal GenelIndirimYuzdesi => _genelIndirimYuzdesi;

        public static void Configure(decimal genelIndirimYuzdesi, IDictionary<int, decimal>? kategoriIndirimleri = null)
        {
            var clampedGenel = Math.Clamp(genelIndirimYuzdesi, 0m, 95m);
            var yeniHarita = new Dictionary<int, decimal>();

            if (kategoriIndirimleri != null)
            {
                foreach (var kvp in kategoriIndirimleri)
                {
                    if (kvp.Value >= 0m && kvp.Value <= 95m)
                    {
                        yeniHarita[kvp.Key] = kvp.Value;
                    }
                }
            }

            lock (_syncRoot)
            {
                _genelIndirimYuzdesi = clampedGenel;
                _kategoriIndirimleri = yeniHarita;
            }
        }

        public static decimal GetIndirimYuzdesi(int? kategoriId = null, Kategori? kategori = null)
        {
            if (kategori?.IndirimYuzdesi.HasValue == true && kategori.IndirimYuzdesi.Value >= 0m)
            {
                return Math.Clamp(kategori.IndirimYuzdesi.Value, 0m, 95m);
            }

            if (kategori?.ParentKategori?.IndirimYuzdesi.HasValue == true && kategori.ParentKategori.IndirimYuzdesi.Value >= 0m)
            {
                return Math.Clamp(kategori.ParentKategori.IndirimYuzdesi.Value, 0m, 95m);
            }

            var currentMap = _kategoriIndirimleri;
            if (kategoriId.HasValue && currentMap.TryGetValue(kategoriId.Value, out var katOran))
            {
                return Math.Clamp(katOran, 0m, 95m);
            }

            if (kategori?.ParentKategoriId.HasValue == true && currentMap.TryGetValue(kategori.ParentKategoriId.Value, out var parentOran))
            {
                return Math.Clamp(parentOran, 0m, 95m);
            }

            return _genelIndirimYuzdesi;
        }

        public static decimal GetCarpan(int? kategoriId = null, Kategori? kategori = null)
        {
            var yuzde = GetIndirimYuzdesi(kategoriId, kategori);
            return (100m - yuzde) / 100m;
        }

        public static decimal ApplyDiscount(decimal bazFiyat, int? kategoriId = null, Kategori? kategori = null, int decimals = 0)
        {
            if (bazFiyat <= 0m)
            {
                return 0m;
            }

            var carpan = GetCarpan(kategoriId, kategori);
            return Math.Round(bazFiyat * carpan, decimals);
        }
    }
}
