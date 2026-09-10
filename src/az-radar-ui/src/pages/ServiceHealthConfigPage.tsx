import {
  Badge,
  Button,
  Card,
  Checkbox,
  Dropdown,
  Field,
  Input,
  Option,
  Spinner,
  Switch,
  Text,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import {
  AddRegular,
  ArrowSyncRegular,
  DeleteRegular,
  SendRegular,
} from "@fluentui/react-icons";
import { useEffect, useState } from "react";
import {
  api,
  type ServiceHealthChannel,
  type ServiceHealthDeliveryIntent,
  type ServiceHealthEvent,
  type ServiceHealthEventType,
  type ServiceHealthSubscription,
} from "../api/client";

const eventTypes: Array<{ value: ServiceHealthEventType; label: string }> = [
  { value: "ServiceIssue", label: "Service issues" },
  { value: "PlannedMaintenance", label: "Planned maintenance" },
  { value: "HealthAdvisory", label: "Health advisories" },
  { value: "SecurityAdvisory", label: "Security advisories" },
];

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    gap: "20px",
    padding: "24px",
    maxWidth: "1050px",
  },
  header: { display: "flex", flexDirection: "column", gap: "4px" },
  card: { display: "flex", flexDirection: "column", gap: "16px", padding: "24px" },
  row: { display: "flex", gap: "12px", flexWrap: "wrap", alignItems: "end" },
  actions: { display: "flex", gap: "8px", alignItems: "center", flexWrap: "wrap" },
  item: {
    display: "flex",
    flexDirection: "column",
    gap: "8px",
    padding: "14px 16px",
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
  },
  itemTop: {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "center",
    gap: "12px",
  },
  eventTypes: { display: "flex", gap: "12px", flexWrap: "wrap" },
  muted: { color: tokens.colorNeutralForeground3 },
  error: { color: tokens.colorPaletteRedForeground1 },
  success: { color: tokens.colorPaletteGreenForeground1 },
  mono: { fontFamily: "Consolas, 'Courier New', monospace" },
});

