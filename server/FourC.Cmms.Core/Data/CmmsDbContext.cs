using System.Linq.Expressions;
using System.Text.Json;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Auditing;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Files;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Locations;
using FourC.Cmms.Core.Notifications;
using FourC.Cmms.Core.People;
using FourC.Cmms.Core.Pm;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Purchasing;
using FourC.Cmms.Core.Work;
using FourC.Cmms.Core.Settings;
using FourC.Cmms.Core.Tenancy;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FourC.Cmms.Core.Data;

public class CmmsDbContext(DbContextOptions<CmmsDbContext> options, ITenantContext tenant)
    : IdentityUserContext<AppUser, Guid>(options), IDataProtectionKeyContext
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    /// <summary>Evaluated per query; EF parameterizes it so each context instance sees its own tenant.</summary>
    public Guid? CurrentTenantId => tenant.TenantId;
    /// <summary>Sites the current request may see; null = every site.</summary>
    public IReadOnlyList<Guid>? CurrentSiteIds => tenant.SiteIds;
    /// <summary>Location paths the current request is limited to; null = no branch limit.</summary>
    public IReadOnlyList<string>? CurrentBranchPaths => tenant.BranchPaths;

    /// <summary>The organization (+ soft delete) filter on every tenant-owned entity.</summary>
    public const string TenantFilter = "Tenant";
    /// <summary>The site and branch limits of the signed-in person (0.9.0); see <see cref="QueryScope.AllSites{T}"/>.</summary>
    public const string SiteFilter = "Site";

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<OutboundEmail> OutboundEmails => Set<OutboundEmail>();
    public DbSet<SavedView> SavedViews => Set<SavedView>();
    public DbSet<UserListSetting> UserListSettings => Set<UserListSetting>();
    public DbSet<RoleListSetting> RoleListSettings => Set<RoleListSetting>();
    public DbSet<PrintLayout> PrintLayouts => Set<PrintLayout>();
    public DbSet<UnitMovement> UnitMovements => Set<UnitMovement>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<AssetCategory> AssetCategories => Set<AssetCategory>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetTransfer> AssetTransfers => Set<AssetTransfer>();
    public DbSet<AssetPart> AssetParts => Set<AssetPart>();
    public DbSet<DowntimeEvent> DowntimeEvents => Set<DowntimeEvent>();
    public DbSet<CustomFieldDefinition> CustomFields => Set<CustomFieldDefinition>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<FailureCode> FailureCodes => Set<FailureCode>();
    public DbSet<Symptom> Symptoms => Set<Symptom>();
    public DbSet<Remedy> Remedies => Set<Remedy>();
    public DbSet<WorkRequest> WorkRequests => Set<WorkRequest>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderAssignee> WorkOrderAssignees => Set<WorkOrderAssignee>();
    public DbSet<WorkOrderTask> WorkOrderTasks => Set<WorkOrderTask>();
    public DbSet<WorkOrderLabor> WorkOrderLabor => Set<WorkOrderLabor>();
    public DbSet<WorkOrderPart> WorkOrderParts => Set<WorkOrderPart>();
    public DbSet<WorkOrderCost> WorkOrderCosts => Set<WorkOrderCost>();
    public DbSet<WorkOrderComment> WorkOrderComments => Set<WorkOrderComment>();
    public DbSet<RequestType> RequestTypes => Set<RequestType>();
    public DbSet<CancelReason> CancelReasons => Set<CancelReason>();
    public DbSet<Costs.CostCentre> CostCentres => Set<Costs.CostCentre>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<BrandModel> BrandModels => Set<BrandModel>();
    public DbSet<AssetGroup> AssetGroups => Set<AssetGroup>();
    public DbSet<AssetGroupMember> AssetGroupMembers => Set<AssetGroupMember>();
    public DbSet<Inventory.Unit> Units => Set<Inventory.Unit>();
    public DbSet<Costs.Budget> Budgets => Set<Costs.Budget>();
    public DbSet<Planning.AvailabilityType> AvailabilityTypes => Set<Planning.AvailabilityType>();
    public DbSet<Planning.AvailabilityEntry> AvailabilityEntries => Set<Planning.AvailabilityEntry>();
    public DbSet<WorkRequestComment> WorkRequestComments => Set<WorkRequestComment>();
    public DbSet<RequestFeedback> RequestFeedback => Set<RequestFeedback>();
    public DbSet<FourC.Cmms.Core.Ai.AiCall> AiCalls => Set<FourC.Cmms.Core.Ai.AiCall>();
    public DbSet<FourC.Cmms.Core.Ai.AiDiagnosis> AiDiagnoses => Set<FourC.Cmms.Core.Ai.AiDiagnosis>();
    public DbSet<FourC.Cmms.Core.Ai.AiDiagnosisVote> AiDiagnosisVotes => Set<FourC.Cmms.Core.Ai.AiDiagnosisVote>();
    public DbSet<StockCount> StockCounts => Set<StockCount>();
    public DbSet<StockCountLine> StockCountLines => Set<StockCountLine>();
    public DbSet<PushMessage> PushMessages => Set<PushMessage>();
    public DbSet<WorkOrderStatusChange> WorkOrderStatusChanges => Set<WorkOrderStatusChange>();
    public DbSet<PmTask> PmTasks => Set<PmTask>();
    public DbSet<PmTaskStep> PmTaskSteps => Set<PmTaskStep>();
    public DbSet<PmTaskPart> PmTaskParts => Set<PmTaskPart>();
    public DbSet<PmSchedule> PmSchedules => Set<PmSchedule>();
    public DbSet<PmScheduleTask> PmScheduleTasks => Set<PmScheduleTask>();
    public DbSet<PmScheduleAsset> PmScheduleAssets => Set<PmScheduleAsset>();
    public DbSet<ScheduleCalendar> ScheduleCalendars => Set<ScheduleCalendar>();
    public DbSet<PmScheduleCalendar> PmScheduleCalendars => Set<PmScheduleCalendar>();
    public DbSet<PmScheduleMeter> PmScheduleMeters => Set<PmScheduleMeter>();
    public DbSet<WorkOrderGroup> WorkOrderGroups => Set<WorkOrderGroup>();
    public DbSet<WorkOrderGroupComment> WorkOrderGroupComments => Set<WorkOrderGroupComment>();
    public DbSet<AssetChecklist> AssetChecklists => Set<AssetChecklist>();
    public DbSet<MeasurementPoint> MeasurementPoints => Set<MeasurementPoint>();
    public DbSet<MeasurementPointAsset> MeasurementPointAssets => Set<MeasurementPointAsset>();
    public DbSet<MeasurementReading> MeasurementReadings => Set<MeasurementReading>();
    public DbSet<WorkOrderAsset> WorkOrderAssets => Set<WorkOrderAsset>();
    public DbSet<Meter> Meters => Set<Meter>();
    public DbSet<MeterReading> MeterReadings => Set<MeterReading>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<PartGroup> PartGroups => Set<PartGroup>();
    public DbSet<Part> Parts => Set<Part>();
    public DbSet<StockLevel> StockLevels => Set<StockLevel>();
    /// <summary>Stock reserved for open work per part and store (a view; read-only).</summary>
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<TechAction> TechActions => Set<TechAction>();
    public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<PurchaseRequest> PurchaseRequests => Set<PurchaseRequest>();
    public DbSet<PurchaseRequestLine> PurchaseRequestLines => Set<PurchaseRequestLine>();
    public DbSet<PurchaseRequestComment> PurchaseRequestComments => Set<PurchaseRequestComment>();
    public DbSet<OrderType> OrderTypes => Set<OrderType>();
    public DbSet<ApprovalLevel> ApprovalLevels => Set<ApprovalLevel>();
    public DbSet<PurchaseOrderApprovalStep> PurchaseOrderApprovalSteps => Set<PurchaseOrderApprovalStep>();
    public DbSet<PurchaseOrderSignature> PurchaseOrderSignatures => Set<PurchaseOrderSignature>();
    // Contracts and contractors (0.17.0)
    public DbSet<SupplierContact> SupplierContacts => Set<SupplierContact>();
    public DbSet<SupplierAddress> SupplierAddresses => Set<SupplierAddress>();
    public DbSet<ContractorCategory> ContractorCategories => Set<ContractorCategory>();
    public DbSet<SupplierCategoryRate> SupplierCategoryRates => Set<SupplierCategoryRate>();
    public DbSet<WorkOrderContractor> WorkOrderContractors => Set<WorkOrderContractor>();
    public DbSet<SupplierPart> SupplierParts => Set<SupplierPart>();
    public DbSet<FourC.Cmms.Core.Contracts.Contract> Contracts => Set<FourC.Cmms.Core.Contracts.Contract>();
    public DbSet<FourC.Cmms.Core.Contracts.ContractPeriod> ContractPeriods => Set<FourC.Cmms.Core.Contracts.ContractPeriod>();
    public DbSet<FourC.Cmms.Core.Contracts.ContractAmendment> ContractAmendments => Set<FourC.Cmms.Core.Contracts.ContractAmendment>();
    public DbSet<FourC.Cmms.Core.Contracts.ContractAsset> ContractAssets => Set<FourC.Cmms.Core.Contracts.ContractAsset>();
    public DbSet<FourC.Cmms.Core.Contracts.ContractType> ContractTypes => Set<FourC.Cmms.Core.Contracts.ContractType>();

    /// <summary>ASP.NET Core data-protection key ring (sign-in cookies), shared by every app instance.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Tenant>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Slug).HasMaxLength(100);
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Settings).HasConversion(JsonConverter<TenantSettings>()).Metadata.SetValueComparer(JsonComparer<TenantSettings>());
        });

        b.Entity<AppUser>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.JobTitle).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(50);
            e.Property(x => x.Language).HasMaxLength(5);
            e.Property(x => x.LegacyRef).HasMaxLength(200);
            e.HasIndex(x => x.TenantId);
            e.HasMany(x => x.Roles).WithOne().HasForeignKey(x => x.UserId);
            e.Property(x => x.EmailOptOuts).HasDefaultValueSql("'[]'");
            e.Property(x => x.SiteIds).HasDefaultValueSql("'[]'");
            e.Property(x => x.LocationIds).HasDefaultValueSql("'[]'");
        });

        b.Entity<SavedView>(e =>
        {
            e.Property(x => x.List).HasMaxLength(40);
            e.Property(x => x.Name).HasMaxLength(60);
            e.Property(x => x.Query).HasMaxLength(2000);
            e.Property(x => x.HiddenColumns).HasDefaultValueSql("'[]'");
            e.HasIndex(x => new { x.UserId, x.List, x.Name }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.PinnedToHome });
            e.HasIndex(x => x.TenantId);
        });

        b.Entity<UserListSetting>(e =>
        {
            e.HasKey(x => new { x.UserId, x.List });
            e.Property(x => x.List).HasMaxLength(40);
            e.Property(x => x.HiddenColumns).HasDefaultValueSql("'[]'");
            e.HasIndex(x => x.TenantId);
        });

        b.Entity<RoleListSetting>(e =>
        {
            e.HasKey(x => new { x.RoleId, x.List });
            e.Property(x => x.List).HasMaxLength(40);
            e.Property(x => x.HiddenColumns).HasDefaultValueSql("'[]'");
            e.HasIndex(x => x.TenantId);
            e.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<OutboundEmail>(e =>
        {
            e.Property(x => x.EventKey).HasMaxLength(40);
            e.Property(x => x.ToAddress).HasMaxLength(320);
            e.Property(x => x.ToName).HasMaxLength(200);
            e.Property(x => x.FromName).HasMaxLength(200);
            e.Property(x => x.ReplyTo).HasMaxLength(320);
            e.Property(x => x.Subject).HasMaxLength(300);
            e.Property(x => x.EntityType).HasMaxLength(40);
            e.Property(x => x.LastError).HasMaxLength(1000);
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.HasIndex(x => new { x.TenantId, x.CreatedAt });
        });

        b.Entity<Role>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            e.Property(x => x.HomeLayout).HasConversion(NullableJsonConverter<List<HomeCard>>()).Metadata.SetValueComparer(NullableJsonComparer<List<HomeCard>>());
            e.Property(x => x.HiddenModules).HasDefaultValueSql("'[]'");
        });
        b.Entity<UserRole>(e =>
        {
            e.HasKey(x => new { x.UserId, x.RoleId });
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Site>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.Country).HasMaxLength(100);
            e.Property(x => x.TimeZone).HasMaxLength(100);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
            e.Property(x => x.Settings).HasConversion(JsonConverter<SiteSettings>()).HasDefaultValueSql("'{}'").Metadata.SetValueComparer(JsonComparer<SiteSettings>());
        });

        b.Entity<Location>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(100);
            e.Property(x => x.Name).HasMaxLength(300);
            e.Property(x => x.Path).HasMaxLength(800).IsUnicode(false);
            e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.SiteId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasIndex(x => new { x.TenantId, x.Path });
            e.HasIndex(x => new { x.TenantId, x.ParentId });
        });

        b.Entity<AssetCategory>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Color).HasMaxLength(20);
            e.Property(x => x.DowntimeCostPerHour).HasPrecision(18, 2);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        });

        b.Entity<Asset>(e =>
        {
            ConfigureTenantEntity(e);
            // Serial units (0.24.0): the part a unit belongs to, its store while in stock, one serial per part among live units.
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.PartId, x.SerialNumber }).IsUnique().HasFilter("[PartId] IS NOT NULL AND [IsDeleted] = 0");
            e.Property(x => x.AssetNumber).HasMaxLength(100);
            e.Property(x => x.Name).HasMaxLength(300);
            e.Property(x => x.Manufacturer).HasMaxLength(200);
            e.Property(x => x.Model).HasMaxLength(200);
            e.Property(x => x.SerialNumber).HasMaxLength(200);
            e.Property(x => x.Barcode).HasMaxLength(200);
            e.Property(x => x.OperationNumber).HasMaxLength(50);
            foreach (var p in new Expression<Func<Asset, decimal?>>[] { x => x.PurchasePrice, x => x.CurrentValue, x => x.SalvageValue, x => x.DowntimeCostPerHour })
                e.Property(p).HasPrecision(18, 2);
            e.Property(x => x.ExpectedLifeYears).HasPrecision(9, 2);
            e.Property(x => x.CustomFields).HasConversion(JsonConverter<Dictionary<string, string?>>()).Metadata.SetValueComparer(JsonComparer<Dictionary<string, string?>>());
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ParentAsset).WithMany().HasForeignKey(x => x.ParentAssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.AssetNumber }).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasIndex(x => new { x.TenantId, x.Barcode });
            e.HasIndex(x => new { x.TenantId, x.LocationId });
            e.HasIndex(x => new { x.TenantId, x.ParentAssetId });
            e.Property(x => x.RunsHoursPerDay).HasPrecision(4, 2);
            e.Property(x => x.RunsOn).HasMaxLength(200).HasConversion(JsonConverter<List<DayOfWeek>>()).Metadata.SetValueComparer(JsonComparer<List<DayOfWeek>>());
        });

        b.Entity<AssetPart>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Quantity).HasPrecision(18, 3);
            e.Property(x => x.Note).HasMaxLength(200);
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Part).WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.AssetId, x.PartId }).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasIndex(x => new { x.TenantId, x.PartId });
        });

        b.Entity<DowntimeEvent>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.SourceNumber).HasMaxLength(50);
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            // One open stop per asset.
            e.HasIndex(x => new { x.TenantId, x.AssetId }).IsUnique().HasFilter("[EndedAt] IS NULL AND [IsDeleted] = 0").HasDatabaseName("IX_DowntimeEvents_OpenPerAsset");
            e.HasIndex(x => new { x.TenantId, x.AssetId, x.StartedAt });
        });

        b.Entity<AssetTransfer>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.AssetId });
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.Property(x => x.LegacyRef).HasMaxLength(200);
        });

        b.Entity<CustomFieldDefinition>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.EntityType).HasMaxLength(50);
            e.Property(x => x.Key).HasMaxLength(100);
            e.Property(x => x.Label).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.EntityType, x.Key }).IsUnique().HasFilter("[IsDeleted] = 0");
        });

        b.Entity<AuditEntry>(e =>
        {
            e.Property(x => x.EntityType).HasMaxLength(100);
            e.Property(x => x.Action).HasMaxLength(20);
            e.Property(x => x.UserName).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId, x.Timestamp });
            e.Property(x => x.Source).HasMaxLength(10);
            e.Property(x => x.ParentType).HasMaxLength(100);
            e.HasIndex(x => new { x.TenantId, x.Timestamp });
            e.HasIndex(x => new { x.TenantId, x.ChangeId });
            e.HasIndex(x => new { x.TenantId, x.ParentType, x.ParentId });
        });

        b.Entity<Attachment>(e =>
        {
            e.Property(x => x.EntityType).HasMaxLength(50);
            e.Property(x => x.FileName).HasMaxLength(260);
            e.Property(x => x.ContentType).HasMaxLength(200);
            e.Property(x => x.SignedBy).HasMaxLength(200);
            e.Property(x => x.StorageKey).HasMaxLength(400);
            e.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId });
            e.HasIndex(x => x.WorkOrderTaskId).HasFilter("[WorkOrderTaskId] IS NOT NULL");
        });

        b.Entity<NumberSequence>(e =>
        {
            e.HasKey(x => new { x.TenantId, x.Name });
            e.Property(x => x.Name).HasMaxLength(100); // 0.21.0: room for per-site yearly sequences
        });

        b.Entity<Trade>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Color).HasMaxLength(20);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
        b.Entity<FailureCode>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
        b.Entity<Symptom>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
        b.Entity<Remedy>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
        b.Entity<Employee>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.FullName).HasMaxLength(200); e.Property(x => x.JobTitle).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(256); e.Property(x => x.Phone).HasMaxLength(50); e.Property(x => x.Company).HasMaxLength(200);
            e.Property(x => x.HourlyRate).HasPrecision(18, 2); e.Property(x => x.OvertimeRate).HasPrecision(18, 2);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasIndex(x => new { x.TenantId, x.UserId });
            e.Property(x => x.WorkingHours).HasConversion(NullableJsonConverter<List<WorkingDay>>()).Metadata.SetValueComparer(NullableJsonComparer<List<WorkingDay>>());
            e.HasOne(x => x.CompanyRecord).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        });
        // Cost centres and budgets (0.20.0).
        b.Entity<Costs.CostCentre>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(200);
            e.HasOne<Costs.CostCentre>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
        b.Entity<Costs.Budget>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasOne<Costs.CostCentre>().WithMany().HasForeignKey(x => x.CostCentreId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.TenantId, x.CostCentreId, x.Year, x.Month }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
        // Brands and models, asset groups and units (0.21.0).
        b.Entity<Brand>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasMany(x => x.Models).WithOne().HasForeignKey(x => x.BrandId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
        b.Entity<BrandModel>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.BrandId, x.Name }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
        b.Entity<Asset>().HasOne<Brand>().WithMany().HasForeignKey(x => x.BrandId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<Asset>().HasOne<BrandModel>().WithMany().HasForeignKey(x => x.ModelId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<AssetGroup>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(200);
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Members).WithOne().HasForeignKey(x => x.AssetGroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
        b.Entity<AssetGroupMember>(e =>
        {
            e.HasKey(x => new { x.AssetGroupId, x.AssetId });
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.AssetId);
        });
        b.Entity<PmSchedule>().HasOne<AssetGroup>().WithMany().HasForeignKey(x => x.AssetGroupId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<Inventory.Unit>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Symbol).HasMaxLength(30).UseCollation("Latin1_General_100_CS_AS");
            e.Property(x => x.Name).HasMaxLength(100); e.Property(x => x.NameAr).HasMaxLength(100);
            e.HasIndex(x => new { x.TenantId, x.Symbol }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
        // What carries a cost centre (0.20.0).
        b.Entity<Location>().HasOne<Costs.CostCentre>().WithMany().HasForeignKey(x => x.CostCentreId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<Asset>().HasOne<Costs.CostCentre>().WithMany().HasForeignKey(x => x.CostCentreId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<WorkRequest>().HasOne<Costs.CostCentre>().WithMany().HasForeignKey(x => x.CostCentreId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<WorkOrder>().HasOne<Costs.CostCentre>().WithMany().HasForeignKey(x => x.CostCentreId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<PmSchedule>().HasOne<Costs.CostCentre>().WithMany().HasForeignKey(x => x.CostCentreId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<StockTransaction>().HasOne<Costs.CostCentre>().WithMany().HasForeignKey(x => x.CostCentreId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<UnitMovement>(e =>
        {
            e.Property(x => x.Note).HasMaxLength(500); e.Property(x => x.ByName).HasMaxLength(200);
            e.Property(x => x.Cost).HasPrecision(18, 4);
            e.HasIndex(x => new { x.UnitId, x.At });
            e.HasIndex(x => x.TenantId);
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PrintLayout>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Paper).HasMaxLength(20); e.Property(x => x.Name).HasMaxLength(60); e.Property(x => x.KindKey).HasMaxLength(60);
            e.Property(x => x.PaperSize).HasMaxLength(10); e.Property(x => x.LabelSize).HasMaxLength(20);
            e.Property(x => x.HeaderText).HasMaxLength(500); e.Property(x => x.HeaderTextAr).HasMaxLength(500);
            e.Property(x => x.FooterText).HasMaxLength(500); e.Property(x => x.FooterTextAr).HasMaxLength(500);
            e.Property(x => x.Terms).HasMaxLength(4000); e.Property(x => x.TermsAr).HasMaxLength(4000);
            e.Property(x => x.Blocks).HasConversion(JsonConverter<List<PrintBlock>>()).HasDefaultValueSql("'[]'").Metadata.SetValueComparer(JsonComparer<List<PrintBlock>>());
            e.Property(x => x.LabelFields).HasDefaultValueSql("'[]'");
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.Paper, x.Name }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
        b.Entity<Team>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Color).HasMaxLength(20);
            e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
        b.Entity<WorkRequest>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.RequesterName).HasMaxLength(200); e.Property(x => x.RequesterEmail).HasMaxLength(256); e.Property(x => x.RequesterPhone).HasMaxLength(50);
            e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Symptom).WithMany().HasForeignKey(x => x.SymptomId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasOne<DowntimeEvent>().WithMany().HasForeignKey(x => x.DowntimeEventId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RequestType).WithMany().HasForeignKey(x => x.RequestTypeId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.ExtraRecipientIds).HasDefaultValueSql("'[]'");
            e.Property(x => x.AccessWindows).HasConversion(JsonConverter<List<AccessWindow>>()).HasDefaultValueSql("'[]'").Metadata.SetValueComparer(JsonComparer<List<AccessWindow>>());
            e.Property(x => x.FeedbackTokenHash).HasMaxLength(64).IsUnicode(false);
            e.HasIndex(x => x.FeedbackTokenHash).HasFilter("[FeedbackTokenHash] IS NOT NULL");
        });
        b.Entity<WorkOrder>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.EstimatedHours).HasPrecision(9, 2); e.Property(x => x.DowntimeHours).HasPrecision(9, 2); e.Property(x => x.RequestedBy).HasMaxLength(200);
            e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Trade).WithMany().HasForeignKey(x => x.TradeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.FailureCode).WithMany().HasForeignKey(x => x.FailureCodeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Symptom).WithMany().HasForeignKey(x => x.SymptomId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Remedy).WithMany().HasForeignKey(x => x.RemedyId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Assignees).WithOne().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Assets).WithOne().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasIndex(x => new { x.TenantId, x.Status, x.DueDate });
            e.HasIndex(x => new { x.TenantId, x.AssetId });
            e.HasOne<DowntimeEvent>().WithMany().HasForeignKey(x => x.DowntimeEventId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CancelReason).WithMany().HasForeignKey(x => x.CancelReasonId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.AccessWindows).HasConversion(JsonConverter<List<AccessWindow>>()).HasDefaultValueSql("'[]'").Metadata.SetValueComparer(JsonComparer<List<AccessWindow>>());
        });
        b.Entity<WorkOrderAssignee>(e =>
        {
            e.HasKey(x => new { x.WorkOrderId, x.EmployeeId });
            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.PlannedHours).HasPrecision(9, 2);
        });
        b.Entity<WorkOrderPart>(e =>
        {
            e.Property(x => x.Quantity).HasPrecision(18, 3); e.Property(x => x.Notes).HasMaxLength(500);
            e.HasOne(x => x.Part).WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.WorkOrderId, x.PartId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.PartId, x.StoreId });
        });
        b.Entity<WorkOrderTask>(e =>
        {
            e.Property(x => x.Description).HasMaxLength(1000); e.Property(x => x.DoneBy).HasMaxLength(200); e.HasIndex(x => new { x.TenantId, x.WorkOrderId });
            e.Property(x => x.Reading).HasMaxLength(100); e.Property(x => x.Remarks).HasMaxLength(2000); e.Property(x => x.LegacyRef).HasMaxLength(200);
            e.Property(x => x.UpdatedByName).HasMaxLength(200);
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Unit).HasMaxLength(20); e.Property(x => x.TextValue).HasMaxLength(1000); e.Property(x => x.Value).HasPrecision(18, 4);
            e.HasIndex(x => new { x.TenantId, x.LegacyRef }).HasFilter("[LegacyRef] IS NOT NULL");
        });
        b.Entity<WorkOrderAsset>(e =>
        {
            e.HasKey(x => new { x.WorkOrderId, x.AssetId });
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AssetId, x.WorkOrderId });
        });
        b.Entity<WorkOrderLabor>(e =>
        {
            e.Property(x => x.Hours).HasPrecision(9, 2); e.Property(x => x.OvertimeHours).HasPrecision(9, 2);
            e.Property(x => x.Rate).HasPrecision(18, 2); e.Property(x => x.OvertimeRate).HasPrecision(18, 2); e.Property(x => x.LegacyRef).HasMaxLength(200);
            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.WorkOrderId });
        });
        b.Entity<WorkOrderCost>(e =>
        {
            e.Property(x => x.Description).HasMaxLength(500); e.Property(x => x.Vendor).HasMaxLength(200); e.Property(x => x.Reference).HasMaxLength(100);
            e.Property(x => x.Quantity).HasPrecision(18, 3); e.Property(x => x.UnitCost).HasPrecision(18, 2);
            e.HasIndex(x => new { x.TenantId, x.WorkOrderId });
        });
        b.Entity<WorkOrderComment>(e => { e.Property(x => x.UserName).HasMaxLength(200); e.HasIndex(x => new { x.TenantId, x.WorkOrderId }); });
        b.Entity<RequestType>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.RecipientIds).HasDefaultValueSql("'[]'");
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasOne<Trade>().WithMany().HasForeignKey(x => x.DefaultTradeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
        });
        // Time away and extra time (0.19.0).
        b.Entity<Planning.AvailabilityType>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Color).HasMaxLength(20);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
        b.Entity<Planning.AvailabilityEntry>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Days).HasMaxLength(200).HasConversion(JsonConverter<List<DayOfWeek>>()).Metadata.SetValueComparer(JsonComparer<List<DayOfWeek>>());
            e.Property(x => x.Hours).HasPrecision(5, 2); e.Property(x => x.Notes).HasMaxLength(1000);
            e.HasOne(x => x.Type).WithMany().HasForeignKey(x => x.TypeId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.TenantId, x.From, x.To });
        });
        b.Entity<CancelReason>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
        b.Entity<WorkRequestComment>(e =>
        {
            e.Property(x => x.Body).HasMaxLength(4000); e.Property(x => x.UserName).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.WorkRequestId });
            e.HasOne<WorkRequest>().WithMany().HasForeignKey(x => x.WorkRequestId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<StockCount>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.StockCountId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TenantId, x.StoreId, x.Status });
        });
        b.Entity<StockCountLine>(e =>
        {
            e.Property(x => x.CountedQuantity).HasPrecision(18, 3); e.Property(x => x.CountedByName).HasMaxLength(200);
            e.HasOne(x => x.Part).WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.StockCountId, x.PartId }).IsUnique();
        });
        b.Entity<PushMessage>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200); e.Property(x => x.Body).HasMaxLength(1000); e.Property(x => x.Link).HasMaxLength(300);
            e.Property(x => x.EntityType).HasMaxLength(50); e.Property(x => x.LastError).HasMaxLength(2000);
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
        });
        // The AI assistant (0.15.0): the call log, kept diagnoses and their votes.
        b.Entity<FourC.Cmms.Core.Ai.AiCall>(e =>
        {
            e.Property(x => x.UserName).HasMaxLength(200); e.Property(x => x.Model).HasMaxLength(100);
            e.Property(x => x.Feature).HasConversion<string>().HasMaxLength(20); e.Property(x => x.Result).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.EstimatedCost).HasPrecision(18, 6);
            e.HasIndex(x => new { x.TenantId, x.CreatedAt });
        });
        b.Entity<FourC.Cmms.Core.Ai.AiDiagnosis>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.CreatedByName).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.WorkOrderId });
            e.HasIndex(x => new { x.TenantId, x.WorkRequestId });
        });
        b.Entity<FourC.Cmms.Core.Ai.AiDiagnosisVote>(e =>
        {
            e.HasIndex(x => new { x.DiagnosisId, x.UserId }).IsUnique();
            e.HasOne<FourC.Cmms.Core.Ai.AiDiagnosis>().WithMany().HasForeignKey(x => x.DiagnosisId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<RequestFeedback>(e =>
        {
            e.ToTable("RequestFeedback");
            e.Property(x => x.Comment).HasMaxLength(2000); e.Property(x => x.ByName).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.WorkRequestId });
            e.HasIndex(x => new { x.TenantId, x.AnsweredAt });
            e.HasOne<WorkRequest>().WithMany().HasForeignKey(x => x.WorkRequestId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<WorkOrderStatusChange>(e =>
        {
            e.Property(x => x.Note).HasMaxLength(2000); e.Property(x => x.ChangedByName).HasMaxLength(200); e.Property(x => x.LegacyRef).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.WorkOrderId, x.ChangedAt });
        });

        b.Entity<PmTask>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(300); e.Property(x => x.EstimatedHours).HasPrecision(9, 2);
            e.Property(x => x.Title).HasMaxLength(300); e.Property(x => x.ExpectedStopHours).HasPrecision(9, 2);
            e.Property(x => x.WorkType).HasDefaultValue(WorkType.Preventive);
            e.Property(x => x.RecipientIds).HasDefaultValueSql("'[]'");
            e.HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.PmTaskId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Parts).WithOne().HasForeignKey(x => x.PmTaskId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
        b.Entity<PmTaskStep>(e =>
        {
            e.Property(x => x.Description).HasMaxLength(2000); e.Property(x => x.Unit).HasMaxLength(20);
            e.HasOne<MeasurementPoint>().WithMany().HasForeignKey(x => x.MeasurementPointId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.MeasurementPointId).HasFilter("[MeasurementPointId] IS NOT NULL");
        });
        b.Entity<MeasurementPoint>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Unit).HasMaxLength(20);
            foreach (var p in new[] { "ValidMin", "ValidMax", "AlarmLow", "AlertLow", "Target", "AlertHigh", "AlarmHigh" }) e.Property(p).HasPrecision(18, 4);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasMany(x => x.Assets).WithOne().HasForeignKey(x => x.PointId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<MeasurementPointAsset>(e =>
        {
            e.HasKey(x => new { x.PointId, x.AssetId });
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.AssetId);
        });
        b.Entity<MeasurementReading>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Value).HasPrecision(18, 4); e.Property(x => x.Note).HasMaxLength(1000); e.Property(x => x.ByName).HasMaxLength(200);
            e.HasOne<MeasurementPoint>().WithMany().HasForeignKey(x => x.PointId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.PointId, x.AssetId, x.ReadAt });
            e.HasIndex(x => new { x.TenantId, x.WorkOrderTaskId }).HasFilter("[WorkOrderTaskId] IS NOT NULL");
            e.HasIndex(x => new { x.TenantId, x.ReadAt, x.Level });
        });
        b.Entity<PmTaskPart>(e =>
        {
            e.Property(x => x.Quantity).HasPrecision(18, 3);
            e.HasOne(x => x.Part).WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PmTaskId, x.PartId }).IsUnique();
        });
        b.Entity<PmSchedule>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(300);
            foreach (var p in new System.Linq.Expressions.Expression<Func<PmSchedule, decimal?>>[] { x => x.MeterInterval, x => x.NextDueReading, x => x.LowerLimit, x => x.UpperLimit })
                e.Property(p).HasPrecision(18, 3);
            e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PmTask).WithMany().HasForeignKey(x => x.PmTaskId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Meter).WithMany().HasForeignKey(x => x.MeterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.ExtraTasks).WithOne().HasForeignKey(x => x.PmScheduleId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Assets).WithOne().HasForeignKey(x => x.PmScheduleId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Calendars).WithOne().HasForeignKey(x => x.PmScheduleId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.ExtraMeters).WithOne().HasForeignKey(x => x.PmScheduleId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.RecipientIds).HasDefaultValueSql("'[]'");
            e.Property(x => x.MultiAssetMode).HasDefaultValue(MultiAssetMode.Round);
            e.Property(x => x.GroupPlan).HasDefaultValue(GroupPlan.Copy);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasIndex(x => new { x.TenantId, x.IsActive, x.NextDueDate });
            e.HasIndex(x => new { x.TenantId, x.AssetId });
        });
        b.Entity<PmScheduleTask>(e => e.HasKey(x => new { x.PmScheduleId, x.PmTaskId }));
        b.Entity<PmScheduleCalendar>(e =>
        {
            e.HasKey(x => new { x.PmScheduleId, x.ScheduleCalendarId });
            e.HasOne<ScheduleCalendar>().WithMany().HasForeignKey(x => x.ScheduleCalendarId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ScheduleCalendarId);
        });
        b.Entity<PmScheduleMeter>(e =>
        {
            e.HasKey(x => new { x.PmScheduleId, x.MeterId });
            e.Property(x => x.Interval).HasPrecision(18, 3); e.Property(x => x.NextDueReading).HasPrecision(18, 3);
            e.HasOne<Meter>().WithMany().HasForeignKey(x => x.MeterId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.MeterId);
        });
        b.Entity<ScheduleCalendar>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Dates).HasDefaultValueSql("'[]'");
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
        b.Entity<WorkOrderGroup>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.Title).HasMaxLength(300);
            e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PmSchedule>().WithMany().HasForeignKey(x => x.PmScheduleId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasIndex(x => new { x.TenantId, x.PmScheduleId, x.PmDueDate }).IsUnique().HasFilter("[PmScheduleId] IS NOT NULL AND [PmDueDate] IS NOT NULL AND [IsDeleted] = 0");
        });
        b.Entity<WorkOrderGroupComment>(e =>
        {
            e.Property(x => x.Body).HasMaxLength(4000); e.Property(x => x.UserName).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.GroupId });
            e.HasOne<WorkOrderGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AssetChecklist>(e =>
        {
            ConfigureTenantEntity(e);
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AssetCategory>().WithMany().HasForeignKey(x => x.AssetCategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PmTask>().WithMany().HasForeignKey(x => x.PmTaskId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.AssetId, x.PmTaskId }).IsUnique().HasFilter("[AssetId] IS NOT NULL AND [IsDeleted] = 0");
            e.HasIndex(x => new { x.TenantId, x.AssetCategoryId, x.PmTaskId }).IsUnique().HasFilter("[AssetCategoryId] IS NOT NULL AND [IsDeleted] = 0");
            e.ToTable(t => t.HasCheckConstraint("CK_AssetChecklists_Target", "([AssetId] IS NULL AND [AssetCategoryId] IS NOT NULL) OR ([AssetId] IS NOT NULL AND [AssetCategoryId] IS NULL)"));
        });
        b.Entity<PmScheduleAsset>(e =>
        {
            e.HasKey(x => new { x.PmScheduleId, x.AssetId });
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.AssetId);
            e.HasOne<PmTask>().WithMany().HasForeignKey(x => x.PmTaskId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Meter>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Unit).HasMaxLength(30);
            e.Property(x => x.CurrentReading).HasPrecision(18, 3); e.Property(x => x.MaxDailyIncrement).HasPrecision(18, 3);
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Meter>().WithMany().HasForeignKey(x => x.SourceMeterId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
        b.Entity<MeterReading>(e =>
        {
            e.Property(x => x.Value).HasPrecision(18, 3); e.Property(x => x.Note).HasMaxLength(1000); e.Property(x => x.ByName).HasMaxLength(200); e.Property(x => x.LegacyRef).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.MeterId, x.ReadAt });
        });
        b.Entity<WorkOrder>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.PmScheduleId, x.PmDueDate }).IsUnique().HasFilter("[PmScheduleId] IS NOT NULL AND [PmDueDate] IS NOT NULL AND [GroupId] IS NULL AND [IsDeleted] = 0");
            e.Property(x => x.RecipientIds).HasDefaultValueSql("'[]'");
            e.HasOne<WorkOrderGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.GroupId }).HasFilter("[GroupId] IS NOT NULL");
        });

        b.Entity<AppUser>(e => e.Property(x => x.PurchaseApprovalLimit).HasPrecision(18, 2));
        b.Entity<Store>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(200);
            e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
        b.Entity<PartGroup>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
        b.Entity<Part>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.PartNumber).HasMaxLength(100); e.Property(x => x.Name).HasMaxLength(300); e.Property(x => x.Manufacturer).HasMaxLength(200);
            e.Property(x => x.ManufacturerPartNumber).HasMaxLength(100); e.Property(x => x.UnitOfMeasure).HasMaxLength(30); e.Property(x => x.Barcode).HasMaxLength(200);
            e.Property(x => x.StandardPrice).HasPrecision(18, 4); e.Property(x => x.AverageCost).HasPrecision(18, 4); e.Property(x => x.LastPurchasePrice).HasPrecision(18, 4);
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.PartNumber }).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasIndex(x => new { x.TenantId, x.Barcode });
        });
        b.Entity<StockLevel>(e =>
        {
            e.Property(x => x.Bin).HasMaxLength(100); e.Property(x => x.LegacyRef).HasMaxLength(200);
            foreach (var p in new System.Linq.Expressions.Expression<Func<StockLevel, decimal>>[] { x => x.MinLevel, x => x.ReorderLevel, x => x.MaxLevel, x => x.ReorderQuantity, x => x.QuantityOnHand })
                e.Property(p).HasPrecision(18, 3);
            e.HasOne(x => x.Part).WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.PartId, x.StoreId }).IsUnique();
        });
        b.Entity<StockTransaction>(e =>
        {
            e.Property(x => x.Quantity).HasPrecision(18, 3); e.Property(x => x.UnitCost).HasPrecision(18, 4);
            e.Property(x => x.Reference).HasMaxLength(100); e.Property(x => x.BatchNumber).HasMaxLength(100); e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.CreatedByName).HasMaxLength(200); e.Property(x => x.LegacyRef).HasMaxLength(200);
            e.HasOne(x => x.Part).WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.PartId, x.Date });
            e.HasIndex(x => new { x.TenantId, x.WorkOrderId });
            e.HasIndex(x => new { x.TenantId, x.LegacyRef }).HasFilter("[LegacyRef] IS NOT NULL");
        });
        b.Entity<Supplier>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(300); e.Property(x => x.ContactName).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(256); e.Property(x => x.Phone).HasMaxLength(50); e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.Country).HasMaxLength(100); e.Property(x => x.TaxNumber).HasMaxLength(100); e.Property(x => x.PaymentTerms).HasMaxLength(200); e.Property(x => x.Currency).HasMaxLength(3);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
        b.Entity<PurchaseOrder>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.Currency).HasMaxLength(3); e.Property(x => x.RequestedBy).HasMaxLength(200);
            e.Property(x => x.PaymentTerms).HasMaxLength(200); e.Property(x => x.ShippingMethod).HasMaxLength(100); e.Property(x => x.SupplierQuoteRef).HasMaxLength(100);
            e.Property(x => x.ApprovedByName).HasMaxLength(200); e.Property(x => x.CancelReason).HasMaxLength(1000);
            e.Property(x => x.ExchangeRate).HasPrecision(18, 6); e.Property(x => x.TaxPercent).HasPrecision(9, 3); e.Property(x => x.ShippingCost).HasPrecision(18, 2);
            e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasIndex(x => new { x.TenantId, x.Status });
        });
        b.Entity<PurchaseOrderLine>(e =>
        {
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Description).HasMaxLength(500); e.Property(x => x.UnitOfMeasure).HasMaxLength(30); e.Property(x => x.AccountCode).HasMaxLength(50);
            e.Property(x => x.Quantity).HasPrecision(18, 3); e.Property(x => x.QuantityReceived).HasPrecision(18, 3);
            e.Property(x => x.UnitPrice).HasPrecision(18, 4); e.Property(x => x.DiscountPercent).HasPrecision(9, 3);
            e.HasOne(x => x.Part).WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(x => x.NetUnitPrice); e.Ignore(x => x.LineTotal); e.Ignore(x => x.Pieces); e.Ignore(x => x.PiecesReceived);
            e.Property(x => x.PackSize).HasPrecision(18, 4).HasDefaultValue(1m); e.Property(x => x.SupplierPartNumber).HasMaxLength(100);
            e.HasOne<PurchaseRequestLine>().WithMany().HasForeignKey(x => x.PurchaseRequestLineId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.PurchaseRequestLineId);
        });
        b.Entity<PurchaseOrder>(e => e.HasOne<OrderType>().WithMany().HasForeignKey(x => x.OrderTypeId).OnDelete(DeleteBehavior.Restrict));

        // Purchase requests and approval levels (0.16.0)
        b.Entity<PurchaseRequest>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RequestedByName).HasMaxLength(200); e.Property(x => x.DecidedByName).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(4000); e.Property(x => x.RejectReason).HasMaxLength(1000); e.Property(x => x.CancelReason).HasMaxLength(1000);
            e.Property(x => x.RecipientIds).HasDefaultValueSql("'[]'");
            e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PurchaseRequestId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasIndex(x => new { x.TenantId, x.Status });
        });
        b.Entity<PurchaseRequestLine>(e =>
        {
            e.Property(x => x.Description).HasMaxLength(500); e.Property(x => x.UnitOfMeasure).HasMaxLength(30); e.Property(x => x.Reason).HasMaxLength(1000);
            e.Property(x => x.Quantity).HasPrecision(18, 3); e.Property(x => x.EstimatedUnitPrice).HasPrecision(18, 4);
            e.HasOne(x => x.Part).WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PurchaseRequestComment>(e =>
        {
            e.Property(x => x.Body).HasMaxLength(4000); e.Property(x => x.UserName).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.PurchaseRequestId });
            e.HasOne<PurchaseRequest>().WithMany().HasForeignKey(x => x.PurchaseRequestId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<OrderType>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
        // Contracts and contractors (0.17.0)
        b.Entity<Supplier>(e =>
        {
            e.Property(x => x.IsSupplier).HasDefaultValue(true);
            e.HasMany(x => x.Contacts).WithOne().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Addresses).WithOne().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.CategoryRates).WithOne().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Cascade);
        });
        // Supplier catalogue (0.18.0). Parts and companies are soft-deleted, so the rows are removed by the API, not by a cascade.
        b.Entity<SupplierPart>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.SupplierPartNumber).HasMaxLength(100); e.Property(x => x.PackName).HasMaxLength(100); e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.PackSize).HasPrecision(18, 4); e.Property(x => x.PackPrice).HasPrecision(18, 4); e.Property(x => x.DiscountPercent).HasPrecision(9, 3);
            e.Property(x => x.LastPaidPrice).HasPrecision(18, 4); e.Property(x => x.LastPaidDiscount).HasPrecision(9, 3); e.Property(x => x.LastPaidCurrency).HasMaxLength(3);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.HasOne(x => x.Part).WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.TenantId, x.PartId, x.SupplierId }).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasIndex(x => x.SupplierId);
        });
        b.Entity<SupplierContact>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Role).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(50); e.Property(x => x.Email).HasMaxLength(256);
        });
        b.Entity<SupplierAddress>(e =>
        {
            e.Property(x => x.Label).HasMaxLength(100); e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.City).HasMaxLength(100); e.Property(x => x.Country).HasMaxLength(100);
        });
        b.Entity<ContractorCategory>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.DefaultRate).HasPrecision(18, 2);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
        b.Entity<SupplierCategoryRate>(e =>
        {
            e.Property(x => x.Rate).HasPrecision(18, 2);
            e.HasOne<ContractorCategory>().WithMany().HasForeignKey(x => x.ContractorCategoryId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.SupplierId, x.ContractorCategoryId }).IsUnique();
        });
        b.Entity<WorkOrderContractor>(e =>
        {
            e.Property(x => x.PersonName).HasMaxLength(200); e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.PlannedHours).HasPrecision(9, 2); e.Property(x => x.HoursDone).HasPrecision(9, 2); e.Property(x => x.Rate).HasPrecision(18, 2);
            e.HasIndex(x => new { x.TenantId, x.PlannedOn });
            e.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.ContractorCategoryId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.TenantId, x.WorkOrderId });
            e.HasIndex(x => x.SupplierId);
        });
        b.Entity<FourC.Cmms.Core.Contracts.ContractType>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
        b.Entity<FourC.Cmms.Core.Contracts.Contract>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.Title).HasMaxLength(300); e.Property(x => x.Notes).HasMaxLength(4000);
            e.Property(x => x.Value).HasPrecision(18, 2); e.Property(x => x.Currency).HasMaxLength(3); e.Property(x => x.PaymentTerms).HasMaxLength(200);
            e.Property(x => x.ExpectedPer).HasConversion<string>().HasMaxLength(10); e.Property(x => x.TerminationReason).HasMaxLength(1000);
            e.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.ContractType).WithMany().HasForeignKey(x => x.ContractTypeId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Trade>().WithMany().HasForeignKey(x => x.TradeId).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Periods).WithOne().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Amendments).WithOne().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Assets).WithOne().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasIndex(x => new { x.TenantId, x.EndDate });
            e.HasIndex(x => x.SupplierId);
        });
        b.Entity<FourC.Cmms.Core.Contracts.ContractPeriod>(e =>
        {
            e.Property(x => x.Value).HasPrecision(18, 2);
            e.HasIndex(x => new { x.ContractId, x.Number }).IsUnique();
        });
        b.Entity<FourC.Cmms.Core.Contracts.ContractAmendment>(e =>
        {
            e.Property(x => x.Description).HasMaxLength(2000); e.Property(x => x.NewValue).HasPrecision(18, 2); e.Property(x => x.CreatedByName).HasMaxLength(200);
            e.HasIndex(x => x.ContractId);
        });
        b.Entity<FourC.Cmms.Core.Contracts.ContractAsset>(e =>
        {
            e.Property(x => x.YearlyCost).HasPrecision(18, 2);
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.ContractId, x.AssetId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.AssetId });
        });
        b.Entity<WorkOrder>(e => e.HasOne<FourC.Cmms.Core.Contracts.Contract>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.NoAction));
        b.Entity<PmSchedule>(e => e.HasOne<FourC.Cmms.Core.Contracts.Contract>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.NoAction));
        b.Entity<ApprovalLevel>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.MinAmount).HasPrecision(18, 2);
            e.Property(x => x.Rule).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.ApproverIds).HasDefaultValueSql("'[]'");
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OrderType>().WithMany().HasForeignKey(x => x.OrderTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.SiteId, x.OrderTypeId });
        });
        b.Entity<PurchaseOrderApprovalStep>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.MinAmount).HasPrecision(18, 2);
            e.Property(x => x.Rule).HasConversion<string>().HasMaxLength(10); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ApproverIds).HasDefaultValueSql("'[]'");
            e.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TenantId, x.PurchaseOrderId, x.Round });
        });
        b.Entity<PurchaseOrderSignature>(e =>
        {
            e.Property(x => x.UserName).HasMaxLength(200); e.Property(x => x.Comment).HasMaxLength(1000);
            e.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TenantId, x.PurchaseOrderId, x.Round });
        });

        b.Entity<TechAction>(e =>
        {
            e.Property(x => x.Type).HasMaxLength(40); e.Property(x => x.Status).HasMaxLength(20); e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.Message).HasMaxLength(2000);
            e.HasIndex(x => new { x.TenantId, x.UserId, x.ClientId }).IsUnique();
        });
        b.Entity<UserSession>(e =>
        {
            e.Property(x => x.DeviceName).HasMaxLength(120); e.Property(x => x.PushToken).HasMaxLength(512);
            e.HasIndex(x => new { x.TenantId, x.UserId });
        });
        b.Entity<StockReservation>(e => { e.HasNoKey(); e.ToView(Reservations.ViewName); e.Property(x => x.Reserved).HasPrecision(18, 3); });

        // Tenant isolation (+ soft delete) for every tenant-owned entity.
        foreach (var type in b.Model.GetEntityTypes().Select(t => t.ClrType).Where(t => typeof(ITenantOwned).IsAssignableFrom(t)).ToList())
            GetType().GetMethod(nameof(ApplyTenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .MakeGenericMethod(type).Invoke(this, [b]);
        ApplySiteFilters(b);
    }

    private void ApplyTenantFilter<T>(ModelBuilder b) where T : class, ITenantOwned
    {
        if (typeof(ISoftDeletable).IsAssignableFrom(typeof(T)))
            b.Entity<T>().HasQueryFilter(TenantFilter, e => e.TenantId == CurrentTenantId && !((ISoftDeletable)e).IsDeleted);
        else
            b.Entity<T>().HasQueryFilter(TenantFilter, e => e.TenantId == CurrentTenantId);
    }

    /// <summary>What kind of site limit this context has; each kind gets its own model, so requests without a limit carry no
    /// site filter at all (not even a parameter check or a join).</summary>
    public enum SiteScopeKind { None, Sites, Branches }

    public SiteScopeKind ScopeKind => CurrentSiteIds is null ? SiteScopeKind.None : CurrentBranchPaths is null ? SiteScopeKind.Sites : SiteScopeKind.Branches;

    /// <summary>The kind of model this instance got (fixed when it first needed one).</summary>
    private SiteScopeKind? _modelKind;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, ScopedModelCacheKeyFactory>()
            .AddInterceptors(ScopeGuard.Instance);

    /// <summary>One cached model per <see cref="SiteScopeKind"/>.</summary>
    private sealed class ScopedModelCacheKeyFactory : Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
        {
            if (context is not CmmsDbContext c) return (context.GetType(), designTime);
            var kind = c.ScopeKind;
            c._modelKind = kind;
            return (context.GetType(), kind, designTime);
        }
    }

    /// <summary>
    /// Fails closed: a context whose model was built before the request's limits were known (so without them) refuses to run any
    /// command once the limits are there, instead of reading every site.
    /// </summary>
    private sealed class ScopeGuard : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public static readonly ScopeGuard Instance = new();

        private static void Check(Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData e)
        {
            if (e.Context is CmmsDbContext c && c._modelKind is { } built && built != c.ScopeKind)
                throw new InvalidOperationException($"This database context was set up for site scope {built} but the request has {c.ScopeKind}.");
        }

        public override Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> ReaderExecuting(System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result)
        { Check(eventData); return result; }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        { Check(eventData); return ValueTask.FromResult(result); }

        public override Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<object> ScalarExecuting(System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<object> result)
        { Check(eventData); return result; }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<object>> ScalarExecutingAsync(System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        { Check(eventData); return ValueTask.FromResult(result); }

        public override Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> NonQueryExecuting(System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result)
        { Check(eventData); return result; }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> NonQueryExecutingAsync(System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        { Check(eventData); return ValueTask.FromResult(result); }
    }

    /// <summary>
    /// The signed-in person's site and branch limits (0.9.0), on the models of limited requests only. Records that belong to a site
    /// are limited by it (and, for people limited to branches of the location tree, by their location's path); child rows read
    /// on their own follow their parent.
    /// </summary>
    private void ApplySiteFilters(ModelBuilder b)
    {
        var kind = ScopeKind;
        if (kind == SiteScopeKind.None) return;
        b.Entity<Site>().HasQueryFilter(SiteFilter, s => CurrentSiteIds!.Contains(s.Id));
        if (kind == SiteScopeKind.Sites)
        {
            b.Entity<Location>().HasQueryFilter(SiteFilter, l => CurrentSiteIds!.Contains(l.SiteId));
            b.Entity<Asset>().HasQueryFilter(SiteFilter, e => CurrentSiteIds!.Contains(e.SiteId));
            b.Entity<WorkRequest>().HasQueryFilter(SiteFilter, e => CurrentSiteIds!.Contains(e.SiteId));
            b.Entity<WorkOrder>().HasQueryFilter(SiteFilter, e => CurrentSiteIds!.Contains(e.SiteId));
            b.Entity<PmSchedule>().HasQueryFilter(SiteFilter, e => CurrentSiteIds!.Contains(e.SiteId));
        }
        else
        {
            b.Entity<Location>().HasQueryFilter(SiteFilter, l => CurrentSiteIds!.Contains(l.SiteId) && CurrentBranchPaths!.Any(p => l.Path.StartsWith(p)));
            // Spare serial units (0.24.0: in a store, at repair or scrapped) have no place in the tree: the site lets people see them.
            b.Entity<Asset>().HasQueryFilter(SiteFilter, e => CurrentSiteIds!.Contains(e.SiteId)
                && ((e.LocationId != null && CurrentBranchPaths!.Any(p => e.Location!.Path.StartsWith(p))) || (e.PartId != null && e.ParentAssetId == null)));
            b.Entity<WorkRequest>().HasQueryFilter(SiteFilter, e => CurrentSiteIds!.Contains(e.SiteId)
                && e.LocationId != null && CurrentBranchPaths!.Any(p => e.Location!.Path.StartsWith(p)));
            b.Entity<WorkOrder>().HasQueryFilter(SiteFilter, e => CurrentSiteIds!.Contains(e.SiteId)
                && e.LocationId != null && CurrentBranchPaths!.Any(p => e.Location!.Path.StartsWith(p)));
            b.Entity<PmSchedule>().HasQueryFilter(SiteFilter, e => CurrentSiteIds!.Contains(e.SiteId)
                && e.LocationId != null && CurrentBranchPaths!.Any(p => e.Location!.Path.StartsWith(p)));
        }
        b.Entity<Store>().HasQueryFilter(SiteFilter, e => CurrentSiteIds!.Contains(e.SiteId));
        b.Entity<WorkOrderGroup>().HasQueryFilter(SiteFilter, e => CurrentSiteIds!.Contains(e.SiteId));
        b.Entity<PurchaseOrder>().HasQueryFilter(SiteFilter, e => CurrentSiteIds!.Contains(e.SiteId));
        b.Entity<Team>().HasQueryFilter(SiteFilter, e => e.SiteId == null || CurrentSiteIds!.Contains(e.SiteId.Value));
        b.Entity<PrintLayout>().HasQueryFilter(SiteFilter, e => e.SiteId == null || CurrentSiteIds!.Contains(e.SiteId.Value));
        b.Entity<Costs.CostCentre>().HasQueryFilter(SiteFilter, e => e.SiteId == null || CurrentSiteIds!.Contains(e.SiteId.Value));
        b.Entity<AssetGroup>().HasQueryFilter(SiteFilter, e => e.SiteId == null || CurrentSiteIds!.Contains(e.SiteId.Value));

        // Child rows: visible when their parent is.
        b.Entity<WorkOrderLabor>().HasQueryFilter(SiteFilter, x => WorkOrders.Any(w => w.Id == x.WorkOrderId));
        b.Entity<WorkOrderPart>().HasQueryFilter(SiteFilter, x => WorkOrders.Any(w => w.Id == x.WorkOrderId));
        b.Entity<WorkOrderCost>().HasQueryFilter(SiteFilter, x => WorkOrders.Any(w => w.Id == x.WorkOrderId));
        b.Entity<WorkOrderComment>().HasQueryFilter(SiteFilter, x => WorkOrders.Any(w => w.Id == x.WorkOrderId));
        b.Entity<WorkRequestComment>().HasQueryFilter(SiteFilter, x => WorkRequests.Any(r => r.Id == x.WorkRequestId));
        b.Entity<RequestFeedback>().HasQueryFilter(SiteFilter, x => WorkRequests.Any(r => r.Id == x.WorkRequestId));
        b.Entity<WorkOrderStatusChange>().HasQueryFilter(SiteFilter, x => WorkOrders.Any(w => w.Id == x.WorkOrderId));
        b.Entity<WorkOrderTask>().HasQueryFilter(SiteFilter, x => WorkOrders.Any(w => w.Id == x.WorkOrderId));
        b.Entity<WorkOrderAssignee>().HasQueryFilter(SiteFilter, x => WorkOrders.Any(w => w.Id == x.WorkOrderId));
        b.Entity<WorkOrderAsset>().HasQueryFilter(SiteFilter, x => WorkOrders.Any(w => w.Id == x.WorkOrderId));
        b.Entity<StockLevel>().HasQueryFilter(SiteFilter, x => Stores.Any(s => s.Id == x.StoreId));
        b.Entity<StockCount>().HasQueryFilter(SiteFilter, x => Stores.Any(s => s.Id == x.StoreId));
        b.Entity<StockCountLine>().HasQueryFilter(SiteFilter, x => StockCounts.Any(c => c.Id == x.StockCountId));
        b.Entity<StockTransaction>().HasQueryFilter(SiteFilter, x => Stores.Any(s => s.Id == x.StoreId));
        b.Entity<Meter>().HasQueryFilter(SiteFilter, x => Assets.Any(a => a.Id == x.AssetId));
        b.Entity<MeterReading>().HasQueryFilter(SiteFilter, x => Meters.Any(m => m.Id == x.MeterId));
        // A reading follows its asset (and so branch limits), else its site; a point's asset links follow the asset.
        b.Entity<MeasurementReading>().HasQueryFilter(SiteFilter, x => x.AssetId != null ? Assets.Any(a => a.Id == x.AssetId) : Sites.Any(s => s.Id == x.SiteId));
        b.Entity<MeasurementPointAsset>().HasQueryFilter(SiteFilter, x => Assets.Any(a => a.Id == x.AssetId));
        b.Entity<DowntimeEvent>().HasQueryFilter(SiteFilter, x => Assets.Any(a => a.Id == x.AssetId));
        b.Entity<AssetPart>().HasQueryFilter(SiteFilter, x => Assets.Any(a => a.Id == x.AssetId));
        b.Entity<AssetTransfer>().HasQueryFilter(SiteFilter, x => Assets.Any(a => a.Id == x.AssetId));
        b.Entity<PmScheduleTask>().HasQueryFilter(SiteFilter, x => PmSchedules.Any(p => p.Id == x.PmScheduleId));
        b.Entity<PmScheduleAsset>().HasQueryFilter(SiteFilter, x => PmSchedules.Any(p => p.Id == x.PmScheduleId));
        b.Entity<PmScheduleCalendar>().HasQueryFilter(SiteFilter, x => PmSchedules.Any(p => p.Id == x.PmScheduleId));
        b.Entity<PmScheduleMeter>().HasQueryFilter(SiteFilter, x => PmSchedules.Any(p => p.Id == x.PmScheduleId));
        b.Entity<WorkOrderGroupComment>().HasQueryFilter(SiteFilter, x => WorkOrderGroups.Any(g => g.Id == x.GroupId));
        // An asset's checklist follows the asset; a category's is for the whole organization.
        b.Entity<AssetChecklist>().HasQueryFilter(SiteFilter, x => x.AssetId == null || Assets.Any(a => a.Id == x.AssetId));
        b.Entity<PurchaseOrderLine>().HasQueryFilter(SiteFilter, x => PurchaseOrders.Any(p => p.Id == x.PurchaseOrderId));
        b.Entity<PurchaseRequest>().HasQueryFilter(SiteFilter, e => CurrentSiteIds!.Contains(e.SiteId));
        b.Entity<PurchaseRequestLine>().HasQueryFilter(SiteFilter, x => PurchaseRequests.Any(r => r.Id == x.PurchaseRequestId));
        b.Entity<PurchaseRequestComment>().HasQueryFilter(SiteFilter, x => PurchaseRequests.Any(r => r.Id == x.PurchaseRequestId));
        b.Entity<PurchaseOrderApprovalStep>().HasQueryFilter(SiteFilter, x => PurchaseOrders.Any(p => p.Id == x.PurchaseOrderId));
        b.Entity<PurchaseOrderSignature>().HasQueryFilter(SiteFilter, x => PurchaseOrders.Any(p => p.Id == x.PurchaseOrderId));
        // Contracts (0.17.0): organization-wide ones are seen everywhere; a site's only at that site. Their rows follow them;
        // covered assets are read through Assets, whose own filter hides other sites.
        b.Entity<WorkOrderContractor>().HasQueryFilter(SiteFilter, x => WorkOrders.Any(w => w.Id == x.WorkOrderId));
        b.Entity<FourC.Cmms.Core.Contracts.Contract>().HasQueryFilter(SiteFilter, e => e.SiteId == null || CurrentSiteIds!.Contains(e.SiteId.Value));
        b.Entity<FourC.Cmms.Core.Contracts.ContractPeriod>().HasQueryFilter(SiteFilter, x => Contracts.Any(c => c.Id == x.ContractId));
        b.Entity<FourC.Cmms.Core.Contracts.ContractAmendment>().HasQueryFilter(SiteFilter, x => Contracts.Any(c => c.Id == x.ContractId));
        b.Entity<FourC.Cmms.Core.Contracts.ContractAsset>().HasQueryFilter(SiteFilter, x => Contracts.Any(c => c.Id == x.ContractId));
        b.Entity<StockReservation>().HasQueryFilter(SiteFilter, x => Stores.Any(s => s.Id == x.StoreId));
    }

    private static void ConfigureTenantEntity<T>(EntityTypeBuilder<T> e) where T : TenantEntity
    {
        e.Property(x => x.RowVersion).IsRowVersion();
        e.Property(x => x.LegacyRef).HasMaxLength(200);
        e.HasIndex(x => new { x.TenantId, x.LegacyRef }).HasFilter("[LegacyRef] IS NOT NULL");
    }

    private static Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<T, string> JsonConverter<T>() where T : new() =>
        new(v => JsonSerializer.Serialize(v, Json), v => string.IsNullOrEmpty(v) ? new T() : JsonSerializer.Deserialize<T>(v, Json) ?? new T());

    private static Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<T, string> NullableJsonConverter<T>() where T : class =>
        new(v => JsonSerializer.Serialize(v, Json), v => JsonSerializer.Deserialize<T>(v, Json)!);

    private static ValueComparer<T?> NullableJsonComparer<T>() where T : class => new(
        (a, c) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(c, Json),
        v => v == null ? 0 : JsonSerializer.Serialize(v, Json).GetHashCode(),
        v => v == null ? null : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Json), Json));

    private static ValueComparer<T> JsonComparer<T>() => new(
        (a, c) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(c, Json),
        v => JsonSerializer.Serialize(v, Json).GetHashCode(),
        v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Json), Json)!);

    // ---------- Save pipeline: tenant stamping, audit fields, soft delete, audit trail ----------

    private static readonly HashSet<string> IgnoredAuditProps =
        [nameof(TenantEntity.RowVersion), nameof(TenantEntity.UpdatedAt), nameof(TenantEntity.UpdatedById), nameof(TenantEntity.CreatedAt), nameof(TenantEntity.CreatedById), "Path", "Depth",
         // A sign-in's bookkeeping on the user (0.25.0: sign-ins are logged as events of their own).
         nameof(AppUser.LastLoginAt), nameof(AppUser.AccessFailedCount), nameof(AppUser.ConcurrencyStamp), nameof(AppUser.SecurityStamp),
         nameof(AppUser.NormalizedEmail), nameof(AppUser.NormalizedUserName), nameof(AppUser.LockoutEnd)];

    /// <summary>Who this context acts for (0.25.0: hand-written change entries take it).</summary>
    public ITenantContext Actor => tenant;

    private Guid? _changeId;
    /// <summary>This save's change number: the request's or job run's, else one for this context.</summary>
    private Guid ChangeNumber => tenant.ChangeId != Guid.Empty ? tenant.ChangeId : _changeId ??= Guid.CreateVersion7();

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var audits = new List<AuditEntry>();
        foreach (var entry in ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is ITenantOwned owned)
            {
                if (entry.State == EntityState.Added && owned.TenantId == Guid.Empty)
                    owned.TenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant in context.");
                else if (entry.State != EntityState.Unchanged && tenant.TenantId is { } t && owned.TenantId != t)
                    throw new InvalidOperationException("Cross-tenant write blocked.");
            }
            if (entry.Entity is ISoftDeletable sd && entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                sd.IsDeleted = true;
                sd.DeletedAt = now;
            }
            if (entry.Entity is IAuditable a)
            {
                if (entry.State == EntityState.Added) { if (a.CreatedAt == default) a.CreatedAt = now; a.CreatedById ??= tenant.UserId; } // imports keep legacy dates
                else if (entry.State == EntityState.Modified) { a.UpdatedAt = now; a.UpdatedById = tenant.UserId; }
            }
            if (entry.Entity is AuditEntry written && entry.State == EntityState.Added && written.ChangeId == Guid.Empty)
            {
                written.ChangeId = ChangeNumber;
                written.Source = tenant.Source;
            }
            else if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && AuditScope.IsRecorded(entry.Metadata.ClrType)
                     && BuildAudit(entry, now) is { } audit)
                audits.Add(audit);
        }
        AuditEntries.AddRange(audits.Where(x => x.Action != "None"));
        return await base.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The change entry for one tracked row (0.25.0: every recorded entity, with its source, change number and parent). A record
    /// of its own logs its changed fields; a child row (a labour line, a comment) also logs its fields when added or deleted.
    /// Sensitive values are written masked. Null when the row can't be placed in an organization.
    /// </summary>
    private AuditEntry? BuildAudit(EntityEntry entry, DateTimeOffset now)
    {
        var entity = entry.Entity;
        var tenantId = entity is ITenantOwned owned ? owned.TenantId
            : entry.Metadata.FindProperty("TenantId") is { } tp && entry.Property(tp.Name).CurrentValue is Guid g && g != Guid.Empty ? g
            : tenant.TenantId;
        if (tenantId is not { } tid || tid == Guid.Empty) return null;
        var parent = AuditScope.ParentOf(entity);
        var ownId = entry.Metadata.FindProperty("Id") is { } idp && entry.Property(idp.Name).CurrentValue is Guid gid ? gid : (Guid?)null;
        var child = entity is not TenantEntity;
        string action;
        Dictionary<string, object?>? changes = null;
        object? Value(PropertyEntry p, object? v) => AuditScope.IsSensitive(p.Metadata.Name) && v is not null ? AuditScope.Masked : v;
        var fields = entry.Properties.Where(p => !IgnoredAuditProps.Contains(p.Metadata.Name) && p.Metadata.Name is not "TenantId" and not "Id");
        if (entry.State == EntityState.Added)
        {
            action = "Created";
            if (child) changes = fields.Where(p => p.CurrentValue is not null).ToDictionary(p => p.Metadata.Name, p => (object?)new { to = Value(p, p.CurrentValue) });
        }
        else if (entry.State == EntityState.Deleted || (entity is ISoftDeletable { IsDeleted: true } && entry.Property(nameof(ISoftDeletable.IsDeleted)).IsModified))
        {
            action = "Deleted";
            if (child && entry.State == EntityState.Deleted)
                changes = fields.Where(p => p.OriginalValue is not null).ToDictionary(p => p.Metadata.Name, p => (object?)new { from = Value(p, p.OriginalValue) });
        }
        else
        {
            action = "Updated";
            changes = [];
            foreach (var p in fields.Where(p => p.IsModified))
            {
                var from = p.OriginalValue; var to = p.CurrentValue;
                if (Equals(from, to) || (p.Metadata.GetValueComparer() is { } cmp && cmp.Equals(from, to))) continue;
                changes[p.Metadata.Name] = new { from = Value(p, from), to = Value(p, to) };
            }
            if (changes.Count == 0) action = "None";
        }
        return new AuditEntry
        {
            TenantId = tid,
            EntityType = entry.Metadata.ClrType.Name,
            EntityId = ownId ?? parent?.Id ?? Guid.Empty,
            Action = action,
            Changes = changes is { Count: > 0 } ? JsonSerializer.Serialize(changes, Json) : null,
            UserId = tenant.UserId,
            UserName = tenant.UserName,
            Timestamp = now,
            Source = tenant.Source,
            ChangeId = ChangeNumber,
            ParentType = parent?.Type,
            ParentId = parent?.Id,
        };
    }

    /// <summary>Runs work in a transaction that is compatible with the retrying execution strategy.</summary>
    public Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default) =>
        Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            await using var tx = await Database.BeginTransactionAsync(token);
            var result = await work(token);
            await tx.CommitAsync(token);
            return result;
        }, ct);

    /// <summary>
    /// Gives back the numbers from <paramref name="firstUnused"/> to the end of a block taken with <see cref="NextNumbersAsync"/>,
    /// if nobody has taken a number since (the sequence still stands at <paramref name="end"/>).
    /// </summary>
    public Task ReturnNumbersAsync(string name, long firstUnused, long end, CancellationToken ct = default)
    {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant in context.");
        return Database.ExecuteSqlAsync(
            $"UPDATE NumberSequences SET NextValue = {firstUnused} WHERE TenantId = {tenantId} AND Name = {name} AND NextValue = {end}", ct);
    }

    /// <summary>Ensures a sequence exists and its next value is at least <paramref name="next"/>.</summary>
    public Task SetSequenceAtLeastAsync(string name, long next, CancellationToken ct = default)
    {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant in context.");
        return Database.ExecuteSqlAsync($"""
            MERGE NumberSequences WITH (HOLDLOCK) AS t
            USING (SELECT {tenantId} AS TenantId, {name} AS Name) AS s ON t.TenantId = s.TenantId AND t.Name = s.Name
            WHEN MATCHED AND t.NextValue < {next} THEN UPDATE SET NextValue = {next}
            WHEN NOT MATCHED THEN INSERT (TenantId, Name, NextValue) VALUES ({tenantId}, {name}, {next});
            """, ct);
    }

    /// <summary>Atomically allocates the next number in a per-tenant sequence.</summary>
    public Task<long> NextNumberAsync(string name, CancellationToken ct = default) => NextNumbersAsync(name, 1, ct);

    /// <summary>Atomically allocates <paramref name="count"/> consecutive numbers in a per-tenant sequence; returns the first.</summary>
    public async Task<long> NextNumbersAsync(string name, int count, CancellationToken ct = default)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant in context.");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var values = await Database.SqlQuery<long>(
                $"UPDATE NumberSequences SET NextValue = NextValue + {count} OUTPUT deleted.NextValue AS Value WHERE TenantId = {tenantId} AND Name = {name}")
                .ToListAsync(ct);
            if (values.Count == 1) return values[0];
            try
            {
                await Database.ExecuteSqlAsync($"INSERT INTO NumberSequences (TenantId, Name, NextValue) VALUES ({tenantId}, {name}, 1)", ct);
            }
            catch (Microsoft.Data.SqlClient.SqlException) { /* concurrent insert; retry the update */ }
        }
        throw new InvalidOperationException($"Could not allocate number for sequence {name}.");
    }
}
