using FourC.Cmms.Core.Ai;
using FourC.Cmms.Core.Auditing;
using FourC.Cmms.Core.Contracts;
using FourC.Cmms.Core.Files;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Inventory;
using FourC.Cmms.Core.Pm;
using FourC.Cmms.Core.Purchasing;
using FourC.Cmms.Core.Work;

namespace FourC.Cmms.Core.People;

/// <summary>
/// Every column holding a copy of a person's name (0.25.0), for anonymising someone who has left. A column with an id
/// beside it is replaced by that (user) id; a column that only kept the text is replaced where it equals the person's name.
/// A test fails when a new name column appears in neither list.
/// </summary>
public static class NameColumns
{
    public static readonly IReadOnlyList<(Type Entity, string Property, string? IdProperty)> All =
    [
        (typeof(WorkOrder), nameof(WorkOrder.RequestedBy), null),
        (typeof(WorkOrderTask), nameof(WorkOrderTask.DoneBy), null),
        (typeof(WorkOrderTask), nameof(WorkOrderTask.UpdatedByName), nameof(WorkOrderTask.UpdatedById)),
        (typeof(WorkOrderComment), nameof(WorkOrderComment.UserName), nameof(WorkOrderComment.UserId)),
        (typeof(WorkOrderStatusChange), nameof(WorkOrderStatusChange.ChangedByName), nameof(WorkOrderStatusChange.ChangedById)),
        (typeof(WorkOrderGroupComment), nameof(WorkOrderGroupComment.UserName), nameof(WorkOrderGroupComment.UserId)),
        (typeof(WorkRequest), nameof(WorkRequest.RequesterName), null),
        (typeof(WorkRequestComment), nameof(WorkRequestComment.UserName), nameof(WorkRequestComment.UserId)),
        (typeof(RequestFeedback), nameof(RequestFeedback.ByName), nameof(RequestFeedback.UserId)),
        (typeof(MeterReading), nameof(MeterReading.ByName), null),
        (typeof(MeasurementReading), nameof(MeasurementReading.ByName), nameof(MeasurementReading.ById)),
        (typeof(PurchaseOrder), nameof(PurchaseOrder.RequestedBy), null),
        (typeof(PurchaseOrder), nameof(PurchaseOrder.ApprovedByName), nameof(PurchaseOrder.ApprovedById)),
        (typeof(PurchaseOrderSignature), nameof(PurchaseOrderSignature.UserName), nameof(PurchaseOrderSignature.UserId)),
        (typeof(PurchaseRequest), nameof(PurchaseRequest.RequestedByName), nameof(PurchaseRequest.RequestedById)),
        (typeof(PurchaseRequest), nameof(PurchaseRequest.DecidedByName), nameof(PurchaseRequest.DecidedById)),
        (typeof(PurchaseRequestComment), nameof(PurchaseRequestComment.UserName), nameof(PurchaseRequestComment.UserId)),
        (typeof(ContractAmendment), nameof(ContractAmendment.CreatedByName), null),
        (typeof(StockCountLine), nameof(StockCountLine.CountedByName), nameof(StockCountLine.CountedById)),
        (typeof(StockTransaction), nameof(StockTransaction.CreatedByName), nameof(StockTransaction.CreatedById)),
        (typeof(UnitMovement), nameof(UnitMovement.ByName), nameof(UnitMovement.ByUserId)),
        (typeof(Attachment), nameof(Attachment.SignedBy), null),
        (typeof(AiCall), nameof(AiCall.UserName), nameof(AiCall.UserId)),
        (typeof(AiDiagnosis), nameof(AiDiagnosis.CreatedByName), nameof(AiDiagnosis.CreatedById)),
        (typeof(ApiKey), nameof(ApiKey.CreatedByName), nameof(ApiKey.CreatedById)),
        (typeof(FourC.Cmms.Core.Imports.ImportJob), nameof(FourC.Cmms.Core.Imports.ImportJob.UserName), nameof(FourC.Cmms.Core.Imports.ImportJob.UserId)),
    ];

    /// <summary>
    /// Columns that look like a name copy but aren't one to replace here: the activity log's own user name (rewritten with
    /// the log), the sign-in's user name (replaced when the account is closed) and an outside contractor's person.
    /// </summary>
    public static readonly IReadOnlyList<(Type Entity, string Property)> NotPeople =
    [
        (typeof(AuditEntry), nameof(AuditEntry.UserName)),
        (typeof(AppUser), nameof(AppUser.UserName)),
        (typeof(AppUser), nameof(AppUser.NormalizedUserName)),
        (typeof(WorkOrderContractor), nameof(WorkOrderContractor.PersonName)),
    ];
}
