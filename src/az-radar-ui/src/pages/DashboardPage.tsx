import {
  makeStyles,
  tokens,
  shorthands,
  Card,
  Text,
  Badge,
  Spinner,
  Divider,
  mergeClasses,
  Tab,
  TabList,
} from "@fluentui/react-components";
import {
  ErrorCircleRegular,
  ClockRegular,
  CheckmarkCircleRegular,
  SparkleRegular,
  DataBarVerticalRegular,
  EyeRegular,
  ArrowSyncCheckmarkRegular,
  WarningRegular,
  DatabaseRegular,
  BookRegular,
} from "@fluentui/react-icons";
import { useEffect, useState, useMemo } from "react";
import { useNavigate } from "react-router-dom";
import { api } from "../api/client";
import type { DashboardStats } from "../api/client";

/* ------------------------------------------------------------------ */
/*  Styles                                                            */
/* ------------------------------------------------------------------ */

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    gap: "24px",
    padding: "24px",
    maxWidth: "1400px",
  },
  header: {
    display: "flex",
    flexDirection: "column",
    gap: "2px",
  },
  subtitle: {
    color: tokens.colorNeutralForeground3,
  },

  /* Section 1 — Counter cards */
  counterGrid: {
    display: "grid",
    gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))",
    gap: "12px",
  },
  counterCard: {
    padding: "16px 18px",
    display: "flex",
    alignItems: "center",
    gap: "14px",
    ...shorthands.borderRadius("8px"),
    minHeight: "72px",
  },
  // Distinct vivid card backgrounds
  cardRetirement: {
    backgroundColor: "#dc2626",
    color: "#fff",
  },
  cardUrgent: {
    backgroundColor: "#ea580c",
    color: "#fff",
  },
  cardGA: {
    backgroundColor: "#0078D4",
    color: "#fff",
  },
  cardPreview: {
    backgroundColor: "#7c3aed",
    color: "#fff",
  },
  cardFeatures: {
    backgroundColor: "#059669",
    color: "#fff",
  },
  cardTotal: {
    backgroundColor: "#334155",
    color: "#fff",
  },
  cardWatched: {
    backgroundColor: "#0891b2",
    color: "#fff",
  },
  cardJobs: {
    backgroundColor: "#4f46e5",
    color: "#fff",
  },
  counterIcon: {
    fontSize: "28px",
    flexShrink: 0,
  },
  counterIconWhite: {
    fontSize: "28px",
    flexShrink: 0,
    color: "#fff",
  },
  counterBody: {
    display: "flex",
    flexDirection: "column",
    gap: "2px",
  },
  counterValue: {
    fontSize: "28px",
    fontWeight: 700,
    lineHeight: "1",
  },
  counterValueWhite: {
    fontSize: "28px",
    fontWeight: 700,
    lineHeight: "1",
    color: "#fff",
  },
  counterLabel: {
    fontSize: "12px",
    color: tokens.colorNeutralForeground3,
    whiteSpace: "nowrap",
  },
  counterLabelWhite: {
    fontSize: "12px",
    color: "rgba(255,255,255,0.85)",
    whiteSpace: "nowrap",
  },

  /* Section 2 — Retirement timeline (compact card) */
  sectionTitle: {
    display: "flex",
    alignItems: "center",
    gap: "8px",
  },
  timelineCard: {
    padding: "0",
    overflow: "hidden",
  },
  timelineHeader: {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "center",
    padding: "16px 20px 0 20px",
    flexWrap: "wrap" as const,
    gap: "8px",
  },
  timelineTabs: {
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    paddingLeft: "12px",
  },
  timelineList: {
    display: "flex",
    flexDirection: "column",
    maxHeight: "340px",
    overflow: "auto",
  },
  timelineItem: {
    display: "flex",
    alignItems: "center",
    gap: "12px",
    padding: "10px 20px",
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    borderLeftWidth: "4px",
    borderLeftStyle: "solid",
    "&:last-child": {
      borderBottom: "none",
    },
  },
  borderRed: { borderLeftColor: tokens.colorPaletteRedBorderActive },
  borderOrange: { borderLeftColor: tokens.colorPaletteDarkOrangeBorderActive },
  borderYellow: { borderLeftColor: tokens.colorPaletteYellowBorderActive },
  borderGreen: { borderLeftColor: tokens.colorPaletteGreenBorderActive },
  timelineItemBody: {
    flex: 1,
    minWidth: 0,
    display: "flex",
    flexDirection: "column",
    gap: "2px",
  },
  timelineItemTitle: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap" as const,
  },
  timelineItemMeta: {
    display: "flex",
    gap: "6px",
    alignItems: "center",
    flexWrap: "wrap" as const,
  },
  timelineItemRight: {
    flexShrink: 0,
    textAlign: "right" as const,
    display: "flex",
    flexDirection: "column",
    alignItems: "flex-end",
    gap: "2px",
  },
  timelineEmpty: {
    padding: "32px 20px",
    textAlign: "center" as const,
    color: tokens.colorNeutralForeground3,
  },

  /* Sections 3 + 4 side-by-side */
  twoColGrid: {
    display: "grid",
    gridTemplateColumns: "1fr 1fr",
    gap: "16px",
    alignItems: "start",
    "@media (max-width: 800px)": {
      gridTemplateColumns: "1fr",
    },
  },
  barChartCard: {
    padding: "20px",
    display: "flex",
    flexDirection: "column",
    gap: "14px",
  },
  barRow: {
    display: "flex",
    flexDirection: "column",
    gap: "4px",
  },
  barHeader: {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "center",
  },
  barTrack: {
    height: "22px",
    backgroundColor: tokens.colorNeutralBackground3,
    ...shorthands.borderRadius("4px"),
    overflow: "hidden",
    position: "relative" as const,
  },
  barFill: {
    height: "100%",
    ...shorthands.borderRadius("4px"),
    transition: "width 0.5s ease",
    minWidth: "2px",
  },

  /* Section 4 — Top affected services */
  servicesScrollArea: {
    maxHeight: "280px",
    overflow: "auto",
  },
  serviceRow: {
    display: "flex",
    alignItems: "center",
    gap: "12px",
    padding: "6px 0",
  },
  serviceName: {
    width: "180px",
    flexShrink: 0,
    fontSize: "13px",
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },
  serviceBarWrap: {
    flex: 1,
    height: "18px",
    backgroundColor: tokens.colorNeutralBackground3,
    ...shorthands.borderRadius("4px"),
    overflow: "hidden",
  },
  serviceBar: {
    height: "100%",
    backgroundColor: tokens.colorBrandBackground,
    ...shorthands.borderRadius("4px"),
    transition: "width 0.5s ease",
    minWidth: "2px",
  },
  serviceCount: {
    fontSize: "13px",
    fontWeight: 600,
    width: "36px",
    textAlign: "right" as const,
  },

  /* Section 5 — Data sources footer */
  sourceCard: {
    padding: "16px 20px",
    display: "flex",
    flexWrap: "wrap",
    gap: "24px",
    alignItems: "center",
  },
  sourceItem: {
    display: "flex",
    alignItems: "center",
    gap: "8px",
  },

  /* Utilities */
  dangerText: { color: tokens.colorPaletteRedForeground1 },
  warningText: { color: tokens.colorPaletteDarkOrangeForeground1 },
  successText: { color: tokens.colorPaletteGreenForeground1 },
  brandText: { color: tokens.colorBrandForeground1 },
  purpleText: { color: tokens.colorPaletteBerryForeground1 },
  neutralText: { color: tokens.colorNeutralForeground3 },
});

