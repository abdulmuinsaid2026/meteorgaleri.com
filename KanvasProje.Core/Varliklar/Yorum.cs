using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KanvasProje.Core.Varliklar
{
    public class Yorum : BaseEntity
    {
        public int UrunId { get; set; }
        [ForeignKey("UrunId")]
        public Urun Urun { get; set; } = default!;

        public string? AppUserId { get; set; } // Gerçek kullanıcı ise dolar
        
        [Required]
        [StringLength(50)]
        public string AdSoyad { get; set; } = string.Empty; // Bot ise "Ahmet K." gibi yazacağız

        [Required(ErrorMessage = "Yorum metni zorunludur.")]
        [StringLength(2000, MinimumLength = 2, ErrorMessage = "Yorum metni 2 ile 2000 karakter arasında olmalıdır.")]
        public string YorumMetni { get; set; } = string.Empty;

        [Range(1, 5, ErrorMessage = "Puan 1 ile 5 arasında olmalıdır.")]
        public int Puan { get; set; } // 1 ile 5 arası

        public bool OnayliMi { get; set; } // Admin onayı
    }
}