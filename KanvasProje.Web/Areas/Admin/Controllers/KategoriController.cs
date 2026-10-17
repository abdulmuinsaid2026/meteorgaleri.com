using KanvasProje.Core.Varliklar;
using KanvasProje.Data;
using KanvasProje.Core.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KanvasProje.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class KategoriController : AdminBaseController
    {
        private readonly KanvasDbContext _context;

        public KategoriController(KanvasDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string? arama, string? durum, string? tip)
        {
            var kategoriler = await BuildCategoryListQuery(arama, durum, tip)
                .ToListAsync();

            ViewBag.Arama = arama;
            ViewBag.Durum = durum;
            ViewBag.Tip = tip;
            ViewBag.ToplamKategori = kategoriler.Count;
            ViewBag.AktifKategori = kategoriler.Count(x => x.AktifMi && !x.SilindiMi);
            ViewBag.AnaKategori = kategoriler.Count(x => !x.ParentKategoriId.HasValue);
            ViewBag.AltKategori = kategoriler.Count(x => x.ParentKategoriId.HasValue);

            return View(kategoriler);
        }

        public async Task<IActionResult> ExcelExport(string? arama, string? durum, string? tip)
        {
            ExcelPackage.License.SetNonCommercialOrganization("MeteorGaleri");
            var kategoriler = await BuildCategoryListQuery(arama, durum, tip).ToListAsync();
            var categoryLookup = kategoriler.ToDictionary(x => x.Id);

            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("Kategoriler");
            var headers = new[]
            {
                "ID",
                "Kategori",
                "Hiyerarşi",
                "Üst Kategori",
                "Slug",
                "Kısa Açıklama",
                "Ürün Sayısı",
                "Alt Kategori Sayısı",
                "Sıra",
                "Durum",
                "SEO Başlığı",
                "SEO Açıklaması"
            };

            for (var i = 0; i < headers.Length; i++)
            {
                worksheet.Cells[1, i + 1].Value = headers[i];
            }

            var row = 2;
            foreach (var kategori in kategoriler)
            {
                worksheet.Cells[row, 1].Value = kategori.Id;
                worksheet.Cells[row, 2].Value = kategori.Ad;
                worksheet.Cells[row, 3].Value = CategoryPresentationHelper.BuildHierarchyLabel(kategori, categoryLookup);
                worksheet.Cells[row, 4].Value = kategori.ParentKategori?.Ad ?? "Ana kategori";
                worksheet.Cells[row, 5].Value = kategori.Slug;
                worksheet.Cells[row, 6].Value = kategori.KisaAciklama;
                worksheet.Cells[row, 7].Value = kategori.Urunler.Count(x => !x.SilindiMi);
                worksheet.Cells[row, 8].Value = kategori.AltKategoriler.Count(x => !x.SilindiMi);
                worksheet.Cells[row, 9].Value = kategori.Sira;
                worksheet.Cells[row, 10].Value = kategori.AktifMi && !kategori.SilindiMi ? "Aktif" : "Pasif";
                worksheet.Cells[row, 11].Value = kategori.SeoTitle;
                worksheet.Cells[row, 12].Value = kategori.SeoDescription;
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
                $"kategoriler-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
        }

        public async Task<IActionResult> PdfExport(string? arama, string? durum, string? tip)
        {
            QuestPDF.Settings.License = LicenseType.Community;
            var kategoriler = await BuildCategoryListQuery(arama, durum, tip).ToListAsync();
            var categoryLookup = kategoriler.ToDictionary(x => x.Id);

            var pdfBytes = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(24);
                    page.DefaultTextStyle(x => x.FontSize(8).FontFamily("Arial"));

                    page.Header().Column(column =>
                    {
                        column.Item().Text("Kategori Yönetimi Raporu").FontSize(18).SemiBold().FontColor("#313511");
                        column.Item().Text($"Oluşturulma: {DateTime.Now:dd.MM.yyyy HH:mm} | Kayıt: {kategoriler.Count}")
                            .FontSize(9)
                            .FontColor("#6b6b61");
                    });

                    page.Content().PaddingTop(14).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(34);
                            columns.RelativeColumn(2.4f);
                            columns.RelativeColumn(2.8f);
                            columns.RelativeColumn(1.9f);
                            columns.RelativeColumn(2.2f);
                            columns.ConstantColumn(46);
                            columns.ConstantColumn(50);
                            columns.ConstantColumn(46);
                            columns.ConstantColumn(50);
                        });

                        table.Header(header =>
                        {
                            AddPdfHeader(header, "ID");
                            AddPdfHeader(header, "Kategori");
                            AddPdfHeader(header, "Hiyerarşi");
                            AddPdfHeader(header, "Üst Kategori");
                            AddPdfHeader(header, "Slug");
                            AddPdfHeader(header, "Ürün");
                            AddPdfHeader(header, "Alt");
                            AddPdfHeader(header, "Sıra");
                            AddPdfHeader(header, "Durum");
                        });

                        foreach (var kategori in kategoriler)
                        {
                            AddPdfCell(table, kategori.Id.ToString());
                            AddPdfCell(table, kategori.Ad);
                            AddPdfCell(table, CategoryPresentationHelper.BuildHierarchyLabel(kategori, categoryLookup));
                            AddPdfCell(table, kategori.ParentKategori?.Ad ?? "Ana kategori");
                            AddPdfCell(table, kategori.Slug ?? "-");
                            AddPdfCell(table, kategori.Urunler.Count(x => !x.SilindiMi).ToString());
                            AddPdfCell(table, kategori.AltKategoriler.Count(x => !x.SilindiMi).ToString());
                            AddPdfCell(table, kategori.Sira.ToString());
                            AddPdfCell(table, kategori.AktifMi && !kategori.SilindiMi ? "Aktif" : "Pasif");
                        }
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

            return File(pdfBytes, "application/pdf", $"kategoriler-{DateTime.Now:yyyyMMdd-HHmm}.pdf");
        }

        [HttpGet]
        public async Task<IActionResult> Ekle()
        {
            await PopulateParentCategoriesAsync();
            return View(new Kategori
            {
                AktifMi = true,
                UrunSiralamaTipi = "manual"
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Ekle(Kategori kategori)
        {
            if (!await ValidateCategoryAsync(kategori))
            {
                await PopulateParentCategoriesAsync(kategori.ParentKategoriId);
                return View(kategori);
            }

            kategori.OlusturulmaTarihi = DateTime.UtcNow;
            kategori.SilindiMi = false;
            kategori.Sira = await NormalizeCategoryOrderAsync(kategori.Sira);
            kategori.UrunSiralamaTipi = NormalizeSortType(kategori.UrunSiralamaTipi);
            kategori.Slug = await GenerateUniqueCategorySlugAsync(kategori.Slug, kategori.Ad, null);

            _context.Kategoriler.Add(kategori);
            await _context.SaveChangesAsync();

            TempData["Basari"] = "Kategori başarıyla oluşturuldu.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Duzenle(int id)
        {
            var kategori = await _context.Kategoriler
                .Include(x => x.ParentKategori)
                .Include(x => x.AltKategoriler)
                .Include(x => x.Urunler)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (kategori == null)
            {
                return NotFound();
            }

            PopulateCategoryEditStats(kategori);
            await PopulateParentCategoriesAsync(kategori.ParentKategoriId, kategori.Id);
            return View(kategori);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Duzenle(Kategori model)
        {
            var kategori = await _context.Kategoriler.FirstOrDefaultAsync(x => x.Id == model.Id);
            if (kategori == null)
            {
                return NotFound();
            }

            if (!await ValidateCategoryAsync(model))
            {
                PopulateCategoryEditStats(kategori);
                await PopulateParentCategoriesAsync(model.ParentKategoriId, model.Id);
                return View(model);
            }

            kategori.Ad = model.Ad.Trim();
            kategori.KisaAciklama = model.KisaAciklama?.Trim() ?? string.Empty;
            kategori.Aciklama = model.Aciklama?.Trim() ?? string.Empty;
            kategori.GorselUrl = string.IsNullOrWhiteSpace(model.GorselUrl) ? null : model.GorselUrl.Trim();
            kategori.BannerUrl = string.IsNullOrWhiteSpace(model.BannerUrl) ? null : model.BannerUrl.Trim();
            kategori.ParentKategoriId = model.ParentKategoriId;
            kategori.AktifMi = model.AktifMi;
            kategori.Sira = await NormalizeCategoryOrderAsync(model.Sira);
            kategori.SeoTitle = model.SeoTitle?.Trim() ?? string.Empty;
            kategori.SeoDescription = model.SeoDescription?.Trim() ?? string.Empty;
            kategori.UstMetin = model.UstMetin?.Trim() ?? string.Empty;
            kategori.AltMetin = model.AltMetin?.Trim() ?? string.Empty;
            kategori.KampanyaEtiketi = model.KampanyaEtiketi?.Trim() ?? string.Empty;
            kategori.UrunSiralamaTipi = NormalizeSortType(model.UrunSiralamaTipi);
            kategori.Slug = await GenerateUniqueCategorySlugAsync(model.Slug, model.Ad, model.Id);

            await _context.SaveChangesAsync();

            TempData["Basari"] = "Kategori güncellendi.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Sil(int id)
        {
            var kategori = await _context.Kategoriler.FirstOrDefaultAsync(x => x.Id == id);
            if (kategori == null)
            {
                return RedirectToAction(nameof(Index));
            }

            var altKategoriler = await _context.Kategoriler
                .Where(x => x.ParentKategoriId == id)
                .ToListAsync();

            foreach (var altKategori in altKategoriler)
            {
                altKategori.ParentKategoriId = null;
            }

            kategori.AktifMi = false;
            kategori.SilindiMi = true;

            await _context.SaveChangesAsync();
            TempData["Basari"] = "Kategori arşive alındı.";
            return RedirectToAction(nameof(Index));
        }

        private IQueryable<Kategori> BuildCategoryListQuery(string? arama, string? durum, string? tip)
        {
            var query = _context.Kategoriler
                .Include(x => x.ParentKategori)
                .Include(x => x.AltKategoriler)
                .Include(x => x.Urunler)
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(arama))
            {
                var term = arama.Trim().ToLower();
                query = query.Where(x =>
                    x.Ad.ToLower().Contains(term) ||
                    (x.Slug != null && x.Slug.ToLower().Contains(term)) ||
                    x.KisaAciklama.ToLower().Contains(term) ||
                    x.SeoTitle.ToLower().Contains(term));
            }

            query = durum switch
            {
                "aktif" => query.Where(x => x.AktifMi && !x.SilindiMi),
                "pasif" => query.Where(x => !x.AktifMi || x.SilindiMi),
                _ => query
            };

            query = tip switch
            {
                "ana" => query.Where(x => x.ParentKategoriId == null),
                "alt" => query.Where(x => x.ParentKategoriId != null),
                _ => query
            };

            return query
                .OrderBy(x => x.Sira)
                .ThenBy(x => x.Ad);
        }

        private static void AddPdfHeader(TableCellDescriptor header, string text)
        {
            header.Cell()
                .Background("#313511")
                .Border(0.5f)
                .BorderColor("#313511")
                .Padding(5)
                .Text(text)
                .FontColor(Colors.White)
                .SemiBold();
        }

        private static void AddPdfCell(TableDescriptor table, string text)
        {
            table.Cell()
                .BorderBottom(0.5f)
                .BorderColor("#e5e2dc")
                .Padding(5)
                .Text(string.IsNullOrWhiteSpace(text) ? "-" : text);
        }

        private async Task PopulateParentCategoriesAsync(int? selectedId = null, int? excludedId = null)
        {
            ViewBag.ParentKategoriler = new SelectList(
                await BuildParentCategoryOptionsAsync(excludedId),
                "Value",
                "Text",
                selectedId?.ToString());
        }

        private void PopulateCategoryEditStats(Kategori kategori)
        {
            ViewBag.UrunSayisi = kategori.Urunler?.Count(x => !x.SilindiMi) ?? 0;
            ViewBag.AltKategoriSayisi = kategori.AltKategoriler?.Count(x => !x.SilindiMi) ?? 0;
            ViewBag.UstKategoriAdi = kategori.ParentKategori?.Ad ?? "Ana kategori";
        }

        private async Task<List<SelectListItem>> BuildParentCategoryOptionsAsync(int? excludedId)
        {
            var kategoriler = await _context.Kategoriler
                .OrderBy(x => x.Sira)
                .ThenBy(x => x.Ad)
                .ToListAsync();

            var optionList = new List<SelectListItem>
            {
                new() { Value = string.Empty, Text = "Ana kategori" }
            };

            foreach (var (category, depth) in CategoryTreeHelper.FlattenHierarchy(kategoriler, excludedId))
            {
                optionList.Add(new SelectListItem
                {
                    Value = category.Id.ToString(),
                    Text = $"{new string('-', depth * 2)}{(depth > 0 ? " " : string.Empty)}{category.Ad}"
                });
            }

            return optionList;
        }

        private async Task<bool> ValidateCategoryAsync(Kategori kategori)
        {
            if (string.IsNullOrWhiteSpace(kategori.Ad))
            {
                ModelState.AddModelError(nameof(Kategori.Ad), "Kategori adı zorunludur.");
            }

            if (kategori.ParentKategoriId == kategori.Id && kategori.Id != 0)
            {
                ModelState.AddModelError(nameof(Kategori.ParentKategoriId), "Bir kategori kendisinin üst kategorisi olamaz.");
            }

            if (kategori.ParentKategoriId.HasValue)
            {
                var categories = await _context.Kategoriler
                    .IgnoreQueryFilters()
                    .ToListAsync();

                var parentExists = categories.Any(x => x.Id == kategori.ParentKategoriId.Value);
                if (!parentExists)
                {
                    ModelState.AddModelError(nameof(Kategori.ParentKategoriId), "Seçilen üst kategori bulunamadı.");
                }
                else if (kategori.Id != 0 && CategoryTreeHelper.IsDescendant(categories, kategori.Id, kategori.ParentKategoriId.Value))
                {
                    ModelState.AddModelError(nameof(Kategori.ParentKategoriId), "Bir kategori kendi alt dalının altına taşınamaz.");
                }
            }

            return ModelState.IsValid;
        }

        private async Task<int> NormalizeCategoryOrderAsync(int currentOrder)
        {
            if (currentOrder > 0)
            {
                return currentOrder;
            }

            var lastOrder = await _context.Kategoriler
                .OrderByDescending(x => x.Sira)
                .Select(x => (int?)x.Sira)
                .FirstOrDefaultAsync();

            return (lastOrder ?? 0) + 1;
        }

        private static string NormalizeSortType(string? sortType)
        {
            var value = sortType?.Trim().ToLowerInvariant();
            return value switch
            {
                "price_asc" => "price_asc",
                "price_desc" => "price_desc",
                "newest" => "newest",
                "popular" => "popular",
                _ => "manual"
            };
        }

        private async Task<string> GenerateUniqueCategorySlugAsync(string? requestedSlug, string title, int? excludedId)
        {
            var baseSlug = SlugHelper.GenerateSlug(string.IsNullOrWhiteSpace(requestedSlug) ? title : requestedSlug);
            var existingSlugs = await _context.Kategoriler
                .Where(x => x.Id != excludedId && x.Slug != null)
                .Select(x => x.Slug!)
                .ToListAsync();

            return SlugHelper.EnsureUnique(baseSlug, existingSlugs);
        }
    }
}
