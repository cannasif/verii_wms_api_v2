using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.SystemManagement.Application.Users;

public sealed partial class UserManagementService
{
    private const int ImportTemplateLastRow = MaxImportRows + 1;
    private const int ExcelMaximumColumnCount = 16_384;

    private static readonly string[] TemplateBaseHeaders =
    [
        "Username",
        "Email",
        "Password",
        "FirstName",
        "LastName",
        "PhoneNumber",
        "Role",
        "IsActive"
    ];

    public async Task<byte[]> CreateImportTemplateAsync(CancellationToken cancellationToken)
    {
        var groups = await Groups.Query()
            .Where(group => group.IsActive)
            .OrderBy(group => group.Name)
            .ThenBy(group => group.Id)
            .Select(group => new ImportTemplatePermissionGroup(
                group.Id,
                group.Name,
                group.Description,
                group.IsSystemAdmin))
            .ToListAsync(cancellationToken);
        var currentPasswordPolicy = await passwordPolicy.GetAsync(cancellationToken);

        if (TemplateBaseHeaders.Length + groups.Count > ExcelMaximumColumnCount)
        {
            throw AppException.BadRequest(
                "Aktif yetki grubu sayısı Excel kolon sınırını aştığı için şablon oluşturulamadı.");
        }

        using var workbook = new XLWorkbook();
        CreateUsersWorksheet(workbook, groups);
        CreateGuideWorksheet(workbook, currentPasswordPolicy, groups);
        CreateExampleWorksheet(workbook, groups);
        CreatePermissionGroupsWorksheet(workbook, groups);

        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    internal static string PermissionGroupColumnHeader(long id, string name) =>
        $"PermissionGroup[{id}] {name.Trim()}";

    private static void CreateUsersWorksheet(
        XLWorkbook workbook,
        IReadOnlyList<ImportTemplatePermissionGroup> groups)
    {
        var worksheet = workbook.Worksheets.Add("Kullanıcılar");
        var headers = BuildTemplateHeaders(groups);
        WriteHeaderRow(worksheet, headers);
        StyleDataEntryArea(worksheet, headers.Length);

        worksheet.SheetView.FreezeRows(1);
        worksheet.Range(1, 1, ImportTemplateLastRow, headers.Length).SetAutoFilter();
        ConfigurePrintLayout(worksheet, repeatHeaderRow: true);
        worksheet.Column(1).Width = 22;
        worksheet.Column(2).Width = 30;
        worksheet.Column(3).Width = 20;
        worksheet.Columns(4, 6).Width = 18;
        worksheet.Column(7).Width = 14;
        worksheet.Column(8).Width = 12;
        if (headers.Length > TemplateBaseHeaders.Length)
        {
            worksheet.Columns(TemplateBaseHeaders.Length + 1, headers.Length).Width = 28;
            worksheet.Range(1, TemplateBaseHeaders.Length + 1, 1, headers.Length).Style.Alignment.WrapText = true;
        }

        AddListValidation(
            worksheet.Range(2, 7, ImportTemplateLastRow, 7),
            "User,Manager,Admin",
            "Rol seçimi",
            "Role alanı User, Manager veya Admin olmalıdır.");
        AddListValidation(
            worksheet.Range(2, 8, ImportTemplateLastRow, 8),
            "true,false",
            "Aktiflik seçimi",
            "IsActive alanı true veya false olmalıdır.");

        for (var column = TemplateBaseHeaders.Length + 1; column <= headers.Length; column++)
        {
            AddListValidation(
                worksheet.Range(2, column, ImportTemplateLastRow, column),
                "true,false",
                "Yetki grubu seçimi",
                "Kullanıcı bu gruba atanacaksa true, atanmayacaksa false seçin.");
        }

        AddUniqueValueValidation(
            worksheet.Range(2, 1, ImportTemplateLastRow, 1),
            $"COUNTIF($A$2:$A${ImportTemplateLastRow},A2)=1",
            "Aynı kullanıcı adı dosyada yalnızca bir kez kullanılabilir.");
        AddUniqueValueValidation(
            worksheet.Range(2, 2, ImportTemplateLastRow, 2),
            $"COUNTIF($B$2:$B${ImportTemplateLastRow},B2)=1",
            "Aynı e-posta adresi dosyada yalnızca bir kez kullanılabilir.");
    }

    private static void CreateGuideWorksheet(
        XLWorkbook workbook,
        PasswordPolicyResponse passwordPolicy,
        IReadOnlyList<ImportTemplatePermissionGroup> groups)
    {
        var worksheet = workbook.Worksheets.Add("Açıklamalar");
        worksheet.Range("A1:F1").Merge();
        worksheet.Cell("A1").Value = "WMS V2 — Kullanıcı Aktarım Şablonu";
        worksheet.Range("A1:F1").Style
            .Fill.SetBackgroundColor(XLColor.FromHtml("#10243E"))
            .Font.SetFontColor(XLColor.White)
            .Font.SetBold()
            .Font.SetFontSize(16);
        worksheet.Row(1).Height = 30;

        worksheet.Range("A3:F4").Merge();
        worksheet.Cell("A3").Value =
            "ÖNEMLİ: Bu aktarım yalnızca yeni kullanıcı kaydı oluşturur. Mevcut kullanıcıları güncellemez, pasife almaz veya silmez. " +
            "Kullanıcı adı ve e-posta dosya içinde benzersiz olmalıdır; sistemde mevcut olan kayıtlar değiştirilmeden atlanır.";
        worksheet.Range("A3:F4").Style
            .Fill.SetBackgroundColor(XLColor.FromHtml("#FFF4D6"))
            .Font.SetFontColor(XLColor.FromHtml("#7A4B00"))
            .Font.SetBold()
            .Alignment.SetWrapText();

        var guideRows = new[]
        {
            new[] { "Kolon", "Zorunlu", "Beklenen değer", "Örnek", "Kural", "Güvenlik notu" },
            new[] { "Username", "Evet", "3-100 karakter", "ayse.yilmaz", "Dosya içinde benzersiz", "Sistemde mevcutsa satır atlanır" },
            new[] { "Email", "Evet", "Geçerli e-posta", "ayse.yilmaz@firma.com", "Dosya içinde benzersiz; en fazla 200 karakter", "Sistemde mevcutsa satır atlanır" },
            new[] { "Password", "Evet", $"{passwordPolicy.MinimumLength}-{passwordPolicy.MaximumLength} karakter", "Gecici!2026", "İndirme ve yükleme anındaki merkezi şifre politikasına uymalı", "Sonuçlarda ve loglarda gösterilmez" },
            new[] { "FirstName", "Hayır", "Metin", "Ayşe", "En fazla 100 karakter", "" },
            new[] { "LastName", "Hayır", "Metin", "Yılmaz", "En fazla 100 karakter", "" },
            new[] { "PhoneNumber", "Hayır", "Metin", "+90 555 000 00 00", "En fazla 40 karakter", "" },
            new[] { "Role", "Evet", "User | Manager | Admin", "User", "Superadmin açılamaz", "Listeden seçin" },
            new[] { "IsActive", "Evet", "true | false", "true", "1/0 ve evet/hayır da yüklemede kabul edilir", "Listeden seçin" },
            new[] { "PermissionGroup[ID] Grup Adı", "Hayır", "true | false", "true", "Birden fazla grup sütununda true seçilebilir", "Yalnızca indirme anındaki aktif gruplar eklenir" }
        };

        for (var row = 0; row < guideRows.Length; row++)
            for (var column = 0; column < guideRows[row].Length; column++)
                if (!string.IsNullOrEmpty(guideRows[row][column]))
                    worksheet.Cell(row + 6, column + 1).Value = guideRows[row][column];

        StyleTable(worksheet, 6, guideRows.Length + 5, 6);
        worksheet.Range("A17:F18").Merge();
        worksheet.Cell("A17").Value =
            $"Şablon indirilirken {groups.Count} aktif yetki grubu anlık olarak eklendi. Kullanıcılar sayfasında atanacak her grup için true seçin; " +
            "kolon adlarını veya sırasını değiştirmeyin. Dosyayı .xlsx biçiminde kaydedip yükleyin.";
        worksheet.Range("A17:F18").Style
            .Fill.SetBackgroundColor(XLColor.FromHtml("#E0F7FA"))
            .Font.SetFontColor(XLColor.FromHtml("#155E75"))
            .Alignment.SetWrapText();
        worksheet.Columns(1, 6).AdjustToContents(1, 18, 10, 42);
        worksheet.Column(5).Width = 42;
        worksheet.Column(6).Width = 36;
        worksheet.SheetView.FreezeRows(5);
        ConfigurePrintLayout(worksheet);
    }

    private static void CreateExampleWorksheet(
        XLWorkbook workbook,
        IReadOnlyList<ImportTemplatePermissionGroup> groups)
    {
        var worksheet = workbook.Worksheets.Add("Örnek");
        var headers = BuildTemplateHeaders(groups);
        WriteHeaderRow(worksheet, headers);

        var example = new[]
        {
            "ayse.yilmaz",
            "ayse.yilmaz@firma.com",
            "Gecici!2026",
            "Ayşe",
            "Yılmaz",
            "+90 555 000 00 00",
            "User",
            "true"
        };
        for (var column = 0; column < example.Length; column++)
            worksheet.Cell(2, column + 1).Value = example[column];
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            worksheet.Cell(2, TemplateBaseHeaders.Length + groupIndex + 1).Value = groupIndex < 2 ? "true" : "false";

        worksheet.Range(2, 1, 2, headers.Length).Style
            .Fill.SetBackgroundColor(XLColor.FromHtml("#F3F7FA"))
            .Border.SetBottomBorder(XLBorderStyleValues.Thin)
            .Border.SetBottomBorderColor(XLColor.FromHtml("#D8E1EA"));
        worksheet.Columns(1, headers.Length).AdjustToContents(1, 2, 12, 32);
        worksheet.SheetView.FreezeRows(1);
        ConfigurePrintLayout(worksheet, repeatHeaderRow: true);
    }

    private static void CreatePermissionGroupsWorksheet(
        XLWorkbook workbook,
        IReadOnlyList<ImportTemplatePermissionGroup> groups)
    {
        var worksheet = workbook.Worksheets.Add("Yetki Grupları");
        var headers = new[] { "ID", "Yetki Grubu", "Açıklama", "Sistem Yöneticisi", "Şablon Kolonu" };
        WriteHeaderRow(worksheet, headers);

        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            var row = index + 2;
            worksheet.Cell(row, 1).Value = group.Id;
            worksheet.Cell(row, 2).Value = group.Name;
            if (!string.IsNullOrWhiteSpace(group.Description))
                worksheet.Cell(row, 3).Value = group.Description;
            worksheet.Cell(row, 4).Value = group.IsSystemAdmin ? "Evet" : "Hayır";
            worksheet.Cell(row, 5).Value = PermissionGroupColumnHeader(group.Id, group.Name);
        }

        if (groups.Count == 0)
        {
            worksheet.Range("A2:E2").Merge();
            worksheet.Cell("A2").Value = "Şablon indirilirken aktif yetki grubu bulunamadı.";
        }
        else
        {
            StyleTable(worksheet, 1, groups.Count + 1, headers.Length);
        }

        worksheet.Columns(1, headers.Length).AdjustToContents(1, Math.Max(groups.Count + 1, 2), 10, 42);
        worksheet.Column(3).Width = 42;
        worksheet.Column(5).Width = 38;
        worksheet.SheetView.FreezeRows(1);
        ConfigurePrintLayout(worksheet, repeatHeaderRow: true);
    }

