using System.Net;
using System.Text;
using Inventory.Client.Extensions;
using Inventory.Client.Pages.Orders;
using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Pages.Invoicing;

/// <summary>
/// ساخت سند چاپی فاکتور (فروش / خرید / برگشت از فروش / برگشت از خرید) برای ماژول فاکتورها.
///
/// چرا این فایل لازم شد؟
/// دکمهٔ «چاپ» در صفحهٔ فاکتور قبلاً <c>window.print()</c> صدا می‌زد؛ یعنی «کل پوستهٔ برنامه»
/// (سایدبار، نوار بالا، فیلدهای فرم و دکمه‌ها) چاپ می‌شد و به‌خاطر قواعد <c>@media print</c> برنامه
/// خروجی ناخوانا/ناقص بود. اینجا مثل فاکتور فروشِ سفارش‌ها (SaleInvoicePrint) یک سند کاملِ
/// خودبسنده (doctype + CSS مستقل) ساخته می‌شود و در iframe جداگانه چاپ می‌گردد.
///
/// سربرگ/پاورقی/اندازهٔ کاغذ (A4 یا A5) از «تعریف شرکت» (CompanyProfile) خوانده می‌شود.
/// </summary>
public static class FacInvoicePrint
{
    /// <summary>سند کامل HTML چاپ را از فاکتور + پروفایل شرکت می‌سازد.</summary>
    public static string Build(FacInvoice doc, CompanyProfileDto? profile)
    {
        var company = string.IsNullOrWhiteSpace(profile?.Name) ? "فروغ آریا" : profile!.Name.Trim();
        var title = doc.KindTitle;
        var isSaleSide = doc.Kind is InvoiceKind.Sale or InvoiceKind.SaleReturn;
        var partyLabel = isSaleSide ? "خریدار / مشتری" : "فروشنده / تأمین‌کننده";
        var lines = doc.Lines.OrderBy(l => l.RowNo).ThenBy(l => l.Id).ToList();

        var showSignatures = profile?.PrintShowSignatures ?? true;
        var a5 = profile?.IsA5 == true;

        var hasLineDiscount = lines.Any(l => l.Discount > 0);
        var hasVat = lines.Any(l => l.VatAmount > 0) || doc.TotalVat > 0;
        var hasShipping = doc.ShippingCost > 0;
        var isCredit = doc.Settlement == SettlementType.Credit;

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"fa\" dir=\"rtl\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>").Append(Esc(title)).Append(' ').Append(doc.Number.FaDigits()).Append("</title>");
        sb.Append("<style>").Append(CssFor(a5)).Append("</style></head><body><main class=\"sheet\">");

        // ---------- متن‌های بالای فاکتور (از تنظیمات چاپ شرکت) ----------
        foreach (var hl in SplitLines(profile?.PrintHeaderLines))
            sb.Append("<div class=\"pre-head\">").Append(Esc(hl)).Append("</div>");

        // ---------- سربرگ: نام شرکت + عنوان سند ----------
        sb.Append("<header class=\"header\">");
        sb.Append("<div class=\"brand-center\"><div class=\"mark\">").Append(MarkFor(doc.Kind)).Append("</div>");
        sb.Append("<div class=\"company\">").Append(Esc(company)).Append("</div>");
        sb.Append("<div class=\"caption\">").Append(Esc(title)).Append("</div></div>");

        var contactBits = new List<string>();
        if (!string.IsNullOrWhiteSpace(profile?.Phone)) contactBits.Add("تلفن: " + profile!.Phone!.Trim());
        if (!string.IsNullOrWhiteSpace(profile?.EconomicCode)) contactBits.Add("کد اقتصادی: " + profile!.EconomicCode!.Trim());
        if (!string.IsNullOrWhiteSpace(profile?.TaxId)) contactBits.Add("شناسه ملی: " + profile!.TaxId!.Trim());
        if (contactBits.Count > 0)
            sb.Append("<div class=\"contact-strip\">").Append(Esc(string.Join("  •  ", contactBits))).Append("</div>");
        if (!string.IsNullOrWhiteSpace(profile?.Address))
            sb.Append("<div class=\"contact-strip address\">آدرس: ").Append(Esc(profile!.Address!.Trim())).Append("</div>");

        sb.Append("<div class=\"invoice-stamp\"><span>").Append(Esc(title)).Append("</span><strong>")
            .Append(doc.Number.FaDigits()).Append("</strong></div></header>");

