using System.Reflection;
using FourC.Cmms.Core.Ai;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Contracts;
using FourC.Cmms.Core.Files;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Notifications;
using FourC.Cmms.Core.Pm;
using FourC.Cmms.Core.Purchasing;
using FourC.Cmms.Core.Settings;
using FourC.Cmms.Core.Tenancy;
using FourC.Cmms.Core.Work;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace FourC.Cmms.Core.Auditing;

/// <summary>
/// Which records the activity log covers (0.25.0). Every <see cref="TenantEntity"/> is recorded; every other entity is either
/// listed here as recorded (with the record it belongs to) or excluded: immutable ledgers that are already dated and named,
/// system rows, and personal conveniences.
/// </summary>
public static class AuditScope
{
    /// <summary>Recorded child rows: the record each belongs to, and the property holding its id.</summary>
    private static readonly Dictionary<Type, (string ParentType, string Property)?> Children = new()
    {
        [typeof(WorkOrderTask)] = ("WorkOrder", nameof(WorkOrderTask.WorkOrderId)),
        [typeof(WorkOrderLabor)] = ("WorkOrder", nameof(WorkOrderLabor.WorkOrderId)),
        [typeof(WorkOrderPart)] = ("WorkOrder", nameof(WorkOrderPart.WorkOrderId)),
        [typeof(WorkOrderContractor)] = ("WorkOrder", nameof(WorkOrderContractor.WorkOrderId)),
        [typeof(WorkOrderCost)] = ("WorkOrder", nameof(WorkOrderCost.WorkOrderId)),
        [typeof(WorkOrderComment)] = ("WorkOrder", nameof(WorkOrderComment.WorkOrderId)),
        [typeof(WorkOrderAssignee)] = ("WorkOrder", nameof(WorkOrderAssignee.WorkOrderId)),
        [typeof(WorkOrderAsset)] = ("WorkOrder", nameof(WorkOrderAsset.WorkOrderId)),
        [typeof(WorkOrderGroupComment)] = ("WorkOrderGroup", nameof(WorkOrderGroupComment.GroupId)),
        [typeof(WorkRequestComment)] = ("WorkRequest", nameof(WorkRequestComment.WorkRequestId)),
        [typeof(RequestFeedback)] = ("WorkRequest", nameof(RequestFeedback.WorkRequestId)),
        [typeof(PurchaseOrderLine)] = ("PurchaseOrder", nameof(PurchaseOrderLine.PurchaseOrderId)),
        [typeof(PurchaseOrderApprovalStep)] = ("PurchaseOrder", nameof(PurchaseOrderApprovalStep.PurchaseOrderId)),
        [typeof(PurchaseOrderSignature)] = ("PurchaseOrder", nameof(PurchaseOrderSignature.PurchaseOrderId)),
        [typeof(PurchaseRequestLine)] = ("PurchaseRequest", nameof(PurchaseRequestLine.PurchaseRequestId)),
        [typeof(PurchaseRequestComment)] = ("PurchaseRequest", nameof(PurchaseRequestComment.PurchaseRequestId)),
        [typeof(ContractPeriod)] = ("Contract", nameof(ContractPeriod.ContractId)),
        [typeof(ContractAmendment)] = ("Contract", nameof(ContractAmendment.ContractId)),
        [typeof(ContractAsset)] = ("Contract", nameof(ContractAsset.ContractId)),
        [typeof(StockLevel)] = ("Part", nameof(StockLevel.PartId)),
        [typeof(StockCountLine)] = ("StockCount", nameof(StockCountLine.StockCountId)),
        [typeof(PmTaskPart)] = ("PmTask", nameof(PmTaskPart.PmTaskId)),
        [typeof(PmTaskStep)] = ("PmTask", nameof(PmTaskStep.PmTaskId)),
        [typeof(PmScheduleAsset)] = ("PmSchedule", nameof(PmScheduleAsset.PmScheduleId)),
        [typeof(PmScheduleTask)] = ("PmSchedule", nameof(PmScheduleTask.PmScheduleId)),
        [typeof(PmScheduleMeter)] = ("PmSchedule", nameof(PmScheduleMeter.PmScheduleId)),
        [typeof(PmScheduleCalendar)] = ("PmSchedule", nameof(PmScheduleCalendar.PmScheduleId)),
        [typeof(AssetGroupMember)] = ("AssetGroup", nameof(AssetGroupMember.AssetGroupId)),
        [typeof(MeasurementPointAsset)] = ("MeasurementPoint", nameof(MeasurementPointAsset.PointId)),
        [typeof(SupplierAddress)] = ("Supplier", nameof(SupplierAddress.SupplierId)),
        [typeof(SupplierContact)] = ("Supplier", nameof(SupplierContact.SupplierId)),
        [typeof(SupplierCategoryRate)] = ("Supplier", nameof(SupplierCategoryRate.SupplierId)),
        [typeof(AssetPart)] = ("Asset", nameof(AssetPart.AssetId)),
        [typeof(RoleListSetting)] = ("Role", nameof(RoleListSetting.RoleId)),
        [typeof(UserRole)] = ("AppUser", nameof(UserRole.UserId)),
        [typeof(ApiKey)] = ("AppUser", nameof(ApiKey.ServiceAccountId)),
        [typeof(Attachment)] = null, // its EntityType and EntityId
        [typeof(AppUser)] = null,
        [typeof(Role)] = null,
    };

