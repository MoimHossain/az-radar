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

type ServiceHealthTab = "subscriptions" | "events" | "intents" | "targets";
type DispatchTargetTab = "teams" | "wiki";

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
  const [selectedTargetTab, setSelectedTargetTab] = useState<DispatchTargetTab>("teams");
  const [subscriptions, setSubscriptions] = useState<ServiceHealthSubscription[]>([]);
  const [channels, setChannels] = useState<ServiceHealthChannel[]>([]);
  const [wikiTargets, setWikiTargets] = useState<ServiceHealthChannel[]>([]);
  const [events, setEvents] = useState<ServiceHealthEvent[]>([]);
  const [deliveryIntents, setDeliveryIntents] = useState<ServiceHealthDeliveryIntent[]>([]);
  const [subscriptionId, setSubscriptionId] = useState("");
  const [testEventType, setTestEventType] = useState<ServiceHealthEventType>("ServiceIssue");
  const [azureRegions, setAzureRegions] = useState<string[]>([]);
  const [testEventRegions, setTestEventRegions] = useState<string[]>(["Central US"]);
  const [wikiDisplayName, setWikiDisplayName] = useState("CloudLens Service Health Hub");
  const [wikiUri, setWikiUri] = useState("");
  const [wikiAuthenticationType, setWikiAuthenticationType] =
    useState<"pat" | "managed-identity">("pat");
  const [wikiPat, setWikiPat] = useState("");
  const [wikiManagedIdentityClientId, setWikiManagedIdentityClientId] = useState("");
  const [wikiRegions, setWikiRegions] = useState<string[]>(["Central US"]);
  const [targetRegionDrafts, setTargetRegionDrafts] = useState<Record<string, string[]>>({});
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ type: "success" | "error"; text: string } | null>(null);
  const visibleDeliveryIntents = deliveryIntents.filter(
    (intent) => intent.status !== "delivered" && intent.status !== "cancelled",
  );

  const load = async () => {
    const [registeredSubscriptions, notificationChannels, recentEvents, recentIntents, regions] = await Promise.all([
      api.getServiceHealthSubscriptions(),
      api.getServiceHealthChannels(),
      api.getServiceHealthEvents(50),
      api.getServiceHealthDeliveryIntents(100),
      api.getAzureRegions(),
    ]);
    setSubscriptions(registeredSubscriptions);
    setChannels(notificationChannels.filter((channel) => channel.type === "teams-bot"));
    setWikiTargets(notificationChannels.filter((channel) => channel.type === "azure-devops-wiki"));
    setEvents(recentEvents);
    setDeliveryIntents(recentIntents);
    setAzureRegions(regions);
    setTargetRegionDrafts(Object.fromEntries(
      notificationChannels
        .filter((channel) => channel.type === "azure-devops-wiki")
        .map((channel) => [channel.id, channel.includedRegions ?? []]),
    ));
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
      await load().catch(() => undefined);
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
      const result = await api.publishServiceHealthTestEvent(id, testEventType, testEventRegions);
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

  const registerWikiTarget = () => runAction(async () => {
    await api.createServiceHealthWikiTarget({
      displayName: wikiDisplayName.trim(),
      wikiUri: wikiUri.trim(),
      authenticationType: wikiAuthenticationType,
      includedRegions: wikiRegions,
      personalAccessToken: wikiAuthenticationType === "pat" ? wikiPat : undefined,
      managedIdentityClientId:
        wikiAuthenticationType === "managed-identity"
          ? wikiManagedIdentityClientId.trim()
          : undefined,
    });
    setWikiUri("");
    setWikiPat("");
  }, "Azure DevOps Wiki target registered and initial publication queued.");

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
          Register Azure subscriptions, review ingested events, and manage durable dispatching targets.
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
        <Tab value="targets">Dispatching targets</Tab>
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
                  <Dropdown
                    size="small"
                    multiselect
                    value={testEventRegions.length === 0
                      ? "No region (fallback)"
                      : `${testEventRegions.length} region${testEventRegions.length === 1 ? "" : "s"}`}
                    selectedOptions={testEventRegions}
                    onOptionSelect={(_, data) => setTestEventRegions(data.selectedOptions)}
                    aria-label="Synthetic event regions"
                  >
                    <Option value="Global">Global</Option>
                    {azureRegions.map((region) => (
                      <Option key={region} value={region}>{region}</Option>
                    ))}
                  </Dropdown>
                  <Button
                    size="small"
                    appearance="subtle"
                    onClick={() => setTestEventRegions([])}
                  >
                    Test missing region
                  </Button>
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

        </>
      )}

      {selectedTab === "targets" && (
        <>
          <TabList
            selectedValue={selectedTargetTab}
            onTabSelect={(_, data) => setSelectedTargetTab(data.value as DispatchTargetTab)}
          >
            <Tab value="teams">Teams channels</Tab>
            <Tab value="wiki">Azure DevOps Wiki</Tab>
          </TabList>

          {selectedTargetTab === "teams" && (
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
          )}

          {selectedTargetTab === "wiki" && (
            <Card className={styles.card}>
              <Text weight="semibold">Azure DevOps Wiki projection</Text>
              <Text size={200} className={styles.muted}>
                CloudLens maintains each registered wiki page with all four Service Health event families.
                Pages are refreshed after event changes and reconciled daily.
              </Text>

              <div className={styles.row}>
                <Field label="Display name" required style={{ flex: 1, minWidth: "280px" }}>
                  <Input
                    value={wikiDisplayName}
                    onChange={(_, data) => setWikiDisplayName(data.value)}
                  />
                </Field>
                <Field label="Authentication" required style={{ minWidth: "220px" }}>
                  <Dropdown
                    value={wikiAuthenticationType === "pat" ? "Personal Access Token" : "Managed Identity"}
                    selectedOptions={[wikiAuthenticationType]}
                    onOptionSelect={(_, data) => {
                      if (data.optionValue) {
                        setWikiAuthenticationType(data.optionValue as "pat" | "managed-identity");
                      }
                    }}
                  >
                    <Option value="pat">Personal Access Token</Option>
                    <Option value="managed-identity">Managed Identity</Option>
                  </Dropdown>
                </Field>
              </div>
              <Field label="Azure DevOps Wiki page URI" required>
                <Input
                  className={styles.mono}
                  value={wikiUri}
                  onChange={(_, data) => setWikiUri(data.value)}
                  placeholder="https://dev.azure.com/organization/project/_wiki/wikis/wiki/123/page"
                />
              </Field>
              <Field
                label="Azure regions"
                hint="Known regional events must match one of these regions. Global or unscoped events are always shown."
                required
              >
                <Dropdown
                  multiselect
                  value={`${wikiRegions.length} region${wikiRegions.length === 1 ? "" : "s"} selected`}
                  selectedOptions={wikiRegions}
                  onOptionSelect={(_, data) => setWikiRegions(data.selectedOptions)}
                >
                  {azureRegions.map((region) => (
                    <Option key={region} value={region}>{region}</Option>
                  ))}
                </Dropdown>
              </Field>
              {wikiAuthenticationType === "pat" ? (
                <Field
                  label="Personal Access Token"
                  hint="Stored in Azure Key Vault and never returned by the API."
                  required
                >
                  <Input
                    type="password"
                    value={wikiPat}
                    onChange={(_, data) => setWikiPat(data.value)}
                  />
                </Field>
              ) : (
                <Field
                  label="User-assigned managed identity client ID"
                  hint="The identity must be attached to CloudLens and authorized in Azure DevOps."
                  required
                >
                  <Input
                    className={styles.mono}
                    value={wikiManagedIdentityClientId}
                    onChange={(_, data) => setWikiManagedIdentityClientId(data.value)}
                  />
                </Field>
              )}
              <div className={styles.actions}>
                <Button
                  appearance="primary"
                  icon={busy ? <Spinner size="tiny" /> : <AddRegular />}
                  disabled={
                    busy ||
                    !wikiDisplayName.trim() ||
                    !wikiUri.trim() ||
                    wikiRegions.length === 0 ||
                    (wikiAuthenticationType === "pat"
                      ? !wikiPat
                      : !wikiManagedIdentityClientId.trim())
                  }
                  onClick={() => void registerWikiTarget()}
                >
                  Register and publish
                </Button>
              </div>

              {wikiTargets.map((target) => (
                <div className={styles.item} key={target.id}>
                  <div className={styles.itemTop}>
                    <div>
                      <Text weight="semibold">{target.displayName}</Text>
                      <Text block size={200} className={styles.muted}>
                        {target.azureDevOpsOrganization} / {target.azureDevOpsProject} /{" "}
                        {target.azureDevOpsWikiIdentifier}
                      </Text>
                      <Text block size={200} className={styles.mono}>
                        {target.azureDevOpsPagePath}
                      </Text>
                    </div>
                    <Badge
                      appearance="outline"
                      color={
                        target.registrationStatus === "registered"
                          ? "success"
                          : target.registrationStatus === "pending"
                            ? "warning"
                            : "danger"
                      }
                    >
                      {target.registrationStatus}
                    </Badge>
                  </div>
                  <Text size={200} className={styles.muted}>
                    Authentication: {target.authenticationType === "pat" ? "Personal Access Token" : "Managed Identity"}
                    {" · "}Last success: {target.lastSucceededAt
                      ? new Date(target.lastSucceededAt).toLocaleString()
                      : "not published yet"}
                  </Text>
                  <Field
                    label="Azure regions"
                    hint="Global, missing, and unknown-only event scope is shown automatically."
                  >
                    <Dropdown
                      multiselect
                      value={`${(targetRegionDrafts[target.id] ?? []).length} region${
                        (targetRegionDrafts[target.id] ?? []).length === 1 ? "" : "s"
                      } selected`}
                      selectedOptions={targetRegionDrafts[target.id] ?? []}
                      onOptionSelect={(_, data) => setTargetRegionDrafts((current) => ({
                        ...current,
                        [target.id]: data.selectedOptions,
                      }))}
                    >
                      {azureRegions.map((region) => (
                        <Option key={region} value={region}>{region}</Option>
                      ))}
                    </Dropdown>
                  </Field>
                  {target.lastErrorMessage && (
                    <Text size={200} className={styles.error}>
                      {target.lastErrorCode}: {target.lastErrorMessage}
                    </Text>
                  )}
                  <div className={styles.actions}>
                    <Button
                      size="small"
                      appearance="secondary"
                      disabled={busy || (targetRegionDrafts[target.id] ?? []).length === 0}
                      onClick={() => void runAction(
                        () => api.updateServiceHealthWikiTarget(
                          target.id,
                          target.displayName,
                          targetRegionDrafts[target.id] ?? [],
                        ).then(() => undefined),
                        "Azure DevOps Wiki regions updated and publication queued.",
                      )}
                    >
                      Save regions
                    </Button>
                    <Button
                      appearance="primary"
                      icon={<SendRegular />}
                      disabled={busy}
                      onClick={() => void runAction(
                        () => api.publishServiceHealthWikiTarget(target.id).then(() => undefined),
                        "Wiki publication queued.",
                      )}
                    >
                      Publish now
                    </Button>
                    <Button
                      appearance="secondary"
                      icon={<ArrowSyncRegular />}
                      disabled={busy}
                      onClick={() => void runAction(
                        () => api.testServiceHealthWikiTarget(target.id).then(() => undefined),
                        "Azure DevOps Wiki connection verified.",
                      )}
                    >
                      Test connection
                    </Button>
                    <Button
                      appearance="subtle"
                      icon={<DeleteRegular />}
                      disabled={busy}
                      onClick={() => {
                        if (!window.confirm(`Remove Azure DevOps Wiki target ${target.displayName}?`)) return;
                        void runAction(
                          () => api.deleteServiceHealthWikiTarget(target.id),
                          "Azure DevOps Wiki target removed.",
                        );
                      }}
                    >
                      Remove
                    </Button>
                  </div>
                </div>
              ))}
            </Card>
          )}
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
