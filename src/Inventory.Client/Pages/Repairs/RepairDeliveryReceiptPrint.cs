using System.Net;
using System.Text;
using Inventory.Client.Extensions;
using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Pages;

/// <summary>
/// ساخت «فیش تحویل و خدمات دستگاه»؛
/// کاملاً متمایز از فاکتور فروش حسابداری با تمرکز بر مشخصات دستگاه،
/// متعلقات همراه، گزارش فنی خدمات، تاییدیه رضایت/سلامت دستگاه توسط مشتری و گارانتی.
/// </summary>
public static class RepairDeliveryReceiptPrint
{
    public static string Build(RepairOrderDto repair, string? companyName, CompanyProfileDto? profile = null)
    {
        var company = !string.IsNullOrWhiteSpace(profile?.Name)
            ? profile!.Name.Trim()
            : (!string.IsNullOrWhiteSpace(companyName) ? companyName.Trim() : "فروغ آریا");

        var devices = DevicesFor(repair);
        var phone = !string.IsNullOrWhiteSpace(repair.PartyMobile) ? repair.PartyMobile : repair.PartyPhone;
        var deliveryDate = repair.DeliveredAt ?? DateTime.Now;

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"fa\" dir=\"rtl\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>").Append("فیش تحویل دستگاه ").Append(Esc(repair.Number)).Append("</title>");
        sb.Append("<style>").Append(Css).Append("</style></head><body><main class=\"sheet\">");

        // ---------- سربرگ اختصاصی فیش تحویل کارگاهی ----------
        sb.Append("<header class=\"header\">");
        sb.Append("<div class=\"brand\">");
        sb.Append("<div class=\"receipt-badge-icon\"><i class=\"mark\">ت</i></div>");
        sb.Append("<div>");
        sb.Append("<div class=\"company\">").Append(Esc(company)).Append("</div>");
        sb.Append("<div class=\"caption\">واحد تخصصی خدمات و تعمیرات سخت‌افزار و تجهیزات</div>");
        sb.Append("</div></div>");

        sb.Append("<div class=\"receipt-stamp\">");
        sb.Append("<div class=\"tag\">رسید ترخیص و تحویل</div>");
        sb.Append("<strong>فیش تحویل دستگاه</strong>");
        sb.Append("<div class=\"number num\">").Append(Esc(repair.Number)).Append("</div>");
        sb.Append("</div>");
        sb.Append("</header>");

        // ---------- نوار مشخصات پذیرش و ترخیص ----------
        sb.Append("<section class=\"meta-grid\">");
        Meta(sb, "شماره سند پذیرش", repair.Number);
        Meta(sb, "تاریخ و ساعت پذیرش", repair.ReceivedAt.ToFaDateTime().FaDigits());
        Meta(sb, "تاریخ تحویل دستگاه", deliveryDate.ToFaDateTime().FaDigits());
        Meta(sb, "کارشناس فنی", string.IsNullOrWhiteSpace(repair.TechnicianName) ? "واحد خدمات فنی" : repair.TechnicianName!);
        sb.Append("</section>");

        // ---------- اطلاعات تحویل‌گیرنده / مشتری ----------
        sb.Append("<section class=\"party-card\">");
        sb.Append("<div class=\"party-title\"><span>تحویل‌گیرنده / صاحب دستگاه:</span> <strong>")
            .Append(Esc(repair.PartyName ?? "مشتری گرامی"))
            .Append("</strong></div>");
        if (!string.IsNullOrWhiteSpace(phone))
        {
            sb.Append("<div class=\"party-contact\"><span>شماره تماس:</span> <b dir=\"ltr\" class=\"num\">")
                .Append(Esc(phone)).Append("</b></div>");
        }
        sb.Append("<div class=\"party-status\">وضعیت سند: <b>")
            .Append(repair.DeliveredAt.HasValue ? "تحویل شده به مشتری" : "آماده تحویل و ترخیص")
            .Append("</b></div>");
        sb.Append("</section>");

