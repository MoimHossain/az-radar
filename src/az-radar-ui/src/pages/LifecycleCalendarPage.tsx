import {
  makeStyles,
  tokens,
  Card,
  Text,
  Badge,
  Spinner,
  Input,
  Button,
  Combobox,
  Option,
  Divider,
  Link,
} from "@fluentui/react-components";
import {
  SearchRegular,
  DismissRegular,
  CalendarRegular,
  TimelineRegular,
  GridRegular,
  OpenRegular,
  ChevronDownRegular,
  ChevronUpRegular,
  ChevronLeftRegular,
  ChevronRightRegular,
  CalendarMonthRegular,
  ErrorCircleRegular,
  WarningRegular,
  ArrowTrendingRegular,
  NewRegular,
  CheckmarkCircleRegular,
  EyeRegular,
  ArrowCircleUpRegular,
} from "@fluentui/react-icons";
import { useEffect, useState, useMemo, useCallback, useRef } from "react";
import { useNavigate } from "react-router-dom";
import { api, type CalendarItem } from "../api/client";

/* ------------------------------------------------------------------ */
/*  Severity palette                                                   */
/* ------------------------------------------------------------------ */
const SEVERITY_COLORS: Record<string, string> = {
  critical: "#dc2626",
  high: "#ea580c",
  medium: "#0078D4",
  low: "#059669",
};

const severityColor = (s: string) =>
  SEVERITY_COLORS[s.toLowerCase()] ?? tokens.colorNeutralStroke1;

const CHANGE_TYPE_OPTIONS = [
  "retirement",
  "deprecation",
  "breaking-change",
  "new-feature",
  "ga",
  "preview",
  "migration-required",
];

const SEVERITY_OPTIONS = ["critical", "high", "medium", "low"];

/* ------------------------------------------------------------------ */
/*  Helpers                                                            */
/* ------------------------------------------------------------------ */
// Deadlines are calendar dates (yyyy-MM-dd); parse them as local dates so they never shift a day.
function parseDeadline(value: string | null | undefined): Date | null {
  if (!value) return null;
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
  const d = m ? new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3])) : new Date(value);
  return isNaN(d.getTime()) ? null : d;
}

function startOfToday(): Date {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), now.getDate());
}

function addMonths(date: Date, months: number): Date {
  return new Date(date.getFullYear(), date.getMonth() + months, date.getDate());
}

function daysUntil(deadline: string | null | undefined): number | null {
  const d = parseDeadline(deadline);
  if (!d) return null;
  return Math.round((d.getTime() - startOfToday().getTime()) / 86_400_000);
}

function formatDate(iso: string | null | undefined): string {
  const d = parseDeadline(iso);
  if (!d) return iso || "No date announced";
  return d.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
}

function monthKey(iso: string): string {
  const d = parseDeadline(iso);
  if (!d) return "Unknown";
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;
}

function monthLabel(key: string): string {
  if (key === "Unknown") return key;
  const [y, m] = key.split("-");
  const d = new Date(Number(y), Number(m) - 1);
  return d.toLocaleDateString("en-US", { month: "long", year: "numeric" });
}

function quarterIndex(d: Date): number {
  return d.getFullYear() * 4 + Math.floor(d.getMonth() / 3);
}

function quarterLabel(index: number): string {
  return `Q${(index % 4) + 1} ${Math.floor(index / 4)}`;
}

function quarterOf(iso: string): string {
  const d = parseDeadline(iso);
  return d ? quarterLabel(quarterIndex(d)) : "Unknown";
}

/** Quarters from the earliest to the latest deadline (at least the next four quarters). */
function getQuarters(items: CalendarItem[]): string[] {
  const current = quarterIndex(startOfToday());
  let first = current;
  let last = current + 3;
  for (const item of items) {
    const d = parseDeadline(item.deadline);
    if (!d) continue;
    const q = quarterIndex(d);
    if (q < first) first = q;
    if (q > last) last = q;
  }
  const out: string[] = [];
  for (let q = first; q <= last; q++) out.push(quarterLabel(q));
  return out;
}

/** Relative horizon buckets, matching how platform teams plan lifecycle work. */
const HORIZONS = [
  { key: "past", label: "Past due (last 90 days)", fromMonths: -Infinity, toMonths: 0 },
  { key: "0-6", label: "Within 6 months", fromMonths: 0, toMonths: 6 },
  { key: "7-12", label: "Within 7–12 months", fromMonths: 6, toMonths: 12 },
  { key: "13-24", label: "Within 13–24 months", fromMonths: 12, toMonths: 24 },
  { key: "25-36", label: "Within 25–36 months", fromMonths: 24, toMonths: 36 },
  { key: "later", label: "Beyond 36 months", fromMonths: 36, toMonths: Infinity },
] as const;

function horizonOf(deadline: string, today: Date): string | null {
  const d = parseDeadline(deadline);
  if (!d) return null;
  for (const h of HORIZONS) {
    const from = h.fromMonths === -Infinity ? null : addMonths(today, h.fromMonths);
    const to = h.toMonths === Infinity ? null : addMonths(today, h.toMonths);
    if ((from === null || d >= from) && (to === null || d < to)) return h.key;
  }
  return null;
}

