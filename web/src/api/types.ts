import type { SlaState } from '@/components/sla';

export type Paged<T> = { items: T[]; total: number; page: number; pageSize: number };
export type AssetStatus = 'Active' | 'Inactive' | 'InStorage' | 'UnderRepair' | 'Disposed';
export type Criticality = 'Low' | 'Medium' | 'High' | 'Critical';
export type LocationType = 'Area' | 'Building' | 'Floor' | 'Room' | 'Zone' | 'ProductionLine' | 'Space' | 'Other';
export type WarrantyType = 'None' | 'Warranty' | 'ServiceContract';
export type CustomFieldType = 'Text' | 'Number' | 'Date' | 'Select' | 'Boolean' | 'LongText';

export const ASSET_STATUSES: AssetStatus[] = ['Active', 'Inactive', 'InStorage', 'UnderRepair', 'Disposed'];
export const CRITICALITIES: Criticality[] = ['Low', 'Medium', 'High', 'Critical'];
export const LOCATION_TYPES: LocationType[] = ['Area', 'Building', 'Floor', 'Room', 'Zone', 'ProductionLine', 'Space', 'Other'];
export const WARRANTY_TYPES: WarrantyType[] = ['None', 'Warranty', 'ServiceContract'];
export const FIELD_TYPES: CustomFieldType[] = ['Text', 'LongText', 'Number', 'Date', 'Select', 'Boolean'];

export type Terminology = { site: string; sites: string; location: string; locations: string; asset: string; assets: string };
export type TenantSettings = {
  terminology: Terminology; assetNumberPrefix: string; assetNumberPadding: number; autoNumberAssets: boolean;
  workOrderNumberPrefix: string; workRequestNumberPrefix: string; requireCompletionNotes: boolean; autoGeneratePm: boolean;
  purchaseOrderNumberPrefix: string; defaultTaxPercent: number; currency: string; dateFormat: string; timeZone: string;
  requireLaborToComplete: boolean; requireAllTasksDone: boolean; requireFailureCodeForBreakdowns: boolean; autoStartWork: boolean;
  emailEnabled: boolean; disabledEmails: string[];
  pmSkipAssetStatuses: AssetStatus[]; pmNonWorkingDays: DayName[]; pmNoStacking: boolean;
  /** Response and completion targets per priority, in minutes (0.5.0). */
  slaTargets: SlaTarget[]; dailyCapacityHours: number;
};
export type SlaTarget = { priority: WorkPriority; respondMinutes?: number | null; completeMinutes?: number | null };
/** Deadlines of one record and where it stands against them. */
export type SlaView = { response: SlaState; completion: SlaState; respondBy?: string | null; dueAt?: string | null; respondedAt?: string | null };
export type EmailStatusInfo = { available: boolean; allowed: boolean; slug: string; from?: string; enabled: boolean; events: string[] };
export type EmailLogItem = {
  id: string; createdAt: string; sentAt?: string; eventKey: string; toAddress: string; subject: string; status: 'Pending' | 'Sent' | 'Failed'; attempts: number; lastError?: string;
};
export type Me = {
  user: { id: string; fullName: string; email: string; jobTitle?: string };
  tenant: { id: string; name: string; slug: string; settings: TenantSettings };
  permissions: string[];
  /** Set when the account is linked to an employee: the technician view is for them. */
  employeeId?: string | null;
};

export type Site = {
  id: string; code: string; name: string; address?: string; city?: string; country?: string; timeZone?: string;
  latitude?: number; longitude?: number; notes?: string; isActive: boolean; locationCount: number; assetCount: number; rowVersion: string;
};
export type HierarchySite = { id: string; code: string; name: string; isActive: boolean; assetCount: number };
export type HierarchyLocation = {
  id: string; siteId: string; parentId?: string | null; code: string; name: string; type: LocationType;
  criticality: Criticality; isActive: boolean; assetCount: number;
};
export type Hierarchy = { sites: HierarchySite[]; locations: HierarchyLocation[] };
export type LocationDetail = {
  id: string; siteId: string; site: { id: string; code: string; name: string }; parentId?: string | null; code: string; name: string;
  type: LocationType; criticality: Criticality; description?: string; notesToTechnician?: string; latitude?: number; longitude?: number;
  isActive: boolean; depth: number; createdAt: string; updatedAt?: string; ancestors: { id: string; code: string; name: string }[];
  childCount: number; assetCount: number; subtreeAssetCount: number; rowVersion: string;
};

