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
} from "@fluentui/react-components";
import { AddRegular, ArrowClockwiseRegular } from "@fluentui/react-icons";
import { useEffect, useState, useCallback } from "react";
import { api, type CrawlJob } from "../api/client";

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

export function CrawlJobsPage() {
  const styles = useStyles();
  const [jobs, setJobs] = useState<CrawlJob[]>([]);
  const [loading, setLoading] = useState(true);
  const [creating, setCreating] = useState(false);
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
                <TableHeaderCell>ID</TableHeaderCell>
                <TableHeaderCell>Type</TableHeaderCell>
                <TableHeaderCell>Status</TableHeaderCell>
                <TableHeaderCell>Created</TableHeaderCell>
                <TableHeaderCell>Completed</TableHeaderCell>
                <TableHeaderCell>Result</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {jobs.map((job) => (
                <TableRow key={job.id}>
                  <TableCell>
                    <Text font="monospace" size={200}>
                      {job.id.substring(0, 8)}…
                    </Text>
                  </TableCell>
                  <TableCell>
                    <Badge appearance="outline">{job.jobType}</Badge>
                  </TableCell>
                  <TableCell>
                    <Badge
                      color={statusColor(job.status)}
                      className={styles.statusBadge}
                    >
                      {job.status}
                    </Badge>
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
                      <Text
                        size={200}
                        style={{ color: tokens.colorPaletteRedForeground1 }}
                      >
                        {job.error}
                      </Text>
                    ) : (
                      <Text size={200}>—</Text>
                    )}
                  </TableCell>
                </TableRow>
              ))}
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
