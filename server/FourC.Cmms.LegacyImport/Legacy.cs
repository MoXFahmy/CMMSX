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
    public string? PMNo { get; set; } public string? TaskNo { get; set; } public DateTime? PMTarStartDate { get; set; }
}
public sealed class LChecklist { public string ChecklistNo { get; set; } public string? CheckListName { get; set; } public double? EstimatedLaborHours { get; set; } public string? Tasks { get; set; } public int? Active { get; set; } }
public sealed class LChecklistStep { public long ElementId { get; set; } public string ChecklistNo { get; set; } public long? SeqNo { get; set; } public string? TaskDesc { get; set; } }
public sealed class LPmSchedule
{
    public string PMNo { get; set; } public string? PMName { get; set; } public string? AssetNo { get; set; } public string? PhyLocationNo { get; set; } public string? ChecklistNo { get; set; }
    public int? PeriodDays { get; set; } public int? FreqUnits { get; set; } public int? Frequency { get; set; } public DateTime? StartDate { get; set; } public DateTime? TargetStartDate { get; set; }
    public DateTime? NextDate { get; set; } public string? TypePMgen { get; set; } public string? TypePM { get; set; } public int? PMCounter { get; set; } public int? PMActive { get; set; }
    public string? PMWOTrade { get; set; } public int? WeekofMonth { get; set; } public int? DayofWeek { get; set; } public string? SiteCode { get; set; } public int? workpriority { get; set; }
}
public sealed class LMultiplePm { public string MPMNo { get; set; } public string MTaskNo { get; set; } public int? MTaskSeq { get; set; } }
public sealed class LPmMeter
{
    public string MeterNo { get; set; } public string? MeterDescription { get; set; } public string? AssetNo { get; set; } public string? MeterType { get; set; }
    public double? Lowerlimit { get; set; } public double? UpperLimit { get; set; } public double? LastPMreading { get; set; } public string? PMLowTaskNo { get; set; }
    public string? PMUppTaskNo { get; set; } public string? PMIncTaskNo { get; set; } public string? Units { get; set; } public double? IncFreq { get; set; } public double? IncPMDue { get; set; }
    public int? FreqUnits { get; set; } public int? Frequency { get; set; } public DateTime? NextDate { get; set; } public int? PMCounter { get; set; } public int? PMActive { get; set; }
    public string? PMWOTrade { get; set; } public int? TimeBased { get; set; } public int? PMType { get; set; } public string? SiteCode { get; set; } public decimal? max_daily_reading { get; set; }
}
public sealed class LReading { public long ReadingID { get; set; } public DateTime? Date { get; set; } public string MeterNo { get; set; } public double? Reading { get; set; } }
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
    public List<LChecklist> Checklists = [];
    public List<LChecklistStep> ChecklistSteps = [];
    public List<LPmSchedule> PmSchedules = [];
    public List<LMultiplePm> MultiplePm = [];
    public List<LPmMeter> Meters = [];
    public List<LReading> Readings = [];
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