        // ---------- بخش ۱: مشخصات فنی و فیزیکی دستگاه‌ها و متعلقات تحویلی ----------
        sb.Append("<section class=\"section\"><div class=\"section-title\"><span class=\"bullet\">▪</span> مشخصات دستگاه تعمیری و متعلقات همراه تحویل‌شده</div>");
        sb.Append("<div class=\"devices-list\">");
        for (var i = 0; i < devices.Count; i++)
        {
            var d = devices[i];
            sb.Append("<article class=\"device-box\">");
            sb.Append("<div class=\"device-header\">");
            sb.Append("<span class=\"idx\">دستگاه ").Append((i + 1).Num()).Append("</span>");
            sb.Append("<strong>").Append(Esc(DeviceLabel(d))).Append("</strong>");
            sb.Append("</div>");

            sb.Append("<div class=\"device-grid\">");
            if (!string.IsNullOrWhiteSpace(d.SerialNumber))
            {
                sb.Append("<div class=\"d-item\"><span class=\"lbl\">شماره سریال:</span><span class=\"val num-code\" dir=\"ltr\">")
                    .Append(Esc(d.SerialNumber)).Append("</span></div>");
            }
            if (!string.IsNullOrWhiteSpace(d.ProblemDescription))
            {
                sb.Append("<div class=\"d-item\"><span class=\"lbl\">ایراد اظهارشده:</span><span class=\"val\">")
                    .Append(Esc(d.ProblemDescription)).Append("</span></div>");
            }
            if (!string.IsNullOrWhiteSpace(d.Accessories))
            {
                sb.Append("<div class=\"d-item highlight\"><span class=\"lbl\">لوازم و متعلقات همراه:</span><span class=\"val fw-bold\">")
                    .Append(Esc(d.Accessories)).Append("</span></div>");
            }
            if (d.QuotedPrice > 0)
            {
                sb.Append("<div class=\"d-item\"><span class=\"lbl\">برآورد اولیه:</span><span class=\"val num\">")
                    .Append(d.QuotedPrice.Money()).Append(" ریال</span></div>");
            }
            sb.Append("</div>");
            sb.Append("</article>");
        }
        sb.Append("</div></section>");

        // ---------- بخش ۲: گزارش کارهای فنی انجام‌شده و قطعات ----------
        sb.Append("<section class=\"section\"><div class=\"section-title\"><span class=\"bullet\">▪</span> شرح اقدامات فنی، خدمات انجام‌شده و قطعات مصرفی</div>");
        sb.Append("<table class=\"receipt-table\"><thead><tr>");
        sb.Append("<th style=\"width:35px\">ردیف</th><th>شرح خدمات فنی و قطعات تعویض‌شده</th><th style=\"width:50px\">تعداد</th><th style=\"width:95px\">اجرت/فی (ریال)</th><th style=\"width:105px\">جمع (ریال)</th>");
        sb.Append("</tr></thead><tbody>");