    private static string[] BuildTemplateHeaders(IReadOnlyList<ImportTemplatePermissionGroup> groups) =>
        [.. TemplateBaseHeaders, .. groups.Select(group => PermissionGroupColumnHeader(group.Id, group.Name))];

    private static void WriteHeaderRow(IXLWorksheet worksheet, IReadOnlyList<string> headers)
    {
        for (var column = 0; column < headers.Count; column++)
            worksheet.Cell(1, column + 1).Value = headers[column];

        worksheet.Range(1, 1, 1, headers.Count).Style
            .Fill.SetBackgroundColor(XLColor.FromHtml("#10243E"))
            .Font.SetFontColor(XLColor.White)
            .Font.SetBold()
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
            .Border.SetBottomBorder(XLBorderStyleValues.Thin)
            .Border.SetBottomBorderColor(XLColor.FromHtml("#4DD9E7"));
        worksheet.Row(1).Height = 30;
    }

    private static void StyleDataEntryArea(IXLWorksheet worksheet, int columnCount)
    {
        worksheet.Range(2, 1, ImportTemplateLastRow, columnCount).Style
            .Border.SetBottomBorder(XLBorderStyleValues.Hair)
            .Border.SetBottomBorderColor(XLColor.FromHtml("#D8E1EA"));
    }

