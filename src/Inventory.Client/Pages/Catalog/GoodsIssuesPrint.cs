using Inventory.Client.Extensions;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Pages.Catalog;

/// <summary>
/// ساخت سند چاپِ «حواله تحویل کالا».
/// سند به‌صورت یک سند HTML کاملاً مستقل (doctype + CSS خودبساده) تولید می‌شود تا در
/// یک iframe جداگانه چاپ شود؛ چون استایل‌های <c>@media print</c>ِ خودِ برنامه
/// (مخفی‌کردن سایدبار/محتوا و …) روی محتوای صفحه اثر می‌گذارد و چاپ را سفید می‌کند.
/// </summary>
public static class GoodsIssuesPrint
{
    /// <summary>سند کامل HTML چاپ را از روی حواله می‌سازد.</summary>
    public static string Build(GoodsIssueDto d, string company)
    {
        var companyName = string.IsNullOrWhiteSpace(company) ? "شرکت" : company;
        var total = d.Lines.Sum(l => l.Quantity);

        var sb = new System.Text.StringBuilder();
        sb.Append("<!doctype html><html lang=\"fa\" dir=\"rtl\"><head><meta charset=\"utf-8\">");
        sb.Append("<title>").Append(Esc("حواله بیجک ")).Append(Esc(d.Number)).Append("</title>");
        sb.Append("<style>").Append(Css).Append("</style></head><body>");

        // ---------- سربرگ ----------
        sb.Append("<div class=\"head\"><div class=\"brand\"><div class=\"logo\">ح</div><div>")
          .Append("<div class=\"co\">").Append(Esc(companyName)).Append("</div>")
          .Append("<div class=\"sub\">حواله بیجک</div></div></div>");

        sb.Append("<div class=\"meta\">");
        sb.Append("<div><span class=\"l\">شماره حواله</span><span class=\"v\">").Append(Esc(d.Number)).Append("</span></div>");
        sb.Append("<div><span class=\"l\">تاریخ</span><span class=\"v\">").Append(d.Date.ToFa().FaDigits()).Append("</span></div>");
        sb.Append("<div><span class=\"l\">تعداد اقلام</span><span class=\"v\">").Append(d.Lines.Count.Num()).Append("</span></div>");
        sb.Append("</div></div>");

        // ---------- مشتری ----------
        sb.Append("<div class=\"party\">");
        sb.Append("<div class=\"item\"><span class=\"l\">مشتری</span><span class=\"v\">").Append(Esc(d.PartyName)).Append("</span></div>");
        sb.Append("<div class=\"item\"><span class=\"l\">جمع مقدار</span><span class=\"v\">").Append(total.Qty()).Append("</span></div>");
        if (!string.IsNullOrWhiteSpace(d.Description))
            sb.Append("<div class=\"item\"><span class=\"l\">توضیحات</span><span class=\"v\">").Append(Esc(d.Description)).Append("</span></div>");
        sb.Append("</div>");

        // ---------- جدول اقلام ----------
        sb.Append("<table><thead><tr><th class=\"n\">#</th><th class=\"nm\">نام کالا</th>")
          .Append("<th class=\"q\">مقدار</th></tr></thead><tbody>");
        for (var i = 0; i < d.Lines.Count; i++)
        {
            var l = d.Lines[i];
            sb.Append("<tr><td class=\"n\">").Append((i + 1).Num()).Append("</td>")
              .Append("<td class=\"nm\">").Append(Esc(l.ProductName)).Append("</td>")
              .Append("<td class=\"q\">").Append(l.Quantity.Qty()).Append("</td></tr>");
        }
        sb.Append("</tbody><tfoot><tr><td class=\"tl\" colspan=\"2\">جمع مقادیر</td>")
          .Append("<td class=\"tv\">").Append(total.Qty()).Append("</td></tr></tfoot></table>");

        // ---------- امضاها ----------
        sb.Append("<div class=\"signs\"><div>امضای تحویل‌دهنده</div>")
          .Append("<div>امضای صاحب کالا</div>")
          .Append("<div>امضای مشتری / دریافت‌کننده</div></div>");

        // ---------- پانوسه ----------
        sb.Append("<div class=\"foot\">سامانه مدیریت انبار و فروش • ").Append(Esc(companyName))
          .Append(" • تولیدشده در ").Append(DateTime.Now.ToFaDateTime().FaDigits()).Append("</div>");

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string Esc(string? t) => System.Net.WebUtility.HtmlEncode(t ?? "");

    /// <summary>استایل سند چاپ (A4) — خودبساده و بدون وابستگی به بوت‌استراپ.</summary>
    public const string Css = @"@font-face { font-family:'Vazirmatn'; src:url('__FONT_BASE__fonts/Vazirmatn-Regular.woff2') format('woff2'); font-weight:400; font-display:swap; }
@font-face { font-family:'Vazirmatn'; src:url('__FONT_BASE__fonts/Vazirmatn-Medium.woff2') format('woff2'); font-weight:500; font-display:swap; }
@font-face { font-family:'Vazirmatn'; src:url('__FONT_BASE__fonts/Vazirmatn-SemiBold.woff2') format('woff2'); font-weight:600; font-display:swap; }
@font-face { font-family:'Vazirmatn'; src:url('__FONT_BASE__fonts/Vazirmatn-Bold.woff2') format('woff2'); font-weight:700; font-display:swap; }
@font-face { font-family:'Vazirmatn'; src:url('__FONT_BASE__fonts/Vazirmatn-ExtraBold.woff2') format('woff2'); font-weight:800; font-display:swap; }
@page { size: A4 portrait; margin: 12mm; }
* { box-sizing: border-box; }
body { margin:0; background:#fff; color:#0f172a; font-family:'Vazirmatn',Tahoma,'Segoe UI',sans-serif; font-size:12px; }
.head { display:flex; justify-content:space-between; align-items:flex-start; gap:12px; padding-bottom:9px; border-bottom:3px solid #4f46e5; }
.brand { display:flex; align-items:center; gap:10px; }
.logo { width:46px; height:46px; border-radius:13px; background:#4f46e5; color:#fff; font-size:24px; font-weight:800; text-align:center; line-height:46px; }
.co { font-size:16px; font-weight:800; color:#312e81; }
.sub { font-size:11.5px; color:#64748b; margin-top:2px; }
.meta { min-width:180px; font-size:11px; }
.meta div { display:flex; justify-content:space-between; gap:12px; padding:1.5px 0; }
.meta .l { color:#64748b; }
.meta .v { font-weight:800; }
.party { display:flex; gap:26px; flex-wrap:wrap; margin:12px 0 14px; background:#f8fafc; border:1px solid #e2e8f0; border-radius:9px; padding:9px 13px; }
.party .item { font-size:12px; }
.party .l { color:#64748b; margin-inline-end:7px; }
.party .v { font-weight:800; color:#1e293b; }
table { width:100%; border-collapse:collapse; }
thead th { background:#4f46e5; color:#fff; padding:8px 9px; border:1px solid #4338ca; font-size:12px; font-weight:700; }
tbody td { border:1px solid #e2e8f0; padding:7px 9px; }
tbody tr:nth-child(even) td { background:#f8fafc; }
td.n { width:44px; text-align:center; color:#94a3b8; font-weight:800; }
td.nm { font-weight:600; }
td.q { width:110px; text-align:center; font-weight:800; }
tfoot td { background:#eef2ff; border:1px solid #c7d2fe; padding:9px; font-weight:800; color:#3730a3; }
tfoot .tl { text-align:left; }
tfoot .tv { text-align:center; font-size:13.5px; }
.signs { display:flex; gap:34px; margin-top:46px; }
.signs div { flex:1; text-align:center; font-size:11px; color:#64748b; border-top:1px dashed #94a3b8; padding-top:7px; }
.foot { margin-top:26px; padding-top:8px; border-top:1px solid #e2e8f0; font-size:9.5px; color:#94a3b8; text-align:center; }";
}
