import type { HomeCard } from '@/pages/home/cards';
import type { SlaState } from '@/components/sla';
import type { WorkingDay } from '@/components/workingHours';

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
/** 0.21.0: an organization's wording for one value of a fixed list (names, colour, order, hidden). */
export type ListLabel = { name?: string | null; nameAr?: string | null; color?: string | null; order?: number | null; hidden?: boolean };
export type ListKey = 'workType' | 'priority' | 'assetStatus' | 'criticality';
export type ListLabels = Partial<Record<ListKey, Record<string, ListLabel>>>;

export type TenantSettings = {
  /** 0.21.0 */
  listLabels?: ListLabels; restartNumbersYearly?: boolean;
  terminology: Terminology; terminologyArabic?: Terminology; defaultLanguage?: 'en' | 'ar'; assetNumberPrefix: string; assetNumberPadding: number; autoNumberAssets: boolean;
  workOrderNumberPrefix: string; workRequestNumberPrefix: string; requireCompletionNotes: boolean; autoGeneratePm: boolean;
  purchaseOrderNumberPrefix: string; purchaseRequestNumberPrefix?: string; purchaseRequestRecipientIds?: string[]; defaultTaxPercent: number; currency: string; dateFormat: string; timeZone: string;
  requireLaborToComplete: boolean; requireAllTasksDone: boolean; requireFailureCodeForBreakdowns: boolean; autoStartWork: boolean;
  /** Smart checklists (0.13.0): what a failed step raises by default, its template, and readings needed to complete. */
  followUpDefault?: FollowUpKind; followUpTemplateId?: string | null; requireReadingSteps?: boolean;
  /** The organization's logo (0.14.0): when it was uploaded, or null when there is none. */
  logoVersion?: number | null;
  /** The AI assistant (0.15.0): on for this organization, and its monthly limit (empty = the server's ceiling). */
  aiEnabled?: boolean; aiMonthlyLimit?: number | null;
  emailEnabled: boolean; disabledEmails: string[];
  /** Who gets each work and request e-mail (0.10.0): event key → recipient kinds; an event not listed uses its defaults. */
  emailRecipients?: Record<string, string[]>;
  /** Actions that need a comment (0.10.0): reject, refuse, hold, reopen, cancel; completing follows requireCompletionNotes. */
  commentRequiredFor?: string[];
  pmSkipAssetStatuses: AssetStatus[]; pmNonWorkingDays: DayName[]; pmNoStacking: boolean;
  /** Response and completion targets per priority, in minutes (0.5.0). */
  slaTargets: SlaTarget[]; dailyCapacityHours: number;
  /** The organization's weekly working hours (0.9.0); empty = every day from 08:00 for dailyCapacityHours. */
  workingHours?: WorkingDay[];
};
export type SlaTarget = { priority: WorkPriority; respondMinutes?: number | null; completeMinutes?: number | null };
/** Deadlines of one record and where it stands against them. */
export type SlaView = { response: SlaState; completion: SlaState; respondBy?: string | null; dueAt?: string | null; respondedAt?: string | null };
export type EmailStatusInfo = {
  available: boolean; allowed: boolean; slug: string; from?: string; enabled: boolean; events: string[];
  grid: { key: string; applicable: string[]; defaults: string[] }[];
};
/** A request type (0.10.0): default trade and priority, recipients, optional automatic approval. */
export type RequestType = {
  id: string; code: string; name: string; isActive: boolean; defaultTradeId?: string | null; defaultPriority?: WorkPriority | null;
  recipientIds: string[]; autoApprove: boolean; teamId?: string | null;
};
/** When the work may be done (0.10.0). */
export type AccessWindow = { start: string; end: string; note?: string | null };
/** The requester's answer to finished work (0.10.0). */
export type RequestFeedback = { id?: string; accepted: boolean; rating?: number | null; comment?: string | null; byName?: string | null; answeredAt: string; viaLink?: boolean };
/** A message on a request's thread (0.10.0). */
export type RequestComment = { id: string; body: string; userName?: string | null; byRequester: boolean; createdAt: string };
export type EmailLogItem = {
  id: string; createdAt: string; sentAt?: string; eventKey: string; toAddress: string; subject: string; status: 'Pending' | 'Sent' | 'Failed'; attempts: number; lastError?: string;
};
export type Me = {
  user: { id: string; fullName: string; email: string; jobTitle?: string; language: 'en' | 'ar'; languageChoice?: 'en' | 'ar' | null };
  tenant: { id: string; name: string; slug: string; settings: TenantSettings };
  permissions: string[];
  /** Set when the account is linked to an employee: the technician view is for them. */
  employeeId?: string | null;
  /** The sites this person may see (for the switch), whether they are limited, and the site this request was narrowed to (0.9.0). */
  access?: { limited: boolean; branches: boolean; sites: { id: string; code: string; name: string; isActive: boolean }[]; siteId?: string | null };
  /** The home page cards of all the person's roles and the menu items they hide (0.22.0). */
  home?: { cards: HomeCard[]; hiddenModules: string[] };
};

export type Site = {
  id: string; code: string; name: string; address?: string; city?: string; country?: string; timeZone?: string;
  latitude?: number; longitude?: number; notes?: string; isActive: boolean; locationCount: number; assetCount: number; rowVersion: string;
  /** The site's own settings (0.9.0); empty values use the organization's. */
  settings?: SiteSettings;
};
export type SiteSettings = {
  assetNumberPrefix?: string | null; workOrderNumberPrefix?: string | null; workRequestNumberPrefix?: string | null; purchaseOrderNumberPrefix?: string | null; purchaseRequestNumberPrefix?: string | null; purchaseRequestRecipientIds?: string[];
  workingHours?: WorkingDay[] | null; defaultStoreId?: string | null; requestRecipientIds?: string[]; emailSenderName?: string | null;
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
  childCount: number; assetCount: number; subtreeAssetCount: number; rowVersion: string; costCentreId?: string | null;
};

