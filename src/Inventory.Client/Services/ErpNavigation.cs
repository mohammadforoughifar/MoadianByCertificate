namespace Inventory.Client.Services;

/// <summary>Single ERP navigation catalogue (mirror of HrNavigation, gated by modules). API permissions remain authoritative.</summary>
public static class ErpNavigation
{
    public record Entry(string Href, string Title, string Icon, params string[] Modules)
    {
        public bool Allowed(Func<string, bool> hasModule) => Modules.Any(hasModule);
    }
    public record Section(string Id, string Title, string Icon, Entry[] Items);
    private static Entry E(string href, string title, string icon, params string[] modules) => new(href, title, icon, modules);
    public static readonly Section[] Sections =
    [
        new("inv-def", "تعاریف انبار", "bi-box-seam", [
            E("inv/products", "تعریف کالا", "bi-boxes", "Products"),
            E("inv/categories", "گروه‌بندی کالا", "bi-diagram-3", "Products"),
            E("inv/attributes", "ویژگی‌های کالا", "bi-sliders", "Products"),
            E("inv/warehouses", "مدیریت انبارها", "bi-building-gear", "Warehouses"),
            E("inv/doc-types", "نوع رسید و حواله", "bi-journals", "InvDocTypes"),
            E("inv/settings", "تنظیمات انبار و کدینگ", "bi-gear-wide-connected", "Products")]),
        new("inv-ops", "عملیات انبار", "bi-arrow-left-right", [
            E("inv/docs", "رسید و حواله", "bi-journal-text", "InvDocs"),
            E("inv/docs/new", "سند جدید انبار", "bi-file-earmark-plus", "InvDocs")]),
        new("inv-rep", "گزارش‌های انبار", "bi-clipboard-pulse", [
            E("inv/stock", "موجودی انبار", "bi-clipboard-check", "InvDocs"),
            E("inv/kardex", "کاردکس کالا", "bi-clipboard-data", "InvDocs")]),
        new("acc-def", "تعاریف حسابداری", "bi-diagram-2-fill", [
            E("acc/accounts", "کدینگ حساب‌ها", "bi-diagram-3-fill", "AccAccounts"),
            E("acc/fiscal-years", "سال‌های مالی", "bi-calendar-range", "AccAccounts"),
            E("acc/inv-rules", "سند خودکار انبار", "bi-gear-wide-connected", "AccAccounts"),
            E("acc/dimensions", "ابعاد تحلیلی", "bi-grid-3x3-gap-fill", "AccAccounts"),
            E("acc/fixed-assets", "دارایی ثابت", "bi-boxes", "AccAccounts"),
            E("acc/budgets", "بودجه و کنترل", "bi-pie-chart-fill", "AccAccounts")]),
        new("acc-ops", "عملیات حسابداری", "bi-journal-plus", [
            E("acc/vouchers", "اسناد حسابداری", "bi-journal-text", "AccVouchers"),
            E("acc/vouchers/new", "صدور سند جدید", "bi-file-earmark-plus", "AccVouchers")]),
        new("acc-rep", "گزارش‌های حسابداری", "bi-graph-up-arrow", [
            E("acc/dashboard", "داشبورد مالی", "bi-speedometer2", "AccVouchers"),
            E("acc/reports/journal", "دفتر روزنامه", "bi-journals", "AccVouchers"),
            E("acc/reports/ledger", "دفتر حساب", "bi-journal-bookmark", "AccVouchers"),
            E("acc/reports/trial-balance", "تراز آزمایشی", "bi-table", "AccVouchers")]),
        new("fac", "فاکتور و فروش", "bi-bag-check", [
            E("fac/invoices", "فهرست فاکتورها", "bi-receipt", "FacInvoices"),
            E("fac/invoices/new/Sale", "فاکتور فروش", "bi-cart-check", "FacInvoices"),
            E("fac/invoices/new/Purchase", "فاکتور خرید", "bi-bag-plus", "FacInvoices"),
            E("fac/invoices/new/SaleReturn", "برگشت از فروش", "bi-arrow-return-left", "FacInvoices"),
            E("fac/invoices/new/PurchaseReturn", "برگشت از خرید", "bi-arrow-return-right", "FacInvoices"),
            E("fac/dashboard", "داشبورد فروش و خرید", "bi-speedometer2", "FacInvoices"),
            E("fac/reports/summary", "گزارش فروش و خرید", "bi-bar-chart-line", "FacInvoices"),
            E("fac/rules", "تنظیمات فاکتور", "bi-gear-wide-connected", "FacInvoices")]),
        new("trs", "خزانه‌داری", "bi-bank", [
            E("trs/cheques", "دفتر چک", "bi-card-checklist", "TrsCheques"),
            E("trs/accounts", "صندوق و بانک", "bi-wallet2", "TrsAccounts"),
            E("trs/reports/flow", "گردش صندوق و بانک", "bi-list-columns", "TrsAccounts"),
            E("trs/dashboard", "داشبورد خزانه", "bi-speedometer2", "TrsAccounts"),
            E("trs/rules", "تنظیمات خزانه", "bi-gear-wide-connected", "TrsAccounts")]),
        new("stk", "انبارگردانی و بارکد", "bi-upc-scan", [
            E("stk/sessions", "دوره‌های انبارگردانی", "bi-clipboard-data", "StkSessions"),
            E("stk/sessions/new", "دوره انبارگردانی جدید", "bi-file-earmark-plus", "StkSessions"),
            E("stk/reports/diff", "گزارش مغایرت", "bi-file-earmark-bar-graph", "StkSessions"),
            E("stk/barcodes", "بارکد کالا", "bi-upc", "StkBarcodes"),
            E("stk/labels", "چاپ برچسب", "bi-tags", "StkBarcodes")]),
    ];
    public static Section[] Visible(Func<string, bool> hasModule) => Sections
        .Select(s => s with { Items = s.Items.Where(i => i.Allowed(hasModule)).ToArray() }).Where(s => s.Items.Length > 0).ToArray();
    public static bool CanEnter(Func<string, bool> hasModule) => Visible(hasModule).Length > 0;
    public static string Path(string path) => path.Split('?', '#')[0].Trim('/').ToLowerInvariant();
    private static bool At(string path, string root) => path == root || path.StartsWith(root + "/", StringComparison.Ordinal);
    public static bool IsWorkspace(string path)
    {
        path = Path(path);
        // دریافت/پرداخت و تسویهٔ فاکتورها و تنظیمات مودیان از منوی اصلی «عملیات» باز می‌شوند، نه فضای کاری ERP.
        if (At(path, "trs/vouchers")) return false;
        return new[] { "inv", "acc", "fac", "trs", "stk" }.Any(p => At(path, p));
    }
    public static Entry? Find(string path)
    {
        path = Path(path);
        return Sections.SelectMany(s => s.Items).Where(i => At(path, i.Href.ToLowerInvariant()))
            .OrderByDescending(i => i.Href.Length).FirstOrDefault();
    }
    public static string? SectionId(string path)
    {
        path = Path(path);
        var entry = Find(path);
        return entry is null ? null : Sections.FirstOrDefault(s => s.Items.Contains(entry))?.Id;
    }
}