export type AssetListItem = {
  id: string; assetNumber: string; name: string; categoryId?: string; categoryName?: string; categoryColor?: string;
  siteId: string; siteName: string; locationId?: string; locationName?: string; locationPath?: string;
  parentAssetId?: string; parentAssetNumber?: string; status: AssetStatus; criticality: Criticality;
  manufacturer?: string; model?: string; serialNumber?: string; commissionedOn?: string; warrantyExpiresOn?: string;
  customFields: Record<string, string | null>; updatedAt?: string;
};
export type AssetDetail = {
  id: string; assetNumber: string; name: string; description?: string; categoryId?: string;
  category?: { id: string; code: string; name: string; color?: string }; siteId: string; site: { id: string; code: string; name: string };
  locationId?: string; locationTrail: { id: string; code: string; name: string }[]; parentAssetId?: string;
  parentAsset?: { id: string; assetNumber: string; name: string }; status: AssetStatus; criticality: Criticality;
  manufacturer?: string; model?: string; serialNumber?: string; barcode?: string; operationNumber?: string;
  commissionedOn?: string; disposedOn?: string; purchasePrice?: number; currentValue?: number; salvageValue?: number;
  expectedLifeYears?: number; downtimeCostPerHour?: number; warrantyType: WarrantyType; warrantyExpiresOn?: string;
  warrantyNotes?: string; notes?: string; notesToTechnician?: string; customFields: Record<string, string | null>;
  childCount: number; primaryImageId?: string; createdAt: string; createdBy?: string; updatedAt?: string; updatedBy?: string;
  legacyRef?: string; rowVersion: string;
};
export type Category = { id: string; code: string; name: string; description?: string; color?: string; isActive: boolean; assetCount: number };
export type CustomField = {
  id: string; entityType: string; key: string; label: string; dataType: CustomFieldType; options: string[];
  isRequired: boolean; sortOrder: number; categoryIds: string[]; showInList: boolean;
};
export type Attachment = { id: string; fileName: string; contentType: string; size: number; description?: string; isPrimaryImage: boolean; createdAt: string; createdBy?: string };
export type AuditItem = { id: string; action: string; changes?: string; userName?: string; timestamp: string };
export type Transfer = { id: string; transferredAt: string; reason?: string; fromSite?: string; fromLocation?: string; toSite?: string; toLocation?: string; transferredBy?: string };
export type User = { id: string; fullName: string; email: string; jobTitle?: string; phone?: string; isActive: boolean; lastLoginAt?: string; createdAt: string; roles: { roleId: string; name: string }[]; purchaseApprovalLimit?: number | null };
export type Role = { id: string; name: string; description?: string; isSystem: boolean; permissions: string[]; userCount: number };
export type PermissionInfo = { code: string; group: string; name: string };
export type Dashboard = {
  work: WorkDashboard;
  totals: { sites: number; locations: number; assets: number; criticalAssets: number; underRepair: number; warrantyExpiring90: number; assetValue: number };
  byStatus: { status: AssetStatus; count: number }[];
  byCriticality: { criticality: Criticality; count: number }[];
  byCategory: { categoryId?: string; name: string; color?: string; count: number }[];
  bySite: { siteId: string; name: string; count: number }[];
  warrantyExpiring: { id: string; assetNumber: string; name: string; warrantyExpiresOn: string; warrantyType: WarrantyType }[];
  recentAssets: { id: string; assetNumber: string; name: string; status: AssetStatus; createdAt: string; siteName: string }[];
};

