using System.Net;
using System.Text;
using Inventory.Client.Extensions;
using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Pages.Orders;

/// <summary>
/// ساخت فاکتور/رسید چاپی مستقل و خودبسنده برای اسناد خرید/فروش (Orders).
/// سند به‌صورت HTML کامل (doctype + CSS خودبسنده) تولید می‌شود تا در iframe جداگانه
/// چاپ شود؛ چون استایل‌های <c>@media print</c>ِ خودِ برنامه روی محتوای سند اثر می‌گذارد.
/// نام فروشگاه، شماره حساب، شماره تماس و تنظیمات چاپ از «تعریف شرکت» (CompanyProfile) می‌آید.
/// </summary>
public static class SaleInvoicePrint
{
    /// <summary>سند کامل HTML چاپ را از روی سند خرید/فروش + پروفایل شرکت می‌سازد.</summary>
    public static string Build(Order order, CompanyProfileDto? profile)
    {
        var company = string.IsNullOrWhiteSpace(profile?.Name) ? "فروغ آریا" : profile!.Name.Trim();
        var isSale = order.IsSale;
        var docTitle = isSale ? "فاکتور فروش" : "رسید خرید";
        var partyLabel = isSale ? "مشتری" : "فروشنده";
        var lines = order.Lines.OrderBy(l => l.Id).ToList();
        var showSignatures = profile?.PrintShowSignatures ?? true;
        var css = CssFor(profile?.IsA5 == true);

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"fa\" dir=\"rtl\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>").Append(Esc(docTitle)).Append(' ').Append(Esc(order.Number)).Append("</title>");
        sb.Append("<style>").Append(css).Append("</style></head><body><main class=\"sheet\">");

        // ---------- متن‌های بالای فاکتور (از تنظیمات چاپ) ----------
        foreach (var hl in SplitLines(profile?.PrintHeaderLines))
            sb.Append("<div class=\"pre-head\">").Append(Esc(hl)).Append("</div>");

        // ---------- سربرگ — وسط‌چین (نام فروشگاه + عنوان سند) ----------
        sb.Append("<header class=\"header\">");
        sb.Append("<div class=\"brand-center\"><div class=\"mark\">")
            .Append(isSale ? "ف" : "ر")
            .Append("</div><div class=\"company\">")
            .Append(Esc(company)).Append("</div><div class=\"caption\">").Append(docTitle).Append("</div></div>");

        // ---------- اطلاعات تماس فروشگاه — وسط‌چین (تلفن / آدرس / شناسه‌ها) ----------
        var contactBits = new List<string>();
        if (!string.IsNullOrWhiteSpace(profile?.Phone)) contactBits.Add("تلفن: " + profile!.Phone!.Trim());
        if (!string.IsNullOrWhiteSpace(profile?.EconomicCode)) contactBits.Add("کد اقتصادی: " + profile!.EconomicCode!.Trim());
        if (!string.IsNullOrWhiteSpace(profile?.TaxId)) contactBits.Add("شناسه ملی: " + profile!.TaxId!.Trim());
        if (contactBits.Count > 0)
            sb.Append("<div class=\"contact-strip\">").Append(Esc(string.Join("  •  ", contactBits))).Append("</div>");
        if (!string.IsNullOrWhiteSpace(profile?.Address))
            sb.Append("<div class=\"contact-strip address\">آدرس: ").Append(Esc(profile!.Address!.Trim())).Append("</div>");

        sb.Append("<div class=\"invoice-stamp\"><span>").Append(docTitle).Append("</span><strong>")
            .Append(Esc(order.Number)).Append("</strong></div></header>");

        // ---------- متادیتای سند ----------
        sb.Append("<section class=\"meta\">");
        Meta(sb, "شماره سند", order.Number);
        Meta(sb, "تاریخ", order.Date.ToFa().FaDigits());
        Meta(sb, "انبار", string.IsNullOrWhiteSpace(order.WarehouseName) ? "—" : order.WarehouseName!);
        if (isSale)
            Meta(sb, "روش پرداخت", PayLabel(order.PaymentMethod));
        sb.Append("</section>");

