using System.Net;
using System.Text;
using Inventory.Client.Extensions;
using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Pages;

/// <summary>ساخت قبض چاپی پذیرش تعمیرات و فاکتور فروش مستقل و خودبسنده.</summary>
public static class RepairInvoicePrint
{
    public static string Build(RepairOrderDto repair, Order invoice, string? companyName)
        => Build(repair, companyName, invoice);

    public static string Build(RepairOrderDto repair, string? companyName, Order? invoice = null)
    {
        var company = string.IsNullOrWhiteSpace(companyName) ? "فروغ آریا" : companyName.Trim();
        var devices = DevicesFor(repair);
        var phone = !string.IsNullOrWhiteSpace(repair.PartyMobile) ? repair.PartyMobile : repair.PartyPhone;
        var hasInvoice = invoice is not null;

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"fa\" dir=\"rtl\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>").Append(hasInvoice ? "فاکتور خدمات و تعمیرات " + Esc(invoice!.Number) : "قبض پذیرش تعمیرات " + Esc(repair.Number)).Append("</title>");
        sb.Append("<style>").Append(Css).Append("</style></head><body><main class=\"sheet\">");

        // Header
        sb.Append("<header class=\"header\"><div class=\"brand\"><div class=\"mark\">ت</div><div><div class=\"company\">")
            .Append(Esc(company)).Append("</div><div class=\"caption\">")
            .Append(hasInvoice ? "فاکتور خدمات و تعمیرات" : "قبض پذیرش و خدمات تعمیرات")
            .Append("</div></div></div>");

        if (hasInvoice)
        {
            sb.Append("<div class=\"invoice-stamp\"><span>فاکتور فروش</span><strong>").Append(Esc(invoice!.Number)).Append("</strong></div>");
        }
        else
        {
            sb.Append("<div class=\"invoice-stamp\"><span>قبض پذیرش</span><strong>").Append(Esc(repair.Number)).Append("</strong></div>");
        }
        sb.Append("</header>");

        // Meta
        sb.Append("<section class=\"meta\">");
        Meta(sb, "شماره پذیرش", repair.Number);
        if (hasInvoice)
        {
            Meta(sb, "شماره فاکتور", invoice!.Number);
            Meta(sb, "تاریخ فاکتور", invoice.Date.ToFa().FaDigits());
        }
        Meta(sb, "تاریخ پذیرش", repair.ReceivedAt.ToFa().FaDigits());
        Meta(sb, "تعمیرکار", string.IsNullOrWhiteSpace(repair.TechnicianName) ? "—" : repair.TechnicianName!);
        Meta(sb, "وضعیت", StatusTitle(repair.Status));
        sb.Append("</section>");

        // Party
        sb.Append("<section class=\"party\"><div class=\"party-icon\">م</div><div class=\"party-info\"><div class=\"eyebrow\">")
            .Append(hasInvoice ? "صورتحساب برای" : "پذیرش از / مشتری")
            .Append("</div><strong>")
            .Append(Esc(repair.PartyName ?? invoice?.PartyName ?? "مشتری"))
            .Append("</strong>");
        if (!string.IsNullOrWhiteSpace(phone))
            sb.Append("<span class=\"phone\" dir=\"ltr\">").Append(Esc(phone)).Append("</span>");
        sb.Append("</div><div class=\"party-note\">ارائهٔ این قبض هنگام پیگیری و تحویل دستگاه الزامی است.</div></section>");

