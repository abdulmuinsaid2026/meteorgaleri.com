namespace KanvasProje.Core.DTOs
{
    public class BenzerUrunDto
    {
        public int Id { get; set; }
        public string Baslik { get; set; } = string.Empty;
        public string? Slug { get; set; }
        public string? AnaGorselUrl { get; set; }
        public decimal Fiyat { get; set; }
        public decimal? IndirimliFiyat { get; set; }
        public int KategoriId { get; set; }

        public decimal EtkinFiyat =>
            IndirimliFiyat.HasValue && IndirimliFiyat.Value > 0 && IndirimliFiyat.Value < Fiyat
                ? IndirimliFiyat.Value
                : KanvasProje.Core.Helpers.FiyatPolitikasiHelper.ApplyDiscount(Fiyat, KategoriId, null, 0);

        public bool IndirimVarMi => Fiyat > 0 && EtkinFiyat < Fiyat;
    }
}