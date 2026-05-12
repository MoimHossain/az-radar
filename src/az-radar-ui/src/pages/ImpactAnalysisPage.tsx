import {
  makeStyles,
  tokens,
  Card,
  Text,
  Badge,
  Spinner,
  Input,
  Button,
  Divider,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Tab,
  TabList,
} from "@fluentui/react-components";
import {
  SearchRegular,
  DismissRegular,
  ShieldCheckmarkRegular,
  CodeRegular,
} from "@fluentui/react-icons";
import { useEffect, useState, useMemo } from "react";
import { api, type BlastRadiusSummary, type AffectedResource } from "../api/client";

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
  },
  header: {
    display: "flex",
    flexDirection: "column",
    gap: "4px",
  },
  filterBar: {
    display: "flex",
    alignItems: "center",
    gap: "12px",
    flexWrap: "wrap" as const,
  },
  searchInput: {
    minWidth: "280px",
  },
  tableCard: {
    overflow: "hidden",
  },
  clickableRow: {
    cursor: "pointer",
    "&:hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  retirementRow: {
    cursor: "pointer",
    backgroundColor: tokens.colorPaletteRedBackground1,
    "&:hover": {
      backgroundColor: tokens.colorPaletteRedBackground2,
    },
  },
  boldCount: {
    fontWeight: 700,
    fontSize: "15px",
  },
  redCount: {
    fontWeight: 700,
    fontSize: "15px",
    color: tokens.colorPaletteRedForeground1,
  },

  // Right-anchored panel styles (same pattern as FeedItemsPage)
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
    width: "620px",
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
    padding: "20px 24px 0 24px",
    gap: "12px",
  },
  panelTitleRow: {
    display: "flex",
    gap: "8px",
    alignItems: "flex-start",
    flex: 1,
  },
  panelNav: {
    paddingLeft: "24px",
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  panelContent: {
    flex: 1,
    overflow: "auto",
    padding: "20px 24px 24px 24px",
  },
  detailGrid: {
    display: "grid",
    gridTemplateColumns: "1fr 1fr",
    gap: "16px",
    marginTop: "16px",
  },
  detailField: {
    display: "flex",
    flexDirection: "column",
    gap: "4px",
  },
  detailFieldFull: {
    display: "flex",
    flexDirection: "column",
    gap: "4px",
    gridColumn: "1 / -1",
  },
  fieldLabel: {
    fontSize: "12px",
    fontWeight: 600,
    color: tokens.colorNeutralForeground3,
    textTransform: "uppercase" as const,
  },
  breakdownSection: {
    display: "flex",
    flexDirection: "column",
    gap: "6px",
    gridColumn: "1 / -1",
    marginTop: "8px",
  },
  breakdownRow: {
    display: "flex",
    alignItems: "center",
    gap: "8px",
  },
  breakdownLabel: {
    minWidth: "140px",
    fontSize: "13px",
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap" as const,
  },
  breakdownBarTrack: {
    flex: 1,
    height: "8px",
    borderRadius: "4px",
    backgroundColor: tokens.colorNeutralBackground5,
    overflow: "hidden",
  },
  breakdownBarFill: {
    height: "100%",
    borderRadius: "4px",
    backgroundColor: tokens.colorBrandBackground,
  },
  breakdownCount: {
    minWidth: "36px",
    textAlign: "right" as const,
    fontSize: "12px",
    fontWeight: 600,
  },
  resourcesTable: {
    gridColumn: "1 / -1",
    marginTop: "8px",
  },
  tagsList: {
    display: "flex",
    gap: "4px",
    flexWrap: "wrap" as const,
  },
  rawJsonBox: {
    padding: "12px",
    backgroundColor: tokens.colorNeutralBackground4,
    borderRadius: "6px",
    fontFamily: "Consolas, 'Courier New', monospace",
    fontSize: "12px",
    lineHeight: "1.5",
    overflow: "auto",
    whiteSpace: "pre-wrap" as const,
    wordBreak: "break-all" as const,
  },
  emptyState: {
    padding: "60px 40px",
    textAlign: "center" as const,
  },
});

