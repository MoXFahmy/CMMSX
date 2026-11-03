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
  currency: string; dateFormat: string; timeZone: string;
};
export type Me = {
  user: { id: string; fullName: string; email: string; jobTitle?: string };
  tenant: { id: string; name: string; slug: string; settings: TenantSettings };
  permissions: string[];
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
export type User = { id: string; fullName: string; email: string; jobTitle?: string; phone?: string; isActive: boolean; lastLoginAt?: string; createdAt: string; roles: { roleId: string; name: string }[] };
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
};
export type WorkOrderDetail = {
  id: string; number: string; title: string; description?: string; type: WorkType; priority: WorkPriority; status: WorkOrderStatus; holdReason?: HoldReason;
  siteId: string; site: { id: string; name: string }; locationId?: string; locationTrail: { id: string; name: string }[];
  assetId?: string; asset?: { id: string; assetNumber: string; name: string; status: AssetStatus; criticality: Criticality; notesToTechnician?: string; warrantyType: WarrantyType; warrantyExpiresOn?: string; underWarranty: boolean };
  requestedBy?: string; preventionNotes?: string;
  tradeId?: string; tradeName?: string; failureCodeId?: string; failureCode?: { code: string; name: string };
  assignees: { employeeId: string; fullName: string; code: string }[]; plannedStart?: string; dueDate?: string; estimatedHours?: number;
  startedAt?: string; completedAt?: string; closedAt?: string; completionNotes?: string; causeNotes?: string; assetWasDown: boolean; downtimeHours?: number;
  locationNotesToTechnician?: string; request?: { id: string; number: string; requesterName?: string };
  pm?: { id: string; code: string; name: string }; pmDueDate?: string;
  tasks: { id: string; sequence: number; description: string; isDone: boolean; doneAt?: string; doneBy?: string }[];
  labor: { id: string; employeeId: string; employeeName: string; workDate: string; createdAt: string; hours: number; overtimeHours: number; rate: number; overtimeRate: number; notes?: string; cost: number }[];
  costs: { id: string; description: string; quantity: number; unitCost: number; vendor?: string; reference?: string; total: number; createdAt: string }[];
  totals: { laborHours: number; laborCost: number; otherCost: number; totalCost: number };
  allowedTransitions: WorkOrderStatus[]; isOverdue: boolean; createdAt: string; updatedAt?: string; legacyRef?: string; rowVersion: string;
};
export type ActivityItem = { id: string; kind: 'comment' | 'status'; body?: string; from?: WorkOrderStatus; to?: WorkOrderStatus; holdReason?: HoldReason; by?: string; at: string };
export type WorkRequestListItem = {
  id: string; number: string; title: string; status: WorkRequestStatus; priority: WorkPriority; type: WorkType; siteId: string; siteName: string;
  locationName?: string; assetId?: string; assetNumber?: string; assetName?: string; requesterName?: string; requiredBy?: string; createdAt: string;
  workOrderId?: string; workOrderNumber?: string;
};
export type WorkRequestDetail = {
  id: string; number: string; title: string; description?: string; status: WorkRequestStatus; priority: WorkPriority; type: WorkType;
  siteId: string; site: { id: string; name: string }; locationId?: string; locationName?: string; assetId?: string; asset?: { id: string; assetNumber: string; name: string };
  requesterName?: string; requesterEmail?: string; requesterPhone?: string; requiredBy?: string; decisionNote?: string; decidedAt?: string; decidedBy?: string;
  workOrder?: { id: string; number: string; status: WorkOrderStatus }; createdAt: string; createdBy?: string; canCancel: boolean;
};
export type WorkDashboard = {
  open: number; overdue: number; completedLast30: number; requestsAwaiting: number;
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
export type PmTaskDetail = { id: string; code: string; name: string; description?: string; tradeId?: string; estimatedHours?: number; safetyNotes?: string; isActive: boolean; steps: string[]; rowVersion: string };
export type PmScheduleListItem = {
  id: string; code: string; name: string; type: PmScheduleType; basis: PmBasis; isActive: boolean; siteId: string; siteName: string; assetId?: string; assetNumber?: string;
  assetName?: string; locationName?: string; taskCode: string; taskName: string; schedule: string; nextDueDate?: string; nextDueReading?: number; meterUnit?: string;
  meterReading?: number; lastGeneratedAt?: string; lastWorkOrderId?: string; lastWorkOrderNumber?: string; openWorkOrders: number; isOverdue: boolean; multipleTasks: number;
};
export type PmScheduleDetail = {
  id: string; code: string; name: string; description?: string; siteId: string; locationId?: string; assetId?: string; asset?: { id: string; assetNumber: string; name: string };
  tradeId?: string; priority: WorkPriority; assigneeIds: string[]; pmTaskId: string; task: { id: string; code: string; name: string };
  extraTasks: { pmTaskId: string; everyNth: number }[]; type: PmScheduleType; basis: PmBasis; frequency: number; unit: FrequencyUnit;
  weekOfMonth?: number; dayOfWeek?: DayName; dayOfMonth?: number; workPeriodDays: number; wholeMonth: boolean; leadDays: number; startDate: string;
  nextDueDate?: string; lastDueDate?: string; lastGeneratedAt?: string; generationCount: number; meterId?: string;
  meter?: { id: string; code: string; name: string; unit: string; currentReading?: number }; meterTrigger?: MeterTrigger; meterInterval?: number;
  nextDueReading?: number; lowerLimit?: number; upperLimit?: number; lowerTaskId?: string; upperTaskId?: string; timeBased: boolean; isActive: boolean;
  schedule: string; upcoming: string[]; recentWorkOrders: { id: string; number: string; title: string; status: WorkOrderStatus; pmDueDate?: string; completedAt?: string }[];
  legacyRef?: string; rowVersion: string;
};
export type PmRunResult = { scheduleId: string; code: string; name: string; dueDate?: string; taskName?: string; assetNumber?: string; workOrderId?: string; workOrderNumber?: string; skipped?: string };
export type ForecastItem = { date: string; id: string; code: string; name: string; assetNumber?: string; siteName: string; type: PmScheduleType; taskName: string; overdue: boolean };
export type MeterItem = { id: string; code: string; name: string; assetId: string; assetNumber: string; assetName: string; unit: string; currentReading?: number; lastReadAt?: string; maxDailyIncrement?: number; isActive: boolean; schedules: number };
export type MeterReadingItem = { id: string; value: number; readAt: string; note?: string; source: string; byName?: string; workOrderId?: string };
