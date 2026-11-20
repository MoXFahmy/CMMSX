import type { DataTableColumn } from 'mantine-datatable';

/** The lists that have the toolkit; the same keys the server uses for views, column settings and exports. */
export type ListKey = 'work-orders' | 'work-requests' | 'pm-schedules' | 'parts' | 'purchase-orders' | 'assets';

/** One exported column: a property of the list row (dots reach into objects) and its heading (the screen title by default). */
export type ExportSpec = { key: string; title?: string };

/** A list column. `export` says what it puts in the Excel/CSV file: nothing (false), several columns, or (default) its accessor. */
export type ListColumn<T> = DataTableColumn<T> & { export?: false | ExportSpec[] };

/** A filter shown as a chip: the address keys it uses (two for a date range), its label and how its value reads. */
export type FilterDef = { keys: string[]; label: string; format?: (values: string[]) => string };

export type SavedView = { id: string; name: string; query: string; hiddenColumns: string[]; isDefault: boolean };

export type BulkResult = { updated: number; skipped: { id: string; number: string; reason: string }[] };
