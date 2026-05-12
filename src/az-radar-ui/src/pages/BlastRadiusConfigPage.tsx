import {
  makeStyles,
  tokens,
  Card,
  Text,
  Button,
  Input,
  Spinner,
  Field,
} from "@fluentui/react-components";
import { SaveRegular } from "@fluentui/react-icons";
import { useEffect, useState } from "react";
import { api } from "../api/client";

const CONFIG_KEY = "blast-radius-uami-client-id";

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    gap: "20px",
    padding: "24px",
    maxWidth: "720px",
  },
  header: {
    display: "flex",
    flexDirection: "column",
    gap: "4px",
  },
  card: {
    display: "flex",
    flexDirection: "column",
    gap: "20px",
    padding: "24px",
  },
  description: {
    color: tokens.colorNeutralForeground3,
    lineHeight: "20px",
  },
  actions: {
    display: "flex",
    alignItems: "center",
    gap: "12px",
  },
  successText: {
    color: tokens.colorPaletteGreenForeground1,
  },
  errorText: {
    color: tokens.colorPaletteRedForeground1,
  },
});

export function BlastRadiusConfigPage() {
  const styles = useStyles();
  const [clientId, setClientId] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<{ type: "success" | "error"; text: string } | null>(null);

  useEffect(() => {
    api
      .getConfig(CONFIG_KEY)
      .then((config) => {
        if (config?.value) setClientId(config.value);
      })
      .catch(console.error)
      .finally(() => setLoading(false));
  }, []);

  const handleSave = async () => {
    setSaving(true);
    setMessage(null);
    try {
      await api.setConfig(CONFIG_KEY, clientId, "UAMI for ARG access");
      setMessage({ type: "success", text: "Configuration saved successfully." });
    } catch (err) {
      setMessage({
        type: "error",
        text: err instanceof Error ? err.message : "Failed to save configuration.",
      });
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <div className={styles.container}>
        <Spinner label="Loading configuration..." />
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <Text size={700} weight="bold" block>
          Blast Radius Configuration
        </Text>
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
          Configure Azure Resource Graph access for impact analysis
        </Text>
      </div>

      <Card className={styles.card}>
        <Field
          label="UAMI Client ID for Resource Graph"
          hint="This User-Assigned Managed Identity must have Reader access across your subscriptions and be assigned to the JobHost App Service."
        >
          <Input
            value={clientId}
            onChange={(_, d) => {
              setClientId(d.value);
              setMessage(null);
            }}
            placeholder="e.g. c6fec013-d3e4-497c-9c49-3bf14fa305ce"
            style={{ fontFamily: "Consolas, 'Courier New', monospace" }}
          />
        </Field>

        <div className={styles.actions}>
          <Button
            appearance="primary"
            icon={saving ? <Spinner size="tiny" /> : <SaveRegular />}
            disabled={saving || !clientId.trim()}
            onClick={handleSave}
          >
            Save
          </Button>
          {message && (
            <Text
              size={200}
              className={message.type === "success" ? styles.successText : styles.errorText}
            >
              {message.text}
            </Text>
          )}
        </div>
      </Card>
    </div>
  );
}