export function ServiceHealthConfigPage() {
  const styles = useStyles();
  const [subscriptions, setSubscriptions] = useState<ServiceHealthSubscription[]>([]);
  const [channels, setChannels] = useState<ServiceHealthChannel[]>([]);
  const [events, setEvents] = useState<ServiceHealthEvent[]>([]);
  const [deliveryIntents, setDeliveryIntents] = useState<ServiceHealthDeliveryIntent[]>([]);
  const [subscriptionId, setSubscriptionId] = useState("");
  const [testEventType, setTestEventType] = useState<ServiceHealthEventType>("ServiceIssue");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ type: "success" | "error"; text: string } | null>(null);

  const load = async () => {
    const [registeredSubscriptions, notificationChannels, recentEvents, recentIntents] = await Promise.all([
      api.getServiceHealthSubscriptions(),
      api.getServiceHealthChannels(),
      api.getServiceHealthEvents(20),
      api.getServiceHealthDeliveryIntents(20),
    ]);
    setSubscriptions(registeredSubscriptions);
    setChannels(notificationChannels);
    setEvents(recentEvents);
    setDeliveryIntents(recentIntents);
  };

  useEffect(() => {
    load()
      .catch((error) => setMessage({ type: "error", text: String(error) }))
      .finally(() => setLoading(false));
  }, []);

  const registerSubscription = async () => {
    setBusy(true);
    setMessage(null);
    try {
      await api.registerServiceHealthSubscription(subscriptionId.trim());
      setSubscriptionId("");
      await load();
      setMessage({ type: "success", text: "Subscription registered and diagnostic setting verified." });
    } catch (error) {
      await load().catch(() => {});
      setMessage({ type: "error", text: error instanceof Error ? error.message : String(error) });
    } finally {
      setBusy(false);
    }
  };

  const verifySubscription = async (id: string) => {
    setBusy(true);
    setMessage(null);
    try {
      await api.verifyServiceHealthSubscription(id);
      await load();
      setMessage({ type: "success", text: "Subscription configuration verified." });
    } catch (error) {
      await load().catch(() => {});
      setMessage({ type: "error", text: error instanceof Error ? error.message : String(error) });
    } finally {
      setBusy(false);
    }
  };

  const removeSubscription = async (id: string) => {
    setBusy(true);
    try {
      await api.removeServiceHealthSubscription(id, false);
      await load();
    } finally {
      setBusy(false);
    }
  };

  const publishTestEvent = async (id: string) => {
    setBusy(true);
    setMessage(null);
    try {
      const result = await api.publishServiceHealthTestEvent(id, testEventType);
      setMessage({
        type: "success",
        text: `Synthetic ${result.eventType} event ${result.trackingId} was published. Refresh recent events in a few seconds.`,
      });
    } catch (error) {
      setMessage({ type: "error", text: error instanceof Error ? error.message : String(error) });
    } finally {
      setBusy(false);
    }
  };

  if (loading) {
    return <div className={styles.container}><Spinner label="Loading Service Health configuration..." /></div>;
  }

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <Text size={700} weight="bold" block>Service Health Dispatch</Text>
        <Text size={200} className={styles.muted}>
          Register pilot subscriptions and configure central platform Teams channels by Service Health event type.
        </Text>
      </div>

      {message && (
        <Text className={message.type === "success" ? styles.success : styles.error}>{message.text}</Text>
      )}

      <Card className={styles.card}>
        <Text weight="semibold">Register a subscription</Text>
        <Text size={200} className={styles.muted}>
          The dedicated provisioning identity must already have the approved diagnostic-setting role
          on this subscription. AzRadar will configure only the ServiceHealth Activity Log category.
        </Text>
        <div className={styles.row}>
          <Field label="Subscription ID" style={{ flex: 1, minWidth: "360px" }} required>
            <Input
              className={styles.mono}
              value={subscriptionId}
              onChange={(_, data) => setSubscriptionId(data.value)}
              placeholder="00000000-0000-0000-0000-000000000000"
            />
          </Field>
          <Button
            appearance="primary"
            icon={busy ? <Spinner size="tiny" /> : <AddRegular />}
            disabled={busy || !subscriptionId.trim()}
            onClick={registerSubscription}
          >
            Register
          </Button>
        </div>
        {subscriptions.map((subscription) => (
          <div className={styles.item} key={subscription.id}>
            <div className={styles.itemTop}>
              <div>
                <Text weight="semibold">{subscription.displayName || subscription.id}</Text>
                <Text block size={200} className={styles.mono}>{subscription.id}</Text>
              </div>
              <Badge
                appearance="outline"
                color={subscription.status === "active"
                  ? "success"
                  : subscription.status === "permission-required"
                    ? "warning"
                    : "danger"}
              >
                {subscription.status}
              </Badge>
            </div>
            <Text size={200} className={styles.muted}>
              Diagnostic setting: {subscription.diagnosticSettingName || "not configured"} ·
              Event Hub: {subscription.eventHubName || "not configured"}
            </Text>
            {subscription.lastErrorMessage && (
              <Text size={200} className={styles.error}>{subscription.lastErrorMessage}</Text>
            )}
            <div className={styles.actions}>
              <Dropdown
                size="small"
                value={eventTypes.find((item) => item.value === testEventType)?.label}
                selectedOptions={[testEventType]}
                onOptionSelect={(_, data) => {
                  if (data.optionValue)
                    setTestEventType(data.optionValue as ServiceHealthEventType);
                }}
                aria-label="Synthetic event type"
              >
                {eventTypes.map((eventType) => (
                  <Option key={eventType.value} value={eventType.value}>
                    {eventType.label}
                  </Option>
                ))}
              </Dropdown>
              <Button
                size="small"
                appearance="secondary"
                icon={<SendRegular />}
                disabled={busy || subscription.status !== "active"}
                onClick={() => publishTestEvent(subscription.id)}
              >
                Send test event
              </Button>
              <Button
                size="small"
                appearance="secondary"
                icon={<ArrowSyncRegular />}
                disabled={busy}
                onClick={() => verifySubscription(subscription.id)}
              >
                Verify
              </Button>
              <Button
                size="small"
                appearance="subtle"
                icon={<DeleteRegular />}
                disabled={busy}
                onClick={() => removeSubscription(subscription.id)}
              >
                Remove from AzRadar
              </Button>
            </div>
          </div>
        ))}
      </Card>

      <Card className={styles.card}>
        <div className={styles.itemTop}>
          <div>
            <Text weight="semibold">Recent ingested events</Text>
            <Text block size={200} className={styles.muted}>
              Events have been normalized, deduplicated, enriched, and evaluated for configured routes.
            </Text>
          </div>
          <Button appearance="secondary" icon={<ArrowSyncRegular />} onClick={load}>
            Refresh
          </Button>
        </div>
        {events.length === 0 && <Text className={styles.muted}>No events have been ingested yet.</Text>}
        {events.map((event) => (
          <div className={styles.item} key={event.id}>
            <div className={styles.itemTop}>
              <div>
                <Text weight="semibold">{event.llmAnalysis?.suggestedTitle || event.title}</Text>
                <Text block size={200} className={styles.muted}>
                  {event.eventType} · {event.service || "Unknown service"} · {event.region || "Global"}
                </Text>
              </div>
              <div className={styles.actions}>
                {event.isSynthetic && <Badge appearance="outline" color="informative">Synthetic</Badge>}
                <Badge
                  appearance="outline"
                  color={event.routingStatus === "ready-for-dispatch" ? "success" : "subtle"}
                >
                  {event.routingStatus}
                </Badge>
              </div>
            </div>
            <Text size={200}>{event.llmAnalysis?.briefSummary || event.summary}</Text>
            <Text size={200} className={styles.muted}>
              Tracking ID: {event.trackingId} · Received: {new Date(event.receivedAt).toLocaleString()}
            </Text>
          </div>
        ))}
      </Card>

      <Card className={styles.card}>
        <Text weight="semibold">Pending delivery intents</Text>
        <Text size={200} className={styles.muted}>
          Delivery state from the durable Service Bus and Teams bot dispatcher.
        </Text>
        {deliveryIntents.length === 0 && (
          <Text className={styles.muted}>No channel-matched delivery intents yet.</Text>
        )}
        {deliveryIntents.map((intent) => (
          <div className={styles.item} key={intent.id}>
            <div className={styles.itemTop}>
              <Text weight="semibold">{intent.channelDisplayName}</Text>
              <Badge appearance="outline" color="warning">{intent.status}</Badge>
            </div>
            <Text size={200} className={styles.muted}>
              {intent.eventType} · Event {intent.eventId}
            </Text>
          </div>
        ))}
      </Card>

      <Card className={styles.card}>
        <Text weight="semibold">Platform Teams destinations</Text>
        <Text size={200} className={styles.muted}>
          Install the AzRadar Teams app in a standard channel. The bot discovers the channel here
          as disabled; an administrator then selects event families and enables delivery.
        </Text>
        {channels.length === 0 && (
          <Text className={styles.muted}>No Teams destinations have been discovered yet.</Text>
        )}

        {channels.map((channel) => (
          <div className={styles.item} key={channel.id}>
            <div className={styles.itemTop}>
              <div>
                <Text weight="semibold">{channel.displayName}</Text>
                <Text block size={200} className={styles.muted}>
                  {channel.type === "teams-bot"
                    ? `${channel.registrationStatus} · ${channel.teamName || "Unknown team"}`
                    : "Legacy Teams Workflow"}
                </Text>
              </div>
              <div className={styles.actions}>
                <Switch
                  checked={channel.enabled}
                  label="Enabled"
                  disabled={busy || (channel.type === "teams-bot" && channel.registrationStatus !== "registered")}
                  onChange={async (_, data) => {
                    await api.updateServiceHealthChannel({ ...channel, enabled: data.checked });
                    await load();
                  }}
                />
                {channel.type === "teams-workflow" && (
                  <Button
                    appearance="subtle"
                    icon={<DeleteRegular />}
                    onClick={async () => {
                      await api.removeServiceHealthChannel(channel.id);
                      await load();
                    }}
                    aria-label={`Delete ${channel.displayName}`}
                  />
                )}
              </div>
            </div>
            <div className={styles.eventTypes}>
              {eventTypes.map((eventType) => (
                <Checkbox
                  key={eventType.value}
                  label={eventType.label}
                  checked={channel.subscribedEventTypes.includes(eventType.value)}
                  disabled={busy}
                  onChange={async (_, data) => {
                    const subscribedEventTypes = data.checked === true
                      ? [...new Set([...channel.subscribedEventTypes, eventType.value])]
                      : channel.subscribedEventTypes.filter((value) => value !== eventType.value);
                    if (subscribedEventTypes.length === 0) return;
                    await api.updateServiceHealthChannel({ ...channel, subscribedEventTypes });
                    await load();
                  }}
                />
              ))}
            </div>
          </div>
        ))}
      </Card>
    </div>
  );
}