/* ------------------------------------------------------------------ */
/*  Helpers                                                           */
/* ------------------------------------------------------------------ */

const CHANGE_TYPE_COLORS: Record<string, string> = {
  retirement: tokens.colorPaletteRedBackground3,
  deprecation: tokens.colorPaletteDarkOrangeBackground3,
  "general-availability": tokens.colorBrandBackground,
  preview: tokens.colorPaletteBerryBackground2,
  "new-feature": tokens.colorPaletteGreenBackground3,
  update: tokens.colorNeutralBackground5,
};

function urgencyBorderClass(
  days: number | null,
  styles: ReturnType<typeof useStyles>,
) {
  if (days === null || days < 30) return styles.borderRed;
  if (days < 90) return styles.borderOrange;
  if (days < 180) return styles.borderYellow;
  return styles.borderGreen;
}

function urgencyBadge(days: number | null) {
  if (days === null) return { label: "Unknown", color: "informative" as const };
  if (days < 0)
    return { label: `OVERDUE (${Math.abs(days)}d ago)`, color: "danger" as const };
  if (days < 30)
    return { label: `${days}d remaining`, color: "danger" as const };
  if (days < 90)
    return { label: `${days}d remaining`, color: "warning" as const };
  if (days < 180)
    return { label: `${days}d remaining`, color: "important" as const };
  return { label: `${days}d remaining`, color: "success" as const };
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}

