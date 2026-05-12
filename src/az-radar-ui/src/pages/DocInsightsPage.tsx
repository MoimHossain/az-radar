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
  OpenRegular,
  CodeRegular,
  DismissCircleRegular,
  WarningRegular,
  ShieldCheckmarkRegular,
  NewRegular,
  ArrowCircleUpRegular,
  InfoRegular,
  ArrowTrendingRegular,
} from "@fluentui/react-icons";
import { useEffect, useState, useMemo } from "react";
import { api, type DocInsight, type LlmAnalysis } from "../api/client";

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
  servicesList: {
    display: "flex",
    gap: "4px",
    flexWrap: "wrap" as const,
  },
  // Right-anchored panel
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
    width: "560px",
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
  summaryBox: {
    padding: "14px",
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: "8px",
    gridColumn: "1 / -1",
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
    overflow: "auto",
    whiteSpace: "pre-wrap" as const,
    wordBreak: "break-all" as const,
  },
  rawSection: {
    display: "flex",
    flexDirection: "column",
    gap: "16px",
  },
  rawSectionLabel: {
    fontSize: "12px",
    fontWeight: 600,
    color: tokens.colorNeutralForeground3,
    textTransform: "uppercase" as const,
    marginBottom: "4px",
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
  return (
    changeType === "retirement" ||
    changeType === "deprecation" ||
    changeType === "breaking-change"
  );
}

