import type { HoldReason, WorkOrderStatus, WorkPriority, WorkType } from '@/api/types';

/** A job as the phone keeps it (GET /api/tech/bundle). */
export type TechJob = {
  id: string; number: string; title: string; description?: string | null; type: WorkType; priority: WorkPriority; status: WorkOrderStatus;
  holdReason?: HoldReason | null; dueDate?: string | null; dueAt?: string | null; plannedStart?: string | null; siteId: string;
  /** The team the job was given to (0.9.0): it's on this phone because the person is in that team. */
  teamName?: string | null;
  /** 0.10.0: when the work may be done, and "only while the asset is stopped". */
  accessWindows?: { start: string; end: string; note?: string | null }[]; onlyWhenStopped?: boolean;
  /** downSince: when the asset's open stop began (as of the last refresh); empty while it runs. */
  asset?: { id: string; assetNumber: string; name: string; notesToTechnician?: string | null; downSince?: string | null } | null;
  /** The job's own stop on the asset's downtime log (as of the last refresh): completing the job ends it and fills the downtime. */
  downtime?: { startedAt: string; endedAt?: string | null } | null;
  location?: { id: string; path?: string | null; notesToTechnician?: string | null } | null;
  roundAssets: { id: string; assetNumber: string; name: string }[];
  tasks: { id: string; sequence: number; description: string; isDone: boolean; reading?: string | null; remarks?: string | null; assetId?: string | null; updatedAt?: string | null; updatedByName?: string | null }[];
  plannedParts: { partNumber: string; partName: string; unitOfMeasure: string; quantity: number; storeCode?: string | null }[];
  files: { id: string; fileName: string; contentType: string; isSignature: boolean; signedBy?: string | null }[];
  comments: { body: string; userName?: string | null; createdAt: string }[];
  myLabor: { workDate: string; hours: number; overtimeHours: number; notes?: string | null }[];
  /** Anyone has logged hours on the job (copies saved before 0.5.5 don't say). */
  hasLabor?: boolean;
  /** 0.11.0: planned lines (to mark used) and the job site's default store. */
  plannedLines?: PlannedLine[]; defaultStoreId?: string | null;
  /** The people on the job and its team's members (a crew leader logs their hours). */
  crew?: { employeeId: string; name: string }[];
};

export type PlannedLine = { id: string; partId: string; storeId?: string | null; quantity: number; issued: number };

/** A part as the phone keeps it (GET /api/tech/stock, 0.11.0): stock and bin in each store of my sites. */
export type StockPart = {
  id: string; partNumber: string; name: string; unitOfMeasure: string; barcode?: string | null; isShelfItem: boolean; allowNegativeStock: boolean;
  photoId?: string | null; levels: { storeId: string; onHand: number; reserved: number; bin?: string | null }[];
};

/** Parts, stores, cancel reasons, PM checklists, open counts and (to receive) open purchase orders, for working offline. */
export type TechStock = {
  stores: { id: string; code: string; name: string; siteId: string }[];
  parts: StockPart[];
  cancelReasons: { id: string; name: string }[];
  pmTasks: { id: string; code: string; name: string; steps: string[] }[];
  counts: { id: string; name: string; storeId: string }[];
  purchaseOrders: { id: string; number: string; supplier: string; lines: { id: string; lineNumber: number; partId: string; description: string; unitOfMeasure?: string | null; outstanding: number; storeId?: string | null }[] }[] | null;
};

export type TechBundle = {
  employeeId: string; userId: string; jobs: TechJob[];
  failureCodes: { id: string; code: string; name: string }[];
  completionRules: { notes: boolean; labour: boolean; allSteps: boolean; failureCodeOnBreakdown: boolean; holdNote?: boolean };
  /** What this person may do on the phone (0.11.0). */
  can?: { logForOthers: boolean; cancel: boolean; createWorkOrders: boolean };
};

/** The assets, locations and meters of my sites (GET /api/tech/places), for scanning offline. */
export type TechPlaces = {
  assets: { id: string; assetNumber: string; name: string; barcode?: string | null; siteId: string; locationId?: string | null; notesToTechnician?: string | null }[];
  locations: { id: string; name: string; path?: string | null; siteId: string; notesToTechnician?: string | null }[];
  meters: { id: string; assetId: string; name: string; unit: string; currentReading?: number | null; lastReadAt?: string | null }[];
};
