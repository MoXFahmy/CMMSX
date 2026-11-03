using System.Linq.Expressions;
using System.Text.Json;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Auditing;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Files;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Locations;
using FourC.Cmms.Core.People;
using FourC.Cmms.Core.Pm;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Purchasing;
using FourC.Cmms.Core.Work;
using FourC.Cmms.Core.Settings;
using FourC.Cmms.Core.Tenancy;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FourC.Cmms.Core.Data;

public class CmmsDbContext(DbContextOptions<CmmsDbContext> options, ITenantContext tenant)
    : IdentityUserContext<AppUser, Guid>(options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    /// <summary>Evaluated per query; EF parameterizes it so each context instance sees its own tenant.</summary>
    public Guid? CurrentTenantId => tenant.TenantId;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<AssetCategory> AssetCategories => Set<AssetCategory>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetTransfer> AssetTransfers => Set<AssetTransfer>();
    public DbSet<CustomFieldDefinition> CustomFields => Set<CustomFieldDefinition>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<FailureCode> FailureCodes => Set<FailureCode>();
    public DbSet<WorkRequest> WorkRequests => Set<WorkRequest>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderAssignee> WorkOrderAssignees => Set<WorkOrderAssignee>();
    public DbSet<WorkOrderTask> WorkOrderTasks => Set<WorkOrderTask>();
    public DbSet<WorkOrderLabor> WorkOrderLabor => Set<WorkOrderLabor>();
    public DbSet<WorkOrderCost> WorkOrderCosts => Set<WorkOrderCost>();
    public DbSet<WorkOrderComment> WorkOrderComments => Set<WorkOrderComment>();
    public DbSet<WorkOrderStatusChange> WorkOrderStatusChanges => Set<WorkOrderStatusChange>();
    public DbSet<PmTask> PmTasks => Set<PmTask>();
    public DbSet<PmTaskStep> PmTaskSteps => Set<PmTaskStep>();
    public DbSet<PmSchedule> PmSchedules => Set<PmSchedule>();
    public DbSet<PmScheduleTask> PmScheduleTasks => Set<PmScheduleTask>();
    public DbSet<Meter> Meters => Set<Meter>();
    public DbSet<MeterReading> MeterReadings => Set<MeterReading>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<PartGroup> PartGroups => Set<PartGroup>();
    public DbSet<Part> Parts => Set<Part>();
    public DbSet<StockLevel> StockLevels => Set<StockLevel>();
    public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();

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
            e.Property(x => x.LegacyRef).HasMaxLength(200);
            e.HasIndex(x => x.TenantId);
            e.HasMany(x => x.Roles).WithOne().HasForeignKey(x => x.UserId);
        });

        b.Entity<Role>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
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
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        });

        b.Entity<Asset>(e =>
        {
            ConfigureTenantEntity(e);
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
        });

        b.Entity<Attachment>(e =>
        {
            e.Property(x => x.EntityType).HasMaxLength(50);
            e.Property(x => x.FileName).HasMaxLength(260);
            e.Property(x => x.ContentType).HasMaxLength(200);
            e.Property(x => x.StorageKey).HasMaxLength(400);
            e.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId });
        });

        b.Entity<NumberSequence>(e =>
        {
            e.HasKey(x => new { x.TenantId, x.Name });
            e.Property(x => x.Name).HasMaxLength(50);
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
        b.Entity<Employee>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.FullName).HasMaxLength(200); e.Property(x => x.JobTitle).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(256); e.Property(x => x.Phone).HasMaxLength(50); e.Property(x => x.Company).HasMaxLength(200);
            e.Property(x => x.HourlyRate).HasPrecision(18, 2); e.Property(x => x.OvertimeRate).HasPrecision(18, 2);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasIndex(x => new { x.TenantId, x.UserId });
        });
        b.Entity<WorkRequest>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Number).HasMaxLength(50); e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.RequesterName).HasMaxLength(200); e.Property(x => x.RequesterEmail).HasMaxLength(256); e.Property(x => x.RequesterPhone).HasMaxLength(50);
            e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasIndex(x => new { x.TenantId, x.Status });
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
            e.HasOne(x => x.FailureCode).WithMany().HasForeignKey(x => x.FailureCodeId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Assignees).WithOne().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasIndex(x => new { x.TenantId, x.Status, x.DueDate });
            e.HasIndex(x => new { x.TenantId, x.AssetId });
        });
        b.Entity<WorkOrderAssignee>(e =>
        {
            e.HasKey(x => new { x.WorkOrderId, x.EmployeeId });
            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<WorkOrderTask>(e => { e.Property(x => x.Description).HasMaxLength(1000); e.Property(x => x.DoneBy).HasMaxLength(200); e.HasIndex(x => new { x.TenantId, x.WorkOrderId }); });
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
        b.Entity<WorkOrderStatusChange>(e =>
        {
            e.Property(x => x.Note).HasMaxLength(2000); e.Property(x => x.ChangedByName).HasMaxLength(200); e.Property(x => x.LegacyRef).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.WorkOrderId, x.ChangedAt });
        });

        b.Entity<PmTask>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(300); e.Property(x => x.EstimatedHours).HasPrecision(9, 2);
            e.HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.PmTaskId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
        b.Entity<PmTaskStep>(e => e.Property(x => x.Description).HasMaxLength(2000));
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
            e.HasMany(x => x.ExtraTasks).WithOne().HasForeignKey(x => x.PmScheduleId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasIndex(x => new { x.TenantId, x.IsActive, x.NextDueDate });
            e.HasIndex(x => new { x.TenantId, x.AssetId });
        });
        b.Entity<PmScheduleTask>(e => e.HasKey(x => new { x.PmScheduleId, x.PmTaskId }));
        b.Entity<Meter>(e =>
        {
            ConfigureTenantEntity(e);
            e.Property(x => x.Code).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Unit).HasMaxLength(30);
            e.Property(x => x.CurrentReading).HasPrecision(18, 3); e.Property(x => x.MaxDailyIncrement).HasPrecision(18, 3);
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
        b.Entity<MeterReading>(e =>
        {
            e.Property(x => x.Value).HasPrecision(18, 3); e.Property(x => x.Note).HasMaxLength(1000); e.Property(x => x.ByName).HasMaxLength(200); e.Property(x => x.LegacyRef).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.MeterId, x.ReadAt });
        });
        b.Entity<WorkOrder>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.PmScheduleId, x.PmDueDate }).IsUnique().HasFilter("[PmScheduleId] IS NOT NULL AND [PmDueDate] IS NOT NULL AND [IsDeleted] = 0");
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
            e.Property(x => x.Description).HasMaxLength(500); e.Property(x => x.UnitOfMeasure).HasMaxLength(30); e.Property(x => x.AccountCode).HasMaxLength(50);
            e.Property(x => x.Quantity).HasPrecision(18, 3); e.Property(x => x.QuantityReceived).HasPrecision(18, 3);
            e.Property(x => x.UnitPrice).HasPrecision(18, 4); e.Property(x => x.DiscountPercent).HasPrecision(9, 3);
            e.HasOne(x => x.Part).WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(x => x.NetUnitPrice); e.Ignore(x => x.LineTotal);
        });

        // Tenant isolation (+ soft delete) for every tenant-owned entity.
        foreach (var type in b.Model.GetEntityTypes().Select(t => t.ClrType).Where(t => typeof(ITenantOwned).IsAssignableFrom(t)).ToList())
            GetType().GetMethod(nameof(ApplyTenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .MakeGenericMethod(type).Invoke(this, [b]);
    }

    private void ApplyTenantFilter<T>(ModelBuilder b) where T : class, ITenantOwned
    {
        if (typeof(ISoftDeletable).IsAssignableFrom(typeof(T)))
            b.Entity<T>().HasQueryFilter(e => e.TenantId == CurrentTenantId && !((ISoftDeletable)e).IsDeleted);
        else
            b.Entity<T>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
    }

    private static void ConfigureTenantEntity<T>(EntityTypeBuilder<T> e) where T : TenantEntity
    {
        e.Property(x => x.RowVersion).IsRowVersion();
        e.Property(x => x.LegacyRef).HasMaxLength(200);
        e.HasIndex(x => new { x.TenantId, x.LegacyRef }).HasFilter("[LegacyRef] IS NOT NULL");
    }

    private static Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<T, string> JsonConverter<T>() where T : new() =>
        new(v => JsonSerializer.Serialize(v, Json), v => string.IsNullOrEmpty(v) ? new T() : JsonSerializer.Deserialize<T>(v, Json) ?? new T());

    private static ValueComparer<T> JsonComparer<T>() => new(
        (a, c) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(c, Json),
        v => JsonSerializer.Serialize(v, Json).GetHashCode(),
        v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Json), Json)!);

    // ---------- Save pipeline: tenant stamping, audit fields, soft delete, audit trail ----------

    private static readonly HashSet<string> IgnoredAuditProps =
        [nameof(TenantEntity.RowVersion), nameof(TenantEntity.UpdatedAt), nameof(TenantEntity.UpdatedById), nameof(TenantEntity.CreatedAt), nameof(TenantEntity.CreatedById), "Path", "Depth"];

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
            if (entry.Entity is TenantEntity te && entry.State is EntityState.Added or EntityState.Modified)
                audits.Add(BuildAudit(entry, te, now));
        }
        AuditEntries.AddRange(audits.Where(x => x.Action != "None"));
        return await base.SaveChangesAsync(ct);
    }

    private AuditEntry BuildAudit(EntityEntry entry, TenantEntity te, DateTimeOffset now)
    {
        string action;
        Dictionary<string, object?>? changes = null;
        if (entry.State == EntityState.Added) action = "Created";
        else if (te.IsDeleted && entry.Property(nameof(ISoftDeletable.IsDeleted)).IsModified) action = "Deleted";
        else
        {
            action = "Updated";
            changes = [];
            foreach (var p in entry.Properties.Where(p => p.IsModified && !IgnoredAuditProps.Contains(p.Metadata.Name)))
            {
                var from = p.OriginalValue; var to = p.CurrentValue;
                if (Equals(from, to) || (p.Metadata.GetValueComparer() is { } cmp && cmp.Equals(from, to))) continue;
                changes[p.Metadata.Name] = new { from, to };
            }
            if (changes.Count == 0) action = "None";
        }
        return new AuditEntry
        {
            TenantId = te.TenantId,
            EntityType = entry.Metadata.ClrType.Name,
            EntityId = te.Id,
            Action = action,
            Changes = changes is { Count: > 0 } ? JsonSerializer.Serialize(changes, Json) : null,
            UserId = tenant.UserId,
            UserName = tenant.UserName,
            Timestamp = now,
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
    public async Task<long> NextNumberAsync(string name, CancellationToken ct = default)
    {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant in context.");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var values = await Database.SqlQuery<long>(
                $"UPDATE NumberSequences SET NextValue = NextValue + 1 OUTPUT deleted.NextValue AS Value WHERE TenantId = {tenantId} AND Name = {name}")
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