function formatAge(dateStr: string): string {
  const mins = Math.floor((Date.now() - new Date(dateStr).getTime()) / 60000);
  if (mins < 60) return `${mins}m ago`;
  const hours = Math.floor(mins / 60);
  if (hours < 24) return `${hours}h ago`;
  return `${Math.floor(hours / 24)}d ago`;
}

function isRetirementType(changeType: string): boolean {
  return changeType === "retirement" || changeType === "deprecation";
}

function confidenceColor(confidence: string): "success" | "warning" | "informative" {
  switch (confidence) {
    case "high":
      return "success";
    case "potential":
      return "warning";
    default:
      return "informative";
  }
}

function severityColor(
  severity: string
): "danger" | "warning" | "success" | "informative" | "important" {
  switch (severity) {
    case "critical":
      return "danger";
    case "high":
      return "warning";
    case "medium":
      return "important";
    case "low":
      return "success";
    default:
      return "informative";
  }
}

export function ImpactAnalysisPage() {
  const styles = useStyles();
  const [items, setItems] = useState<BlastRadiusSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [searchText, setSearchText] = useState("");
  const [selectedItem, setSelectedItem] = useState<BlastRadiusSummary | null>(null);
  const [panelTab, setPanelTab] = useState<string>("details");

  useEffect(() => {
    api
      .getBlastRadiusSummaries()
      .then(setItems)
      .catch(console.error)
      .finally(() => setLoading(false));
  }, []);

  const filteredItems = useMemo(() => {
    if (!searchText) return items;
    const q = searchText.toLowerCase();
    return items.filter(
      (item) =>
        item.sourceTitle.toLowerCase().includes(q) ||
        item.resourceType.toLowerCase().includes(q) ||
        item.changeType.toLowerCase().includes(q)
    );
  }, [items, searchText]);

  const latestScan = useMemo(() => {
    if (items.length === 0) return null;
    return items.reduce((latest, item) =>
      new Date(item.scannedAt) > new Date(latest.scannedAt) ? item : latest
    ).scannedAt;
  }, [items]);

  if (loading) {
    return (
      <div className={styles.container}>
        <Spinner label="Loading impact analysis data..." />
      </div>
    );
  }

  return (
    <div className={styles.container}>
      {/* Header */}
      <div className={styles.headerRow}>
        <div className={styles.header}>
          <Text size={700} weight="bold">
            Impact Analysis
          </Text>
          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
            {items.length} blast radius results
            {latestScan && ` · Last scan ${formatAge(latestScan)}`}
          </Text>
        </div>
      </div>

      {/* Filter bar */}
      <div className={styles.filterBar}>
        <Input
          className={styles.searchInput}
          contentBefore={<SearchRegular />}
          placeholder="Filter by keyword"
          value={searchText}
          onChange={(_, d) => setSearchText(d.value)}
        />
        {searchText && (
          <Button
            icon={<DismissRegular />}
            appearance="subtle"
            size="small"
            onClick={() => setSearchText("")}
          >
            Clear
          </Button>
        )}
      </div>

      {/* Table or empty state */}
      {filteredItems.length === 0 ? (
        <Card className={styles.emptyState}>
          <ShieldCheckmarkRegular
            style={{ fontSize: 48, color: tokens.colorNeutralForeground3 }}
          />
          <Text
            block
            size={400}
            style={{ marginTop: 12, color: tokens.colorNeutralForeground3 }}
          >
            {items.length === 0
              ? "No blast radius data yet. Run a Blast Radius Scan from the Crawling Jobs page."
              : "No items match your filter."}
          </Text>
        </Card>
      ) : (
        <Card className={styles.tableCard}>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Retirement</TableHeaderCell>
                <TableHeaderCell style={{ width: 160 }}>Resource Type</TableHeaderCell>
                <TableHeaderCell style={{ width: 120 }}>Affected Resources</TableHeaderCell>
                <TableHeaderCell style={{ width: 110 }}>Subscriptions</TableHeaderCell>
                <TableHeaderCell style={{ width: 90 }}>Regions</TableHeaderCell>
                <TableHeaderCell style={{ width: 110 }}>Confidence</TableHeaderCell>
                <TableHeaderCell style={{ width: 100 }}>Scanned</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {filteredItems.map((item) => {
                const retirement = isRetirementType(item.changeType);
                return (
                  <TableRow
                    key={item.id}
                    className={retirement ? styles.retirementRow : styles.clickableRow}
                    onClick={() => {
                      setSelectedItem(item);
                      setPanelTab("details");
                    }}
                  >
                    <TableCell>
                      <Text
                        size={300}
                        weight={retirement ? "bold" : "regular"}
                        style={{
                          display: "block",
                          maxWidth: 320,
                          overflow: "hidden",
                          textOverflow: "ellipsis",
                          whiteSpace: "nowrap",
                        }}
                        title={item.sourceTitle}
                      >
                        {item.sourceTitle}
                      </Text>
                    </TableCell>
                    <TableCell>
                      <Badge appearance="outline" size="small">
                        {item.resourceType}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      <Text
                        className={
                          item.totalResources > 0
                            ? retirement
                              ? styles.redCount
                              : styles.boldCount
                            : undefined
                        }
                      >
                        {item.totalResources}
                      </Text>
                    </TableCell>
                    <TableCell>
                      <Text size={200}>{item.subscriptionCount}</Text>
                    </TableCell>
                    <TableCell>
                      <Text size={200}>{Object.keys(item.regionBreakdown).length}</Text>
                    </TableCell>
                    <TableCell>
                      <Badge color={confidenceColor(item.matchConfidence)} size="small">
                        {item.matchConfidence}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      <Text size={200}>{formatAge(item.scannedAt)}</Text>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </Card>
      )}

      {/* Right-anchored detail panel */}
      {selectedItem && (
        <>
          <div
            className={styles.panelBackdrop}
            onClick={() => setSelectedItem(null)}
          />
          <div className={styles.panel}>
            {/* Panel header */}
            <div className={styles.panelHeader}>
              <div className={styles.panelTitleRow}>
                <div>
                  <Text weight="semibold" size={500} block>
                    {selectedItem.sourceTitle}
                  </Text>
                  <div
                    style={{ display: "flex", gap: 6, flexWrap: "wrap", marginTop: 8 }}
                  >
                    <Badge color={severityColor(selectedItem.severity)}>
                      {selectedItem.severity}
                    </Badge>
                    <Badge
                      appearance={isRetirementType(selectedItem.changeType) ? "filled" : "outline"}
                      color={isRetirementType(selectedItem.changeType) ? "danger" : "informative"}
                    >
                      {selectedItem.changeType}
                    </Badge>
                    {selectedItem.deadline && (
                      <Badge appearance="outline" color="warning">
                        Deadline: {selectedItem.deadline}
                      </Badge>
                    )}
                    <Badge color={confidenceColor(selectedItem.matchConfidence)}>
                      {selectedItem.matchConfidence} confidence
                    </Badge>
                  </div>
                </div>
              </div>
              <Button
                appearance="subtle"
                icon={<DismissRegular />}
                onClick={() => setSelectedItem(null)}
              />
            </div>

            {/* Tab navigation */}
            <div className={styles.panelNav}>
              <TabList
                selectedValue={panelTab}
                onTabSelect={(_, d) => setPanelTab(d.value as string)}
                size="small"
              >
                <Tab value="details">Details</Tab>
                <Tab value="resources">
                  Resources ({selectedItem.topResources.length})
                </Tab>
                <Tab value="raw" icon={<CodeRegular />}>
                  Raw Data
                </Tab>
              </TabList>
            </div>

            {/* Panel content */}
            <div className={styles.panelContent}>
              {panelTab === "details" && (
                <div className={styles.detailGrid}>
                  <div className={styles.detailField}>
                    <Text className={styles.fieldLabel}>Resource Type</Text>
                    <Badge appearance="outline">{selectedItem.resourceType}</Badge>
                  </div>
                  <div className={styles.detailField}>
                    <Text className={styles.fieldLabel}>Total Affected</Text>
                    <Text size={500} weight="bold">
                      {selectedItem.totalResources}
                    </Text>
                  </div>
                  <div className={styles.detailField}>
                    <Text className={styles.fieldLabel}>Subscriptions</Text>
                    <Text size={400}>{selectedItem.subscriptionCount}</Text>
                  </div>
                  <div className={styles.detailField}>
                    <Text className={styles.fieldLabel}>Scanned</Text>
                    <Text size={200}>
                      {new Date(selectedItem.scannedAt).toLocaleString()}
                    </Text>
                  </div>

                  {/* Region breakdown */}
                  {Object.keys(selectedItem.regionBreakdown).length > 0 && (
                    <div className={styles.breakdownSection}>
                      <Text className={styles.fieldLabel}>Region Breakdown</Text>
                      <Divider />
                      {renderBreakdown(selectedItem.regionBreakdown, styles)}
                    </div>
                  )}

                  {/* Subscription breakdown */}
                  {Object.keys(selectedItem.subscriptionBreakdown).length > 0 && (
                    <div className={styles.breakdownSection}>
                      <Text className={styles.fieldLabel}>Subscription Breakdown</Text>
                      <Divider />
                      {renderBreakdown(selectedItem.subscriptionBreakdown, styles)}
                    </div>
                  )}
                </div>
              )}

              {panelTab === "resources" && (
                <div className={styles.resourcesTable}>
                  {selectedItem.topResources.length === 0 ? (
                    <Text
                      size={200}
                      style={{ color: tokens.colorNeutralForeground3 }}
                    >
                      No resource details available.
                    </Text>
                  ) : (
                    <Table size="small">
                      <TableHeader>
                        <TableRow>
                          <TableHeaderCell>Name</TableHeaderCell>
                          <TableHeaderCell>Resource Group</TableHeaderCell>
                          <TableHeaderCell>Location</TableHeaderCell>
                          <TableHeaderCell>SKU</TableHeaderCell>
                          <TableHeaderCell>Tags</TableHeaderCell>
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        {selectedItem.topResources.map((res, idx) => (
                          <ResourceRow key={idx} resource={res} styles={styles} />
                        ))}
                      </TableBody>
                    </Table>
                  )}
                </div>
              )}

              {panelTab === "raw" && (
                <div className={styles.rawJsonBox}>
                  {JSON.stringify(selectedItem, null, 2)}
                </div>
              )}
            </div>
          </div>
        </>
      )}
    </div>
  );
}

function renderBreakdown(
  data: Record<string, number>,
  styles: ReturnType<typeof useStyles>
) {
  const entries = Object.entries(data).sort(([, a], [, b]) => b - a);
  const max = Math.max(...entries.map(([, v]) => v), 1);
  return entries.map(([label, count]) => (
    <div key={label} className={styles.breakdownRow}>
      <Text className={styles.breakdownLabel} title={label}>
        {label}
      </Text>
      <div className={styles.breakdownBarTrack}>
        <div
          className={styles.breakdownBarFill}
          style={{ width: `${(count / max) * 100}%` }}
        />
      </div>
      <Text className={styles.breakdownCount}>{count}</Text>
    </div>
  ));
}

function ResourceRow({
  resource,
  styles,
}: {
  resource: AffectedResource;
  styles: ReturnType<typeof useStyles>;
}) {
  const tagEntries = Object.entries(resource.tags ?? {});
  return (
    <TableRow>
      <TableCell>
        <Text size={200} weight="semibold">
          {resource.name}
        </Text>
        <Text
          size={100}
          style={{ display: "block", color: tokens.colorNeutralForeground3 }}
        >
          {resource.subscriptionId.substring(0, 8)}…
        </Text>
      </TableCell>
      <TableCell>
        <Text size={200}>{resource.resourceGroup}</Text>
      </TableCell>
      <TableCell>
        <Text size={200}>{resource.location}</Text>
      </TableCell>
      <TableCell>
        <Text size={200}>{resource.sku || "—"}</Text>
      </TableCell>
      <TableCell>
        <div className={styles.tagsList}>
          {tagEntries.length === 0 ? (
            <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
              —
            </Text>
          ) : (
            tagEntries.slice(0, 3).map(([k, v]) => (
              <Badge key={k} appearance="tint" size="small">
                {k}: {v}
              </Badge>
            ))
          )}
          {tagEntries.length > 3 && (
            <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
              +{tagEntries.length - 3} more
            </Text>
          )}
        </div>
      </TableCell>
    </TableRow>
  );
}
