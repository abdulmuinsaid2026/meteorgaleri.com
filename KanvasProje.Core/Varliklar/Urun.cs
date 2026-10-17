using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace KanvasProje.Core.Varliklar
{
    public class Urun : BaseEntity
    {
        public string Baslik { get; set; } = string.Empty;
        public string KisaAd { get; set; } = string.Empty;
        public string? Slug { get; set; }
        public string UrlYolu { get; set; } = string.Empty;
        public string SKU { get; set; } = string.Empty;
        public string Barkod { get; set; } = string.Empty;
        public string Marka { get; set; } = string.Empty;
        public string UrunTipi { get; set; } = "Genel";
        public string Etiketler { get; set; } = string.Empty;
        public string KisaAciklama { get; set; } = string.Empty;
        public string Aciklama { get; set; } = string.Empty;
        public string TeknikOzellikler { get; set; } = string.Empty;
        public string MalzemeBilgisi { get; set; } = string.Empty;
        public string BakimTalimati { get; set; } = string.Empty;
        public string PaketlemeBilgisi { get; set; } = string.Empty;
        public string AnaGorselUrl { get; set; } = string.Empty;
        public string StokDurumu { get; set; } = "Stokta";
        public decimal Fiyat { get; set; }
        public decimal? IndirimliFiyat { get; set; }
        public decimal Maliyet { get; set; }
        public decimal KdvOrani { get; set; } = 20;
        public int UretimSuresiGun { get; set; }
        public int KargoyaVerilisSuresiGun { get; set; }
        public int TahminiTeslimSuresiGun { get; set; }
        public bool AktifMi { get; set; } = true;
        public bool OneCikanMi { get; set; }
        public bool YeniUrunMu { get; set; }
        public bool KampanyaliMi { get; set; }
        public bool AnaSayfadaGoster { get; set; }
        public int Sira { get; set; }
        public int GoruntulenmeSayisi { get; set; }
        public int SatisSayisi { get; set; }
        public int FavoriSayisi { get; set; }
        public int MinSiparisAdedi { get; set; } = 1;
        public int? MaxSiparisAdedi { get; set; }
        public string SeoTitle { get; set; } = string.Empty;
        public string SeoDescription { get; set; } = string.Empty;
        public string SeoKeywords { get; set; } = string.Empty;

        public int KategoriId { get; set; }
        public Kategori? Kategori { get; set; }

        public virtual ICollection<UrunResim> UrunResimleri { get; set; } = new List<UrunResim>();
        public virtual ICollection<UrunSecenek> UrunSecenek { get; set; } = new List<UrunSecenek>();
        public virtual ICollection<UrunOzellikDegeri> UrunOzellikleri { get; set; } = new List<UrunOzellikDegeri>();

        [NotMapped]
        public decimal EtkinFiyat =>
            IndirimliFiyat.HasValue && IndirimliFiyat.Value > 0 && IndirimliFiyat.Value < Fiyat
                ? IndirimliFiyat.Value
                : KanvasProje.Core.Helpers.FiyatPolitikasiHelper.ApplyDiscount(Fiyat, KategoriId, Kategori, 0);

        [NotMapped]
        public bool IndirimVarMi =>
            Fiyat > 0 && EtkinFiyat < Fiyat;

        [NotMapped]
        public int ToplamStok =>
            UrunSecenek?.Where(x => !x.SilindiMi && x.AktifMi).Sum(x => x.StokAdedi) ?? 0;

        [NotMapped]
        public bool StoktaVarMi =>
            (UrunSecenek?.Any(x =>
                !x.SilindiMi &&
                x.SatinAlinabilirMi &&
                (!x.TukeninceGizle || x.StokAdedi > 0 || x.OnSipariseAcikMi)) ?? false)
            || string.Equals(StokDurumu, "Stokta", System.StringComparison.OrdinalIgnoreCase)
            || ToplamStok > 0;
    }
}
