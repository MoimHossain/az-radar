import {
  makeStyles,
  tokens,
  Card,
  Text,
  Button,
  Badge,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  DialogTrigger,
  Field,
  Select,
  Tooltip,
} from "@fluentui/react-components";
import {
  AddRegular,
  ArrowClockwiseRegular,
  DeleteRegular,
  WarningRegular,
} from "@fluentui/react-icons";
import { useEffect, useState, useCallback } from "react";
import { api, type CrawlJob } from "../api/client";

const STALE_THRESHOLD_MINUTES = 10;

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    gap: "20px",
    padding: "24px",
  },
  headerRow: {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "center",
  },
  statusBadge: {
    textTransform: "capitalize" as const,
  },
  staleRow: {
    backgroundColor: tokens.colorPaletteDarkOrangeBackground1,
  },
  actionsCell: {
    display: "flex",
    gap: "4px",
    alignItems: "center",
  },
  staleIndicator: {
    display: "flex",
    alignItems: "center",
    gap: "4px",
    color: tokens.colorPaletteDarkOrangeForeground1,
  },
});

function statusColor(
  status: string
): "success" | "warning" | "danger" | "informative" | "important" {
  switch (status) {
    case "completed":
      return "success";
    case "processing":
      return "warning";
    case "failed":
      return "danger";
    case "pending":
      return "informative";
    default:
      return "important";
  }
}

function isStaleJob(job: CrawlJob): boolean {
  if (job.status !== "pending" && job.status !== "processing") return false;
  const created = new Date(job.createdAt).getTime();
  const now = Date.now();
  return now - created > STALE_THRESHOLD_MINUTES * 60 * 1000;
}

function isDeletable(job: CrawlJob): boolean {
  return (
    job.status === "completed" ||
    job.status === "failed" ||
    isStaleJob(job)
  );
}

function formatAge(createdAt: string): string {
  const mins = Math.floor(
    (Date.now() - new Date(createdAt).getTime()) / 60000
  );
  if (mins < 60) return `${mins}m ago`;
  const hours = Math.floor(mins / 60);
  if (hours < 24) return `${hours}h ago`;
  return `${Math.floor(hours / 24)}d ago`;
}

