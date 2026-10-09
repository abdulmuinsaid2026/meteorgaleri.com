using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace KanvasProje.Core.Varliklar
{
    public class UrunSecenek : BaseEntity
    {
        [ForeignKey("Urun")]
        public int UrunId { get; set; }
        public Urun Urun { get; set; } = default!;

        public string Olcu { get; set; } = string.Empty;
        public string CerceveTipi { get; set; } = string.Empty;
        public string CerceveRengi { get; set; } = string.Empty;
        public string CerceveKalinligi { get; set; } = string.Empty;
        public string MalzemeTuru { get; set; } = string.Empty;
        public string Yon { get; set; } = string.Empty;
        public int ParcaSayisi { get; set; } = 1;
        public string VaryantSku { get; set; } = string.Empty;
        public string KisilestirmeMetni { get; set; } = string.Empty;
        public string OzelTasarimNotu { get; set; } = string.Empty;
        public decimal FiyatFarki { get; set; }
        public decimal SatisFiyati { get; set; }
        public decimal MaliyetFiyati { get; set; }
        public int StokAdedi { get; set; } = 100;
        public int UretimSuresiGun { get; set; }
        public decimal Desi { get; set; }
        public string GorselUrl { get; set; } = string.Empty;
        public bool AktifMi { get; set; } = true;
        public bool VarsayilanMi { get; set; }
        public bool TukeninceGizle { get; set; }
        public bool OnSipariseAcikMi { get; set; }
        public int Sira { get; set; }

        [NotMapped]
        public bool SatinAlinabilirMi => AktifMi && (StokAdedi > 0 || OnSipariseAcikMi);

        [NotMapped]
        public decimal EtkinFiyat => Math.Round(SatisFiyati * 0.80m, 0);

        [NotMapped]
        public decimal EskiFiyat => SatisFiyati;

        [NotMapped]
        public bool IndirimVarMi => SatisFiyati > 0;

        [NotMapped]
        public string VaryantBasligi
        {
            get
            {
                var parcalar = new List<string>
                {
                    Olcu,
                    CerceveTipi,
                    CerceveRengi,
                    MalzemeTuru,
                    Yon,
                    ParcaSayisi > 1 ? $"{ParcaSayisi} Parca" : string.Empty
                };

                return string.Join(" / ", parcalar.Where(x => !string.IsNullOrWhiteSpace(x)));
            }
        }

        [NotMapped]
        public string VaryantOzeti
        {
            get
            {
                var parcalar = new List<string>();

                if (!string.IsNullOrWhiteSpace(CerceveKalinligi))
                {
                    parcalar.Add($"Kalinlik: {CerceveKalinligi}");
                }

                if (!string.IsNullOrWhiteSpace(VaryantSku))
                {
                    parcalar.Add($"SKU: {VaryantSku}");
                }

                if (!string.IsNullOrWhiteSpace(KisilestirmeMetni))
                {
                    parcalar.Add($"Kisellestirme: {KisilestirmeMetni}");
                }

                if (!string.IsNullOrWhiteSpace(OzelTasarimNotu))
                {
                    parcalar.Add($"Not: {OzelTasarimNotu}");
                }

                return string.Join(" | ", parcalar);
            }
        }
    }
}
