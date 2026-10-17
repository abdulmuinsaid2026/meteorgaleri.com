using KanvasProje.Core.Interfaces;
using KanvasProje.Core.Helpers;
using KanvasProje.Core.Varliklar;
using KanvasProje.Data;
using KanvasProje.Service.Interfaces;
using KanvasProje.Service.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;


namespace KanvasProje.Web.Controllers
{
    public class SiparisController : Controller
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly IService<Adres> _adresService;
        private readonly KanvasDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ISepetService _sepetService;
        private readonly ISiteSettingsService _siteSettingsService;
        private readonly IPaymentService _paymentService;
        private readonly ILogger<SiparisController> _logger;
        private readonly IWebHostEnvironment _env;

        public SiparisController(
            UserManager<AppUser> userManager,
            IService<Adres> adresService,
            KanvasDbContext context,
            IEmailService emailService,
            ISepetService sepetService,
            ISiteSettingsService siteSettingsService,
            IPaymentService paymentService,
            ILogger<SiparisController> logger,
            IWebHostEnvironment env)
        {
            _userManager = userManager;
            _adresService = adresService;
            _context = context;
            _emailService = emailService;
            _sepetService = sepetService;
            _siteSettingsService = siteSettingsService;
            _paymentService = paymentService;
            _logger = logger;
            _env = env;
        }

