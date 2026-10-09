using System.Text;
using KanvasProje.Core.Helpers;
using KanvasProje.Core.Interfaces;
using KanvasProje.Core.Varliklar;
using KanvasProje.Data;
using KanvasProje.Service.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace KanvasProje.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class SiparisController : AdminBaseController
    {
        private readonly IService<Siparis> _siparisService;
        private readonly IService<SiparisDetay> _detayService;
        private readonly IService<UrunSecenek> _secenekService;
        private readonly IService<Urun> _urunService;
        private readonly IEmailService _emailService;
        private readonly ISiteSettingsService _siteSettingsService;
        private readonly KanvasDbContext _context;
        private readonly IWebHostEnvironment _env;

        public SiparisController(
            IService<Siparis> siparisService,
            IService<SiparisDetay> detayService,
            IService<UrunSecenek> secenekService,
            IService<Urun> urunService,
            IEmailService emailService,
            ISiteSettingsService siteSettingsService,
            KanvasDbContext context,
            IWebHostEnvironment env)
        {
            _siparisService = siparisService;
            _detayService = detayService;
            _secenekService = secenekService;
            _urunService = urunService;
            _emailService = emailService;
            _siteSettingsService = siteSettingsService;
            _context = context;
            _env = env;
        }

        public async Task<IActionResult> Index(string search, int? durum, int page = 1, int pageSize = 20)
        {
            page = Math.Max(page, 1);
            var allowedPageSizes = new[] { 20, 50, 100 };
            if (!allowedPageSizes.Contains(pageSize))
            {
                pageSize = 20;
            }

            var siparisler = await _siparisService.GetAllAsync();
            var siparisDetaylari = await _detayService.GetAllAsync();
            var siparisDetayOzetleri = siparisDetaylari
                .GroupBy(x => x.SiparisId)
                .ToDictionary(
                    x => x.Key,
                    x => $"{x.Count()} ürün / {x.Sum(v => v.Adet)} adet");
            var sorgu = siparisler.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim().ToLowerInvariant();
                sorgu = sorgu.Where(x =>
                    x.Id.ToString().Contains(search) ||
                    (!string.IsNullOrWhiteSpace(x.SiparisNo) && x.SiparisNo.ToLowerInvariant().Contains(search)) ||
                    (!string.IsNullOrWhiteSpace(x.MusteriAdSoyad) && x.MusteriAdSoyad.ToLowerInvariant().Contains(search)) ||
                    (!string.IsNullOrWhiteSpace(x.Telefon) && x.Telefon.Contains(search)) ||
                    (!string.IsNullOrWhiteSpace(x.Eposta) && x.Eposta.ToLowerInvariant().Contains(search)));
            }

            if (durum.HasValue)
            {
                sorgu = sorgu.Where(x => x.Durum == durum.Value);
            }

            ViewBag.CurrentSearch = search;
            ViewBag.CurrentDurum = durum;
            ViewBag.TumuCount = siparisler.Count();
            ViewBag.YeniCount = siparisler.Count(x => x.Durum == SiparisDurumHelper.SiparisAlindi);
            ViewBag.HazirlaniyorCount = siparisler.Count(x => x.Durum == SiparisDurumHelper.UretimHazirlaniyor);
            ViewBag.PaketleniyorCount = siparisler.Count(x => x.Durum == SiparisDurumHelper.Paketleniyor);
            ViewBag.KargodaCount = siparisler.Count(x => x.Durum == SiparisDurumHelper.KargoyaVerildi);
            ViewBag.TeslimCount = siparisler.Count(x => x.Durum == SiparisDurumHelper.TeslimEdildi);
            ViewBag.FaturaYuklenmemişCount = siparisler.Count(x => !x.FaturaYuklendiMi);
            ViewBag.SiparisDetayOzetleri = siparisDetayOzetleri;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.PageSizeOptions = allowedPageSizes;
            ViewBag.TotalCount = sorgu.Count();
            ViewBag.TotalPages = (int)Math.Ceiling((double)ViewBag.TotalCount / pageSize);

            return View(sorgu
                .OrderByDescending(x => x.OlusturulmaTarihi)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList());
        }

        public async Task<IActionResult> Detay(int id)
        {
            var siparis = await _siparisService.GetByIdAsync(id);
            if (siparis == null)
            {
                return NotFound();
            }

            var siparisUrunleri = await _context.SiparisDetaylari
                .AsNoTracking()
                .Where(x => x.SiparisId == id && !x.SilindiMi)
                .Include(x => x.Urun)
                .Include(x => x.UrunSecenek)
                .ToListAsync();

            var urunBilgileri = siparisUrunleri.Select(item =>
            {
                var secenek = item.UrunSecenek;
                var urun = item.Urun ?? (secenek?.Urun);
                if (urun == null) return (dynamic?)null;

                var varyasyonMetni = !string.IsNullOrWhiteSpace(item.SecenekAdi)
                    ? item.SecenekAdi
                    : (string.IsNullOrWhiteSpace(secenek?.VaryantBasligi) ? "Varsayılan varyasyon" : secenek.VaryantBasligi);
                var olcuMetni = !string.IsNullOrWhiteSpace(item.SecenekAdi) ? item.SecenekAdi : secenek?.Olcu;

                return new
                {
                    Baslik = urun.Baslik,
                    Resim = !string.IsNullOrWhiteSpace(secenek?.GorselUrl) ? secenek.GorselUrl : urun.AnaGorselUrl,
                    Olcu = olcuMetni,
                    Cerceve = secenek?.CerceveTipi,
                    Secenek = varyasyonMetni,
                    SecenekDetay = secenek?.VaryantOzeti,
                    CerceveModeli = item.CerceveModeli,
                    Adet = item.Adet,
                    Fiyat = item.BirimFiyat,
                    Toplam = item.Adet * item.BirimFiyat,
                    MusteriNotu = item.MusteriNotu
                };
            }).Where(x => x != null).ToList();

            ViewBag.UrunBilgileri = urunBilgileri;
            ViewBag.KargoFirmalari = await _context.KargoFirmalari
                .AsNoTracking()
                .Where(x => !x.SilindiMi && x.AktifMi)
                .OrderByDescending(x => x.VarsayilanMi)
                .ThenBy(x => x.Ad)
                .ToListAsync();
            ViewBag.SeciliKargoFirmasi = await ResolveKargoFirmasiAsync(siparis);
            ViewBag.SiteSettings = _siteSettingsService.GetSettings();
            return View(siparis);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DurumGuncelle(int id, int durum, string kargoNo, int? kargoFirmasiId)
        {
            var siparis = await _siparisService.GetByIdAsync(id);
            if (siparis == null)
            {
                TempData["Mesaj"] = "Sipariş bulunamadı.";
                TempData["Durum"] = "danger";
                return RedirectToAction("Detay", new { id });
            }

            var eskiDurum = siparis.Durum;
            var temizKargoNo = kargoNo?.Trim() ?? string.Empty;
            var firma = await ResolveKargoFirmasiAsync(siparis, kargoFirmasiId);

            var odemeBekleyenPaytrSiparisi =
                string.Equals(siparis.OdemeSaglayici, "PayTR", StringComparison.OrdinalIgnoreCase) &&
                !siparis.OdemeTamamlandiMi;

            if (odemeBekleyenPaytrSiparisi &&
                durum != eskiDurum &&
                durum != SiparisDurumHelper.IptalEdildi)
            {
                TempData["Mesaj"] = "PayTR tahsilatı doğrulanmamış bu sipariş üretim veya kargo aşamasına alınamaz.";
                TempData["Durum"] = "danger";
                return RedirectToAction("Detay", new { id });
            }

            if (durum != eskiDurum && !CanMoveOrderStatusForward(eskiDurum, durum))
            {
                TempData["Mesaj"] = $"Sipariş durumu '{SiparisDurumHelper.GetLabel(eskiDurum)}' aşamasından '{SiparisDurumHelper.GetLabel(durum)}' aşamasına geri alınamaz.";
                TempData["Durum"] = "warning";
                return RedirectToAction("Detay", new { id });
            }

            siparis.Durum = durum;
            siparis.KargoFirmasiId = firma?.Id;
            siparis.KargoFirmasi = firma?.Ad;
            if (!string.IsNullOrWhiteSpace(temizKargoNo))
            {
                siparis.KargoTakipNo = temizKargoNo;
            }

            await _siparisService.UpdateAsync(siparis);

            if (durum == eskiDurum)
            {
                TempData["Mesaj"] = "Sipariş operasyon bilgileri güncellendi.";
                TempData["Durum"] = "success";
                return RedirectToAction("Detay", new { id });
            }

            var mailSonucu = await SendStatusNotificationAsync(siparis, eskiDurum, durum, firma?.Ad ?? "Aras Kargo", temizKargoNo);
            if (mailSonucu.Success)
            {
                TempData["Mesaj"] = "Sipariş durumu güncellendi. Müşteriye bilgilendirme e-postası gönderildi.";
                TempData["Durum"] = "success";
            }
            else
            {
                TempData["Mesaj"] = $"Sipariş durumu güncellendi. E-posta gönderimi atlandı: {mailSonucu.Message}";
                TempData["Durum"] = "warning";
            }

            return RedirectToAction("Detay", new { id });
        }

        public async Task<IActionResult> ExcelExport()
        {
            ExcelPackage.License.SetNonCommercialOrganization("MeteorGaleri");
            var siparisler = await _siparisService.GetAllAsync();
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("Siparişler");

            var headers = new[]
            {
                "Sipariş No",
                "Müşteri",
                "E-posta",
                "Telefon",
                "Şehir",
                "İlçe",
                "Tarih",
                "Tutar",
                "Durum",
                "Kargo Firması",
                "Kargo Takip No"
            };

            for (var i = 0; i < headers.Length; i++)
            {
                worksheet.Cells[1, i + 1].Value = headers[i];
            }

            var row = 2;
            foreach (var item in siparisler)
            {
                worksheet.Cells[row, 1].Value = string.IsNullOrWhiteSpace(item.SiparisNo) ? $"#{item.Id}" : item.SiparisNo;
                worksheet.Cells[row, 2].Value = item.MusteriAdSoyad;
                worksheet.Cells[row, 3].Value = item.Eposta;
                worksheet.Cells[row, 4].Value = item.Telefon;
                worksheet.Cells[row, 5].Value = item.Sehir;
                worksheet.Cells[row, 6].Value = item.Ilce;
                worksheet.Cells[row, 7].Value = item.OlusturulmaTarihi.ToLocalTime();
                worksheet.Cells[row, 8].Value = item.ToplamTutar;
                worksheet.Cells[row, 9].Value = SiparisDurumHelper.GetShortLabel(item.Durum);
                worksheet.Cells[row, 10].Value = item.KargoFirmasi;
                worksheet.Cells[row, 11].Value = item.KargoTakipNo;
                row++;
            }

            using (var range = worksheet.Cells[1, 1, 1, headers.Length])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(49, 53, 17));
                range.Style.Font.Color.SetColor(System.Drawing.Color.White);
            }

            worksheet.Column(7).Style.Numberformat.Format = "dd.mm.yyyy hh:mm";
            worksheet.Column(8).Style.Numberformat.Format = "#,##0.00 TL";
            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();

            return File(
                package.GetAsByteArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"siparisler-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TopluEtiketYazdir(List<int> siparisIds)
        {
            siparisIds = siparisIds
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (!siparisIds.Any())
            {
                TempData["Mesaj"] = "Etiket yazdırmak için en az bir sipariş seçmelisiniz.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index));
            }

            var validDurumlar = new[] { 
                SiparisDurumHelper.SiparisAlindi, 
                SiparisDurumHelper.UretimHazirlaniyor, 
                SiparisDurumHelper.Paketleniyor 
            };

            var siralama = siparisIds
                .Select((id, index) => new { id, index })
                .ToDictionary(x => x.id, x => x.index);

            var siparisler = await _context.Siparisler
                .AsNoTracking()
                .Include(x => x.SiparisDetaylari)
                    .ThenInclude(x => x.Urun)
                .Include(x => x.SiparisDetaylari)
                    .ThenInclude(x => x.UrunSecenek)
                .Where(x => siparisIds.Contains(x.Id) && validDurumlar.Contains(x.Durum))
                .ToListAsync();

            if (!siparisler.Any())
            {
                TempData["Mesaj"] = "Seçilen siparişler arasında etiket yazdırılabilecek (Yeni, Hazırlanıyor veya Paketleniyor durumunda) sipariş bulunamadı.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index));
            }

            siparisler = siparisler
                .OrderBy(x => siralama.TryGetValue(x.Id, out var index) ? index : int.MaxValue)
                .ToList();

            var kargoFirmalari = await _context.KargoFirmalari
                .AsNoTracking()
                .Where(x => !x.SilindiMi && x.AktifMi)
                .OrderByDescending(x => x.VarsayilanMi)
                .ThenBy(x => x.Ad)
                .ToListAsync();

            ViewBag.KargoFirmalari = kargoFirmalari;
            ViewBag.SiteSettings = _siteSettingsService.GetSettings();

            return View("TopluEtiket", siparisler);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TopluOnayla(List<int> siparisIds)
        {
            siparisIds = siparisIds?.Where(x => x > 0).Distinct().ToList() ?? new List<int>();

            if (!siparisIds.Any())
            {
                TempData["Mesaj"] = "Onaylamak için en az bir sipariş seçmelisiniz.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index));
            }

            var siparisler = await _context.Siparisler
                .Where(x => siparisIds.Contains(x.Id) &&
                            x.Durum == SiparisDurumHelper.SiparisAlindi &&
                            (x.OdemeSaglayici != "PayTR" || x.OdemeTamamlandiMi))
                .ToListAsync();

            if (!siparisler.Any())
            {
                TempData["Mesaj"] = "Onaylanacak 'Yeni' durumlu sipariş bulunamadı.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index));
            }

            foreach (var siparis in siparisler)
            {
                siparis.Durum = SiparisDurumHelper.UretimHazirlaniyor;
            }

            await _context.SaveChangesAsync();

            TempData["Mesaj"] = $"{siparisler.Count} sipariş 'Hazırlanıyor' durumuna alındı.";
            TempData["Durum"] = "success";
            
            return RedirectToAction(nameof(Index), new { toast = Uri.EscapeDataString($"{siparisler.Count} sipariş onaylandı"), toastType = "success" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TopluKargoyaVer(List<int> siparisIds, int? kargoFirmasiId, string? kargoNo)
        {
            siparisIds = siparisIds?.Where(x => x > 0).Distinct().ToList() ?? new List<int>();

            if (!siparisIds.Any())
            {
                TempData["Mesaj"] = "Kargoya vermek için en az bir sipariş seçmelisiniz.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index));
            }

            var validDurumlar = new[] { SiparisDurumHelper.UretimHazirlaniyor, SiparisDurumHelper.Paketleniyor };
            var siparisler = await _context.Siparisler
                .Where(x => siparisIds.Contains(x.Id) && validDurumlar.Contains(x.Durum))
                .ToListAsync();

            if (!siparisler.Any())
            {
                TempData["Mesaj"] = "Kargoya verilecek 'Hazırlanıyor' veya 'Paketleniyor' durumlu sipariş bulunamadı.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index));
            }

            foreach (var siparis in siparisler)
            {
                siparis.Durum = SiparisDurumHelper.KargoyaVerildi;
                if (kargoFirmasiId.HasValue)
                    siparis.KargoFirmasiId = kargoFirmasiId.Value;
                if (!string.IsNullOrWhiteSpace(kargoNo))
                    siparis.KargoTakipNo = kargoNo;
            }

            await _context.SaveChangesAsync();

            TempData["Mesaj"] = $"{siparisler.Count} sipariş 'Kargoya Verildi' durumuna alındı.";
            TempData["Durum"] = "success";
            
            return RedirectToAction(nameof(Index), new { toast = Uri.EscapeDataString($"{siparisler.Count} sipariş kargoya verildi"), toastType = "success" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TopluTeslimEt(List<int> siparisIds)
        {
            siparisIds = siparisIds?.Where(x => x > 0).Distinct().ToList() ?? new List<int>();

            if (!siparisIds.Any())
            {
                TempData["Mesaj"] = "Teslim etmek icin en az bir siparis secmelisiniz.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index), new { durum = SiparisDurumHelper.KargoyaVerildi });
            }

            var siparisler = await _context.Siparisler
                .Where(x => siparisIds.Contains(x.Id) && x.Durum == SiparisDurumHelper.KargoyaVerildi)
                .ToListAsync();

            if (!siparisler.Any())
            {
                TempData["Mesaj"] = "Teslim edilecek 'Kargoda' durumlu siparis bulunamadi.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index), new { durum = SiparisDurumHelper.KargoyaVerildi });
            }

            foreach (var siparis in siparisler)
            {
                siparis.Durum = SiparisDurumHelper.TeslimEdildi;
            }

            await _context.SaveChangesAsync();

            TempData["Mesaj"] = $"{siparisler.Count} siparis 'Teslim Edildi' durumuna alindi.";
            TempData["Durum"] = "success";

            return RedirectToAction(nameof(Index), new
            {
                durum = SiparisDurumHelper.KargoyaVerildi,
                toast = Uri.EscapeDataString($"{siparisler.Count} siparis teslim edildi"),
                toastType = "success"
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TopluDurumGuncelle(List<int> siparisIds, int yeniDurum)
        {
            siparisIds = siparisIds?.Where(x => x > 0).Distinct().ToList() ?? new List<int>();

            if (!siparisIds.Any())
            {
                TempData["Mesaj"] = "Güncellemek için en az bir sipariş seçmelisiniz.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index));
            }

            var siparisler = await _context.Siparisler
                .Where(x => siparisIds.Contains(x.Id))
                .ToListAsync();

            if (!siparisler.Any())
            {
                TempData["Mesaj"] = "Güncellenecek sipariş bulunamadı.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index));
            }

            var durumAd = SiparisDurumHelper.GetLabel(yeniDurum);
            var guncellenecekler = siparisler
                .Where(x => CanMoveOrderStatusForward(x.Durum, yeniDurum))
                .ToList();

            if (!guncellenecekler.Any())
            {
                TempData["Mesaj"] = $"Seçili siparişler '{durumAd}' durumuna alınamaz. Sipariş durumu geriye döndürülemez.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index));
            }

            foreach (var siparis in guncellenecekler)
            {
                siparis.Durum = yeniDurum;
            }

            await _context.SaveChangesAsync();

            TempData["Mesaj"] = $"{guncellenecekler.Count} sipariş '{durumAd}' durumuna güncellendi.";
            TempData["Durum"] = "success";
            
            return RedirectToAction(nameof(Index), new { toast = Uri.EscapeDataString($"{guncellenecekler.Count} sipariş durumu güncellendi"), toastType = "success" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TopluIptal(string siparisIds)
        {
            if (string.IsNullOrWhiteSpace(siparisIds))
            {
                TempData["Mesaj"] = "İptal etmek için en az bir sipariş seçmelisiniz.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index));
            }

            var idList = siparisIds.Split(',').Select(x => int.TryParse(x.Trim(), out var id) ? id : 0).Where(x => x > 0).Distinct().ToList();

            if (!idList.Any())
            {
                TempData["Mesaj"] = "İptal etmek için en az bir sipariş seçmelisiniz.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index));
            }

            var siparisler = await _context.Siparisler
                .Where(x =>
                    idList.Contains(x.Id) &&
                    x.Durum != SiparisDurumHelper.KargoyaVerildi &&
                    x.Durum != SiparisDurumHelper.TeslimEdildi &&
                    x.Durum != SiparisDurumHelper.IptalEdildi)
                .ToListAsync();

            if (!siparisler.Any())
            {
                TempData["Mesaj"] = "İptal edilebilecek (Kargoya Verilmemiş) sipariş bulunamadı.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index));
            }

            foreach (var siparis in siparisler)
            {
                siparis.Durum = SiparisDurumHelper.IptalEdildi;
                
                if (!string.IsNullOrWhiteSpace(siparis.Eposta))
                {
                    await SendIptalEmailAsync(siparis);
                }
            }

            await _context.SaveChangesAsync();

            TempData["Mesaj"] = $"{siparisler.Count} sipariş iptal edildi ve müşterilere e-posta gönderildi.";
            TempData["Durum"] = "success";
            
            return RedirectToAction(nameof(Index), new { toast = Uri.EscapeDataString($"{siparisler.Count} sipariş iptal edildi"), toastType = "success" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SiparisIptal(int id)
        {
            var siparis = await _context.Siparisler.FindAsync(id);
            
            if (siparis == null)
            {
                TempData["Mesaj"] = "Sipariş bulunamadı.";
                TempData["Durum"] = "warning";
                return RedirectToAction(nameof(Index));
            }

            if (siparis.Durum == SiparisDurumHelper.KargoyaVerildi)
            {
                TempData["Mesaj"] = "Kargoya verilmiş sipariş iptal edilemez!";
                TempData["Durum"] = "danger";
                return RedirectToAction(nameof(Detay), new { id });
            }

            siparis.Durum = SiparisDurumHelper.IptalEdildi;
            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(siparis.Eposta))
            {
                await SendIptalEmailAsync(siparis);
            }

            TempData["Mesaj"] = "Sipariş iptal edildi ve müşteriye e-posta gönderildi.";
            TempData["Durum"] = "success";
            
            return RedirectToAction(nameof(Detay), new { id });
        }

        private async Task SendIptalEmailAsync(Siparis siparis)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(siparis.Eposta) || !IsValidEmail(siparis.Eposta))
                    return;

                var siparisNo = string.IsNullOrWhiteSpace(siparis.SiparisNo) ? $"#{siparis.Id}" : siparis.SiparisNo;
                
                var icerik = $@"
                    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px;'>
                        <h2 style='color: #dc3545;'>Siparişiniz İptal Edildi</h2>
                        <p>Merhaba <strong>{siparis.MusteriAdSoyad}</strong>,</p>
                        <p>Siparişiniz iptal edilmiştir:</p>
                        <div style='background: #f8f9fa; padding: 15px; border-radius: 8px; margin: 20px 0;'>
                            <p><strong>Sipariş No:</strong> {siparisNo}</p>
                            <p><strong>İptal Tarihi:</strong> {DateTime.Now:dd.MM.yyyy HH:mm}</p>
                        </div>
                        <p>Ödeme yapıldıysa, iadeniz 3-5 iş günü içinde hesabınıza yapılacaktır.</p>
                        <p>Herhangi bir sorunuz varsa bizimle iletişime geçebilirsiniz.</p>
                        <p style='margin-top: 30px;'>Saygılarımızla,<br/><strong>MeteorGaleri</strong></p>
                    </div>";

                await _emailService.SendMailAsync(siparis.Eposta, $"Sipariş {siparisNo} - İptal", icerik);
            }
            catch
            {
            }
        }

        private async Task<(bool Success, string Message)> SendStatusNotificationAsync(
            Siparis siparis,
            int eskiDurum,
            int yeniDurum,
            string kargoFirmasi,
            string kargoTakipNo)
        {
            if (string.IsNullOrWhiteSpace(siparis.Eposta))
            {
                return (false, "müşteri e-posta adresi boş.");
            }

            if (!IsValidEmail(siparis.Eposta))
            {
                return (false, "müşteri e-posta adresi geçerli formatta değil.");
            }

            try
            {
                if (SiparisDurumHelper.IsShipped(yeniDurum) && !string.IsNullOrWhiteSpace(kargoTakipNo))
                {
                    var kargoMailGitti = await _emailService.SendKargoNotificationEmail(
                        siparis.Eposta,
                        siparis.MusteriAdSoyad,
                        siparis.SiparisNo,
                        kargoFirmasi,
                        kargoTakipNo);

                    return kargoMailGitti
                        ? (true, string.Empty)
                        : (false, "Kargo e-postası SMTP tarafından gönderilemedi.");
                }

                var yeniDurumLabel = SiparisDurumHelper.GetLabel(yeniDurum);
                var oncekiDurumLabel = SiparisDurumHelper.GetLabel(eskiDurum);
                var takipLink = Url.Action("SiparisDetay", "Profil", new { id = siparis.Id }, Request.Scheme) ?? string.Empty;
                var urunSatirlari = await BuildOrderItemsTableRowsAsync(siparis.Id);

                await _emailService.SendTemplateMailAsync(
                    siparis.Eposta,
                    $"Sipari\u015F durumunuz g\u00FCncellendi: {yeniDurumLabel}",
                    siparis.MusteriAdSoyad,
                    BuildStatusEmailContent(siparis, oncekiDurumLabel, yeniDurumLabel, yeniDurum, urunSatirlari),
                    takipLink,
                    "Sipari\u015Fimi G\u00F6r\u00FCnt\u00FCle");

                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private async Task<KargoFirmasi?> ResolveKargoFirmasiAsync(Siparis siparis, int? selectedId = null)
        {
            if (selectedId.HasValue)
            {
                var selected = await _context.KargoFirmalari
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == selectedId.Value && !x.SilindiMi && x.AktifMi);

                if (selected != null)
                {
                    return selected;
                }
            }

            if (siparis.KargoFirmasiId.HasValue)
            {
                var existing = await _context.KargoFirmalari
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == siparis.KargoFirmasiId.Value && !x.SilindiMi);

                if (existing != null)
                {
                    return existing;
                }
            }

            return await _context.KargoFirmalari
                .AsNoTracking()
                .Where(x => !x.SilindiMi && x.AktifMi)
                .OrderByDescending(x => x.VarsayilanMi)
                .ThenBy(x => x.Ad)
                .FirstOrDefaultAsync();
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

        private static string BuildStatusEmailContent(Siparis siparis, string oncekiDurum, string yeniDurum, int durum, string urunSatirlari)
        {
            var siparisNo = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(siparis.SiparisNo) ? $"#{siparis.Id}" : siparis.SiparisNo);
            var oncekiDurumText = System.Net.WebUtility.HtmlEncode(oncekiDurum);
            var yeniDurumText = System.Net.WebUtility.HtmlEncode(yeniDurum);
            var durumMesaji = durum switch
            {
                SiparisDurumHelper.UretimHazirlaniyor => "Siparişiniz üretim planına alındı. Ürünleriniz özenle hazırlanıyor.",
                SiparisDurumHelper.Paketleniyor => "Ürünleriniz hazırlandı ve korunaklı paketleme aşamasına geçti.",
                SiparisDurumHelper.TeslimEdildi => "Siparişiniz teslim edildi olarak güncellendi. Güle güle kullanmanızı dileriz.",
                SiparisDurumHelper.IptalEdildi => "Siparişiniz iptal edildi olarak güncellendi. Detaylar için bizimle iletişime geçebilirsiniz.",
                SiparisDurumHelper.IadeTalebi => "İade talebiniz alındı ve ekibimiz tarafından inceleniyor.",
                SiparisDurumHelper.IadeOnaylandi => "İade talebiniz onaylandı. Sonraki adımlar için sizinle iletişime geçeceğiz.",
                SiparisDurumHelper.IadeTamamlandi => "İade süreciniz tamamlandı.",
                _ => "Siparişinizin durumu güncellendi."
            };

            return $@"
                <p>Sipariş numaranız <strong>{siparisNo}</strong> için durum güncellemesi yapıldı.</p>
                <p><strong>Önceki durum:</strong> {oncekiDurumText}<br>
                <strong>Yeni durum:</strong> {yeniDurumText}</p>
                <p>{durumMesaji}</p>";
        }

        private async Task<string> BuildOrderItemsTableRowsAsync(int siparisId)
        {
            var detaylar = await _context.SiparisDetaylari
                .AsNoTracking()
                .Include(x => x.Urun)
                .Include(x => x.UrunSecenek)
                .Where(x => x.SiparisId == siparisId && !x.SilindiMi)
                .ToListAsync();

            var rows = new StringBuilder();
            foreach (var item in detaylar)
            {
                var urunAdi = System.Net.WebUtility.HtmlEncode(item.Urun?.Baslik ?? "Ürün");
                var detayMetni = System.Net.WebUtility.HtmlEncode(BuildOrderLineDetail(item));
                var detayHtml = string.IsNullOrWhiteSpace(detayMetni)
                    ? string.Empty
                    : $"<div style='margin-top:4px; font-size:12px; color:#6f6a5e;'>{detayMetni}</div>";
                var notSatiri = !string.IsNullOrWhiteSpace(item.MusteriNotu)
                    ? $"<div style='margin-top:4px; font-size:12px; color:#b58735; font-style:italic;'>Not: {System.Net.WebUtility.HtmlEncode(item.MusteriNotu)}</div>"
                    : string.Empty;

                rows.Append($@"
                    <tr>
                        <td style='padding:10px; border-top:1px solid #e5e2dc; color:#47473d;'>
                            <div>{urunAdi}</div>
                            {detayHtml}
                            {notSatiri}
                        </td>
                        <td style='padding:10px; border-top:1px solid #e5e2dc; text-align:center; color:#47473d;'>{item.Adet}</td>
                        <td style='padding:10px; border-top:1px solid #e5e2dc; text-align:right; color:#313511; font-weight:600;'>{(item.BirimFiyat * item.Adet):N2} TL</td>
                    </tr>");
            }

            return rows.ToString();
        }

        private static string BuildOrderLineDetail(SiparisDetay item)
        {
            var details = new List<string>();
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

            if (!string.IsNullOrWhiteSpace(item.CerceveModeli) && item.CerceveModeli != "Çerçevesiz")
            {
                details.Add($"Çerçeve: {item.CerceveModeli}");
            }

            return string.Join(" | ", details);
        }

        private static bool CanMoveOrderStatusForward(int currentStatus, int nextStatus)
        {
            if (currentStatus == nextStatus)
            {
                return true;
            }

            if (currentStatus == SiparisDurumHelper.IptalEdildi ||
                currentStatus == SiparisDurumHelper.TeslimEdildi ||
                SiparisDurumHelper.IsReturn(currentStatus))
            {
                return false;
            }

            if (nextStatus == SiparisDurumHelper.IptalEdildi)
            {
                return currentStatus != SiparisDurumHelper.KargoyaVerildi &&
                       currentStatus != SiparisDurumHelper.TeslimEdildi;
            }

            if (!TryGetOperationalStatusRank(currentStatus, out var currentRank) ||
                !TryGetOperationalStatusRank(nextStatus, out var nextRank))
            {
                return false;
            }

            return nextRank > currentRank;
        }

        private static bool TryGetOperationalStatusRank(int status, out int rank)
        {
            rank = status switch
            {
                SiparisDurumHelper.SiparisAlindi => 1,
                SiparisDurumHelper.UretimHazirlaniyor => 2,
                SiparisDurumHelper.Paketleniyor => 3,
                SiparisDurumHelper.KargoyaVerildi => 4,
                SiparisDurumHelper.TeslimEdildi => 5,
                _ => 0
            };

            return rank > 0;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FaturaYukle(IFormFile faturaDosyasi, int siparisId)
        {
            if (faturaDosyasi == null || faturaDosyasi.Length == 0)
            {
                TempData["Hata"] = "Lütfen bir dosya seçin.";
                return RedirectToAction("Detay", new { id = siparisId });
            }

            // Sadece PDF kontrolü - hem içerik tipi hem uzantı
            var allowedContentTypes = new[] { "application/pdf" };
            var allowedExtensions = new[] { ".pdf" };
            var fileExtension = Path.GetExtension(faturaDosyasi.FileName).ToLowerInvariant();
            var contentType = faturaDosyasi.ContentType?.ToLowerInvariant();

            if (!allowedContentTypes.Contains(contentType) || !allowedExtensions.Contains(fileExtension))
            {
                TempData["Hata"] = "Sadece PDF dosyaları yüklenebilir.";
                return RedirectToAction("Detay", new { id = siparisId });
            }

            // Dosya boyutu kontrolü (5MB max)
            const long maxFileSize = 5 * 1024 * 1024;
            if (faturaDosyasi.Length > maxFileSize)
            {
                TempData["Hata"] = "Dosya boyutu maksimum 5 MB olabilir.";
                return RedirectToAction("Detay", new { id = siparisId });
            }

            var siparis = await _context.Siparisler.FindAsync(siparisId);
            if (siparis == null)
            {
                TempData["Hata"] = "Sipariş bulunamadı.";
                return RedirectToAction("Index");
            }

            // Upload klasörü oluştur
            var uploadsPath = Path.Combine(_env.WebRootPath, "uploads", "invoices");

            if (!Directory.Exists(uploadsPath))
            {
                Directory.CreateDirectory(uploadsPath);
            }

            // Güvenli dosya adı: siparisId_benzersizGuid.pdf
            var safeFileName = $"{siparisId}_{Guid.NewGuid():N}.pdf";
            var filePath = Path.Combine(uploadsPath, safeFileName);
            var relativePath = $"/uploads/invoices/{safeFileName}";

            // Eski fatura varsa sil
            if (!string.IsNullOrWhiteSpace(siparis.FaturaDosyaYolu))
            {
                var oldFilePath = Path.Combine(_env.WebRootPath, siparis.FaturaDosyaYolu.TrimStart('/'));
                if (System.IO.File.Exists(oldFilePath))
                {
                    try { System.IO.File.Delete(oldFilePath); }
                    catch { /* Silinemezse ignore */ }
                }
            }

            // Dosyayı kaydet
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await faturaDosyasi.CopyToAsync(stream);
            }

            // Sipariş fatura bilgilerini güncelle
            siparis.FaturaDosyaYolu = relativePath;
            siparis.FaturaDosyaAdi = faturaDosyasi.FileName;
            siparis.FaturaYuklendiMi = true;
            siparis.FaturaYuklenmeTarihi = DateTime.UtcNow;

            // E-posta ile fatura gönder
            var mailGonderildi = false;
            if (!string.IsNullOrWhiteSpace(siparis.Eposta))
            {
                mailGonderildi = await _emailService.SendInvoiceEmailAsync(
                    siparis.Eposta,
                    siparis.MusteriAdSoyad,
                    siparis.SiparisNo ?? siparis.Id.ToString(),
                    filePath);

                if (mailGonderildi)
                {
                    siparis.FaturaMailGonderildiMi = true;
                    siparis.FaturaMailGonderimTarihi = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync();

            TempData["Basarili"] = mailGonderildi
                ? "Fatura başarıyla yüklendi ve müşteriye e-posta gönderildi."
                : "Fatura yüklendi ancak e-posta gönderilemedi.";

            return RedirectToAction("Detay", new { id = siparisId });
        }

        [HttpGet]
        public async Task<IActionResult> FaturaIndir(int id)
        {
            var siparis = await _context.Siparisler.FindAsync(id);
            if (siparis == null || string.IsNullOrWhiteSpace(siparis.FaturaDosyaYolu))
            {
                return NotFound("Fatura bulunamadı.");
            }

            var filePath = Path.Combine(_env.WebRootPath, siparis.FaturaDosyaYolu.TrimStart('/'));

            if (!System.IO.File.Exists(filePath))
            {
                return NotFound("Fatura dosyası bulunamadı.");
            }

            var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
            return File(fileBytes, "application/pdf", siparis.FaturaDosyaAdi ?? $"fatura_{id}.pdf");
        }
    }
}