        // ---------- طرف حساب ----------
        sb.Append("<section class=\"party\"><div class=\"party-icon\">م</div><div class=\"party-info\"><div class=\"eyebrow\">")
            .Append(partyLabel)
            .Append("</div><strong>").Append(Esc(order.PartyName ?? "—")).Append("</strong></div>");
        if (!string.IsNullOrWhiteSpace(order.ReferrerName))
            sb.Append("<div class=\"party-note\">معرف: ").Append(Esc(order.ReferrerName)).Append("</div>");
        sb.Append("</section>");

        // ---------- جدول اقلام ----------
        sb.Append("<section class=\"section\"><div class=\"section-head\"><span class=\"step\">۱</span><div><h2>اقلام سند</h2><small>")
            .Append(lines.Count.Num()).Append(" قلم</small></div></div>");
        var hasLineDiscount = lines.Any(l => l.DiscountAmount > 0);
        var totalGross = lines.Sum(l => l.Quantity * l.Price);
        var totalDiscount = lines.Sum(l => l.DiscountAmount);

        sb.Append("<table><thead><tr><th class=\"num-col\">ردیف</th><th>شرح کالا / خدمت</th><th>واحد</th><th class=\"qty-col\">تعداد</th><th class=\"money-col\">فی (ریال)</th>");
        if (hasLineDiscount) sb.Append("<th class=\"money-col\">تخفیف</th>");
        sb.Append("<th class=\"money-col\">مبلغ (ریال)</th></tr></thead><tbody>");
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            sb.Append("<tr><td class=\"num-cell\">").Append((i + 1).Num()).Append("</td><td><strong>")
                .Append(Esc(line.ProductName));
            if (line.IsService)
                sb.Append(" <span class=\"badge\">خدمت</span>");
            sb.Append("</strong></td><td class=\"qty-cell\">").Append(Esc(line.Unit))
                .Append("</td><td class=\"qty-cell\">").Append(line.Quantity.Qty())
                .Append("</td><td class=\"money-cell\">").Append(line.Price.Money());
            if (hasLineDiscount)
            {
                sb.Append("</td><td class=\"money-cell\">");
                if (line.DiscountAmount > 0)
                {
                    sb.Append(line.DiscountAmount.Money());
                    if (line.DiscountIsAmount && line.DiscountPercent > 0)
                        sb.Append(" <small class=\"text-muted\">(").Append(line.DiscountPercent.ToString("0.##")).Append("%</small>)");
                }
                else
                {
                    sb.Append("—");
                }
            }
            sb.Append("</td><td class=\"money-cell total-cell\">").Append(line.Total.Money()).Append("</td></tr>");
        }
        if (lines.Count == 0)
            sb.Append("<tr><td colspan=\"" + (hasLineDiscount ? "7" : "6") + "\" class=\"empty\">برای این سند ردیفی ثبت نشده است.</td></tr>");
        sb.Append("</tbody></table></section>");

        // ---------- توضیحات ----------
        if (!string.IsNullOrWhiteSpace(order.Description))
            sb.Append("<div class=\"desc\"><span>توضیحات: </span>").Append(Esc(order.Description)).Append("</div>");

        // ---------- جمع کل ----------
        if (hasLineDiscount && totalDiscount > 0)
        {
            sb.Append("<div style=\"margin-top:10px;padding:8px 12px;border:1px solid #e2e8f0;border-radius:10px;background:#f8fafc;font-size:10.5px;\">");
            sb.Append("<div style=\"display:flex;justify-content:space-between;padding:2px 0;\"><span>جمع اقلام (ناخالص):</span><b class=\"num\">").Append(totalGross.Money()).Append(" ریال</b></div>");
            sb.Append("<div style=\"display:flex;justify-content:space-between;padding:2px 0;color:#dc2626;\"><span>تخفیف کل اقلام:</span><b class=\"num\">- ").Append(totalDiscount.Money()).Append(" ریال</b></div>");
            sb.Append("</div>");
        }

        sb.Append("<section class=\"grand-total\"><div><span>مبلغ قابل پرداخت</span><small>جمع نهایی ")
            .Append(docTitle).Append("</small></div><strong>")
            .Append(order.TotalAmount.Money()).Append(" <small>ریال</small></strong></section>");

        // ---------- امضاها ----------
        if (showSignatures)
        {
            sb.Append("<section class=\"signatures\"><div><span>امضای ").Append(partyLabel)
                .Append("</span></div><div><span>مهر و امضای ").Append(isSale ? "فروشنده" : "خریدار")
                .Append("</span></div></section>");
        }

        // ---------- footer — شامل شماره حساب فروشگاه ----------
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

    private static void Meta(StringBuilder sb, string label, string value)
        => sb.Append("<div class=\"meta-item\"><span>").Append(Esc(label)).Append("</span><strong>").Append(Esc(value)).Append("</strong></div>");

    private static string PayLabel(PaymentMethod m) => m switch
    {
        PaymentMethod.Cash => "نقدی",
        PaymentMethod.Credit => "نسیه",
        PaymentMethod.Cheque => "چک",
        PaymentMethod.Mixed => "ترکیبی",
        _ => "اقساطی"
    };

    /// <summary>متن چندخطی را به فهرست خطوط تبدیل می‌کند (\n یا \r\n).</summary>
    private static List<string> SplitLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        return text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static string Esc(string? value) => WebUtility.HtmlEncode(value ?? "");

    /// <summary>CSS سند با اندازهٔ کاغذ انتخاب‌شده (A4 یا A5).</summary>
    private static string CssFor(bool a5) => Css
        .Replace("__PAGE_SIZE__", a5 ? "A5 portrait" : "A4 portrait")
        .Replace("__PAGE_MARGIN__", a5 ? "7mm" : "10mm")
        .Replace("__SHEET_W__", a5 ? "134mm" : "190mm");

    public const string Css = @"@font-face { font-family:'Vazirmatn'; src:url('__FONT_BASE__fonts/Vazirmatn-Regular.woff2') format('woff2'); font-weight:400; font-display:swap; }
