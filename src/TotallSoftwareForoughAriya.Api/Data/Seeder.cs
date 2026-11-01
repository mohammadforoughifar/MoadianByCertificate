using Inventory.Shared;

namespace Inventory.Api.Data;

/// <summary>ایجاد داده نمونه واقع‌گرایانه برای اولین اجرای برنامه.</summary>
public static class Seeder
{
    private record SeedLine(TransactionType Type, DateTime Date, int WhId, int? PartyId, int ProductId, decimal Qty, decimal Price, string Desc);

    /// <summary>واحدهای شمارش پیش‌فرض سیستم.</summary>
    public static readonly string[] DefaultUnits =
    {
        "عدد", "کیلوگرم", "گرم", "لیتر", "متر", "سانتی‌متر", "مترمربع",
        "بسته", "کارتن", "جفت", "حلقه", "دستگاه", "شاخه", "رول", "گالن", "تن"
    };

    public static void Seed(AppDbContext db)
    {
        // ---------------- انبارها ----------------
        var wh1 = new Warehouse { Name = "انبار مرکزی", Address = "تهران، خیابان آزادی، پلاک ۱۲", Phone = "۰۲۱-۶۶۰۰۱۲۳۴" };
        var wh2 = new Warehouse { Name = "انبار شعب", Address = "کرج، بلوار طالقانی، جنب بازار", Phone = "۰۲۶-۳۲۲۰۱۱۲۲" };
        var wh3 = new Warehouse { Name = "انبار قطعات یدکی", Address = "قم، جاده قدیم تهران", Phone = "۰۲۵-۳۷۷۷۸۸۹۹" };
        db.Warehouses.AddRange(wh1, wh2, wh3);

        // ---------------- طرف حساب‌ها ----------------
        var suppliers = new List<Party>
        {
            new() { Type = PartyType.Supplier, Name = "پخش روغن پارس", Phone = "۰۲۱-۸۸۰۰۱۱۲۲", Mobile = "۰۹۱۲۱۱۱۲۲۳۳", Address = "تهران، میدان فاطمی" },
            new() { Type = PartyType.Supplier, Name = "بازرگانی قطعات امین", Phone = "۰۲۱-۵۵۶۶۷۷۸۸", Mobile = "۰۹۱۲۳۳۳۴۴۵۵", Address = "تهران، بازار بزرگ" },
            new() { Type = PartyType.Supplier, Name = "تأمین لاستیک ایران", Phone = "۰۲۶-۳۴۴۴۵۵۶۶", Mobile = "۰۹۳۵۱۱۱۲۲۳۳", Address = "کرج، شهرک صنعتی" }
        };
        var customers = new List<Party>
        {
            new() { Type = PartyType.Customer, Name = "تعمیرگاه مرکزی", Phone = "۰۲۱-۲۲۳۳۴۴۵۵", Mobile = "۰۹۱۲۵۵۵۶۶۷۷", Address = "تهران، خیابان انقلاب" },
            new() { Type = PartyType.Customer, Name = "فروشگاه قطعات ولیعصر", Phone = "۰۲۱-۸۸۷۷۶۶۵۵", Mobile = "۰۹۱۲۷۷۷۸۸۹۹", Address = "تهران، ولیعصر" },
            new() { Type = PartyType.Customer, Name = "شرکت حمل‌ونقل آریا", Phone = "۰۲۱-۴۴۵۵۶۶۷۷", Mobile = "۰۹۱۲۹۹۹۰۰۱۱", Address = "تهران، جاده مخصوص کرج" },
            new() { Type = PartyType.Customer, Name = "گاراژ آزادی", Phone = "۰۲۶-۳۳۴۴۵۵۶۶", Mobile = "۰۹۱۹۳۳۳۴۴۵۵", Address = "کرج، میدان استاندارد" }
        };
        db.Parties.AddRange(suppliers);
        db.Parties.AddRange(customers);

        // ---------------- کالاها ----------------
        // (نام، گروه، واحد، قیمت فروش، قیمت خرید، نقطه سفارش، حداکثر موجودی)
        var defs = new (string Name, string Cat, string Unit, decimal SP, decimal PP, decimal RP, decimal MX)[]
        {
            ("روغن موتور 20W50", "روغن", "لیتر", 850_000, 700_000, 20, 100),
            ("فیلتر روغن", "فیلتر", "عدد", 250_000, 180_000, 30, 150),
            ("لاستیک 185/65R15", "لاستیک", "حلقه", 6_500_000, 5_400_000, 8, 40),
            ("باتری 66 آمپر", "باتری", "عدد", 9_800_000, 8_200_000, 6, 30),
            ("ضدیخ", "مایعات", "لیتر", 320_000, 240_000, 40, 200),
            ("لنت ترمز جلو", "ترمز", "جفت", 1_150_000, 850_000, 15, 80),
            ("فیلتر هوا", "فیلتر", "عدد", 480_000, 330_000, 25, 120),
            ("روغن هیدرولیک", "روغن", "لیتر", 290_000, 210_000, 35, 160),
            ("تسمه دینام", "تسمه", "عدد", 780_000, 560_000, 10, 60),
            ("شمع موتور", "برقی", "عدد", 190_000, 130_000, 60, 300),
            ("واشر سرسیلندر", "موتور", "عدد", 940_000, 720_000, 4, 20),
            ("فیلتر کابین", "فیلتر", "عدد", 350_000, 240_000, 20, 100),
            ("دیسک ترمز", "ترمز", "عدد", 2_300_000, 1_800_000, 6, 30),
            ("مایع شیشه‌شور", "مایعات", "لیتر", 85_000, 55_000, 80, 400)
        };

        // ---------------- گروه‌های کالا ----------------
        foreach (var catName in defs.Select(d => d.Cat).Distinct())
            db.ProductCategories.Add(new ProductCategory { Name = catName, CreatedAt = DateTime.Now });

        // ---------------- واحدهای شمارش پیش‌فرض ----------------
        foreach (var unitName in DefaultUnits)
            db.MeasureUnits.Add(new MeasureUnit { Name = unitName, CreatedAt = DateTime.Now });

        var products = new List<Product>();
        int pi = 1;
        foreach (var d in defs)
        {
            products.Add(new Product
            {
                Code = $"P-{pi:0000}",
                Name = d.Name,
                Category = d.Cat,
                Unit = d.Unit,
                SalePrice = d.SP,
                PurchasePrice = d.PP,
                ReorderPoint = d.RP,
                MaxStock = d.MX,
                Description = null,
                CreatedAt = DateTime.Now
            });
            pi++;
        }
        db.Products.AddRange(products);
        db.SaveChanges();

        // همه کالاهای نمونه به انبار مرکزی اختصاص داده می‌شوند (مشتری‌ها بدون معرف هستند)
        foreach (var p in products) p.WarehouseId = wh1.Id;
        db.SaveChanges();

        // ---------------- ساخت اسناد به ترتیب تاریخ ----------------
        var lines = new List<SeedLine>();
        int pr = 1, sl = 1, adj = 1;

        var now = DateTime.Now;
        var (jy, jm, jd) = PersianDate.FromGregorian(now);

        for (int i = 0; i < products.Count; i++)
        {
            var p = products[i];
            var wh = wh1.Id; // همه کالاها و اسناد در انبار مرکزی
            var supplier = suppliers[i % suppliers.Count];
            var customer = customers[i % customers.Count];

            // موجودی اول دوره (نقطه شروع کاردکس)
            var openQty = (i == 2 || i == 9 || i == 12) ? p.ReorderPoint / 2 : p.ReorderPoint * 3 + 10;
            lines.Add(new SeedLine(TransactionType.Adjustment, D(1403, 1, 5).AddDays(i), wh, null, p.Id, openQty, p.PurchasePrice, $"موجودی اول دوره - {p.Name} (شماره {adj})"));

            // خرید اول
            var buy1 = Math.Round(p.ReorderPoint * 1.5m, 0);
            lines.Add(new SeedLine(TransactionType.Purchase, D(1403, 2, 12).AddDays(i), wh, supplier.Id, p.Id, buy1, p.PurchasePrice, "خرید از تأمین‌کننده"));
            pr++;

            // فروش اول
            var sell1 = Math.Round(p.ReorderPoint * 0.8m, 0);
            lines.Add(new SeedLine(TransactionType.Sale, D(1403, 3, 14).AddDays(i), wh, customer.Id, p.Id, sell1, p.SalePrice, "فروش به مشتری"));
            sl++;

            // خرید دوم
            var buy2 = Math.Round(p.ReorderPoint * 1.1m, 0);
            lines.Add(new SeedLine(TransactionType.Purchase, D(1403, 4, 9).AddDays(i), wh, supplier.Id, p.Id, buy2, p.PurchasePrice, "خرید تکمیلی"));
            pr++;

            // فروش دوم
            var sell2 = Math.Round(p.ReorderPoint * 1.0m, 0);
            lines.Add(new SeedLine(TransactionType.Sale, D(1403, 5, 22).AddDays(i), wh, customer.Id, p.Id, sell2, p.SalePrice, "فروش"));
            sl++;

            // فعالیت در ماه جاری (برای داشبورد)
            var cmDay = Math.Min(jd, 15);
            lines.Add(new SeedLine(TransactionType.Sale, D(jy, jm, cmDay), wh, customer.Id, p.Id, Math.Max(1, Math.Round(p.ReorderPoint * 0.3m, 0)), p.SalePrice, "فروش ماه جاری"));
            sl++;
        }

        // چند سند امروز برای داشبورد «امروز»
        lines.Add(new SeedLine(TransactionType.Sale, now, wh1.Id, customers[1].Id, products[0].Id, 4, products[0].SalePrice, "فروش امروز"));
        lines.Add(new SeedLine(TransactionType.Sale, now.AddHours(-2), wh1.Id, customers[0].Id, products[4].Id, 10, products[4].SalePrice, "فروش امروز"));
        lines.Add(new SeedLine(TransactionType.Purchase, now.AddHours(-5), wh1.Id, suppliers[0].Id, products[0].Id, 30, products[0].PurchasePrice, "خرید امروز"));

        // فروش تکمیلی برای نمونه گزارش نقطه سفارش
        lines.Add(new SeedLine(TransactionType.Sale, now.AddDays(-1), wh1.Id, customers[2].Id, products[2].Id, 3, products[2].SalePrice, "فروش"));

        // ---------------- درج اسناد و بازسازی موجودی ----------------
        lines.Sort((a, b) => a.Date.CompareTo(b.Date));

        int nPr = 1, nSl = 1, nAdj = 1;
        var stockMap = new Dictionary<(int, int), (decimal Qty, decimal Cost)>();

        foreach (var l in lines)
        {
            string number;
            switch (l.Type)
            {
                case TransactionType.Purchase: number = $"PR-{nPr++:0000}"; break;
                case TransactionType.Sale: number = $"SL-{nSl++:0000}"; break;
                default: number = $"ADJ-{nAdj++:0000}"; break;
            }

            var txn = new Transaction
            {
                Number = number,
                Type = l.Type,
                Date = l.Date,
                Description = l.Desc,
                WarehouseId = l.WhId,
                PartyId = l.PartyId,
                Amount = l.Type == TransactionType.Sale ? l.Qty * l.Price : l.Qty * l.Price,
                CreatedAt = DateTime.Now
            };
            txn.Lines.Add(new TransactionLine { ProductId = l.ProductId, Quantity = l.Qty, Price = l.Price });
            txn.Amount = txn.Lines.Sum(x => x.Quantity * x.Price);
            db.Transactions.Add(txn);

            // بازسازی موجودی
            var key = (l.WhId, l.ProductId);
            if (!stockMap.TryGetValue(key, out var st)) st = (0, 0);
            switch (l.Type)
            {
                case TransactionType.Purchase:
                    var nq = st.Qty + l.Qty;
                    var nc = nq != 0 ? (st.Qty * st.Cost + l.Qty * l.Price) / nq : st.Cost;
                    st = (nq, nc);
                    break;
                case TransactionType.Sale:
                    st = (st.Qty - l.Qty, st.Cost);
                    break;
                default:
                    st = (l.Qty, st.Cost == 0 && l.Qty > 0 ? l.Price : st.Cost);
                    break;
            }
            stockMap[key] = st;
        }

        foreach (var kv in stockMap)
        {
            if (kv.Value.Qty == 0) continue;
            db.Stocks.Add(new Stock
            {
                WarehouseId = kv.Key.Item1,
                ProductId = kv.Key.Item2,
                Quantity = kv.Value.Qty,
                AvgCost = kv.Value.Cost
            });
        }

        db.SaveChanges();
    }

    private static DateTime D(int y, int m, int d) => PersianDate.ToGregorian(y, m, d);
}