export type AssetListItem = {
  id: string; assetNumber: string; name: string; categoryId?: string; categoryName?: string; categoryColor?: string;
  siteId: string; siteName: string; locationId?: string; locationName?: string; locationPath?: string;
  parentAssetId?: string; parentAssetNumber?: string; status: AssetStatus; criticality: Criticality;
  manufacturer?: string; model?: string; serialNumber?: string; commissionedOn?: string; warrantyExpiresOn?: string;
  customFields: Record<string, string | null>; updatedAt?: string;
  /** When the asset's open stop began; empty while it runs. */
  downSince?: string | null;
};
export type AssetDetail = {
  onlyWhenStopped?: boolean; costCentreId?: string | null;
  /** 0.21.0 */
  brandId?: string | null; modelId?: string | null; groupIds?: string[];
  id: string; assetNumber: string; name: string; description?: string; categoryId?: string;
  category?: { id: string; code: string; name: string; color?: string; downtimeCostPerHour?: number | null }; siteId: string; site: { id: string; code: string; name: string };
  locationId?: string; locationTrail: { id: string; code: string; name: string }[]; parentAssetId?: string;
  parentAsset?: { id: string; assetNumber: string; name: string }; status: AssetStatus; criticality: Criticality;
  manufacturer?: string; model?: string; serialNumber?: string; barcode?: string; operationNumber?: string;
  commissionedOn?: string; disposedOn?: string; purchasePrice?: number; currentValue?: number; salvageValue?: number;
  expectedLifeYears?: number; downtimeCostPerHour?: number; warrantyType: WarrantyType; warrantyExpiresOn?: string;
  warrantyNotes?: string; notes?: string; notesToTechnician?: string; customFields: Record<string, string | null>;
  childCount: number; primaryImageId?: string; createdAt: string; createdBy?: string; updatedAt?: string; updatedBy?: string;
  legacyRef?: string; rowVersion: string;
  runsHoursPerDay: number; runsOn: DayName[]; downSince?: string | null;
  /** 0.24.0: a unit of a serial-tracked part — its part, its store when in one, its end of life and where it is. */
  partId?: string | null; partNumber?: string | null; partName?: string | null; storeId?: string | null; storeName?: string | null; endOfLifeOn?: string | null;
  unitState?: UnitState | null;
};
export type DowntimeSource = 'Request' | 'WorkOrder' | 'Manual';
/** A stop of an asset: no end while it is still down. */
export type DowntimeStop = {
  id: string; startedAt: string; endedAt?: string | null; hours: number; reason?: string | null; source: DowntimeSource; sourceNumber?: string | null;
  links: { kind: 'request' | 'workOrder'; id: string; number: string }[];
};
export type AssetDowntime = {
  downSince?: string | null; runsHoursPerDay: number; runsOn: DayName[];
  availability30?: number | null; plannedHours30: number; downHours30: number; stops: DowntimeStop[];
};
export type AssetPartLine = { partId: string; partNumber: string; name: string; unitOfMeasure: string; quantity: number; note?: string | null };
export type Category = { id: string; code: string; name: string; description?: string; color?: string; isActive: boolean; assetCount: number; downtimeCostPerHour?: number | null };
export type CustomField = {
  id: string; entityType: string; key: string; label: string; dataType: CustomFieldType; options: string[];
  isRequired: boolean; sortOrder: number; categoryIds: string[]; showInList: boolean;
};
export type Attachment = { id: string; fileName: string; contentType: string; size: number; description?: string; isPrimaryImage: boolean; createdAt: string; createdBy?: string };
export type AuditItem = { id: string; action: string; changes?: string; userName?: string; timestamp: string };
export type Transfer = { id: string; transferredAt: string; reason?: string; fromSite?: string; fromLocation?: string; toSite?: string; toLocation?: string; transferredBy?: string };
export type User = {
  id: string; fullName: string; email: string; jobTitle?: string; phone?: string; isActive: boolean; lastLoginAt?: string; createdAt: string;
  roles: { roleId: string; name: string }[]; purchaseApprovalLimit?: number | null;
  /** Sites this person may see (empty = every site) and branches of the location tree they are limited to (0.9.0). */
  siteIds?: string[]; locationIds?: string[];
};
/** A named group of technicians that work can be given to (0.9.0). */
export type Team = {
  id: string; code: string; name: string; siteId?: string | null; siteName?: string | null; color?: string | null; memberIds: string[]; isActive: boolean; rowVersion: string;
};
export type Role = {
  id: string; name: string; description?: string; isSystem: boolean; permissions: string[]; userCount: number;
  /** Its home page cards, its hidden menu items and the lists it has default columns on (0.22.0). */
  home?: HomeCard[] | null; hiddenModules?: string[]; defaultColumns?: string[];
};
export type PermissionInfo = { code: string; group: string; name: string };
export type Dashboard = {
  work: WorkDashboard;
  totals: { sites: number; locations: number; assets: number; criticalAssets: number; underRepair: number; warrantyExpiring90: number; assetValue: number; assetsDown: number };
  byStatus: { status: AssetStatus; count: number }[];
  byCriticality: { criticality: Criticality; count: number }[];
  byCategory: { categoryId?: string; name: string; color?: string; count: number }[];
  bySite: { siteId: string; name: string; count: number }[];
  /** For people who see several sites: the work at each (0.9.0). */
  bySiteWork?: { siteId: string; name: string; open: number; late: number; completedLast30: number; requestsWaiting: number }[];
  warrantyExpiring: { id: string; assetNumber: string; name: string; warrantyExpiresOn: string; warrantyType: WarrantyType }[];
  recentAssets: { id: string; assetNumber: string; name: string; status: AssetStatus; createdAt: string; siteName: string }[];
};