@font-face { font-family:'Vazirmatn'; src:url('__FONT_BASE__fonts/Vazirmatn-Medium.woff2') format('woff2'); font-weight:500; font-display:swap; }
@font-face { font-family:'Vazirmatn'; src:url('__FONT_BASE__fonts/Vazirmatn-Bold.woff2') format('woff2'); font-weight:700; font-display:swap; }
@page { size:__PAGE_SIZE__; margin:__PAGE_MARGIN__; }
* { box-sizing:border-box; }
body { margin:0; background:#fff; color:#0f172a; font-family:'Vazirmatn',Tahoma,'Segoe UI',sans-serif; font-size:11px; }
.sheet { max-width:__SHEET_W__; margin:0 auto; }
.pre-head { text-align:center; color:#475569; font-size:9px; padding:1px 0; }
.header { display:block; text-align:center; padding:0 0 13px; border-bottom:3px solid #2563eb; }
.brand-center { display:grid; justify-items:center; gap:7px; margin-bottom:9px; }
.mark { width:49px; height:49px; display:grid; place-items:center; border-radius:15px; background:linear-gradient(135deg,#2563eb,#7c3aed); color:#fff; font-size:27px; font-weight:800; box-shadow:0 5px 14px #2563eb33; }
.company { color:#172554; font-size:21px; font-weight:800; }
.caption { color:#64748b; font-size:11px; }
.invoice-stamp { display:grid; justify-items:center; gap:2px; width:max-content; margin:10px auto 0; padding:8px 15px; border:1px solid #bfdbfe; border-radius:12px; background:#eff6ff; color:#1d4ed8; }
.invoice-stamp span { font-size:9px; }
.invoice-stamp strong { font-size:15px; font-weight:800; }
.contact-strip { margin-top:8px; padding:6px 12px; border:1px solid #e2e8f0; border-radius:9px; background:#f8fafc; color:#475569; font-size:9.5px; text-align:center; }
.contact-strip.address { color:#64748b; font-size:9px; }
.meta { display:grid; grid-template-columns:repeat(4,minmax(0,1fr)); gap:7px; margin:12px 0; }
.meta-item { min-width:0; display:grid; gap:4px; padding:8px 10px; border:1px solid #e2e8f0; border-radius:9px; background:#f8fafc; }
.meta-item span,.eyebrow { color:#64748b; font-size:9px; }
.meta-item strong { overflow-wrap:anywhere; color:#0f172a; font-size:10px; }
.party { display:flex; align-items:center; gap:10px; margin-bottom:14px; padding:10px 12px; border:1px solid #c7d2fe; border-radius:11px; background:#eef2ff; }
.party-icon { width:36px; height:36px; display:grid; place-items:center; border-radius:11px; background:#fff; color:#4f46e5; font-size:16px; font-weight:800; }
.party-info { display:grid; gap:2px; }
.party-info strong { color:#1e1b4b; font-size:13px; }
.party-note { margin-inline-start:auto; color:#64748b; font-size:9px; }
.section { margin-top:13px; }
.section-head { display:flex; align-items:center; gap:8px; margin-bottom:7px; }
.section-head h2 { margin:0; color:#1e293b; font-size:12px; font-weight:800; }
.section-head small { display:block; margin-top:1px; color:#94a3b8; font-size:9px; }
.step { width:23px; height:23px; display:grid; place-items:center; border-radius:8px; background:#dbeafe; color:#1d4ed8; font-size:11px; font-weight:800; }
table { width:100%; border-collapse:collapse; }
thead th { padding:8px 8px; background:#1e40af; color:#fff; text-align:right; font-size:9.5px; font-weight:700; }
thead th:first-child { border-radius:0 8px 0 0; }
thead th:last-child { border-radius:8px 0 0 0; }
tbody td { padding:7px 8px; border-bottom:1px solid #e2e8f0; vertical-align:top; }
tbody tr:nth-child(even) td { background:#f8fafc; }
.num-col { width:38px; text-align:center; }
.qty-col { width:70px; text-align:center; }
.money-col { width:110px; text-align:left; }
.num-cell { text-align:center; color:#94a3b8; }
.qty-cell { text-align:center; }
.money-cell { text-align:left; white-space:nowrap; font-variant-numeric:tabular-nums; }
.total-cell { color:#0f172a; font-weight:700; }
.badge { display:inline-block; padding:1px 6px; border-radius:6px; background:#dbeafe; color:#1d4ed8; font-size:8.5px; font-weight:700; }
.empty { padding:18px; color:#64748b; text-align:center; }
.desc { margin-top:10px; padding:8px 12px; border:1px dashed #cbd5e1; border-radius:9px; color:#475569; font-size:10px; background:#f8fafc; }
.desc span { color:#64748b; font-weight:700; }
.grand-total { display:flex; justify-content:space-between; align-items:center; gap:16px; margin-top:11px; padding:12px 14px; border:1px solid #bfdbfe; border-radius:12px; background:linear-gradient(100deg,#eff6ff,#f5f3ff); }
.grand-total div { display:grid; gap:3px; }
.grand-total span { color:#1e3a8a; font-size:12px; font-weight:800; }
.grand-total div small { color:#64748b; font-size:9px; }
.grand-total strong { color:#1d4ed8; font-size:18px; font-weight:800; white-space:nowrap; }
.grand-total strong small { font-size:9px; }
.signatures { display:flex; gap:35px; margin-top:29px; }
.signatures div { flex:1; height:36px; border-top:1px dashed #94a3b8; text-align:center; }
.signatures span { position:relative; top:5px; color:#64748b; font-size:9px; }
footer { margin-top:16px; padding-top:8px; border-top:1px solid #e2e8f0; color:#94a3b8; text-align:center; font-size:8px; }
footer div { padding:1px 0; }
footer span { padding:0 4px; color:#cbd5e1; }
footer .account-line { font-size:9.5px; font-weight:800; color:#1e3a8a; }
@media print {
 body { -webkit-print-color-adjust:exact; print-color-adjust:exact; font-size:10px; }
 .sheet { width:__SHEET_W__; max-width:100%; margin:0 auto; }
 thead { display:table-header-group; }
 tr,.grand-total,.desc,.signatures { break-inside:avoid; page-break-inside:avoid; }
 tbody td { padding:5px 6px; }
}
@media (max-width:700px) { .meta { grid-template-columns:repeat(2,minmax(0,1fr)); } .party-note { display:none; } .money-col { width:auto; } }
";
}
