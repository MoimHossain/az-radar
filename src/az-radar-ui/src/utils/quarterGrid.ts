/**
 * Three-year quarter grid for the Lifecycle Calendar (PRD R5).
 * Pure functions with no React or API imports so the bucketing can be verified in isolation.
 */

export type QuarterBand = "0-6" | "7-12" | "13-24" | "25-36";
export type QuarterHorizonFilter = "all" | "past" | "0-6" | "7-12" | "13-24" | "25+";

export interface Dated {
  deadline: string;
}

export interface QuarterCell<T extends Dated> {
  index: number;
  label: string;
  /** 1-4, used to keep calendar-quarter column alignment. */
  quarter: number;
  year: number;
  band: QuarterBand;
  items: T[];
}

export interface QuarterYear<T extends Dated> {
  year: number;
  quarters: QuarterCell<T>[];
}

export interface QuarterGrid<T extends Dated> {
  pastDue: T[];
  years: QuarterYear<T>[];
  later: T[];
  /** Label of the last quarter in the grid, e.g. "Q3 2029". */
  lastLabel: string;
}

export interface VisibleQuarterGrid<T extends Dated> {
  /** null when the Past due block is hidden. */
  pastDue: T[] | null;
  years: QuarterYear<T>[];
  /** null when the Later block is hidden. */
  later: T[] | null;
  quarterCount: number;
  itemCount: number;
  firstLabel: string | null;
  lastLabel: string | null;
}

export const QUARTER_FILTERS: { key: QuarterHorizonFilter; label: string }[] = [
  { key: "all", label: "All" },
  { key: "past", label: "Past due" },
  { key: "0-6", label: "Within 6 months" },
  { key: "7-12", label: "Within 7–12 months" },
  { key: "13-24", label: "Within 13–24 months" },
  { key: "25+", label: "After 25 months" },
];

export const BAND_COLORS: Record<QuarterBand | "past" | "later", string> = {
  past: "#6b7280",
  "0-6": "#dc2626",
  "7-12": "#f59e0b",
  "13-24": "#0078d4",
  "25-36": "#16a34a",
  later: "#9ca3af",
};

/** Deadlines are calendar dates (yyyy-MM-dd); parse them as local dates so they never shift a day. */
export function parseDeadline(value: string | null | undefined): Date | null {
  if (!value) return null;
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
  const d = m ? new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3])) : new Date(value);
  return isNaN(d.getTime()) ? null : d;
}

export function quarterIndex(d: Date): number {
  return d.getFullYear() * 4 + Math.floor(d.getMonth() / 3);
}

export function quarterLabel(index: number): string {
  return `Q${(index % 4) + 1} ${Math.floor(index / 4)}`;
}

/** Legacy horizon band of a quarter, by its offset from the current quarter. */
export function bandForOffset(offset: number): QuarterBand {
  if (offset <= 1) return "0-6";
  if (offset <= 3) return "7-12";
  if (offset <= 7) return "13-24";
  return "25-36";
}

export function buildQuarterGrid<T extends Dated>(items: T[], today: Date, quarterCount = 12): QuarterGrid<T> {
  const first = quarterIndex(today);
  const last = first + quarterCount - 1;
  const cells: QuarterCell<T>[] = [];
  for (let index = first; index <= last; index++) {
    cells.push({
      index,
      label: quarterLabel(index),
      quarter: (index % 4) + 1,
      year: Math.floor(index / 4),
      band: bandForOffset(index - first),
      items: [],
    });
  }

  const pastDue: T[] = [];
  const later: T[] = [];
  for (const item of items) {
    const d = parseDeadline(item.deadline);
    if (!d) continue;
    const q = quarterIndex(d);
    if (q < first) pastDue.push(item);
    else if (q > last) later.push(item);
    else cells[q - first].items.push(item);
  }

  const years: QuarterYear<T>[] = [];
  for (const cell of cells) {
    const current = years[years.length - 1];
    if (current?.year === cell.year) current.quarters.push(cell);
    else years.push({ year: cell.year, quarters: [cell] });
  }

  return { pastDue, years, later, lastLabel: quarterLabel(last) };
}

function bandMatches(filter: QuarterHorizonFilter, band: QuarterBand): boolean {
  if (filter === "all") return true;
  if (filter === "25+") return band === "25-36";
  return filter === band;
}

export function selectQuarterGrid<T extends Dated>(
  grid: QuarterGrid<T>,
  filter: QuarterHorizonFilter
): VisibleQuarterGrid<T> {
  const pastDue = filter === "past" || (filter === "all" && grid.pastDue.length > 0) ? grid.pastDue : null;
  const later = (filter === "all" || filter === "25+") && grid.later.length > 0 ? grid.later : null;
  const years =
    filter === "past"
      ? []
      : grid.years
          .map((y) => ({ year: y.year, quarters: y.quarters.filter((q) => bandMatches(filter, q.band)) }))
          .filter((y) => y.quarters.length > 0);

  const quarters = years.flatMap((y) => y.quarters);
  const itemCount =
    (pastDue?.length ?? 0) + (later?.length ?? 0) + quarters.reduce((sum, q) => sum + q.items.length, 0);

  return {
    pastDue,
    years,
    later,
    quarterCount: quarters.length,
    itemCount,
    firstLabel: quarters[0]?.label ?? null,
    lastLabel: quarters[quarters.length - 1]?.label ?? null,
  };
}

export function countForFilter<T extends Dated>(grid: QuarterGrid<T>, filter: QuarterHorizonFilter): number {
  return selectQuarterGrid(grid, filter).itemCount;
}