/* ------------------------------------------------------------------ */
/*  Styles                                                             */
/* ------------------------------------------------------------------ */

const pulseKeyframes = {
  "0%": { boxShadow: "0 0 0 0 rgba(220,38,38,0.25)" },
  "70%": { boxShadow: "0 0 0 6px rgba(220,38,38,0)" },
  "100%": { boxShadow: "0 0 0 0 rgba(220,38,38,0)" },
};

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    gap: "16px",
    padding: "24px",
  },
  headerRow: {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "flex-start",
    flexWrap: "wrap",
    gap: "12px",
  },
  header: { display: "flex", flexDirection: "column", gap: "4px" },
  filterBar: {
    display: "flex",
    alignItems: "center",
    gap: "12px",
    flexWrap: "wrap",
  },
  filterCombo: { minWidth: "180px" },
  viewToggle: {
    display: "flex",
    alignItems: "center",
    gap: "4px",
  },
  legend: {
    display: "flex",
    alignItems: "center",
    gap: "16px",
    flexWrap: "wrap",
  },
  legendDot: {
    width: "10px",
    height: "10px",
    borderRadius: "50%",
    display: "inline-block",
    marginRight: "4px",
  },
  legendItem: {
    display: "flex",
    alignItems: "center",
    gap: "2px",
    fontSize: "12px",
    color: tokens.colorNeutralForeground3,
  },

  /* ---------- Timeline view ---------- */
  timeline: {
    display: "flex",
    flexDirection: "column",
    gap: "24px",
  },
  monthGroup: {
    display: "flex",
    flexDirection: "column",
    gap: "8px",
  },
  monthHeader: {
    position: "sticky",
    top: 0,
    zIndex: 2,
    padding: "8px 16px",
    borderRadius: "6px",
    fontWeight: 600,
    fontSize: "15px",
    color: tokens.colorNeutralForeground1,
    backgroundColor: tokens.colorBrandBackground2,
    display: "flex",
    alignItems: "center",
    gap: "8px",
  },
  monthCount: {
    fontSize: "12px",
    fontWeight: 400,
    color: tokens.colorNeutralForeground3,
  },
  timelineCard: {
    display: "flex",
    flexDirection: "column",
    gap: "6px",
    padding: "12px 16px",
    borderRadius: "8px",
    backgroundColor: tokens.colorNeutralBackground1,
    borderLeft: "4px solid transparent",
    cursor: "pointer",
    transitionProperty: "box-shadow, background-color",
    transitionDuration: "150ms",
    "&:hover": {
      boxShadow: tokens.shadow4,
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  timelineCardUrgent: {
    animationName: pulseKeyframes,
    animationDuration: "2s",
    animationIterationCount: "infinite",
  },
  cardTopRow: {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "flex-start",
    gap: "12px",
  },
  cardTitle: {
    fontWeight: 600,
    fontSize: "14px",
    lineHeight: "20px",
    flex: 1,
    color: tokens.colorNeutralForeground1,
  },
  cardTitleRetirement: { fontWeight: 700 },
  cardDeadline: {
    fontSize: "12px",
    color: tokens.colorNeutralForeground3,
    whiteSpace: "nowrap",
    flexShrink: 0,
  },
  badgeRow: {
    display: "flex",
    alignItems: "center",
    gap: "6px",
    flexWrap: "wrap",
  },
  serviceBadge: {
    fontSize: "11px",
    padding: "1px 6px",
    borderRadius: "4px",
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground2,
    whiteSpace: "nowrap",
  },
  overdueBadge: {
    fontWeight: 700,
    fontSize: "11px",
    color: "#dc2626",
    textTransform: "uppercase",
  },
  expandedSection: {
    display: "flex",
    flexDirection: "column",
    gap: "8px",
    padding: "12px 0 4px 0",
  },
  expandedLabel: {
    fontWeight: 600,
    fontSize: "12px",
    color: tokens.colorNeutralForeground3,
    textTransform: "uppercase",
    letterSpacing: "0.04em",
  },
  expandedText: {
    fontSize: "13px",
    color: tokens.colorNeutralForeground2,
    lineHeight: "20px",
  },

  /* ---------- Quarter view ---------- */
  horizonGrid: {
    display: "grid",
    gridTemplateColumns: "repeat(3, 1fr)",
    gap: "16px",
    "@media (max-width: 1200px)": {
      gridTemplateColumns: "1fr 1fr",
    },
    "@media (max-width: 760px)": {
      gridTemplateColumns: "1fr",
    },
  },
  undatedCard: {
    display: "flex",
    flexDirection: "column",
    gap: "8px",
    padding: "16px",
  },
  undatedHeader: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    gap: "8px",
    cursor: "pointer",
  },
  quarterGrid: {
    display: "grid",
    gridTemplateColumns: "1fr 1fr",
    gap: "16px",
    "@media (max-width: 900px)": {
      gridTemplateColumns: "1fr",
    },
  },
  quarterCard: {
    display: "flex",
    flexDirection: "column",
    padding: "16px",
    borderRadius: "8px",
    backgroundColor: tokens.colorNeutralBackground1,
    boxShadow: tokens.shadow2,
    maxHeight: "420px",
    overflow: "hidden",
  },
  quarterHeader: {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "center",
    marginBottom: "12px",
  },
  quarterTitle: {
    fontWeight: 700,
    fontSize: "16px",
    color: tokens.colorNeutralForeground1,
  },
  quarterList: {
    display: "flex",
    flexDirection: "column",
    gap: "6px",
    overflowY: "auto",
    flex: 1,
  },
  quarterItem: {
    display: "flex",
    alignItems: "center",
    gap: "8px",
    fontSize: "13px",
    color: tokens.colorNeutralForeground2,
    padding: "4px 0",
    cursor: "pointer",
    "&:hover": { color: tokens.colorNeutralForeground1 },
  },
  quarterDot: {
    width: "8px",
    height: "8px",
    borderRadius: "50%",
    flexShrink: 0,
  },
  quarterItemTitle: {
    flex: 1,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },
  quarterItemDate: {
    fontSize: "11px",
    color: tokens.colorNeutralForeground3,
    whiteSpace: "nowrap",
    flexShrink: 0,
  },
  moreLabel: {
    fontSize: "12px",
    color: tokens.colorBrandForeground1,
    fontWeight: 600,
    paddingTop: "4px",
  },

  /* ---------- Calendar (month grid) view ---------- */
  calendarNav: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    gap: "16px",
    padding: "8px 0",
  },
  calendarMonthLabel: {
    fontWeight: 700,
    fontSize: "16px",
    minWidth: "160px",
    textAlign: "center" as const,
    color: tokens.colorNeutralForeground1,
  },
  calendarGrid: {
    display: "grid",
    gridTemplateColumns: "repeat(7, 1fr)",
    gap: "1px",
    backgroundColor: tokens.colorNeutralStroke2,
    borderRadius: "8px",
    overflow: "hidden",
  },
  calendarDayHeader: {
    padding: "8px 4px",
    textAlign: "center" as const,
    fontWeight: 600,
    fontSize: "12px",
    color: tokens.colorNeutralForeground3,
    backgroundColor: tokens.colorNeutralBackground3,
    textTransform: "uppercase" as const,
  },
  calendarCell: {
    minHeight: "88px",
    padding: "6px",
    backgroundColor: tokens.colorNeutralBackground1,
    display: "flex",
    flexDirection: "column",
    gap: "4px",
    cursor: "default",
    position: "relative" as const,
    transitionProperty: "background-color",
    transitionDuration: "120ms",
  },
  calendarCellHasItems: {
    backgroundColor: tokens.colorNeutralBackground1Hover,
    cursor: "pointer",
    "&:hover": {
      backgroundColor: tokens.colorNeutralBackground3,
    },
  },
  calendarCellOutside: {
    backgroundColor: tokens.colorNeutralBackground2,
    color: tokens.colorNeutralForeground4,
  },
  calendarCellToday: {
    outline: `2px solid ${tokens.colorBrandBackground}`,
    outlineOffset: "-2px",
  },
  calendarDayNumber: {
    fontWeight: 600,
    fontSize: "13px",
    lineHeight: "16px",
    color: "inherit",
  },
  calendarDots: {
    display: "flex",
    flexWrap: "wrap" as const,
    gap: "3px",
    marginTop: "2px",
  },
  calendarDot: {
    width: "8px",
    height: "8px",
    borderRadius: "50%",
    flexShrink: 0,
  },
  calendarMore: {
    fontSize: "10px",
    fontWeight: 600,
    color: tokens.colorNeutralForeground3,
    lineHeight: "8px",
  },
  calendarPopover: {
    position: "absolute" as const,
    top: "100%",
    left: 0,
    zIndex: 20,
    minWidth: "240px",
    maxWidth: "320px",
    backgroundColor: tokens.colorNeutralBackground1,
    boxShadow: tokens.shadow16,
    borderRadius: "8px",
    padding: "8px",
    display: "flex",
    flexDirection: "column",
    gap: "4px",
  },
  calendarPopoverItem: {
    display: "flex",
    alignItems: "center",
    gap: "6px",
    padding: "4px 6px",
    borderRadius: "4px",
    fontSize: "12px",
    cursor: "pointer",
    "&:hover": {
      backgroundColor: tokens.colorNeutralBackground3,
    },
  },
  calendarPopoverTitle: {
    flex: 1,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap" as const,
    color: tokens.colorNeutralForeground1,
  },

  /* ---------- Detail panel ---------- */
  panelBackdrop: {
    position: "fixed" as const,
    top: 0,
    left: 0,
    right: 0,
    bottom: 0,
    backgroundColor: "rgba(0,0,0,0.3)",
    zIndex: 1000,
  },
  panel: {
    position: "fixed" as const,
    top: 0,
    right: 0,
    bottom: 0,
    width: "680px",
    maxWidth: "90vw",
    backgroundColor: tokens.colorNeutralBackground1,
    boxShadow: tokens.shadow64,
    zIndex: 1001,
    display: "flex",
    flexDirection: "column",
    overflow: "hidden",
  },
  panelHeader: {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "flex-start",
    padding: "20px 24px 16px 24px",
    gap: "12px",
  },
  panelTitleRow: {
    display: "flex",
    gap: "10px",
    alignItems: "flex-start",
    flex: 1,
  },
  panelContent: {
    flex: 1,
    overflow: "auto",
    padding: "0 24px 24px 24px",
    display: "flex",
    flexDirection: "column",
    gap: "16px",
  },
  panelBadgeRow: {
    display: "flex",
    gap: "8px",
    flexWrap: "wrap" as const,
    alignItems: "center",
  },
  panelSection: {
    display: "flex",
    flexDirection: "column",
    gap: "4px",
  },
  panelLabel: {
    fontWeight: 600,
    fontSize: "12px",
    color: tokens.colorNeutralForeground3,
    textTransform: "uppercase" as const,
    letterSpacing: "0.04em",
  },
  panelText: {
    fontSize: "14px",
    color: tokens.colorNeutralForeground2,
    lineHeight: "22px",
  },
  panelServicesRow: {
    display: "flex",
    gap: "6px",
    flexWrap: "wrap" as const,
  },
  panelActions: {
    display: "flex",
    gap: "12px",
    flexWrap: "wrap" as const,
    marginTop: "8px",
  },

  /* ---------- Misc ---------- */
  center: {
    display: "flex",
    justifyContent: "center",
    alignItems: "center",
    padding: "64px",
  },
  empty: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    gap: "8px",
    padding: "48px",
    color: tokens.colorNeutralForeground3,
  },
});

