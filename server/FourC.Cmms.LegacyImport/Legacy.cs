using System.Reflection;
using Dapper;
using Microsoft.Data.SqlClient;

#pragma warning disable CS8618 // Dapper-populated DTOs
public sealed class LSite { public string SiteCode { get; set; } public string? Sitename { get; set; } }
public sealed class LDepartment { public string DepartmentNo { get; set; } public string? DepartmentDescription { get; set; } public string? SiteCode { get; set; } public bool? Active { get; set; } public string? Latitude { get; set; } public string? Longitude { get; set; } }
public sealed class LLocation { public string LocationNo { get; set; } public string? LocationDescription { get; set; } public string? NotetoTech { get; set; } public string? DeptNo { get; set; } public string? LocCriticality { get; set; } public string? SiteCode { get; set; } }
public sealed class LCategory { public string AssetCatCode { get; set; } public string? AssetCategory { get; set; } }
public sealed class LStatus { public int AssetStatusID { get; set; } public string? AssetStatusDesc { get; set; } }
public sealed class LWarranty { public int Warranty_contractID { get; set; } public string? Warranty_contract { get; set; } }
public sealed class LSibling { public string AssetNo { get; set; } public string Sibling { get; set; } }
public sealed class LTransfer { public long TransferId { get; set; } public string AssetNo { get; set; } public string? TransferType { get; set; } public string? LocationNo { get; set; } public string? SiteCode { get; set; } public string? Remarks { get; set; } public DateTime? TransferDate { get; set; } }

public sealed class LAsset
{
    public string AssetNumber { get; set; }
    public string? AssetDescription { get; set; }
    public string? LocationNo { get; set; }
    public string? AssetCatCode { get; set; }
    public int? Status { get; set; }
    public string? ModelNumber { get; set; }
    public string? SerialNumber { get; set; }
    public DateTime? DateAcquired { get; set; }
    public DateTime? DateSold { get; set; }
    public DateTime? DataDisposed { get; set; }
    public double? PurchasePrice { get; set; }
    public double? Price { get; set; }
    public double? SalvageValue { get; set; }
    public double? CurrentValue { get; set; }
    public string? Comments { get; set; }
    public int? Warranty_Contract { get; set; }
    public DateTime? Warranty_ContractExpiry { get; set; }
    public string? Warranty_ContractNotes { get; set; }
    public string? DepartmentNo { get; set; }
    public string? Criticality { get; set; }
    public string? NotesToTech { get; set; }
    public double? EstLife { get; set; }
    public string? Manufacturer { get; set; }
    public string? SiteCode { get; set; }
    public string? OpNo { get; set; }
    public decimal? DowntimeCostPerHour { get; set; }
    public string? NameplateInfo1 { get; set; } public string? NameplateInfo2 { get; set; } public string? NameplateInfo3 { get; set; } public string? NameplateInfo4 { get; set; }
    public string? NameplateInfo5 { get; set; } public string? NameplateInfo6 { get; set; } public string? NameplateInfo7 { get; set; } public string? NameplateInfo8 { get; set; }
    public string? NameplateInfo9 { get; set; } public string? NameplateInfo10 { get; set; } public string? NameplateInfo11 { get; set; } public string? NameplateInfo12 { get; set; }
    public string? NameplateInfo13 { get; set; } public string? NameplateInfo14 { get; set; } public string? NameplateInfo15 { get; set; } public string? NameplateInfo16 { get; set; }
    public string? NameplateInfo17 { get; set; } public string? NameplateInfo18 { get; set; } public string? NameplateInfo19 { get; set; } public string? NameplateInfo20 { get; set; }
    public string? NameplateInfo21 { get; set; } public string? NameplateInfo22 { get; set; } public string? NameplateInfo23 { get; set; } public string? NameplateInfo24 { get; set; }
    public string? NameplateInfo25 { get; set; } public string? NameplateInfo26 { get; set; } public string? NameplateInfo27 { get; set; } public string? NameplateInfo28 { get; set; }
    public string? NameplateInfo29 { get; set; } public string? NameplateInfo30 { get; set; }

