import {
  Badge, Button, Card, Checkbox, Field, Input, Select, Spinner, Text,
  Table, TableHeader, TableHeaderCell, TableBody, TableRow, TableCell,
  makeStyles, tokens,
} from "@fluentui/react-components";
import { useCallback, useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { api, type JobSchedule, type ScheduleRequest, type ScheduleRun } from "../api/client";

const labels: Record<string, string> = {
  "azure-updates": "Azure Updates",
  "ms-learn-intelligence": "MS Learn Intelligence",
  "github-crawl": "GitHub Change Radar",
  "blast-radius-scan": "Blast Radius Scan",
};
const days = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
const useStyles = makeStyles({
  page: { padding: "24px", display: "flex", flexDirection: "column", gap: "20px" },
  row: { display: "flex", gap: "12px", alignItems: "center", flexWrap: "wrap" },
  form: { display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(230px, 1fr))", gap: "16px" },
  error: { color: tokens.colorPaletteRedForeground1 },
  history: { display: "flex", flexDirection: "column", gap: "12px" },
});
function initialRequest(): ScheduleRequest {
  const date = new Date();
  const localDate = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
  return {
    name: "", frequency: "daily", localDate, localTime: "00:00",
    timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC",
    weekdays: [1], primary: { jobType: "azure-updates", skipLlmAnalysis: false }, postJob: null,
  };
}
function when(value?: string) { return value ? new Date(value).toLocaleString() : "-"; }

export function SchedulesPage() {
  const styles = useStyles();
  const [schedules, setSchedules] = useState<JobSchedule[]>([]);
  const [types, setTypes] = useState<string[]>([]);
  const [continuation, setContinuation] = useState<string>();
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<JobSchedule>();
  const [form, setForm] = useState<ScheduleRequest>(initialRequest);
  const [preview, setPreview] = useState<string[]>([]);
  const [historyId, setHistoryId] = useState<string>();
  const [runs, setRuns] = useState<ScheduleRun[]>([]);
  const [runContinuation, setRunContinuation] = useState<string>();
  const createKey = useRef({ payload: "", key: "" });
  const runKeys = useRef(new Map<string, string>());

  const load = useCallback(async () => {
    const [page, catalog] = await Promise.all([api.getSchedules(), api.getJobTypes()]);
    setSchedules(page.items);
    setContinuation(page.continuationToken);
    setTypes(catalog);
    setLoaded(true);
  }, []);

  useEffect(() => {
    let active = true;
    const refresh = () => {
      if (document.visibilityState === "hidden") return;
      void load().catch((e: unknown) => { if (active) setError(String(e)); });
    };
    refresh();
    const timer = window.setInterval(refresh, 15000);
    return () => { active = false; window.clearInterval(timer); };
  }, [load]);

  async function act(work: () => Promise<unknown>) {
    setBusy(true);
    setError("");
    try { await work(); }
    catch (e) { setError(e instanceof Error ? e.message : String(e)); }
    finally { setBusy(false); }
  }
  function change<K extends keyof ScheduleRequest>(key: K, value: ScheduleRequest[K]) {
    setForm((old) => ({ ...old, [key]: value }));
    setPreview([]);
  }
  async function save() {
    if (editing) await api.updateSchedule(editing, form);
    else {
      const payload = JSON.stringify(form);
      if (createKey.current.payload !== payload)
        createKey.current = { payload, key: crypto.randomUUID() };
      await api.createSchedule(form, createKey.current.key);
      createKey.current = { payload: "", key: "" };
    }
    setShowForm(false);
    setEditing(undefined);
    await load();
  }
  async function history(id: string) {
    const page = await api.getScheduleRuns(id);
    setHistoryId(id);
    setRuns(page.items);
    setRunContinuation(page.continuationToken);
  }

  return <div className={styles.page}>
    <div className={styles.row}>
      <Text size={700} weight="semibold">Schedules</Text>
      <Button appearance="primary" disabled={busy} onClick={() => {
        setEditing(undefined); setForm(initialRequest()); setPreview([]); setShowForm(true);
      }}>New schedule</Button>
      <Button disabled={busy} onClick={() => void act(async () => {
        await load();
        if (historyId) await history(historyId);
      })}>Refresh</Button>
    </div>
    <Text>Run a job once or on a daily/weekly schedule. An optional post-job starts only after full primary success.</Text>
    <Text size={200}>Times follow the selected time zone. Checks run every 30 seconds; actual start depends on worker capacity.
      Missed runs catch up once within 24 hours. Occurrences are skipped while the same schedule is active.</Text>
    {error && <Card role="alert" className={styles.error}>{error}</Card>}
    {busy && <Spinner size="tiny" label="Updating schedules" />}

    {showForm && <Card>
      <Text size={500} weight="semibold">{editing ? "Edit schedule" : "New schedule"}</Text>
      <div className={styles.form}>
        <Field label="Name" required><Input value={form.name} maxLength={120} onChange={(_, d) => change("name", d.value)} /></Field>
        <Field label="Primary job"><Select value={form.primary.jobType} onChange={(_, d) =>
          change("primary", { jobType: d.value, skipLlmAnalysis: false })}>
          {types.map((type) => <option key={type} value={type}>{labels[type] || type}</option>)}
        </Select></Field>
        <Field label="Frequency"><Select value={form.frequency} onChange={(_, d) => {
          if (d.value === "once" || d.value === "daily" || d.value === "weekly") change("frequency", d.value);
        }}><option value="once">Once</option><option value="daily">Daily</option><option value="weekly">Weekly</option></Select></Field>
        <Field label={form.frequency === "once" ? "Date" : "Start date"} required>
          <Input type="date" value={form.localDate} onChange={(_, d) => change("localDate", d.value)} />
        </Field>
        <Field label="Local time" required><Input type="time" value={form.localTime} onChange={(_, d) => change("localTime", d.value)} /></Field>
        <Field label="Time zone (IANA)" required><Input value={form.timeZone} placeholder="Europe/Amsterdam"
          onChange={(_, d) => change("timeZone", d.value)} /></Field>
        <Field label="Post-job (only after success)"><Select value={form.postJob?.jobType || ""} onChange={(_, d) =>
          change("postJob", d.value ? { jobType: d.value, skipLlmAnalysis: false } : null)}>
          <option value="">None</option>
          {types.map((type) => <option key={type} value={type}>{labels[type] || type}</option>)}
        </Select></Field>
      </div>
      {form.frequency === "weekly" && <div className={styles.row}>{days.map((day, i) =>
        <Checkbox key={day} label={day} checked={form.weekdays.includes(i)} onChange={(_, d) =>
          change("weekdays", d.checked ? [...form.weekdays, i] : form.weekdays.filter((v) => v !== i))} />)}</div>}
      {form.primary.jobType === "azure-updates" && <Checkbox label="Skip primary LLM analysis (raw backfill)"
        checked={form.primary.skipLlmAnalysis} onChange={(_, d) =>
          change("primary", { ...form.primary, skipLlmAnalysis: !!d.checked })} />}
      {form.postJob?.jobType === "azure-updates" && <Checkbox label="Skip post-job LLM analysis"
        checked={form.postJob.skipLlmAnalysis} onChange={(_, d) =>
          change("postJob", { jobType: "azure-updates", skipLlmAnalysis: !!d.checked })} />}
      {form.primary.skipLlmAnalysis && form.postJob && <Text>
        Raw backfill does not produce new LLM analysis. The post-job reads the current application dataset.
      </Text>}
      <Text size={200}>Daylight saving: nonexistent times move forward to the first valid time; repeated times run once at the earlier offset.
        Editing does not change an already-admitted chain.</Text>
      <div className={styles.row}>
        <Button disabled={busy} onClick={() => void act(async () => setPreview(await api.previewSchedule(form)))}>Preview next runs</Button>
        <Button appearance="primary" disabled={busy} onClick={() => void act(save)}>Save schedule</Button>
        <Button disabled={busy} onClick={() => setShowForm(false)}>Cancel</Button>
      </div>
      {preview.map((date) => <Text key={date} size={200}>
        {new Intl.DateTimeFormat(undefined, { timeZone: form.timeZone, dateStyle: "full", timeStyle: "long" }).format(new Date(date))}
        {" · UTC: "}{new Date(date).toISOString()}
      </Text>)}
    </Card>}

    {!loaded && !error ? <Spinner label="Loading schedules" /> : <Table aria-label="Job schedules">
      <TableHeader><TableRow>{["Name / jobs", "Schedule", "Next due", "State / last result", "Actions"].map((h) =>
        <TableHeaderCell key={h}>{h}</TableHeaderCell>)}</TableRow></TableHeader>
      <TableBody>{schedules.map((schedule) => <TableRow key={schedule.scheduleId}>
        <TableCell><Text weight="semibold">{schedule.configuration.name}</Text><br />
          {labels[schedule.configuration.primary.jobType]}
          {schedule.configuration.postJob && <> → {labels[schedule.configuration.postJob.jobType]}</>}
        </TableCell>
        <TableCell>{schedule.configuration.frequency} at {schedule.configuration.localTime}<br />
          {schedule.configuration.timeZone}
        </TableCell>
        <TableCell>{when(schedule.nextDueAt)}<br />
          <Text size={200}>Shown in your browser time zone</Text>
        </TableCell>
        <TableCell><Badge appearance="tint">{schedule.enabled ? (schedule.nextDueAt ? "Enabled" : "Finished") : "Paused"}</Badge>
          <br />{schedule.lastOutcome || (schedule.activeRunId ? "Admitted" : "No runs")}
          {schedule.lastError && <Text block className={styles.error}>{schedule.lastError}</Text>}
          <Text block size={200}>Skipped: {schedule.skippedOccurrences} · Missed: {schedule.missedOccurrences}</Text>
        </TableCell>
        <TableCell><div className={styles.row}>
          <Button size="small" disabled={busy} onClick={() => void act(() => history(schedule.scheduleId))}>History</Button>
          <Button size="small" disabled={busy} onClick={() => {
            setEditing(schedule); setForm(schedule.configuration); setPreview([]); setShowForm(true);
          }}>Edit</Button>
          <Button size="small" disabled={busy} onClick={() => void act(async () => {
            await api.setScheduleEnabled(schedule, !schedule.enabled); await load();
          })}>{schedule.enabled ? "Pause" : "Resume"}</Button>
          <Button size="small" disabled={busy || !!schedule.activeRunId} onClick={() => void act(async () => {
            const key = runKeys.current.get(schedule.scheduleId) || crypto.randomUUID();
            runKeys.current.set(schedule.scheduleId, key);
            await api.runScheduleNow(schedule, key);
            runKeys.current.delete(schedule.scheduleId);
            await load(); await history(schedule.scheduleId);
          })}>Run now</Button>
          <Button size="small" disabled={busy} onClick={() => {
            if (window.confirm("Delete this schedule? Future runs stop; any admitted primary and post-job will still finish."))
              void act(async () => { await api.deleteSchedule(schedule); await load(); });
          }}>Delete</Button>
        </div></TableCell>
      </TableRow>)}</TableBody>
    </Table>}
    {loaded && schedules.length === 0 && <Text>No schedules yet.</Text>}
    {continuation && <Button disabled={busy} onClick={() => void act(async () => {
      const page = await api.getSchedules(continuation);
      setSchedules((old) => [...old, ...page.items]); setContinuation(page.continuationToken);
    })}>Load more schedules</Button>}

    {historyId && <Card className={styles.history}>
      <div className={styles.row}><Text size={500} weight="semibold">Run history</Text>
        <Button disabled={busy} onClick={() => void act(() => history(historyId))}>Refresh history</Button>
        <Button onClick={() => setHistoryId(undefined)}>Close</Button></div>
      {runs.length === 0 && <Text>No runs yet.</Text>}
      {runs.map((run) => <Card key={run.id}>
        <Text weight="semibold">{when(run.scheduledFor)} · {run.status}</Text>
        <Text size={200}>Revision {run.revision} · admitted {when(run.createdAt)} · finished {when(run.completedAt)}</Text>
        <Link to={`/crawl-jobs?jobId=${run.primaryJobId}`}>Primary: {labels[run.configuration.primary.jobType]}</Link>
        {run.postJobId && <Link to={`/crawl-jobs?jobId=${run.postJobId}`}>Post-job: {labels[run.configuration.postJob?.jobType || ""]}</Link>}
        {run.error && <Text className={styles.error}>{run.error}</Text>}
        {run.status === "failed" && <Button disabled={busy} onClick={() => void act(async () => {
          const keyName = `retry-${run.id}`;
          const key = runKeys.current.get(keyName) || crypto.randomUUID();
          runKeys.current.set(keyName, key);
          await api.retryScheduleRun(run.scheduleId, run.id, key);
          runKeys.current.delete(keyName);
          await load(); await history(run.scheduleId);
        })}>Retry failed step</Button>}
      </Card>)}
      {runContinuation && <Button disabled={busy} onClick={() => void act(async () => {
        const page = await api.getScheduleRuns(historyId, runContinuation);
        setRuns((old) => [...old, ...page.items]); setRunContinuation(page.continuationToken);
      })}>Load older runs</Button>}
    </Card>}
  </div>;
}