        var items = repair.Items.OrderBy(i => i.Id).ToList();
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var desc = !string.IsNullOrWhiteSpace(it.Description) ? it.Description : (it.ProductName ?? "خدمات تعمیراتی");
            var total = it.Quantity * it.Price;
            sb.Append("<tr>");
            sb.Append("<td class=\"text-center num\">").Append((i + 1).Num()).Append("</td>");
            sb.Append("<td><strong>").Append(Esc(desc)).Append("</strong>");
            if (!string.IsNullOrWhiteSpace(it.ProductName) && !string.Equals(desc, it.ProductName, StringComparison.Ordinal))
            {
                sb.Append("<div class=\"sub-desc\">قطعه انبار: ").Append(Esc(it.ProductName)).Append("</div>");
            }
            sb.Append("</td>");
            sb.Append("<td class=\"text-center num\">").Append(it.Quantity.Qty()).Append("</td>");
            sb.Append("<td class=\"text-left num\">").Append(it.Price.Money()).Append("</td>");
            sb.Append("<td class=\"text-left num fw-bold\">").Append(total.Money()).Append("</td>");
            sb.Append("</tr>");
        }
        if (items.Count == 0)
        {
            sb.Append("<tr><td colspan=\"5\" class=\"empty-msg\">سرویس و بازبینی فنی دستگاه طبق درخواست انجام گرفت.</td></tr>");
        }
        sb.Append("</tbody></table></section>");

        // ---------- خلاصه تسویه مالی فیش تحویل ----------
        var grandTotal = repair.TotalPrice > 0 ? repair.TotalPrice : repair.QuotedPrice;
        sb.Append("<section class=\"settlement-box\">");
        sb.Append("<div class=\"settlement-meta\">");
        sb.Append("<div>روش تسویه: <strong>").Append(repair.InvoiceTransactionId is > 0 ? "تسویه شده / فاکتور فروش" : "تسویه نقدی کارگاهی").Append("</strong></div>");
        if (!string.IsNullOrWhiteSpace(repair.Note))
        {
            sb.Append("<div class=\"repair-note\">یادداشت ترخیص: ").Append(Esc(repair.Note)).Append("</div>");
        }
        sb.Append("</div>");
        sb.Append("<div class=\"settlement-total\">");
        sb.Append("<span>مبلغ کل خدمات و قطعات:</span>");
        sb.Append("<strong class=\"num\">").Append(grandTotal.Money()).Append(" <small>ریال</small></strong>");
        sb.Append("</div>");
        sb.Append("</section>");

        // ---------- بخش حقوقی تاییدیه و اقرار تحویل دستگاه (متمایزکننده فیش تحویل) ----------
        sb.Append("<section class=\"acceptance-clause\">");
        sb.Append("<div class=\"clause-title\"><i class=\"check-icon\">✓</i> <b>اقرارنامه و تاییدیه تحویل دستگاه توسط مشتری:</b></div>");
        sb.Append("<p class=\"clause-text\">");
        sb.Append("اینجانب (صاحب دستگاه یا نماینده تام‌الاختیار وی)، بدینوسیله تایید و اقرار می‌نمایم که دستگاه مندرج در این فیش را به همراه کلیه لوازم، قطعات جانبی و متعلقات تحویل داده شده، به صورت <b>روشن، تست‌شده، سالم و بدون هیچ‌گونه ایراد فیزیکی، شکستگی یا نقص ظاهری</b> تحویل گرفتم.");
        sb.Append("</p>");
        sb.Append("</section>");

        // ---------- شرایط گارانتی و مقررات خدمات ----------
        sb.Append("<section class=\"warranty-box\">");
        sb.Append("<div><b>شرایط گارانتی و ضوابط ترخیص:</b></div>");
        sb.Append("<ol>");
        sb.Append("<li>قطعات تعویضی و خدمات فنی انجام‌شده دارای ۱۰ روز مهلت تست و ضمانت کارگاهی می‌باشند.</li>");
        sb.Append("<li>صدمات ناشی از ضربه، نوسانات الکتریکی، آب‌خوردگی، شکستگی، باز شدن دستگاه توسط افراد غیرمجاز و مشکلات نرم‌افزاری جدید شامل گارانتی نمی‌باشد.</li>");
        sb.Append("<li>ارائه اصل این فیش جهت هرگونه استفاده از گارانتی یا پیگیری بعدی الزامی است.</li>");
        sb.Append("</ol>");
        sb.Append("</section>");

        // ---------- امضاها: تحویل‌گیرنده و تحویل‌دهنده ----------
        sb.Append("<section class=\"signatures\">");
        sb.Append("<div class=\"sig-box\">");
        sb.Append("<div class=\"sig-role\">نام و امضای تحویل‌گیرنده (مشتری)</div>");
        sb.Append("<div class=\"sig-sub\">«دستگاه صحیح، سالم و تست‌شده تحویل گرفته شد»</div>");
        sb.Append("</div>");
        sb.Append("<div class=\"sig-box\">");
        sb.Append("<div class=\"sig-role\">مهر و امضای واحد فنی و تحویل خدمات</div>");
        sb.Append("<div class=\"sig-sub\">").Append(Esc(company)).Append("</div>");
        sb.Append("</div>");
        sb.Append("</section>");

        sb.Append("<footer>فیش تحویل کارگاهی <span>•</span> ").Append(Esc(company)).Append(" <span>•</span> زمان صدور فیش: ")
            .Append(DateTime.Now.ToFaDateTime().FaDigits()).Append("</footer>");
        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    private static void Meta(StringBuilder sb, string label, string value)
        => sb.Append("<div class=\"meta-card\"><span>").Append(Esc(label)).Append("</span><strong>").Append(Esc(value)).Append("</strong></div>");

    private static List<RepairDeviceDto> DevicesFor(RepairOrderDto repair)
    {
        if (repair.Devices is { Count: > 0 }) return repair.Devices;
        return new List<RepairDeviceDto>
        {
            new()
            {
                DeviceType = repair.DeviceType,
                DeviceModel = repair.DeviceModel,
                SerialNumber = repair.SerialNumber,
                ProblemDescription = repair.ProblemDescription,
                Accessories = repair.Accessories,
                QuotedPrice = repair.QuotedPrice
            }
        };
    }

    private static string DeviceLabel(RepairDeviceDto d)
        => string.IsNullOrWhiteSpace(d.DeviceModel)
            ? (string.IsNullOrWhiteSpace(d.DeviceType) ? "دستگاه" : d.DeviceType)
            : $"{d.DeviceType} {d.DeviceModel}".Trim();

    private static string Esc(string? value) => WebUtility.HtmlEncode(value ?? "");

    public const string Css = @"@font-face { font-family:'Vazirmatn'; src:url('__FONT_BASE__fonts/Vazirmatn-Regular.woff2') format('woff2'); font-weight:400; font-display:swap; }
