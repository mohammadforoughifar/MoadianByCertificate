using Inventory.Shared.Dtos;
using System.Net.Http.Json;
using System.Text.Json;
using Inventory.Client.Extensions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Inventory.Client.Pages;

/// <summary>منطق صفحهٔ «مرکز مدیریت ماشین‌های اداری» (پایش SNMP، رویدادها، کشف شبکه، تعمیرات، هزینه‌ها).</summary>
public partial class OfficeMachines : IDisposable
{
    [Inject] private NavigationManager Nav { get; set; } = default!;

    // ================================================================== وضعیت صفحه
    private List<MachineItem> items = new();
    private DashboardInfo dash = new();
    private bool isLoading = true;
    private string searchText = "";
    private string filter = "all";          // all | online | problem | offline | lowsupply | repair | retired | unmonitored
    private string view = "cards";          // cards | table
    private string pageTab = "devices";     // devices | alerts
    private List<EventItem> openEvents = new();
    private Timer? _refreshTimer;

    private static IEnumerable<MachineItem> ApplyFilter(string f, IEnumerable<MachineItem> q) => f switch
    {
        "online" => q.Where(m => m.OnlineState == "Online" && m.Status != "Retired"),
        "problem" => q.Where(m => m.OnlineState is "Warning" or "Error" && m.Status != "Retired"),
        "offline" => q.Where(m => m.OnlineState == "Offline" && m.Status != "Retired"),
        "lowsupply" => q.Where(m => m.LowestSupplyPercent != null && m.LowestSupplyPercent <= m.SupplyLowThreshold && m.Status != "Retired"),
        "repair" => q.Where(m => m.Status == "InRepair"),
        "retired" => q.Where(m => m.Status == "Retired"),
        "unmonitored" => q.Where(m => m.OnlineState == "NotMonitored" && m.Status != "Retired"),
        _ => q.Where(m => m.Status != "Retired"),
    };

