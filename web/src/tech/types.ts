import type { HoldReason, WorkOrderStatus, WorkPriority, WorkType } from '@/api/types';

/** A job as the phone keeps it (GET /api/tech/bundle). */
export type TechJob = {
  id: string; number: string; title: string; description?: string | null; type: WorkType; priority: WorkPriority; status: WorkOrderStatus;
  holdReason?: HoldReason | null; dueDate?: string | null; dueAt?: string | null; plannedStart?: string | null; siteId: string;
  /** The team the job was given to (0.9.0): it's on this phone because the person is in that team. */
  teamName?: string | null;
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
};

export type TechBundle = {
  employeeId: string; userId: string; jobs: TechJob[];
  failureCodes: { id: string; code: string; name: string }[];
  completionRules: { notes: boolean; labour: boolean; allSteps: boolean; failureCodeOnBreakdown: boolean };
};

/** The assets, locations and meters of my sites (GET /api/tech/places), for scanning offline. */
export type TechPlaces = {
  assets: { id: string; assetNumber: string; name: string; barcode?: string | null; siteId: string; locationId?: string | null; notesToTechnician?: string | null }[];
  locations: { id: string; name: string; path?: string | null; siteId: string; notesToTechnician?: string | null }[];
  meters: { id: string; assetId: string; name: string; unit: string; currentReading?: number | null; lastReadAt?: string | null }[];
};
