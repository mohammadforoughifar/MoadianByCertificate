using ClosedXML.Excel;
using Inventory.Shared;

namespace Inventory.Api.Services.Export;

/// <summary>
/// تبدیل <see cref="ExportSpec"/> به فایل اکسل (xlsx) با ClosedXML.
///
/// اصل مهم: اعداد به‌صورت <b>عدد واقعی</b> نوشته می‌شوند نه متن فارسی،
/// تا کاربر بتواند در اکسل روی آن‌ها Sum، فیلتر و نمودار بسازد.
/// ارقام فارسی فقط در PDF استفاده می‌شود.
/// </summary>
public static class ExcelWriter
{
    // ---------- پالت رنگ هماهنگ با تم بنفش برنامه ----------
    private const string HeaderBg = "#4B2FB8";
    private const string HeaderFg = "#FFFFFF";
    private const string TitleFg = "#2E1A80";
    private const string MetaBg = "#F3F0FF";
    private const string TotalBg = "#E8E3FF";
    private const string StripeBg = "#FAF9FF";
    private const string BorderColor = "#D8D4EE";
    private const string DangerBg = "#FDECEC";
    private const string SuccessBg = "#E9F8F0";

    private const string MoneyFormat = "#,##0;[Red]-#,##0";
    private const string NumberFormat = "#,##0.###;[Red]-#,##0.###";
    private const string PercentFormat = "0.0\"٪\"";

