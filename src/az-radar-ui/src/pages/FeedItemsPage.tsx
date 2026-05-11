import {
  makeStyles,
  tokens,
  Card,
  Text,
  Badge,
  Spinner,
  Link,
  Divider,
  Input,
  Dropdown,
  Option,
  Button,
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  DialogActions,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
} from "@fluentui/react-components";
import {
  SearchRegular,
  DismissRegular,
  DismissCircleRegular,
  WarningRegular,
  ShieldCheckmarkRegular,
  NewRegular,
  ArrowCircleUpRegular,
  InfoRegular,
  ArrowTrendingRegular,
  OpenRegular,
  CodeRegular,
  ChevronDownRegular,
  ChevronRightRegular,
} from "@fluentui/react-icons";
import { useEffect, useState, useMemo } from "react";
import { api, type FeedItem } from "../api/client";

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
  filterDropdown: {
    minWidth: "170px",
  },
  tableCard: {
    overflow: "hidden",
  },
  retirementRow: {
    backgroundColor: tokens.colorPaletteRedBackground1,
    "&:hover": {
      backgroundColor: tokens.colorPaletteRedBackground2,
    },
  },
  deprecationRow: {
    backgroundColor: tokens.colorPaletteDarkOrangeBackground1,
    "&:hover": {
      backgroundColor: tokens.colorPaletteDarkOrangeBackground2,
    },
  },
  normalRow: {
    cursor: "pointer",
    "&:hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  clickableRow: {
    cursor: "pointer",
  },
  titleCell: {
    display: "flex",
    alignItems: "center",
    gap: "8px",
  },
  // Detail dialog styles
  dialogSurface: {
    maxWidth: "680px",
    width: "100%",
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
  summaryBox: {
    padding: "14px",
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: "8px",
    gridColumn: "1 / -1",
  },
  servicesList: {
    display: "flex",
    gap: "4px",
    flexWrap: "wrap" as const,
  },
  confidenceBar: {
    height: "6px",
    borderRadius: "3px",
    backgroundColor: tokens.colorNeutralBackground5,
    overflow: "hidden",
    width: "120px",
  },
  confidenceFill: {
    height: "100%",
    borderRadius: "3px",
    backgroundColor: tokens.colorBrandBackground,
  },
  retirementIndicator: {
    width: "4px",
    borderRadius: "2px",
    alignSelf: "stretch",
    flexShrink: 0,
  },
  dialogTitleRow: {
    display: "flex",
    gap: "8px",
    alignItems: "center",
    flexWrap: "wrap" as const,
  },
  linksRow: {
    display: "flex",
    gap: "16px",
    marginTop: "8px",
  },
  rawJsonBox: {
    padding: "12px",
    backgroundColor: tokens.colorNeutralBackground4,
    borderRadius: "6px",
    fontFamily: "Consolas, 'Courier New', monospace",
    fontSize: "12px",
    lineHeight: "1.5",
    maxHeight: "300px",
    overflow: "auto",
    whiteSpace: "pre-wrap" as const,
    wordBreak: "break-all" as const,
    marginTop: "8px",
  },
  debugToggle: {
    display: "flex",
    alignItems: "center",
    gap: "6px",
    cursor: "pointer",
    userSelect: "none" as const,
    color: tokens.colorNeutralForeground3,
    marginTop: "4px",
    "&:hover": {
      color: tokens.colorNeutralForeground1,
    },
  },
});

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

function changeTypeIcon(changeType: string) {
  switch (changeType) {
    case "retirement":
    case "deprecation":
      return <DismissCircleRegular style={{ color: tokens.colorPaletteRedForeground1 }} />;
    case "breaking-change":
      return <WarningRegular style={{ color: tokens.colorPaletteDarkOrangeForeground1 }} />;
    case "security-advisory":
      return <ShieldCheckmarkRegular style={{ color: tokens.colorPaletteRedForeground1 }} />;
    case "new-feature":
    case "preview":
      return <NewRegular style={{ color: tokens.colorPaletteGreenForeground1 }} />;
    case "general-availability":
      return <ArrowCircleUpRegular style={{ color: tokens.colorBrandForeground1 }} />;
    default:
      return <InfoRegular />;
  }
}

