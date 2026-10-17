using System;
using System.ComponentModel.DataAnnotations;

namespace KanvasProje.Core.Varliklar
{
    public class IletisimMesaj
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "Ad Soyad zorunludur")]
        [StringLength(120)]
        public string AdSoyad { get; set; } = string.Empty;

        [Required(ErrorMessage = "E-posta zorunludur")]
        [EmailAddress(ErrorMessage = "Geçerli bir e-posta giriniz")]
        [StringLength(160)]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Konu seçimi zorunludur")]
        [StringLength(150)]
        public string Konu { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mesaj zorunludur")]
        [StringLength(4000, MinimumLength = 5)]
        public string Mesaj { get; set; } = string.Empty;

        public DateTime Tarih { get; set; } = DateTime.UtcNow;
        public string? IpAdresi { get; set; }
        public bool OkunduMu { get; set; } = false; // Admin okuyunca true yaparız
    }
}