/* ------------------------------------------------------------------ */
/*  Component                                                          */
/* ------------------------------------------------------------------ */
type ViewMode = "timeline" | "horizon" | "quarter" | "calendar";

const CHANGE_TYPE_ICONS: Record<string, React.ReactNode> = {
  retirement: <ErrorCircleRegular style={{ color: "#dc2626" }} />,
  deprecation: <WarningRegular style={{ color: "#ea580c" }} />,
  "breaking-change": <ArrowTrendingRegular style={{ color: "#dc2626" }} />,
  "new-feature": <NewRegular style={{ color: "#059669" }} />,
  ga: <CheckmarkCircleRegular style={{ color: "#0078D4" }} />,
  preview: <EyeRegular style={{ color: "#7c3aed" }} />,
  "migration-required": <ArrowCircleUpRegular style={{ color: "#ea580c" }} />,
};

const QUARTER_VISIBLE = 8;

export function LifecycleCalendarPage() {
  const styles = useStyles();
  const navigate = useNavigate();

  const [items, setItems] = useState<CalendarItem[]>([]);
  const [undatedItems, setUndatedItems] = useState<CalendarItem[]>([]);
  const [showUndated, setShowUndated] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [view, setView] = useState<ViewMode>("timeline");
  const [changeTypeFilter, setChangeTypeFilter] = useState<string[]>([]);
  const [severityFilter, setSeverityFilter] = useState<string[]>([]);
  const [keyword, setKeyword] = useState("");

  const [selectedCalendarItem, setSelectedCalendarItem] = useState<CalendarItem | null>(null);

  // Calendar month view state
  const [calendarMonth, setCalendarMonth] = useState(() => {
    const now = new Date();
    return { year: now.getFullYear(), month: now.getMonth() };
  });
  const [openDay, setOpenDay] = useState<string | null>(null);
  const popoverRef = useRef<HTMLDivElement | null>(null);

  /* Fetch */
  useEffect(() => {
    setLoading(true);
    // Undated items are supplementary; their failure must not hide the calendar.
    api.getUndatedCalendarItems().then(setUndatedItems).catch(() => setUndatedItems([]));
    api
      .getCalendarItems()
      .then(setItems)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  const matchesFilters = useCallback(
    (i: CalendarItem) => {
      if (changeTypeFilter.length && !changeTypeFilter.includes(i.changeType.toLowerCase())) return false;
      if (severityFilter.length && !severityFilter.includes(i.severity.toLowerCase())) return false;
      if (keyword.trim()) {
        const kw = keyword.toLowerCase();
        return (
          i.title.toLowerCase().includes(kw) ||
          i.affectedServices.some((s) => s.toLowerCase().includes(kw)) ||
          (i.briefSummary ?? "").toLowerCase().includes(kw)
        );
      }
      return true;
    },
    [changeTypeFilter, severityFilter, keyword]
  );

  /* Filtered items */
  const filtered = useMemo(
    () =>
      items
        .filter(matchesFilters)
        .sort((a, b) => (parseDeadline(a.deadline)?.getTime() ?? 0) - (parseDeadline(b.deadline)?.getTime() ?? 0)),
    [items, matchesFilters]
  );

  const filteredUndated = useMemo(() => undatedItems.filter(matchesFilters), [undatedItems, matchesFilters]);

  /* Month groups */
  const monthGroups = useMemo(() => {
    const map = new Map<string, CalendarItem[]>();
    for (const item of filtered) {
      const k = monthKey(item.deadline);
      if (!map.has(k)) map.set(k, []);
      map.get(k)!.push(item);
    }
    return [...map.entries()].sort(([a], [b]) => a.localeCompare(b));
  }, [filtered]);

  /* Quarter groups */
  const quarters = useMemo(() => {
    const qs = getQuarters(filtered);
    const map = new Map<string, CalendarItem[]>(qs.map((q) => [q, []]));
    for (const item of filtered) {
      const q = quarterOf(item.deadline);
      if (map.has(q)) map.get(q)!.push(item);
    }
    return qs.map((q) => ({ label: q, items: map.get(q) ?? [] }));
  }, [filtered]);

  /* Horizon groups */
  const horizons = useMemo(() => {
    const today = startOfToday();
    const map = new Map<string, CalendarItem[]>(HORIZONS.map((h) => [h.key, []]));
    for (const item of filtered) {
      const key = horizonOf(item.deadline, today);
      if (key) map.get(key)!.push(item);
    }
    return HORIZONS.map((h) => ({ label: h.label, items: map.get(h.key) ?? [] }));
  }, [filtered]);

  /* Calendar month grid helpers */
  const calendarGrid = useMemo(() => {
    const { year, month } = calendarMonth;
    const firstDay = new Date(year, month, 1);
    const lastDay = new Date(year, month + 1, 0);
    // Monday = 0 ... Sunday = 6 (ISO)
    const startDow = (firstDay.getDay() + 6) % 7;
    const cells: { date: Date; inMonth: boolean }[] = [];
    // fill leading days from previous month
    for (let i = startDow - 1; i >= 0; i--) {
      cells.push({ date: new Date(year, month, -i), inMonth: false });
    }
    // current month
    for (let d = 1; d <= lastDay.getDate(); d++) {
      cells.push({ date: new Date(year, month, d), inMonth: true });
    }
    // fill trailing days
    while (cells.length % 7 !== 0) {
      const last = cells[cells.length - 1].date;
      cells.push({ date: new Date(last.getFullYear(), last.getMonth(), last.getDate() + 1), inMonth: false });
    }
    return cells;
  }, [calendarMonth]);

  const calendarItemsByDay = useMemo(() => {
    const map = new Map<string, CalendarItem[]>();
    for (const item of filtered) {
      const d = parseDeadline(item.deadline);
      if (!d) continue;
      const key = `${d.getFullYear()}-${d.getMonth()}-${d.getDate()}`;
      if (!map.has(key)) map.set(key, []);
      map.get(key)!.push(item);
    }
    return map;
  }, [filtered]);

  const calendarMonthLabelText = useMemo(() => {
    const d = new Date(calendarMonth.year, calendarMonth.month);
    return d.toLocaleDateString("en-US", { month: "long", year: "numeric" });
  }, [calendarMonth]);

  // Close day popover on outside click
  useEffect(() => {
    if (!openDay) return;
    const handler = (e: MouseEvent) => {
      if (popoverRef.current && !popoverRef.current.contains(e.target as Node)) {
        setOpenDay(null);
      }
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, [openDay]);

  const clearFilters = useCallback(() => {
    setChangeTypeFilter([]);
    setSeverityFilter([]);
    setKeyword("");
  }, []);

  const hasFilters = changeTypeFilter.length > 0 || severityFilter.length > 0 || keyword.trim() !== "";

  /* ---- Render helpers ---- */

  const renderBadges = (item: CalendarItem) => {
    const days = daysUntil(item.deadline);
    const isOverdue = days !== null && days < 0;
    const isUrgent = days !== null && days >= 0 && days <= 30;
    return (
      <div className={styles.badgeRow}>
        {isOverdue && <span className={styles.overdueBadge}>OVERDUE</span>}
        {isUrgent && !isOverdue && (
          <Badge appearance="filled" color="danger" size="small">
            {days}d left
          </Badge>
        )}
        <Badge appearance="tint" color="informative" size="small">
          {item.changeType}
        </Badge>
        <Badge
          appearance="filled"
          size="small"
          style={{ backgroundColor: severityColor(item.severity), color: "#fff" }}
        >
          {item.severity}
        </Badge>
        {item.affectedServices.slice(0, 3).map((s) => (
          <span key={s} className={styles.serviceBadge}>
            {s}
          </span>
        ))}
        {item.affectedServices.length > 3 && (
          <span className={styles.serviceBadge}>+{item.affectedServices.length - 3}</span>
        )}
      </div>
    );
  };

  const renderTimelineCard = (item: CalendarItem) => {
    const days = daysUntil(item.deadline);
    const isOverdue = days !== null && days < 0;
    const isUrgent = days !== null && days >= 0 && days <= 30;
    const isExpanded = false;
    const isRetirementType =
      item.changeType.toLowerCase() === "retirement" ||
      item.changeType.toLowerCase() === "deprecation";

    return (
      <div
        key={item.id}
        className={`${styles.timelineCard} ${isUrgent || isOverdue ? styles.timelineCardUrgent : ""}`}
        style={{
          borderLeftColor: severityColor(item.severity),
          ...(isUrgent || isOverdue
            ? { borderLeftWidth: "5px" }
            : {}),
        }}
        onClick={() => setSelectedCalendarItem(item)}
      >
        <div className={styles.cardTopRow}>
          <span
            className={`${styles.cardTitle} ${isRetirementType ? styles.cardTitleRetirement : ""}`}
          >
            {item.title}
          </span>
          <span className={styles.cardDeadline}>
            {formatDate(item.deadline)}
            {isExpanded ? (
              <ChevronUpRegular style={{ marginLeft: 4, verticalAlign: "middle" }} />
            ) : (
              <ChevronDownRegular style={{ marginLeft: 4, verticalAlign: "middle" }} />
            )}
          </span>
        </div>

        {renderBadges(item)}

        {isExpanded && (
          <div className={styles.expandedSection}>
            <Divider />
            {item.briefSummary && (
              <>
                <span className={styles.expandedLabel}>Summary</span>
                <span className={styles.expandedText}>{item.briefSummary}</span>
              </>
            )}
            {item.actionRequired && (
              <>
                <span className={styles.expandedLabel}>Action Required</span>
                <span className={styles.expandedText}>{item.actionRequired}</span>
              </>
            )}
            {item.link && (
              <Link href={item.link} target="_blank" inline>
                View original <OpenRegular style={{ marginLeft: 4, verticalAlign: "middle" }} />
              </Link>
            )}
          </div>
        )}
      </div>
    );
  };

  /* ---- Main render ---- */

  const renderBucketCard = (label: string, bucketItems: CalendarItem[], emptyText: string) => (
    <Card key={label} className={styles.quarterCard}>
      <div className={styles.quarterHeader}>
        <span className={styles.quarterTitle}>{label}</span>
        <Badge appearance="tint" color="informative" size="medium">
          {bucketItems.length}
        </Badge>
      </div>

      {bucketItems.length === 0 ? (
        <Text size={200} style={{ color: tokens.colorNeutralForeground3, padding: "12px 0" }}>
          {emptyText}
        </Text>
      ) : (
        <div className={styles.quarterList}>
          {bucketItems.slice(0, QUARTER_VISIBLE).map((item) => (
            <div
              key={item.id}
              className={styles.quarterItem}
              onClick={() => setSelectedCalendarItem(item)}
            >
              <span
                className={styles.quarterDot}
                style={{ backgroundColor: severityColor(item.severity) }}
              />
              <span className={styles.quarterItemTitle}>{item.title}</span>
              <span className={styles.quarterItemDate}>{formatDate(item.deadline)}</span>
            </div>
          ))}
          {bucketItems.length > QUARTER_VISIBLE && (
            <span className={styles.moreLabel}>+{bucketItems.length - QUARTER_VISIBLE} more</span>
          )}
        </div>
      )}
    </Card>
  );

  if (loading) {
    return (
      <div className={styles.center}>
        <Spinner size="large" label="Loading calendar..." />
      </div>
    );
  }

  if (error) {
    return (
      <div className={styles.center}>
        <Text style={{ color: "#dc2626" }}>Error: {error}</Text>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      {/* Header */}
      <div className={styles.headerRow}>
        <div className={styles.header}>
          <Text size={700} weight="bold">
            <CalendarRegular style={{ marginRight: 8, verticalAlign: "middle" }} />
            Lifecycle Calendar
          </Text>
          <Text size={300} style={{ color: tokens.colorNeutralForeground3 }}>
            Azure lifecycle deadlines from the last 90 days through the next 5 years
          </Text>
        </div>

        <div className={styles.viewToggle}>
          <Button
            appearance={view === "timeline" ? "primary" : "subtle"}
            icon={<TimelineRegular />}
            size="small"
            onClick={() => setView("timeline")}
          >
            Timeline
          </Button>
          <Button
            appearance={view === "horizon" ? "primary" : "subtle"}
            icon={<ArrowTrendingRegular />}
            size="small"
            onClick={() => setView("horizon")}
          >
            Horizon
          </Button>
          <Button
            appearance={view === "quarter" ? "primary" : "subtle"}
            icon={<GridRegular />}
            size="small"
            onClick={() => setView("quarter")}
          >
            Quarter
          </Button>
          <Button
            appearance={view === "calendar" ? "primary" : "subtle"}
            icon={<CalendarMonthRegular />}
            size="small"
            onClick={() => setView("calendar")}
          >
            Calendar
          </Button>
        </div>
      </div>

      {/* Filter bar */}
      <Card size="small" style={{ padding: "12px 16px" }}>
        <div className={styles.filterBar}>
          <Combobox
            className={styles.filterCombo}
            multiselect
            placeholder="Change Type"
            selectedOptions={changeTypeFilter}
            onOptionSelect={(_e, d) => setChangeTypeFilter(d.selectedOptions)}
          >
            {CHANGE_TYPE_OPTIONS.map((o) => (
              <Option key={o} value={o}>
                {o}
              </Option>
            ))}
          </Combobox>

          <Combobox
            className={styles.filterCombo}
            multiselect
            placeholder="Severity"
            selectedOptions={severityFilter}
            onOptionSelect={(_e, d) => setSeverityFilter(d.selectedOptions)}
          >
            {SEVERITY_OPTIONS.map((o) => (
              <Option key={o} value={o}>
                {o}
              </Option>
            ))}
          </Combobox>

          <Input
            placeholder="Search..."
            contentBefore={<SearchRegular />}
            value={keyword}
            onChange={(_e, d) => setKeyword(d.value)}
            style={{ minWidth: 180 }}
          />

          {hasFilters && (
            <Button
              appearance="subtle"
              icon={<DismissRegular />}
              size="small"
              onClick={clearFilters}
            >
              Clear
            </Button>
          )}
        </div>

        {/* Legend */}
        <div className={styles.legend} style={{ marginTop: 8 }}>
          {SEVERITY_OPTIONS.map((s) => (
            <span key={s} className={styles.legendItem}>
              <span
                className={styles.legendDot}
                style={{ backgroundColor: severityColor(s) }}
              />
              {s.charAt(0).toUpperCase() + s.slice(1)}
            </span>
          ))}
        </div>
      </Card>

      {/* Content */}
      {filtered.length === 0 ? (
        <div className={styles.empty}>
          <CalendarRegular style={{ fontSize: 32 }} />
          <Text size={400}>No calendar items match the current filters.</Text>
        </div>
      ) : view === "timeline" ? (
        /* Timeline View */
        <div className={styles.timeline}>
          {monthGroups.map(([key, groupItems]) => (
            <div key={key} className={styles.monthGroup}>
              <div className={styles.monthHeader}>
                {monthLabel(key)}
                <span className={styles.monthCount}>({groupItems.length} items)</span>
              </div>
              {groupItems.map(renderTimelineCard)}
            </div>
          ))}
        </div>
      ) : view === "horizon" ? (
        /* Horizon View */
        <div className={styles.horizonGrid}>
          {horizons.map((h) => renderBucketCard(h.label, h.items, "No deadlines in this horizon"))}
        </div>
      ) : view === "quarter" ? (
        /* Quarter View */
        <div className={styles.quarterGrid}>
          {quarters.map((q) => renderBucketCard(q.label, q.items, "No items this quarter"))}
        </div>
      ) : (
        /* Calendar (Month Grid) View */
        <div>
          {/* Month navigation */}
          <div className={styles.calendarNav}>
            <Button
              appearance="subtle"
              icon={<ChevronLeftRegular />}
              size="small"
              onClick={() =>
                setCalendarMonth((prev) => {
                  const d = new Date(prev.year, prev.month - 1);
                  return { year: d.getFullYear(), month: d.getMonth() };
                })
              }
            />
            <span className={styles.calendarMonthLabel}>{calendarMonthLabelText}</span>
            <Button
              appearance="subtle"
              icon={<ChevronRightRegular />}
              size="small"
              onClick={() =>
                setCalendarMonth((prev) => {
                  const d = new Date(prev.year, prev.month + 1);
                  return { year: d.getFullYear(), month: d.getMonth() };
                })
              }
            />
          </div>

          {/* Grid */}
          <div className={styles.calendarGrid}>
            {/* Day-of-week headers */}
            {["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"].map((d) => (
              <div key={d} className={styles.calendarDayHeader}>{d}</div>
            ))}

            {/* Day cells */}
            {calendarGrid.map((cell, idx) => {
              const dayKey = `${cell.date.getFullYear()}-${cell.date.getMonth()}-${cell.date.getDate()}`;
              const dayItems = calendarItemsByDay.get(dayKey) ?? [];
              const hasItems = dayItems.length > 0;
              const today = new Date();
              const isToday =
                cell.date.getFullYear() === today.getFullYear() &&
                cell.date.getMonth() === today.getMonth() &&
                cell.date.getDate() === today.getDate();
              const isOpen = openDay === dayKey;
              const MAX_DOTS = 4;
              const isBottomHalf = idx >= calendarGrid.length - 14;

              return (
                <div
                  key={idx}
                  className={[
                    styles.calendarCell,
                    hasItems ? styles.calendarCellHasItems : "",
                    !cell.inMonth ? styles.calendarCellOutside : "",
                    isToday ? styles.calendarCellToday : "",
                  ]
                    .filter(Boolean)
                    .join(" ")}
                  onClick={() => {
                    if (hasItems) setOpenDay(isOpen ? null : dayKey);
                  }}
                >
                  <span className={styles.calendarDayNumber}>{cell.date.getDate()}</span>
                  {hasItems && (
                    <div className={styles.calendarDots}>
                      {dayItems.slice(0, MAX_DOTS).map((item, i) => (
                        <span
                          key={i}
                          className={styles.calendarDot}
                          style={{ backgroundColor: severityColor(item.severity) }}
                          title={item.title}
                        />
                      ))}
                      {dayItems.length > MAX_DOTS && (
                        <span className={styles.calendarMore}>
                          +{dayItems.length - MAX_DOTS}
                        </span>
                      )}
                    </div>
                  )}

                  {/* Day popover */}
                  {isOpen && (
                    <div
                      className={styles.calendarPopover}
                      ref={popoverRef}
                      style={isBottomHalf ? { bottom: "100%", top: "auto" } : undefined}
                      onClick={(e) => e.stopPropagation()}
                    >
                      <Text size={200} weight="semibold" style={{ padding: "0 6px 4px" }}>
                        {cell.date.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" })}
                      </Text>
                      {dayItems.map((item) => (
                        <div
                          key={item.id}
                          className={styles.calendarPopoverItem}
                          onClick={() => {
                            setSelectedCalendarItem(item);
                            setOpenDay(null);
                          }}
                        >
                          <Badge
                            appearance="filled"
                            size="small"
                            style={{
                              backgroundColor: severityColor(item.severity),
                              color: "#fff",
                              flexShrink: 0,
                            }}
                          >
                            {item.severity}
                          </Badge>
                          <span className={styles.calendarPopoverTitle}>{item.title}</span>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </div>
      )}

      {/* Undated lifecycle changes */}
      {filteredUndated.length > 0 && (
        <Card className={styles.undatedCard}>
          <div
            className={styles.undatedHeader}
            role="button"
            tabIndex={0}
            onClick={() => setShowUndated((v) => !v)}
            onKeyDown={(e) => {
              if (e.key === "Enter" || e.key === " ") setShowUndated((v) => !v);
            }}
          >
            <Text weight="semibold">
              <WarningRegular style={{ marginRight: 6, verticalAlign: "middle", color: "#ea580c" }} />
              Lifecycle changes without an announced date ({filteredUndated.length})
            </Text>
            {showUndated ? <ChevronUpRegular /> : <ChevronDownRegular />}
          </div>
          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
            Retirements and deprecations whose announcement does not state a deadline. They cannot be placed on
            the calendar but still need follow-up.
          </Text>
          {showUndated && (
            <div className={styles.quarterList}>
              {filteredUndated.map((item) => (
                <div
                  key={item.id}
                  className={styles.quarterItem}
                  onClick={() => setSelectedCalendarItem(item)}
                >
                  <span
                    className={styles.quarterDot}
                    style={{ backgroundColor: severityColor(item.severity) }}
                  />
                  <span className={styles.quarterItemTitle}>{item.title}</span>
                  <span className={styles.quarterItemDate}>{item.changeType}</span>
                </div>
              ))}
            </div>
          )}
        </Card>
      )}

      {/* Detail Panel */}
      {selectedCalendarItem && (() => {
        const item = selectedCalendarItem;
        const days = daysUntil(item.deadline);
        const isOverdue = days !== null && days < 0;
        const ctIcon = CHANGE_TYPE_ICONS[item.changeType.toLowerCase()];
        const targetPage = item.source === "ms-learn" ? "/doc-insights" : "/feed-items";
        const targetLabel = item.source === "ms-learn" ? "Show in Docs Intelligence" : "Show in Azure Updates";

        return (
          <>
            <div
              className={styles.panelBackdrop}
              onClick={() => setSelectedCalendarItem(null)}
            />
            <div className={styles.panel}>
              <div className={styles.panelHeader}>
                <div className={styles.panelTitleRow}>
                  {ctIcon && <span style={{ fontSize: 20, flexShrink: 0, marginTop: 2 }}>{ctIcon}</span>}
                  <Text size={500} weight="bold" style={{ lineHeight: "24px" }}>
                    {item.title}
                  </Text>
                </div>
                <Button
                  appearance="subtle"
                  icon={<DismissRegular />}
                  size="small"
                  onClick={() => setSelectedCalendarItem(null)}
                />
              </div>

              <div className={styles.panelContent}>
                {/* Badges */}
                <div className={styles.panelBadgeRow}>
                  <Badge
                    appearance="filled"
                    size="medium"
                    style={{ backgroundColor: severityColor(item.severity), color: "#fff" }}
                  >
                    {item.severity}
                  </Badge>
                  <Badge appearance="tint" color="informative" size="medium">
                    {item.changeType}
                  </Badge>
                  <Badge
                    appearance="filled"
                    color={isOverdue ? "danger" : days !== null && days <= 30 ? "danger" : "informative"}
                    size="medium"
                  >
                    {formatDate(item.deadline)}
                    {days !== null && (
                      isOverdue
                        ? ` (${Math.abs(days)}d overdue)`
                        : ` (${days}d remaining)`
                    )}
                  </Badge>
                  {item.deadlineSource === "extracted" && (
                    <Badge appearance="outline" color="warning" size="medium">
                      Date taken from announcement text
                    </Badge>
                  )}
                </div>

                <Divider />

                {/* Summary */}
                {item.briefSummary && (
                  <div className={styles.panelSection}>
                    <span className={styles.panelLabel}>Summary</span>
                    <span className={styles.panelText}>{item.briefSummary}</span>
                  </div>
                )}

                {/* Action Required */}
                {item.actionRequired && (
                  <div className={styles.panelSection}>
                    <span className={styles.panelLabel}>Action Required</span>
                    <span className={styles.panelText}>{item.actionRequired}</span>
                  </div>
                )}

                {/* Affected Services */}
                {item.affectedServices.length > 0 && (
                  <div className={styles.panelSection}>
                    <span className={styles.panelLabel}>Affected Services</span>
                    <div className={styles.panelServicesRow}>
                      {item.affectedServices.map((svc) => (
                        <Badge key={svc} appearance="outline" size="medium">
                          {svc}
                        </Badge>
                      ))}
                    </div>
                  </div>
                )}

                <Divider />

                {/* Actions */}
                <div className={styles.panelActions}>
                  {item.link && (
                    <Link href={item.link} target="_blank" inline>
                      <Button appearance="subtle" icon={<OpenRegular />} size="small">
                        View Original
                      </Button>
                    </Link>
                  )}
                  <Button
                    appearance="primary"
                    size="small"
                    onClick={() => {
                      setSelectedCalendarItem(null);
                      navigate(`${targetPage}?highlight=${encodeURIComponent(item.title)}`);
                    }}
                  >
                    {targetLabel}
                  </Button>
                </div>
              </div>
            </div>
          </>
        );
      })()}
    </div>
  );
}