        [HttpGet]
        public async Task<IActionResult> Odeme()
        {
            var userId = User.Identity?.IsAuthenticated == true ? _userManager.GetUserId(User) : null;
            var sessionId = HttpContext.Session.Id;

            var sepetItems = await _sepetService.GetSepetItemsAsync(userId, sessionId);
            if (sepetItems == null || !sepetItems.Any())
            {
                return RedirectToAction("Index", "Sepet");
            }

            await PrepareCheckoutViewDataAsync(userId, sessionId, sepetItems);

            var model = new Siparis();
            
            int? secilenKargoId = HttpContext.Session.GetInt32("SecilenKargoId");
            if (secilenKargoId.HasValue)
            {
                model.KargoFirmasiId = secilenKargoId.Value;
            }

            if (User.Identity?.IsAuthenticated == true)
            {
                var user = await _userManager.GetUserAsync(User);
                if (user != null)
                {
                    var tumAdresler = await _adresService.GetAllAsync();
                    ViewBag.KayitliAdresler = tumAdresler.Where(x => x.AppUserId == user.Id).ToList();

                    model.MusteriAdSoyad = user.AdSoyad;
                    model.Eposta = user.Email ?? string.Empty;
                    model.Telefon = user.PhoneNumber ?? string.Empty;
                    model.Sehir = user.Sehir;
                }
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Odeme(
            [Bind("MusteriAdSoyad,Eposta,Telefon,Sehir,Ilce,AcikAdres,KargoFirmasiId")] Siparis siparis,
            string? yeniAdresBasligi,
            bool adresiKaydet)
        {
            var userId = User.Identity?.IsAuthenticated == true ? _userManager.GetUserId(User) : null;
            var sessionId = HttpContext.Session.Id;

            var sepetItems = await _sepetService.GetSepetItemsAsync(userId, sessionId);
            if (sepetItems == null || !sepetItems.Any())
            {
                return RedirectToAction("Index", "Sepet");
            }

            // Stok ve satın alınabilirlik kontrolü
            foreach (var item in sepetItems)
            {
                if (item.Urun == null || !item.Urun.AktifMi || item.Urun.SilindiMi)
                {
                    TempData["SepetHata"] = "Sepetinizdeki bazı ürünler artık satışta değil.";
                    return RedirectToAction("Index", "Sepet");
                }

                if (item.UrunSecenek != null)
                {
                    if (!item.UrunSecenek.AktifMi || item.UrunSecenek.SilindiMi || !item.UrunSecenek.SatinAlinabilirMi)
                    {
                        TempData["SepetHata"] = $"{item.UrunBaslik} ürününün seçilen varyantı şu anda satışa kapalı.";
                        return RedirectToAction("Index", "Sepet");
                    }

                    if (item.UrunSecenek.StokAdedi > 0 && item.Adet > item.UrunSecenek.StokAdedi && !item.UrunSecenek.OnSipariseAcikMi)
                    {
                        TempData["SepetHata"] = $"{item.UrunBaslik} ürünü için yeterli stok bulunmuyor (Mevcut: {item.UrunSecenek.StokAdedi}).";
                        return RedirectToAction("Index", "Sepet");
                    }
                }
            }

            if (!string.IsNullOrEmpty(userId))
            {
                var currentUser = await _userManager.GetUserAsync(User);
                if (!string.IsNullOrWhiteSpace(currentUser?.Email))
                {
                    siparis.Eposta = currentUser.Email;
                }
            }

            NormalizeCheckoutInput(siparis);

            if (!ValidateCheckoutInput(siparis))
            {
                await PrepareCheckoutViewDataAsync(userId, sessionId, sepetItems);
                ViewBag.FormHata = "Lütfen teslimat bilgilerini eksiksiz ve geçerli formatta doldurun.";
                return View(siparis);
            }

            decimal hamTutar = sepetItems.Sum(x => x.Toplam);

            // Mass-assignment koruması: Sunucu tarafından yönetilen alanları sıfırla
            siparis.Id = 0;
            siparis.FaturaDosyaYolu = null;
            siparis.FaturaDosyaAdi = null;
            siparis.FaturaYuklendiMi = false;
            siparis.FaturaYuklenmeTarihi = null;
            siparis.FaturaMailGonderildiMi = false;
            siparis.OdemeOnayTarihi = null;
            siparis.OdemeHataKodu = null;
            siparis.OdemeHataMesaji = null;

            siparis.SiparisNo = await GenerateUniqueOrderNumberAsync();
            siparis.EmailHashKodu = Guid.NewGuid().ToString("N")[..16];
            siparis.OlusturulmaTarihi = DateTime.UtcNow;
            siparis.Durum = 0;
            siparis.SilindiMi = false;
            siparis.AppUserId = userId;
            siparis.KargoTakipNo = string.Empty;
            siparis.ToplamTutar = hamTutar;
            siparis.IndirimTutari = 0;
            siparis.KuponKodu = null;

            string? kuponKodu = HttpContext.Session.GetString("UygulananKupon");
            if (!string.IsNullOrEmpty(kuponKodu))
            {
                var kupon = await _context.Kuponlar.FirstOrDefaultAsync(x => x.Kod == kuponKodu && !x.SilindiMi);
                if (kupon != null &&
                    kupon.AktifMi &&
                    kupon.SonKullanmaTarihi > DateTime.UtcNow &&
                    (kupon.KullanimLimiti <= 0 || kupon.KullanilanMiktar < kupon.KullanimLimiti) &&
                    hamTutar >= kupon.MinSepetTutari)
                {
                    var indirim = CalculateCouponDiscount(kupon, hamTutar);
                    siparis.IndirimTutari = indirim;
                    siparis.KuponKodu = kupon.Kod;
                    siparis.ToplamTutar = Math.Max(0, siparis.ToplamTutar - indirim);
                }
            }

            // --- KARGO HESAPLAMA VE KAYDETME ---
            var settings = _siteSettingsService.GetSettings();
            var paytrAktif = settings.PaytrAktifMi;
            if (paytrAktif)
            {
                siparis.OdemeSaglayici = "PayTR";
                siparis.OdemeDurumu = "Bekliyor";
                siparis.Aciklama = "PayTR ile güvenli ödeme bekleniyor.";
            }
            else
            {
                siparis.OdemeSaglayici = string.Empty;
                siparis.OdemeDurumu = "OdemeBeklenmiyor";
                siparis.Aciklama = "Onay bekliyor.";
            }
            siparis.OdemeTamamlandiMi = false;

            var secilenKargo = await _context.KargoFirmalari
                .FirstOrDefaultAsync(x => x.Id == siparis.KargoFirmasiId && !x.SilindiMi && x.AktifMi);
            
            if (secilenKargo == null)
            {
                secilenKargo = await _context.KargoFirmalari
                    .FirstOrDefaultAsync(x => x.VarsayilanMi && !x.SilindiMi && x.AktifMi)
                    ?? await _context.KargoFirmalari.FirstOrDefaultAsync(x => !x.SilindiMi && x.AktifMi);
            }

            if (secilenKargo != null)
            {
                siparis.KargoFirmasiId = secilenKargo.Id;
                siparis.KargoFirmasi = secilenKargo.Ad;
            }

            decimal kargoUcreti = 0;
            if (siparis.ToplamTutar < settings.UcretsizKargoLimiti)
            {
                kargoUcreti = secilenKargo?.Fiyat ?? settings.KargoBedeli;
            }

            siparis.KargoUcreti = kargoUcreti;
            siparis.ToplamTutar = siparis.ToplamTutar + kargoUcreti;

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                if (adresiKaydet && !string.IsNullOrEmpty(userId))
                {
                    var mevcutAdresSayisi = await _context.Adresler.CountAsync(x => x.AppUserId == userId && !x.SilindiMi);
                    if (mevcutAdresSayisi < 20)
                    {
                        _context.Adresler.Add(new Adres
                        {
                            AppUserId = userId,
                            Baslik = string.IsNullOrWhiteSpace(yeniAdresBasligi) ? "Yeni Adresim" : yeniAdresBasligi.Trim(),
                            AdSoyad = siparis.MusteriAdSoyad,
                            Telefon = siparis.Telefon,
                            Sehir = siparis.Sehir,
                            Ilce = siparis.Ilce,
                            AcikAdres = siparis.AcikAdres,
                            OlusturulmaTarihi = DateTime.UtcNow,
                            SilindiMi = false
                        });
                    }
                }

                _context.Siparisler.Add(siparis);
                await _context.SaveChangesAsync();

                foreach (var item in sepetItems)
                {
                    var gercekSecenekId = await ResolveOrderVariantIdAsync(item);

                    _context.SiparisDetaylari.Add(new SiparisDetay
                    {
                        SiparisId = siparis.Id,
                        UrunSecenekId = gercekSecenekId,
                        Adet = item.Adet,
                        BirimFiyat = item.Fiyat,
                        OlusturulmaTarihi = DateTime.UtcNow,
                        UrunId = item.UrunId,
                        CerceveModeli = item.CerceveModeli,
                        SecenekAdi = item.SecenekAdi,
                        MusteriNotu = item.MusteriNotu,
                        SilindiMi = false
                    });
                }

                await _context.SaveChangesAsync();

                if (!paytrAktif)
                {
                    await CompleteOrderInventoryAndCouponAsync(siparis);
                    await ClearOrderCartAsync(siparis);
                    await _context.SaveChangesAsync();
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            HttpContext.Session.SetString("SonSiparisNo", siparis.SiparisNo);

            if (!paytrAktif)
            {
                HttpContext.Session.Remove("UygulananKupon");
            }

            _logger.LogInformation(
                "Siparis olusturuldu. SiparisNo={SiparisNo}, Tutar={Tutar}, Saglayici={Saglayici}",
                siparis.SiparisNo,
                siparis.ToplamTutar,
                siparis.OdemeSaglayici);

            // PayTR aktifse PayTR ödeme sayfasına yönlendir (sepet ödeme onaylanana kadar korunur)
            if (paytrAktif)
            {
                return RedirectToAction(nameof(PaytrOdeme), new { siparisNo = siparis.SiparisNo });
            }

            // Hiçbiri aktif değilse siparişi beklemede oluştur
            await SendAdminOrderNotificationEmailAsync(siparis);
            await SendCustomerOrderConfirmationEmailAsync(siparis);

            return RedirectToAction(nameof(Beklemede), new { siparisNo = siparis.SiparisNo });
        }

        /// <summary>
        /// PayTR ödeme sayfası — iframe'i gösterir.
        /// Sipariş oluşturulduktan sonra buraya yönlenir.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> PaytrOdeme(string siparisNo)
        {
            if (string.IsNullOrWhiteSpace(siparisNo))
            {
                return RedirectToAction("Index", "Sepet");
            }

            var siparis = await _context.Siparisler
                .Include(x => x.SiparisDetaylari)
                    .ThenInclude(x => x.Urun)
                .FirstOrDefaultAsync(x => x.SiparisNo == siparisNo && !x.SilindiMi);

            if (siparis == null)
            {
                return RedirectToAction("Index", "Sepet");
            }

            // Sahiplik kontrolü
            var currentUserId = User.Identity?.IsAuthenticated == true ? _userManager.GetUserId(User) : null;
            var sessionSonSiparisNo = HttpContext.Session.GetString("SonSiparisNo");
            bool isOwner = (!string.IsNullOrEmpty(siparis.AppUserId) && siparis.AppUserId == currentUserId)
                || string.Equals(sessionSonSiparisNo, siparis.SiparisNo, StringComparison.Ordinal);

            if (!isOwner)
            {
                return Forbid();
            }

            if (siparis.OdemeTamamlandiMi)
            {
                return RedirectToAction(nameof(PaytrBasarili), new { siparisNo = siparis.SiparisNo });
            }

            if (string.Equals(siparis.OdemeDurumu, "Basarisiz", StringComparison.OrdinalIgnoreCase) ||
                siparis.Durum == SiparisDurumHelper.IptalEdildi)
            {
                return RedirectToAction(nameof(PaytrBasarisiz), new { siparisNo = siparis.SiparisNo });
            }

            var settings = _siteSettingsService.GetSettings();
            var request = HttpContext.Request;
            var baseUrl = $"{request.Scheme}://{request.Host}";

            var sepetItems = siparis.SiparisDetaylari
                .Where(x => !x.SilindiMi)
                .Select(x => new PaymentBasketItem
                {
                    Name = x.Urun?.Baslik ?? $"Ürün #{x.UrunId}",
                    Price = x.BirimFiyat,
                    Quantity = x.Adet
                })
                .ToList();

            if (siparis.KargoUcreti > 0)
            {
                sepetItems.Add(new PaymentBasketItem
                {
                    Name = "Kargo Ücreti",
                    Price = siparis.KargoUcreti,
                    Quantity = 1
                });
            }

            // İndirim kuponu varsa PayTR sepet toplamı ile ToplamTutar uyumsuzluğunu önle
            var basketSum = sepetItems.Sum(x => x.Price * x.Quantity);
            if (Math.Abs(basketSum - siparis.ToplamTutar) > 0.01m)
            {
                sepetItems = new List<PaymentBasketItem>
                {
                    new PaymentBasketItem
                    {
                        Name = $"Sipariş #{siparis.SiparisNo}",
                        Price = siparis.ToplamTutar,
                        Quantity = 1
                    }
                };
            }

            var initRequest = new PaymentInitRequest
            {
                OrderId = siparis.SiparisNo,
                TotalPrice = siparis.ToplamTutar,
                BasketPrice = siparis.ToplamTutar,
                CallbackUrl = !string.IsNullOrWhiteSpace(settings.PaytrCallbackUrl) ? settings.PaytrCallbackUrl : $"{baseUrl}/Siparis/PaytrBildirim",
                OkUrl = $"{baseUrl}/Siparis/PaytrBasarili?siparisNo={Uri.EscapeDataString(siparis.SiparisNo)}",
                FailUrl = $"{baseUrl}/Siparis/PaytrBasarisiz?siparisNo={Uri.EscapeDataString(siparis.SiparisNo)}",
                BuyerName = siparis.MusteriAdSoyad,
                BuyerEmail = siparis.Eposta,
                BuyerPhone = siparis.Telefon,
                BuyerAddress = $"{siparis.AcikAdres}, {siparis.Ilce}/{siparis.Sehir}",
                BuyerCity = siparis.Sehir,
                BuyerIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                BasketItems = sepetItems
            };

            var result = await _paymentService.InitializeCheckoutAsync(initRequest);

            if (!result.Success || string.IsNullOrWhiteSpace(result.Token))
            {
                _logger.LogWarning(
                    "PayTR baslatilamadi. SiparisNo={SiparisNo}, Hata={Hata}",
                    siparisNo, result.ErrorMessage);

                ViewBag.Hata = result.ErrorMessage ?? "Ödeme sayfası başlatılamadı.";
                return View("PaytrOdeme");
            }

            ViewBag.PaytrToken = result.Token;
            ViewBag.SiparisNo = siparis.SiparisNo;
            ViewBag.ToplamTutar = siparis.ToplamTutar;

            return View();
        }

        /// <summary>
        /// PayTR bildirim URL'i — Ödeme sonucunu PayTR arka plandan POST eder.
        /// Bu metoda müsteri uğramaz, session kullanilamaz.
        /// Sadece düz metin "OK" dönülmeli.
        /// </summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [AllowAnonymous]
        public async Task<IActionResult> PaytrBildirim()
        {
            var merchantOid = HttpContext.Request.Form["merchant_oid"].FirstOrDefault() ?? string.Empty;
            var status = HttpContext.Request.Form["status"].FirstOrDefault() ?? string.Empty;
            var totalAmount = HttpContext.Request.Form["total_amount"].FirstOrDefault() ?? string.Empty;
            var hash = HttpContext.Request.Form["hash"].FirstOrDefault() ?? string.Empty;
            var testMode = HttpContext.Request.Form["test_mode"].FirstOrDefault();
            var failedReasonCode = HttpContext.Request.Form["failed_reason_code"].FirstOrDefault();
            var failedReasonMessage = HttpContext.Request.Form["failed_reason_msg"].FirstOrDefault();

            _logger.LogInformation(
                "PayTR bildirim alindi. merchant_oid={Oid}, status={Status}, total_amount={Amount}",
                merchantOid, status, totalAmount);

            if (string.IsNullOrWhiteSpace(merchantOid) || string.IsNullOrWhiteSpace(hash))
            {
                _logger.LogWarning("PayTR bildirimde zorunlu alanlar eksik.");
                return BadRequest("PAYTR notification failed: missing fields");
            }

            // Hash doğrulama
            var verifyRequest = new PaymentVerifyRequest
            {
                MerchantOid = merchantOid,
                Status = status,
                TotalAmount = totalAmount,
                Hash = hash
            };

            var verifyResult = await _paymentService.VerifyPaymentAsync(verifyRequest);

            if (!verifyResult.SignatureValid)
            {
                _logger.LogWarning(
                    "PayTR bildirim hash dogrulama basarisiz. merchant_oid={Oid}", merchantOid);
                return BadRequest("PAYTR notification failed: bad hash");
            }

            // Siparişi bul ve güncelle
            var siparis = await _context.Siparisler
                .FirstOrDefaultAsync(x => x.SiparisNo == merchantOid && !x.SilindiMi);

            if (siparis == null)
            {
                _logger.LogWarning(
                    "PayTR bildirim: Siparis bulunamadi. merchant_oid={Oid}", merchantOid);
                return NotFound("PAYTR notification failed: order not found");
            }

            // PayTR ayni bildirimi birden fazla gonderebilir. Operasyon durumuna
            // degil, odemenin kesin sonucuna bakarak idempotent davran.
            if (string.Equals(siparis.OdemeDurumu, "Basarili", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(siparis.OdemeDurumu, "Basarisiz", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "PayTR bildirim: Odeme zaten islenmis. merchant_oid={Oid}, odemeDurumu={OdemeDurumu}",
                    merchantOid, siparis.OdemeDurumu);
                return Content("OK");
            }

            if (verifyResult.PaymentSuccessful)
            {
                var expectedAmount = Convert.ToInt64(Math.Round(siparis.ToplamTutar * 100));
                if (!long.TryParse(totalAmount, out var paidAmount) || paidAmount != expectedAmount)
                {
                    _logger.LogError(
                        "PayTR tutar uyusmazligi. SiparisNo={SiparisNo}, BeklenenKurus={Expected}, GelenKurus={Received}",
                        siparis.SiparisNo, expectedAmount, totalAmount);
                    return BadRequest("PAYTR notification failed: amount mismatch");
                }

                // Ödeme başarılı — siparişi onayla, stok ve kupon düşümünü yap, üye sepetini temizle
                siparis.OdemeDurumu = "Basarili";
                siparis.OdemeTamamlandiMi = true;
                siparis.OdemeOnayTarihi = DateTime.UtcNow;
                siparis.OdemeHataKodu = null;
                siparis.OdemeHataMesaji = null;
                siparis.Durum = SiparisDurumHelper.UretimHazirlaniyor;
                siparis.Aciklama = "PayTR ile ödeme başarıyla tamamlandı.";

                await CompleteOrderInventoryAndCouponAsync(siparis);
                if (!string.IsNullOrWhiteSpace(siparis.AppUserId))
                {
                    await ClearOrderCartAsync(siparis);
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "PayTR odeme basarili. SiparisNo={SiparisNo}, Tutar={Tutar}",
                    siparis.SiparisNo, verifyResult.PaidPrice);

                // Bildirim e-postalarını gönder
                await SendAdminOrderNotificationEmailAsync(siparis);
                await SendCustomerOrderConfirmationEmailAsync(siparis);
            }
            else
            {
                // Ödeme başarısız — merkezi durum tanımına göre 4 = İptal.
                siparis.OdemeDurumu = "Basarisiz";
                siparis.OdemeTamamlandiMi = false;
                siparis.OdemeOnayTarihi = null;
                siparis.OdemeHataKodu = failedReasonCode?.Trim();
                siparis.OdemeHataMesaji = failedReasonMessage?.Trim();
                siparis.Durum = SiparisDurumHelper.IptalEdildi;
                siparis.Aciklama = string.IsNullOrWhiteSpace(failedReasonMessage)
                    ? "PayTR ödemesi başarısız; tahsilat yapılmadı."
                    : $"PayTR ödemesi başarısız: {failedReasonMessage.Trim()}";

                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "PayTR odeme basarisiz. SiparisNo={SiparisNo}", siparis.SiparisNo);
            }

            return Content("OK");
        }

        /// <summary>
        /// PayTR ödeme başarılı sayfası — Müşteri buraya yönlenir.
        /// UYARI: Sadece yönlendirme sayfasıdır! Gerçek onay Bildirim URL'dendir.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> PaytrBasarili(string siparisNo)
        {
            if (string.IsNullOrWhiteSpace(siparisNo))
            {
                return RedirectToAction("Index", "Sepet");
            }

            var siparis = await _context.Siparisler
                .FirstOrDefaultAsync(x => x.SiparisNo == siparisNo && !x.SilindiMi);

            if (siparis == null)
            {
                return RedirectToAction("Index", "Sepet");
            }

            if (string.Equals(siparis.OdemeDurumu, "Basarisiz", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction(nameof(PaytrBasarisiz), new { siparisNo });
            }

            var currentUserId = User.Identity?.IsAuthenticated == true ? _userManager.GetUserId(User) : null;
            var isAuthenticatedOwner = !string.IsNullOrEmpty(currentUserId) && siparis.AppUserId == currentUserId;
            var isSessionOwner = string.Equals(HttpContext.Session.GetString("SonSiparisNo"), siparis.SiparisNo, StringComparison.Ordinal);

            ViewBag.SiparisNo = siparisNo;
            ViewBag.SiparisId = isAuthenticatedOwner ? siparis.Id : 0;

            if (isAuthenticatedOwner || isSessionOwner)
            {
                HttpContext.Session.Remove("UygulananKupon");
                if (siparis.OdemeTamamlandiMi)
                {
                    await ClearOrderCartAsync(siparis);
                    await _context.SaveChangesAsync();
                }
            }

            // Ödeme başarılı mı kontrol et
            if (!siparis.OdemeTamamlandiMi)
            {
                ViewBag.OdemeBekliyor = true;
            }

            return View();
        }

        /// <summary>
        /// PayTR ödeme başarısız sayfası
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> PaytrBasarisiz(string? siparisNo = null)
        {
            if (!string.IsNullOrWhiteSpace(siparisNo))
            {
                var siparis = await _context.Siparisler
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.SiparisNo == siparisNo && !x.SilindiMi);

                if (siparis != null)
                {
                    ViewBag.SiparisNo = siparis.SiparisNo;
                    if (!string.IsNullOrWhiteSpace(siparis.OdemeHataMesaji))
                    {
                        ViewBag.Hata = siparis.OdemeHataMesaji;
                    }
                }
            }

            return View();
        }

        public IActionResult Beklemede(string siparisNo)
        {
            ViewBag.SiparisNo = siparisNo;
            return View();
        }

        public IActionResult Basarili(string siparisNo)
        {
            ViewBag.SiparisNo = siparisNo;
            return View();
        }

        public IActionResult Basarisiz()
        {
            return View();
        }

        [AllowAnonymous]
        public async Task<IActionResult> FaturaIndir(int id, string? token = null)
        {
            var siparis = await _context.Siparisler
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id && !x.SilindiMi);
            if (siparis == null)
            {
                return NotFound("Sipariş bulunamadı.");
            }

            bool yetkiliMi = false;

            if (!string.IsNullOrWhiteSpace(token) &&
                !string.IsNullOrWhiteSpace(siparis.EmailHashKodu) &&
                string.Equals(token, siparis.EmailHashKodu, StringComparison.Ordinal))
            {
                yetkiliMi = true;
            }
            else if (User.Identity?.IsAuthenticated == true)
            {
                var user = await _userManager.GetUserAsync(User);
                if (user != null)
                {
                    if (!string.IsNullOrEmpty(siparis.AppUserId) && siparis.AppUserId == user.Id)
                    {
                        yetkiliMi = true;
                    }
                    else if (string.IsNullOrEmpty(siparis.AppUserId) &&
                             !string.IsNullOrWhiteSpace(user.Email) &&
                             string.Equals(siparis.Eposta, user.Email, StringComparison.OrdinalIgnoreCase))
                    {
                        yetkiliMi = true;
                    }
                }
            }

            if (!yetkiliMi)
            {
                return User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();
            }

            if (string.IsNullOrWhiteSpace(siparis.FaturaDosyaYolu))
            {
                return NotFound("Fatura henüz yüklenmemiş.");
            }

            var webRoot = Path.GetFullPath(_env.WebRootPath);
            var relativePath = siparis.FaturaDosyaYolu.TrimStart('/', '\\');
            var filePath = Path.GetFullPath(Path.Combine(webRoot, relativePath));

            if (!filePath.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return BadRequest("Geçersiz dosya yolu.");
            }

            if (!System.IO.File.Exists(filePath))
            {
                return NotFound("Fatura dosyası bulunamadı.");
            }

            var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
            var safeDownloadName = Path.GetFileName(siparis.FaturaDosyaAdi ?? $"fatura_{id}.pdf");
            return File(fileBytes, "application/pdf", safeDownloadName);
        }

        private async Task PrepareCheckoutViewDataAsync(string? userId, string sessionId, List<SepetItem>? sepetItems = null)
        {
            sepetItems ??= await _sepetService.GetSepetItemsAsync(userId, sessionId);
            var araToplam = sepetItems.Sum(x => x.Toplam);
            decimal indirimTutari = 0;
            var kuponKodu = HttpContext.Session.GetString("UygulananKupon");

            if (!string.IsNullOrEmpty(kuponKodu))
            {
                var kupon = await _context.Kuponlar.FirstOrDefaultAsync(x => x.Kod == kuponKodu && !x.SilindiMi);
                if (kupon != null &&
                    kupon.AktifMi &&
                    kupon.SonKullanmaTarihi > DateTime.UtcNow &&
                    (kupon.KullanimLimiti <= 0 || kupon.KullanilanMiktar < kupon.KullanimLimiti) &&
                    araToplam >= kupon.MinSepetTutari)
                {
                    indirimTutari = CalculateCouponDiscount(kupon, araToplam);
                }
                else
                {
                    HttpContext.Session.Remove("UygulananKupon");
                    kuponKodu = null;
                }
            }

            var settings = _siteSettingsService.GetSettings();
            var kargoFirmalari = await _context.KargoFirmalari
                .Where(x => !x.SilindiMi && x.AktifMi)
                .OrderByDescending(x => x.VarsayilanMi)
                .ThenBy(x => x.Ad)
                .ToListAsync();

            int? secilenKargoId = HttpContext.Session.GetInt32("SecilenKargoId");
            var varsayilanKargo = kargoFirmalari.FirstOrDefault(x => x.Id == secilenKargoId)
                ?? kargoFirmalari.FirstOrDefault(x => x.VarsayilanMi) 
                ?? kargoFirmalari.FirstOrDefault();
            decimal varsayilanKargoUcreti = varsayilanKargo?.Fiyat ?? settings.KargoBedeli;

            decimal sepetToplamiIndirimli = araToplam - indirimTutari;
            decimal gosterilecekKargoBedeli = sepetToplamiIndirimli >= settings.UcretsizKargoLimiti ? 0 : varsayilanKargoUcreti;

            ViewBag.AraToplam = araToplam;
            ViewBag.IndirimTutari = indirimTutari;
            ViewBag.KuponKodu = kuponKodu;
            ViewBag.UcretsizKargoLimiti = settings.UcretsizKargoLimiti;
            ViewBag.GosterilecekKargoBedeli = gosterilecekKargoBedeli;
            ViewBag.ToplamTutar = Math.Max(0, sepetToplamiIndirimli + gosterilecekKargoBedeli);
            ViewBag.SepetItems = sepetItems;
            ViewBag.KargoFirmalari = kargoFirmalari;

            if (!string.IsNullOrWhiteSpace(userId))
            {
                var tumAdresler = await _adresService.GetAllAsync();
                ViewBag.KayitliAdresler = tumAdresler.Where(x => x.AppUserId == userId).ToList();
            }
        }

        private static void NormalizeCheckoutInput(Siparis siparis)
        {
            siparis.MusteriAdSoyad = siparis.MusteriAdSoyad?.Trim() ?? string.Empty;
            siparis.Eposta = siparis.Eposta?.Trim() ?? string.Empty;
            siparis.Sehir = siparis.Sehir?.Trim() ?? string.Empty;
            siparis.Ilce = siparis.Ilce?.Trim() ?? string.Empty;
            siparis.AcikAdres = siparis.AcikAdres?.Trim() ?? string.Empty;

            var temizTel = new string((siparis.Telefon ?? string.Empty).Where(char.IsDigit).ToArray());
            if (!temizTel.StartsWith("0") && temizTel.Length == 10)
            {
                temizTel = "0" + temizTel;
            }

            siparis.Telefon = temizTel;
        }

        private bool ValidateCheckoutInput(Siparis siparis)
        {
            if (string.IsNullOrWhiteSpace(siparis.MusteriAdSoyad))
            {
                ModelState.AddModelError(nameof(Siparis.MusteriAdSoyad), "Ad soyad zorunludur.");
            }

            if (string.IsNullOrWhiteSpace(siparis.Eposta) || !IsValidEmail(siparis.Eposta))
            {
                ModelState.AddModelError(nameof(Siparis.Eposta), "Gecerli bir e-posta adresi girilmelidir.");
            }

            if (siparis.Telefon.Length != 11 || !siparis.Telefon.StartsWith("0"))
            {
                ModelState.AddModelError(nameof(Siparis.Telefon), "Telefon 0 ile baslayan 11 haneli bir numara olmalidir.");
            }

            if (string.IsNullOrWhiteSpace(siparis.Sehir))
            {
                ModelState.AddModelError(nameof(Siparis.Sehir), "Sehir zorunludur.");
            }

            if (string.IsNullOrWhiteSpace(siparis.Ilce))
            {
                ModelState.AddModelError(nameof(Siparis.Ilce), "Ilce zorunludur.");
            }

            if (string.IsNullOrWhiteSpace(siparis.AcikAdres) || siparis.AcikAdres.Length < 10)
            {
                ModelState.AddModelError(nameof(Siparis.AcikAdres), "Acik adres en az 10 karakter olmalidir.");
            }

            return ModelState.IsValid;
        }

        private static bool IsValidEmail(string email)
        {
            try
            {
                var address = new System.Net.Mail.MailAddress(email);
                return string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private async Task<string> GenerateUniqueOrderNumberAsync()
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var candidate = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + Random.Shared.Next(100, 999);
                if (!await _context.Siparisler.AnyAsync(x => x.SiparisNo == candidate))
                {
                    return candidate;
                }
            }

            return $"{DateTime.UtcNow:yyyyMMddHHmmssfff}{Guid.NewGuid():N}"[..24];
        }

        private async Task<int?> ResolveOrderVariantIdAsync(SepetItem item)
        {
            if (item.UrunSecenekId.HasValue)
            {
                return item.UrunSecenekId.Value;
            }

            if (!string.IsNullOrWhiteSpace(item.SecenekAdi) && item.SecenekAdi.Contains("Özel Ölçü", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var varsayilan = await _context.UrunSecenekleri
                .AsNoTracking()
                .Where(x =>
                    x.UrunId == item.UrunId &&
                    !x.SilindiMi &&
                    x.AktifMi &&
                    (!x.TukeninceGizle || x.StokAdedi > 0 || x.OnSipariseAcikMi))
                .OrderByDescending(x => x.VarsayilanMi)
                .ThenBy(x => x.Sira)
                .ThenBy(x => x.SatisFiyati)
                .FirstOrDefaultAsync();

            return varsayilan?.Id;
        }

        private static decimal CalculateCouponDiscount(Kupon kupon, decimal sepetTutari)
        {
            var discount = kupon.Tip == 0
                ? sepetTutari * (kupon.Deger / 100)
                : kupon.Deger;

            return Math.Round(Math.Min(sepetTutari, Math.Max(0, discount)), 2);
        }

        private async Task CompleteOrderInventoryAndCouponAsync(Siparis siparis)
        {
            var detaylar = await _context.SiparisDetaylari
                .Include(x => x.Urun)
                .Include(x => x.UrunSecenek)
                .Where(x => x.SiparisId == siparis.Id && !x.SilindiMi)
                .ToListAsync();

            foreach (var detay in detaylar)
            {
                if (detay.UrunSecenek != null && detay.UrunSecenek.StokAdedi > 0)
                {
                    detay.UrunSecenek.StokAdedi = Math.Max(0, detay.UrunSecenek.StokAdedi - detay.Adet);
                }

                if (detay.Urun != null)
                {
                    detay.Urun.SatisSayisi += detay.Adet;
                }
            }

            if (!string.IsNullOrWhiteSpace(siparis.KuponKodu))
            {
                await _context.Kuponlar
                    .Where(k => k.Kod == siparis.KuponKodu && !k.SilindiMi && (k.KullanimLimiti <= 0 || k.KullanilanMiktar < k.KullanimLimiti))
                    .ExecuteUpdateAsync(s => s.SetProperty(k => k.KullanilanMiktar, k => k.KullanilanMiktar + 1));
            }
        }

        private async Task SendAdminOrderNotificationEmailAsync(Siparis siparis)
        {
            var settings = _siteSettingsService.GetSettings();
            if (!settings.YeniSiparisMailBildirimi)
            {
                return;
            }

            var recipientEmail = string.IsNullOrWhiteSpace(settings.BildirimAliciEmail)
                ? settings.Email
                : settings.BildirimAliciEmail;

            if (string.IsNullOrWhiteSpace(recipientEmail))
            {
                return;
            }

            var brandName = string.IsNullOrWhiteSpace(settings.MarkaAdi) ? settings.SiteAdi : settings.MarkaAdi;
            var detailUrl = Url.Action("Detay", "Siparis", new { area = "Admin", id = siparis.Id }, Request.Scheme)
                ?? $"{Request.Scheme}://{Request.Host}/Admin/Siparis/Detay/{siparis.Id}";

            var customerName = System.Net.WebUtility.HtmlEncode(siparis.MusteriAdSoyad ?? string.Empty);
            var customerEmail = System.Net.WebUtility.HtmlEncode(siparis.Eposta ?? string.Empty);
            var orderNumber = System.Net.WebUtility.HtmlEncode(siparis.SiparisNo ?? string.Empty);
            var odemeDurumuText = siparis.OdemeTamamlandiMi ? "Ödendi (PayTR Onaylı)" : "Beklemede";
            var orderItemsHtml = await BuildOrderItemsTableRowsAsync(siparis.Id);

            var body = $@"
                <h3>Yeni sipari&#351; al&#305;nd&#305;</h3>
                <p><strong>Sipari&#351; No:</strong> {orderNumber}</p>
                <p><strong>M&uuml;&#351;teri:</strong> {customerName}</p>
                <p><strong>E-posta:</strong> {customerEmail}</p>
                <p><strong>&Ouml;deme Durumu:</strong> {odemeDurumuText}</p>
                <p><strong>Tutar:</strong> {siparis.ToplamTutar:N2} TL</p>
                <table role='presentation' width='100%' cellpadding='0' cellspacing='0' style='border:1px solid #e5e2dc; border-radius:10px; background:#fff; margin:16px 0;'>
                    <thead>
                        <tr style='background:#fafaf8;'>
                            <th style='padding:10px; text-align:left; color:#1B2A4A;'>Ürün</th>
                            <th style='padding:10px; text-align:center; color:#1B2A4A;'>Adet</th>
                            <th style='padding:10px; text-align:right; color:#1B2A4A;'>Tutar</th>
                        </tr>
                    </thead>
                    <tbody>{orderItemsHtml}</tbody>
                </table>
                <p><a href=""{detailUrl}"">Admin panelinde sipari&#351;i g&ouml;r&uuml;nt&uuml;le</a></p>";

            try
            {
                await _emailService.SendTemplateMailAsync(
                    recipientEmail,
                    $"{brandName} - Yeni Sipari\u015F {siparis.SiparisNo}",
                    "Operasyon Ekibi",
                    body);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Yeni siparis mail bildirimi gonderilemedi. SiparisNo={SiparisNo}", siparis.SiparisNo);
            }
        }

        private async Task SendCustomerOrderConfirmationEmailAsync(Siparis siparis)
        {
            if (string.IsNullOrWhiteSpace(siparis.Eposta))
            {
                _logger.LogWarning("Musteri emaili eksik, siparis onay maili gonderilemedi. SiparisNo={SiparisNo}", siparis.SiparisNo);
                return;
            }

            var settings = _siteSettingsService.GetSettings();
            var brandName = string.IsNullOrWhiteSpace(settings.MarkaAdi) ? settings.SiteAdi : settings.MarkaAdi;
            var siteUrl = _siteSettingsService.BuildAbsoluteUrl(string.Empty);

            var detaylar = await _context.SiparisDetaylari
                .Include(x => x.Urun)
                .Include(x => x.UrunSecenek)
                .Where(x => x.SiparisId == siparis.Id && !x.SilindiMi)
                .ToListAsync();

            var urunListesi = new System.Text.StringBuilder();
            foreach (var item in detaylar)
            {
                var urunAdi = System.Net.WebUtility.HtmlEncode(item.Urun?.Baslik ?? "Ürün");
                var detayMetni = System.Net.WebUtility.HtmlEncode(BuildOrderLineDetail(item));
                var adet = item.Adet;
                var fiyat = item.BirimFiyat * item.Adet;
                var notSatiri = !string.IsNullOrWhiteSpace(item.MusteriNotu)
                    ? $"<div style='margin-top:4px; font-size:12px; color:#b58735; font-style:italic;'>Not: {System.Net.WebUtility.HtmlEncode(item.MusteriNotu)}</div>"
                    : string.Empty;
                urunListesi.Append($@"
                    <tr>
                        <td style='padding:12px; border-bottom:1px solid #e5e2dc; color:#47473d;'>
                            <div>{urunAdi}</div>
                            {(string.IsNullOrWhiteSpace(detayMetni) ? string.Empty : $"<div style='margin-top:4px; font-size:12px; color:#6f6a5e;'>{detayMetni}</div>")}
                            {notSatiri}
                        </td>
                        <td style='padding:12px; border-bottom:1px solid #e5e2dc; text-align:center; color:#47473d;'>{adet}</td>
                        <td style='padding:12px; border-bottom:1px solid #e5e2dc; text-align:right; color:#1B2A4A; font-weight:600;'>{fiyat:N2} TL</td>
                    </tr>");
            }

            var musteriAdSoyad = System.Net.WebUtility.HtmlEncode(siparis.MusteriAdSoyad ?? "Değerli Müşterimiz");
            var siparisNo = System.Net.WebUtility.HtmlEncode(siparis.SiparisNo ?? "");
            var toplamTutar = siparis.ToplamTutar;
            var teslimatAdresi = $"{siparis.AcikAdres}, {siparis.Ilce}/{siparis.Sehir}";
            var teslimatBilgi = System.Net.WebUtility.HtmlEncode(teslimatAdresi);
            var girisMesaji = siparis.OdemeTamamlandiMi
                ? "Sipari&#351;inizi ald&#305;k ve &ouml;demeniz onayland&#305;. Sipari&#351;iniz &uuml;retim a&#351;amas&#305;na al&#305;nm&#305;&#351;t&#305;r."
                : "Sipari&#351;inizi ald&#305;k. &Ouml;deme/onay s&uuml;reci tamamland&#305;&#287;&#305;nda sizi bilgilendirece&#287;iz.";

            var content = $@"
                <p style='margin-bottom:20px;'>Merhaba <strong>{musteriAdSoyad}</strong>,</p>
                <p style='margin-bottom:20px;'>{girisMesaji}</p>
                
                <table role='presentation' width='100%' cellpadding='0' cellspacing='0' style='border:1px solid #e5e2dc; border-radius:12px; background:#fff; margin:20px 0;'>
                    <thead>
                        <tr style='background:#fafaf8;'>
                            <th style='padding:12px; border-bottom:2px solid #e5e2dc; text-align:left; color:#1B2A4A; font-size:13px;'>Ürün</th>
                            <th style='padding:12px; border-bottom:2px solid #e5e2dc; text-align:center; color:#1B2A4A; font-size:13px;'>Adet</th>
                            <th style='padding:12px; border-bottom:2px solid #e5e2dc; text-align:right; color:#1B2A4A; font-size:13px;'>Tutar</th>
                        </tr>
                    </thead>
                    <tbody>
                        {urunListesi}
                    </tbody>
                    <tfoot>
                        <tr style='background:#fafaf8;'>
                            <td colspan='2' style='padding:14px; border-top:2px solid #e5e2dc; text-align:right; color:#313511; font-weight:700;'>Toplam:</td>
                            <td style='padding:14px; border-top:2px solid #e5e2dc; text-align:right; color:#01ADD3; font-size:18px; font-weight:700;'>{toplamTutar:N2} TL</td>
                        </tr>
                    </tfoot>
                </table>
                
                <table role='presentation' width='100%' cellpadding='0' cellspacing='0' style='border:1px solid #e5e2dc; border-radius:12px; background:#fffaf0; margin:20px 0;'>
                    <tr>
                        <td style='padding:16px; color:#47473d;'>
                            <strong style='color:#1B2A4A;'>Sipari&#351; No:</strong> <span style='font-size:16px; color:#01ADD3; font-weight:700;'>{siparisNo}</span>
                        </td>
                    </tr>
                    <tr>
                        <td style='padding:16px; border-top:1px solid #e5e2dc; color:#47473d;'>
                            <strong style='color:#1B2A4A;'>Teslimat Adresi:</strong> {teslimatBilgi}
                        </td>
                    </tr>
                </table>
                
                <p style='margin-top:24px; color:#47473d; font-size:14px;'>
                    Sipari&#351;inizin durumunu <a href='{siteUrl}/Profil/Siparislerim' style='color:#01ADD3; font-weight:600; text-decoration:underline;'>sipari&#351;lerim</a> sayfas&#305;ndan takip edebilirsiniz.
                </p>
                <p style='margin-top:16px; color:#47473d; font-size:14px;'>
                    Herhangi bir sorunuz veya talebiniz olursa bizimle <a href='{siteUrl}/Kurumsal/Iletisim' style='color:#01ADD3; font-weight:600; text-decoration:underline;'>ileti&#351;ime</a> geçebilirsiniz.
                </p>
                <p style='margin-top:24px; color:#999; font-size:13px;'>
                    Bu e-posta otomatik olarak g&ouml;nderilmi&#351;tir. L&uuml;tfen bu mesaj&#305; do&#287;rudan yan&#305;tlamay&#305;n.
                </p>";

            try
            {
                await _emailService.SendTemplateMailAsync(
                    siparis.Eposta,
                    $"{brandName} - Sipari\u015Finiz Al\u0131nd\u0131 ({siparisNo})",
                    musteriAdSoyad,
                    content,
                    "",
                    "");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Musteri siparis onay maili gonderilemedi. SiparisNo={SiparisNo}, Email={Email}", siparis.SiparisNo, siparis.Eposta);
            }
        }

        private async Task<string> BuildOrderItemsTableRowsAsync(int siparisId)
        {
            var detaylar = await _context.SiparisDetaylari
                .Include(x => x.Urun)
                .Include(x => x.UrunSecenek)
                .Where(x => x.SiparisId == siparisId && !x.SilindiMi)
                .ToListAsync();

            var rows = new System.Text.StringBuilder();
            foreach (var item in detaylar)
            {
                var urunAdi = System.Net.WebUtility.HtmlEncode(item.Urun?.Baslik ?? "Ürün");
                var detayMetni = System.Net.WebUtility.HtmlEncode(BuildOrderLineDetail(item));
                var detayHtml = string.IsNullOrWhiteSpace(detayMetni)
                    ? string.Empty
                    : $"<div style='margin-top:4px; font-size:12px; color:#6f6a5e;'>{detayMetni}</div>";

                rows.Append($@"
                    <tr>
                        <td style='padding:10px; border-top:1px solid #e5e2dc; color:#47473d;'>
                            <div>{urunAdi}</div>
                            {detayHtml}
                        </td>
                        <td style='padding:10px; border-top:1px solid #e5e2dc; text-align:center; color:#47473d;'>{item.Adet}</td>
                        <td style='padding:10px; border-top:1px solid #e5e2dc; text-align:right; color:#1B2A4A; font-weight:600;'>{(item.BirimFiyat * item.Adet):N2} TL</td>
                    </tr>");
            }

            return rows.ToString();
        }

        private static string BuildOrderLineDetail(SiparisDetay item)
        {
            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(item.SecenekAdi))
            {
                details.Add(item.SecenekAdi);
            }
            else
            {
                var variant = item.UrunSecenek;
                if (variant != null)
                {
                    var variantText = string.IsNullOrWhiteSpace(variant.VaryantBasligi)
                        ? variant.Olcu
                        : variant.VaryantBasligi;

                    if (!string.IsNullOrWhiteSpace(variantText) &&
                        !variantText.Contains("Standart", StringComparison.OrdinalIgnoreCase))
                    {
                        details.Add(variantText);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(item.CerceveModeli) && item.CerceveModeli != "Çerçevesiz")
            {
                details.Add($"Çerçeve: {item.CerceveModeli}");
            }

            return string.Join(" | ", details);
        }

        private async Task ClearOrderCartAsync(Siparis siparis)
        {
            Sepet? sepet = null;

            if (!string.IsNullOrWhiteSpace(siparis.AppUserId))
            {
                sepet = await _context.Sepetler
                    .Include(x => x.SepetItems.Where(i => !i.SilindiMi))
                    .FirstOrDefaultAsync(x => x.AppUserId == siparis.AppUserId && !x.SilindiMi);
            }
            else
            {
                var sessionId = HttpContext.Session.Id;
                sepet = await _context.Sepetler
                    .Include(x => x.SepetItems.Where(i => !i.SilindiMi))
                    .FirstOrDefaultAsync(x =>
                        x.SessionId == sessionId &&
                        string.IsNullOrEmpty(x.AppUserId) &&
                        !x.SilindiMi);
            }

            if (sepet == null)
            {
                return;
            }

            foreach (var item in sepet.SepetItems.Where(x => !x.SilindiMi))
            {
                item.SilindiMi = true;
            }

            sepet.SonGuncellemeTarihi = DateTime.UtcNow;
        }
    }
}