export function CrawlJobsPage() {
  const styles = useStyles();
  const [jobs, setJobs] = useState<CrawlJob[]>([]);
  const [loading, setLoading] = useState(true);
  const [creating, setCreating] = useState(false);
  const [deleting, setDeleting] = useState<Set<string>>(new Set());
  const [dialogOpen, setDialogOpen] = useState(false);
  const [jobType, setJobType] = useState("azure-updates");

  const loadJobs = useCallback(() => {
    setLoading(true);
    api
      .getCrawlJobs()
      .then(setJobs)
      .catch(console.error)
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    loadJobs();
    const interval = setInterval(loadJobs, 10000);
    return () => clearInterval(interval);
  }, [loadJobs]);

  const handleCreate = async () => {
    setCreating(true);
    try {
      await api.createCrawlJob(jobType);
      setDialogOpen(false);
      loadJobs();
    } catch (err) {
      console.error("Failed to create job:", err);
    } finally {
      setCreating(false);
    }
  };

  const handleDelete = async (id: string) => {
    setDeleting((prev) => new Set(prev).add(id));
    try {
      await api.deleteCrawlJob(id);
      setJobs((prev) => prev.filter((j) => j.id !== id));
    } catch (err) {
      console.error("Failed to delete job:", err);
    } finally {
      setDeleting((prev) => {
        const next = new Set(prev);
        next.delete(id);
        return next;
      });
    }
  };

  return (
    <div className={styles.container}>
      <div className={styles.headerRow}>
        <div>
          <Text size={700} weight="bold" block>
            Crawling Jobs
          </Text>
          <Text
            size={200}
            style={{ color: tokens.colorNeutralForeground3 }}
          >
            Create and monitor feed crawling tasks
          </Text>
        </div>
        <div style={{ display: "flex", gap: 8 }}>
          <Button
            icon={<ArrowClockwiseRegular />}
            appearance="subtle"
            onClick={loadJobs}
          >
            Refresh
          </Button>
          <Dialog open={dialogOpen} onOpenChange={(_, d) => setDialogOpen(d.open)}>
            <DialogTrigger disableButtonEnhancement>
              <Button icon={<AddRegular />} appearance="primary">
                New Crawl Job
              </Button>
            </DialogTrigger>
            <DialogSurface>
              <DialogBody>
                <DialogTitle>Create Crawl Job</DialogTitle>
                <DialogContent>
                  <Field label="Job Type">
                    <Select
                      value={jobType}
                      onChange={(_, d) => setJobType(d.value)}
                    >
                      <option value="azure-updates">Azure Updates Feed</option>
                      <option value="ms-learn-intelligence">MS Learn Intelligence</option>
                    </Select>
                  </Field>
                </DialogContent>
                <DialogActions>
                  <DialogTrigger disableButtonEnhancement>
                    <Button appearance="secondary">Cancel</Button>
                  </DialogTrigger>
                  <Button
                    appearance="primary"
                    onClick={handleCreate}
                    disabled={creating}
                  >
                    {creating ? <Spinner size="tiny" /> : "Create"}
                  </Button>
                </DialogActions>
              </DialogBody>
            </DialogSurface>
          </Dialog>
        </div>
      </div>

      {loading && jobs.length === 0 ? (
        <Spinner label="Loading jobs..." />
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Type</TableHeaderCell>
                <TableHeaderCell>Status</TableHeaderCell>
                <TableHeaderCell>Created</TableHeaderCell>
                <TableHeaderCell>Completed</TableHeaderCell>
                <TableHeaderCell>Result</TableHeaderCell>
                <TableHeaderCell style={{ width: 80 }}>Actions</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {jobs.map((job) => {
                const stale = isStaleJob(job);
                return (
                  <TableRow
                    key={job.id}
                    className={stale ? styles.staleRow : undefined}
                  >
                    <TableCell>
                      <div>
                        <Badge appearance="outline">{job.jobType}</Badge>
                        <Text
                          font="monospace"
                          size={100}
                          style={{
                            display: "block",
                            marginTop: 2,
                            color: tokens.colorNeutralForeground3,
                          }}
                        >
                          {job.id.substring(0, 8)}…
                        </Text>
                      </div>
                    </TableCell>
                    <TableCell>
                      <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
                        <Badge
                          color={stale ? "warning" : statusColor(job.status)}
                          className={styles.statusBadge}
                        >
                          {job.status}
                        </Badge>
                        {stale && (
                          <Tooltip
                            content={`Unresponsive for ${formatAge(job.createdAt)} — safe to delete`}
                            relationship="description"
                          >
                            <span className={styles.staleIndicator}>
                              <WarningRegular fontSize={14} />
                              <Text size={100}>stale</Text>
                            </span>
                          </Tooltip>
                        )}
                      </div>
                    </TableCell>
                    <TableCell>
                      <Text size={200}>
                        {new Date(job.createdAt).toLocaleString()}
                      </Text>
                    </TableCell>
                    <TableCell>
                      <Text size={200}>
                        {job.completedAt
                          ? new Date(job.completedAt).toLocaleString()
                          : "—"}
                      </Text>
                    </TableCell>
                    <TableCell>
                      {job.result ? (
                        <Text size={200}>
                          {job.result.newItems} new / {job.result.skippedItems}{" "}
                          skipped / {job.result.totalChecked} total
                        </Text>
                      ) : job.error ? (
                        <Tooltip content={job.error} relationship="description">
                          <Text
                            size={200}
                            style={{
                              color: tokens.colorPaletteRedForeground1,
                              maxWidth: 250,
                              overflow: "hidden",
                              textOverflow: "ellipsis",
                              whiteSpace: "nowrap",
                              display: "block",
                            }}
                          >
                            {job.error}
                          </Text>
                        </Tooltip>
                      ) : (
                        <Text size={200}>—</Text>
                      )}
                    </TableCell>
                    <TableCell>
                      <div className={styles.actionsCell}>
                        {isDeletable(job) && (
                          <Tooltip content="Delete job" relationship="label">
                            <Button
                              icon={
                                deleting.has(job.id) ? (
                                  <Spinner size="tiny" />
                                ) : (
                                  <DeleteRegular />
                                )
                              }
                              appearance="subtle"
                              size="small"
                              disabled={deleting.has(job.id)}
                              onClick={() => handleDelete(job.id)}
                            />
                          </Tooltip>
                        )}
                      </div>
                    </TableCell>
                  </TableRow>
                );
              })}
              {jobs.length === 0 && (
                <TableRow>
                  <TableCell colSpan={6}>
                    <Text
                      style={{
                        padding: 20,
                        display: "block",
                        textAlign: "center",
                        color: tokens.colorNeutralForeground3,
                      }}
                    >
                      No crawl jobs yet. Click "New Crawl Job" to get started.
                    </Text>
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </Card>
      )}
    </div>
  );
}