function isRetirementType(changeType: string | undefined): boolean {
  return changeType === "retirement" || changeType === "deprecation" || changeType === "breaking-change";
}

const ALL_CHANGE_TYPES = [
  "retirement",
  "deprecation",
  "breaking-change",
  "security-advisory",
  "new-feature",
  "migration-required",
  "preview",
  "general-availability",
  "update",
];

const ALL_SEVERITIES = ["critical", "high", "medium", "low", "informational"];

export function FeedItemsPage() {
  const styles = useStyles();
  const [items, setItems] = useState<FeedItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [searchText, setSearchText] = useState("");
  const [selectedChangeTypes, setSelectedChangeTypes] = useState<string[]>([]);
  const [selectedSeverities, setSelectedSeverities] = useState<string[]>([]);
  const [selectedItem, setSelectedItem] = useState<FeedItem | null>(null);
  const [showRawLlm, setShowRawLlm] = useState(false);
  const [showRawFeed, setShowRawFeed] = useState(false);

  useEffect(() => {
    api
      .getFeedItems(undefined, 200)
      .then(setItems)
      .catch(console.error)
      .finally(() => setLoading(false));
  }, []);

  const filteredItems = useMemo(() => {
    return items.filter((item) => {
      if (searchText) {
        const q = searchText.toLowerCase();
        const matchesText =
          item.title.toLowerCase().includes(q) ||
          item.summary.toLowerCase().includes(q) ||
          item.llmAnalysis?.affectedServices.some((s) =>
            s.toLowerCase().includes(q)
          ) ||
          item.llmAnalysis?.briefSummary.toLowerCase().includes(q);
        if (!matchesText) return false;
      }
      if (selectedChangeTypes.length > 0) {
        if (
          !item.llmAnalysis ||
          !selectedChangeTypes.includes(item.llmAnalysis.changeType)
        )
          return false;
      }
      if (selectedSeverities.length > 0) {
        if (
          !item.llmAnalysis ||
          !selectedSeverities.includes(item.llmAnalysis.severity)
        )
          return false;
      }
      return true;
    });
  }, [items, searchText, selectedChangeTypes, selectedSeverities]);

  const hasActiveFilters =
    searchText || selectedChangeTypes.length > 0 || selectedSeverities.length > 0;

  if (loading) {
    return (
      <div className={styles.container}>
        <Spinner label="Loading feed items..." />
      </div>
    );
  }

  return (
    <div className={styles.container}>
      {/* Header */}
      <div className={styles.headerRow}>
        <div className={styles.header}>
          <Text size={700} weight="bold">
            Azure Updates
          </Text>
          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
            {filteredItems.length} of {items.length} items · AI-analyzed for
            impact
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
        <Dropdown
          className={styles.filterDropdown}
          placeholder="Change Type"
          multiselect
          selectedOptions={selectedChangeTypes}
          onOptionSelect={(_, d) => {
            setSelectedChangeTypes(d.selectedOptions);
          }}
        >
          {ALL_CHANGE_TYPES.map((ct) => (
            <Option key={ct} value={ct}>
              {ct}
            </Option>
          ))}
        </Dropdown>
        <Dropdown
          className={styles.filterDropdown}
          placeholder="Severity"
          multiselect
          selectedOptions={selectedSeverities}
          onOptionSelect={(_, d) => {
            setSelectedSeverities(d.selectedOptions);
          }}
        >
          {ALL_SEVERITIES.map((s) => (
            <Option key={s} value={s}>
              {s}
            </Option>
          ))}
        </Dropdown>

        {hasActiveFilters && (
          <Button
            icon={<DismissRegular />}
            appearance="subtle"
            size="small"
            onClick={() => {
              setSearchText("");
              setSelectedChangeTypes([]);
              setSelectedSeverities([]);
            }}
          >
            Clear filters
          </Button>
        )}
      </div>

      {/* Table */}
      {filteredItems.length === 0 ? (
        <Card style={{ padding: 40, textAlign: "center" }}>
          <ArrowTrendingRegular
            style={{ fontSize: 48, color: tokens.colorNeutralForeground3 }}
          />
          <Text
            block
            size={400}
            style={{ marginTop: 12, color: tokens.colorNeutralForeground3 }}
          >
            {items.length === 0
              ? "No feed items yet. Create a crawl job to start ingesting Azure updates."
              : "No items match your filters."}
          </Text>
        </Card>
      ) : (
        <Card className={styles.tableCard}>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHeaderCell style={{ width: 40 }}></TableHeaderCell>
                <TableHeaderCell>Title</TableHeaderCell>
                <TableHeaderCell style={{ width: 140 }}>
                  Change Type
                </TableHeaderCell>
                <TableHeaderCell style={{ width: 110 }}>
                  Severity
                </TableHeaderCell>
                <TableHeaderCell style={{ width: 200 }}>
                  Affected Services
                </TableHeaderCell>
                <TableHeaderCell style={{ width: 110 }}>
                  Published
                </TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {filteredItems.map((item) => {
                const isRetirement = isRetirementType(
                  item.llmAnalysis?.changeType
                );
                const rowClass = isRetirement
                  ? item.llmAnalysis?.changeType === "retirement"
                    ? `${styles.clickableRow} ${styles.retirementRow}`
                    : `${styles.clickableRow} ${styles.deprecationRow}`
                  : `${styles.clickableRow} ${styles.normalRow}`;

                return (
                  <TableRow
                    key={item.id}
                    className={rowClass}
                    onClick={() => {
                      setSelectedItem(item);
                      setShowRawLlm(false);
                      setShowRawFeed(false);
                    }}
                  >
                    <TableCell>
                      {item.llmAnalysis &&
                        changeTypeIcon(item.llmAnalysis.changeType)}
                    </TableCell>
                    <TableCell>
                      <div>
                        <Text
                          weight={isRetirement ? "bold" : "regular"}
                          size={300}
                        >
                          {item.title}
                        </Text>
                      </div>
                    </TableCell>
                    <TableCell>
                      {item.llmAnalysis && (
                        <Badge
                          appearance={isRetirement ? "filled" : "outline"}
                          color={isRetirement ? "danger" : "informative"}
                          size="small"
                        >
                          {item.llmAnalysis.changeType}
                        </Badge>
                      )}
                    </TableCell>
                    <TableCell>
                      {item.llmAnalysis && (
                        <Badge
                          color={severityColor(item.llmAnalysis.severity)}
                          size="small"
                        >
                          {item.llmAnalysis.severity}
                        </Badge>
                      )}
                    </TableCell>
                    <TableCell>
                      <div className={styles.servicesList}>
                        {item.llmAnalysis?.affectedServices
                          .slice(0, 2)
                          .map((s) => (
                            <Badge
                              key={s}
                              appearance="tint"
                              size="small"
                            >
                              {s}
                            </Badge>
                          ))}
                        {(item.llmAnalysis?.affectedServices.length ?? 0) >
                          2 && (
                          <Text
                            size={100}
                            style={{
                              color: tokens.colorNeutralForeground3,
                            }}
                          >
                            +
                            {item.llmAnalysis!.affectedServices.length - 2}{" "}
                            more
                          </Text>
                        )}
                      </div>
                    </TableCell>
                    <TableCell>
                      <Text size={200}>
                        {new Date(item.publishDate).toLocaleDateString()}
                      </Text>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </Card>
      )}

      {/* Detail Dialog */}
      <Dialog
        open={selectedItem !== null}
        onOpenChange={(_, d) => {
          if (!d.open) setSelectedItem(null);
        }}
      >
        <DialogSurface className={styles.dialogSurface}>
          {selectedItem && (
            <DialogBody>
              <DialogTitle
                action={
                  <Button
                    appearance="subtle"
                    icon={<DismissRegular />}
                    onClick={() => setSelectedItem(null)}
                  />
                }
              >
                <div className={styles.dialogTitleRow}>
                  {selectedItem.llmAnalysis &&
                    changeTypeIcon(selectedItem.llmAnalysis.changeType)}
                  <span>{selectedItem.title}</span>
                </div>
              </DialogTitle>
              <DialogContent>
                {/* Badges row */}
                <div
                  style={{ display: "flex", gap: 8, flexWrap: "wrap", marginBottom: 12 }}
                >
                  {selectedItem.llmAnalysis && (
                    <>
                      <Badge
                        color={severityColor(selectedItem.llmAnalysis.severity)}
                      >
                        {selectedItem.llmAnalysis.severity}
                      </Badge>
                      <Badge
                        appearance={
                          isRetirementType(selectedItem.llmAnalysis.changeType)
                            ? "filled"
                            : "outline"
                        }
                        color={
                          isRetirementType(selectedItem.llmAnalysis.changeType)
                            ? "danger"
                            : "informative"
                        }
                      >
                        {selectedItem.llmAnalysis.changeType}
                      </Badge>
                      {selectedItem.llmAnalysis.deadline && (
                        <Badge appearance="outline" color="warning">
                          Deadline: {selectedItem.llmAnalysis.deadline}
                        </Badge>
                      )}
                    </>
                  )}
                  <Badge appearance="outline">
                    {new Date(selectedItem.publishDate).toLocaleDateString()}
                  </Badge>
                </div>

                {selectedItem.llmAnalysis ? (
                  <div className={styles.detailGrid}>
                    {/* AI Summary */}
                    <div className={styles.summaryBox}>
                      <Text
                        size={200}
                        weight="semibold"
                        block
                        style={{ marginBottom: 4 }}
                      >
                        AI Summary
                      </Text>
                      <Text size={300}>
                        {selectedItem.llmAnalysis.briefSummary}
                      </Text>
                    </div>

                    {/* Action Required */}
                    <div className={styles.detailFieldFull}>
                      <Text className={styles.fieldLabel}>
                        Action Required
                      </Text>
                      <Text size={300}>
                        {selectedItem.llmAnalysis.actionRequired || "None"}
                      </Text>
                    </div>

                    {/* Effort & Confidence */}
                    <div className={styles.detailField}>
                      <Text className={styles.fieldLabel}>
                        Effort Estimate
                      </Text>
                      <Badge appearance="outline">
                        {selectedItem.llmAnalysis.effortEstimate}
                      </Badge>
                    </div>
                    <div className={styles.detailField}>
                      <Text className={styles.fieldLabel}>
                        AI Confidence
                      </Text>
                      <div
                        style={{
                          display: "flex",
                          alignItems: "center",
                          gap: 8,
                        }}
                      >
                        <div className={styles.confidenceBar}>
                          <div
                            className={styles.confidenceFill}
                            style={{
                              width: `${(selectedItem.llmAnalysis.aiConfidence * 100).toFixed(0)}%`,
                            }}
                          />
                        </div>
                        <Text size={200}>
                          {(
                            selectedItem.llmAnalysis.aiConfidence * 100
                          ).toFixed(0)}
                          %
                        </Text>
                      </div>
                    </div>

                    {/* Affected Services */}
                    {selectedItem.llmAnalysis.affectedServices.length > 0 && (
                      <div className={styles.detailFieldFull}>
                        <Text className={styles.fieldLabel}>
                          Affected Services
                        </Text>
                        <div className={styles.servicesList}>
                          {selectedItem.llmAnalysis.affectedServices.map(
                            (s) => (
                              <Badge key={s} appearance="tint">
                                {s}
                              </Badge>
                            )
                          )}
                        </div>
                      </div>
                    )}

                    {/* Affected Resource Types */}
                    {selectedItem.llmAnalysis.affectedResourceTypes.length >
                      0 && (
                      <div className={styles.detailFieldFull}>
                        <Text className={styles.fieldLabel}>
                          Affected Resource Types
                        </Text>
                        <div className={styles.servicesList}>
                          {selectedItem.llmAnalysis.affectedResourceTypes.map(
                            (r) => (
                              <Badge
                                key={r}
                                appearance="outline"
                                size="small"
                              >
                                {r}
                              </Badge>
                            )
                          )}
                        </div>
                      </div>
                    )}

                    {/* Migration Path */}
                    {selectedItem.llmAnalysis.migrationPath && (
                      <div className={styles.detailFieldFull}>
                        <Text className={styles.fieldLabel}>
                          Migration Path
                        </Text>
                        <Text size={300}>
                          {selectedItem.llmAnalysis.migrationPath}
                        </Text>
                      </div>
                    )}
                  </div>
                ) : (
                  <Text
                    size={200}
                    style={{ color: tokens.colorNeutralForeground3 }}
                  >
                    No AI analysis available for this item.
                  </Text>
                )}

                <Divider style={{ margin: "16px 0 8px" }} />

                {/* Links */}
                <div className={styles.linksRow}>
                  <Link href={selectedItem.link} target="_blank">
                    <OpenRegular style={{ marginRight: 4 }} />
                    View on Azure
                  </Link>
                  {selectedItem.llmAnalysis?.microsoftDocLinks?.map(
                    (link, i) => (
                      <Link key={i} href={link} target="_blank">
                        <OpenRegular style={{ marginRight: 4 }} />
                        Documentation {i + 1}
                      </Link>
                    )
                  )}
                </div>

                {/* Diagnostics: Raw LLM Response */}
                {selectedItem.llmAnalysis && (
                  <>
                    <Divider style={{ margin: "16px 0 8px" }} />
                    <div
                      className={styles.debugToggle}
                      onClick={() => setShowRawLlm((v) => !v)}
                    >
                      {showRawLlm ? (
                        <ChevronDownRegular fontSize={12} />
                      ) : (
                        <ChevronRightRegular fontSize={12} />
                      )}
                      <CodeRegular fontSize={14} />
                      <Text size={200} weight="semibold">
                        Raw LLM Analysis (JSON)
                      </Text>
                    </div>
                    {showRawLlm && (
                      <div className={styles.rawJsonBox}>
                        {JSON.stringify(selectedItem.llmAnalysis, null, 2)}
                      </div>
                    )}
                  </>
                )}

                {/* Diagnostics: Raw Feed Content */}
                {selectedItem.rawContent && (
                  <>
                    <div
                      className={styles.debugToggle}
                      onClick={() => setShowRawFeed((v) => !v)}
                      style={{ marginTop: 8 }}
                    >
                      {showRawFeed ? (
                        <ChevronDownRegular fontSize={12} />
                      ) : (
                        <ChevronRightRegular fontSize={12} />
                      )}
                      <CodeRegular fontSize={14} />
                      <Text size={200} weight="semibold">
                        Raw Feed Content
                      </Text>
                    </div>
                    {showRawFeed && (
                      <div className={styles.rawJsonBox}>
                        {selectedItem.rawContent}
                      </div>
                    )}
                  </>
                )}
              </DialogContent>
              <DialogActions>
                <Button
                  appearance="secondary"
                  onClick={() => setSelectedItem(null)}
                >
                  Close
                </Button>
              </DialogActions>
            </DialogBody>
          )}
        </DialogSurface>
      </Dialog>
    </div>
  );
}