/* ------------------------------------------------------------------ */
/*  Component                                                         */
/* ------------------------------------------------------------------ */

export function DashboardPage() {
  const styles = useStyles();
  const navigate = useNavigate();
  const [stats, setStats] = useState<DashboardStats | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [deadlineFilter, setDeadlineFilter] = useState<string>("30");

  useEffect(() => {
    api
      .getDashboardStats()
      .then(setStats)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  const sortedDeadlines = useMemo(() => {
    if (!stats) return [];
    return [...stats.deadlines]
      .filter((d) => d.daysRemaining === null || d.daysRemaining > -90)
      .sort((a, b) => {
        const da = a.daysRemaining ?? Infinity;
        const db = b.daysRemaining ?? Infinity;
        return da - db;
      });
  }, [stats]);

  const filteredDeadlines = useMemo(() => {
    return sortedDeadlines.filter((d) => {
      const days = d.daysRemaining;
      switch (deadlineFilter) {
        case "30": return days !== null && days >= 0 && days < 30;
        case "90": return days !== null && days >= 0 && days < 90;
        case "overdue": return days !== null && days < 0;
        default: return true;
      }
    });
  }, [sortedDeadlines, deadlineFilter]);

  if (loading) {
    return (
      <div className={styles.container}>
        <Spinner label="Loading dashboard…" />
      </div>
    );
  }

  if (error || !stats) {
    return (
      <div className={styles.container}>
        <Text>
          Failed to load dashboard{error ? `: ${error}` : ""}
        </Text>
      </div>
    );
  }

  const changeEntries = Object.entries(stats.changeTypeBreakdown);
  const maxChange = Math.max(...changeEntries.map(([, v]) => v), 1);
  const changeTotal = changeEntries.reduce((s, [, v]) => s + v, 0) || 1;

  const maxServiceTotal = Math.max(
    ...stats.topAffectedServices.map((s) => s.total),
    1,
  );

  return (
    <div className={styles.container}>
      {/* ---- Header ---- */}
      <div className={styles.header}>
        <Text size={800} weight="bold">
          Dashboard
        </Text>
        <Text size={300} className={styles.subtitle}>
          Sharp focus on what's changing in your Azure estate
        </Text>
      </div>

      {/* ---- Section 1: Counter cards ---- */}
      <div className={styles.counterGrid}>
        <Card className={mergeClasses(styles.counterCard, styles.cardRetirement)}>
          <ErrorCircleRegular className={styles.counterIconWhite} />
          <div className={styles.counterBody}>
            <Text className={styles.counterValueWhite}>{stats.totalRetirements}</Text>
            <Text className={styles.counterLabelWhite}>Retirements</Text>
          </div>
        </Card>

        <Card className={mergeClasses(styles.counterCard, styles.cardUrgent)}>
          <ClockRegular className={styles.counterIconWhite} />
          <div className={styles.counterBody}>
            <Text className={styles.counterValueWhite}>{stats.urgentDeadlines}</Text>
            <Text className={styles.counterLabelWhite}>Urgent (&lt; 90 days)</Text>
          </div>
        </Card>

        <Card className={mergeClasses(styles.counterCard, styles.cardGA)}>
          <CheckmarkCircleRegular className={styles.counterIconWhite} />
          <div className={styles.counterBody}>
            <Text className={styles.counterValueWhite}>{stats.totalGA}</Text>
            <Text className={styles.counterLabelWhite}>Generally Available</Text>
          </div>
        </Card>

        <Card className={mergeClasses(styles.counterCard, styles.cardPreview)}>
          <DataBarVerticalRegular className={styles.counterIconWhite} />
          <div className={styles.counterBody}>
            <Text className={styles.counterValueWhite}>{stats.totalPreviews}</Text>
            <Text className={styles.counterLabelWhite}>In Preview</Text>
          </div>
        </Card>

        <Card className={mergeClasses(styles.counterCard, styles.cardFeatures)}>
          <SparkleRegular className={styles.counterIconWhite} />
          <div className={styles.counterBody}>
            <Text className={styles.counterValueWhite}>{stats.totalNewFeatures}</Text>
            <Text className={styles.counterLabelWhite}>New Features</Text>
          </div>
        </Card>

        <Card className={mergeClasses(styles.counterCard, styles.cardTotal)}>
          <DataBarVerticalRegular className={styles.counterIconWhite} />
          <div className={styles.counterBody}>
            <Text className={styles.counterValueWhite}>{stats.totalItems}</Text>
            <Text className={styles.counterLabelWhite}>Total Tracked</Text>
          </div>
        </Card>

        <Card className={mergeClasses(styles.counterCard, styles.cardWatched)}>
          <EyeRegular className={styles.counterIconWhite} />
          <div className={styles.counterBody}>
            <Text className={styles.counterValueWhite}>{stats.watchedServices}</Text>
            <Text className={styles.counterLabelWhite}>Watched Services</Text>
          </div>
        </Card>

        <Card className={mergeClasses(styles.counterCard, styles.cardJobs)}>
          <ArrowSyncCheckmarkRegular className={styles.counterIconWhite} />
          <div className={styles.counterBody}>
            <Text className={styles.counterValueWhite}>{stats.completedJobs}</Text>
            <Text className={styles.counterLabelWhite}>Jobs Completed</Text>
          </div>
        </Card>
      </div>

      {/* ---- Blast Radius KPI banner ---- */}
      {stats.blastRadiusTotalResources > 0 && (
        <Card style={{ padding: "16px 20px", backgroundColor: "#1e293b", color: "#fff", borderRadius: 8 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 24, flexWrap: "wrap" }}>
            <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
              <Text style={{ fontSize: 14, fontWeight: 600, color: "#94a3b8" }}>
                💥 BLAST RADIUS
              </Text>
            </div>
            <div style={{ display: "flex", gap: 32, flexWrap: "wrap" }}>
              <div>
                <Text style={{ fontSize: 28, fontWeight: 700, color: "#f87171" }}>
                  {stats.blastRadiusTotalResources}
                </Text>
                <Text style={{ fontSize: 12, color: "#94a3b8", display: "block" }}>
                  Resources Affected
                </Text>
              </div>
              <div>
                <Text style={{ fontSize: 28, fontWeight: 700, color: "#fbbf24" }}>
                  {stats.blastRadiusItemsScanned}
                </Text>
                <Text style={{ fontSize: 12, color: "#94a3b8", display: "block" }}>
                  Retirements Scanned
                </Text>
              </div>
              <div>
                <Text style={{ fontSize: 28, fontWeight: 700, color: "#38bdf8" }}>
                  {stats.blastRadiusSubscriptions}
                </Text>
                <Text style={{ fontSize: 12, color: "#94a3b8", display: "block" }}>
                  Subscriptions Impacted
                </Text>
              </div>
              {stats.blastRadiusLastScan && (
                <div style={{ marginLeft: "auto" }}>
                  <Text style={{ fontSize: 11, color: "#64748b" }}>
                    Last scan: {new Date(stats.blastRadiusLastScan).toLocaleString()}
                  </Text>
                </div>
              )}
            </div>
          </div>
        </Card>
      )}

      {/* ---- Section 2: Retirement timeline card ---- */}
      {sortedDeadlines.length > 0 && (
        <Card className={styles.timelineCard}>
          <div className={styles.timelineHeader}>
            <div className={styles.sectionTitle}>
              <WarningRegular style={{ fontSize: 20, color: "#dc2626" }} />
              <Text size={500} weight="semibold">
                Retirement &amp; Deprecation Timeline
              </Text>
              <Badge appearance="filled" color="danger" size="small">
                {sortedDeadlines.length}
              </Badge>
            </div>
          </div>

          <div className={styles.timelineTabs}>
            <TabList
              selectedValue={deadlineFilter}
              onTabSelect={(_, d) => setDeadlineFilter(d.value as string)}
              size="small"
            >
              <Tab value="30">
                &lt; 1 month ({sortedDeadlines.filter((d) => d.daysRemaining !== null && d.daysRemaining >= 0 && d.daysRemaining < 30).length})
              </Tab>
              <Tab value="90">
                &lt; 3 months ({sortedDeadlines.filter((d) => d.daysRemaining !== null && d.daysRemaining >= 0 && d.daysRemaining < 90).length})
              </Tab>
              <Tab value="overdue">
                Overdue ({sortedDeadlines.filter((d) => d.daysRemaining !== null && d.daysRemaining < 0).length})
              </Tab>
              <Tab value="all">All ({sortedDeadlines.length})</Tab>
            </TabList>
          </div>

          {filteredDeadlines.length > 0 ? (
            <div className={styles.timelineList}>
              {filteredDeadlines.map((d) => {
                const badge = urgencyBadge(d.daysRemaining);
                const targetPage = d.source === "ms-learn" ? "/doc-insights" : "/feed-items";
                return (
                  <div
                    key={d.title + d.deadline}
                    className={mergeClasses(
                      styles.timelineItem,
                      urgencyBorderClass(d.daysRemaining, styles),
                    )}
                    onClick={() => navigate(`${targetPage}?highlight=${encodeURIComponent(d.title)}`)}
                    style={{ cursor: "pointer" }}
                  >
                    <div className={styles.timelineItemBody}>
                      <Text weight="semibold" size={300} className={styles.timelineItemTitle}>
                        {d.title}
                      </Text>
                      <div className={styles.timelineItemMeta}>
                        {d.affectedServices.slice(0, 3).map((svc) => (
                          <Badge key={svc} appearance="outline" size="small">
                            {svc}
                          </Badge>
                        ))}
                        {d.affectedServices.length > 3 && (
                          <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
                            +{d.affectedServices.length - 3} more
                          </Text>
                        )}
                      </div>
                    </div>
                    <div className={styles.timelineItemRight}>
                      <Badge
                        appearance={badge.color === "danger" ? "filled" : "tint"}
                        color={badge.color}
                        size="small"
                      >
                        {badge.label}
                      </Badge>
                      <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
                        {formatDate(d.deadline)}
                      </Text>
                    </div>
                  </div>
                );
              })}
            </div>
          ) : (
            <div className={styles.timelineEmpty}>
              <Text size={300}>No deadlines match this filter.</Text>
            </div>
          )}
        </Card>
      )}

      {/* ---- Sections 3 + 4 side-by-side ---- */}
      <div className={styles.twoColGrid}>
        {/* Section 3: Change distribution */}
        <Card className={styles.barChartCard}>
          <Text size={500} weight="semibold">
            Change Distribution
          </Text>
          <Divider />
          {changeEntries
            .sort(([, a], [, b]) => b - a)
            .map(([type, count]) => {
              const pct = Math.round((count / changeTotal) * 100);
              const color =
                CHANGE_TYPE_COLORS[type] || tokens.colorNeutralBackground5;
              return (
                <div key={type} className={styles.barRow}>
                  <div className={styles.barHeader}>
                    <Text size={200}>{type}</Text>
                    <Text size={200} weight="semibold">
                      {count} ({pct}%)
                    </Text>
                  </div>
                  <div className={styles.barTrack}>
                    <div
                      className={styles.barFill}
                      style={{
                        width: `${(count / maxChange) * 100}%`,
                        backgroundColor: color,
                      }}
                    />
                  </div>
                </div>
              );
            })}
        </Card>

        {/* Section 4: Top affected services */}
        <Card className={styles.barChartCard}>
          <Text size={500} weight="semibold">
            Most Impacted Services
          </Text>
          <Divider />
          <div className={styles.servicesScrollArea}>
          {stats.topAffectedServices
            .sort((a, b) => b.retirements - a.retirements)
            .map((svc) => (
              <div key={svc.service} className={styles.serviceRow}>
                <Text className={styles.serviceName} title={svc.service}>
                  {svc.service}
                </Text>
                <div className={styles.serviceBarWrap}>
                  <div
                    className={styles.serviceBar}
                    style={{
                      width: `${(svc.total / maxServiceTotal) * 100}%`,
                    }}
                  />
                </div>
                <Text className={styles.serviceCount}>{svc.total}</Text>
                {svc.retirements > 0 && (
                  <Badge appearance="filled" color="danger" size="small">
                    {svc.retirements} ret.
                  </Badge>
                )}
              </div>
            ))}
          </div>
        </Card>
      </div>

      {/* ---- Section 5: Data sources ---- */}
      <Card className={styles.sourceCard}>
        <div className={styles.sourceItem}>
          <DatabaseRegular />
          <Text size={200}>
            Azure Updates:{" "}
            <Text weight="semibold" size={200}>
              {stats.sourceBreakdown.azureUpdates} items
            </Text>
          </Text>
        </div>
        <div className={styles.sourceItem}>
          <BookRegular />
          <Text size={200}>
            MS Learn Docs:{" "}
            <Text weight="semibold" size={200}>
              {stats.sourceBreakdown.msLearnDocs} items
            </Text>
          </Text>
        </div>
        {stats.latestCrawl && (
          <div className={styles.sourceItem}>
            <ClockRegular />
            <Text size={200}>
              Last crawl:{" "}
              <Text weight="semibold" size={200}>
                {new Date(stats.latestCrawl).toLocaleString()}
              </Text>
            </Text>
          </div>
        )}
      </Card>
    </div>
  );
}