    private static readonly PropertyInfo[] NameplateProps = Enumerable.Range(1, 30).Select(n => typeof(LAsset).GetProperty($"NameplateInfo{n}")!).ToArray();
    public string? Nameplate(int n) => (string?)NameplateProps[n - 1].GetValue(this);
}
public sealed class LIdName { public int Id { get; set; } public string? Name { get; set; } }
public sealed class LFailure { public string FailureCauseCode { get; set; } public string? FailureCauseDescription { get; set; } public bool? IsActive { get; set; } }
public sealed class LEmployee
{
    public string EmployeeNO { get; set; } public string? FirstName { get; set; } public string? Positions { get; set; } public string? Email { get; set; }
    public string? HandPhone { get; set; } public string? WorkPhone { get; set; } public double? HourlySalary { get; set; } public double? OverTime1 { get; set; }
    public int? WorkTrade { get; set; } public string? SiteCode { get; set; } public bool? IsInternal { get; set; } public int? Central { get; set; }
}
public sealed class LRequest
{
    public string RequestNo { get; set; } public string? SiteCode { get; set; } public int? RequestStatus { get; set; } public DateTime? ReceivedDate { get; set; }
    public string? ProblemDesc { get; set; } public DateTime? RequiredDate { get; set; } public int? WorkType { get; set; } public int? WorkPriority { get; set; }
    public string? EmployeeNo { get; set; } public string? AssetNo { get; set; } public string? WorkorderNo { get; set; } public string? Remarks { get; set; }
    public string? LocationNo { get; set; } public string? requestby { get; set; } public string? telno { get; set; }
}
public sealed class LWorkOrder
{
    public string WorkorderNo { get; set; } public string? RequestorNo { get; set; } public string? EmployeeNo { get; set; } public string? AssetNo { get; set; }
    public string? LocationNo { get; set; } public string? ProblemDescription { get; set; } public DateTime? DateReceived { get; set; } public DateTime? EstDateStart { get; set; }
    public DateTime? ActDateStart { get; set; } public DateTime? ActDateEnd { get; set; } public DateTime? DateRequired { get; set; } public double? EstDuration { get; set; }
    public string? ActionTaken { get; set; } public string? FailureCauseCode { get; set; } public int? WorkType { get; set; } public string? CauseDescription { get; set; }
    public string? PreventionTaken { get; set; } public int? WorkStatus { get; set; } public int? WorkPriority { get; set; } public int? WOTrade { get; set; }
    public string? SiteCode { get; set; } public double? DownTime { get; set; } public string? Notes { get; set; } public string? requestby { get; set; }
}
public sealed class LAssign { public string EmployeeNo { get; set; } public string WorkorderNo { get; set; } }
public sealed class LLabor
{
    public long WorkorderLaborID { get; set; } public string WorkorderNo { get; set; } public string EmployeeNo { get; set; } public string? Comment { get; set; }
    public double? SalaryHours { get; set; } public double? OverTime1 { get; set; } public double? OverTime2 { get; set; } public double? OverTime3 { get; set; } public double? OverTime4 { get; set; }
    public DateTime? StartDate { get; set; } public DateTime? LogDate { get; set; } public double? TotHour { get; set; } public double? TotCost { get; set; }
}
public sealed class LDirect { public long DirectID { get; set; } public string? StockDesc { get; set; } public DateTime? Date { get; set; } public string? PRNo { get; set; } public string WONo { get; set; } public double? Qty { get; set; } public double? Price { get; set; } public string? supplier { get; set; } public string? invoicenumber { get; set; } }
public sealed class LStatusAudit { public string ID { get; set; } public string WONo { get; set; } public int WOStatus { get; set; } public DateTime? DT { get; set; } public string? EmpNo { get; set; } }
public sealed class LJobTask { public long AutoId { get; set; } public double? SeqId { get; set; } public string? Details { get; set; } public string? Remarks { get; set; } public string WoNo { get; set; } }
#pragma warning restore CS8618

public sealed class LegacyWork
{
    public Dictionary<int, string> WorkTypes = [], Priorities = [], Statuses = [], RequestStatuses = [], Trades = [];
    public List<LFailure> FailureCodes = [];
    public List<LEmployee> Employees = [];
    public List<LRequest> Requests = [];
    public List<LWorkOrder> WorkOrders = [];
    public List<LAssign> Assignments = [];
    public List<LLabor> Labor = [];
    public List<LDirect> Direct = [];
    public List<LStatusAudit> StatusAudit = [];
    public List<LJobTask> Tasks = [];
}

public sealed class LegacyData
{
    public List<LSite> Sites = [];
    public List<LDepartment> Departments = [];
    public List<LLocation> Locations = [];
    public List<LCategory> Categories = [];
    public List<LStatus> Statuses = [];
    public List<LWarranty> Warranty = [];
    public List<LAsset> Assets = [];
    public List<LSibling> Siblings = [];
    public List<LTransfer> Transfers = [];
    public List<(string From, string To)> Relabels = [];
    public Dictionary<int, string> NameplateCaptions = [];
}

public static class Legacy
{
    public static async Task<string?> VersionAsync(SqlConnection c) =>
        await Try(() => c.QueryFirstOrDefaultAsync<string>("SELECT TOP 1 version FROM patchinfo ORDER BY id DESC"));

