import {
  makeStyles,
  tokens,
  Card,
  Text,
  Badge,
  Spinner,
  Accordion,
  AccordionItem,
  AccordionHeader,
  AccordionPanel,
  Link,
  Divider,
} from "@fluentui/react-components";
import {
  ArrowCircleUpRegular,
  DismissCircleRegular,
  ShieldCheckmarkRegular,
  InfoRegular,
  WarningRegular,
  NewRegular,
  ArrowTrendingRegular,
} from "@fluentui/react-icons";
import { useEffect, useState } from "react";
import { api, type FeedItem } from "../api/client";

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    gap: "16px",
    padding: "24px",
  },
  header: {
    display: "flex",
    flexDirection: "column",
    gap: "4px",
  },
  itemCard: {
    marginBottom: "8px",
  },
  itemHeader: {
    display: "flex",
    alignItems: "center",
    gap: "12px",
    width: "100%",
  },
  badges: {
    display: "flex",
    gap: "6px",
    flexWrap: "wrap" as const,
  },
  analysisGrid: {
    display: "grid",
    gridTemplateColumns: "1fr 1fr",
    gap: "16px",
    padding: "12px 0",
  },
  analysisField: {
    display: "flex",
    flexDirection: "column",
    gap: "2px",
  },
  fieldLabel: {
    fontSize: "12px",
    fontWeight: 600,
    color: tokens.colorNeutralForeground3,
    textTransform: "uppercase" as const,
  },
  summaryBox: {
    padding: "12px",
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: "6px",
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
      return <DismissCircleRegular />;
    case "breaking-change":
      return <WarningRegular />;
    case "security-advisory":
      return <ShieldCheckmarkRegular />;
    case "new-feature":
    case "preview":
      return <NewRegular />;
    case "general-availability":
      return <ArrowCircleUpRegular />;
    default:
      return <InfoRegular />;
  }
}

export function FeedItemsPage() {
  const styles = useStyles();
  const [items, setItems] = useState<FeedItem[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    api
      .getFeedItems(undefined, 100)
      .then(setItems)
      .catch(console.error)
      .finally(() => setLoading(false));
  }, []);

  if (loading) {
    return (
      <div className={styles.container}>
        <Spinner label="Loading feed items..." />
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <Text size={700} weight="bold">
          Azure Updates
        </Text>
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
          {items.length} items tracked · AI-analyzed for impact
        </Text>
      </div>

      {items.length === 0 ? (
        <Card style={{ padding: 40, textAlign: "center" }}>
          <ArrowTrendingRegular
            style={{ fontSize: 48, color: tokens.colorNeutralForeground3 }}
          />
          <Text
            block
            size={400}
            style={{
              marginTop: 12,
              color: tokens.colorNeutralForeground3,
            }}
          >
            No feed items yet. Create a crawl job to start ingesting Azure
            updates.
          </Text>
        </Card>
      ) : (
        <Accordion collapsible multiple>
          {items.map((item) => (
            <AccordionItem key={item.id} value={item.id}>
              <AccordionHeader>
                <div className={styles.itemHeader}>
                  {item.llmAnalysis && changeTypeIcon(item.llmAnalysis.changeType)}
                  <Text weight="semibold" style={{ flex: 1 }}>
                    {item.title}
                  </Text>
                  <div className={styles.badges}>
                    {item.llmAnalysis && (
                      <>
                        <Badge
                          color={severityColor(item.llmAnalysis.severity)}
                          size="small"
                        >
                          {item.llmAnalysis.severity}
                        </Badge>
                        <Badge appearance="outline" size="small">
                          {item.llmAnalysis.changeType}
                        </Badge>
                      </>
                    )}
                    <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
                      {new Date(item.publishDate).toLocaleDateString()}
                    </Text>
                  </div>
                </div>
              </AccordionHeader>
              <AccordionPanel>
                <Card className={styles.itemCard}>
                  {item.llmAnalysis ? (
                    <div className={styles.analysisGrid}>
                      <div className={styles.summaryBox}>
                        <Text size={200} weight="semibold" block>
                          AI Summary
                        </Text>
                        <Text size={300}>
                          {item.llmAnalysis.briefSummary}
                        </Text>
                      </div>

                      <div className={styles.analysisField}>
                        <Text className={styles.fieldLabel}>Action Required</Text>
                        <Text size={200}>
                          {item.llmAnalysis.actionRequired || "None"}
                        </Text>
                      </div>

                      <div className={styles.analysisField}>
                        <Text className={styles.fieldLabel}>Effort Estimate</Text>
                        <Badge appearance="outline">
                          {item.llmAnalysis.effortEstimate}
                        </Badge>
                      </div>

                      {item.llmAnalysis.deadline && (
                        <div className={styles.analysisField}>
                          <Text className={styles.fieldLabel}>Deadline</Text>
                          <Text size={200}>{item.llmAnalysis.deadline}</Text>
                        </div>
                      )}

                      {item.llmAnalysis.affectedServices.length > 0 && (
                        <div className={styles.analysisField}>
                          <Text className={styles.fieldLabel}>Affected Services</Text>
                          <div className={styles.servicesList}>
                            {item.llmAnalysis.affectedServices.map((s) => (
                              <Badge key={s} appearance="tint" size="small">
                                {s}
                              </Badge>
                            ))}
                          </div>
                        </div>
                      )}

                      {item.llmAnalysis.migrationPath && (
                        <div
                          className={styles.analysisField}
                          style={{ gridColumn: "1 / -1" }}
                        >
                          <Text className={styles.fieldLabel}>Migration Path</Text>
                          <Text size={200}>
                            {item.llmAnalysis.migrationPath}
                          </Text>
                        </div>
                      )}

                      <div className={styles.analysisField}>
                        <Text className={styles.fieldLabel}>AI Confidence</Text>
                        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                          <div className={styles.confidenceBar}>
                            <div
                              className={styles.confidenceFill}
                              style={{
                                width: `${(item.llmAnalysis.aiConfidence * 100).toFixed(0)}%`,
                              }}
                            />
                          </div>
                          <Text size={200}>
                            {(item.llmAnalysis.aiConfidence * 100).toFixed(0)}%
                          </Text>
                        </div>
                      </div>
                    </div>
                  ) : (
                    <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                      No AI analysis available for this item.
                    </Text>
                  )}

                  <Divider style={{ margin: "12px 0" }} />

                  <div style={{ display: "flex", gap: 16 }}>
                    <Link href={item.link} target="_blank">
                      View on Azure →
                    </Link>
                    {item.llmAnalysis?.microsoftDocLinks?.map((link, i) => (
                      <Link key={i} href={link} target="_blank">
                        Documentation {i + 1} →
                      </Link>
                    ))}
                  </div>
                </Card>
              </AccordionPanel>
            </AccordionItem>
          ))}
        </Accordion>
      )}
    </div>
  );
}
