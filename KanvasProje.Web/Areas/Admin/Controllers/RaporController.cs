using KanvasProje.Core.Helpers;
using KanvasProje.Core.Models;
using KanvasProje.Core.Varliklar;
using KanvasProje.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KanvasProje.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class RaporController : AdminBaseController
    {
        private readonly KanvasDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public RaporController(KanvasDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        public async Task<IActionResult> Index(DateTime? baslangic, DateTime? bitis)
        {
            var (startUtc, endUtc, startLocal, endLocal) = ResolveDateRange(baslangic, bitis);
            var model = await BuildReportAsync(startUtc, endUtc, startLocal, endLocal);
            return View(model);
        }

        public async Task<IActionResult> ExcelExport(DateTime? baslangic, DateTime? bitis)
        {
            var (startUtc, endUtc, startLocal, endLocal) = ResolveDateRange(baslangic, bitis);
            var model = await BuildReportAsync(startUtc, endUtc, startLocal, endLocal);

            ExcelPackage.License.SetNonCommercialOrganization("MeteorGaleri");
            using var package = new ExcelPackage();

            AddSummarySheet(package, model);
            AddDailySheet(package, model);
            AddHourlySheet(package, model);
            AddStatusSheet(package, model);
            AddProductSheet(package, "En Çok Satan Ürünler", model.EnCokSatanUrunler);
            AddProductSheet(package, "En Çok Tıklanan Ürünler", model.EnCokTiklananUrunler);
            AddConversionSheet(package, model);
            AddCustomerSheet(package, model);
            AddReturnReasonSheet(package, model);
            AddCategorySheet(package, model);
            AddCitySheet(package, model);
            AddCouponSheet(package, model);
            AddTrafficSheet(package, "Trafik Kaynakları", model.TrafikKaynaklari);
            AddTrafficSheet(package, "Gezilen Sayfalar", model.EnCokGezilenSayfalar);
            AddTrafficSheet(package, "Cihaz Dağılımı", model.CihazDagilimi);
            AddCargoSheet(package, model);

            var bytes = package.GetAsByteArray();
            return File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"raporlar-{startLocal:yyyyMMdd}-{endLocal:yyyyMMdd}.xlsx");
        }

        public async Task<IActionResult> PdfExport(DateTime? baslangic, DateTime? bitis)
        {
            var (startUtc, endUtc, startLocal, endLocal) = ResolveDateRange(baslangic, bitis);
            var model = await BuildReportAsync(startUtc, endUtc, startLocal, endLocal);
            var logoPath = Path.Combine(_webHostEnvironment.WebRootPath, "EmailTemplates", "meteorgaleri-logo.png");

            QuestPDF.Settings.License = LicenseType.Community;
            var pdfBytes = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(28);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                    page.Header().Column(column =>
                    {
                        column.Item().Row(row =>
                        {
                            row.RelativeItem().Column(header =>
                            {
                                header.Item().Text("MeteorGaleri Rapor Özeti").FontSize(18).Bold().FontColor("#1B2A4A");
                                header.Item().Text(model.AralikEtiketi).FontSize(9).FontColor("#6b6f45");
                            });

                            row.ConstantItem(120).AlignRight().Element(box =>
                            {
                                if (System.IO.File.Exists(logoPath))
                                {
                                    box.Height(44).Image(logoPath).FitArea();
                                }
                                else
                                {
                                    box.Text("MeteorGaleri").FontSize(14).Bold().FontColor("#1B2A4A");
                                }
                            });
                        });

                        column.Item().PaddingTop(12).LineHorizontal(1).LineColor("#e5e2dc");
                    });

                    page.Content().PaddingVertical(16).Column(column =>
                    {
                        column.Spacing(14);

                        column.Item().Row(row =>
                        {
                            row.RelativeItem().Element(box => PdfKpi(box, "Ciro", Money(model.Ciro), "Satış getiren siparişlerden hesaplandı."));
                            row.RelativeItem().Element(box => PdfKpi(box, "Sipariş", model.SiparisSayisi.ToString(), $"Ortalama sepet: {Money(model.OrtalamaSepet)}"));
                        });
                        column.Item().Row(row =>
                        {
                            row.RelativeItem().Element(box => PdfKpi(box, "Dönüşüm", $"%{model.DonusumOrani:N2}", $"{model.TekilZiyaretciSayisi} tekil ziyaretçi"));
                            row.RelativeItem().Element(box => PdfKpi(box, "Terk Sepet", Money(model.TerkEdilenSepetTutari), $"{model.TerkEdilenSepetSayisi} sepet"));
                        });
                        column.Item().Row(row =>
                        {
                            row.RelativeItem().Element(box => PdfKpi(box, "Yeni Müşteri", model.YeniMusteriSayisi.ToString(), $"{model.TekrarMusteriSayisi} tekrar müşteri"));
                            row.RelativeItem().Element(box => PdfKpi(box, "İade / İptal", $"{model.IadeTalebiSayisi} / {model.IptalSiparisSayisi}", "Operasyon takibi için"));
                        });

                        PdfSection(column, "Aksiyon Önerileri", table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(3);
                            });
                            PdfHeader(table, "Başlık");
                            PdfHeader(table, "Açıklama");
                            foreach (var item in model.Oneriler.Take(5))
                            {
                                PdfCell(table, item.Baslik);
                                PdfCell(table, item.Aciklama);
                            }
                        });

                        PdfSection(column, "Çok Tıklanan Ama Satışta Zayıf Ürünler", table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });
                            PdfHeader(table, "Ürün");
                            PdfHeader(table, "Tıklama");
                            PdfHeader(table, "Satış");
                            PdfHeader(table, "Risk");
                            foreach (var item in model.UrunDonusumSorunlari.Take(8))
                            {
                                PdfCell(table, item.UrunAdi);
                                PdfCell(table, item.Goruntulenme.ToString());
                                PdfCell(table, item.SatisAdedi.ToString());
                                PdfCell(table, item.RiskNotu);
                            }
                        });

                        PdfSection(column, "En Değerli Müşteriler", table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });
                            PdfHeader(table, "Müşteri");
                            PdfHeader(table, "E-posta");
                            PdfHeader(table, "Sipariş");
                            PdfHeader(table, "Ciro");
                            foreach (var item in model.EnDegerliMusteriler.Take(8))
                            {
                                PdfCell(table, item.Musteri);
                                PdfCell(table, item.Eposta);
                                PdfCell(table, item.SiparisAdedi.ToString());
                                PdfCell(table, Money(item.Ciro));
                            }
                        });

                        PdfSection(column, "İade / İptal Nedenleri", table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2);
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });
                            PdfHeader(table, "Neden");
                            PdfHeader(table, "Tip");
                            PdfHeader(table, "Adet");
                            PdfHeader(table, "Tutar");
                            foreach (var item in model.IadeIptalNedenleri.Take(8))
                            {
                                PdfCell(table, item.Neden);
                                PdfCell(table, item.Tip);
                                PdfCell(table, item.Adet.ToString());
                                PdfCell(table, Money(item.Tutar));
                            }
                        });
                    });

                    page.Footer()
                        .AlignRight()
                        .Text(text =>
                        {
                            text.Span("Sayfa ");
                            text.CurrentPageNumber();
                            text.Span(" / ");
                            text.TotalPages();
                        });
                });
            }).GeneratePdf();

            return File(pdfBytes, "application/pdf", $"rapor-ozeti-{startLocal:yyyyMMdd}-{endLocal:yyyyMMdd}.pdf");
        }

        private async Task<RaporIndexViewModel> BuildReportAsync(DateTime startUtc, DateTime endUtc, DateTime startLocal, DateTime endLocal)
        {
            var periodDays = Math.Max(1, (endUtc - startUtc).Days);
            var previousStartUtc = startUtc.AddDays(-periodDays);
            var previousEndUtc = startUtc;

            var orders = await _context.Siparisler
                .AsNoTracking()
                .Include(x => x.SiparisDetaylari)
                    .ThenInclude(x => x.Urun)
                        .ThenInclude(x => x.Kategori)
                .Where(x => x.OlusturulmaTarihi >= startUtc && x.OlusturulmaTarihi < endUtc)
                .ToListAsync();

            var previousOrders = await _context.Siparisler
                .AsNoTracking()
                .Where(x => x.OlusturulmaTarihi >= previousStartUtc && x.OlusturulmaTarihi < previousEndUtc)
                .ToListAsync();

            var allCustomerOrders = await _context.Siparisler
                .AsNoTracking()
                .Where(x => x.Durum != SiparisDurumHelper.IptalEdildi &&
                            x.Durum != SiparisDurumHelper.IadeTalebi &&
                            x.Durum != SiparisDurumHelper.IadeOnaylandi &&
                            x.Durum != SiparisDurumHelper.IadeTamamlandi)
                .Select(x => new
                {
                    x.Id,
                    x.AppUserId,
                    x.Eposta,
                    x.MusteriAdSoyad,
                    x.Sehir,
                    x.ToplamTutar,
                    x.OlusturulmaTarihi
                })
                .ToListAsync();

            var visitorLogs = await _context.ZiyaretciLoglari
                .AsNoTracking()
                .Where(x => x.OlusturulmaTarihi >= startUtc && x.OlusturulmaTarihi < endUtc)
                .ToListAsync();

            var abandonedCarts = await _context.Sepetler
                .AsNoTracking()
                .Include(x => x.SepetItems)
                .Where(x => x.TerkEdildi && x.TerkEdilmeTarihi >= startUtc && x.TerkEdilmeTarihi < endUtc)
                .ToListAsync();

            var returns = await _context.IadeTalepleri
                .AsNoTracking()
                .Include(x => x.Siparis)
                .Where(x => x.OlusturulmaTarihi >= startUtc && x.OlusturulmaTarihi < endUtc)
                .ToListAsync();

            var allProducts = await _context.Urunler
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(x => !x.SilindiMi)
                .Select(x => new
                {
                    x.Id,
                    x.Baslik,
                    x.AnaGorselUrl,
                    x.GoruntulenmeSayisi,
                    x.FavoriSayisi
                })
                .ToListAsync();

            var revenueOrders = orders.Where(IsRevenueOrder).ToList();
            var previousRevenueOrders = previousOrders.Where(IsRevenueOrder).ToList();
            var allDetails = revenueOrders.SelectMany(x => x.SiparisDetaylari).ToList();
            var salesByProduct = allDetails
                .GroupBy(x => x.UrunId)
                .ToDictionary(
                    x => x.Key,
                    x => new
                    {
                        Adet = x.Sum(i => i.Adet),
                        Ciro = x.Sum(i => i.BirimFiyat * i.Adet)
                    });

            var periodCustomerKeys = revenueOrders.Select(GetCustomerKey).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var firstOrderByCustomer = allCustomerOrders
                .GroupBy(x => !string.IsNullOrWhiteSpace(x.AppUserId)
                    ? $"u:{x.AppUserId.Trim()}"
                    : string.IsNullOrWhiteSpace(x.Eposta)
                        ? string.Empty
                        : $"e:{x.Eposta.Trim().ToLowerInvariant()}")
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .ToDictionary(x => x.Key, x => x.Min(o => o.OlusturulmaTarihi), StringComparer.OrdinalIgnoreCase);
            var periodOrderCountByCustomer = revenueOrders
                .GroupBy(GetCustomerKey)
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
            var newCustomerKeys = periodCustomerKeys
                .Where(x => firstOrderByCustomer.TryGetValue(x, out var firstOrderDate) && firstOrderDate >= startUtc && firstOrderDate < endUtc)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var repeatCustomerKeys = periodCustomerKeys
                .Where(x => (firstOrderByCustomer.TryGetValue(x, out var firstOrderDate) && firstOrderDate < startUtc) ||
                            (periodOrderCountByCustomer.TryGetValue(x, out var count) && count > 1))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var topSelling = allDetails
                .GroupBy(x => new
                {
                    x.UrunId,
                    UrunAdi = string.IsNullOrWhiteSpace(x.Urun?.Baslik) ? $"Ürün #{x.UrunId}" : x.Urun.Baslik,
                    Gorsel = x.Urun?.AnaGorselUrl ?? string.Empty
                })
                .Select(g => new RaporProductMetric
                {
                    UrunId = g.Key.UrunId,
                    UrunAdi = g.Key.UrunAdi,
                    GorselUrl = g.Key.Gorsel,
                    Adet = g.Sum(x => x.Adet),
                    Ciro = g.Sum(x => x.BirimFiyat * x.Adet),
                    Goruntulenme = allProducts.FirstOrDefault(p => p.Id == g.Key.UrunId)?.GoruntulenmeSayisi ?? 0,
                    Favori = allProducts.FirstOrDefault(p => p.Id == g.Key.UrunId)?.FavoriSayisi ?? 0
                })
                .OrderByDescending(x => x.Ciro)
                .ThenByDescending(x => x.Adet)
                .Take(10)
                .ToList();

            var topClicked = allProducts
                .OrderByDescending(x => x.GoruntulenmeSayisi)
                .ThenByDescending(x => x.FavoriSayisi)
                .Take(10)
                .Select(x =>
                {
                    salesByProduct.TryGetValue(x.Id, out var sales);
                    return new RaporProductMetric
                    {
                        UrunId = x.Id,
                        UrunAdi = x.Baslik,
                        GorselUrl = x.AnaGorselUrl,
                        Goruntulenme = x.GoruntulenmeSayisi,
                        Favori = x.FavoriSayisi,
                        Adet = sales?.Adet ?? 0,
                        Ciro = sales?.Ciro ?? 0
                    };
                })
                .ToList();

            var conversionRisk = allProducts
                .Where(x => x.GoruntulenmeSayisi > 0)
                .Select(x =>
                {
                    salesByProduct.TryGetValue(x.Id, out var sales);
                    var sold = sales?.Adet ?? 0;
                    var conversion = x.GoruntulenmeSayisi == 0 ? 0 : Math.Round(sold * 100m / x.GoruntulenmeSayisi, 2);
                    return new RaporProductConversionMetric
                    {
                        UrunId = x.Id,
                        UrunAdi = x.Baslik,
                        GorselUrl = x.AnaGorselUrl,
                        Goruntulenme = x.GoruntulenmeSayisi,
                        SatisAdedi = sold,
                        Ciro = sales?.Ciro ?? 0,
                        DonusumOrani = conversion,
                        RiskNotu = sold == 0 ? "Tıklama var, satış yok" : conversion < 1 ? "Dönüşüm düşük" : "Takip edilmeli"
                    };
                })
                .Where(x => x.SatisAdedi == 0 || x.DonusumOrani < 1)
                .OrderByDescending(x => x.Goruntulenme)
                .ThenBy(x => x.DonusumOrani)
                .Take(12)
                .ToList();

            var customerReport = revenueOrders
                .GroupBy(GetCustomerKey)
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .Select(g =>
                {
                    var lastOrder = g.OrderByDescending(x => x.OlusturulmaTarihi).First();
                    var key = g.Key;
                    return new RaporCustomerMetric
                    {
                        Musteri = string.IsNullOrWhiteSpace(lastOrder.MusteriAdSoyad) ? "İsimsiz müşteri" : lastOrder.MusteriAdSoyad,
                        Eposta = string.IsNullOrWhiteSpace(lastOrder.Eposta) ? "-" : lastOrder.Eposta,
                        Sehir = string.IsNullOrWhiteSpace(lastOrder.Sehir) ? "-" : lastOrder.Sehir,
                        SiparisAdedi = g.Count(),
                        Ciro = g.Sum(x => x.ToplamTutar),
                        SonSiparisTarihi = g.Max(x => x.OlusturulmaTarihi),
                        YeniMusteri = newCustomerKeys.Contains(key)
                    };
                })
                .OrderByDescending(x => x.Ciro)
                .Take(12)
                .ToList();

            var cancelledReasons = orders
                .Where(x => x.Durum == SiparisDurumHelper.IptalEdildi)
                .GroupBy(x => NormalizeReason(x.Aciklama, "Sebep girilmemiş"))
                .Select(g => new RaporReturnReasonMetric
                {
                    Neden = g.Key,
                    Tip = "İptal",
                    Adet = g.Count(),
                    Tutar = g.Sum(x => x.ToplamTutar)
                });
            var returnReasons = returns
                .GroupBy(x => NormalizeReason(x.Neden, "Sebep girilmemiş"))
                .Select(g => new RaporReturnReasonMetric
                {
                    Neden = g.Key,
                    Tip = "İade",
                    Adet = g.Count(),
                    Tutar = g.Sum(x => x.Siparis?.ToplamTutar ?? 0)
                });

            var model = new RaporIndexViewModel
            {
                Baslangic = startLocal,
                Bitis = endLocal.AddDays(-1),
                AralikEtiketi = $"{startLocal:dd.MM.yyyy} - {endLocal.AddDays(-1):dd.MM.yyyy}",
                Ciro = revenueOrders.Sum(x => x.ToplamTutar),
                OncekiCiro = previousRevenueOrders.Sum(x => x.ToplamTutar),
                IndirimToplami = revenueOrders.Sum(x => x.IndirimTutari),
                OrtalamaSepet = revenueOrders.Count == 0 ? 0 : revenueOrders.Average(x => x.ToplamTutar),
                SiparisSayisi = revenueOrders.Count,
                OncekiSiparisSayisi = previousRevenueOrders.Count,
                SatilanUrunAdedi = allDetails.Sum(x => x.Adet),
                TekilMusteriSayisi = periodCustomerKeys.Count,
                YeniMusteriSayisi = newCustomerKeys.Count,
                TekrarMusteriSayisi = repeatCustomerKeys.Count,
                TekrarMusteriCirosu = revenueOrders.Where(x => repeatCustomerKeys.Contains(GetCustomerKey(x))).Sum(x => x.ToplamTutar),
                ZiyaretSayisi = visitorLogs.Count,
                TekilZiyaretciSayisi = visitorLogs.Select(x => x.IpAdresi).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Count(),
                BekleyenSiparisSayisi = orders.Count(x => x.Durum == SiparisDurumHelper.SiparisAlindi),
                KargoyaHazirSiparisSayisi = orders.Count(x => x.Durum == SiparisDurumHelper.Paketleniyor),
                IadeTalebiSayisi = returns.Count,
                IptalSiparisSayisi = orders.Count(x => x.Durum == SiparisDurumHelper.IptalEdildi),
                TerkEdilenSepetSayisi = abandonedCarts.Count,
                TerkEdilenSepetTutari = abandonedCarts.Sum(x => x.SepetItems.Sum(i => i.Fiyat * i.Adet)),
                GunlukMetrikler = BuildDailyMetrics(startLocal, endLocal, revenueOrders, visitorLogs),
                SaatlikMetrikler = BuildHourlyMetrics(revenueOrders, visitorLogs),
                DurumDagilimi = orders
                    .GroupBy(x => x.Durum)
                    .Select(g => new RaporStatusMetric
                    {
                        Durum = g.Key,
                        Etiket = SiparisDurumHelper.GetShortLabel(g.Key),
                        Adet = g.Count(),
                        Tutar = g.Where(IsRevenueOrder).Sum(x => x.ToplamTutar)
                    })
                    .OrderByDescending(x => x.Adet)
                    .ToList(),
                EnCokSatanUrunler = topSelling,
                EnCokTiklananUrunler = topClicked,
                UrunDonusumSorunlari = conversionRisk,
                EnDegerliMusteriler = customerReport,
                IadeIptalNedenleri = returnReasons.Concat(cancelledReasons)
                    .OrderByDescending(x => x.Adet)
                    .ThenByDescending(x => x.Tutar)
                    .Take(12)
                    .ToList(),
                KategoriPerformansi = allDetails
                    .GroupBy(x => new
                    {
                        KategoriId = x.Urun?.KategoriId ?? 0,
                        KategoriAdi = x.Urun?.Kategori?.Ad ?? "Kategorisiz"
                    })
                    .Select(g => new RaporCategoryMetric
                    {
                        KategoriId = g.Key.KategoriId,
                        KategoriAdi = g.Key.KategoriAdi,
                        UrunAdedi = g.Select(x => x.UrunId).Distinct().Count(),
                        SiparisAdedi = g.Sum(x => x.Adet),
                        Ciro = g.Sum(x => x.BirimFiyat * x.Adet)
                    })
                    .OrderByDescending(x => x.Ciro)
                    .Take(10)
                    .ToList(),
                SehirPerformansi = revenueOrders
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.Sehir) ? "Belirtilmemiş" : x.Sehir.Trim())
                    .Select(g => new RaporCityMetric
                    {
                        Sehir = g.Key,
                        SiparisAdedi = g.Count(),
                        Ciro = g.Sum(x => x.ToplamTutar)
                    })
                    .OrderByDescending(x => x.Ciro)
                    .Take(10)
                    .ToList(),
                KuponPerformansi = revenueOrders
                    .Where(x => !string.IsNullOrWhiteSpace(x.KuponKodu))
                    .GroupBy(x => x.KuponKodu!.Trim().ToUpperInvariant())
                    .Select(g => new RaporCouponMetric
                    {
                        Kod = g.Key,
                        Kullanim = g.Count(),
                        Indirim = g.Sum(x => x.IndirimTutari),
                        Ciro = g.Sum(x => x.ToplamTutar)
                    })
                    .OrderByDescending(x => x.Kullanim)
                    .Take(10)
                    .ToList(),
                TrafikKaynaklari = visitorLogs
                    .GroupBy(x => NormalizeReferer(x.ReferansUrl))
                    .Select(g => new RaporTrafficMetric
                    {
                        Etiket = g.Key,
                        Adet = g.Count(),
                        Tekil = g.Select(x => x.IpAdresi).Distinct().Count()
                    })
                    .OrderByDescending(x => x.Adet)
                    .Take(10)
                    .ToList(),
                EnCokGezilenSayfalar = visitorLogs
                    .Where(x => string.Equals(x.Metod, "GET", StringComparison.OrdinalIgnoreCase))
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.Url) ? "/" : x.Url)
                    .Select(g => new RaporTrafficMetric
                    {
                        Etiket = g.Key,
                        Adet = g.Count(),
                        Tekil = g.Select(x => x.IpAdresi).Distinct().Count()
                    })
                    .OrderByDescending(x => x.Adet)
                    .Take(12)
                    .ToList(),
                CihazDagilimi = visitorLogs
                    .GroupBy(x => NormalizeDevice(x.CihazModeli, x.IsletimSistemi))
                    .Select(g => new RaporTrafficMetric
                    {
                        Etiket = g.Key,
                        Adet = g.Count(),
                        Tekil = g.Select(x => x.IpAdresi).Distinct().Count()
                    })
                    .OrderByDescending(x => x.Adet)
                    .ToList(),
                KargoPerformansi = orders
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.KargoFirmasi) ? "Aras Kargo" : x.KargoFirmasi.Trim())
                    .Select(g => new RaporKargoMetric
                    {
                        Firma = g.Key,
                        SiparisAdedi = g.Count(),
                        Kargoda = g.Count(x => x.Durum == SiparisDurumHelper.KargoyaVerildi),
                        Teslim = g.Count(x => x.Durum == SiparisDurumHelper.TeslimEdildi)
                    })
                    .OrderByDescending(x => x.SiparisAdedi)
                    .ToList()
            };

            model.DonusumOrani = model.TekilZiyaretciSayisi == 0
                ? 0
                : Math.Round(model.SiparisSayisi * 100m / model.TekilZiyaretciSayisi, 2);
            model.Oneriler = BuildInsights(model);

            return model;
        }

        private static IReadOnlyList<RaporDailyMetric> BuildDailyMetrics(DateTime startLocal, DateTime endLocal, IReadOnlyList<Siparis> orders, IReadOnlyList<ZiyaretciLog> visitorLogs)
        {
            var days = new List<RaporDailyMetric>();
            for (var day = startLocal.Date; day < endLocal.Date; day = day.AddDays(1))
            {
                var nextDay = day.AddDays(1);
                days.Add(new RaporDailyMetric
                {
                    Tarih = day,
                    Ciro = orders.Where(x => ToTurkeyLocal(x.OlusturulmaTarihi) >= day && ToTurkeyLocal(x.OlusturulmaTarihi) < nextDay).Sum(x => x.ToplamTutar),
                    Siparis = orders.Count(x => ToTurkeyLocal(x.OlusturulmaTarihi) >= day && ToTurkeyLocal(x.OlusturulmaTarihi) < nextDay),
                    Ziyaret = visitorLogs.Count(x => ToTurkeyLocal(x.OlusturulmaTarihi) >= day && ToTurkeyLocal(x.OlusturulmaTarihi) < nextDay)
                });
            }

            return days;
        }

        private static IReadOnlyList<RaporHourlyMetric> BuildHourlyMetrics(IReadOnlyList<Siparis> orders, IReadOnlyList<ZiyaretciLog> visitorLogs)
        {
            return Enumerable.Range(0, 24)
                .Select(hour => new RaporHourlyMetric
                {
                    Saat = hour,
                    Siparis = orders.Count(x => ToTurkeyLocal(x.OlusturulmaTarihi).Hour == hour),
                    Ciro = orders.Where(x => ToTurkeyLocal(x.OlusturulmaTarihi).Hour == hour).Sum(x => x.ToplamTutar),
                    Ziyaret = visitorLogs.Count(x => ToTurkeyLocal(x.OlusturulmaTarihi).Hour == hour)
                })
                .ToList();
        }

        private static IReadOnlyList<RaporInsightItem> BuildInsights(RaporIndexViewModel model)
        {
            var insights = new List<RaporInsightItem>();

            if (model.BekleyenSiparisSayisi > 0)
            {
                insights.Add(new RaporInsightItem
                {
                    Seviye = "warning",
                    Baslik = "Bekleyen siparişler var",
                    Aciklama = $"{model.BekleyenSiparisSayisi} sipariş henüz işleme alınmamış görünüyor.",
                    Link = "/Admin/Siparis?durum=0"
                });
            }

            if (model.UrunDonusumSorunlari.Count > 0)
            {
                var risk = model.UrunDonusumSorunlari.First();
                insights.Add(new RaporInsightItem
                {
                    Seviye = "warning",
                    Baslik = "Ürün dönüşüm riski",
                    Aciklama = $"{risk.UrunAdi} yüksek görüntülenmeye rağmen düşük satış üretiyor. Fiyat, görsel, varyasyon ve açıklama kontrol edilebilir.",
                    Link = $"/Admin/Urun/Duzenle/{risk.UrunId}"
                });
            }

            if (model.TerkEdilenSepetSayisi > 0)
            {
                insights.Add(new RaporInsightItem
                {
                    Seviye = "info",
                    Baslik = "Terk edilen sepet fırsatı",
                    Aciklama = $"{model.TerkEdilenSepetSayisi} terk edilen sepette yaklaşık {model.TerkEdilenSepetTutari:N2} TL potansiyel gelir var.",
                    Link = "/Admin/Rapor"
                });
            }

            if (model.IadeTalebiSayisi + model.IptalSiparisSayisi > 0)
            {
                insights.Add(new RaporInsightItem
                {
                    Seviye = "info",
                    Baslik = "İade / iptal takibi",
                    Aciklama = $"{model.IadeTalebiSayisi} iade talebi ve {model.IptalSiparisSayisi} iptal siparişi var. Nedenleri düzenli takip etmek kaliteyi artırır.",
                    Link = "/Admin/Iade"
                });
            }

            if (model.DonusumOrani > 0 && model.DonusumOrani < 1)
            {
                insights.Add(new RaporInsightItem
                {
                    Seviye = "info",
                    Baslik = "Dönüşüm oranı düşük",
                    Aciklama = $"Dönüşüm oranı %{model.DonusumOrani:N2}. Ürün detay sayfası, sepet ve kampanya akışı güçlendirilebilir.",
                    Link = "/Admin/Kupon"
                });
            }

            if (insights.Count == 0)
            {
                insights.Add(new RaporInsightItem
                {
                    Seviye = "success",
                    Baslik = "Operasyon normal görünüyor",
                    Aciklama = "Seçilen dönem için acil aksiyon gerektiren bir rapor uyarısı bulunmuyor.",
                    Link = "/Admin/Siparis"
                });
            }

            return insights;
        }

        private static (DateTime StartUtc, DateTime EndUtc, DateTime StartLocal, DateTime EndLocal) ResolveDateRange(DateTime? baslangic, DateTime? bitis)
        {
            var todayLocal = DateTime.UtcNow.AddHours(3).Date;
            var startLocal = baslangic?.Date ?? todayLocal.AddDays(-29);
            var endLocalInclusive = bitis?.Date ?? todayLocal;

            if (endLocalInclusive < startLocal)
            {
                (startLocal, endLocalInclusive) = (endLocalInclusive, startLocal);
            }

            var endLocalExclusive = endLocalInclusive.AddDays(1);
            var startUtc = DateTime.SpecifyKind(startLocal.AddHours(-3), DateTimeKind.Utc);
            var endUtc = DateTime.SpecifyKind(endLocalExclusive.AddHours(-3), DateTimeKind.Utc);
            return (startUtc, endUtc, startLocal, endLocalExclusive);
        }

        private static bool IsRevenueOrder(Siparis order)
        {
            if (SiparisDurumHelper.IsCancelled(order.Durum) || SiparisDurumHelper.IsReturn(order.Durum))
            {
                return false;
            }

            if (string.Equals(order.OdemeSaglayici, "PayTR", StringComparison.OrdinalIgnoreCase) && !order.OdemeTamamlandiMi)
            {
                return false;
            }

            return order.Durum != SiparisDurumHelper.SiparisAlindi || order.OdemeTamamlandiMi;
        }

        private static DateTime ToTurkeyLocal(DateTime value)
        {
            return value.Kind == DateTimeKind.Local ? value : value.AddHours(3);
        }

        private static string GetCustomerKey(Siparis order)
        {
            if (!string.IsNullOrWhiteSpace(order.AppUserId))
            {
                return $"u:{order.AppUserId.Trim()}";
            }

            return string.IsNullOrWhiteSpace(order.Eposta) ? string.Empty : $"e:{order.Eposta.Trim().ToLowerInvariant()}";
        }

        private static string NormalizeReferer(string? referer)
        {
            if (string.IsNullOrWhiteSpace(referer))
            {
                return "Direkt / Bilinmiyor";
            }

            if (Uri.TryCreate(referer, UriKind.Absolute, out var uri))
            {
                return uri.Host.Replace("www.", string.Empty);
            }

            return referer.Length > 40 ? referer[..40] : referer;
        }

        private static string NormalizeDevice(string? model, string? os)
        {
            var text = $"{model} {os}".ToLowerInvariant();
            if (text.Contains("iphone") || text.Contains("android") || text.Contains("mobile"))
            {
                return "Mobil";
            }

            if (text.Contains("ipad") || text.Contains("tablet"))
            {
                return "Tablet";
            }

            return "Masaüstü";
        }

        private static string NormalizeReason(string? value, string fallback)
        {
            var text = value?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback;
            }

            return text.Length > 80 ? text[..80] : text;
        }

        private static string Money(decimal value)
        {
            return $"{value:N2} TL";
        }

        private static void AddSummarySheet(ExcelPackage package, RaporIndexViewModel model)
        {
            var summary = package.Workbook.Worksheets.Add("Özet");
            var summaryRows = new (string Label, object Value)[]
            {
                ("Rapor Aralığı", model.AralikEtiketi),
                ("Ciro", model.Ciro),
                ("Sipariş Sayısı", model.SiparisSayisi),
                ("Ortalama Sepet", model.OrtalamaSepet),
                ("Satılan Ürün Adedi", model.SatilanUrunAdedi),
                ("Tekil Müşteri", model.TekilMusteriSayisi),
                ("Yeni Müşteri", model.YeniMusteriSayisi),
                ("Tekrar Müşteri", model.TekrarMusteriSayisi),
                ("Tekrar Müşteri Cirosu", model.TekrarMusteriCirosu),
                ("Ziyaret", model.ZiyaretSayisi),
                ("Tekil Ziyaretçi", model.TekilZiyaretciSayisi),
                ("Dönüşüm Oranı", $"{model.DonusumOrani:N2}%"),
                ("Toplam İndirim", model.IndirimToplami),
                ("İade Talebi", model.IadeTalebiSayisi),
                ("İptal Siparişi", model.IptalSiparisSayisi),
                ("Terk Edilen Sepet", model.TerkEdilenSepetSayisi),
                ("Terk Edilen Sepet Tutarı", model.TerkEdilenSepetTutari)
            };

            for (var i = 0; i < summaryRows.Length; i++)
            {
                summary.Cells[i + 1, 1].Value = summaryRows[i].Label;
                summary.Cells[i + 1, 2].Value = summaryRows[i].Value;
            }

            StyleKeyValueSheet(summary, summaryRows.Length);
        }

        private static void AddDailySheet(ExcelPackage package, RaporIndexViewModel model)
        {
            var ws = package.Workbook.Worksheets.Add("Günlük");
            WriteHeaders(ws, "Tarih", "Ciro", "Sipariş", "Ziyaret");
            var row = 2;
            foreach (var item in model.GunlukMetrikler)
            {
                ws.Cells[row, 1].Value = item.Tarih;
                ws.Cells[row, 2].Value = item.Ciro;
                ws.Cells[row, 3].Value = item.Siparis;
                ws.Cells[row, 4].Value = item.Ziyaret;
                row++;
            }
            ws.Column(1).Style.Numberformat.Format = "dd.mm.yyyy";
            ws.Column(2).Style.Numberformat.Format = "#,##0.00";
            AutoFit(ws);
        }

        private static void AddHourlySheet(ExcelPackage package, RaporIndexViewModel model)
        {
            var ws = package.Workbook.Worksheets.Add("Saatlik Analiz");
            WriteHeaders(ws, "Saat", "Sipariş", "Ciro", "Ziyaret");
            var row = 2;
            foreach (var item in model.SaatlikMetrikler)
            {
                ws.Cells[row, 1].Value = $"{item.Saat:00}:00";
                ws.Cells[row, 2].Value = item.Siparis;
                ws.Cells[row, 3].Value = item.Ciro;
                ws.Cells[row, 4].Value = item.Ziyaret;
                row++;
            }
            ws.Column(3).Style.Numberformat.Format = "#,##0.00";
            AutoFit(ws);
        }

        private static void AddStatusSheet(ExcelPackage package, RaporIndexViewModel model)
        {
            var ws = package.Workbook.Worksheets.Add("Sipariş Durumları");
            WriteHeaders(ws, "Durum", "Adet", "Tutar");
            var row = 2;
            foreach (var item in model.DurumDagilimi)
            {
                ws.Cells[row, 1].Value = item.Etiket;
                ws.Cells[row, 2].Value = item.Adet;
                ws.Cells[row, 3].Value = item.Tutar;
                row++;
            }
            ws.Column(3).Style.Numberformat.Format = "#,##0.00";
            AutoFit(ws);
        }

        private static void AddProductSheet(ExcelPackage package, string title, IReadOnlyList<RaporProductMetric> items)
        {
            var ws = package.Workbook.Worksheets.Add(title);
            WriteHeaders(ws, "Ürün Id", "Ürün", "Satış Adedi", "Ciro", "Görüntülenme", "Favori");
            var row = 2;
            foreach (var item in items)
            {
                ws.Cells[row, 1].Value = item.UrunId;
                ws.Cells[row, 2].Value = item.UrunAdi;
                ws.Cells[row, 3].Value = item.Adet;
                ws.Cells[row, 4].Value = item.Ciro;
                ws.Cells[row, 5].Value = item.Goruntulenme;
                ws.Cells[row, 6].Value = item.Favori;
                row++;
            }
            ws.Column(4).Style.Numberformat.Format = "#,##0.00";
            AutoFit(ws);
        }

        private static void AddConversionSheet(ExcelPackage package, RaporIndexViewModel model)
        {
            var ws = package.Workbook.Worksheets.Add("Ürün Dönüşüm Riski");
            WriteHeaders(ws, "Ürün Id", "Ürün", "Görüntülenme", "Satış", "Ciro", "Dönüşüm %", "Risk Notu");
            var row = 2;
            foreach (var item in model.UrunDonusumSorunlari)
            {
                ws.Cells[row, 1].Value = item.UrunId;
                ws.Cells[row, 2].Value = item.UrunAdi;
                ws.Cells[row, 3].Value = item.Goruntulenme;
                ws.Cells[row, 4].Value = item.SatisAdedi;
                ws.Cells[row, 5].Value = item.Ciro;
                ws.Cells[row, 6].Value = item.DonusumOrani;
                ws.Cells[row, 7].Value = item.RiskNotu;
                row++;
            }
            ws.Column(5).Style.Numberformat.Format = "#,##0.00";
            ws.Column(6).Style.Numberformat.Format = "0.00";
            AutoFit(ws);
        }

        private static void AddCustomerSheet(ExcelPackage package, RaporIndexViewModel model)
        {
            var ws = package.Workbook.Worksheets.Add("Müşteriler");
            WriteHeaders(ws, "Müşteri", "E-posta", "Şehir", "Sipariş", "Ciro", "Son Sipariş", "Tip");
            var row = 2;
            foreach (var item in model.EnDegerliMusteriler)
            {
                ws.Cells[row, 1].Value = item.Musteri;
                ws.Cells[row, 2].Value = item.Eposta;
                ws.Cells[row, 3].Value = item.Sehir;
                ws.Cells[row, 4].Value = item.SiparisAdedi;
                ws.Cells[row, 5].Value = item.Ciro;
                ws.Cells[row, 6].Value = ToTurkeyLocal(item.SonSiparisTarihi);
                ws.Cells[row, 7].Value = item.YeniMusteri ? "Yeni" : "Tekrar";
                row++;
            }
            ws.Column(5).Style.Numberformat.Format = "#,##0.00";
            ws.Column(6).Style.Numberformat.Format = "dd.mm.yyyy hh:mm";
            AutoFit(ws);
        }

        private static void AddReturnReasonSheet(ExcelPackage package, RaporIndexViewModel model)
        {
            var ws = package.Workbook.Worksheets.Add("İade İptal Nedenleri");
            WriteHeaders(ws, "Neden", "Tip", "Adet", "Tutar");
            var row = 2;
            foreach (var item in model.IadeIptalNedenleri)
            {
                ws.Cells[row, 1].Value = item.Neden;
                ws.Cells[row, 2].Value = item.Tip;
                ws.Cells[row, 3].Value = item.Adet;
                ws.Cells[row, 4].Value = item.Tutar;
                row++;
            }
            ws.Column(4).Style.Numberformat.Format = "#,##0.00";
            AutoFit(ws);
        }

        private static void AddCategorySheet(ExcelPackage package, RaporIndexViewModel model)
        {
            var ws = package.Workbook.Worksheets.Add("Kategori Performansı");
            WriteHeaders(ws, "Kategori Id", "Kategori", "Ürün Adedi", "Satış Adedi", "Ciro");
            var row = 2;
            foreach (var item in model.KategoriPerformansi)
            {
                ws.Cells[row, 1].Value = item.KategoriId;
                ws.Cells[row, 2].Value = item.KategoriAdi;
                ws.Cells[row, 3].Value = item.UrunAdedi;
                ws.Cells[row, 4].Value = item.SiparisAdedi;
                ws.Cells[row, 5].Value = item.Ciro;
                row++;
            }
            ws.Column(5).Style.Numberformat.Format = "#,##0.00";
            AutoFit(ws);
        }

        private static void AddCitySheet(ExcelPackage package, RaporIndexViewModel model)
        {
            var ws = package.Workbook.Worksheets.Add("Şehirler");
            WriteHeaders(ws, "Şehir", "Sipariş", "Ciro");
            var row = 2;
            foreach (var item in model.SehirPerformansi)
            {
                ws.Cells[row, 1].Value = item.Sehir;
                ws.Cells[row, 2].Value = item.SiparisAdedi;
                ws.Cells[row, 3].Value = item.Ciro;
                row++;
            }
            ws.Column(3).Style.Numberformat.Format = "#,##0.00";
            AutoFit(ws);
        }

        private static void AddCouponSheet(ExcelPackage package, RaporIndexViewModel model)
        {
            var ws = package.Workbook.Worksheets.Add("Kuponlar");
            WriteHeaders(ws, "Kupon", "Kullanım", "İndirim", "Ciro");
            var row = 2;
            foreach (var item in model.KuponPerformansi)
            {
                ws.Cells[row, 1].Value = item.Kod;
                ws.Cells[row, 2].Value = item.Kullanim;
                ws.Cells[row, 3].Value = item.Indirim;
                ws.Cells[row, 4].Value = item.Ciro;
                row++;
            }
            ws.Column(3).Style.Numberformat.Format = "#,##0.00";
            ws.Column(4).Style.Numberformat.Format = "#,##0.00";
            AutoFit(ws);
        }

        private static void AddTrafficSheet(ExcelPackage package, string title, IReadOnlyList<RaporTrafficMetric> items)
        {
            var ws = package.Workbook.Worksheets.Add(title);
            WriteHeaders(ws, "Başlık", "Toplam", "Tekil");
            var row = 2;
            foreach (var item in items)
            {
                ws.Cells[row, 1].Value = item.Etiket;
                ws.Cells[row, 2].Value = item.Adet;
                ws.Cells[row, 3].Value = item.Tekil;
                row++;
            }
            AutoFit(ws);
        }

        private static void AddCargoSheet(ExcelPackage package, RaporIndexViewModel model)
        {
            var ws = package.Workbook.Worksheets.Add("Kargo");
            WriteHeaders(ws, "Firma", "Sipariş", "Kargoda", "Teslim");
            var row = 2;
            foreach (var item in model.KargoPerformansi)
            {
                ws.Cells[row, 1].Value = item.Firma;
                ws.Cells[row, 2].Value = item.SiparisAdedi;
                ws.Cells[row, 3].Value = item.Kargoda;
                ws.Cells[row, 4].Value = item.Teslim;
                row++;
            }
            AutoFit(ws);
        }

        private static void WriteHeaders(ExcelWorksheet worksheet, params string[] headers)
        {
            for (var i = 0; i < headers.Length; i++)
            {
                worksheet.Cells[1, i + 1].Value = headers[i];
            }

            using var range = worksheet.Cells[1, 1, 1, headers.Length];
            range.Style.Font.Bold = true;
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(49, 53, 17));
            range.Style.Font.Color.SetColor(System.Drawing.Color.White);
        }

        private static void StyleKeyValueSheet(ExcelWorksheet worksheet, int rows)
        {
            using var firstColumn = worksheet.Cells[1, 1, rows, 1];
            firstColumn.Style.Font.Bold = true;
            firstColumn.Style.Fill.PatternType = ExcelFillStyle.Solid;
            firstColumn.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(242, 238, 229));
            worksheet.Column(2).Style.Numberformat.Format = "#,##0.00";
            AutoFit(worksheet);
        }

        private static void AutoFit(ExcelWorksheet worksheet)
        {
            if (worksheet.Dimension != null)
            {
                worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
            }
        }

        private static void PdfKpi(IContainer container, string title, string value, string subtitle)
        {
            container.Padding(4).Border(1).BorderColor("#e5e2dc").Background("#fcf9f3").Padding(10).Column(column =>
            {
                column.Item().Text(title).FontSize(8).FontColor("#6b6f45");
                column.Item().Text(value).FontSize(14).Bold().FontColor("#313511");
                column.Item().Text(subtitle).FontSize(7).FontColor("#777");
            });
        }

        private static void PdfSection(ColumnDescriptor column, string title, Action<TableDescriptor> tableBuilder)
        {
            column.Item().Text(title).FontSize(12).Bold().FontColor("#313511");
            column.Item().Table(tableBuilder);
        }

        private static void PdfHeader(TableDescriptor table, string text)
        {
            table.Cell().Background("#313511").Padding(5).Text(text).FontColor(Colors.White).Bold().FontSize(8);
        }

        private static void PdfCell(TableDescriptor table, string text)
        {
            table.Cell().BorderBottom(0.5f).BorderColor("#e5e2dc").Padding(5).Text(string.IsNullOrWhiteSpace(text) ? "-" : text).FontSize(7);
        }
    }
}
