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
function daysUntil(deadline: string): number | null {
  const d = new Date(deadline);
  if (isNaN(d.getTime())) return null;
  return Math.ceil((d.getTime() - Date.now()) / 86_400_000);
}

function formatDate(iso: string): string {
  const d = new Date(iso);
  if (isNaN(d.getTime())) return iso;
  return d.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
}

function monthKey(iso: string): string {
  const d = new Date(iso);
  if (isNaN(d.getTime())) return "Unknown";
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;
}

function monthLabel(key: string): string {
  if (key === "Unknown") return key;
  const [y, m] = key.split("-");
  const d = new Date(Number(y), Number(m) - 1);
  return d.toLocaleDateString("en-US", { month: "long", year: "numeric" });
}

function quarterOf(iso: string): string {
  const d = new Date(iso);
  if (isNaN(d.getTime())) return "Unknown";
  const q = Math.ceil((d.getMonth() + 1) / 3);
  return `Q${q} ${d.getFullYear()}`;
}

function getQuarters(): string[] {
  const now = new Date();
  const q = Math.ceil((now.getMonth() + 1) / 3);
  const y = now.getFullYear();
  const out: string[] = [];
  for (let i = 0; i < 4; i++) {
    const qn = ((q - 1 + i) % 4) + 1;
    const yn = y + Math.floor((q - 1 + i) / 4);
    out.push(`Q${qn} ${yn}`);
  }
  return out;
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
type ViewMode = "timeline" | "quarter" | "calendar";

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
    api
      .getCalendarItems()
      .then(setItems)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  /* Filtered items */
  const filtered = useMemo(() => {
    let f = items;
    if (changeTypeFilter.length)
      f = f.filter((i) => changeTypeFilter.includes(i.changeType.toLowerCase()));
    if (severityFilter.length)
      f = f.filter((i) => severityFilter.includes(i.severity.toLowerCase()));
    if (keyword.trim()) {
      const kw = keyword.toLowerCase();
      f = f.filter(
        (i) =>
          i.title.toLowerCase().includes(kw) ||
          i.affectedServices.some((s) => s.toLowerCase().includes(kw)) ||
          i.briefSummary.toLowerCase().includes(kw)
      );
    }
    return f.sort((a, b) => new Date(a.deadline).getTime() - new Date(b.deadline).getTime());
  }, [items, changeTypeFilter, severityFilter, keyword]);

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
    const qs = getQuarters();
    const map = new Map<string, CalendarItem[]>(qs.map((q) => [q, []]));
    for (const item of filtered) {
      const q = quarterOf(item.deadline);
      if (map.has(q)) map.get(q)!.push(item);
    }
    return qs.map((q) => ({ label: q, items: map.get(q) ?? [] }));
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
      const d = new Date(item.deadline);
      if (isNaN(d.getTime())) continue;
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
            Upcoming Azure changes at a glance
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
      ) : view === "quarter" ? (
        /* Quarter View */
        <div className={styles.quarterGrid}>
          {quarters.map((q) => (
            <Card key={q.label} className={styles.quarterCard}>
              <div className={styles.quarterHeader}>
                <span className={styles.quarterTitle}>{q.label}</span>
                <Badge appearance="tint" color="informative" size="medium">
                  {q.items.length}
                </Badge>
              </div>

              {q.items.length === 0 ? (
                <Text
                  size={200}
                  style={{ color: tokens.colorNeutralForeground3, padding: "12px 0" }}
                >
                  No items this quarter
                </Text>
              ) : (
                <div className={styles.quarterList}>
                  {q.items.slice(0, QUARTER_VISIBLE).map((item) => (
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
                      <span className={styles.quarterItemDate}>
                        {formatDate(item.deadline)}
                      </span>
                    </div>
                  ))}
                  {q.items.length > QUARTER_VISIBLE && (
                    <span className={styles.moreLabel}>
                      +{q.items.length - QUARTER_VISIBLE} more
                    </span>
                  )}
                </div>
              )}
            </Card>
          ))}
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
