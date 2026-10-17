using System.ComponentModel.DataAnnotations;

namespace KanvasProje.Web.Models
{
    public class SifreSifirlaViewModel
    {
        public string Token { get; set; } = string.Empty;

        [Required(ErrorMessage = "Yeni şifre zorunludur")]
        [MinLength(8, ErrorMessage = "Şifre en az 8 karakter olmalı (büyük/küçük harf, rakam ve özel karakter içermelidir)")]
        public string YeniSifre { get; set; } = string.Empty;

        [Required(ErrorMessage = "Şifre tekrarı zorunludur")]
        [Compare("YeniSifre", ErrorMessage = "Şifreler uyuşmuyor")]
        public string SifreTekrar { get; set; } = string.Empty;
    }
}
