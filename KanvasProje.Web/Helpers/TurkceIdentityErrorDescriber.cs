using Microsoft.AspNetCore.Identity;

namespace KanvasProje.Core.Helpers
{
    /// <summary>
    /// ASP.NET Identity hata mesajlarını Türkçeye çevirir.
    /// </summary>
    public class TurkceIdentityErrorDescriber : IdentityErrorDescriber
    {
        public override IdentityError DuplicateEmail(string email)
            => new() { Code = nameof(DuplicateEmail), Description = "Bu e-posta adresi ile kayıtlı bir hesap zaten mevcut. Giriş yapabilir veya şifrenizi sıfırlayabilirsiniz." };

        public override IdentityError DuplicateUserName(string userName)
            => new() { Code = nameof(DuplicateUserName), Description = "Bu e-posta adresi ile kayıtlı bir hesap zaten mevcut." };

        public override IdentityError InvalidEmail(string? email)
            => new() { Code = nameof(InvalidEmail), Description = $"'{email}' geçersiz bir e-posta adresidir." };

        public override IdentityError InvalidUserName(string? userName)
            => new() { Code = nameof(InvalidUserName), Description = $"'{userName}' geçersiz bir kullanıcı adıdır. Sadece harf ve rakam kullanılabilir." };

        public override IdentityError PasswordMismatch()
            => new() { Code = nameof(PasswordMismatch), Description = "Şifre hatalı." };

        public override IdentityError PasswordRequiresDigit()
            => new() { Code = nameof(PasswordRequiresDigit), Description = "Şifre en az bir rakam (0-9) içermelidir." };

        public override IdentityError PasswordRequiresLower()
            => new() { Code = nameof(PasswordRequiresLower), Description = "Şifre en az bir küçük harf (a-z) içermelidir." };

        public override IdentityError PasswordRequiresUpper()
            => new() { Code = nameof(PasswordRequiresUpper), Description = "Şifre en az bir büyük harf (A-Z) içermelidir." };

        public override IdentityError PasswordRequiresNonAlphanumeric()
            => new() { Code = nameof(PasswordRequiresNonAlphanumeric), Description = "Şifre en az bir özel karakter (!@#$%^&* vb.) içermelidir." };

        public override IdentityError PasswordTooShort(int length)
            => new() { Code = nameof(PasswordTooShort), Description = $"Şifre en az {length} karakter uzunluğunda olmalıdır." };

        public override IdentityError PasswordRequiresUniqueChars(int uniqueChars)
            => new() { Code = nameof(PasswordRequiresUniqueChars), Description = $"Şifre en az {uniqueChars} farklı karakter içermelidir." };

        public override IdentityError UserAlreadyHasPassword()
            => new() { Code = nameof(UserAlreadyHasPassword), Description = "Kullanıcının zaten bir şifresi var." };

        public override IdentityError UserAlreadyInRole(string role)
            => new() { Code = nameof(UserAlreadyInRole), Description = $"Kullanıcı zaten '{role}' rolünde." };

        public override IdentityError UserNotInRole(string role)
            => new() { Code = nameof(UserNotInRole), Description = $"Kullanıcı '{role}' rolünde değil." };

        public override IdentityError UserLockoutNotEnabled()
            => new() { Code = nameof(UserLockoutNotEnabled), Description = "Bu kullanıcı için kilit özelliği aktif değil." };

        public override IdentityError DefaultError()
            => new() { Code = nameof(DefaultError), Description = "Beklenmeyen bir hata oluştu. Lütfen tekrar deneyin." };

        public override IdentityError ConcurrencyFailure()
            => new() { Code = nameof(ConcurrencyFailure), Description = "Eşzamanlılık hatası. Lütfen tekrar deneyin." };

        public override IdentityError RecoveryCodeRedemptionFailed()
            => new() { Code = nameof(RecoveryCodeRedemptionFailed), Description = "Kurtarma kodu doğrulaması başarısız." };

        public override IdentityError LoginAlreadyAssociated()
            => new() { Code = nameof(LoginAlreadyAssociated), Description = "Bu dış giriş zaten başka bir hesapla ilişkili." };

        public override IdentityError InvalidToken()
            => new() { Code = nameof(InvalidToken), Description = "Geçersiz doğrulama kodu." };
    }
}