// ---------- Materials & purchasing ----------
public sealed class LSubStore { public string SubStoreCode { get; set; } public string? SubStoreDesc { get; set; } public string? SiteCode { get; set; } public int? isDefault { get; set; } }
public sealed class LStock
{
    public string StockNo { get; set; } public string? StockDescription { get; set; } public string? UOM { get; set; } public string? AttrGroup { get; set; } public string? Status { get; set; }
    public string? Location { get; set; } public double? Price2 { get; set; } public string? Notes { get; set; } public string? SiteCode { get; set; } public int? LeadTime { get; set; }
    public double? AvgPrice { get; set; } public double? StockBalance { get; set; } public double? MinLevel { get; set; } public double? MaxLevel { get; set; } public string? Specification { get; set; }
    public string? Manufacturer { get; set; } public string? SAPNo { get; set; } public string? BarcodeId { get; set; } public short? Criticality { get; set; } public short? ShelfItem { get; set; }
    public bool? allow_negative_value { get; set; } public string? supplier_no { get; set; }
}
public sealed class LStockLevel { public string stock_no { get; set; } public string? sub_store_code { get; set; } public double? max_level { get; set; } public double? re_order_level { get; set; } public double? min_level { get; set; } public int? location { get; set; } public double? reorder_qty { get; set; } }
public sealed class LSupplier
{
    public string SupplierNo { get; set; } public string? SupplierName { get; set; } public string? ContactName { get; set; } public string? Address { get; set; } public string? City { get; set; }
    public string? PostalCode { get; set; } public string? StateOrProvince { get; set; } public string? Country { get; set; } public string? PhoneNumber { get; set; } public string? FaxNumber { get; set; }
    public string? Services { get; set; } public string? AccountNo { get; set; } public short? Active { get; set; } public string? EmailAddress { get; set; }
}
public sealed class LReceive { public long ReceiveID { get; set; } public string? Stockcode { get; set; } public DateTime? Date { get; set; } public string? MRNNo { get; set; } public string? Vendor { get; set; } public double? QtyRec { get; set; } public string? PONo { get; set; } public double? Price1 { get; set; } public string? SiteCode { get; set; } public string? SubStoreCode { get; set; } public string? BatchNo { get; set; } public DateTime? BatchExpiryDate { get; set; } public string? Comments { get; set; } public string? FromSubStore { get; set; } public short? Transfer { get; set; } public DateTime? CreatedDate { get; set; } }
public sealed class LIssue { public long IssueID { get; set; } public string? StockNo { get; set; } public DateTime? DateIssue { get; set; } public string? IWTNo { get; set; } public double? QtyIssue { get; set; } public string? WONo { get; set; } public double? Price3 { get; set; } public string? SiteCode { get; set; } public string? SubStoreCode { get; set; } public string? BatchNo { get; set; } public string? ToSubStoreCode { get; set; } public short? Transfer { get; set; } public string? Comments { get; set; } public string? CreatedByEmpNo { get; set; } }
public sealed class LReturn { public long ReturnID { get; set; } public string? StockNo { get; set; } public string? IWTNo { get; set; } public double? Qty { get; set; } public DateTime? Date { get; set; } public string? Person { get; set; } public string? Comment { get; set; } public string? WONo { get; set; } public string? SiteCode { get; set; } public double? RetPrice { get; set; } public string? SubStoreCode { get; set; } public string? BatchNo { get; set; } }
public sealed class LAdjust { public long AdjustmentID { get; set; } public string? StockNo { get; set; } public DateTime? Date { get; set; } public string? Person { get; set; } public decimal? QtyAdj { get; set; } public string? Comment { get; set; } public double? Price4 { get; set; } public string? SiteCode { get; set; } public string? SubStoreCode { get; set; } public string? BatchNo { get; set; } }
public sealed class LPrMaster
{
    public string pr_no { get; set; } public DateTime? pr_date { get; set; } public DateTime? order_date { get; set; } public int? status_id { get; set; } public int? auth_status { get; set; }
    public string? order_by { get; set; } public string? req_by { get; set; } public string? del_to { get; set; } public decimal? delivery_charges { get; set; } public int? pay_term_id { get; set; }
    public string? supplier_no { get; set; } public DateTime? exp_del_date { get; set; } public string? order_notes { get; set; } public DateTime? cancel_date { get; set; } public string? cancel_notes { get; set; }
    public string? po_no { get; set; } public string? sitecode { get; set; } public string? quote_ref { get; set; } public long? ship_id { get; set; } public double? freight { get; set; } public long? currency_id { get; set; }
    public double? conv_rate { get; set; } public double? gst_percent { get; set; } public double? LumpDiscount { get; set; } public double? TaxRate { get; set; } public double? MiscValue { get; set; }
}
public sealed class LPrDetail { public string? pr_id { get; set; } public string po_req_no { get; set; } public string? part_no { get; set; } public string? part_desc { get; set; } public decimal? qty { get; set; } public decimal? unit_price { get; set; } public decimal? disc { get; set; } public string? uom { get; set; } public string? wo_no { get; set; } public decimal? received_qty { get; set; } public string? SubStoreCode { get; set; } }