        // ---------- متادیتای فاکتور ----------
        sb.Append("<section class=\"meta\">");
        Meta(sb, "شماره فاکتور", doc.Number.FaDigits());
        Meta(sb, "تاریخ", doc.Date.ToFa().FaDigits());
        Meta(sb, "انبار", string.IsNullOrWhiteSpace(doc.WarehouseName) ? "—" : doc.WarehouseName!);
        Meta(sb, "نحوه تسویه", isCredit
            ? (doc.DueDate.HasValue ? $"نسیه — سررسید {doc.DueDate.Value.ToFa().FaDigits()}" : "نسیه")
            : "نقدی");
        if (!string.IsNullOrWhiteSpace(doc.RefNumber))
            Meta(sb, "شماره عطف", doc.RefNumber!);
        Meta(sb, "وضعیت سند", doc.StatusTitle);
        sb.Append("</section>");

        // ---------- طرف حساب ----------
        sb.Append("<section class=\"party\"><div class=\"party-icon\">م</div><div class=\"party-info\"><div class=\"eyebrow\">")
            .Append(partyLabel).Append("</div><strong>").Append(Esc(doc.PartyName ?? "—")).Append("</strong></div></section>");

        // ---------- جدول اقلام ----------
        sb.Append("<section class=\"section\"><div class=\"section-head\"><span class=\"step\">۱</span><div><h2>اقلام فاکتور</h2><small>")
            .Append(lines.Count.Num()).Append(" قلم");
        if (doc.TotalQuantity > 0)
            sb.Append(" — جمع تعداد ").Append(doc.TotalQuantity.Qty());
        sb.Append("</small></div></div>");

