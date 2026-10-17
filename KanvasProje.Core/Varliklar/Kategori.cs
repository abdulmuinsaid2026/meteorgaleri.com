using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace KanvasProje.Core.Varliklar
{
    public class Kategori : BaseEntity
    {
        public string Ad { get; set; } = string.Empty;
        public string KisaAciklama { get; set; } = string.Empty;
        public string Aciklama { get; set; } = string.Empty;
        public string? Slug { get; set; }
        public string? GorselUrl { get; set; }
        public string? BannerUrl { get; set; }
        public string SeoTitle { get; set; } = string.Empty;
        public string SeoDescription { get; set; } = string.Empty;
        public string UstMetin { get; set; } = string.Empty;
        public string AltMetin { get; set; } = string.Empty;
        public string KampanyaEtiketi { get; set; } = string.Empty;
        public string UrunSiralamaTipi { get; set; } = "manual";
        public int Sira { get; set; }
        public bool AktifMi { get; set; } = true;
        public decimal? IndirimYuzdesi { get; set; }

        public int? ParentKategoriId { get; set; }
        public Kategori? ParentKategori { get; set; }
        public ICollection<Kategori> AltKategoriler { get; set; } = new List<Kategori>();

        public ICollection<Urun> Urunler { get; set; } = new List<Urun>();

        [NotMapped]
        public bool AnaKategoriMi => !ParentKategoriId.HasValue;
    }
}