public sealed class LegacyMaterials
{
    public List<LSubStore> Stores = [];
    public List<LStock> Parts = [];
    public List<LStockLevel> Levels = [];
    public List<(string Part, string Alternate)> Alternates = [];
    public List<(string Part, string Supplier)> PartSuppliers = [];
    public List<LSupplier> Suppliers = [];
    public List<LReceive> Receipts = [];
    public List<LIssue> Issues = [];
    public List<LReturn> Returns = [];
    public List<LAdjust> Adjustments = [];
    public List<LPrMaster> Orders = [];
    public List<LPrDetail> OrderLines = [];
    public Dictionary<int, string> PaymentTerms = [];
    public Dictionary<int, string> ShipVia = [];
    public Dictionary<int, string> Currencies = [];
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
            Checklists = (await Try(() => c.QueryAsync<LChecklist>("SELECT ChecklistNo, CAST(CheckListName AS nvarchar(max)) AS CheckListName, EstimatedLaborHours, CAST(Tasks AS nvarchar(max)) AS Tasks, CAST(Active AS int) AS Active FROM pmchecklist")) ?? []).ToList(),
            ChecklistSteps = (await Try(() => c.QueryAsync<LChecklistStep>("SELECT ElementId, ChecklistNo, SeqNo, TaskDesc FROM checklistelements")) ?? []).ToList(),
            PmSchedules = (await Try(() => c.QueryAsync<LPmSchedule>("SELECT PMNo, CAST(PMName AS nvarchar(max)) AS PMName, AssetNo, PhyLocationNo, ChecklistNo, PeriodDays, FreqUnits, Frequency, StartDate, TargetStartDate, NextDate, CAST(TypePMgen AS nvarchar(10)) AS TypePMgen, CAST(TypePM AS nvarchar(10)) AS TypePM, PMCounter, PMActive, PMWOTrade, WeekofMonth, DayofWeek, SiteCode, workpriority FROM pmschedule")) ?? []).ToList(),
            MultiplePm = (await Try(() => c.QueryAsync<LMultiplePm>("SELECT MPMNo, MTaskNo, MTaskSeq FROM multiplepm")) ?? []).ToList(),
            Meters = (await Try(() => c.QueryAsync<LPmMeter>("SELECT MeterNo, CAST(MeterDescription AS nvarchar(max)) AS MeterDescription, AssetNo, MeterType, Lowerlimit, UpperLimit, LastPMreading, PMLowTaskNo, PMUppTaskNo, PMIncTaskNo, Units, IncFreq, IncPMDue, FreqUnits, Frequency, NextDate, PMCounter, PMActive, PMWOTrade, TimeBased, PMType, SiteCode, max_daily_reading FROM pmmeters")) ?? []).ToList(),
            Readings = (await Try(() => c.QueryAsync<LReading>("SELECT ReadingID, Date, MeterNo, Reading FROM readings", commandTimeout: 600)) ?? []).ToList(),
        };
    }

    public static async Task<LegacyMaterials> LoadMaterialsAsync(SqlConnection c)
    {
        async Task<Dictionary<int, string>> Map(string sql) => (await Try(() => c.QueryAsync<LIdName>(sql)) ?? []).Where(x => x.Name != null).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().Name!.Trim());
        async Task<List<T>> All<T>(string sql) => (await Try(() => c.QueryAsync<T>(sql, commandTimeout: 900)) ?? []).ToList();
        return new LegacyMaterials
        {
            Stores = await All<LSubStore>("SELECT SubStoreCode, SubStoreDesc, SiteCode, CAST(isDefault AS int) AS isDefault FROM substore"),
            Parts = await All<LStock>("SELECT StockNo, StockDescription, UOM, AttrGroup, Status, Location, Price2, CAST(Notes AS nvarchar(max)) AS Notes, SiteCode, LeadTime, AvgPrice, StockBalance, MinLevel, MaxLevel, CAST(Specification AS nvarchar(max)) AS Specification, Manufacturer, SAPNo, BarcodeId, Criticality, ShelfItem, allow_negative_value, supplier_no FROM stockcode"),
            Levels = await All<LStockLevel>("SELECT stock_no, sub_store_code, max_level, re_order_level, min_level, location, reorder_qty FROM stockcode_levels"),
            Alternates = (await All<(string, string)>("SELECT StockCode, AlternativePart FROM stock_alternative")).Where(x => x.Item1 != null && x.Item2 != null).ToList(),
            PartSuppliers = (await All<(string, string)>("SELECT stock_no, supplier_no FROM stock_code_supplier")).Where(x => x.Item1 != null && x.Item2 != null).ToList(),
            Suppliers = await All<LSupplier>("SELECT SupplierNo, SupplierName, ContactName, CAST(Address AS nvarchar(max)) AS Address, City, PostalCode, StateOrProvince, Country, PhoneNumber, FaxNumber, CAST(Services AS nvarchar(max)) AS Services, AccountNo, Active, EmailAddress FROM suppliers"),
            Receipts = await All<LReceive>("SELECT ReceiveID, Stockcode, Date, MRNNo, Vendor, QtyRec, PONo, Price1, SiteCode, SubStoreCode, BatchNo, BatchExpiryDate, Comments, FromSubStore, Transfer, CreatedDate FROM receive"),
            Issues = await All<LIssue>("SELECT IssueID, StockNo, DateIssue, IWTNo, QtyIssue, WONo, Price3, SiteCode, SubStoreCode, BatchNo, ToSubStoreCode, Transfer, Comments, CreatedByEmpNo FROM issue"),
            Returns = await All<LReturn>("SELECT ReturnID, StockNo, IWTNo, Qty, Date, Person, Comment, WONo, SiteCode, RetPrice, SubStoreCode, BatchNo FROM [return]"),
            Adjustments = await All<LAdjust>("SELECT AdjustmentID, StockNo, Date, Person, QtyAdj, CAST(Comment AS nvarchar(max)) AS Comment, Price4, SiteCode, SubStoreCode, BatchNo FROM adjustment"),
            Orders = await All<LPrMaster>("SELECT pr_no, pr_date, order_date, status_id, auth_status, order_by, req_by, CAST(del_to AS nvarchar(max)) AS del_to, delivery_charges, pay_term_id, supplier_no, exp_del_date, order_notes, cancel_date, cancel_notes, po_no, sitecode, quote_ref, ship_id, freight, currency_id, conv_rate, gst_percent, LumpDiscount, TaxRate, MiscValue FROM purchase_req_master"),
            OrderLines = await All<LPrDetail>("SELECT pr_id, po_req_no, part_no, part_desc, qty, unit_price, disc, uom, wo_no, received_qty, SubStoreCode FROM purchase_req_details"),
            PaymentTerms = await Map("SELECT pay_term_id AS Id, CAST(pay_term_desc AS nvarchar(200)) AS Name FROM payment_term"),
            ShipVia = await Map("SELECT CAST(ship_id AS int) AS Id, CAST(ship_name AS nvarchar(200)) AS Name FROM ship_via"),
            Currencies = await Map("SELECT CAST(currency_id AS int) AS Id, CAST(currency_name AS nvarchar(200)) AS Name FROM currency_rate"),
        };
    }

    private static async Task<T?> Try<T>(Func<Task<T>> f)
    {
        try { return await f(); }
        catch (SqlException) { return default; }
    }
}