        sb.Append("<table><thead><tr><th class=\"num-col\">ردیف</th><th>شرح کالا / خدمت</th><th class=\"unit-col\">واحد</th><th class=\"qty-col\">تعداد</th><th class=\"money-col\">فی (ریال)</th>");
        if (hasLineDiscount) sb.Append("<th class=\"money-col\">تخفیف</th>");
        if (hasVat) sb.Append("<th class=\"money-col\">مالیات</th>");
        sb.Append("<th class=\"money-col\">جمع سطر</th></tr></thead><tbody>");

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            sb.Append("<tr><td class=\"num-cell\">").Append((i + 1).Num()).Append("</td><td><strong>")
                .Append(Esc(line.ProductName));
            if (!string.IsNullOrWhiteSpace(line.ProductCode))
                sb.Append(" <span class=\"badge\">").Append(Esc(line.ProductCode)).Append("</span>");
            sb.Append("</strong>");
            if (!string.IsNullOrWhiteSpace(line.Description))
                sb.Append("<div class=\"line-desc\">").Append(Esc(line.Description)).Append("</div>");
            sb.Append("</td><td class=\"qty-cell\">").Append(Esc(line.Unit))
                .Append("</td><td class=\"qty-cell\">").Append(line.Quantity.Qty())
                .Append("</td><td class=\"money-cell\">").Append(line.UnitPrice.Money());
            if (hasLineDiscount)
                sb.Append("</td><td class=\"money-cell\">").Append(line.Discount > 0 ? line.Discount.Money() : "—");
            if (hasVat)
                sb.Append("</td><td class=\"money-cell\">").Append(line.VatAmount > 0 ? line.VatAmount.Money() : "—");
            sb.Append("</td><td class=\"money-cell total-cell\">").Append(line.Total.Money()).Append("</td></tr>");
        }
        if (lines.Count == 0)
            sb.Append("<tr><td colspan=\"8\" class=\"empty\">برای این فاکتور ردیفی ثبت نشده است.</td></tr>");
        sb.Append("</tbody></table></section>");

        // ---------- توضیحات ----------
        if (!string.IsNullOrWhiteSpace(doc.Description))
            sb.Append("<div class=\"desc\"><span>توضیحات: </span>").Append(Esc(doc.Description)).Append("</div>");

        // ---------- جمع‌بندی مبالغ ----------
        sb.Append("<section class=\"fac-totals\">");
        TotalRow(sb, "جمع اقلام (ناخالص)", doc.TotalGross.Money());
        if (doc.TotalLineDiscount > 0) TotalRow(sb, "تخفیف اقلام", "- " + doc.TotalLineDiscount.Money());
        if (doc.InvoiceDiscount > 0) TotalRow(sb, "تخفیف کل فاکتور", "- " + doc.InvoiceDiscount.Money());
        if (doc.TotalVat > 0) TotalRow(sb, "مالیات و عوارض ارزش افزوده", "+ " + doc.TotalVat.Money());
        if (hasShipping) TotalRow(sb, "هزینه حمل و خدمات", "+ " + doc.ShippingCost.Money());
        sb.Append("<div class=\"net-row\"><span>مبلغ قابل پرداخت</span><strong class=\"num\">")
            .Append(doc.TotalNet.Money()).Append(" <small>ریال</small></strong></div>");
        if (isCredit && doc.Status == InvoiceStatus.Confirmed)
        {
            sb.Append("<div class=\"settle-row\">");
            sb.Append("<span>تسویه‌شده: <b class=\"num\">").Append(doc.SettledAmount.Money()).Append("</b></span>");
            sb.Append("<span>مانده: <b class=\"num").Append(doc.RemainingAmount > 0 ? " due" : "").Append("\">")
                .Append(doc.RemainingAmount.Money()).Append("</b></span>");
            sb.Append("</div>");
        }
        sb.Append("</section>");

        // ---------- امضاها ----------
        if (showSignatures)
        {
            sb.Append("<section class=\"signatures\"><div><span>مهر و امضای فروشنده</span></div><div><span>امضای ")
                .Append(isSaleSide ? "خریدار" : "فروشنده").Append("</span></div></section>");
        }

        // ---------- پاورقی ----------
        sb.Append("<footer>");
        if (!string.IsNullOrWhiteSpace(profile?.AccountNumber))
            sb.Append("<div class=\"account-line\"><b>شماره حساب:</b> ").Append(Esc(profile!.AccountNumber!.Trim())).Append("</div>");
        var footerLines = SplitLines(profile?.PrintFooterLines);
        if (footerLines.Count > 0)
        {
            foreach (var fl in footerLines)
                sb.Append("<div>").Append(Esc(fl)).Append("</div>");
        }
        else
        {
            sb.Append("<div>از اعتماد شما سپاسگزاریم <span>•</span> ").Append(Esc(company)).Append(" <span>•</span> چاپ‌شده در ")
                .Append(DateTime.Now.ToFaDateTime().FaDigits()).Append("</div>");
        }
        sb.Append("</footer>");

        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    private static string MarkFor(InvoiceKind kind) => kind switch
    {
        InvoiceKind.Sale => "ف",
        InvoiceKind.Purchase => "خ",
        InvoiceKind.SaleReturn => "ب",
        _ => "ر"
    };

    private static void Meta(StringBuilder sb, string label, string value)
        => sb.Append("<div class=\"meta-item\"><span>").Append(Esc(label)).Append("</span><strong>").Append(Esc(value)).Append("</strong></div>");

    private static void TotalRow(StringBuilder sb, string label, string value)
        => sb.Append("<div class=\"fac-total-row\"><span>").Append(Esc(label)).Append("</span><b class=\"num\">").Append(Esc(value)).Append("</b></div>");

    private static List<string> SplitLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        return text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static string Esc(string? value) => WebUtility.HtmlEncode(value ?? "");

    /// <summary>CSS سند — همان قالب فاکتور فروش سفارش‌ها + چند قاعدهٔ اختصاصی جمع‌بندی.</summary>
    private static string CssFor(bool a5) => SaleInvoicePrint.Css
        .Replace("__PAGE_SIZE__", a5 ? "A5 portrait" : "A4 portrait")
        .Replace("__PAGE_MARGIN__", a5 ? "7mm" : "10mm")
        .Replace("__SHEET_W__", a5 ? "134mm" : "190mm")
        + ExtraCss;

    private const string ExtraCss = @"
.line-desc { margin-top:3px; color:#64748b; font-size:9px; }
.unit-col { width:58px; text-align:center; }
.fac-totals { margin-top:11px; padding:10px 13px; border:1px solid #e2e8f0; border-radius:11px; background:#f8fafc; }
.fac-total-row { display:flex; justify-content:space-between; gap:16px; padding:3px 0; border-bottom:1px dashed #e2e8f0; color:#475569; font-size:10.5px; }
.fac-total-row b { color:#0f172a; }
.net-row { display:flex; justify-content:space-between; align-items:center; gap:16px; margin-top:9px; padding:9px 12px; border:1px solid #bfdbfe; border-radius:10px; background:linear-gradient(100deg,#eff6ff,#f5f3ff); }
.net-row span { color:#1e3a8a; font-size:12px; font-weight:800; }
.net-row strong { color:#1d4ed8; font-size:17px; font-weight:800; white-space:nowrap; }
.net-row strong small { font-size:9px; }
.settle-row { display:flex; justify-content:flex-end; gap:18px; margin-top:7px; color:#475569; font-size:10px; }
.settle-row b { color:#0f172a; }
.settle-row b.due { color:#b91c1c; }
@media print { .fac-total-row, .net-row, .settle-row { break-inside:avoid; page-break-inside:avoid; } }
";
}
