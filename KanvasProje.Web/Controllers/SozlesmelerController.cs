using KanvasProje.Core.Varliklar;
using Microsoft.AspNetCore.Mvc;

namespace KanvasProje.Web.Controllers
{
    public class SozlesmelerController : Controller
    {
        [HttpGet]
        public IActionResult Gizlilik()
        {
            return View("~/Views/Kurumsal/Gizlilik.cshtml");
        }

        [HttpGet]
        public IActionResult MesafeliSatis()
        {
            return View("~/Views/Kurumsal/Detay.cshtml", MesafeliSatisSayfasi());
        }

        [HttpGet]
        public IActionResult VeriSilme()
        {
            return View("~/Views/Kurumsal/Detay.cshtml", VeriSilmeSayfasi());
        }

        private static KurumsalSayfa VeriSilmeSayfasi()
        {
            return new KurumsalSayfa
            {
                Baslik = "Veri Silme Talebi",
                Icerik = """
                    <p>MeteorGaleri olarak kişisel verilerinizin korunmasına önem veriyoruz. Hesabınızla ilişkili kişisel verilerinizin silinmesini istediğinizde aşağıdaki adımları izleyebilirsiniz. Talepleriniz, yasal saklama yükümlülükleri saklı kalmak kaydıyla en geç 30 gün içinde sonuçlandırılır.</p>

                    <h2>1. Veri Silme Talebi Nasıl Oluşturulur?</h2>
                    <p>Kayıtlı e-posta adresinizden <a href="/Kurumsal/Iletisim">İletişim</a> sayfasındaki kanallar aracılığıyla bize ulaşarak veri silme talebinizi iletebilirsiniz. Talebinizde ad-soyad ve kayıtlı e-posta adresinizi belirtmeniz, kimlik doğrulaması ve işlem hızı açısından önemlidir.</p>

                    <h2>2. Hangi Veriler Silinir?</h2>
                    <p>Talebiniz onaylandığında üyelik hesabınız kapatılır ve hesabınıza bağlı kişisel veriler (ad-soyad, e-posta, telefon, adres bilgileri, favoriler ve sepet kayıtları) silinir veya anonim hale getirilir. Vergi ve ticaret mevzuatı gereği saklanması zorunlu olan sipariş ve fatura kayıtları, yasal süreler boyunca yalnızca mevzuata uygun şekilde muhafaza edilir.</p>

                    <h2>3. Facebook ile Giriş Yapan Kullanıcılar</h2>
                    <p>Facebook hesabınızla giriş yaptıysanız, dilerseniz Facebook tarafındaki uygulama bağlantısını da kaldırabilirsiniz: Facebook → Ayarlar → Uygulamalar ve İnternet Siteleri → MeteorGaleri → Kaldır. Bu işlem, Facebook'un kendi sistemlerinde tuttuğu uygulama bağlantı kaydını kaldırır. MeteorGaleri tarafındaki verilerinizin silinmesi için yukarıdaki 1. adımdaki talebi de iletmeniz gerekir.</p>

                    <h2>4. Gizlilik Politikası</h2>
                    <p>Verilerin işlenmesine ilişkin detaylı bilgi için <a href="/Sozlesmeler/Gizlilik">Gizlilik Politikası</a> sayfasını inceleyebilirsiniz.</p>
                    """
            };
        }

        private static KurumsalSayfa MesafeliSatisSayfasi()
        {
            return new KurumsalSayfa
            {
                Baslik = "Mesafeli Satış Sözleşmesi",
                Icerik = """
                    <p>Bu sözleşme, MeteorGaleri üzerinden verilen siparişlerde alıcı ile satıcı arasındaki mesafeli satış koşullarını açıklar. Sipariş veren müşteri, ödeme adımını tamamladığında bu sözleşmede yer alan temel koşulları kabul etmiş sayılır.</p>

                    <h2>1. Taraflar</h2>
                    <p>Satıcı: MeteorGaleri. Alıcı: Web sitesi üzerinden ürün veya hizmet satın alan gerçek ya da tüzel kişi. Satıcıya ait güncel iletişim bilgilerine <a href="/Kurumsal/Iletisim">İletişim</a> sayfasından ulaşılabilir.</p>

                    <h2>2. Sözleşmenin Konusu</h2>
                    <p>İşbu sözleşmenin konusu, alıcının elektronik ortamda sipariş verdiği ürünlerin satışı, teslimi, cayma hakkı, iade koşulları ve tarafların karşılıklı hak ve yükümlülüklerinin belirlenmesidir.</p>

                    <h2>3. Ürün ve Sipariş Bilgileri</h2>
                    <p>Ürünün adı, adedi, varyasyon seçimi, satış bedeli, ödeme şekli, teslimat adresi ve sipariş tarihi sipariş özeti ekranında ve sipariş kayıtlarında yer alır. Kişiye özel ölçü, tasarım veya üretim tercihi içeren ürünlerde üretim süreci sipariş onayından sonra başlar.</p>

                    <h2>4. Teslimat</h2>
                    <p>Siparişler, ürün tipine ve üretim yoğunluğuna göre belirtilen hazırlık süresi içinde kargoya teslim edilir. Türkiye geneli gönderimlerde kargo firmasından kaynaklanan gecikmeler satıcının doğrudan kontrolü dışında olabilir.</p>

                    <h2>5. Cayma Hakkı ve İade</h2>
                    <p>Standart ürünlerde cayma ve iade talepleri ilgili mevzuat çerçevesinde değerlendirilir. Müşterinin özel ölçü, özel tasarım, kişiselleştirme veya siparişe özel üretim tercihiyle hazırlanan ürünlerde cayma hakkı sınırlı olabilir. Detaylı bilgi için <a href="/Kurumsal/IadeKosullari">İade Koşulları</a> sayfası incelenmelidir.</p>

                    <h2>6. Ödeme ve Güvenlik</h2>
                    <p>Ödeme işlemleri güvenli ödeme altyapısı üzerinden gerçekleştirilir. Kart bilgileri MeteorGaleri sunucularında saklanmaz. Ödeme sırasında kullanılan güvenlik doğrulamaları bankanız veya ödeme sağlayıcınız tarafından yürütülür.</p>

                    <h2>7. Uyuşmazlık</h2>
                    <p>Taraflar arasında doğabilecek uyuşmazlıklarda, yürürlükteki tüketici mevzuatı kapsamında yetkili tüketici hakem heyetleri ve tüketici mahkemeleri yetkilidir.</p>
                    """
            };
        }
    }
}