    private static void StyleTable(IXLWorksheet worksheet, int firstRow, int lastRow, int columnCount)
    {
        worksheet.Range(firstRow, 1, firstRow, columnCount).Style
            .Fill.SetBackgroundColor(XLColor.FromHtml("#0E7490"))
            .Font.SetFontColor(XLColor.White)
            .Font.SetBold()
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        if (lastRow > firstRow)
        {
            worksheet.Range(firstRow + 1, 1, lastRow, columnCount).Style
                .Alignment.SetWrapText()
                .Border.SetBottomBorder(XLBorderStyleValues.Thin)
                .Border.SetBottomBorderColor(XLColor.FromHtml("#D8E1EA"));
        }
    }

    private static void AddListValidation(
        IXLRange range,
        string values,
        string errorTitle,
        string errorMessage)
    {
        var validation = range.CreateDataValidation();
        validation.List($"\"{values}\"", inCellDropdown: true);
        validation.IgnoreBlanks = true;
        validation.ShowErrorMessage = true;
        validation.ErrorStyle = XLErrorStyle.Stop;
        validation.ErrorTitle = errorTitle;
        validation.ErrorMessage = errorMessage;
    }

    private static void AddUniqueValueValidation(IXLRange range, string formula, string errorMessage)
    {
        var validation = range.CreateDataValidation();
        validation.Custom(formula);
        validation.IgnoreBlanks = true;
        validation.ShowErrorMessage = true;
        validation.ErrorStyle = XLErrorStyle.Stop;
        validation.ErrorTitle = "Tekrarlanan değer";
        validation.ErrorMessage = errorMessage;
    }

    private static void ConfigurePrintLayout(IXLWorksheet worksheet, bool repeatHeaderRow = false)
    {
        worksheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        worksheet.PageSetup.FitToPages(1, 0);
        worksheet.PageSetup.Margins.SetTop(0.3).SetBottom(0.3).SetLeft(0.3).SetRight(0.3);
        if (repeatHeaderRow)
            worksheet.PageSetup.SetRowsToRepeatAtTop(1, 1);
    }

    private sealed record ImportTemplatePermissionGroup(
        long Id,
        string Name,
        string? Description,
        bool IsSystemAdmin);
}
