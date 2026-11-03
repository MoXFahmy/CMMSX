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
  totals: { sites: number; locations: number; assets: number; criticalAssets: number; underRepair: number; warrantyExpiring90: number; assetValue: number };
  byStatus: { status: AssetStatus; count: number }[];
  byCriticality: { criticality: Criticality; count: number }[];
  byCategory: { categoryId?: string; name: string; color?: string; count: number }[];
  bySite: { siteId: string; name: string; count: number }[];
  warrantyExpiring: { id: string; assetNumber: string; name: string; warrantyExpiresOn: string; warrantyType: WarrantyType }[];
  recentAssets: { id: string; assetNumber: string; name: string; status: AssetStatus; createdAt: string; siteName: string }[];
};