        // Section 1: Devices
        sb.Append("<section class=\"section\"><div class=\"section-head\"><span class=\"step\">۱</span><div><h2>دستگاه‌های پذیرش‌شده</h2><small>")
            .Append(devices.Count.Num()).Append(" دستگاه</small></div></div><div class=\"devices\">");
        for (var i = 0; i < devices.Count; i++)
        {
            var d = devices[i];
            sb.Append("<article class=\"device\"><div class=\"device-title\"><span class=\"device-no\">")
                .Append((i + 1).Num()).Append("</span><strong>").Append(Esc(DeviceLabel(d))).Append("</strong></div>");
            if (d.QuotedPrice > 0)
                sb.Append("<div class=\"device-detail\"><span>برآورد اولیه</span><b>")
                    .Append(d.QuotedPrice.Money()).Append(" ریال</b></div>");
            if (!string.IsNullOrWhiteSpace(d.SerialNumber))
                sb.Append("<div class=\"device-detail\"><span>سریال</span><b dir=\"ltr\">").Append(Esc(d.SerialNumber)).Append("</b></div>");
            if (!string.IsNullOrWhiteSpace(d.ProblemDescription))
                sb.Append("<div class=\"device-detail\"><span>ایراد اعلامی</span><b>").Append(Esc(d.ProblemDescription)).Append("</b></div>");
            if (!string.IsNullOrWhiteSpace(d.Accessories))
                sb.Append("<div class=\"device-detail\"><span>لوازم همراه</span><b>").Append(Esc(d.Accessories)).Append("</b></div>");
            sb.Append("</article>");
        }
        sb.Append("</div></section>");

        // Section 2: Items
        sb.Append("<section class=\"section\"><div class=\"section-head\"><span class=\"step\">۲</span><div><h2>شرح خدمات و قطعات</h2><small>")
            .Append(hasInvoice ? "اقلام نهایی فاکتور" : "اقلام ثبت‌شده در پذیرش")
            .Append("</small></div></div>");

        sb.Append("<table><thead><tr><th class=\"num-col\">ردیف</th><th>شرح</th><th class=\"qty-col\">تعداد</th><th class=\"money-col\">فی (ریال)</th><th class=\"money-col\">مبلغ (ریال)</th></tr></thead><tbody>");