// ---------------- People & work ----------------
export type WorkType = 'Corrective' | 'Preventive' | 'Breakdown' | 'Inspection' | 'Predictive' | 'Project' | 'Safety' | 'Other';
export type WorkPriority = 'Low' | 'Medium' | 'High' | 'Urgent';
export type WorkOrderStatus = 'Open' | 'InProgress' | 'OnHold' | 'Completed' | 'Closed' | 'Cancelled';
export type HoldReason = 'WaitingForParts' | 'WaitingForApproval' | 'WaitingForAccess' | 'WaitingForContractor' | 'Other';
export type WorkRequestStatus = 'New' | 'Approved' | 'Rejected' | 'Cancelled' | 'Closed';
export const WORK_TYPES: WorkType[] = ['Corrective', 'Preventive', 'Breakdown', 'Inspection', 'Predictive', 'Project', 'Safety', 'Other'];
export const WORK_PRIORITIES: WorkPriority[] = ['Low', 'Medium', 'High', 'Urgent'];
export const WO_STATUSES: WorkOrderStatus[] = ['Open', 'InProgress', 'OnHold', 'Completed', 'Closed', 'Cancelled'];
export const HOLD_REASONS: HoldReason[] = ['WaitingForParts', 'WaitingForApproval', 'WaitingForAccess', 'WaitingForContractor', 'Other'];
export const WR_STATUSES: WorkRequestStatus[] = ['New', 'Approved', 'Closed', 'Rejected', 'Cancelled'];

export type Lookup = { id: string; code: string; name: string; color?: string; isActive: boolean; /** Availability types (0.19.0): Away or Extra. */ kind?: string };
export type Employee = {
  id: string; code: string; fullName: string; jobTitle?: string; email?: string; phone?: string; tradeIds: string[]; siteIds: string[];
  hourlyRate?: number; overtimeRate?: number; isContractor: boolean; company?: string; userId?: string; notes?: string; isActive: boolean;
  /** The contractor company this person works for (0.17.0); their hours count as contractor cost. */
  companyId?: string; companyName?: string;
  openWorkOrders: number; rowVersion: string;
  /** Their own weekly working hours (0.9.0); none = their site's or the organization's. */
  workingHours?: WorkingDay[] | null;
};
export type EmployeeLookup = { id: string; code: string; fullName: string; tradeIds: string[]; isContractor: boolean; company?: string };
export type WorkOrderListItem = {
  id: string; number: string; title: string; type: WorkType; priority: WorkPriority; status: WorkOrderStatus; holdReason?: HoldReason;
  siteId: string; siteName: string; locationId?: string; locationPath?: string; assetId?: string; assetNumber?: string; assetName?: string;
  tradeName?: string; teamName?: string | null; cancelReasonName?: string | null; onlyWhenStopped?: boolean; assignees: string[]; dueDate?: string; createdAt: string; completedAt?: string; isOverdue: boolean;
  dueAt?: string | null; startBy?: string | null; respondedAt?: string | null; responseState: SlaState; completionState: SlaState;
  /** Its group (0.12.0). */
  groupId?: string | null; groupNumber?: string | null;
  /** Codes (0.14.0). */
  symptomName?: string | null; causeName?: string | null; remedyName?: string | null;
};
/** A work order's checklist step (0.13.0 adds its type, answer, photos and follow-up). */
export type WorkOrderStep = { id: string; sequence: number; description: string; isDone: boolean; doneAt?: string; doneBy?: string; assetId?: string; reading?: string;
  remarks?: string; updatedAt?: string; updatedByName?: string; type: import('@/components/measure').StepType; unit?: string | null; decimals?: number | null;
  measurementPointId?: string | null; meterId?: string | null; meterName?: string | null; photoRequired: boolean; beforeStart: boolean; followUpKind: FollowUpKind;
  answer?: import('@/components/measure').StepAnswer | null; value?: number | null; textValue?: string | null; level?: import('@/components/measure').MeasureLevel | null;
  photos: { id: string; fileName: string; contentType: string }[]; followUp?: FollowUpLink | null };