// ---------------- People & work ----------------
export type WorkType = 'Corrective' | 'Preventive' | 'Breakdown' | 'Inspection' | 'Predictive' | 'Project' | 'Safety' | 'Other';
export type WorkPriority = 'Low' | 'Medium' | 'High' | 'Urgent';
export type WorkOrderStatus = 'Open' | 'InProgress' | 'OnHold' | 'Completed' | 'Closed' | 'Cancelled';
export type HoldReason = 'WaitingForParts' | 'WaitingForApproval' | 'WaitingForAccess' | 'WaitingForContractor' | 'Other';
export type WorkRequestStatus = 'New' | 'Approved' | 'Rejected' | 'Cancelled';
export const WORK_TYPES: WorkType[] = ['Corrective', 'Preventive', 'Breakdown', 'Inspection', 'Predictive', 'Project', 'Safety', 'Other'];
export const WORK_PRIORITIES: WorkPriority[] = ['Low', 'Medium', 'High', 'Urgent'];
export const WO_STATUSES: WorkOrderStatus[] = ['Open', 'InProgress', 'OnHold', 'Completed', 'Closed', 'Cancelled'];
export const HOLD_REASONS: HoldReason[] = ['WaitingForParts', 'WaitingForApproval', 'WaitingForAccess', 'WaitingForContractor', 'Other'];
export const WR_STATUSES: WorkRequestStatus[] = ['New', 'Approved', 'Rejected', 'Cancelled'];

