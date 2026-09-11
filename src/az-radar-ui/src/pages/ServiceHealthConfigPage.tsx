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
  Tab,
  TabList,
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

type ServiceHealthTab = "subscriptions" | "events" | "intents";

const compactId = (value: string) =>
  value.length <= 32 ? value : `${value.slice(0, 14)}…${value.slice(-14)}`;

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
  const [selectedTab, setSelectedTab] = useState<ServiceHealthTab>("subscriptions");
  const [subscriptions, setSubscriptions] = useState<ServiceHealthSubscription[]>([]);
  const [channels, setChannels] = useState<ServiceHealthChannel[]>([]);
  const [events, setEvents] = useState<ServiceHealthEvent[]>([]);
  const [deliveryIntents, setDeliveryIntents] = useState<ServiceHealthDeliveryIntent[]>([]);
  const [subscriptionId, setSubscriptionId] = useState("");
  const [testEventType, setTestEventType] = useState<ServiceHealthEventType>("ServiceIssue");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ type: "success" | "error"; text: string } | null>(null);
  const visibleDeliveryIntents = deliveryIntents.filter(
    (intent) => intent.status !== "delivered" && intent.status !== "cancelled",
  );

  const load = async () => {
    const [registeredSubscriptions, notificationChannels, recentEvents, recentIntents] = await Promise.all([
      api.getServiceHealthSubscriptions(),
      api.getServiceHealthChannels(),
      api.getServiceHealthEvents(50),
      api.getServiceHealthDeliveryIntents(100),
    ]);
    setSubscriptions(registeredSubscriptions);
    setChannels(notificationChannels.filter((channel) => channel.type === "teams-bot"));
    setEvents(recentEvents);
    setDeliveryIntents(recentIntents);
  };

  useEffect(() => {
    load()
      .catch((error) => setMessage({ type: "error", text: String(error) }))
      .finally(() => setLoading(false));
  }, []);

  const runAction = async (action: () => Promise<void>, successMessage?: string) => {
    setBusy(true);
    setMessage(null);
    try {
      await action();
      await load();
      if (successMessage) setMessage({ type: "success", text: successMessage });
    } catch (error) {
      setMessage({ type: "error", text: error instanceof Error ? error.message : String(error) });
    } finally {
      setBusy(false);
    }
  };

  const registerSubscription = () => runAction(async () => {
    await api.registerServiceHealthSubscription(subscriptionId.trim());
    setSubscriptionId("");
  }, "Subscription registered and diagnostic setting verified.");

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

  const updateChannel = (channel: ServiceHealthChannel, subscribedEventTypes: ServiceHealthEventType[]) =>
    runAction(
      () => api.updateServiceHealthChannel({
        ...channel,
        displayName: channel.displayName || channel.channelName || "Teams channel",
        subscribedEventTypes,
      }).then(() => undefined),
      subscribedEventTypes.length > 0
        ? "Teams channel routing updated."
        : "Teams channel notifications paused because no event families are selected.",
    );

  const deleteEvent = (event: ServiceHealthEvent) => {
    if (!window.confirm(`Permanently delete event ${event.trackingId} and its completed delivery intents?`)) return;
    void runAction(
      () => api.deleteServiceHealthEvent(event.id),
      `Event ${event.trackingId} was permanently deleted.`,
    );
  };

  const deleteIntent = (intent: ServiceHealthDeliveryIntent) => {
    if (!window.confirm(`Permanently delete the delivery intent for ${intent.channelDisplayName}?`)) return;
    void runAction(
      () => api.deleteServiceHealthDeliveryIntent(intent.id),
      "Delivery intent was permanently deleted.",
    );
  };

  if (loading) {
    return <div className={styles.container}><Spinner label="Loading Service Health configuration..." /></div>;
  }

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <Text size={700} weight="bold" block>Service Health Dispatch</Text>
        <Text size={200} className={styles.muted}>
          Register Azure subscriptions, review ingested events, and manage durable Teams delivery intents.
        </Text>
      </div>

      {message && (
        <Text className={message.type === "success" ? styles.success : styles.error}>{message.text}</Text>
      )}

      <TabList
        selectedValue={selectedTab}
        onTabSelect={(_, data) => setSelectedTab(data.value as ServiceHealthTab)}
      >
        <Tab value="subscriptions">Register subscription</Tab>
        <Tab value="events">Recent ingested events</Tab>
        <Tab value="intents">Pending delivery intents</Tab>
      </TabList>

      {selectedTab === "subscriptions" && (
        <>
          <Card className={styles.card}>
            <Text weight="semibold">Register a subscription</Text>
            <Text size={200} className={styles.muted}>
              The provisioning identity must already have the approved diagnostic-setting role on this
              subscription. AzRadar configures only the ServiceHealth Activity Log category.
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
                      if (data.optionValue) setTestEventType(data.optionValue as ServiceHealthEventType);
                    }}
                    aria-label="Synthetic event type"
                  >
                    {eventTypes.map((eventType) => (
                      <Option key={eventType.value} value={eventType.value}>{eventType.label}</Option>
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
                    onClick={() => void runAction(
                      () => api.verifyServiceHealthSubscription(subscription.id).then(() => undefined),
                      "Subscription configuration verified.",
                    )}
                  >
                    Verify
                  </Button>
                  <Button
                    size="small"
                    appearance="subtle"
                    icon={<DeleteRegular />}
                    disabled={busy}
                    onClick={() => void runAction(
                      () => api.removeServiceHealthSubscription(subscription.id, false),
                      "Subscription watching was unregistered.",
                    )}
                  >
                    Unregister watching
                  </Button>
                </div>
              </div>
            ))}
          </Card>

          <Card className={styles.card}>
            <Text weight="semibold">Teams app channel routing</Text>
            <Text size={200} className={styles.muted}>
              Install CloudLens in each Teams channel and register it there. Every registered channel can
              independently receive one event family or any combination. Selecting none pauses notifications.
            </Text>
            {channels.length === 0 && (
              <Text className={styles.muted}>No Teams app channels have been registered yet.</Text>
            )}
            {channels.map((channel) => (
              <div className={styles.item} key={channel.id}>
                <div className={styles.itemTop}>
                  <div>
                    <Text weight="semibold">
                      {channel.teamName && channel.channelName
                        ? `${channel.teamName} / ${channel.channelName}`
                        : channel.displayName || channel.channelName || "Teams channel"}
                    </Text>
                    <Text block size={200} className={styles.muted}>
                      {channel.registrationStatus} · Teams app destination
                    </Text>
                    <Text
                      block
                      size={200}
                      className={`${styles.muted} ${styles.mono}`}
                      title={channel.channelId}
                    >
                      Channel ID: {compactId(channel.channelId || channel.id)}
                    </Text>
                  </div>
                  <Badge
                    appearance="outline"
                    color={channel.subscribedEventTypes.length > 0 ? "success" : "subtle"}
                  >
                    {channel.subscribedEventTypes.length > 0 ? "routing configured" : "no event families"}
                  </Badge>
                </div>
                <div className={styles.eventTypes}>
                  {eventTypes.map((eventType) => (
                    <Checkbox
                      key={eventType.value}
                      label={eventType.label}
                      checked={channel.subscribedEventTypes.includes(eventType.value)}
                      disabled={busy || channel.registrationStatus !== "registered"}
                      onChange={(_, data) => {
                        const subscribedEventTypes = data.checked === true
                          ? [...new Set([...channel.subscribedEventTypes, eventType.value])]
                          : channel.subscribedEventTypes.filter((value) => value !== eventType.value);
                        void updateChannel(channel, subscribedEventTypes);
                      }}
                    />
                  ))}
                </div>
              </div>
            ))}
          </Card>
        </>
      )}

      {selectedTab === "events" && (
        <Card className={styles.card}>
          <div className={styles.itemTop}>
            <div>
              <Text weight="semibold">Recent ingested events</Text>
              <Text block size={200} className={styles.muted}>
                Events have been normalized, deduplicated, enriched, and evaluated for configured routes.
              </Text>
            </div>
            <Button appearance="secondary" icon={<ArrowSyncRegular />} disabled={busy} onClick={() => void load()}>
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
                  <Button
                    appearance="subtle"
                    icon={<DeleteRegular />}
                    disabled={busy}
                    onClick={() => deleteEvent(event)}
                    aria-label={`Permanently delete event ${event.trackingId}`}
                  />
                </div>
              </div>
              <Text size={200}>{event.llmAnalysis?.briefSummary || event.summary}</Text>
              <Text size={200} className={styles.muted}>
                Tracking ID: {event.trackingId} · Received: {new Date(event.receivedAt).toLocaleString()}
              </Text>
            </div>
          ))}
        </Card>
      )}

      {selectedTab === "intents" && (
        <Card className={styles.card}>
          <div className={styles.itemTop}>
            <div>
              <Text weight="semibold">Pending delivery intents</Text>
              <Text block size={200} className={styles.muted}>
                Durable delivery records. Active in-flight intents cannot be deleted.
              </Text>
            </div>
            <Button appearance="secondary" icon={<ArrowSyncRegular />} disabled={busy} onClick={() => void load()}>
              Refresh
            </Button>
          </div>
          {visibleDeliveryIntents.length === 0 && (
            <Text className={styles.muted}>No pending or failed delivery intents exist.</Text>
          )}
          {visibleDeliveryIntents.map((intent) => (
            <div className={styles.item} key={intent.id}>
              <div className={styles.itemTop}>
                <div>
                  <Text weight="semibold">{intent.channelDisplayName}</Text>
                  <Text block size={200} className={styles.muted}>
                    {intent.eventType} · Event {intent.eventId}
                  </Text>
                </div>
                <div className={styles.actions}>
                  <Badge appearance="outline" color={intent.status === "delivered" ? "success" : "warning"}>
                    {intent.status}
                  </Badge>
                  <Button
                    appearance="subtle"
                    icon={<DeleteRegular />}
                    disabled={busy || ["queued", "dispatching", "retry-scheduled"].includes(intent.status)}
                    onClick={() => deleteIntent(intent)}
                    aria-label={`Permanently delete delivery intent for ${intent.channelDisplayName}`}
                  />
                </div>
              </div>
              {intent.lastErrorMessage && (
                <Text size={200} className={styles.error}>
                  {intent.lastErrorCode}: {intent.lastErrorMessage}
                </Text>
              )}
            </div>
          ))}
        </Card>
      )}
    </div>
  );
}
