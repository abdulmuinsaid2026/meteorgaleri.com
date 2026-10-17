using KanvasProje.Core.Varliklar;
using KanvasProje.Data;
using KanvasProje.Web.Areas.Admin.Models;
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
    public class TopluFiyatGuncelleController : AdminBaseController
    {
        private readonly KanvasDbContext _context;

        public TopluFiyatGuncelleController(KanvasDbContext context)
        {
            _context = context;
        }

        private IQueryable<UrunSecenek> GetKanvasSecenekQuery(bool asNoTracking = true)
        {
            var query = _context.UrunSecenekleri.AsQueryable();
            if (asNoTracking)
            {
                query = query.AsNoTracking();
            }

            return query.Where(x => !x.SilindiMi && !string.IsNullOrWhiteSpace(x.Olcu) &&
                !x.Urun.SilindiMi &&
                x.Urun.KategoriId != 30 &&
                (x.Urun.Kategori == null || x.Urun.Kategori.ParentKategoriId != 30) &&
                x.Urun.UrunTipi != "Cam Tablo" &&
                x.Urun.UrunTipi != "Cam Kesme Tahtası" &&
                x.Urun.UrunTipi != "Ayna" &&
                (x.Urun.Slug == null || !x.Urun.Slug.Contains("cam-tablo")));
        }

        private async Task<List<OlcuFiyatModel>> GetOlcuListesiAsync(string? cerceve)
        {
            var query = GetKanvasSecenekQuery(asNoTracking: true);

            if (!string.IsNullOrWhiteSpace(cerceve))
            {
                if (cerceve == "Standart" || cerceve == "Cercevesiz" || cerceve == "Çerçevesiz" || cerceve == "Çerçevesiz / Standart")
                {
                    query = query.Where(x => string.IsNullOrWhiteSpace(x.CerceveTipi) || x.CerceveTipi == "Standart" || x.CerceveTipi == "Çerçevesiz" || x.CerceveTipi == "Cercevesiz");
                }
                else
                {
                    query = query.Where(x => x.CerceveTipi == cerceve);
                }
            }

            return await query
                .GroupBy(x => new 
                { 
                    x.Olcu, 
                    CerceveTipi = string.IsNullOrWhiteSpace(x.CerceveTipi) || x.CerceveTipi == "Standart" || x.CerceveTipi == "Çerçevesiz" || x.CerceveTipi == "Cercevesiz"
                        ? "Çerçevesiz / Standart"
                        : x.CerceveTipi
                })
                .Select(g => new OlcuFiyatModel
                {
                    Olcu = g.Key.Olcu,
                    CerceveTipi = g.Key.CerceveTipi,
                    UrunSayisi = g.Select(x => x.UrunId).Distinct().Count(),
                    VaryasyonSayisi = g.Count(),
                    MevcutFiyat = g.Min(x => x.SatisFiyati)
                })
                .OrderBy(x => x.CerceveTipi)
                .ThenBy(x => x.Olcu)
                .ToListAsync();
        }

        public async Task<IActionResult> Index(string? cerceve)
        {
            var olcuListesi = await GetOlcuListesiAsync(cerceve);
            ViewBag.SeciliCerceve = cerceve;
            return View(olcuListesi);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FiyatGuncelle(string olcu, string? cerceveTipi, string yeniFiyat)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(olcu) || !TryParseFiyat(yeniFiyat, out var parsedYeniFiyat) || parsedYeniFiyat <= 0)
                {
                    return Json(new { success = false, message = "Geçersiz fiyat veya parametre" });
                }

                var isStandart = string.IsNullOrWhiteSpace(cerceveTipi) 
                    || cerceveTipi == "Çerçevesiz / Standart" 
                    || cerceveTipi == "Standart" 
                    || cerceveTipi == "Cercevesiz"
                    || cerceveTipi == "Çerçevesiz";

                var query = GetKanvasSecenekQuery(asNoTracking: false)
                    .Where(x => x.Olcu == olcu);

                if (isStandart)
                {
                    query = query.Where(x => string.IsNullOrWhiteSpace(x.CerceveTipi) || x.CerceveTipi == "Standart" || x.CerceveTipi == "Çerçevesiz" || x.CerceveTipi == "Cercevesiz");
                }
                else
                {
                    query = query.Where(x => x.CerceveTipi == cerceveTipi);
                }

                var varyasyonlar = await query.ToListAsync();

                if (!varyasyonlar.Any())
                {
                    return Json(new { success = false, message = "Kanvas tablo ölçüsü bulunamadı" });
                }

                await using var transaction = await _context.Database.BeginTransactionAsync();

                var urunIdler = varyasyonlar.Select(x => x.UrunId).Distinct().ToList();

                foreach (var varyasyon in varyasyonlar)
                {
                    varyasyon.SatisFiyati = parsedYeniFiyat;
                    varyasyon.MaliyetFiyati = Math.Round(parsedYeniFiyat * 0.6m, 2);
                }

                var urunler = await _context.Urunler
                    .Where(x => urunIdler.Contains(x.Id))
                    .Include(x => x.UrunSecenek)
                    .ToListAsync();

                foreach (var urun in urunler)
                {
                    urun.IndirimliFiyat = null;
                    var minVarFiyat = urun.UrunSecenek
                        .Where(s => !s.SilindiMi && s.AktifMi && s.SatisFiyati > 0)
                        .Select(s => s.SatisFiyati)
                        .DefaultIfEmpty(0)
                        .Min();
                    if (minVarFiyat > 0)
                    {
                        urun.Fiyat = minVarFiyat;
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Json(new { success = true, message = $"{varyasyonlar.Count} kanvas tablo varyasyonu güncellendi ({cerceveTipi ?? "Standart"})" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Hata: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Guncelle(List<OlcuFiyatGuncelleModel> fiyatlar)
        {
            if (fiyatlar == null || !fiyatlar.Any())
            {
                TempData["Hata"] = "Güncellenecek fiyat bulunamadı.";
                return RedirectToAction(nameof(Index));
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();
            int guncellenenSayisi = 0;
            var affectedUrunIds = new HashSet<int>();

            foreach (var item in fiyatlar.Where(x => x.YeniFiyat.HasValue && x.YeniFiyat > 0 && !string.IsNullOrWhiteSpace(x.Olcu)))
            {
                var isStandart = string.IsNullOrWhiteSpace(item.CerceveTipi) 
                    || item.CerceveTipi == "Çerçevesiz / Standart" 
                    || item.CerceveTipi == "Standart"
                    || item.CerceveTipi == "Cercevesiz"
                    || item.CerceveTipi == "Çerçevesiz";

                var query = GetKanvasSecenekQuery(asNoTracking: false)
                    .Where(x => x.Olcu == item.Olcu);

                if (isStandart)
                    query = query.Where(x => string.IsNullOrWhiteSpace(x.CerceveTipi) || x.CerceveTipi == "Standart" || x.CerceveTipi == "Çerçevesiz" || x.CerceveTipi == "Cercevesiz");
                else
                    query = query.Where(x => x.CerceveTipi == item.CerceveTipi);

                var varyasyonlar = await query.ToListAsync();

                foreach (var varyasyon in varyasyonlar)
                {
                    varyasyon.SatisFiyati = item.YeniFiyat!.Value;
                    varyasyon.MaliyetFiyati = Math.Round(item.YeniFiyat.Value * 0.6m, 2);
                    affectedUrunIds.Add(varyasyon.UrunId);
                }

                guncellenenSayisi += varyasyonlar.Count;
            }

            if (guncellenenSayisi > 0 && affectedUrunIds.Count > 0)
            {
                var urunler = await _context.Urunler
                    .Where(x => affectedUrunIds.Contains(x.Id))
                    .Include(x => x.UrunSecenek)
                    .ToListAsync();

                foreach (var urun in urunler)
                {
                    urun.IndirimliFiyat = null;
                    var minVarFiyat = urun.UrunSecenek
                        .Where(s => !s.SilindiMi && s.AktifMi && s.SatisFiyati > 0)
                        .Select(s => s.SatisFiyati)
                        .DefaultIfEmpty(0)
                        .Min();
                    if (minVarFiyat > 0)
                    {
                        urun.Fiyat = minVarFiyat;
                    }
                }
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Basari"] = $"{guncellenenSayisi} kanvas varyasyon güncellendi.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NormalizeOlcu()
        {
            try
            {
                var varyasyonlar = await GetKanvasSecenekQuery(asNoTracking: false)
                    .ToListAsync();

                int sayac = 0;
                foreach (var v in varyasyonlar)
                {
                    var duzeltilmis = NormalizeOlcuString(v.Olcu);
                    if (duzeltilmis != v.Olcu)
                    {
                        v.Olcu = duzeltilmis;
                        sayac++;
                    }
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = $"{sayac} kanvas ölçüsü normalize edildi" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Hata: " + ex.Message });
            }
        }

        public async Task<IActionResult> ExportExcel(string? cerceve)
        {
            ExcelPackage.License.SetNonCommercialOrganization("MeteorGaleri");
            var olculer = await GetOlcuListesiAsync(cerceve);

            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("Kanvas Fiyat Listesi");

            var headers = new[] { "Ölçü", "Çerçeve Tipi", "Ürün Sayısı", "Varyasyon Sayısı", "Mevcut Satış Fiyatı (TL)" };
            for (var i = 0; i < headers.Length; i++)
            {
                worksheet.Cells[1, i + 1].Value = headers[i];
            }

            var row = 2;
            foreach (var item in olculer)
            {
                worksheet.Cells[row, 1].Value = item.Olcu;
                worksheet.Cells[row, 2].Value = item.CerceveTipi;
                worksheet.Cells[row, 3].Value = item.UrunSayisi;
                worksheet.Cells[row, 4].Value = item.VaryasyonSayisi;
                worksheet.Cells[row, 5].Value = item.MevcutFiyat;
                worksheet.Cells[row, 5].Style.Numberformat.Format = "#,##0.00 ₺";
                row++;
            }

            using (var range = worksheet.Cells[1, 1, 1, headers.Length])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(49, 53, 17));
                range.Style.Font.Color.SetColor(System.Drawing.Color.White);
            }

            if (worksheet.Dimension != null)
            {
                worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
            }

            return File(
                package.GetAsByteArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"kanvas-tablo-fiyat-listesi-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
        }

        public async Task<IActionResult> ExportPdf(string? cerceve)
        {
            QuestPDF.Settings.License = LicenseType.Community;
            var olculer = await GetOlcuListesiAsync(cerceve);

            var pdfBytes = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(24);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                    page.Header().Column(col =>
                    {
                        col.Item().Text("MeteorGaleri - Kanvas Tablo Fiyat Listesi").FontSize(16).SemiBold().FontColor("#1B2A4A");
                        col.Item().Text($"Rapor Tarihi: {DateTime.Now:dd.MM.yyyy HH:mm} | Toplam Ölçü: {olculer.Count}")
                            .FontSize(9)
                            .FontColor("#6b6b61");
                    });

                    page.Content().PaddingTop(12).Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(3);
                            cols.RelativeColumn(3);
                            cols.RelativeColumn(2);
                            cols.RelativeColumn(2);
                            cols.RelativeColumn(3);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background("#313511").Padding(5).Text("Ölçü").SemiBold().FontColor("#ffffff");
                            header.Cell().Background("#313511").Padding(5).Text("Çerçeve").SemiBold().FontColor("#ffffff");
                            header.Cell().Background("#313511").Padding(5).Text("Ürün Sayısı").SemiBold().FontColor("#ffffff");
                            header.Cell().Background("#313511").Padding(5).Text("Varyant Sayısı").SemiBold().FontColor("#ffffff");
                            header.Cell().Background("#313511").Padding(5).Text("Mevcut Fiyat").SemiBold().FontColor("#ffffff");
                        });

                        foreach (var item in olculer)
                        {
                            table.Cell().BorderBottom(1).BorderColor("#e5e2dc").Padding(5).Text(item.Olcu);
                            table.Cell().BorderBottom(1).BorderColor("#e5e2dc").Padding(5).Text(item.CerceveTipi);
                            table.Cell().BorderBottom(1).BorderColor("#e5e2dc").Padding(5).Text(item.UrunSayisi.ToString());
                            table.Cell().BorderBottom(1).BorderColor("#e5e2dc").Padding(5).Text(item.VaryasyonSayisi.ToString());
                            table.Cell().BorderBottom(1).BorderColor("#e5e2dc").Padding(5).Text($"{item.MevcutFiyat:N2} TL");
                        }
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Sayfa ");
                        x.CurrentPageNumber();
                        x.Span(" / ");
                        x.TotalPages();
                    });
                });
            }).GeneratePdf();

            return File(pdfBytes, "application/pdf", $"kanvas-tablo-fiyat-listesi-{DateTime.Now:yyyyMMdd-HHmm}.pdf");
        }

        private string NormalizeOlcuString(string olcu)
        {
            if (string.IsNullOrWhiteSpace(olcu)) return olcu;

            var s = olcu.Trim();

            // Çok parçalı tespiti
            string parcaSuffix = "";
            var mParca = System.Text.RegularExpressions.Regex.Match(s, @"(\d+)\s*[pP]ar[çc]a");
            if (mParca.Success)
            {
                parcaSuffix = $" ({mParca.Groups[1].Value} Parça)";
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(s, @"x\s*4\s*$"))
            {
                parcaSuffix = " (4 Parça)";
            }

            var sClean = System.Text.RegularExpressions.Regex.Replace(s, @"\d+\s*[pP]ar[çc]a.*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            sClean = System.Text.RegularExpressions.Regex.Replace(sClean, @"x\s*4\s*$", "");

            var m = System.Text.RegularExpressions.Regex.Match(sClean, @"(\d+)\s*(?:cm)?\s*[xX×]\s*(\d+)\s*(?:cm)?");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int w) && int.TryParse(m.Groups[2].Value, out int h))
            {
                return $"{w}cm x {h}cm{parcaSuffix}";
            }

            return s;
        }

        private static bool TryParseFiyat(string? input, out decimal result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;

            var clean = input.Trim().Replace(" ", "").Replace("TL", "").Replace("₺", "");
            if (clean.Contains(",") && clean.Contains("."))
            {
                return decimal.TryParse(clean, new System.Globalization.CultureInfo("tr-TR"), out result);
            }
            if (clean.Contains(","))
            {
                return decimal.TryParse(clean, new System.Globalization.CultureInfo("tr-TR"), out result);
            }
            return decimal.TryParse(clean, System.Globalization.CultureInfo.InvariantCulture, out result);
        }
    }

    // Geriye dönük uyumluluk için Controller namespace'inde tutulan tipler
    public class OlcuFiyatModel : KanvasProje.Web.Areas.Admin.Models.OlcuFiyatModel { }
    public class OlcuFiyatGuncelleModel : KanvasProje.Web.Areas.Admin.Models.OlcuFiyatGuncelleModel { }
}