    public static async Task<LegacyData> LoadAsync(SqlConnection c)
    {
        var d = new LegacyData
        {
            // SELECT * keeps the importer tolerant of schema differences between 4CSQL versions.
            Sites = (await c.QueryAsync<LSite>("SELECT * FROM site")).ToList(),
            Departments = (await Try(() => c.QueryAsync<LDepartment>("SELECT * FROM department")) ?? []).ToList(),
            Locations = (await c.QueryAsync<LLocation>("SELECT * FROM location")).ToList(),
            Categories = (await c.QueryAsync<LCategory>("SELECT * FROM assetcategories")).ToList(),
            Statuses = (await c.QueryAsync<LStatus>("SELECT AssetStatusID, CAST(AssetStatusDesc AS nvarchar(200)) AS AssetStatusDesc FROM assetstatus")).ToList(),
            Warranty = (await Try(() => c.QueryAsync<LWarranty>("SELECT * FROM warrantycontract")) ?? []).ToList(),
            Assets = (await c.QueryAsync<LAsset>("SELECT * FROM assets", commandTimeout: 600)).ToList(),
            Siblings = (await Try(() => c.QueryAsync<LSibling>("SELECT AssetNo, Sibling FROM sibling")) ?? []).ToList(),
            Transfers = (await Try(() => c.QueryAsync<LTransfer>("SELECT * FROM assetstransfer")) ?? []).ToList(),
        };
        var relabels = await Try(() => c.QueryAsync<(string, string)>("SELECT OriginalText, ModifiedText FROM relable")) ?? [];
        d.Relabels = relabels.Where(r => !string.IsNullOrWhiteSpace(r.Item1)).ToList();
        // Legacy "Additional Info N" captions on the asset screen are Label6..Label25 (N = 1..20).
        var captions = await Try(() => c.QueryAsync<(string, string)>(
            "SELECT ObjectName, CAST(L1 AS nvarchar(400)) FROM ObjectCaptions WHERE FormName = 'CurrAssetForm' AND ObjectName LIKE 'Label%'")) ?? [];
        foreach (var (name, text) in captions)
            if (int.TryParse(name[5..], out var label) && label is >= 6 and <= 25 && !string.IsNullOrWhiteSpace(text) && !text.StartsWith("Additional Info", StringComparison.OrdinalIgnoreCase))
                d.NameplateCaptions[label - 5] = text.Trim();
        return d;
    }

    public static async Task<LegacyWork> LoadWorkAsync(SqlConnection c)
    {
        async Task<Dictionary<int, string>> Map(string sql) => (await Try(() => c.QueryAsync<LIdName>(sql)) ?? []).Where(x => x.Name != null).ToDictionary(x => x.Id, x => x.Name!.Trim());
        return new LegacyWork
        {
            WorkTypes = await Map("SELECT WorkTypeID AS Id, CAST(WorkTypeDescription AS nvarchar(200)) AS Name FROM worktype"),
            Priorities = await Map("SELECT WorkPriorityID AS Id, CAST(WorkPriority AS nvarchar(200)) AS Name FROM workpriority"),
            Statuses = await Map("SELECT WorkStatusID AS Id, CAST(WorkStatus AS nvarchar(200)) AS Name FROM workstatus"),
            RequestStatuses = await Map("SELECT Statusid AS Id, CAST(Description AS nvarchar(200)) AS Name FROM workrequeststatus"),
            Trades = await Map("SELECT WorkTradeID AS Id, CAST(WorkTrade AS nvarchar(200)) AS Name FROM worktrade"),
            FailureCodes = (await Try(() => c.QueryAsync<LFailure>("SELECT * FROM failurecause")) ?? []).ToList(),
            Employees = (await Try(() => c.QueryAsync<LEmployee>("SELECT * FROM employees")) ?? []).ToList(),
            Requests = (await Try(() => c.QueryAsync<LRequest>("SELECT * FROM worequest", commandTimeout: 600)) ?? []).ToList(),
            WorkOrders = (await Try(() => c.QueryAsync<LWorkOrder>("SELECT * FROM workorders", commandTimeout: 600)) ?? []).ToList(),
            Assignments = (await Try(() => c.QueryAsync<LAssign>("SELECT EmployeeNo, WorkorderNo FROM assigntoworkorder")) ?? []).ToList(),
            Labor = (await Try(() => c.QueryAsync<LLabor>("SELECT * FROM workorderlabor", commandTimeout: 600)) ?? []).ToList(),
            Direct = (await Try(() => c.QueryAsync<LDirect>("SELECT * FROM direct")) ?? []).ToList(),
            StatusAudit = (await Try(() => c.QueryAsync<LStatusAudit>("SELECT ID, WONo, WOStatus, DT, EmpNo FROM WOStatusAudit", commandTimeout: 600)) ?? []).ToList(),
            Tasks = (await Try(() => c.QueryAsync<LJobTask>("SELECT * FROM jobtask")) ?? []).ToList(),
        };
    }

    private static async Task<T?> Try<T>(Func<Task<T>> f)
    {
        try { return await f(); }
        catch (SqlException) { return default; }
    }
}
