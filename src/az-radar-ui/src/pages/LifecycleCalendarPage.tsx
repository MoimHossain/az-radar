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
} from "@fluentui/react-icons";
import { useEffect, useState, useMemo, useCallback } from "react";
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
type ViewMode = "timeline" | "quarter";

const QUARTER_VISIBLE = 8;

export function LifecycleCalendarPage() {
  const styles = useStyles();

  const [items, setItems] = useState<CalendarItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [view, setView] = useState<ViewMode>("timeline");
  const [changeTypeFilter, setChangeTypeFilter] = useState<string[]>([]);
  const [severityFilter, setSeverityFilter] = useState<string[]>([]);
  const [keyword, setKeyword] = useState("");
  const [expandedId, setExpandedId] = useState<string | null>(null);

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
    const isExpanded = expandedId === item.id;
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
        onClick={() => setExpandedId(isExpanded ? null : item.id)}
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
            appearance={view === "timeline" ? "primary" : "secondary"}
            icon={<TimelineRegular />}
            size="small"
            onClick={() => setView("timeline")}
          >
            Timeline
          </Button>
          <Button
            appearance={view === "quarter" ? "primary" : "secondary"}
            icon={<GridRegular />}
            size="small"
            onClick={() => setView("quarter")}
          >
            Quarter
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
      ) : (
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
                      onClick={() => {
                        setExpandedId(item.id);
                        setView("timeline");
                      }}
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
      )}
    </div>
  );
}
