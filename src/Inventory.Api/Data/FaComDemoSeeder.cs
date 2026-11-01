namespace Inventory.Api.Data;

/// <summary>
/// داده نمونه ماژول ارتباطات (FaCom) شرکت فروغ آریا: کاربر دمو برای پرسنل،
/// نظرسنجی + رأی، اطلاعیه، تیکت نمونه.
/// فقط وقتی اجرا می‌شود که Database:SeedDemoData=true و جدول نظرسنجی خالی باشد.
/// ورود دمو: کد پرسنلی (مثلاً 1001) / رمز 123456
/// </summary>
public static class FaComDemoSeeder
{
    public static void Seed(AppDbContext db)
    {
        var today = DateTime.Today;
        var rnd = new Random(7);

        var emps = db.HrEmployees.OrderBy(e => e.Code).Take(10).ToList();
        if (emps.Count == 0) return;

        // ---------- کاربران دمو ----------
        var users = new List<User>();
        foreach (var e in emps)
        {
            var u = db.Users.FirstOrDefault(x => x.Username == e.Code);
            if (u == null)
            {
                u = new User
                {
                    Username = e.Code,
                    PasswordHash = Services.AuthService.HashPassword("123456"),
                    Role = "Operator",
                    FirstName = e.FirstName,
                    LastName = e.LastName,
                    Mobile = e.Mobile,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                };
                db.Users.Add(u);
                db.SaveChanges();
            }
            if (e.SystemUserId != u.Id)
            {
                e.SystemUserId = u.Id;
                db.SaveChanges();
            }
            users.Add(u);
        }
        var adminId = db.Users.Where(x => x.Username == "admin").Select(x => x.Id).FirstOrDefault();

        var salUnit = db.HrOrgUnits.FirstOrDefault(u => u.Code == "SAL-01");
        var itUnit = db.HrOrgUnits.FirstOrDefault(u => u.Code == "IT-01");

        // ---------- نظرسنجی‌ها ----------
        var polls = new[]
        {
            new { Title = "بهترین زمان برگزاری جشن پایان سال",
                Desc = "همکاران گرامی، لطفاً زمان پیشنهادی خود برای جشن پایان سال شرکت را انتخاب کنید.",
                Aud = FaComAudience.All, UnitId = (int?)null, Close = (DateTime?)today.AddDays(10),
                Opts = new[] { "پنجشنبه عصر", "جمعه صبح", "چهارشنبه عصر", "فرقی نمی‌کند" },
                W = new[] { 45, 20, 25, 10 } },
            new { Title = "میزان رضایت از کیفیت رستوران شرکت",
                Desc = "نظر شما برای بهبود کیفیت غذای رستوران بسیار مهم است.",
                Aud = FaComAudience.All, UnitId = (int?)null, Close = (DateTime?)null,
                Opts = new[] { "عالی", "خوب", "متوسط", "ضعیف" },
                W = new[] { 15, 40, 30, 15 } },
            new { Title = "انتخاب رنگ لباس فرم جدید پرسنل",
                Desc = "واحد منابع انسانی در نظر دارد لباس فرم جدید تهیه کند؛ رنگ موردنظر خود را انتخاب کنید.",
                Aud = FaComAudience.All, UnitId = (int?)null, Close = (DateTime?)today.AddDays(5),
                Opts = new[] { "سرمه‌ای", "طوسی", "مشکی" },
                W = new[] { 50, 30, 20 } },
            new { Title = "اولویت‌های بهبود واحد فروش",
                Desc = "همکاران واحد فروش، مهم‌ترین اولویت بهبود را مشخص کنید.",
                Aud = FaComAudience.Unit, UnitId = (int?)salUnit?.Id, Close = (DateTime?)today.AddDays(-2),
                Opts = new[] { "آموزش مذاکره", "ابزار CRM بهتر", "تبلیغات بیشتر", "اصلاح پورسانت" },
                W = new[] { 35, 30, 20, 15 } },
        };

        foreach (var p in polls)
        {
            if (p.Aud == FaComAudience.Unit && p.UnitId is not > 0) continue;
            var poll = new FaComPoll
            {
                Title = p.Title, Description = p.Desc, Audience = p.Aud, OrgUnitId = p.UnitId,
                IsActive = true, CloseAt = p.Close, CreatedByName = "مدیر منابع انسانی",
                CreatedAt = today.AddDays(-7)
            };
            db.FaComPolls.Add(poll);
            db.SaveChanges();
            for (var i = 0; i < p.Opts.Length; i++)
                db.FaComPollOptions.Add(new FaComPollOption { PollId = poll.Id, Text = p.Opts[i], SortOrder = i + 1 });
            db.SaveChanges();
            var optIds = db.FaComPollOptions.Where(o => o.PollId == poll.Id).OrderBy(o => o.SortOrder).Select(o => o.Id).ToList();

            // رأی‌ها: نظرسنجی واحدی فقط از پرسنل همان واحد
            var voters = p.Aud == FaComAudience.Unit
                ? emps.Where(e => e.OrgUnitId == p.UnitId).ToList()
                : emps;
            foreach (var e in voters)
            {
                var u = users.First(x => e.SystemUserId != null && x.Id == e.SystemUserId.Value);
                var pick = WeightedPick(rnd, p.W);
                db.FaComVotes.Add(new FaComVote
                {
                    PollId = poll.Id, OptionId = optIds[pick], UserId = u.Id,
                    VotedAt = today.AddDays(-rnd.Next(0, 6)).AddHours(rnd.Next(8, 18))
                });
            }
            db.SaveChanges();
        }

        // ---------- اطلاعیه‌ها ----------
        if (!db.FaComAnnouncements.Any())
        {
            db.FaComAnnouncements.AddRange(
                new FaComAnnouncement
                {
                    Title = "جشن پایان سال شرکت فروغ آریا",
                    Body = "همکاران گرامی، جشن پایان سال شرکت به‌زودی برگزار می‌شود. زمان نهایی پس از جمع‌بندی نظرسنجی اعلام خواهد شد. حضور همه عزیزان مایه افتخار است.",
                    Audience = FaComAudience.All, IsActive = true,
                    PublishFrom = today.AddDays(-3), CreatedByName = "مدیر منابع انسانی", CreatedAt = today.AddDays(-3)
                },
                new FaComAnnouncement
                {
                    Title = "به‌روزرسانی سیستم حضور و غیاب",
                    Body = "همکاران واحد فناوری اطلاعات، لطفاً توجه داشته باشید سیستم حضور و غیاب از روز شنبه به نسخه جدید ارتقا می‌یابد و ثبت تردد از طریق وب‌کلاک انجام می‌شود.",
                    Audience = FaComAudience.Unit, OrgUnitId = itUnit?.Id, IsActive = true,
                    PublishFrom = today.AddDays(-1), CreatedByName = "مدیر منابع انسانی", CreatedAt = today.AddDays(-1)
                },
                new FaComAnnouncement
                {
                    Title = "تقویم تعطیلات رسمی سال آینده",
                    Body = "تقویم تعطیلات رسمی و مرخصی‌های تشویقی سال آینده نهایی شد و از ابتدای هفته آینده در سامانه قابل مشاهده است.",
                    Audience = FaComAudience.All, IsActive = true,
                    PublishFrom = today.AddDays(2), CreatedByName = "مدیر منابع انسانی", CreatedAt = today
                });
            db.SaveChanges();
        }

        // ---------- تیکت‌های نمونه ----------
        if (!db.FaComTickets.Any() && emps.Count >= 5)
        {
            var t1 = new FaComTicket
            {
                EmployeeId = emps[2].Id, Subject = "درخواست گواهی اشتغال به کار",
                Body = "با سلام، برای ارائه به بانک نیاز به گواهی اشتغال به کار دارم. لطفاً راهنمایی بفرمایید.",
                Category = FaComTicketCategory.Other, Priority = 1, Status = FaComTicketStatus.New,
                CreatedAt = today.AddDays(-1)
            };
            db.FaComTickets.Add(t1);
            db.SaveChanges();

            var t2 = new FaComTicket
            {
                EmployeeId = emps[4].Id, Subject = "مغایرت در فیش حقوقی ماه گذشته",
                Body = "با سلام، مبلغ اضافه‌کاری این ماه در فیش من لحاظ نشده است. لطفاً بررسی شود.",
                Category = FaComTicketCategory.Payroll, Priority = 2, Status = FaComTicketStatus.Answered,
                CreatedAt = today.AddDays(-4)
            };
            db.FaComTickets.Add(t2);
            db.SaveChanges();
            db.FaComReplies.AddRange(
                new FaComReply
                {
                    TicketId = t2.Id, UserId = users.First(x => emps[4].SystemUserId != null && x.Id == emps[4].SystemUserId.Value).Id,
                    UserName = emps[4].FirstName + " " + emps[4].LastName,
                    Body = "اضافه‌کاری روزهای پنجشنبه در فیش لحاظ نشده است.", IsHrReply = false,
                    CreatedAt = today.AddDays(-4)
                },
                new FaComReply
                {
                    TicketId = t2.Id, UserId = adminId > 0 ? adminId : users[0].Id,
                    UserName = "کارشناس منابع انسانی",
                    Body = "با سلام، مغایرت بررسی و در فیش اصلاحی ماه جاری لحاظ شد. سپاس از اطلاع‌رسانی شما.",
                    IsHrReply = true, CreatedAt = today.AddDays(-3)
                });
            db.SaveChanges();
        }

        Console.WriteLine("[DB] کاربران دمو ارتباطات: کد پرسنلی (1001 به بعد) / رمز 123456");
    }

    private static int WeightedPick(Random rnd, int[] weights)
    {
        var total = weights.Sum();
        var r = rnd.Next(total);
        var acc = 0;
        for (var i = 0; i < weights.Length; i++)
        {
            acc += weights[i];
            if (r < acc) return i;
        }
        return 0;
    }
}