    public static byte[] Build(ExportSpec spec)
    {
        using var wb = new XLWorkbook();
        wb.Properties.Title = spec.Title;
        wb.Properties.Author = "سامانه یکپارچه مالی و انبار";
        wb.Properties.Created = DateTime.Now;

        var ws = wb.Worksheets.Add(SheetName(spec.Title));
        ws.RightToLeft = true;
        ws.Style.Font.FontName = "Tahoma";
        ws.Style.Font.FontSize = 10;

        var colCount = spec.Columns.Count + (spec.ShowRowNumbers ? 1 : 0);
        if (colCount == 0) colCount = 1;

        var r = 1;

        // ================= سربرگ =================
        r = WriteTitle(ws, spec, colCount, r);
        r = WriteMeta(ws, spec, colCount, r);
        r = WriteSummary(ws, spec, colCount, r);

        // ================= جدول =================
        var headerRow = r;
        WriteHeader(ws, spec, headerRow);
        r++;

        var firstDataRow = r;
        var seq = 1;
        foreach (var row in spec.Rows)
        {
            WriteRow(ws, spec, row, r, stripe: (r - firstDataRow) % 2 == 1, seq: seq);
            if (row.Style is not (ExportRowStyle.Total or ExportRowStyle.Subtotal)) seq++;
            r++;
        }
        var lastDataRow = r - 1;

        // ================= جمع =================
        var total = spec.EffectiveTotalRow();
        if (total is not null)
        {
            WriteRow(ws, spec, total, r, stripe: false, seq: 0);
            r++;
        }

        // ================= یادداشت =================
        if (spec.Notes.Count > 0)
        {
            r++;
            foreach (var note in spec.Notes)
            {
                var cell = ws.Cell(r, 1);
                cell.Value = "• " + note;
                cell.Style.Font.FontSize = 9;
                cell.Style.Font.FontColor = XLColor.FromHtml("#555555");
                ws.Range(r, 1, r, colCount).Merge();
                r++;
            }
        }

        // ================= پرداخت نهایی =================
        if (lastDataRow >= firstDataRow)
        {
            // فیلتر خودکار و فریز سربرگ — کار با گزارش‌های بلند را آسان می‌کند
            ws.Range(headerRow, 1, lastDataRow, colCount).SetAutoFilter();
            ws.SheetView.FreezeRows(headerRow);
        }

        AdjustColumns(ws, spec, colCount);

        ws.PageSetup.PageOrientation = spec.Landscape ? XLPageOrientation.Landscape : XLPageOrientation.Portrait;
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.Margins.SetTop(0.5).SetBottom(0.5).SetLeft(0.4).SetRight(0.4);
        if (lastDataRow >= firstDataRow) ws.PageSetup.SetRowsToRepeatAtTop(headerRow, headerRow);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // =====================================================================
    // بخش‌های سربرگ
    // =====================================================================

    private static int WriteTitle(IXLWorksheet ws, ExportSpec spec, int colCount, int r)
    {
        var titleCell = ws.Cell(r, 1);
        titleCell.Value = spec.Title;
        titleCell.Style.Font.Bold = true;
        titleCell.Style.Font.FontSize = 16;
        titleCell.Style.Font.FontColor = XLColor.FromHtml(TitleFg);
        titleCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Range(r, 1, r, colCount).Merge();
        ws.Row(r).Height = 24;
        r++;

        if (!string.IsNullOrWhiteSpace(spec.Subtitle))
        {
            var sub = ws.Cell(r, 1);
            sub.Value = spec.Subtitle;
            sub.Style.Font.FontSize = 11;
            sub.Style.Font.FontColor = XLColor.FromHtml("#555555");
            sub.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Range(r, 1, r, colCount).Merge();
            r++;
        }

        var stamp = ws.Cell(r, 1);
        stamp.Value = $"تاریخ صدور گزارش: {PersianDate.ToShort(DateTime.Now)} — ساعت {DateTime.Now:HH:mm}";
        stamp.Style.Font.FontSize = 9;
        stamp.Style.Font.FontColor = XLColor.FromHtml("#888888");
        stamp.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Range(r, 1, r, colCount).Merge();
        r += 2;

        return r;
    }

    private static int WriteMeta(IXLWorksheet ws, ExportSpec spec, int colCount, int r)
    {
        if (spec.Meta.Count == 0) return r;

        foreach (var m in spec.Meta)
        {
            var k = ws.Cell(r, 1);
            k.Value = m.Label;
            k.Style.Font.Bold = true;
            k.Style.Font.FontSize = 9;
            k.Style.Fill.BackgroundColor = XLColor.FromHtml(MetaBg);
            k.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            k.Style.Border.OutsideBorderColor = XLColor.FromHtml(BorderColor);

            var v = ws.Cell(r, 2);
            v.Value = m.Value;
            v.Style.Font.FontSize = 9;
            v.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            v.Style.Border.OutsideBorderColor = XLColor.FromHtml(BorderColor);

            if (colCount > 2) ws.Range(r, 2, r, Math.Min(colCount, 5)).Merge();
            r++;
        }

        return r + 1;
    }

    private static int WriteSummary(IXLWorksheet ws, ExportSpec spec, int colCount, int r)
    {
        if (spec.Summary.Count == 0) return r;

        var col = 1;
        var labelRow = r;
        var valueRow = r + 1;

        foreach (var s in spec.Summary)
        {
            if (col > colCount) break;

            var lbl = ws.Cell(labelRow, col);
            lbl.Value = s.Label;
            lbl.Style.Font.FontSize = 8;
            lbl.Style.Font.FontColor = XLColor.FromHtml("#777777");
            lbl.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            lbl.Style.Fill.BackgroundColor = XLColor.FromHtml(MetaBg);
            lbl.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            lbl.Style.Border.OutsideBorderColor = XLColor.FromHtml(BorderColor);

            var val = ws.Cell(valueRow, col);
            val.Value = s.Value;
            val.Style.Font.Bold = true;
            val.Style.Font.FontSize = 11;
            val.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            val.Style.Fill.BackgroundColor = XLColor.FromHtml(MetaBg);
            val.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            val.Style.Border.OutsideBorderColor = XLColor.FromHtml(BorderColor);

            col++;
        }

        return valueRow + 2;
    }

    // =====================================================================
    // جدول
    // =====================================================================

    private static void WriteHeader(IXLWorksheet ws, ExportSpec spec, int r)
    {
        var c = 1;

        if (spec.ShowRowNumbers)
        {
            StyleHeaderCell(ws.Cell(r, c), "ردیف");
            c++;
        }

        foreach (var col in spec.Columns)
        {
            StyleHeaderCell(ws.Cell(r, c), col.Title);
            c++;
        }

        ws.Row(r).Height = 22;
    }

    private static void StyleHeaderCell(IXLCell cell, string title)
    {
        cell.Value = title;
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontSize = 10;
        cell.Style.Font.FontColor = XLColor.FromHtml(HeaderFg);
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml(HeaderBg);
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        cell.Style.Alignment.WrapText = true;
        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#3A2490");
    }

    private static void WriteRow(IXLWorksheet ws, ExportSpec spec, ExportRow row, int r, bool stripe, int seq)
    {
        var isTotal = row.Style is ExportRowStyle.Total or ExportRowStyle.Subtotal;
        var c = 1;

        string? bg = row.Style switch
        {
            ExportRowStyle.Total or ExportRowStyle.Subtotal => TotalBg,
            ExportRowStyle.Danger => DangerBg,
            ExportRowStyle.Success => SuccessBg,
            _ => stripe ? StripeBg : null
        };

        if (spec.ShowRowNumbers)
        {
            var cell = ws.Cell(r, c);
            if (isTotal) cell.Value = "جمع کل";
            else cell.Value = seq;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Font.FontSize = 9;
            if (!isTotal) cell.Style.Font.FontColor = XLColor.FromHtml("#999999");
            ApplyCommon(cell, bg, isTotal);
            c++;
        }

        for (var i = 0; i < spec.Columns.Count; i++)
        {
            var col = spec.Columns[i];
            var value = i < row.Values.Count ? row.Values[i] : null;
            var cell = ws.Cell(r, c);

            WriteValue(cell, value, col.Kind);

            if (i == 0 && row.Indent > 0)
                cell.Style.Alignment.Indent = Math.Min(row.Indent * 2, 15);

            if (col.Wrap) cell.Style.Alignment.WrapText = true;

            ApplyCommon(cell, bg, isTotal);
            c++;
        }

        if (isTotal) ws.Row(r).Height = 20;
    }

    private static void ApplyCommon(IXLCell cell, string? bg, bool bold)
    {
        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.OutsideBorderColor = XLColor.FromHtml(BorderColor);
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        if (bg is not null) cell.Style.Fill.BackgroundColor = XLColor.FromHtml(bg);
        if (bold) cell.Style.Font.Bold = true;
    }

    /// <summary>مقدار را با نوع درست در سلول می‌نویسد تا در اکسل قابل محاسبه بماند.</summary>
    private static void WriteValue(IXLCell cell, object? value, ExportValueKind kind)
    {
        if (value is null) { cell.Value = ""; return; }

        switch (kind)
        {
            case ExportValueKind.Money:
                if (ExportSpec.TryDecimal(value, out var money))
                {
                    cell.Value = money;
                    cell.Style.NumberFormat.Format = MoneyFormat;
                }
                else cell.Value = value.ToString();
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                break;

            case ExportValueKind.Number:
                if (ExportSpec.TryDecimal(value, out var num))
                {
                    cell.Value = num;
                    cell.Style.NumberFormat.Format = NumberFormat;
                }
                else cell.Value = value.ToString();
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                break;

            case ExportValueKind.Int:
                if (ExportSpec.TryDecimal(value, out var iv))
                {
                    cell.Value = iv;
                    cell.Style.NumberFormat.Format = "#,##0";
                }
                else cell.Value = value.ToString();
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                break;

            case ExportValueKind.Percent:
                if (ExportSpec.TryDecimal(value, out var pv))
                {
                    cell.Value = pv;
                    cell.Style.NumberFormat.Format = PercentFormat;
                }
                else cell.Value = value.ToString();
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                break;

            case ExportValueKind.Date:
                // تاریخ شمسی به‌صورت متن، چون اکسل تقویم جلالی ندارد
                cell.Value = value is DateTime dt ? PersianDate.ToShort(dt) : value.ToString();
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                break;

            case ExportValueKind.DateTime:
                cell.Value = value is DateTime dtt ? PersianDate.ToShortWithTime(dtt) : value.ToString();
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                break;

            case ExportValueKind.Bool:
                cell.Value = value is bool b ? (b ? "بله" : "خیر") : value.ToString();
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                break;

            default:
                cell.Value = value.ToString();
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                break;
        }
    }

    // =====================================================================
    // کمکی
    // =====================================================================

    private static void AdjustColumns(IXLWorksheet ws, ExportSpec spec, int colCount)
    {
        ws.Columns(1, colCount).AdjustToContents();

        var c = 1;
        if (spec.ShowRowNumbers) { ws.Column(c).Width = 6; c++; }

        foreach (var col in spec.Columns)
        {
            if (col.Width > 0) ws.Column(c).Width = col.Width;
            else if (ws.Column(c).Width > 45) ws.Column(c).Width = 45;   // ستون‌های خیلی پهن را مهار می‌کنیم
            else if (ws.Column(c).Width < 8) ws.Column(c).Width = 8;
            c++;
        }
    }

    /// <summary>نام کاربرگ نباید کاراکتر ممنوعه یا بیش از ۳۱ نویسه داشته باشد.</summary>
    private static string SheetName(string title)
    {
        var clean = new string(title.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(clean)) clean = "گزارش";
        return clean.Length > 31 ? clean[..31] : clean;
    }
}