    private IEnumerable<MachineItem> Filtered
    {
        get
        {
            var q = ApplyFilter(filter, items);
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var t = searchText.Trim();
                q = q.Where(m => Has(m.Model, t) || Has(m.SerialNumber, t) || Has(m.Location, t) || Has(m.IpAddress, t)
                    || Has(m.Department, t) || Has(m.Brand, t) || Has(m.AssetTag, t) || Has(m.ResponsibleName, t) || Has(m.Hostname, t)
                    || Has(m.AssignedUserName, t) || Has(m.CompanyName, t));
            }
            // مشکل‌دارها اول
            return q.OrderBy(m => StateRank(m)).ThenBy(m => m.Model);
        }
    }

    private static bool Has(string? s, string t) => (s ?? "").Contains(t, StringComparison.OrdinalIgnoreCase);
    private static string EventTab(EventItem e) => e.Code.StartsWith("LowSupply") || e.Code.Contains("Paper") || e.Code.Contains("Toner") || e.Code.Contains("Drum") ? "supplies" : "overview";
    private int CountFilter(string f) => ApplyFilter(f, items).Count();

    private static int StateRank(MachineItem m) => m.Status == "Retired" ? 9 : m.OnlineState switch
    {
        "Error" => 0, "Offline" => 1, "Warning" => 2, "Unknown" => 3, "Online" => 4, _ => 5
    };

    protected override async Task OnInitializedAsync()
    {
        await LoadAll();
        var uri = Nav.ToAbsoluteUri(Nav.Uri);
        var qs = System.Web.HttpUtility.ParseQueryString(uri.Query);
        if (int.TryParse(qs["id"], out var id) && id > 0) await OpenDetail(id);
        // به‌روزرسانی خودکار وضعیت هر ۶۰ ثانیه (فقط وقتی مودالی باز نیست)
        _refreshTimer = new Timer(async _ =>
        {
            if (ShowForm || detail != null || ShowDiscover) return;
            await InvokeAsync(async () => { await LoadAll(silent: true); StateHasChanged(); });
        }, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
    }

    public void Dispose() => _refreshTimer?.Dispose();

    private async Task LoadAll(bool silent = false)
    {
        if (!silent) isLoading = true;
        try
        {
            var t1 = Http.GetFromJsonAsync<List<MachineItem>>("/api/OfficeMachines");
            var t2 = Http.GetFromJsonAsync<DashboardInfo>("/api/OfficeMachines/dashboard");
            var t3 = Http.GetFromJsonAsync<List<EventItem>>("/api/OfficeMachines/events?openOnly=true");
            await Task.WhenAll(t1, t2, t3);
            items = t1.Result ?? new();
            dash = t2.Result ?? new();
            openEvents = t3.Result ?? new();
        }
        catch (Exception ex)
        {
            if (!silent) Toast.Error("دریافت فهرست ماشین‌ها ناموفق بود: " + ex.Message);
        }
        finally { isLoading = false; }
    }

    private async Task PollAll()
    {
        var r = await Http.PostAsync("/api/OfficeMachines/poll-all", null);
        if (!r.IsSuccessStatusCode) { Toast.Error(await ErrorOf(r)); return; }
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        var n = body.TryGetProperty("queued", out var q) ? q.GetInt32() : 0;
        Toast.Info(n == 0 ? "هیچ دستگاهی با پایش SNMP فعال وجود ندارد." : $"پایش {n} دستگاه آغاز شد؛ نتایج تا چند ثانیهٔ دیگر نمایش داده می‌شود.");
        if (n > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(20, 3 + n)));
            await LoadAll(silent: true);
        }
    }

    // ================================================================== جزئیات دستگاه
    private DetailInfo? detail;
    private string detailTab = "overview";
    private bool polling;

    private async Task OpenDetail(int id, string tab = "overview")
    {
        detailTab = tab;
        await LoadDetail(id);
        if (detail == null) return;
        repairList = new(); costList = new();
        ResetRepairForm(); ResetCostForm();
        if (tab == "repairs") await LoadRepairs();
        if (tab == "costs") await LoadCosts();
    }

    private async Task LoadDetail(int id)
    {
        try { detail = await Http.GetFromJsonAsync<DetailInfo>($"/api/OfficeMachines/{id}"); }
        catch (Exception ex) { Toast.Error("دریافت جزئیات دستگاه ناموفق بود: " + ex.Message); detail = null; }
    }

    private void CloseDetail() => detail = null;

    private async Task SwitchDetailTab(string tab)
    {
        detailTab = tab;
        if (tab == "repairs" && repairList.Count == 0) await LoadRepairs();
        if (tab == "costs" && costList.Count == 0) await LoadCosts();
    }

    private async Task PollNow(int id)
    {
        polling = true;
        try
        {
            var r = await Http.PostAsync($"/api/OfficeMachines/{id}/poll", null);
            if (!r.IsSuccessStatusCode) { Toast.Error(await ErrorOf(r)); return; }
            var res = await r.Content.ReadFromJsonAsync<PollResult>();
            if (res?.Success == true && res.Source == "Agent") Toast.Success($"وضعیت از آخرین گزارش ایجنت ({Ago(res.LastReportAt)}) ارزیابی شد — {StateTitle(res.State)}");
            else if (res?.Success == true) Toast.Success($"پایش انجام شد — وضعیت: {StateTitle(res.State)}");
            else Toast.Error(res?.Error ?? "پایش ناموفق بود.");
            if (detail?.Machine.Id == id) await LoadDetail(id);
            await LoadAll(silent: true);
        }
        catch (Exception ex) { Toast.Error("پایش ناموفق بود: " + ex.Message); }
        finally { polling = false; }
    }

    private async Task ResolveEvent(long eventId)
    {
        var r = await Http.PostAsync($"/api/OfficeMachines/events/{eventId}/resolve", null);
        if (!r.IsSuccessStatusCode) { Toast.Error(await ErrorOf(r)); return; }
        Toast.Success("رویداد بسته شد.");
        if (detail != null) await LoadDetail(detail.Machine.Id);
        await LoadAll(silent: true);
    }

    private async Task Retire(MachineItem m)
    {
        if (!await JS.InvokeAsync<bool>("confirm", $"«{m.Model}» خارج از رده شود؟")) return;
        var r = await Http.DeleteAsync($"/api/OfficeMachines/{m.Id}");
        if (r.IsSuccessStatusCode) { Toast.Success("دستگاه خارج از رده شد."); detail = null; await LoadAll(silent: true); }
        else Toast.Error(await ErrorOf(r));
    }

    private async Task HardDelete(MachineItem m)
    {
        if (!await JS.InvokeAsync<bool>("confirm", $"«{m.Model}» و همهٔ سوابقش برای همیشه حذف شود؟")) return;
        var r = await Http.DeleteAsync($"/api/OfficeMachines/{m.Id}?hard=true");
        if (r.IsSuccessStatusCode) { Toast.Success("دستگاه به‌طور کامل حذف شد."); detail = null; await LoadAll(silent: true); }
        else Toast.Error(await ErrorOf(r));
    }

    private async Task Restore(MachineItem m)
    {
        var r = await Http.PostAsync($"/api/OfficeMachines/{m.Id}/restore", null);
        if (r.IsSuccessStatusCode) { Toast.Success("دستگاه به چرخهٔ کار برگشت."); if (detail != null) await LoadDetail(m.Id); await LoadAll(silent: true); }
        else Toast.Error(await ErrorOf(r));
    }

    // ================================================================== فرم ثبت/ویرایش
    private bool ShowForm;
    private bool editing;
    private bool busy;
    private string formError = "";
    private int editId;
    private string formSection = "basic";
    private MachineForm form = new();
    private List<UserOpt> userLookup = new();
    private bool testing;
    private SnmpTestResult? testResult;

    private async Task OpenForm()
    {
        editing = false; editId = 0; formError = ""; testResult = null; formSection = "basic";
        form = new MachineForm();
        sysDropOpen = false; sysSearch = "";
        formAgentPrinters = new();
        await LoadLookups();
        ShowForm = true;
    }

    private async Task Edit(MachineItem m)
    {
        editing = true; editId = m.Id; formError = ""; testResult = null; formSection = "basic";
        form = MachineForm.From(m);
        sysDropOpen = false; sysSearch = "";
        formAgentPrinters = new();
        await LoadLookups();
        ShowForm = true;
        if (form.ConnectionType == "Cable" && form.LinkedSystemInfoId != null) _ = LoadFormAgentPrinters();
    }

    private void CloseForm() => ShowForm = false;

    // ================================================================== اختصاص به شرکت و کاربر
    private List<CompanyOpt> companyLookup = new();
    private List<PersonOpt> personLookup = new();

    private List<LookupItem> CompanyItems => companyLookup
        .Where(c => c.IsActive || c.Id == form.CompanyId)
        .OrderBy(c => c.Name)
        .Select(c => new LookupItem { Id = c.Id, Name = string.IsNullOrWhiteSpace(c.Code) ? c.Name : $"{c.Name} ({c.Code})" })
        .ToList();

    /// <summary>کاربران شرکت انتخاب‌شده؛ اگر شرکتی انتخاب نشده، همهٔ کاربران.</summary>
    private List<LookupItem> PersonItems => personLookup
        .Where(u => (u.IsActive || u.Id == form.AssignedUserId) && (form.CompanyId is not > 0 || u.CompanyId == form.CompanyId))
        .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
        .Select(u => new LookupItem
        {
            Id = u.Id,
            Name = string.IsNullOrWhiteSpace(u.StaffNumber) ? u.FullName : $"{u.FullName} ({u.StaffNumber})",
            InfoText = form.CompanyId is > 0 ? null : companyLookup.FirstOrDefault(c => c.Id == u.CompanyId)?.Name
        })
        .ToList();

    private void OnCompanySelected(int id)
    {
        form.CompanyId = id > 0 ? id : null;
        // کاربرِ شرکت دیگر با تغییر شرکت پاک می‌شود
        var u = personLookup.FirstOrDefault(x => x.Id == form.AssignedUserId);
        if (u != null && form.CompanyId != null && u.CompanyId != null && u.CompanyId != form.CompanyId) form.AssignedUserId = null;
    }

    private void OnPersonSelected(int id)
    {
        form.AssignedUserId = id > 0 ? id : null;
        var u = personLookup.FirstOrDefault(x => x.Id == id);
        if (u?.CompanyId is > 0 && form.CompanyId is not > 0) form.CompanyId = u.CompanyId;
    }

    // ================================================================== سیستم متصل (USB)
    private List<LookupItem> SysItems => sysLookup
        .OrderBy(x => x.Label)
        .Select(x => new LookupItem { Id = x.Id, Name = string.IsNullOrWhiteSpace(x.Owner) ? x.Label : $"{x.Label} — {x.Owner}", InfoText = x.Ip })
        .ToList();

    private async Task OnSysSelected(int id)
    {
        var s = sysLookup.FirstOrDefault(x => x.Id == id);
        if (s == null)
        {
            form.LinkedSystemInfoId = null; form.LinkedSystemLabel = null; form.AgentPrinterId = null;
            formAgentPrinters = new();
            return;
        }
        // کاربر و شرکتِ سیستم، اگر در فرم خالی باشند، پیشنهاد می‌شوند
        if (form.AssignedUserId is not > 0 && form.CompanyId is not > 0)
        {
            form.AssignedUserId = s.UserId is > 0 ? s.UserId : null;
            form.CompanyId = s.CompanyId is > 0 ? s.CompanyId : null;
        }
        PickSys(s);
        await Task.CompletedTask;
    }

    private void OnAgentPrinterChanged(ChangeEventArgs e)
    {
        var id = int.TryParse(e.Value?.ToString(), out var v) && v > 0 ? v : (int?)null;
        if (id == null) { form.AgentPrinterId = null; return; }
        var p = formAgentPrinters.FirstOrDefault(x => x.Id == id);
        if (p == null) return;
        form.AgentPrinterId = null;           // PickAgentPrinter حالت toggle دارد
        PickAgentPrinter(p);
    }

    private async Task SetConnection(string type)
    {
        form.ConnectionType = type;
        if (type == "Cable") await LoadFormAgentPrinters();
    }

    private async Task LoadLookups()
    {
        var tasks = new List<Task>();
        if (sysLookup.Count == 0)
            tasks.Add(Task.Run(async () =>
            {
                try { sysLookup = await Http.GetFromJsonAsync<List<SysOpt>>("/api/OfficeMachines/systems-lookup") ?? new(); }
                catch { sysLookup = new(); }
            }));
        if (companyLookup.Count == 0)
            tasks.Add(Task.Run(async () =>
            {
                try { companyLookup = await Http.GetFromJsonAsync<List<CompanyOpt>>("/api/SystemCompanies") ?? new(); }
                catch { companyLookup = new(); }
            }));
        if (personLookup.Count == 0)
            tasks.Add(Task.Run(async () =>
            {
                try { personLookup = await Http.GetFromJsonAsync<List<PersonOpt>>("/api/SystemUsers") ?? new(); }
                catch { personLookup = new(); }
            }));
        if (userLookup.Count == 0)
            tasks.Add(Task.Run(async () =>
            {
                try { userLookup = await Http.GetFromJsonAsync<List<UserOpt>>("/api/OfficeMachines/users-lookup") ?? new(); }
                catch { userLookup = new(); }
            }));
        await Task.WhenAll(tasks);
    }

    private async Task Save()
    {
        formError = "";
        if (string.IsNullOrWhiteSpace(form.Model)) { formError = "مدل را وارد کنید (یا با «تست SNMP» خودکار پر کنید)."; formSection = "basic"; return; }
        if (form.ConnectionType == "Network" && string.IsNullOrWhiteSpace(form.IpAddress)) { formError = "برای دستگاه شبکه‌ای، آدرس IP را وارد کنید."; formSection = "connection"; return; }
        if (form.ConnectionType == "Cable" && form.LinkedSystemInfoId is not > 0) { formError = "برای دستگاه کابلی، سیستم متصل را انتخاب کنید."; formSection = "connection"; return; }
        if (form.ConnectionType == "Cable") form.SnmpEnabled = false;
        busy = true;
        try
        {
            var payload = form.ToPayload();
            var resp = editing && editId > 0
                ? await Http.PutAsJsonAsync($"/api/OfficeMachines/{editId}", payload)
                : await Http.PostAsJsonAsync("/api/OfficeMachines", payload);
            if (resp.IsSuccessStatusCode)
            {
                ShowForm = false;
                Toast.Success(editing ? "تغییرات ذخیره شد." : "دستگاه افزوده شد." + (form.SnmpEnabled ? " اولین پایش در پس‌زمینه انجام می‌شود." : ""));
                await LoadAll(silent: true);
                if (detail != null && editing) await LoadDetail(editId);
                if (form.SnmpEnabled) { await Task.Delay(4000); await LoadAll(silent: true); StateHasChanged(); }
            }
            else formError = await ErrorOf(resp);
        }
        catch (Exception ex) { formError = "ارتباط با API برقرار نشد. (" + ex.Message + ")"; }
        finally { busy = false; }
    }

    private async Task TestSnmp()
    {
        testResult = null; formError = "";
        if (string.IsNullOrWhiteSpace(form.IpAddress)) { formError = "ابتدا آدرس IP را وارد کنید."; return; }
        testing = true;
        try
        {
            var r = await Http.PostAsJsonAsync("/api/OfficeMachines/snmp/test", new
            {
                MachineId = editing ? editId : (int?)null,
                IpAddress = form.IpAddress, Version = form.SnmpVersion, Port = form.SnmpPort, Community = form.SnmpCommunity,
                V3User = form.SnmpV3User, V3AuthProtocol = form.SnmpV3AuthProtocol, V3AuthPassword = form.SnmpV3AuthPassword,
                V3PrivProtocol = form.SnmpV3PrivProtocol, V3PrivPassword = form.SnmpV3PrivPassword
            });
            if (!r.IsSuccessStatusCode) { formError = await ErrorOf(r); return; }
            testResult = await r.Content.ReadFromJsonAsync<SnmpTestResult>();
        }
        catch (Exception ex) { formError = "تست SNMP ناموفق بود: " + ex.Message; }
        finally { testing = false; }
    }

    /// <summary>پرکردن خودکار فیلدهای خالی فرم از نتیجهٔ تست SNMP.</summary>
    private void ApplyTestResult(bool overwrite)
    {
        if (testResult is not { Ok: true } t) return;
        string? Pick(string? cur, string? val) => !string.IsNullOrWhiteSpace(val) && (overwrite || string.IsNullOrWhiteSpace(cur)) ? val.Trim() : cur;
        form.Brand = Pick(form.Brand, t.Brand);
        form.Model = Pick(form.Model, t.Model) ?? "";
        form.SerialNumber = Pick(form.SerialNumber, t.SerialNumber);
        form.MacAddress = Pick(form.MacAddress, t.MacAddress);
        form.Location = Pick(form.Location, t.SysLocation);
        if (t.IsColor == true) form.IsColor = true;
        form.SnmpEnabled = true;
        Toast.Success("اطلاعات دستگاه روی فرم اعمال شد.");
    }

    // ================================================================== کشف خودکار
    private bool ShowDiscover;
    private bool scanning;
    private string discoverError = "";
    private DiscoverForm disc = new();
    private DiscoverResult? discResult;
    private HashSet<string> discSelected = new();

    private void OpenDiscover()
    {
        discoverError = ""; discResult = null; discSelected.Clear();
        agentList = null; agentSelected.Clear();
        discTab = dash.UnlinkedAgentPrinters > 0 && items.All(i => i.ConnectionType != "Network") ? "agent" : "snmp";
        if (discTab == "agent") _ = SwitchDiscTab("agent");
        if (string.IsNullOrWhiteSpace(disc.Range))
        {
            var ip = items.Select(i => i.IpAddress).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && x!.Count(c => c == '.') == 3);
            if (ip != null) disc.Range = string.Join('.', ip.Split('.').Take(3)) + ".0/24";
        }
        ShowDiscover = true;
    }

    private async Task RunDiscover()
    {
        discoverError = ""; discResult = null; discSelected.Clear();
        if (string.IsNullOrWhiteSpace(disc.Range)) { discoverError = "بازهٔ IP را وارد کنید."; return; }
        scanning = true;
        try
        {
            var r = await Http.PostAsJsonAsync("/api/OfficeMachines/discover", disc.ToPayload());
            if (!r.IsSuccessStatusCode) { discoverError = await ErrorOf(r); return; }
            discResult = await r.Content.ReadFromJsonAsync<DiscoverResult>();
            foreach (var c in discResult?.Items ?? new()) if (c.ExistingMachineId == null) discSelected.Add(c.Ip);
        }
        catch (Exception ex) { discoverError = "اسکن ناموفق بود: " + ex.Message; }
        finally { scanning = false; }
    }

    private void ToggleDisc(string ip) { if (!discSelected.Remove(ip)) discSelected.Add(ip); }

    private async Task ImportDiscovered()
    {
        if (discResult == null || discSelected.Count == 0) { discoverError = "هیچ دستگاهی انتخاب نشده است."; return; }
        scanning = true;
        try
        {
            var payload = disc.ToPayload();
            var body = new
            {
                payload.Version, payload.Port, payload.Community, payload.V3User, payload.V3AuthProtocol, payload.V3AuthPassword,
                payload.V3PrivProtocol, payload.V3PrivPassword,
                EnableSnmp = true, PollIntervalMinutes = disc.PollIntervalMinutes,
                Items = discResult.Items.Where(c => discSelected.Contains(c.Ip)).Select(c => new { c.Ip, c.Brand, c.Model, c.SerialNumber, c.SysName, c.SysLocation }).ToList()
            };
            var r = await Http.PostAsJsonAsync("/api/OfficeMachines/discover/import", body);
            if (!r.IsSuccessStatusCode) { discoverError = await ErrorOf(r); return; }
            var res = await r.Content.ReadFromJsonAsync<JsonElement>();
            var created = res.TryGetProperty("created", out var cr) ? cr.GetInt32() : 0;
            Toast.Success($"{created} دستگاه افزوده شد؛ اولین پایش در پس‌زمینه انجام می‌شود.");
            ShowDiscover = false;
            await LoadAll(silent: true);
            await Task.Delay(5000);
            await LoadAll(silent: true);
        }
        catch (Exception ex) { discoverError = "افزودن ناموفق بود: " + ex.Message; }
        finally { scanning = false; }
    }

    // ================================================================== تعمیرات
    private List<RepairItem> repairList = new();
    private RepairForm rf = new();
    private int editingRepairId;
    private string repairError = "";

    private void ResetRepairForm() { editingRepairId = 0; rf = new RepairForm(); repairError = ""; }

    private async Task LoadRepairs()
    {
        if (detail == null) return;
        try { repairList = await Http.GetFromJsonAsync<List<RepairItem>>($"/api/OfficeMachines/{detail.Machine.Id}/repairs") ?? new(); }
        catch { repairList = new(); }
    }

    private void EditRepair(RepairItem r)
    {
        editingRepairId = r.Id;
        rf = new RepairForm { GoneDate = r.GoneDate, ReturnDate = r.ReturnDate, Problem = r.Problem, PerformedWork = r.PerformedWork, Cost = r.Cost, Fixed = r.Fixed };
    }

    private async Task SaveRepair()
    {
        repairError = "";
        if (detail == null) return;
        if (string.IsNullOrWhiteSpace(rf.Problem)) { repairError = "شرح ایراد را بنویسید."; return; }
        var payload = new RepairItem
        {
            Id = editingRepairId, GoneDate = rf.GoneDate, ReturnDate = rf.ReturnDate, Problem = rf.Problem.Trim(),
            PerformedWork = string.IsNullOrWhiteSpace(rf.PerformedWork) ? null : rf.PerformedWork.Trim(), Cost = rf.Cost, Fixed = rf.Fixed
        };
        var resp = editingRepairId > 0
            ? await Http.PutAsJsonAsync($"/api/OfficeMachines/repairs/{editingRepairId}", payload)
            : await Http.PostAsJsonAsync($"/api/OfficeMachines/{detail.Machine.Id}/repairs", payload);
        if (resp.IsSuccessStatusCode)
        {
            ResetRepairForm();
            await LoadRepairs();
            await LoadDetail(detail.Machine.Id);
            await LoadAll(silent: true);
        }
        else repairError = await ErrorOf(resp);
    }

    private async Task DelRepair(int rid)
    {
        if (!await JS.InvokeAsync<bool>("confirm", "این رکورد تعمیر حذف شود؟")) return;
        if (await Http.DeleteAsync($"/api/OfficeMachines/repairs/{rid}") is { IsSuccessStatusCode: true } && detail != null)
        {
            if (editingRepairId == rid) ResetRepairForm();
            await LoadRepairs();
            await LoadDetail(detail.Machine.Id);
            await LoadAll(silent: true);
        }
    }

    // ================================================================== هزینه‌ها
    private List<CostItem> costList = new();
    private CostItem cf = new();
    private string costError = "";

    private void ResetCostForm() { cf = new CostItem { CostDate = DateTime.Now, Category = "Toner" }; costError = ""; }

    private async Task LoadCosts()
    {
        if (detail == null) return;
        try { costList = await Http.GetFromJsonAsync<List<CostItem>>($"/api/OfficeMachines/{detail.Machine.Id}/costs") ?? new(); }
        catch { costList = new(); }
    }

    private void EditCost(CostItem c) => cf = new CostItem { Id = c.Id, Title = c.Title, Amount = c.Amount, Category = c.Category, CostDate = c.CostDate };

    private async Task SaveCost()
    {
        costError = "";
        if (detail == null) return;
        if (string.IsNullOrWhiteSpace(cf.Title)) { costError = "عنوان هزینه را بنویسید."; return; }
        if (cf.Amount <= 0) { costError = "مبلغ باید بزرگ‌تر از صفر باشد."; return; }
        var resp = cf.Id > 0
            ? await Http.PutAsJsonAsync($"/api/OfficeMachines/costs/{cf.Id}", cf)
            : await Http.PostAsJsonAsync($"/api/OfficeMachines/{detail.Machine.Id}/costs", cf);
        if (resp.IsSuccessStatusCode)
        {
            ResetCostForm();
            await LoadCosts();
            await LoadDetail(detail.Machine.Id);
            await LoadAll(silent: true);
        }
        else costError = await ErrorOf(resp);
    }

    private async Task DelCost(int id)
    {
        if (!await JS.InvokeAsync<bool>("confirm", "این هزینه حذف شود؟")) return;
        if (await Http.DeleteAsync($"/api/OfficeMachines/costs/{id}") is { IsSuccessStatusCode: true } && detail != null)
        {
            await LoadCosts();
            await LoadDetail(detail.Machine.Id);
            await LoadAll(silent: true);
        }
    }

    // ================================================================== کمبوی سیستم (کابلی)
    private List<SysOpt> sysLookup = new();
    private bool sysDropOpen;
    private string sysSearch = "";
    private ElementReference _omComboEl;
    private double _omTop, _omLeft, _omWidth;
    private string OmMenuStyle => Popover.FixedStyle(_omTop, _omLeft, _omWidth, 300);

    private async Task ToggleSysCombo()
    {
        if (sysDropOpen) { sysDropOpen = false; return; }
        try
        {
            var r = await JS.InvokeAsync<UiRect>("uiRect", _omComboEl);
            (_omTop, _omLeft, _omWidth) = Popover.Below(r, 0, 300);
        }
        catch (JSException) { }
        sysDropOpen = true;
    }

    private IEnumerable<SysOpt> FilteredSys =>
        string.IsNullOrWhiteSpace(sysSearch) ? sysLookup :
        sysLookup.Where(x => x.Label.Contains(sysSearch.Trim(), StringComparison.OrdinalIgnoreCase)
            || x.Owner.Contains(sysSearch.Trim(), StringComparison.OrdinalIgnoreCase)
            || (x.Ip ?? "").Contains(sysSearch.Trim(), StringComparison.OrdinalIgnoreCase));

    private void PickSys(SysOpt s)
    {
        if (form.LinkedSystemInfoId != s.Id) form.AgentPrinterId = null;
        form.LinkedSystemInfoId = s.Id;
        form.LinkedSystemLabel = $"{s.Label} — {s.Owner}";
        sysDropOpen = false;
        _ = LoadFormAgentPrinters();
    }

    // ================================================================== چاپگرهای ایجنت (USB)
    private List<AgentPrinterItem> formAgentPrinters = new();
    private bool loadingAgentPrinters;

    /// <summary>صف‌های چاپ گزارش‌شده توسط ایجنت سیستم انتخاب‌شده در فرم</summary>
    private async Task LoadFormAgentPrinters()
    {
        formAgentPrinters = new();
        if (form.ConnectionType != "Cable" || form.LinkedSystemInfoId is not > 0) { StateHasChanged(); return; }
        loadingAgentPrinters = true; StateHasChanged();
        try
        {
            formAgentPrinters = await Http.GetFromJsonAsync<List<AgentPrinterItem>>(
                $"/api/OfficeMachines/agent-printers?systemId={form.LinkedSystemInfoId}&includeIgnored=true") ?? new();
            // پیشنهاد خودکار: اگر فقط یک چاپگر USB آزاد هست و هنوز چیزی انتخاب نشده
            if (form.AgentPrinterId == null)
            {
                var free = formAgentPrinters.Where(p => p.IsPhysical && p.IsPresent && (p.LinkedMachineId == null || p.LinkedMachineId == editId)).ToList();
                if (free.Count == 1) form.AgentPrinterId = free[0].Id;
            }
        }
        catch { formAgentPrinters = new(); }
        finally { loadingAgentPrinters = false; StateHasChanged(); }
    }

    private void PickAgentPrinter(AgentPrinterItem p)
    {
        form.AgentPrinterId = form.AgentPrinterId == p.Id ? null : p.Id;
        if (form.AgentPrinterId != null)
        {
            var (brand, model) = SplitDriver(p.DriverName, p.PrinterName);
            if (string.IsNullOrWhiteSpace(form.Model)) form.Model = model;
            if (string.IsNullOrWhiteSpace(form.Brand)) form.Brand = brand;
            if (string.IsNullOrWhiteSpace(form.Location) && !string.IsNullOrWhiteSpace(p.Location)) form.Location = p.Location;
        }
    }

    private static readonly string[] KnownBrands = { "HP", "Canon", "Kyocera", "Ricoh", "Sharp", "Brother", "Xerox", "Konica Minolta", "Epson", "Lexmark", "Samsung", "Toshiba", "Pantum", "Panasonic", "OKI", "Dell" };
    private static (string? Brand, string Model) SplitDriver(string? driver, string name)
    {
        // درایورهای عمومی (Canon Generic Plus، HP Universal، Kyocera Classic Universal، Microsoft IPP Class) مدل واقعی را نمی‌گویند ← نام صف چاپ
        var generic = string.IsNullOrWhiteSpace(driver) || System.Text.RegularExpressions.Regex.IsMatch(driver, @"\b(Generic|Universal|IPP Class|Class Driver|WSD Print)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var text = generic ? name.Trim() : driver!.Trim();
        text = System.Text.RegularExpressions.Regex.Replace(text,
            @"\s+(PCL\s*-?\s*6|PCL\s*-?\s*XL|PCL\s*5[ce]?|PCL|PS3?|PostScript|UFR\s*II(\s*LT)?|UFRII(\s*LT)?|KX|GDI|XPS|V4|Class Driver|Series|\(.*?\))\b.*$",
            "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
        if (text.Length == 0) text = name;
        if (text.StartsWith("Hewlett-Packard ", StringComparison.OrdinalIgnoreCase)) text = "HP " + text[16..];
        var brand = KnownBrands.FirstOrDefault(b => text.StartsWith(b + " ", StringComparison.OrdinalIgnoreCase));
        var model = brand == null ? text : text[(brand.Length + 1)..].Trim();
        return (brand, model.Length == 0 ? text : model);
    }

    // --- تب «از ایجنت» در پنجرهٔ کشف
    private string discTab = "snmp";
    private List<AgentPrinterItem>? agentList;
    private HashSet<int> agentSelected = new();
    private bool agentShowVirtual;
    private bool agentShowIgnored;
    private string agentSearch = "";

    private async Task SwitchDiscTab(string tab)
    {
        discTab = tab; discoverError = "";
        if (tab == "agent" && agentList == null) await LoadAgentList();
    }

    private async Task LoadAgentList()
    {
        scanning = true; discoverError = "";
        try
        {
            agentList = await Http.GetFromJsonAsync<List<AgentPrinterItem>>(
                $"/api/OfficeMachines/agent-printers?includeVirtual={agentShowVirtual.ToString().ToLowerInvariant()}&includeIgnored={agentShowIgnored.ToString().ToLowerInvariant()}") ?? new();
            agentSelected = agentList.Where(a => a.CanImport && !a.IsIgnored && a.IsPhysical && a.IsPresent).Select(a => a.Id).ToHashSet();
        }
        catch (Exception ex) { discoverError = "دریافت فهرست ناموفق بود: " + ex.Message; agentList = new(); }
        finally { scanning = false; }
    }

    private IEnumerable<AgentPrinterItem> FilteredAgentList =>
        (agentList ?? new()).Where(a => string.IsNullOrWhiteSpace(agentSearch)
            || a.PrinterName.Contains(agentSearch.Trim(), StringComparison.OrdinalIgnoreCase)
            || (a.HostName ?? "").Contains(agentSearch.Trim(), StringComparison.OrdinalIgnoreCase)
            || (a.SystemOwner ?? "").Contains(agentSearch.Trim(), StringComparison.OrdinalIgnoreCase)
            || (a.DriverName ?? "").Contains(agentSearch.Trim(), StringComparison.OrdinalIgnoreCase)
            || (a.PortIp ?? "").Contains(agentSearch.Trim()));

    private void ToggleAgent(int id) { if (!agentSelected.Remove(id)) agentSelected.Add(id); }

    private async Task ImportAgentPrinters()
    {
        if (agentSelected.Count == 0) { discoverError = "هیچ چاپگری انتخاب نشده است."; return; }
        scanning = true; discoverError = "";
        try
        {
            var r = await Http.PostAsJsonAsync("/api/OfficeMachines/agent-printers/import",
                new { Ids = agentSelected.ToList(), EnableSnmpForNetwork = true, Community = string.IsNullOrWhiteSpace(disc.Community) ? "public" : disc.Community });
            if (!r.IsSuccessStatusCode) { discoverError = await ErrorOf(r); return; }
            var res = await r.Content.ReadFromJsonAsync<JsonElement>();
            var created = res.TryGetProperty("created", out var cr) ? cr.GetInt32() : 0;
            var skipped = res.TryGetProperty("skipped", out var sk) && sk.ValueKind == JsonValueKind.Array
                ? sk.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string>();
            if (created > 0) Toast.Success($"{created} دستگاه از گزارش ایجنت‌ها ثبت شد.");
            if (skipped.Count > 0) { discoverError = string.Join(" | ", skipped.Take(5)) + (skipped.Count > 5 ? $" … (+{skipped.Count - 5})" : ""); }
            await LoadAll(silent: true);
            await LoadAgentList();
            if (skipped.Count == 0) ShowDiscover = false;
        }
        catch (Exception ex) { discoverError = "ثبت ناموفق بود: " + ex.Message; }
        finally { scanning = false; }
    }

    private async Task IgnoreAgentPrinter(AgentPrinterItem a)
    {
        try
        {
            var r = await Http.PostAsJsonAsync($"/api/OfficeMachines/agent-printers/{a.Id}/ignore", new { Ignored = !a.IsIgnored });
            if (!r.IsSuccessStatusCode) { discoverError = await ErrorOf(r); return; }
            a.IsIgnored = !a.IsIgnored;
            if (a.IsIgnored) agentSelected.Remove(a.Id);
            if (a.IsIgnored && !agentShowIgnored) agentList?.Remove(a);
        }
        catch (Exception ex) { discoverError = ex.Message; }
    }

    private async Task DeleteAgentPrinter(AgentPrinterItem a)
    {
        if (!await JS.InvokeAsync<bool>("confirm", $"«{a.PrinterName}» از فهرست حذف شود؟"))
            return;
        try
        {
            var r = await Http.DeleteAsync($"/api/OfficeMachines/agent-printers/{a.Id}");
            if (!r.IsSuccessStatusCode) { discoverError = await ErrorOf(r); return; }
            agentList?.Remove(a); agentSelected.Remove(a.Id);
            if (a.LinkedMachineId != null) await LoadAll(silent: true);
        }
        catch (Exception ex) { discoverError = ex.Message; }
    }

    private static string AgentKindTitle(string? k) => k switch
    {
        "Usb" => "USB", "Local" => "LPT/COM", "Network" => "پورت شبکه", "Wsd" => "WSD", "Connection" => "اشتراکی (سیستم دیگر)", "Virtual" => "مجازی", _ => k ?? "—"
    };

    private static string AgentKindCss(string? k) => k switch
    {
        "Usb" or "Local" => "bg-warning-subtle text-warning-emphasis", "Network" or "Wsd" => "bg-primary-subtle text-primary-emphasis", _ => "bg-secondary-subtle text-secondary-emphasis"
    };

    // ================================================================== کمکی‌های نمایش
    private static async Task<string> ErrorOf(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var m)) return m.GetString() ?? $"کد {(int)r.StatusCode}";
        }
        catch { }
        return (int)r.StatusCode == 403 ? "مجوز این عملیات را ندارید." : $"خطای سرور (کد {(int)r.StatusCode})";
    }

    private static string StateTitle(string? s) => s switch
    {
        "Online" => "آماده", "Warning" => "هشدار", "Error" => "خطا", "Offline" => "آفلاین",
        "NotMonitored" => "بدون پایش", _ => "نامشخص"
    };

    private static string StateCss(string? s) => s switch
    {
        "Online" => "ok", "Warning" => "warn", "Error" => "err", "Offline" => "off", _ => "na"
    };

    private static string PrinterStateTitle(string? s) => s switch
    {
        "idle" => "آماده به کار", "printing" => "در حال چاپ", "warmup" => "در حال گرم شدن", "other" => "سایر", _ => "—"
    };

    private static string StatusTitle(string s) => s switch
    {
        "InService" => "در حال کار", "InRepair" => "در تعمیر", "Standby" => "رزرو / انبار", "Retired" => "خارج از رده", _ => s
    };

    private static string StatusCss(string s) => s switch
    {
        "InService" => "bg-success-subtle text-success-emphasis", "InRepair" => "bg-warning-subtle text-warning-emphasis",
        "Standby" => "bg-info-subtle text-info-emphasis", _ => "bg-secondary-subtle text-secondary-emphasis"
    };

    private static string DeviceTypeTitle(string t) => t switch
    {
        "Printer" => "پرینتر", "MFP" => "چندکاره (MFP)", "Copier" => "فتوکپی", "Scanner" => "اسکنر", "Fax" => "فکس",
        "Plotter" => "پلاتر", "LabelPrinter" => "لیبل‌پرینتر", _ => "سایر"
    };

    private static string DeviceIcon(string t) => t switch
    {
        "Scanner" => "bi-upc-scan", "Fax" => "bi-telephone", "Copier" => "bi-files", "MFP" => "bi-printer-fill",
        "Plotter" => "bi-easel", "LabelPrinter" => "bi-tags", _ => "bi-printer"
    };

    private static string SupplyColorCss(string? color, string kind) => (color ?? "").ToLowerInvariant() switch
    {
        "black" => "#1f2937", "cyan" => "#06b6d4", "magenta" => "#ec4899", "yellow" => "#eab308",
        _ => kind switch { "Drum" => "#8b5cf6", "Waste" => "#94a3b8", "Fuser" => "#f97316", _ => "#64748b" }
    };

    private static string ColorTitle(string? color) => (color ?? "").ToLowerInvariant() switch
    {
        "black" => "مشکی", "cyan" => "فیروزه‌ای", "magenta" => "ارغوانی", "yellow" => "زرد", "" => "", var c => c
    };

    private static string KindTitle(string kind) => kind switch
    {
        "Toner" => "تونر", "Ink" => "جوهر", "Drum" => "درام", "Developer" => "دولوپر", "Fuser" => "فیوزر",
        "Transfer" => "واحد انتقال", "Waste" => "مخزن ضایعات", "Staples" => "منگنه", _ => "مصرفی"
    };

    private static string SupplyLabel(SupplyItem s)
    {
        var c = ColorTitle(s.Color);
        return string.IsNullOrEmpty(c) || s.Kind is "Waste" or "Fuser" ? KindTitle(s.Kind) : $"{KindTitle(s.Kind)} {c}";
    }

    private static string FlagTitle(string code) => code switch
    {
        "Jammed" => "گیر کاغذ", "NoPaper" => "کاغذ تمام", "LowPaper" => "کاغذ کم", "NoToner" => "تونر تمام",
        "LowToner" => "تونر کم", "DoorOpen" => "در باز", "Offline" => "آفلاین", "ServiceRequested" => "نیاز به سرویس",
        "InputTrayMissing" => "سینی ورودی نیست", "OutputTrayMissing" => "سینی خروجی نیست", "MarkerSupplyMissing" => "کارتریج نصب نیست",
        "OutputNearFull" => "خروجی تقریباً پر", "OutputFull" => "خروجی پر", "InputTrayEmpty" => "سینی خالی",
        "OverduePreventMaint" => "سرویس دوره‌ای", "DeviceDown" => "دستگاه Down",
        "Paused" => "صف متوقف (Pause)", "Error" => "خطای چاپگر", "UserIntervention" => "نیاز به اقدام کاربر", "NotAvailable" => "در دسترس نیست",
        "JobError" => "خطای کار چاپی", "QueueStuck" => "صف چاپ گیر کرده", "PrinterRemoved" => "چاپگر از سیستم حذف شده",
        "OutputBinFull" => "خروجی پر", "PaperProblem" => "مشکل کاغذ", "PaperOut" => "کاغذ تمام", "PaperJam" => "گیر کاغذ", "TonerLow" => "تونر کم", _ when code.StartsWith("LowSupply") => "مصرفی کم", _ => code
    };

    private static string CostCategoryTitle(string c) => c switch
    {
        "Toner" => "تونر / کارتریج", "Paper" => "کاغذ", "Service" => "سرویس", "Part" => "قطعه", "Contract" => "قرارداد", _ => "سایر"
    };

    private static string Ago(DateTime? dt)
    {
        if (dt == null) return "هرگز";
        var d = DateTime.Now - dt.Value;
        if (d.TotalSeconds < 60) return "لحظاتی پیش";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} دقیقه پیش";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours} ساعت پیش";
        return $"{(int)d.TotalDays} روز پیش";
    }

    private static string Uptime(long? s)
    {
        if (s is not > 0) return "—";
        var t = TimeSpan.FromSeconds(s.Value);
        return t.TotalDays >= 1 ? $"{(int)t.TotalDays} روز و {t.Hours} ساعت" : $"{t.Hours} ساعت و {t.Minutes} دقیقه";
    }

    private static string N0(long? n) => n is null ? "—" : n.Value.ToString("N0");
    private static string D(DateTime? d) => d?.ToFa() ?? "—";

    private static readonly string[] FaMonths = { "", "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند" };

    private static string Expiry(DateTime? d)
    {
        if (d == null) return "—";
        var days = (d.Value.Date - DateTime.Now.Date).TotalDays;
        return days < 0 ? $"{d.Value.ToFa()} (منقضی)" : days <= 30 ? $"{d.Value.ToFa()} ({(int)days} روز مانده)" : d.Value.ToFa();
    }

    private static string ExpiryCss(DateTime? d)
    {
        if (d == null) return "";
        var days = (d.Value.Date - DateTime.Now.Date).TotalDays;
        return days < 0 ? "text-danger fw-800" : days <= 30 ? "text-warning-emphasis fw-800" : "";
    }

    // ================================================================== مدل‌ها
    public class SupplyItem
    {
        public int Index { get; set; }
        public string Kind { get; set; } = "";
        public string? Color { get; set; }
        public string? Description { get; set; }
        public int Level { get; set; }
        public int MaxCapacity { get; set; }
        public int? Percent { get; set; }
    }

    public class MachineItem
    {
        public int Id { get; set; }
        public string DeviceType { get; set; } = "Printer";
        public string? Brand { get; set; }
        public string Model { get; set; } = "";
        public string? SerialNumber { get; set; }
        public string? AssetTag { get; set; }
        public bool IsColor { get; set; }
        public string? Location { get; set; }
        public string? Department { get; set; }
        public int? ResponsibleUserId { get; set; }
        public string? ResponsibleName { get; set; }
        public int? CompanyId { get; set; }
        public string? CompanyName { get; set; }
        public int? AssignedUserId { get; set; }
        public string? AssignedUserName { get; set; }
        public DateTime? InstallDate { get; set; }
        public string Status { get; set; } = "InService";
        public bool IsActive { get; set; } = true;
        public string? Notes { get; set; }
        public string ConnectionType { get; set; } = "Network";
        public string? IpAddress { get; set; }
        public int? LinkedSystemInfoId { get; set; }
        public string? LinkedSystemLabel { get; set; }
        public string? MacAddress { get; set; }
        public string? Hostname { get; set; }
        public string? FirmwareVersion { get; set; }
        public string? SysDescr { get; set; }
        public DateTime? PurchaseDate { get; set; }
        public decimal? PurchasePrice { get; set; }
        public DateTime? WarrantyUntil { get; set; }
        public string? ServiceVendor { get; set; }
        public DateTime? ServiceContractUntil { get; set; }
        public bool SnmpEnabled { get; set; }
        public string SnmpVersion { get; set; } = "v2c";
        public int SnmpPort { get; set; } = 161;
        public bool HasCommunity { get; set; }
        public string? SnmpV3User { get; set; }
        public string SnmpV3AuthProtocol { get; set; } = "SHA1";
        public bool HasV3Auth { get; set; }
        public string SnmpV3PrivProtocol { get; set; } = "AES";
        public bool HasV3Priv { get; set; }
        public int PollIntervalMinutes { get; set; } = 10;
        public bool AlertsEnabled { get; set; } = true;
        public int SupplyLowThreshold { get; set; } = 15;
        public DateTime? LastPollAt { get; set; }
        public DateTime? LastSeenOnlineAt { get; set; }
        public string OnlineState { get; set; } = "Unknown";
        public string? PrinterState { get; set; }
        public long? LastPageCount { get; set; }
        public int? LowestSupplyPercent { get; set; }
        public string? LastStatusText { get; set; }
        public string? LastErrorCodes { get; set; }
        public string? LastPollError { get; set; }
        public long? UptimeSeconds { get; set; }
        public DateTime CreatedAt { get; set; }
        public int RepairCount { get; set; }
        public int OpenRepairs { get; set; }
        public decimal TotalCost { get; set; }
        public int OpenEvents { get; set; }
        public int CriticalEvents { get; set; }
        public long Pages30d { get; set; }
        public List<SupplyItem> Supplies { get; set; } = new();
        public string MonitorSource { get; set; } = "None";
        public int? AgentPrinterId { get; set; }
        public string? AgentPrinterName { get; set; }
        public string? AgentHost { get; set; }
        public string? AgentDriver { get; set; }
        public string? AgentPort { get; set; }
        public DateTime? AgentLastReportAt { get; set; }
        public bool AgentPrinterPresent { get; set; }
        public bool AgentPageLog { get; set; }
        public int JobsCount { get; set; }
        public int JobsInError { get; set; }
        public int? OldestJobMinutes { get; set; }

        public bool Monitored => MonitorSource != "None";
        public bool ByAgent => MonitorSource == "Agent";

        public IEnumerable<string> Flags => string.IsNullOrWhiteSpace(LastErrorCodes)
            ? Enumerable.Empty<string>() : LastErrorCodes.Split(',', StringSplitOptions.RemoveEmptyEntries);
        public IEnumerable<SupplyItem> MainSupplies => Supplies.Where(s => s.Kind is "Toner" or "Ink").OrderBy(s => ColorOrder(s.Color));
        private static int ColorOrder(string? c) => c switch { "cyan" => 0, "magenta" => 1, "yellow" => 2, "black" => 3, _ => 4 };
    }

    public class DashboardInfo
    {
        public int Total { get; set; }
        public int Monitored { get; set; }
        public int MonitoredByAgent { get; set; }
        public int UnlinkedAgentPrinters { get; set; }
        public int Online { get; set; }
        public int Warning { get; set; }
        public int Error { get; set; }
        public int Offline { get; set; }
        public int LowSupply { get; set; }
        public int InRepair { get; set; }
        public int OpenEvents { get; set; }
        public int CriticalEvents { get; set; }
        public long Pages30d { get; set; }
        public int WarrantyExpiring { get; set; }
        public int ContractExpiring { get; set; }
        public int Retired { get; set; }
    }

    public class TrayItem
    {
        public int TrayIndex { get; set; }
        public string? Name { get; set; }
        public string? MediaName { get; set; }
        public int CurrentLevel { get; set; }
        public int MaxCapacity { get; set; }
        public int? Percent => MaxCapacity > 0 && CurrentLevel >= 0 ? Math.Min(100, CurrentLevel * 100 / MaxCapacity) : CurrentLevel == -3 ? 60 : null;
    }

    public class EventItem
    {
        public long Id { get; set; }
        public int MachineId { get; set; }
        public string Code { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Message { get; set; } = "";
        public string Source { get; set; } = "";
        public DateTime OpenedAt { get; set; }
        public DateTime LastSeenAt { get; set; }
        public bool IsResolved { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public string? ResolvedByName { get; set; }
        public string? Model { get; set; }
        public string? Location { get; set; }
        public string? IpAddress { get; set; }
    }

    public class MonthlyItem { public int Year { get; set; } public int Month { get; set; } public long Pages { get; set; } }
    public class DailyItem { public DateTime Day { get; set; } public long Pages { get; set; } }

    public class DetailInfo
    {
        public MachineItem Machine { get; set; } = new();
        public List<TrayItem> Trays { get; set; } = new();
        public List<EventItem> Events { get; set; } = new();
        public List<MonthlyItem> Monthly { get; set; } = new();
        public List<DailyItem> Daily { get; set; } = new();
    }

    public class PollResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string State { get; set; } = "";
        public int NewEvents { get; set; }
        public int ResolvedEvents { get; set; }
        public string? Source { get; set; }
        public DateTime? LastReportAt { get; set; }
    }

    public class FlagItem { public string Code { get; set; } = ""; public string Title { get; set; } = ""; }

    public class SnmpTestResult
    {
        public bool Ok { get; set; }
        public string? Message { get; set; }
        public string? Brand { get; set; }
        public string? Model { get; set; }
        public string? SerialNumber { get; set; }
        public string? MacAddress { get; set; }
        public string? SysName { get; set; }
        public string? SysLocation { get; set; }
        public string? SysDescr { get; set; }
        public string? FirmwareVersion { get; set; }
        public bool IsPrinter { get; set; }
        public bool? IsColor { get; set; }
        public long? PageCount { get; set; }
        public string? PrinterState { get; set; }
        public string? DisplayText { get; set; }
        public long? UptimeSeconds { get; set; }
        public List<FlagItem> ErrorFlags { get; set; } = new();
        public List<SupplyItem> Supplies { get; set; } = new();
    }

    public class MachineForm
    {
        public string DeviceType { get; set; } = "Printer";
        public string? Brand { get; set; }
        public string Model { get; set; } = "";
        public string? SerialNumber { get; set; }
        public string? AssetTag { get; set; }
        public bool IsColor { get; set; }
        public string? Location { get; set; }
        public string? Department { get; set; }
        public int? ResponsibleUserId { get; set; }
        public int? CompanyId { get; set; }
        public int? AssignedUserId { get; set; }
        public DateTime? InstallDate { get; set; }
        public string Status { get; set; } = "InService";
        public string? Notes { get; set; }
        public string ConnectionType { get; set; } = "Network";
        public string? IpAddress { get; set; }
        public int? LinkedSystemInfoId { get; set; }
        public string? LinkedSystemLabel { get; set; }
        public int? AgentPrinterId { get; set; }
        public string? MacAddress { get; set; }
        public DateTime? PurchaseDate { get; set; }
        public decimal PurchasePrice { get; set; }
        public DateTime? WarrantyUntil { get; set; }
        public string? ServiceVendor { get; set; }
        public DateTime? ServiceContractUntil { get; set; }
        public bool SnmpEnabled { get; set; } = true;
        public string SnmpVersion { get; set; } = "v2c";
        public int SnmpPort { get; set; } = 161;
        public string? SnmpCommunity { get; set; }
        public bool HasCommunity { get; set; }
        public string? SnmpV3User { get; set; }
        public string SnmpV3AuthProtocol { get; set; } = "SHA1";
        public string? SnmpV3AuthPassword { get; set; }
        public bool HasV3Auth { get; set; }
        public string SnmpV3PrivProtocol { get; set; } = "AES";
        public string? SnmpV3PrivPassword { get; set; }
        public bool HasV3Priv { get; set; }
        public int PollIntervalMinutes { get; set; } = 10;
        public bool AlertsEnabled { get; set; } = true;
        public int SupplyLowThreshold { get; set; } = 15;

        public static MachineForm From(MachineItem m) => new()
        {
            DeviceType = m.DeviceType, Brand = m.Brand, Model = m.Model, SerialNumber = m.SerialNumber, AssetTag = m.AssetTag,
            IsColor = m.IsColor, Location = m.Location, Department = m.Department, ResponsibleUserId = m.ResponsibleUserId,
            CompanyId = m.CompanyId, AssignedUserId = m.AssignedUserId,
            InstallDate = m.InstallDate, Status = m.Status, Notes = m.Notes, ConnectionType = m.ConnectionType ?? "Network",
            IpAddress = m.IpAddress, LinkedSystemInfoId = m.LinkedSystemInfoId, LinkedSystemLabel = m.LinkedSystemLabel, AgentPrinterId = m.AgentPrinterId,
            MacAddress = m.MacAddress, PurchaseDate = m.PurchaseDate, PurchasePrice = m.PurchasePrice ?? 0,
            WarrantyUntil = m.WarrantyUntil, ServiceVendor = m.ServiceVendor, ServiceContractUntil = m.ServiceContractUntil,
            SnmpEnabled = m.SnmpEnabled, SnmpVersion = m.SnmpVersion, SnmpPort = m.SnmpPort, HasCommunity = m.HasCommunity,
            SnmpV3User = m.SnmpV3User, SnmpV3AuthProtocol = m.SnmpV3AuthProtocol, HasV3Auth = m.HasV3Auth,
            SnmpV3PrivProtocol = m.SnmpV3PrivProtocol, HasV3Priv = m.HasV3Priv, PollIntervalMinutes = m.PollIntervalMinutes,
            AlertsEnabled = m.AlertsEnabled, SupplyLowThreshold = m.SupplyLowThreshold
        };

        public object ToPayload() => new
        {
            DeviceType, Brand, Model = Model.Trim(), SerialNumber, AssetTag, IsColor, Location, Department, ResponsibleUserId,
            CompanyId, AssignedUserId,
            InstallDate, Status, Notes, ConnectionType,
            IpAddress = ConnectionType == "Network" ? IpAddress?.Trim() : null,
            LinkedSystemInfoId = ConnectionType == "Cable" ? LinkedSystemInfoId : null,
            LinkedSystemLabel = ConnectionType == "Cable" ? LinkedSystemLabel : null,
            AgentPrinterId = ConnectionType == "Cable" ? AgentPrinterId : null,
            MacAddress, PurchaseDate, PurchasePrice = PurchasePrice > 0 ? PurchasePrice : (decimal?)null,
            WarrantyUntil, ServiceVendor, ServiceContractUntil,
            SnmpEnabled = SnmpEnabled && ConnectionType == "Network", SnmpVersion, SnmpPort, SnmpCommunity,
            SnmpV3User, SnmpV3AuthProtocol, SnmpV3AuthPassword, SnmpV3PrivProtocol, SnmpV3PrivPassword,
            PollIntervalMinutes, AlertsEnabled, SupplyLowThreshold
        };
    }

    public class DiscoverForm
    {
        public string Range { get; set; } = "";
        public string Version { get; set; } = "v2c";
        public int Port { get; set; } = 161;
        public string Community { get; set; } = "public";
        public string? V3User { get; set; }
        public string V3AuthProtocol { get; set; } = "SHA1";
        public string? V3AuthPassword { get; set; }
        public string V3PrivProtocol { get; set; } = "AES";
        public string? V3PrivPassword { get; set; }
        public bool PrintersOnly { get; set; } = true;
        public int PollIntervalMinutes { get; set; } = 10;

        public DiscoverPayload ToPayload() => new()
        {
            Range = Range.Trim(), Version = Version, Port = Port, Community = Community, V3User = V3User,
            V3AuthProtocol = V3AuthProtocol, V3AuthPassword = V3AuthPassword, V3PrivProtocol = V3PrivProtocol,
            V3PrivPassword = V3PrivPassword, PrintersOnly = PrintersOnly
        };
    }

    public class DiscoverPayload
    {
        public string Range { get; set; } = "";
        public string Version { get; set; } = "v2c";
        public int Port { get; set; } = 161;
        public string? Community { get; set; }
        public string? V3User { get; set; }
        public string V3AuthProtocol { get; set; } = "SHA1";
        public string? V3AuthPassword { get; set; }
        public string V3PrivProtocol { get; set; } = "AES";
        public string? V3PrivPassword { get; set; }
        public bool PrintersOnly { get; set; } = true;
    }

    public class DiscoverCandidate
    {
        public string Ip { get; set; } = "";
        public bool IsPrinter { get; set; }
        public string? Brand { get; set; }
        public string? Model { get; set; }
        public string? SerialNumber { get; set; }
        public string? SysName { get; set; }
        public string? SysDescr { get; set; }
        public string? SysLocation { get; set; }
        public int? ExistingMachineId { get; set; }
    }

    public class DiscoverResult { public int Scanned { get; set; } public List<DiscoverCandidate> Items { get; set; } = new(); }

    public class AgentPrinterItem
    {
        public int Id { get; set; }
        public string AgentId { get; set; } = "";
        public int? SystemInfoId { get; set; }
        public string? SystemLabel { get; set; }
        public string? SystemOwner { get; set; }
        public string? HostName { get; set; }
        public string PrinterName { get; set; } = "";
        public string? DriverName { get; set; }
        public string? PortName { get; set; }
        public string? Kind { get; set; }
        public string? PortIp { get; set; }
        public bool IsDefault { get; set; }
        public bool IsShared { get; set; }
        public string? Location { get; set; }
        public string? Status { get; set; }
        public string? StatusTitle { get; set; }
        public string? ErrorCodes { get; set; }
        public int JobsCount { get; set; }
        public int JobsInError { get; set; }
        public int? OldestJobMinutes { get; set; }
        public long PagesLogged { get; set; }
        public bool PageLog { get; set; }
        public bool IsPresent { get; set; }
        public bool IsIgnored { get; set; }
        public DateTime? FirstSeenAt { get; set; }
        public DateTime? LastSeenAt { get; set; }
        public DateTime? LastReportAt { get; set; }
        public bool Stale { get; set; }
        public int? LinkedMachineId { get; set; }
        public string? LinkedMachineModel { get; set; }
        public int? NetworkMachineId { get; set; }
        public string? NetworkMachineModel { get; set; }

        public bool IsPhysical => Kind is "Usb" or "Local";
        public bool CanImport => LinkedMachineId == null && NetworkMachineId == null
            && (IsPhysical ? SystemInfoId != null : (Kind == "Network" && PortIp != null));
    }

    public class SysOpt
    {
        public int Id { get; set; }
        public string Label { get; set; } = "";
        public string? Ip { get; set; }
        public string Owner { get; set; } = "";
        public int? UserId { get; set; }
        public int? CompanyId { get; set; }
    }

    public class CompanyOpt { public int Id { get; set; } public string Name { get; set; } = ""; public string? Code { get; set; } public bool IsActive { get; set; } = true; }

    public class PersonOpt
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string StaffNumber { get; set; } = "";
        public int? CompanyId { get; set; }
        public bool IsActive { get; set; } = true;
        public string FullName => $"{FirstName} {LastName}".Trim();
    }
    public class UserOpt { public int Id { get; set; } public string Name { get; set; } = ""; public string Username { get; set; } = ""; }

    public class RepairItem
    {
        public int Id { get; set; }
        public DateTime RepairDate { get; set; } = DateTime.Now;
        public DateTime? GoneDate { get; set; }
        public DateTime? ReturnDate { get; set; }
        public string Problem { get; set; } = "";
        public string? PerformedWork { get; set; }
        public decimal Cost { get; set; }
        public bool Fixed { get; set; } = true;
    }

    private class RepairForm
    {
        public DateTime? GoneDate { get; set; } = DateTime.Now;
        public DateTime? ReturnDate { get; set; }
        public string Problem { get; set; } = "";
        public string? PerformedWork { get; set; }
        public decimal Cost { get; set; }
        public bool Fixed { get; set; } = true;
    }

    public class CostItem
    {
        public int Id { get; set; }
        public DateTime CostDate { get; set; } = DateTime.Now;
        public string Title { get; set; } = "";
        public decimal Amount { get; set; }
        public string Category { get; set; } = "Other";
    }
}