        if (hasInvoice)
        {
            var repairItems = repair.Items.Where(i => i.Price > 0).OrderBy(i => i.Id).ToList();
            var lines = invoice!.Lines.OrderBy(i => i.Id).ToList();
            var unmatchedRepairItems = repairItems.ToList();
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var repairItemIndex = unmatchedRepairItems.FindIndex(item => Matches(item, line));
                RepairItemDto? repairItem = repairItemIndex >= 0 ? unmatchedRepairItems[repairItemIndex] : null;
                if (repairItemIndex >= 0) unmatchedRepairItems.RemoveAt(repairItemIndex);
                var description = !string.IsNullOrWhiteSpace(repairItem?.Description)
                    ? repairItem!.Description
                    : (line.ProductName ?? "خدمت تعمیراتی");
                sb.Append("<tr><td class=\"num-cell\">").Append((i + 1).Num()).Append("</td><td><strong>")
                    .Append(Esc(description)).Append("</strong>");
                if (!string.IsNullOrWhiteSpace(line.ProductName) &&
                    !string.Equals(description, line.ProductName, StringComparison.Ordinal) &&
                    !string.Equals(line.ProductName, "اجرت تعمیرات", StringComparison.Ordinal))
                    sb.Append("<small class=\"subline\">").Append(Esc(line.ProductName)).Append("</small>");
                sb.Append("</td><td class=\"qty-cell\">").Append(line.Quantity.Qty())
                    .Append("</td><td class=\"money-cell\">").Append(line.Price.Money())
                    .Append("</td><td class=\"money-cell total-cell\">").Append(line.Total.Money()).Append("</td></tr>");
            }
            if (lines.Count == 0)
                sb.Append("<tr><td colspan=\"5\" class=\"empty\">برای این فاکتور ردیفی ثبت نشده است.</td></tr>");
        }
        else
        {
            var items = repair.Items.OrderBy(i => i.Id).ToList();
            for (var i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var desc = !string.IsNullOrWhiteSpace(it.Description) ? it.Description : (it.ProductName ?? "خدمت تعمیراتی");
                var total = it.Quantity * it.Price;
                sb.Append("<tr><td class=\"num-cell\">").Append((i + 1).Num()).Append("</td><td><strong>")
                    .Append(Esc(desc)).Append("</strong>");
                if (!string.IsNullOrWhiteSpace(it.ProductName) && !string.Equals(desc, it.ProductName, StringComparison.Ordinal))
                    sb.Append("<small class=\"subline\">").Append(Esc(it.ProductName)).Append("</small>");
                sb.Append("</td><td class=\"qty-cell\">").Append(it.Quantity.Qty())
                    .Append("</td><td class=\"money-cell\">").Append(it.Price.Money())
                    .Append("</td><td class=\"money-cell total-cell\">").Append(total.Money()).Append("</td></tr>");
            }
            if (items.Count == 0)
            {
                sb.Append("<tr><td colspan=\"5\" class=\"empty\">دستگاه در مرحلهٔ عیب‌یابی اولیه است؛ قطعات و خدمات پس از بررسی توسط تکنسین ثبت خواهند شد.</td></tr>");
            }
        }
        sb.Append("</tbody></table></section>");

        // Grand Total
        decimal totalAmount = hasInvoice ? invoice!.TotalAmount : (repair.TotalPrice > 0 ? repair.TotalPrice : repair.QuotedPrice);
        string totalLabel = hasInvoice ? "مبلغ قابل پرداخت" : (repair.TotalPrice > 0 ? "مجموع خدمات و قطعات" : "برآورد اولیه هزینه");
        string totalSub = hasInvoice ? "جمع نهایی فاکتور فروش" : (repair.TotalPrice > 0 ? "مبلغ جاری پذیرش" : "برآورد تخمینی اعلام‌شده");

        sb.Append("<section class=\"grand-total\"><div><span>").Append(totalLabel).Append("</span><small>")
            .Append(totalSub).Append("</small></div><strong>")
            .Append(totalAmount.Money()).Append(" <small>ریال</small></strong></section>");

        if (!string.IsNullOrWhiteSpace(repair.Note))
            sb.Append("<div class=\"delivered\">یادداشت پذیرش: <strong>").Append(Esc(repair.Note)).Append("</strong></div>");

        if (repair.DeliveredAt.HasValue)
            sb.Append("<div class=\"delivered\">تاریخ تحویل دستگاه: <strong>").Append(repair.DeliveredAt.Value.ToFa().FaDigits()).Append("</strong></div>");

        // Terms
        sb.Append("<div class=\"terms\">")
            .Append("<div><b>شرایط و مقررات پذیرش و تحویل:</b></div>")
            .Append("<div>۱. ارائه اصل این قبض هنگام تحویل گرفتن دستگاه الزامی است.</div>")
            .Append("<div>۲. مرکز هیچ‌گونه مسئولیتی در قبال حفظ داده‌ها، حساب‌های کاربری و اطلاعات شخصی روی دستگاه ندارد.</div>")
            .Append("<div>۳. حداکثر مهلت مراجعه جهت تحویل گرفتن دستگاه ۳۰ روز کاری پس از اعلام آماده بودن است.</div>")
            .Append("</div>");

        sb.Append("<section class=\"signatures\"><div><span>امضای مشتری / تحویل‌دهنده</span></div><div><span>مهر و امضای واحد پذیرش و تعمیرات</span></div></section>");
        sb.Append("<footer>از اعتماد شما سپاسگزاریم <span>•</span> ").Append(Esc(company)).Append(" <span>•</span> چاپ‌شده در ")
            .Append(DateTime.Now.ToFaDateTime().FaDigits()).Append("</footer>");
        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    private static string StatusTitle(RepairStatus status) => status switch
    {
        RepairStatus.Received => "پذیرش شده",
        RepairStatus.InProgress => "در حال تعمیر",
        RepairStatus.Ready => "آماده تحویل",
        RepairStatus.Delivered => "تحویل شده",
        RepairStatus.Cancelled => "مرجوع / لغو شده",
        _ => "نامشخص"
    };

    private static void Meta(StringBuilder sb, string label, string value)
        => sb.Append("<div class=\"meta-item\"><span>").Append(Esc(label)).Append("</span><strong>").Append(Esc(value)).Append("</strong></div>");

    private static bool Matches(RepairItemDto item, OrderLine line)
    {
        if (item.ProductId is > 0)
            return item.ProductId == line.ProductId && item.Quantity == line.Quantity && item.Price == line.Price;

        return (line.Quantity == item.Quantity && line.Price == item.Price) ||
               (line.Quantity == 1 && line.Price == item.Price * item.Quantity);
    }

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
body { margin:0; background:#fff; color:#0f172a; font-family:'Vazirmatn',Tahoma,'Segoe UI',sans-serif; font-size:11px; }
.sheet { max-width:134mm; margin:0 auto; }
.header { display:flex; justify-content:space-between; align-items:center; gap:12px; padding:0 0 13px; border-bottom:3px solid #2563eb; }
.brand { display:flex; align-items:center; gap:12px; }
.mark { width:49px; height:49px; display:grid; place-items:center; border-radius:15px; background:linear-gradient(135deg,#2563eb,#7c3aed); color:#fff; font-size:27px; font-weight:800; box-shadow:0 5px 14px #2563eb33; }
.company { color:#172554; font-size:18px; font-weight:800; }
.caption { margin-top:2px; color:#64748b; font-size:11px; }
.invoice-stamp { display:grid; justify-items:center; gap:2px; min-width:150px; padding:8px 15px; border:1px solid #bfdbfe; border-radius:12px; background:#eff6ff; color:#1d4ed8; }
.invoice-stamp span { font-size:9px; }
.invoice-stamp strong { font-size:15px; font-weight:800; }
.meta { display:grid; grid-template-columns:repeat(4,minmax(0,1fr)); gap:7px; margin:12px 0; }
.meta-item { min-width:0; display:grid; gap:4px; padding:8px 10px; border:1px solid #e2e8f0; border-radius:9px; background:#f8fafc; }
.meta-item span,.eyebrow { color:#64748b; font-size:9px; }
.meta-item strong { overflow-wrap:anywhere; color:#0f172a; font-size:10px; }
.party { display:flex; align-items:center; gap:10px; margin-bottom:14px; padding:10px 12px; border:1px solid #c7d2fe; border-radius:11px; background:#eef2ff; }
.party-icon { width:36px; height:36px; display:grid; place-items:center; border-radius:11px; background:#fff; color:#4f46e5; font-size:16px; font-weight:800; }
.party-info { display:grid; gap:2px; }
.party-info strong { color:#1e1b4b; font-size:13px; }
.phone { color:#475569; font-size:10px; }
.party-note { margin-inline-start:auto; color:#64748b; font-size:9px; }
.section { margin-top:13px; }
.section-head { display:flex; align-items:center; gap:8px; margin-bottom:7px; }
.section-head h2 { margin:0; color:#1e293b; font-size:12px; font-weight:800; }
.section-head small { display:block; margin-top:1px; color:#94a3b8; font-size:9px; }
.step { width:23px; height:23px; display:grid; place-items:center; border-radius:8px; background:#dbeafe; color:#1d4ed8; font-size:11px; font-weight:800; }
.devices { display:grid; grid-template-columns:repeat(auto-fit,minmax(75mm,1fr)); gap:7px; }
.device { padding:8px 10px; border:1px solid #e2e8f0; border-radius:10px; break-inside:avoid; }
.device-title { display:flex; align-items:center; gap:7px; margin-bottom:5px; color:#0f172a; font-size:11px; }
.device-no { width:20px; height:20px; display:grid; place-items:center; border-radius:7px; background:#f1f5f9; color:#475569; font-size:9px; }
.device-detail { display:flex; gap:8px; padding-top:3px; font-size:9px; }
.device-detail span { flex:0 0 auto; color:#64748b; }
.device-detail b { min-width:0; color:#334155; font-weight:500; overflow-wrap:anywhere; }
table { width:100%; border-collapse:collapse; }
thead th { padding:8px 8px; background:#1e40af; color:#fff; text-align:right; font-size:9.5px; font-weight:700; }
thead th:first-child { border-radius:0 8px 0 0; }
thead th:last-child { border-radius:8px 0 0 0; }
tbody td { padding:7px 8px; border-bottom:1px solid #e2e8f0; vertical-align:top; }
tbody tr:nth-child(even) td { background:#f8fafc; }
.num-col { width:38px; text-align:center; }
.qty-col { width:65px; text-align:center; }
.money-col { width:100px; text-align:left; }
.num-cell { text-align:center; color:#94a3b8; }
.qty-cell { text-align:center; }
.money-cell { text-align:left; white-space:nowrap; font-variant-numeric:tabular-nums; }
.total-cell { color:#0f172a; font-weight:700; }
.subline { display:block; margin-top:2px; color:#64748b; font-size:8.5px; }
.empty { padding:18px; color:#64748b; text-align:center; }
.grand-total { display:flex; justify-content:space-between; align-items:center; gap:16px; margin-top:11px; padding:12px 14px; border:1px solid #bfdbfe; border-radius:12px; background:linear-gradient(100deg,#eff6ff,#f5f3ff); }
.grand-total div { display:grid; gap:3px; }
.grand-total span { color:#1e3a8a; font-size:12px; font-weight:800; }
.grand-total div small { color:#64748b; font-size:9px; }
.grand-total strong { color:#1d4ed8; font-size:18px; font-weight:800; white-space:nowrap; }
.grand-total strong small { font-size:9px; }
.delivered { margin-top:8px; color:#475569; font-size:9.5px; }
.terms { margin-top:10px; padding:7px 10px; background:#f8fafc; border:1px solid #e2e8f0; border-radius:8px; font-size:8.5px; color:#64748b; line-height:1.6; }
.terms b { color:#334155; }
.signatures { display:flex; gap:35px; margin-top:24px; }
.signatures div { flex:1; height:36px; border-top:1px dashed #94a3b8; text-align:center; }
.signatures span { position:relative; top:5px; color:#64748b; font-size:9px; }
footer { margin-top:16px; padding-top:8px; border-top:1px solid #e2e8f0; color:#94a3b8; text-align:center; font-size:8px; }
footer span { padding:0 4px; color:#cbd5e1; }
@media print {
 body { -webkit-print-color-adjust:exact; print-color-adjust:exact; font-size:10px; }
 .sheet { width:134mm; max-width:100%; margin:0 auto; }
 .header { flex-direction:column; justify-content:center; gap:8px; padding-bottom:8px; text-align:center; }
 .brand { flex-direction:column; justify-content:center; gap:5px; text-align:center; }
 .invoice-stamp { min-width:0; }
 .meta { grid-template-columns:repeat(2,minmax(0,1fr)); gap:5px; margin:8px 0; }
 .meta-item { text-align:center; }
 .party { justify-content:center; margin-bottom:8px; padding:8px 10px; text-align:center; }
 .party-info { text-align:center; }
 .party-note { display:none; }
 .section { margin-top:9px; }
 .section-head { justify-content:center; margin-bottom:5px; text-align:center; }
 .device { padding:6px 8px; }
 .device-title,.device-detail { justify-content:center; text-align:center; }
 thead { display:table-header-group; }
 thead th,tbody td { text-align:center; }
 .money-col,.money-cell,.qty-cell,.num-cell { text-align:center; }
 tr,.device,.grand-total,.delivered,.terms,.signatures { break-inside:avoid; page-break-inside:avoid; }
 tbody td { padding:5px 6px; }
 .grand-total { flex-direction:column; justify-content:center; margin-top:8px; padding:9px 11px; text-align:center; }
 .delivered { text-align:center; }
 .signatures { justify-content:center; margin-top:20px; }
 footer { margin-top:10px; }
}
@media (max-width:700px) { .meta { grid-template-columns:repeat(2,minmax(0,1fr)); } .party-note { display:none; } .money-col { width:auto; } }
";
}