export function DocInsightsPage() {
  const styles = useStyles();
  const [items, setItems] = useState<DocInsight[]>([]);
  const [loading, setLoading] = useState(true);
  const [searchText, setSearchText] = useState("");
  const [selectedService, setSelectedService] = useState<string>("");
  const [selectedItem, setSelectedItem] = useState<DocInsight | null>(null);
  const [panelTab, setPanelTab] = useState<string>("analysis");

  useEffect(() => {
    api
      .getDocInsights(undefined, 200)
      .then(setItems)
      .catch(console.error)
      .finally(() => setLoading(false));
  }, []);

  const serviceNames = useMemo(() => {
    const names = new Set<string>();
    for (const item of items) {
      if (item.serviceName) names.add(item.serviceName);
    }
    return Array.from(names).sort();
  }, [items]);

  const filteredItems = useMemo(() => {
    return items.filter((item) => {
      if (searchText) {
        const q = searchText.toLowerCase();
        const matchesText =
          item.title.toLowerCase().includes(q) ||
          item.serviceName.toLowerCase().includes(q) ||
          item.snippet?.toLowerCase().includes(q) ||
          item.llmAnalysis?.briefSummary.toLowerCase().includes(q);
        if (!matchesText) return false;
      }
      if (selectedService) {
        if (item.serviceName !== selectedService) return false;
      }
      return true;
    });
  }, [items, searchText, selectedService]);

  const hasActiveFilters = searchText || selectedService;

  if (loading) {
    return (
      <div className={styles.container}>
        <Spinner label="Loading doc insights..." />
      </div>
    );
  }

  return (
    <div className={styles.container}>
      {/* Header */}
      <div className={styles.headerRow}>
        <div className={styles.header}>
          <Text size={700} weight="bold">
            Docs Intelligence
          </Text>
          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
            {filteredItems.length} of {items.length} insights · AI-analyzed
            documentation changes
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
          placeholder="Service"
          selectedOptions={selectedService ? [selectedService] : []}
          onOptionSelect={(_, d) => {
            setSelectedService(d.optionValue ?? "");
          }}
        >
          {serviceNames.map((s) => (
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
              setSelectedService("");
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
              ? "No doc insights yet. Run a documentation crawl job to start analyzing Azure docs."
              : "No insights match your filters."}
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
                  Service
                </TableHeaderCell>
                <TableHeaderCell style={{ width: 140 }}>
                  Change Type
                </TableHeaderCell>
                <TableHeaderCell style={{ width: 110 }}>
                  Severity
                </TableHeaderCell>
                <TableHeaderCell style={{ width: 120 }}>
                  Last Analyzed
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
                      setPanelTab("analysis");
                    }}
                  >
                    <TableCell>
                      {item.llmAnalysis &&
                        changeTypeIcon(item.llmAnalysis.changeType)}
                    </TableCell>
                    <TableCell>
                      <Text
                        weight={isRetirement ? "bold" : "regular"}
                        size={300}
                      >
                        {item.title}
                      </Text>
                    </TableCell>
                    <TableCell>
                      <Badge appearance="tint" size="small">
                        {item.serviceName}
                      </Badge>
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
                      <Text size={200}>
                        {new Date(item.lastAnalyzedAt).toLocaleDateString()}
                      </Text>
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
                {selectedItem.llmAnalysis &&
                  changeTypeIcon(selectedItem.llmAnalysis.changeType)}
                <div>
                  <Text weight="semibold" size={500} block>
                    {selectedItem.title}
                  </Text>
                  <div
                    style={{
                      display: "flex",
                      gap: 6,
                      flexWrap: "wrap",
                      marginTop: 8,
                    }}
                  >
                    <Badge appearance="tint">{selectedItem.serviceName}</Badge>
                    {selectedItem.llmAnalysis && (
                      <>
                        <Badge
                          color={severityColor(
                            selectedItem.llmAnalysis.severity
                          )}
                        >
                          {selectedItem.llmAnalysis.severity}
                        </Badge>
                        <Badge
                          appearance={
                            isRetirementType(
                              selectedItem.llmAnalysis.changeType
                            )
                              ? "filled"
                              : "outline"
                          }
                          color={
                            isRetirementType(
                              selectedItem.llmAnalysis.changeType
                            )
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
                <Tab value="analysis">Analysis</Tab>
                <Tab value="raw" icon={<CodeRegular />}>
                  Raw Data
                </Tab>
              </TabList>
            </div>

            {/* Panel content */}
            <div className={styles.panelContent}>
              {panelTab === "analysis" && (
                <>
                  {selectedItem.llmAnalysis ? (
                    <AnalysisTab
                      analysis={selectedItem.llmAnalysis}
                      styles={styles}
                    />
                  ) : (
                    <Text
                      size={200}
                      style={{ color: tokens.colorNeutralForeground3 }}
                    >
                      No AI analysis available for this insight.
                    </Text>
                  )}

                  <Divider style={{ margin: "16px 0 8px" }} />

                  <div className={styles.linksRow}>
                    {selectedItem.docUrl && (
                      <Link href={selectedItem.docUrl} target="_blank">
                        <OpenRegular style={{ marginRight: 4 }} />
                        View Documentation
                      </Link>
                    )}
                    {selectedItem.llmAnalysis?.microsoftDocLinks?.map(
                      (link, i) => (
                        <Link key={i} href={link} target="_blank">
                          <OpenRegular style={{ marginRight: 4 }} />
                          Reference {i + 1}
                        </Link>
                      )
                    )}
                  </div>
                </>
              )}

              {panelTab === "raw" && (
                <div className={styles.rawSection}>
                  {selectedItem.llmAnalysis && (
                    <div>
                      <Text className={styles.rawSectionLabel} block>
                        LLM Analysis Response (JSON)
                      </Text>
                      <div className={styles.rawJsonBox}>
                        {JSON.stringify(selectedItem.llmAnalysis, null, 2)}
                      </div>
                    </div>
                  )}
                  <div>
                    <Text className={styles.rawSectionLabel} block>
                      Documentation Snippet
                    </Text>
                    <div className={styles.rawJsonBox}>
                      {selectedItem.snippet || "(empty)"}
                    </div>
                  </div>
                  <div>
                    <Text className={styles.rawSectionLabel} block>
                      Doc URL
                    </Text>
                    <div className={styles.rawJsonBox}>
                      {selectedItem.docUrl || "(none)"}
                    </div>
                  </div>
                  <div>
                    <Text className={styles.rawSectionLabel} block>
                      Doc Insight Metadata (JSON)
                    </Text>
                    <div className={styles.rawJsonBox}>
                      {JSON.stringify(
                        {
                          id: selectedItem.id,
                          source: selectedItem.source,
                          serviceName: selectedItem.serviceName,
                          contentHash: selectedItem.contentHash,
                          firstSeenAt: selectedItem.firstSeenAt,
                          lastAnalyzedAt: selectedItem.lastAnalyzedAt,
                          crawlJobId: selectedItem.crawlJobId,
                        },
                        null,
                        2
                      )}
                    </div>
                  </div>
                </div>
              )}
            </div>
          </div>
        </>
      )}
    </div>
  );
}

function AnalysisTab({
  analysis,
  styles,
}: {
  analysis: LlmAnalysis;
  styles: ReturnType<typeof useStyles>;
}) {
  return (
    <div className={styles.detailGrid}>
      <div className={styles.summaryBox}>
        <Text size={200} weight="semibold" block style={{ marginBottom: 4 }}>
          AI Summary
        </Text>
        <Text size={300}>{analysis.briefSummary}</Text>
      </div>

      <div className={styles.detailFieldFull}>
        <Text className={styles.fieldLabel}>Action Required</Text>
        <Text size={300}>{analysis.actionRequired || "None"}</Text>
      </div>

      <div className={styles.detailField}>
        <Text className={styles.fieldLabel}>Effort Estimate</Text>
        <Badge appearance="outline">{analysis.effortEstimate}</Badge>
      </div>
      <div className={styles.detailField}>
        <Text className={styles.fieldLabel}>AI Confidence</Text>
        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <div className={styles.confidenceBar}>
            <div
              className={styles.confidenceFill}
              style={{
                width: `${(analysis.aiConfidence * 100).toFixed(0)}%`,
              }}
            />
          </div>
          <Text size={200}>
            {(analysis.aiConfidence * 100).toFixed(0)}%
          </Text>
        </div>
      </div>

      {analysis.affectedServices.length > 0 && (
        <div className={styles.detailFieldFull}>
          <Text className={styles.fieldLabel}>Affected Services</Text>
          <div className={styles.servicesList}>
            {analysis.affectedServices.map((s) => (
              <Badge key={s} appearance="tint">
                {s}
              </Badge>
            ))}
          </div>
        </div>
      )}

      {analysis.affectedResourceTypes.length > 0 && (
        <div className={styles.detailFieldFull}>
          <Text className={styles.fieldLabel}>Affected Resource Types</Text>
          <div className={styles.servicesList}>
            {analysis.affectedResourceTypes.map((r) => (
              <Badge key={r} appearance="outline" size="small">
                {r}
              </Badge>
            ))}
          </div>
        </div>
      )}

      {analysis.migrationPath && (
        <div className={styles.detailFieldFull}>
          <Text className={styles.fieldLabel}>Migration Path</Text>
          <Text size={300}>{analysis.migrationPath}</Text>
        </div>
      )}
    </div>
  );
}