export type Lookup = { id: string; code: string; name: string; color?: string; isActive: boolean };
export type Employee = {
  id: string; code: string; fullName: string; jobTitle?: string; email?: string; phone?: string; tradeIds: string[]; siteIds: string[];
  hourlyRate?: number; overtimeRate?: number; isContractor: boolean; company?: string; userId?: string; notes?: string; isActive: boolean;
  openWorkOrders: number; rowVersion: string;
};
export type EmployeeLookup = { id: string; code: string; fullName: string; tradeIds: string[]; isContractor: boolean; company?: string };
export type WorkOrderListItem = {
  id: string; number: string; title: string; type: WorkType; priority: WorkPriority; status: WorkOrderStatus; holdReason?: HoldReason;
  siteId: string; siteName: string; locationId?: string; locationPath?: string; assetId?: string; assetNumber?: string; assetName?: string;
  tradeName?: string; assignees: string[]; dueDate?: string; createdAt: string; completedAt?: string; isOverdue: boolean;
  dueAt?: string | null; startBy?: string | null; respondedAt?: string | null; responseState: SlaState; completionState: SlaState;
};
export type WorkOrderDetail = {
  id: string; number: string; title: string; description?: string; type: WorkType; priority: WorkPriority; status: WorkOrderStatus; holdReason?: HoldReason;
  siteId: string; site: { id: string; name: string }; locationId?: string; locationTrail: { id: string; name: string }[];
  assetId?: string; asset?: { id: string; assetNumber: string; name: string; serialNumber?: string; status: AssetStatus; criticality: Criticality; notesToTechnician?: string; warrantyType: WarrantyType; warrantyExpiresOn?: string; underWarranty: boolean };
  requestedBy?: string; preventionNotes?: string;
  tradeId?: string; tradeName?: string; failureCodeId?: string; failureCode?: { code: string; name: string };
  assignees: { employeeId: string; fullName: string; code: string; plannedHours?: number | null; hourlyRate?: number | null }[]; plannedStart?: string; dueDate?: string; estimatedHours?: number;
  startedAt?: string; completedAt?: string; closedAt?: string; completionNotes?: string; causeNotes?: string; assetWasDown: boolean; downtimeHours?: number;
  locationNotesToTechnician?: string; request?: { id: string; number: string; requesterName?: string };
  pm?: { id: string; code: string; name: string }; pmDueDate?: string;
  /** Assets a PM round work order covers, in order (empty otherwise). */
  assets: { id: string; assetNumber: string; name: string }[];
  tasks: { id: string; sequence: number; description: string; isDone: boolean; doneAt?: string; doneBy?: string; assetId?: string; reading?: string; remarks?: string }[];
  labor: { id: string; employeeId: string; employeeName: string; workDate: string; createdAt: string; hours: number; overtimeHours: number; rate: number; overtimeRate: number; notes?: string; cost: number }[];
  costs: { id: string; description: string; quantity: number; unitCost: number; vendor?: string; reference?: string; total: number; createdAt: string }[];
  parts: WoPartItem[];
  plannedParts: PlannedPart[];
  /** Signatures captured on completion (attachments): shown on the page and printed on the job card. */
  signatures: { id: string; signedBy?: string | null; createdAt: string }[];
  totals: { laborHours: number; laborCost: number; partsCost: number; otherCost: number; totalCost: number };
  planned: { laborHours: number; laborCost: number; partsCost: number; totalCost: number };
  dueAt?: string | null; dueFromTarget: boolean; startBy?: string | null; respondedAt?: string | null; sla: SlaView;
  allowedTransitions: WorkOrderStatus[]; isOverdue: boolean; createdAt: string; updatedAt?: string; legacyRef?: string; rowVersion: string;
};
/** A part planned on a work order: what is still reserved for it and how short its store is (stock goes to the earliest due job first). */
export type PlannedPart = {
  id: string; partId: string; partNumber: string; partName: string; unitOfMeasure: string; storeId?: string | null; storeCode?: string | null;
  quantity: number; issued: number; reserved: number; short: number; unitCost: number; plannedCost: number; notes?: string | null;
};
/** The 409 answer when a stock move would take stock reserved for other work. */
export type ReservedConflict = { title: string; reserved: { workOrderId: string; workOrderNumber: string; title?: string; moreShort: number }[] };
export type ActivityItem = { id: string; kind: 'comment' | 'status'; body?: string; from?: WorkOrderStatus; to?: WorkOrderStatus; holdReason?: HoldReason; by?: string; at: string };
export type WorkRequestListItem = {
  id: string; number: string; title: string; status: WorkRequestStatus; priority: WorkPriority; type: WorkType; siteId: string; siteName: string;
  locationName?: string; assetId?: string; assetNumber?: string; assetName?: string; requesterName?: string; requiredBy?: string; createdAt: string;
  workOrderId?: string; workOrderNumber?: string;
  respondBy?: string | null; dueAt?: string | null; responseState: SlaState; completionState: SlaState;
};
export type WorkRequestDetail = {
  id: string; number: string; title: string; description?: string; status: WorkRequestStatus; priority: WorkPriority; type: WorkType;
  siteId: string; site: { id: string; name: string }; locationId?: string; locationName?: string; assetId?: string; asset?: { id: string; assetNumber: string; name: string };
  requesterName?: string; requesterEmail?: string; requesterPhone?: string; requiredBy?: string; decisionNote?: string; decidedAt?: string; decidedBy?: string;
  workOrder?: { id: string; number: string; status: WorkOrderStatus }; createdAt: string; createdBy?: string; canCancel: boolean;
  respondBy?: string | null; dueAt?: string | null; dueFromTarget: boolean; sla: SlaView;
};
export type WorkDashboard = {
  open: number; overdue: number; completedLast30: number; requestsAwaiting: number; late: number; dueSoon: number; responseLate: number;
  byStatus: { status: WorkOrderStatus; count: number }[]; byPriority: { priority: WorkPriority; count: number }[];
  urgent: { id: string; number: string; title: string; priority: WorkPriority; status: WorkOrderStatus; dueDate?: string; assetNumber?: string }[];
};
export type AssetOpenWork = {
  status: AssetStatus; warrantyType: WarrantyType; warrantyExpiresOn?: string; underWarranty: boolean;
  openWorkOrders: { id: string; number: string; title: string; status: WorkOrderStatus; priority: WorkPriority }[];
  openRequests: { id: string; number: string; title: string }[];
};