@font-face { font-family:'Vazirmatn'; src:url('__FONT_BASE__fonts/Vazirmatn-Medium.woff2') format('woff2'); font-weight:500; font-display:swap; }
@font-face { font-family:'Vazirmatn'; src:url('__FONT_BASE__fonts/Vazirmatn-Bold.woff2') format('woff2'); font-weight:700; font-display:swap; }
@page { size:A5 portrait; margin:7mm; }
* { box-sizing:border-box; }
body { margin:0; background:#fff; color:#0f172a; font-family:'Vazirmatn',Tahoma,'Segoe UI',sans-serif; font-size:10.5px; line-height:1.5; }
.sheet { max-width:134mm; margin:0 auto; padding:2px; }
.header { display:flex; justify-content:space-between; align-items:center; gap:10px; padding-bottom:10px; border-bottom:2px dashed #0284c7; }
.brand { display:flex; align-items:center; gap:10px; }
.receipt-badge-icon .mark { width:42px; height:42px; display:grid; place-items:center; border-radius:12px; background:linear-gradient(135deg,#0284c7,#0369a1); color:#fff; font-size:24px; font-weight:800; font-style:normal; }
.company { color:#0c4a6e; font-size:16px; font-weight:800; }
.caption { margin-top:2px; color:#64748b; font-size:9.5px; }
.receipt-stamp { display:grid; justify-items:center; gap:2px; min-width:130px; padding:6px 12px; border:2px solid #0284c7; border-radius:10px; background:#f0f9ff; color:#0369a1; text-align:center; }
.receipt-stamp .tag { font-size:8.5px; color:#0284c7; font-weight:600; }
.receipt-stamp strong { font-size:13px; font-weight:800; }
.receipt-stamp .number { font-size:12px; font-weight:700; letter-spacing:0.5px; }
.meta-grid { display:grid; grid-template-columns:repeat(4,minmax(0,1fr)); gap:5px; margin:9px 0; }
.meta-card { display:grid; gap:2px; padding:6px 8px; border:1px solid #e2e8f0; border-radius:8px; background:#f8fafc; font-size:9px; }
.meta-card span { color:#64748b; }
.meta-card strong { color:#0f172a; font-size:9.5px; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
.party-card { display:flex; flex-wrap:wrap; justify-content:space-between; align-items:center; gap:8px; padding:8px 12px; border:1px solid #bae6fd; border-radius:8px; background:#f0f9ff; margin-bottom:10px; font-size:10px; }
.party-title strong { color:#0369a1; font-size:12px; }
.party-contact b { color:#0f172a; }
.party-status b { color:#0284c7; }
.section { margin-top:10px; }
.section-title { font-size:11px; font-weight:700; color:#0c4a6e; margin-bottom:6px; display:flex; align-items:center; gap:5px; }
.bullet { color:#0284c7; font-size:14px; }
.devices-list { display:grid; gap:6px; }
.device-box { border:1px solid #cbd5e1; border-radius:8px; padding:8px 10px; background:#fff; }
.device-header { display:flex; align-items:center; gap:8px; font-size:11px; color:#0f172a; margin-bottom:6px; }
.device-header .idx { background:#e2e8f0; color:#475569; font-size:9px; font-weight:700; padding:1px 6px; border-radius:5px; }
.device-grid { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:4px 10px; font-size:9.5px; }
.d-item { display:flex; gap:6px; }
.d-item.highlight { grid-column:span 2; background:#fffbeb; padding:3px 6px; border-radius:5px; border:1px solid #fef3c7; }
.d-item .lbl { color:#64748b; flex:0 0 auto; }
.d-item .val { color:#1e293b; overflow-wrap:anywhere; }
.num-code { font-family:monospace,'Vazirmatn'; font-weight:700; letter-spacing:1px; color:#0369a1; }
.receipt-table { width:100%; border-collapse:collapse; margin-top:4px; font-size:9.5px; }
.receipt-table th { background:#f1f5f9; color:#334155; font-weight:700; padding:6px 6px; border:1px solid #cbd5e1; text-align:right; }
.receipt-table td { padding:5px 6px; border:1px solid #e2e8f0; vertical-align:middle; }
.text-center { text-align:center; }
.text-left { text-align:left; font-variant-numeric:tabular-nums; }
.sub-desc { font-size:8px; color:#64748b; margin-top:1px; }
.empty-msg { text-align:center; color:#64748b; padding:10px; font-style:italic; }
.settlement-box { display:flex; justify-content:space-between; align-items:center; gap:12px; margin-top:9px; padding:9px 12px; border:1.5px solid #0284c7; border-radius:9px; background:#f0f9ff; }
.settlement-meta { font-size:9.5px; color:#334155; }
.repair-note { font-size:8.5px; color:#64748b; margin-top:2px; }
.settlement-total { text-align:left; }
.settlement-total span { display:block; font-size:9px; color:#64748b; }
.settlement-total strong { font-size:16px; font-weight:800; color:#0369a1; }
.settlement-total small { font-size:9px; }
.acceptance-clause { margin-top:10px; padding:8px 10px; border:1.5px solid #059669; border-radius:8px; background:#f0fdf4; font-size:9px; }
.clause-title { display:flex; align-items:center; gap:5px; color:#065f46; font-weight:700; margin-bottom:3px; }
.check-icon { display:inline-grid; place-items:center; width:16px; height:16px; border-radius:50%; background:#10b981; color:#fff; font-size:10px; font-style:normal; font-weight:bold; }
.clause-text { margin:0; color:#166534; line-height:1.6; text-align:justify; }
.warranty-box { margin-top:8px; padding:6px 9px; border:1px solid #e2e8f0; border-radius:7px; background:#f8fafc; font-size:8px; color:#64748b; line-height:1.5; }
.warranty-box b { color:#334155; font-size:8.5px; }
.warranty-box ol { margin:2px 0 0; padding-inline-start:16px; }
.signatures { display:flex; gap:20px; margin-top:18px; }
.sig-box { flex:1; border-top:1px dashed #64748b; padding-top:6px; text-align:center; min-height:50px; }
.sig-role { font-size:9.5px; font-weight:700; color:#1e293b; }
.sig-sub { font-size:8px; color:#64748b; margin-top:3px; }
footer { margin-top:12px; padding-top:6px; border-top:1px solid #e2e8f0; color:#94a3b8; text-align:center; font-size:7.5px; }
footer span { padding:0 3px; }
@media print {
 body { -webkit-print-color-adjust:exact; print-color-adjust:exact; font-size:9.5px; }
 .sheet { width:134mm; max-width:100%; margin:0 auto; }
 tr,.device-box,.settlement-box,.acceptance-clause,.warranty-box,.signatures { break-inside:avoid; page-break-inside:avoid; }
 .header { padding-bottom:6px; }
 .meta-grid { margin:6px 0; }
 .section { margin-top:7px; }
 .signatures { margin-top:14px; }
}
";
}
