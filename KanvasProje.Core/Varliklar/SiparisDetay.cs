using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace KanvasProje.Core.Varliklar
{
    public class SiparisDetay : BaseEntity
    {
        public int SiparisId { get; set; }
        [ForeignKey("SiparisId")]
        public virtual Siparis Siparis { get; set; } = default!;

        public int? UrunSecenekId { get; set; }

        [ForeignKey("UrunSecenekId")]
        public virtual UrunSecenek? UrunSecenek { get; set; }

        public int Adet { get; set; }
        public decimal BirimFiyat { get; set; }
        public int UrunId { get; set; }
        [ForeignKey("UrunId")]
        public Urun Urun { get; set; } = default!;
        public string CerceveModeli { get; set; } = string.Empty;
        public string? SecenekAdi { get; set; }

        [MaxLength(500)]
        public string? MusteriNotu { get; set; }
    }
}
