import { useEffect, useState } from "react";
import {
  Button, Card, Field, Input, Select, Spinner, Text, Textarea, makeStyles, tokens,
} from "@fluentui/react-components";
import { api } from "../api/client";
import type { AiSettings, AiTestResult } from "../api/client";

const useStyles = makeStyles({
  container: { display: "flex", flexDirection: "column", gap: "20px", padding: "24px", maxWidth: "850px" },
  card: { display: "flex", flexDirection: "column", gap: "16px", padding: "24px" },
  actions: { display: "flex", gap: "12px", alignItems: "center" },
  muted: { color: tokens.colorNeutralForeground3 },
  error: { color: tokens.colorPaletteRedForeground1 },
  reply: { whiteSpace: "pre-wrap", overflowWrap: "anywhere", margin: 0 },
});

export function AiSettingsPage() {
  const styles = useStyles();
  const [settings, setSettings] = useState<AiSettings | null>(null);
  const [provider, setProvider] = useState<AiSettings["provider"]>("foundry");
  const [model, setModel] = useState("gpt-5.4");
  const [pat, setPat] = useState("");
  const [prompt, setPrompt] = useState("Reply with the single word OK.");
  const [result, setResult] = useState<AiTestResult | null>(null);
  const [busy, setBusy] = useState<"loading" | "testing" | "saving" | null>("loading");
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");

  const load = async () => {
    setBusy("loading");
    setError("");
    try {
      const current = await api.getAiSettings();
      setSettings(current);
      setProvider(current.provider);
      setModel(current.copilotModel);
      setPat("");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not load AI settings.");
    } finally { setBusy(null); }
  };

  useEffect(() => {
    let active = true;
    api.getAiSettings().then((current) => {
      if (!active) return;
      setSettings(current);
      setProvider(current.provider);
      setModel(current.copilotModel);
    }).catch((err: unknown) => {
      if (active) setError(err instanceof Error ? err.message : "Could not load AI settings.");
    }).finally(() => { if (active) setBusy(null); });
    return () => { active = false; };
  }, []);

  const clearResult = () => { setResult(null); setMessage(""); setError(""); };
  const run = async (save: boolean) => {
    if (!settings) return;
    setBusy(save ? "saving" : "testing");
    setError("");
    setMessage("");
    try {
      const request = {
        provider, copilotModel: model, pat: provider === "github-copilot" && pat ? pat : undefined,
        prompt, eTag: settings.eTag,
      };
      if (save) {
        const saved = await api.saveAiSettings(request);
        setSettings(saved);
        setPat("");
        setResult(null);
        setMessage("AI provider verified and saved. Future jobs use this profile; running chains keep their existing profile.");
      } else {
        setResult(await api.testAiSettings(request));
        setMessage("Inference succeeded. Testing does not change the active provider or save a newly entered PAT.");
      }
    } catch (err) {
      setResult(null);
      setError(err instanceof Error ? err.message : "AI operation failed.");
    } finally { setBusy(null); }
  };

  return (
    <div className={styles.container}>
      <Text size={700} weight="bold">AI settings</Text>
      <Text className={styles.muted}>Configure and test intelligence for crawling jobs and Service Health.</Text>
      {busy === "loading" && <Spinner label="Loading AI settings..." />}
      {error && <Text role="alert" className={styles.error}>{error}</Text>}
      {!settings && !busy && <Button onClick={() => void load()}>Retry</Button>}
      {settings && (
        <Card className={styles.card}>
          <Text size={200}>Active provider: {settings.provider === "foundry" ? "Foundry Endpoint" : "GitHub Copilot"}</Text>
          <Field label="AI provider">
            <Select value={provider} disabled={!!busy} onChange={(_, data) => {
              setProvider(data.value === "github-copilot" ? "github-copilot" : "foundry");
              setPat("");
              clearResult();
            }}>
              <option value="foundry">Foundry Endpoint</option>
              <option value="github-copilot">GitHub Copilot</option>
            </Select>
          </Field>
          {provider === "foundry" ? (
            <Text className={styles.muted}>
              Endpoint: {settings.endpoint || "Not configured"}<br />
              Deployment: {settings.deployment}<br />
              Uses the existing managed identity. No API key is required.
            </Text>
          ) : (
            <>
              <Field label="Copilot model ID" hint="An explicit model allowed by your Copilot subscription and organization policies.">
                <Input value={model} disabled={!!busy} maxLength={100} onChange={(_, data) => {
                  setModel(data.value); clearResult();
                }} />
              </Field>
              <Field label="Fine-grained GitHub PAT"
                hint={settings.patConfigured
                  ? "Token configured. Leave blank to use it, or enter a replacement. Saved tokens cannot be retrieved."
                  : "Create a user-owned fine-grained PAT with the Copilot Requests account permission. Classic PATs are not supported."}>
                <Input type="password" autoComplete="new-password" value={pat} disabled={!!busy}
                  maxLength={512} placeholder={settings.patConfigured ? "Token configured (write-only)" : "github_pat_..."}
                  onChange={(_, data) => { setPat(data.value); clearResult(); }} />
              </Field>
              <Text size={200} className={styles.muted}>
                Saved securely in Key Vault only when you apply. Scheduled jobs use the token owner's
                Copilot access and budget. Replace expired/revoked tokens manually; no Foundry fallback.
              </Text>
            </>
          )}
          <Field label="Test prompt" hint="A real inference request may consume tokens or Copilot credits.">
            <Textarea value={prompt} disabled={!!busy} maxLength={4000} resize="vertical"
              onChange={(_, data) => { setPrompt(data.value); setResult(null); }} />
          </Field>
          <div className={styles.actions}>
            <Button appearance="secondary" disabled={!!busy || !prompt.trim()}
              onClick={() => void run(false)}>Test</Button>
            <Button appearance="primary" disabled={!!busy || (provider === "github-copilot" &&
              (!model.trim() || (!pat && !settings.patConfigured)))}
              onClick={() => void run(true)}>Save / Apply</Button>
            {busy && <Spinner size="tiny" label={busy === "saving" ? "Verifying and saving..." : "Testing..."} />}
          </div>
          {message && <Text role="status">{message}</Text>}
          {result && (
            <Card>
              <Text weight="semibold">Inference result - {result.model} ({result.latencyMs} ms)</Text>
              <pre className={styles.reply}>{result.reply}</pre>
            </Card>
          )}
          <Text size={200} className={styles.muted}>
            Restrict dashboard/API access through network controls before configuring customer credentials.
            Write-only credentials do not prevent unauthorized changes when management is publicly accessible.
          </Text>
        </Card>
      )}
    </div>
  );
}