export type WorkOrderDetail = {
  /** 0.20.0: its cost centre and the path "Production › Line 1". */
  costCentreId?: string | null; costCentrePath?: string | null;
  id: string; number: string; title: string; description?: string; type: WorkType; priority: WorkPriority; status: WorkOrderStatus; holdReason?: HoldReason;
  siteId: string; site: { id: string; name: string }; locationId?: string; locationTrail: { id: string; name: string }[];
  assetId?: string; asset?: { id: string; assetNumber: string; name: string; serialNumber?: string; status: AssetStatus; criticality: Criticality; notesToTechnician?: string; warrantyType: WarrantyType; warrantyExpiresOn?: string; underWarranty: boolean };
  requestedBy?: string; preventionNotes?: string;
  tradeId?: string; tradeName?: string; failureCodeId?: string; failureCode?: CodeRef | null;
  /** What was seen and what was done (0.14.0); the failure code is the cause. */
  symptomId?: string | null; symptom?: CodeRef | null; remedyId?: string | null; remedy?: CodeRef | null;
  teamId?: string | null; team?: { id: string; code: string; name: string; color?: string | null } | null;
  assignees: { employeeId: string; fullName: string; code: string; plannedHours?: number | null; hourlyRate?: number | null }[]; plannedStart?: string; dueDate?: string; estimatedHours?: number;
  startedAt?: string; completedAt?: string; closedAt?: string; completionNotes?: string; causeNotes?: string; assetWasDown: boolean; downtimeHours?: number;
  /** The asset stop this work order is about (completing it ends the stop), and whether the asset is down now. */
  downtime?: { id: string; startedAt: string; endedAt?: string | null; source: DowntimeSource; sourceNumber?: string | null } | null; assetDownSince?: string | null;
  locationNotesToTechnician?: string;
  request?: { id: string; number: string; requesterName?: string; status?: WorkRequestStatus; awaitingFeedback?: boolean; feedback?: RequestFeedback | null; comments?: RequestComment[] };
  /** 0.10.0 */
  accessWindows?: AccessWindow[]; onlyWhenStopped?: boolean; outsideWindows?: boolean; cancelReason?: { id: string; code: string; name: string } | null;
  pm?: { id: string; code: string; name: string }; pmDueDate?: string;
  /** Assets a PM round work order covers, in order (empty otherwise). */
  assets: { id: string; assetNumber: string; name: string }[];
  tasks: WorkOrderStep[];
  /** Smart checklists (0.13.0): the points its steps use, and the work order and step a follow-up came from. */
  points?: PointView[]; source?: StepSource | null;
  labor: { id: string; employeeId: string; employeeName: string; workDate: string; createdAt: string; hours: number; overtimeHours: number; rate: number; overtimeRate: number; notes?: string; cost: number; companyName?: string | null }[];
  costs: { id: string; description: string; quantity: number; unitCost: number; vendor?: string; reference?: string; total: number; createdAt: string }[];
  parts: WoPartItem[];
  plannedParts: PlannedPart[];
  /** Signatures captured on completion (attachments): shown on the page and printed on the job card. */
  signatures: { id: string; signedBy?: string | null; createdAt: string }[];
  /** Contractor (0.17.0): lines plus labour of company-linked employees (moved out of laborCost). */
  totals: { laborHours: number; laborCost: number; partsCost: number; otherCost: number; contractorHours: number; contractorCost: number; totalCost: number };
  planned: { laborHours: number; laborCost: number; partsCost: number; contractorHours: number; contractorCost: number; totalCost: number };
  contractors: ContractorLine[]; contractId?: string | null; contract?: { id: string; number: string; title: string; companyName: string } | null; coveredBy?: CoveredBy | null;
  dueAt?: string | null; dueFromTarget: boolean; startBy?: string | null; respondedAt?: string | null; sla: SlaView;
  allowedTransitions: WorkOrderStatus[]; isOverdue: boolean; createdAt: string; updatedAt?: string; legacyRef?: string; rowVersion: string;
  /** 0.12.0: the work template it was made from (documents shown, not copied), its own recipients, its group and the group's comments. */
  template?: { id: string; code: string; name: string; expectedStopHours?: number | null; documents: { id: string; fileName: string; contentType: string; size: number }[] } | null;
  recipients?: { id: string; fullName: string }[];
  group?: { id: string; number: string; title: string; comments: { id: string; body: string; userName?: string; createdAt: string }[] } | null;
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
  requestTypeName?: string | null;
  symptomName?: string | null;
};
/** A symptom, cause or remedy as a record shows it (0.14.0). */
export type CodeRef = { id: string; code: string; name: string };
export type WorkRequestDetail = {
  symptomId?: string | null; symptom?: CodeRef | null;
  /** A request raised by a failed checklist step (0.13.0). */
  source?: StepSource | null;
  id: string; number: string; title: string; description?: string; status: WorkRequestStatus; priority: WorkPriority; type: WorkType;
  siteId: string; site: { id: string; name: string }; locationId?: string; locationName?: string; assetId?: string; asset?: { id: string; assetNumber: string; name: string };
  requesterName?: string; requesterEmail?: string; requesterPhone?: string; requiredBy?: string; decisionNote?: string; decidedAt?: string; decidedBy?: string;
  workOrder?: { id: string; number: string; status: WorkOrderStatus; dueDate?: string | null; dueAt?: string | null }; createdAt: string; createdBy?: string; canCancel: boolean; downtimeEventId?: string | null;
  respondBy?: string | null; dueAt?: string | null; dueFromTarget: boolean; sla: SlaView;
  /** 0.10.0 */
  requestTypeId?: string | null; requestType?: { id: string; name: string; defaultTradeId?: string | null } | null; extraRecipients: { id: string; name: string }[];
  accessWindows: AccessWindow[]; onlyWhenStopped: boolean; awaitingFeedback: boolean; canAnswer: boolean; feedback: RequestFeedback[];
  /** The active service contract covering its asset (0.17.0). */
  coveredBy?: CoveredBy | null;
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
export type PmScheduleType = 'FixedInterval' | 'DayOfWeekInMonth' | 'DateOfMonth' | 'Meter' | 'Calendar';
export type PmBasis = 'Scheduled' | 'Actual';
export type FrequencyUnit = 'Day' | 'Week' | 'Month' | 'Year';
export type MeterTrigger = 'Incremental' | 'Threshold';
export type DayName = 'Sunday' | 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday';
export const PM_TYPES: PmScheduleType[] = ['FixedInterval', 'DayOfWeekInMonth', 'DateOfMonth', 'Meter', 'Calendar'];
export const FREQ_UNITS: FrequencyUnit[] = ['Day', 'Week', 'Month', 'Year'];
export const DAY_NAMES: DayName[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

export type PmTaskListItem = { id: string; code: string; name: string; tradeId?: string; estimatedHours?: number; isActive: boolean; stepCount: number; scheduleCount: number };
export type PmTaskPart = { partId: string; partNumber: string; partName: string; unitOfMeasure: string; quantity: number; unitCost: number };
// ---------------- Smart checklists (0.13.0) ----------------
export const STEP_TYPES = ['Check', 'Number', 'Meter', 'Text'] as const;
export type { StepType, StepAnswer, MeasureLevel } from '@/components/measure';
export const FOLLOW_UP_KINDS = ['Default', 'None', 'WorkOrder', 'Request'] as const;
export type FollowUpKind = (typeof FOLLOW_UP_KINDS)[number];
export const MEASUREMENT_KINDS = ['Temperature', 'Pressure', 'Current', 'Voltage', 'Level', 'Consumption', 'Other'] as const;
export type MeasurementKind = (typeof MEASUREMENT_KINDS)[number];
/** A template step's definition. */
export type StepDetail = { description: string; type: import('@/components/measure').StepType; unit?: string | null; decimals?: number | null;
  measurementPointId?: string | null; photoRequired: boolean; beforeStart: boolean; followUp: FollowUpKind; followUpTemplateId?: string | null };
export type PointLevels = { validMin?: number | null; validMax?: number | null; alarmLow?: number | null; alertLow?: number | null; target?: number | null;
  alertHigh?: number | null; alarmHigh?: number | null };
export type MeasurementPointItem = PointLevels & { id: string; code: string; name: string; kind: MeasurementKind; unit?: string | null; decimals: number; isActive: boolean;
  assetCount: number; readingCount: number };
export type MeasurementPointDetail = PointLevels & { id: string; code: string; name: string; kind: MeasurementKind; unit?: string | null; decimals: number; isActive: boolean;
  alarmLowTemplateId?: string | null; alertLowTemplateId?: string | null; alertHighTemplateId?: string | null; alarmHighTemplateId?: string | null;
  assets: { id: string; assetNumber: string; name: string; siteId: string }[]; usedBy: { id: string; code: string; name: string }[]; rowVersion: string };
export type PointView = PointLevels & { id: string; code: string; name: string; kind: MeasurementKind; unit?: string | null; decimals: number };
export type MeasurementReadingItem = { id: string; value: number; readAt: string; level: import('@/components/measure').MeasureLevel; assetId?: string | null;
  assetNumber?: string | null; workOrderId?: string | null; workOrderNumber?: string | null; followUpWorkOrderId?: string | null; followUpRequestId?: string | null;
  followUpNumber?: string | null; note?: string | null; byName?: string | null };
export type AssetMeasurement = PointView & { isActive: boolean; readings: { id: string; readAt: string; value: number; level: import('@/components/measure').MeasureLevel;
  workOrderId?: string | null; followUpWorkOrderId?: string | null; followUpRequestId?: string | null; byName?: string | null; note?: string | null }[] };
export type FollowUpLink = { kind: 'workOrder' | 'request'; id: string; number: string };
export type StepSource = { workOrderId: string; number: string; title: string; stepSequence?: number | null; stepDescription?: string | null;
  answer?: import('@/components/measure').StepAnswer | null; value?: number | null; unit?: string | null; level?: import('@/components/measure').MeasureLevel | null;
  textValue?: string | null; remarks?: string | null; photos: { id: string; fileName: string; contentType: string }[] };

export type PmTaskDetail = { id: string; code: string; name: string; description?: string; tradeId?: string; estimatedHours?: number; safetyNotes?: string; isActive: boolean; steps: string[];
  stepDetails: StepDetail[];
  parts: PmTaskPart[]; partsCost: number; rowVersion: string;
  /** Work template fields (0.12.0). */
  title?: string | null; workType: WorkType; priority?: WorkPriority | null; recipientIds: string[]; expectedStopHours?: number | null;
  usedBy: { schedules: { id: string; code: string; name: string; role: 'routine' | 'everyNth' | 'lower' | 'upper' | 'asset' }[];
    checklists: { id: string; assetId?: string | null; assetCategoryId?: string | null; name?: string | null }[]; openWorkOrders: number } };
/** Estimated cost of one PM visit: parts at current average cost plus estimated hours at the technicians' average rate. */
export type VisitCost = { parts: number; labour: number; total: number };
export type PmScheduleListItem = {
  id: string; code: string; name: string; type: PmScheduleType; basis: PmBasis; isActive: boolean; siteId: string; siteName: string; assetId?: string; assetNumber?: string;
  assetName?: string; locationName?: string; taskCode: string; taskName: string; schedule: string; nextDueDate?: string; nextDueReading?: number; meterUnit?: string;
  meterReading?: number; lastGeneratedAt?: string; lastWorkOrderId?: string; lastWorkOrderNumber?: string; openWorkOrders: number; isOverdue: boolean; multipleTasks: number;
  /** Number of assets a round covers; 0 for single-asset/location schedules. */
  assetCount: number;
  onlyWhenStopped?: boolean;
};
export type PmScheduleDetail = {
  onlyWhenStopped?: boolean; costCentreId?: string | null;
  /** 0.21.0: aimed at an asset group, and the members it would cover today. */
  assetGroupId?: string | null; groupAssets?: { id: string; assetNumber: string; name: string }[];
  /** The service contract it is done under (0.17.0). */
  contractId?: string | null;
  id: string; code: string; name: string; description?: string; siteId: string; locationId?: string; assetId?: string; asset?: { id: string; assetNumber: string; name: string };
  /** A round's assets in order (empty for single-asset/location schedules). */
  assets: { id: string; assetNumber: string; name: string; pmTaskId?: string | null }[];
  tradeId?: string; teamId?: string | null; priority: WorkPriority; assigneeIds: string[]; pmTaskId: string; task: { id: string; code: string; name: string };
  extraTasks: { pmTaskId: string; everyNth: number }[]; type: PmScheduleType; basis: PmBasis; frequency: number; unit: FrequencyUnit;
  weekOfMonth?: number; dayOfWeek?: DayName; dayOfMonth?: number; workPeriodDays: number; wholeMonth: boolean; leadDays: number; startDate: string;
  nextDueDate?: string; lastDueDate?: string; lastGeneratedAt?: string; generationCount: number; meterId?: string;
  meter?: { id: string; code: string; name: string; unit: string; currentReading?: number }; meterTrigger?: MeterTrigger; meterInterval?: number;
  nextDueReading?: number; lowerLimit?: number; upperLimit?: number; lowerTaskId?: string; upperTaskId?: string; timeBased: boolean; isActive: boolean;
  schedule: string; upcoming: string[]; recentWorkOrders: { id: string; number: string; title: string; status: WorkOrderStatus; pmDueDate?: string; completedAt?: string }[];
  visitCost: VisitCost; legacyRef?: string; rowVersion: string;
  /** 0.12.0 */
  recipientIds: string[]; skipStatusesOverride?: AssetStatus[] | null; nonWorkingDaysOverride?: DayName[] | null; noStackingOverride?: boolean | null;
  multiAssetMode: MultiAssetMode; groupPlan: GroupPlan; spreadParts?: number | null; calendarIds: string[]; calendars: { id: string; code: string; name: string }[];
  extraMeters: { meterId: string; code: string; name: string; unit: string; currentReading?: number | null; interval: number; nextDueReading?: number | null }[];
};
export type MultiAssetMode = 'Round' | 'Group';
export type GroupPlan = 'Copy' | 'Split';
export type CalendarKind = 'Interval' | 'Dates';
export type ScheduleCalendarItem = { id: string; code: string; name: string; kind: CalendarKind; frequency: number; unit: FrequencyUnit; anchorDate?: string | null;
  dateCount: number; isActive: boolean; scheduleCount: number; next?: string | null };
export type ScheduleCalendarDetail = Omit<ScheduleCalendarItem, 'next'> & { dates: string[]; next: string[]; usedBy: { id: string; code: string; name: string }[]; rowVersion: string };
export type OccurrenceList = {
  dates: { date: string; dueDate: string; templateName?: string; status: 'Planned' | 'Generated'; workOrderId?: string; workOrderNumber?: string; workOrderStatus?: WorkOrderStatus;
    groupId?: string; groupNumber?: string }[];
  readings: { reading: number; estimatedDate?: string | null }[];
};
export type WorkGroupStatus = 'Open' | 'InProgress' | 'Completed' | 'Closed' | 'Cancelled';
export type WorkGroupListItem = { id: string; number: string; title: string; siteId: string; siteName: string; pmScheduleId?: string; pmCode?: string; dueDate?: string;
  plannedStart?: string; status: WorkGroupStatus; total: number; done: number };
export type WorkGroupDetail = {
  id: string; number: string; title: string; siteId: string; siteName: string; pmDueDate?: string; plannedStart?: string; dueDate?: string; dueAt?: string; createdAt: string;
  pm?: { id: string; code: string; name: string } | null; status: WorkGroupStatus;
  children: { id: string; number: string; title: string; status: WorkOrderStatus; assetId?: string; assetNumber?: string; assetName?: string; dueDate?: string; completedAt?: string;
    assignees: { employeeId: string; fullName: string }[]; stepsDone: number; stepsTotal: number; laborHours: number; cost: number }[];
  totals: { laborHours: number; laborCost: number; partsCost: number; otherCost: number; contractorCost?: number; totalCost: number };
  comments: { id: string; body: string; userName?: string; createdAt: string }[];
};
export type PmRunResult = { scheduleId: string; code: string; name: string; dueDate?: string; taskName?: string; assetNumber?: string; workOrderId?: string; workOrderNumber?: string; skipped?: string; groupId?: string; groupNumber?: string };
export type ForecastItem = { date: string; id: string; code: string; name: string; assetNumber?: string; siteName: string; type: PmScheduleType; taskName: string; overdue: boolean;
  /** The schedule's next occurrence: generating it creates no other work order. */
  isNext: boolean;
  /** Estimated cost of the visit. */
  cost: number };
export type MeterItem = { id: string; code: string; name: string; assetId: string; assetNumber: string; assetName: string; unit: string; currentReading?: number; lastReadAt?: string; maxDailyIncrement?: number; isActive: boolean; schedules: number;
  /** Meter propagation (0.12.0): the meter this one follows, and how many follow it. */
  sourceMeterId?: string | null; sourceCode?: string | null; followers?: number };
export type MeterReadingItem = { id: string; value: number; readAt: string; note?: string; source: string; byName?: string; workOrderId?: string };

// ---------------- Inventory ----------------
export type StockTransactionType = 'Receipt' | 'Issue' | 'Return' | 'Adjustment' | 'TransferOut' | 'TransferIn';
export type StoreItem = { id: string; code: string; name: string; siteId: string; siteName: string; description?: string; isActive: boolean; parts: number; stockValue: number };
export type PartListItem = {
  id: string; partNumber: string; name: string; manufacturer?: string; unitOfMeasure: string; groupName?: string; averageCost: number; isCritical: boolean; isShelfItem: boolean;
  isActive: boolean; onHand: number; reserved: number; available: number; onOrder: number; low: boolean; value: number;
  /** 0.24.0: its stock is serial units. */
  isSerialTracked?: boolean;
};
export type PartLookup = { id: string; partNumber: string; name: string; unitOfMeasure: string; averageCost: number; lastPurchasePrice?: number; isShelfItem: boolean; preferredSupplierId?: string; onHand: number;
  /** How many the asset's spare-parts list says (only when looked up for an asset). */
  onAsset?: number | null };
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
  /** Assets whose spare-parts list has this part. */
  usedOn: { assetId: string; assetNumber: string; assetName: string; quantity: number; note?: string | null }[];
  legacyRef?: string; rowVersion: string;
  /** 0.24.0: each piece is a unit with a serial number; its life in months gives each unit's end of life. */
  isSerialTracked?: boolean; unitLifeMonths?: number | null;
};
export type StockTransactionItem = {
  id: string; type: StockTransactionType; date: string; partId: string; partNumber: string; partName: string; unit: string; storeId: string; storeCode: string;
  quantity: number; unitCost: number; total: number; workOrderId?: string; workOrderNumber?: string; purchaseOrderId?: string; reference?: string; batchNumber?: string;
  expiryDate?: string; notes?: string; createdByName?: string; createdAt: string;
  /** 0.20.0: parts issued to (or returned from) a location without a work order carry their own cost centre. */
  locationId?: string | null; costCentreId?: string | null;
};
export type ProposalItem = {
  partId: string; partNumber: string; partName: string; unitOfMeasure: string; isCritical: boolean; storeId: string; storeCode: string; siteId: string; quantityOnHand: number;
  reserved: number; available: number; onOrder: number; minLevel: number; reorderLevel: number; maxLevel: number; suggestedQuantity: number; preferredSupplierId?: string; supplierName?: string; unitPrice: number; belowMinimum: boolean;
  /** The part's catalogue suppliers (0.18.0), preferred first. */
  catalogue?: CatalogueOption[];
};
export type CatalogueOption = { supplierId: string; supplierName: string; packName: string; packSize: number; packPrice?: number | null; currency: string; minPacks: number; discountPercent: number; isPreferred: boolean };
export type WoPartItem = { id: string; type: StockTransactionType; date: string; partId: string; partNumber: string; partName: string; unitOfMeasure: string; storeId: string; storeCode: string; quantity: number; unitCost: number; total: number; batchNumber?: string; notes?: string; createdByName?: string };

// ---------------- Purchasing ----------------
export type PurchaseOrderStatus = 'Draft' | 'Submitted' | 'Approved' | 'PartiallyReceived' | 'Received' | 'Closed' | 'Cancelled';
export const PO_STATUSES: PurchaseOrderStatus[] = ['Draft', 'Submitted', 'Approved', 'PartiallyReceived', 'Received', 'Closed', 'Cancelled'];
export type SupplierItem = { id: string; code: string; name: string; contactName?: string; email?: string; phone?: string; city?: string; country?: string; currency?: string; paymentTerms?: string; isSupplier: boolean; isContractor: boolean; isManufacturer: boolean; isActive: boolean; openOrders: number; receivedYtd: number };
export type SupplierContact = { id?: string; name: string; role?: string | null; phone?: string | null; email?: string | null };
export type SupplierAddress = { id?: string; label?: string | null; address: string; city?: string | null; country?: string | null };
export type SupplierRate = { categoryId: string; rate: number; categoryName?: string };
export type SupplierDetail = { id: string; code: string; name: string; contactName?: string; email?: string; phone?: string; address?: string; city?: string; country?: string; taxNumber?: string; paymentTerms?: string; currency?: string;
  isSupplier: boolean; isContractor: boolean; isManufacturer: boolean; notes?: string; isActive: boolean; contacts: SupplierContact[]; addresses: SupplierAddress[]; rates: SupplierRate[] };
/** Picker rows (0.17.0: by role; contractor pickers use the contacts and rates). */
export type SupplierLookup = { id: string; code: string; name: string; currency?: string; paymentTerms?: string; contacts: { id: string; name: string; role?: string }[]; rates: { categoryId: string; rate: number }[] };
/** A kind of contractor work with a default hourly rate (0.17.0). */
export type ContractorCategory = { id: string; code: string; name: string; defaultRate: number; isActive: boolean };
export type PoListItem = { id: string; number: string; status: PurchaseOrderStatus; orderTypeName?: string | null; orderDate: string; expectedDate?: string; supplierId: string; supplierName: string; siteName: string; currency: string; requestedBy?: string; lines: number; total: number; receivedPercent: number; isOverdue: boolean };
export type PoLine = {
  id: string; lineNumber: number; partId?: string; partNumber?: string; isShelfItem: boolean; description: string; unitOfMeasure?: string; quantity: number; unitPrice: number; discountPercent: number;
  netUnitPrice: number; lineTotal: number; quantityReceived: number; outstanding: number; storeId?: string; storeCode?: string; workOrderId?: string; workOrderNumber?: string; accountCode?: string;
  purchaseRequestLineId?: string | null; request?: { id: string; number: string } | null;
  /** 0.18.0: pieces per ordered unit and the supplier's part number. */
  packSize: number; supplierPartNumber?: string | null; partUnit?: string | null;
  /** 0.24.0: a serial part's line asks a serial per piece at receipt; a repair line names the unit under repair. */
  isSerialTracked?: boolean; unitId?: string | null; unitSerial?: string | null; unitAssetNumber?: string | null;
};
export type PoDetail = {
  id: string; number: string; status: PurchaseOrderStatus; siteId: string; siteName: string; supplierId: string;
  supplier: { id: string; code: string; name: string; contactName?: string; email?: string; phone?: string; address?: string; city?: string; country?: string; taxNumber?: string };
  orderDate: string; expectedDate?: string; requestedBy?: string; currency: string; exchangeRate: number; paymentTerms?: string; shippingMethod?: string; deliverTo?: string; supplierQuoteRef?: string;
  taxPercent: number; shippingCost: number; notes?: string; submittedAt?: string; approvedAt?: string; approvedByName?: string; cancelledAt?: string; cancelReason?: string; createdAt: string; legacyRef?: string;
  lines: PoLine[]; totals: { subtotal: number; tax: number; shippingCost: number; total: number; totalInBase: number };
  receipts: { id: string; date: string; purchaseOrderLineId?: string; partNumber: string; storeCode: string; quantity: number; unitCost: number; reference?: string; batchNumber?: string; createdByName?: string }[];
  isOverdue: boolean; actions: { edit: boolean; submit: boolean; approve: boolean; approvalLimitExceeded: boolean; reject: boolean; receive: boolean; close: boolean; cancel: boolean; delete: boolean; restartApproval?: boolean };
  orderTypeId?: string | null; orderType?: { id: string; code: string; name: string } | null;
  approval?: import('@/pages/purchasing/approvals').ApprovalPanel | null;
  rowVersion: string;
};

// ---------------- Reports ----------------
export type ReportParam = { name: string; type: 'dateRange' | 'site' | 'select' | 'number' | 'asset' | 'year' | 'bool' | 'costCentre' | 'assetGroup'; options?: { value: string; label: string }[]; default?: string | number };
export type ReportDef = { key: string; category: string; title: string; description: string; permission: string; params: ReportParam[] };
export type ReportColumnType = 'text' | 'number' | 'currency' | 'percent' | 'date' | 'datetime' | 'hours' | 'plan';
export type ReportResult = {
  key: string; title: string; subtitle?: string; columns: { key: string; label: string; type: ReportColumnType }[]; rows: Record<string, unknown>[];
  chart?: { type: 'bar' | 'line' | 'donut'; x: string; series: string[]; stacked: boolean }; summary: { label: string; value: number | null; type: ReportColumnType }[];
};

// ---- Settings › Import ----
export type ImportOutcome = 'New' | 'Changed' | 'Unchanged' | 'Problem';
export type FieldChange = { column: string; old: string | null; new: string | null };
export type RowResult = { row: number; key: string; outcome: ImportOutcome; changes: FieldChange[]; problems: string[] };
/** One sheet of a check or import; `rows` lists the new, changed and problem rows (not the unchanged ones). */
export type SheetResult = { sheet: string; new: number; changed: number; unchanged: number; problems: number; rows: RowResult[]; ignoredColumns: string[] };
export type ImportResult = { saved: boolean; sheets: SheetResult[]; newLookups: string[]; notes: string[]; stopped: string | null };

/** Service contracts (0.17.0). */
export type ContractStatus = 'Upcoming' | 'Active' | 'Expired' | 'Terminated';
export type ExpectedPer = 'Year' | 'Quarter' | 'Month';
export type ContractListItem = { id: string; number: string; title: string; companyName: string; typeName?: string; siteName?: string; startDate: string; endDate: string;
  status: ContractStatus; value?: number; currency?: string; deadline?: string; needsAttention: boolean; autoRenew: boolean };
export type ContractDetail = {
  id: string; number: string; title: string; status: ContractStatus; deadline?: string | null; needsAttention: boolean;
  supplierId: string; company: { id: string; code: string; name: string; contactName?: string; phone?: string; email?: string };
  managerId?: string | null; manager?: { id: string; name: string } | null; contractTypeId?: string | null; type?: { id: string; code: string; name: string } | null;
  tradeId?: string | null; trade?: { id: string; code: string; name: string } | null; siteId?: string | null; site?: { id: string; code: string; name: string } | null;
  notes?: string | null; startDate: string; endDate: string; value?: number | null; currency?: string | null; paymentTerms?: string | null;
  expectedWorkOrders?: number | null; expectedPer?: ExpectedPer | null; autoRenew: boolean; renewalMonths?: number | null; noticeDays: number; alertDays: number;
  terminatedOn?: string | null; terminationReason?: string | null;
  periods: { id: string; number: number; startDate: string; endDate: string; value?: number | null; isRenewal: boolean }[];
  amendments: { id: string; date: string; description: string; newValue?: number | null; createdAt: string; createdByName?: string }[];
  assets: { assetId: string; assetNumber: string; name: string; siteName: string; yearlyCost?: number | null }[];
  workOrders: { id: string; number: string; title: string; status: WorkOrderStatus; completedAt?: string | null; dueDate?: string | null }[]; workOrderCount: number;
  actions: { edit: boolean; amend: boolean; terminate: boolean; delete: boolean }; rowVersion: string;
};
export type ContractOption = { id: string; number: string; title: string; companyName: string; status: ContractStatus; covers: boolean };
export type CoveredBy = { id: string; number: string; title: string; companyName: string };
export type ContractorLine = { id: string; supplierId: string; companyName: string; categoryId?: string | null; categoryName?: string | null; contactId?: string | null; personName?: string | null;
  plannedHours: number; hoursDone: number; doneOn?: string | null; rate: number; cost: number; plannedCost: number; notes?: string | null;
  /** The planned visit day (0.19.0); none = the work order's day. */
  plannedOn?: string | null };

/** A type of time away or extra time (0.19.0). */
export type AvailabilityType = { id: string; code: string; name: string; color?: string | null; kind: 'Away' | 'Extra'; isActive: boolean };
/** Time away or extra time for a person or a team (0.19.0). */
export type AvailabilityEntry = {
  id: string; typeId: string; typeName: string; typeColor?: string | null; typeKind: 'Away' | 'Extra'; employeeId?: string | null; employeeName?: string | null;
  teamId?: string | null; teamName?: string | null; from: string; to: string; days: string[]; hours?: number | null; notes?: string | null;
};

/** A part's supplier with their pack and price (0.18.0). */
export type SupplierPartRow = {
  id: string; supplierId: string; supplierCode: string; supplierName: string; supplierActive: boolean; supplierPartNumber?: string | null; packName: string; packSize: number;
  packPrice?: number | null; currency: string; discountPercent: number; leadTimeDays?: number | null; minPacks: number; notes?: string | null; isActive: boolean; isPreferred: boolean;
  lastPaidPrice?: number | null; lastPaidDiscount?: number | null; lastPaidCurrency?: string | null; lastPaidOn?: string | null;
};
/** An order line as the catalogue fills it (0.18.0). */
export type FilledLine = { partId: string; unitOfMeasure: string; packSize: number; quantity: number; unitPrice: number; discountPercent: number; supplierPartNumber?: string | null; leadTimeDays?: number | null; cataloguePrice: boolean };

/** 0.20.0: a cost centre in the tree; `path` is "Production › Line 1" with hidden parents' names included. */
export type CostCentre = { id: string; code: string; name: string; parentId?: string | null; siteId?: string | null; siteName?: string | null; isActive: boolean; path: string };
export type BudgetRow = { costCentreId: string; path: string; isActive: boolean; months: number[]; total: number };
export type BudgetYear = { year: number; rows: BudgetRow[] };

/** 0.21.0: brands with their models, units, and asset groups. */
export type BrandItem = { id: string; name: string; isActive: boolean; models: { id: string; name: string; isActive: boolean }[] };
export type UnitItem = { id: string; symbol: string; name: string; nameAr?: string | null; isActive: boolean };
export type AssetGroupItem = { id: string; code: string; name: string; siteId?: string | null; siteName?: string | null; isActive: boolean; memberCount: number };
export type AssetGroupDetail = { id: string; code: string; name: string; siteId?: string | null; isActive: boolean; members: { id: string; assetNumber: string; name: string; siteId: string }[] };

// ---------------- Serial units (0.24.0) ----------------
export type UnitState = 'InStore' | 'Fitted' | 'AtRepair' | 'Scrapped';
/** A unit: an asset linked to its serial-tracked part, in a store, fitted to an asset, at repair or scrapped. */
export type UnitRow = {
  id: string; assetNumber: string; serialNumber?: string | null; partId: string; partNumber?: string | null; partName?: string | null; state: UnitState;
  storeId?: string | null; storeName?: string | null; fittedToId?: string | null; fittedToNumber?: string | null; fittedToName?: string | null;
  siteId: string; siteName?: string | null; endOfLifeOn?: string | null; repairCount: number; repairCost: number;
};
export type UnitMovementKind = 'Received' | 'Registered' | 'Fitted' | 'Removed' | 'SentForRepair' | 'BackFromRepair' | 'Scrapped' | 'Transferred';
export type UnitOutcome = 'Store' | 'Repair' | 'Scrap';
export type UnitMovement = {
  kind: UnitMovementKind; at: string; storeId?: string | null; storeName?: string | null; assetId?: string | null; assetNumber?: string | null; assetName?: string | null;
  workOrderId?: string | null; workOrderNumber?: string | null; purchaseOrderId?: string | null; purchaseOrderNumber?: string | null;
  outcome?: UnitOutcome | null; note?: string | null; cost?: number | null; byName?: string | null;
};
export type UnitDetail = UnitRow & { movements: UnitMovement[] };
/** A work order's units: fitted to its asset(s). */
export type WoUnit = { id: string; assetNumber: string; serialNumber?: string | null; partId: string; partNumber?: string | null; partName?: string | null; fittedToId: string; since?: string | null };
/** An asset's units now and before. */
export type FittedUnits = {
  now: { unitId: string; assetNumber: string; serialNumber?: string | null; partName?: string | null; since?: string | null }[];
  before: { unitId: string; assetNumber: string; serialNumber?: string | null; partName?: string | null; from?: string | null; to: string; outcome?: UnitOutcome | null }[];
};
