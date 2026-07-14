import {
  makeStyles,
  tokens,
  Card,
  Text,
  Button,
  Input,
  Spinner,
  Field,
  Switch,
  Badge,
  Link,
} from "@fluentui/react-components";
import {
  SaveRegular,
  AddRegular,
  DeleteRegular,
  PlayRegular,
  BranchRegular,
} from "@fluentui/react-icons";
import { useEffect, useState } from "react";
import { api, type RepoWatchItem } from "../api/client";

const PAT_KEY = "github-pat";

function defaultCutoffLocal(): string {
  const d = new Date();
  d.setDate(d.getDate() - 30);
  // to YYYY-MM-DDTHH:mm in local time
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    gap: "20px",
    padding: "24px",
    maxWidth: "1000px",
  },
  header: { display: "flex", flexDirection: "column", gap: "4px" },
  card: { display: "flex", flexDirection: "column", gap: "16px", padding: "24px" },
  formRow: { display: "flex", gap: "12px", flexWrap: "wrap" },
  actions: { display: "flex", alignItems: "center", gap: "12px", flexWrap: "wrap" },
  successText: { color: tokens.colorPaletteGreenForeground1 },
  errorText: { color: tokens.colorPaletteRedForeground1 },
  dateInput: {
    fontFamily: "inherit",
    fontSize: "14px",
    padding: "5px 8px",
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke1}`,
    background: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  repoRow: {
    display: "flex",
    flexDirection: "column",
    gap: "6px",
    padding: "14px 16px",
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  repoTop: { display: "flex", alignItems: "center", gap: "10px", justifyContent: "space-between" },
  repoMeta: { display: "flex", gap: "16px", flexWrap: "wrap", color: tokens.colorNeutralForeground3, fontSize: "12px" },
  muted: { color: tokens.colorNeutralForeground3 },
});

export function RepositoryWatchlistPage() {
  const styles = useStyles();
  const [repos, setRepos] = useState<RepoWatchItem[]>([]);
  const [loading, setLoading] = useState(true);

  // PAT config
  const [patMasked, setPatMasked] = useState("");
  const [patInput, setPatInput] = useState("");
  const [savingPat, setSavingPat] = useState(false);

  // Add form
  const [repoUrl, setRepoUrl] = useState("");
  const [paths, setPaths] = useState("");
  const [branch, setBranch] = useState("");
  const [label, setLabel] = useState("");
  const [cutoff, setCutoff] = useState(defaultCutoffLocal());
  const [adding, setAdding] = useState(false);

  const [message, setMessage] = useState<{ type: "success" | "error"; text: string } | null>(null);
  const [scanning, setScanning] = useState(false);

  const loadRepos = () =>
    api.getRepoWatchlist().then(setRepos).catch((e) => setMessage({ type: "error", text: String(e) }));

  useEffect(() => {
    Promise.all([
      loadRepos(),
      api.getConfig(PAT_KEY).then((c) => c?.value && setPatMasked(c.value)).catch(() => {}),
    ]).finally(() => setLoading(false));
  }, []);

  const handleSavePat = async () => {
    setSavingPat(true);
    setMessage(null);
    try {
      await api.setConfig(PAT_KEY, patInput.trim(), "Read-only GitHub PAT for Change Radar");
      setPatInput("");
      const c = await api.getConfig(PAT_KEY);
      if (c?.value) setPatMasked(c.value);
      setMessage({ type: "success", text: "GitHub PAT saved." });
    } catch (err) {
      setMessage({ type: "error", text: err instanceof Error ? err.message : "Failed to save PAT." });
    } finally {
      setSavingPat(false);
    }
  };

  const handleAdd = async () => {
    setAdding(true);
    setMessage(null);
    try {
      await api.addRepoWatch({
        repoUrl: repoUrl.trim(),
        branch: branch.trim() || undefined,
        pathFilters: paths.split(",").map((p) => p.trim()).filter(Boolean),
        label: label.trim() || undefined,
        cutoffDate: cutoff ? new Date(cutoff).toISOString() : undefined,
      });
      setRepoUrl(""); setPaths(""); setBranch(""); setLabel(""); setCutoff(defaultCutoffLocal());
      await loadRepos();
      setMessage({ type: "success", text: "Repository added to the watchlist." });
    } catch (err) {
      setMessage({ type: "error", text: err instanceof Error ? err.message : "Failed to add repository." });
    } finally {
      setAdding(false);
    }
  };

  const handleDelete = async (id: string) => {
    await api.removeRepoWatch(id);
    await loadRepos();
  };

  const handleToggle = async (item: RepoWatchItem, enabled: boolean) => {
    await api.patchRepoWatch(item.id, { enabled });
    await loadRepos();
  };

  const handleScanNow = async () => {
    setScanning(true);
    setMessage(null);
    try {
      await api.createCrawlJob("github-crawl");
      setMessage({ type: "success", text: "GitHub Change Radar job queued. Track it on the Crawling Jobs page." });
    } catch (err) {
      setMessage({ type: "error", text: err instanceof Error ? err.message : "Failed to queue job." });
    } finally {
      setScanning(false);
    }
  };

  if (loading) {
    return (
      <div className={styles.container}>
        <Spinner label="Loading repository watchlist..." />
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <Text size={700} weight="bold" block>Repository Watchlist</Text>
        <Text size={200} className={styles.muted}>
          Watch GitHub documentation/source repos. The GitHub Change Radar job analyzes new commits
          since each repo's cutoff and flags changes a platform team must know about.
        </Text>
      </div>

      {message && (
        <Text size={300} className={message.type === "success" ? styles.successText : styles.errorText}>
          {message.text}
        </Text>
      )}

      {/* PAT config */}
      <Card className={styles.card}>
        <Text weight="semibold">GitHub Access Token (optional, read-only)</Text>
        <Text size={200} className={styles.muted}>
          Without a token, GitHub allows 60 requests/hour. A fine-grained read-only PAT (public repos)
          raises this to 5,000/hour. Stored write-only — it is never shown again after saving.
          {patMasked && <> Currently configured: <b>{patMasked}</b>.</>}
        </Text>
        <div className={styles.formRow}>
          <Field style={{ flex: 1, minWidth: "320px" }}>
            <Input
              type="password"
              value={patInput}
              onChange={(_, d) => setPatInput(d.value)}
              placeholder={patMasked ? "Enter a new token to replace the existing one" : "github_pat_..."}
              style={{ fontFamily: "Consolas, monospace" }}
            />
          </Field>
          <Button
            appearance="primary"
            icon={savingPat ? <Spinner size="tiny" /> : <SaveRegular />}
            disabled={savingPat || !patInput.trim()}
            onClick={handleSavePat}
          >
            Save token
          </Button>
        </div>
      </Card>

      {/* Add repo */}
      <Card className={styles.card}>
        <Text weight="semibold">Add a repository</Text>
        <div className={styles.formRow}>
          <Field label="Repository URL" style={{ flex: 2, minWidth: "320px" }} required>
            <Input
              value={repoUrl}
              onChange={(_, d) => setRepoUrl(d.value)}
              placeholder="https://github.com/MicrosoftDocs/azure-aks-docs"
            />
          </Field>
          <Field label="Label" style={{ flex: 1, minWidth: "160px" }}>
            <Input value={label} onChange={(_, d) => setLabel(d.value)} placeholder="e.g. AKS docs" />
          </Field>
        </div>
        <div className={styles.formRow}>
          <Field label="Path filters (comma-separated)" style={{ flex: 2, minWidth: "320px" }}
            hint="Sub-paths to watch, e.g. articles/aks. Leave empty to watch the whole repo.">
            <Input value={paths} onChange={(_, d) => setPaths(d.value)} placeholder="articles/aks" />
          </Field>
          <Field label="Branch" style={{ flex: 1, minWidth: "160px" }} hint="Empty = default branch">
            <Input value={branch} onChange={(_, d) => setBranch(d.value)} placeholder="main" />
          </Field>
        </div>
        <div className={styles.formRow}>
          <Field label="Cutoff date/time" hint="Commits before this are ignored on the first scan.">
            <input
              type="datetime-local"
              className={styles.dateInput}
              value={cutoff}
              onChange={(e) => setCutoff(e.target.value)}
            />
          </Field>
        </div>
        <div className={styles.actions}>
          <Button
            appearance="primary"
            icon={adding ? <Spinner size="tiny" /> : <AddRegular />}
            disabled={adding || !repoUrl.trim()}
            onClick={handleAdd}
          >
            Add repository
          </Button>
          <Button
            appearance="secondary"
            icon={scanning ? <Spinner size="tiny" /> : <PlayRegular />}
            disabled={scanning || repos.length === 0}
            onClick={handleScanNow}
          >
            Scan now
          </Button>
        </div>
      </Card>

      {/* Repo list */}
      <Card className={styles.card}>
        <Text weight="semibold">Watched repositories ({repos.length})</Text>
        {repos.length === 0 && <Text size={200} className={styles.muted}>No repositories yet.</Text>}
        {repos.map((r) => (
          <div key={r.id} className={styles.repoRow}>
            <div className={styles.repoTop}>
              <div style={{ display: "flex", alignItems: "center", gap: "10px" }}>
                <Text weight="semibold">{r.label}</Text>
                <Link href={r.repoUrl} target="_blank" rel="noreferrer">{r.owner}/{r.repo}</Link>
                {r.lastScanStatus && (
                  <Badge
                    appearance="outline"
                    color={r.lastScanStatus === "ok" ? "success" : r.lastScanStatus === "error" ? "danger" : "warning"}
                  >
                    {r.lastScanStatus}
                  </Badge>
                )}
              </div>
              <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
                <Switch checked={r.enabled} onChange={(_, d) => handleToggle(r, d.checked)} label="Enabled" />
                <Button appearance="subtle" icon={<DeleteRegular />} onClick={() => handleDelete(r.id)} aria-label="Delete" />
              </div>
            </div>
            <div className={styles.repoMeta}>
              <span><BranchRegular fontSize={12} /> {r.branch || "default"}</span>
              <span>paths: {r.pathFilters.length ? r.pathFilters.join(", ") : "(whole repo)"}</span>
              <span>cutoff: {new Date(r.cutoffDate).toLocaleString()}</span>
              <span>last scan: {r.lastScanAt ? new Date(r.lastScanAt).toLocaleString() : "never"}</span>
              {r.lastScannedCommitDate && <span>cursor: {new Date(r.lastScannedCommitDate).toLocaleString()}</span>}
            </div>
            {r.lastScanError && <Text size={200} className={styles.errorText}>{r.lastScanError}</Text>}
          </div>
        ))}
      </Card>
    </div>
  );
}