// ---------------- Preventive maintenance ----------------
export type PmScheduleType = 'FixedInterval' | 'DayOfWeekInMonth' | 'DateOfMonth' | 'Meter';
export type PmBasis = 'Scheduled' | 'Actual';
export type FrequencyUnit = 'Day' | 'Week' | 'Month' | 'Year';
export type MeterTrigger = 'Incremental' | 'Threshold';
export type DayName = 'Sunday' | 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday';
export const PM_TYPES: PmScheduleType[] = ['FixedInterval', 'DayOfWeekInMonth', 'DateOfMonth', 'Meter'];
export const FREQ_UNITS: FrequencyUnit[] = ['Day', 'Week', 'Month', 'Year'];
export const DAY_NAMES: DayName[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

export type PmTaskListItem = { id: string; code: string; name: string; tradeId?: string; estimatedHours?: number; isActive: boolean; stepCount: number; scheduleCount: number };
export type PmTaskPart = { partId: string; partNumber: string; partName: string; unitOfMeasure: string; quantity: number; unitCost: number };
export type PmTaskDetail = { id: string; code: string; name: string; description?: string; tradeId?: string; estimatedHours?: number; safetyNotes?: string; isActive: boolean; steps: string[];
  parts: PmTaskPart[]; partsCost: number; rowVersion: string };
/** Estimated cost of one PM visit: parts at current average cost plus estimated hours at the technicians' average rate. */
export type VisitCost = { parts: number; labour: number; total: number };
export type PmScheduleListItem = {
  id: string; code: string; name: string; type: PmScheduleType; basis: PmBasis; isActive: boolean; siteId: string; siteName: string; assetId?: string; assetNumber?: string;
  assetName?: string; locationName?: string; taskCode: string; taskName: string; schedule: string; nextDueDate?: string; nextDueReading?: number; meterUnit?: string;
  meterReading?: number; lastGeneratedAt?: string; lastWorkOrderId?: string; lastWorkOrderNumber?: string; openWorkOrders: number; isOverdue: boolean; multipleTasks: number;
  /** Number of assets a round covers; 0 for single-asset/location schedules. */
  assetCount: number;
};
export type PmScheduleDetail = {
  id: string; code: string; name: string; description?: string; siteId: string; locationId?: string; assetId?: string; asset?: { id: string; assetNumber: string; name: string };
  /** A round's assets in order (empty for single-asset/location schedules). */
  assets: { id: string; assetNumber: string; name: string }[];
  tradeId?: string; priority: WorkPriority; assigneeIds: string[]; pmTaskId: string; task: { id: string; code: string; name: string };
  extraTasks: { pmTaskId: string; everyNth: number }[]; type: PmScheduleType; basis: PmBasis; frequency: number; unit: FrequencyUnit;
  weekOfMonth?: number; dayOfWeek?: DayName; dayOfMonth?: number; workPeriodDays: number; wholeMonth: boolean; leadDays: number; startDate: string;
  nextDueDate?: string; lastDueDate?: string; lastGeneratedAt?: string; generationCount: number; meterId?: string;
  meter?: { id: string; code: string; name: string; unit: string; currentReading?: number }; meterTrigger?: MeterTrigger; meterInterval?: number;
  nextDueReading?: number; lowerLimit?: number; upperLimit?: number; lowerTaskId?: string; upperTaskId?: string; timeBased: boolean; isActive: boolean;
  schedule: string; upcoming: string[]; recentWorkOrders: { id: string; number: string; title: string; status: WorkOrderStatus; pmDueDate?: string; completedAt?: string }[];
  visitCost: VisitCost; legacyRef?: string; rowVersion: string;
};
export type PmRunResult = { scheduleId: string; code: string; name: string; dueDate?: string; taskName?: string; assetNumber?: string; workOrderId?: string; workOrderNumber?: string; skipped?: string };
export type ForecastItem = { date: string; id: string; code: string; name: string; assetNumber?: string; siteName: string; type: PmScheduleType; taskName: string; overdue: boolean;
  /** The schedule's next occurrence: generating it creates no other work order. */
  isNext: boolean;
  /** Estimated cost of the visit. */
  cost: number };
export type MeterItem = { id: string; code: string; name: string; assetId: string; assetNumber: string; assetName: string; unit: string; currentReading?: number; lastReadAt?: string; maxDailyIncrement?: number; isActive: boolean; schedules: number };
export type MeterReadingItem = { id: string; value: number; readAt: string; note?: string; source: string; byName?: string; workOrderId?: string };

// ---------------- Inventory ----------------
export type StockTransactionType = 'Receipt' | 'Issue' | 'Return' | 'Adjustment' | 'TransferOut' | 'TransferIn';
export type StoreItem = { id: string; code: string; name: string; siteId: string; siteName: string; description?: string; isActive: boolean; parts: number; stockValue: number };
export type PartListItem = {
  id: string; partNumber: string; name: string; manufacturer?: string; unitOfMeasure: string; groupName?: string; averageCost: number; isCritical: boolean; isShelfItem: boolean;
  isActive: boolean; onHand: number; reserved: number; available: number; onOrder: number; low: boolean; value: number;
};
export type PartLookup = { id: string; partNumber: string; name: string; unitOfMeasure: string; averageCost: number; lastPurchasePrice?: number; isShelfItem: boolean; preferredSupplierId?: string; onHand: number };
export type StockLevelItem = { id: string; storeId: string; storeCode: string; storeName: string; bin?: string; minLevel: number; reorderLevel: number; maxLevel: number; reorderQuantity: number; quantityOnHand: number;
  /** Reserved for open work orders' planned parts; available = on hand − reserved (may be negative). */
  reserved: number; available: number };
export type PartDetail = {
  id: string; partNumber: string; name: string; specification?: string; manufacturer?: string; manufacturerPartNumber?: string; unitOfMeasure: string; groupId?: string; groupName?: string;
  standardPrice: number; averageCost: number; lastPurchasePrice?: number; leadTimeDays?: number; isCritical: boolean; isShelfItem: boolean; allowNegativeStock: boolean;
  preferredSupplierId?: string; preferredSupplier?: { id: string; code: string; name: string }; barcode?: string; alternatePartIds: string[];
  alternates: { id: string; partNumber: string; name: string }[]; notes?: string; isActive: boolean; levels: StockLevelItem[];
  batches: { storeId: string; batchNumber: string; quantity: number; expiry?: string }[]; onHand: number; reserved: number; available: number; onOrder: number; stockValue: number;
  /** Open work orders holding this part, in the order stock goes to them (earliest due first). */
  reservedFor: { workOrderId: string; workOrderNumber: string; title?: string; storeCode: string; reserved: number; short: number; dueDate?: string }[];
  legacyRef?: string; rowVersion: string;
};
export type StockTransactionItem = {
  id: string; type: StockTransactionType; date: string; partId: string; partNumber: string; partName: string; unit: string; storeId: string; storeCode: string;
  quantity: number; unitCost: number; total: number; workOrderId?: string; workOrderNumber?: string; purchaseOrderId?: string; reference?: string; batchNumber?: string;
  expiryDate?: string; notes?: string; createdByName?: string; createdAt: string;
};
export type ProposalItem = {
  partId: string; partNumber: string; partName: string; unitOfMeasure: string; isCritical: boolean; storeId: string; storeCode: string; siteId: string; quantityOnHand: number;
  reserved: number; available: number; onOrder: number; minLevel: number; reorderLevel: number; maxLevel: number; suggestedQuantity: number; preferredSupplierId?: string; supplierName?: string; unitPrice: number; belowMinimum: boolean;
};
export type WoPartItem = { id: string; type: StockTransactionType; date: string; partId: string; partNumber: string; partName: string; unitOfMeasure: string; storeId: string; storeCode: string; quantity: number; unitCost: number; total: number; batchNumber?: string; notes?: string; createdByName?: string };

// ---------------- Purchasing ----------------
export type PurchaseOrderStatus = 'Draft' | 'Submitted' | 'Approved' | 'PartiallyReceived' | 'Received' | 'Closed' | 'Cancelled';
export const PO_STATUSES: PurchaseOrderStatus[] = ['Draft', 'Submitted', 'Approved', 'PartiallyReceived', 'Received', 'Closed', 'Cancelled'];
export type SupplierItem = { id: string; code: string; name: string; contactName?: string; email?: string; phone?: string; city?: string; country?: string; currency?: string; paymentTerms?: string; isContractor: boolean; isActive: boolean; openOrders: number; receivedYtd: number };
export type SupplierDetail = { id: string; code: string; name: string; contactName?: string; email?: string; phone?: string; address?: string; city?: string; country?: string; taxNumber?: string; paymentTerms?: string; currency?: string; isContractor: boolean; notes?: string; isActive: boolean };
export type SupplierLookup = { id: string; code: string; name: string; currency?: string; paymentTerms?: string };
export type PoListItem = { id: string; number: string; status: PurchaseOrderStatus; orderDate: string; expectedDate?: string; supplierId: string; supplierName: string; siteName: string; currency: string; requestedBy?: string; lines: number; total: number; receivedPercent: number; isOverdue: boolean };
export type PoLine = {
  id: string; lineNumber: number; partId?: string; partNumber?: string; isShelfItem: boolean; description: string; unitOfMeasure?: string; quantity: number; unitPrice: number; discountPercent: number;
  netUnitPrice: number; lineTotal: number; quantityReceived: number; outstanding: number; storeId?: string; storeCode?: string; workOrderId?: string; workOrderNumber?: string; accountCode?: string;
};
export type PoDetail = {
  id: string; number: string; status: PurchaseOrderStatus; siteId: string; siteName: string; supplierId: string;
  supplier: { id: string; code: string; name: string; contactName?: string; email?: string; phone?: string; address?: string; city?: string; country?: string; taxNumber?: string };
  orderDate: string; expectedDate?: string; requestedBy?: string; currency: string; exchangeRate: number; paymentTerms?: string; shippingMethod?: string; deliverTo?: string; supplierQuoteRef?: string;
  taxPercent: number; shippingCost: number; notes?: string; submittedAt?: string; approvedAt?: string; approvedByName?: string; cancelledAt?: string; cancelReason?: string; createdAt: string; legacyRef?: string;
  lines: PoLine[]; totals: { subtotal: number; tax: number; shippingCost: number; total: number; totalInBase: number };
  receipts: { id: string; date: string; purchaseOrderLineId?: string; partNumber: string; storeCode: string; quantity: number; unitCost: number; reference?: string; batchNumber?: string; createdByName?: string }[];
  isOverdue: boolean; actions: { edit: boolean; submit: boolean; approve: boolean; approvalLimitExceeded: boolean; reject: boolean; receive: boolean; close: boolean; cancel: boolean; delete: boolean };
  rowVersion: string;
};

// ---------------- Reports ----------------
export type ReportParam = { name: string; type: 'dateRange' | 'site' | 'select' | 'number'; options?: { value: string; label: string }[]; default?: string | number };
export type ReportDef = { key: string; category: string; title: string; description: string; permission: string; params: ReportParam[] };
export type ReportColumnType = 'text' | 'number' | 'currency' | 'percent' | 'date' | 'hours';
export type ReportResult = {
  key: string; title: string; subtitle?: string; columns: { key: string; label: string; type: ReportColumnType }[]; rows: Record<string, string | number | null>[];
  chart?: { type: 'bar' | 'line' | 'donut'; x: string; series: string[]; stacked: boolean }; summary: { label: string; value: number | null; type: ReportColumnType }[];
};