    /// <summary>Entities the log leaves out: dated ledgers with their own pages, system rows and personal conveniences.</summary>
    public static readonly IReadOnlySet<Type> Excluded = new HashSet<Type>
    {
        typeof(StockTransaction), typeof(UnitMovement), typeof(MeterReading), typeof(UserSession), typeof(AuditEntry), typeof(OutboundEmail),
        typeof(PushMessage), typeof(TechAction), typeof(AiCall), typeof(AiDiagnosisVote), typeof(NumberSequence), typeof(StockReservation),
        typeof(SavedView), typeof(UserListSetting), typeof(WorkOrderStatusChange), typeof(AssetTransfer), typeof(Tenant), typeof(DataProtectionKey),
        typeof(IdentityUserClaim<Guid>), typeof(IdentityUserLogin<Guid>), typeof(IdentityUserToken<Guid>), typeof(PasswordHistory), typeof(FourC.Cmms.Core.Imports.ImportJob),
    };

    /// <summary>Whether changes to <paramref name="type"/> are recorded.</summary>
    /// <summary>The record type a child row belongs to ("WorkOrder" for a labour line); null for a record of its own.</summary>
    public static string? ParentTypeOf(Type type) => Children.TryGetValue(type, out var p) ? p?.ParentType : null;

    public static bool IsRecorded(Type type) => !Excluded.Contains(type) && (typeof(TenantEntity).IsAssignableFrom(type) || Children.ContainsKey(type));

    /// <summary>The record a child row belongs to, or null for a record of its own.</summary>
    public static (string Type, Guid Id)? ParentOf(object entity)
    {
        if (entity is Attachment a) return (a.EntityType, a.EntityId);
        if (!Children.TryGetValue(entity.GetType(), out var parent) || parent is not { } p) return null;
        return entity.GetType().GetProperty(p.Property, BindingFlags.Public | BindingFlags.Instance)?.GetValue(entity) is Guid id ? (p.ParentType, id) : null;
    }

    /// <summary>The text written for a sensitive field's value.</summary>
    public const string Masked = "••••";

    private static readonly HashSet<string> SensitiveNames = new(StringComparer.Ordinal)
        { "PasswordHash", "SecurityStamp", "ConcurrencyStamp", "PushToken" };

    /// <summary>Fields whose values never reach the log: hashes, stamps, tokens and secrets.</summary>
    public static bool IsSensitive(string property) =>
        SensitiveNames.Contains(property) || property.EndsWith("Hash", StringComparison.Ordinal)
        || property.Contains("Token", StringComparison.Ordinal) || property.Contains("Secret", StringComparison.Ordinal)
        || property.Contains("Password", StringComparison.Ordinal) || property.Contains("ApiKey", StringComparison.Ordinal);
}
