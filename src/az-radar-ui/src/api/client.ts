export interface CrawlJob {
  id: string;
  jobType: string;
  skipLlmAnalysis?: boolean;
  status: string;
  createdAt: string;
  startedAt?: string;
  lastHeartbeatAt?: string;
  lastProgressAt?: string;
  isStale?: boolean;
  completedAt?: string;
  result?: CrawlJobResult;
  error?: string;
  attemptCount: number;
}

export interface CrawlJobResult {
  newItems: number;
  totalChecked: number;
  skippedItems: number;
  updatedItems?: number;
}

export interface FeedItem {
  id: string;
  source: string;
  title: string;
  link: string;
  publishDate: string;
  summary: string;
  categories: string[];
  rawContent: string;
  llmAnalysis?: LlmAnalysis;
  llmAnalysisSkipped?: boolean;
  firstSeenAt: string;
  crawlJobId: string;
}

export interface LlmAnalysis {
  suggestedTitle: string;
  changeType: string;
  severity: string;
  affectedServices: string[];
  affectedResourceTypes: string[];
  actionRequired: string;
  deadline?: string;
  effortEstimate: string;
  migrationPath: string;
  microsoftDocLinks: string[];
  aiConfidence: number;
  briefSummary: string;
  requiresAttention?: boolean;
  attentionJustification?: string;
}

export interface DashboardStats {
  totalItems: number;
  totalRetirements: number;
  totalGA: number;
  totalPreviews: number;
  totalNewFeatures: number;
  urgentDeadlines: number;
  watchedServices: number;
  totalJobs: number;
  completedJobs: number;
  latestCrawl?: string;
  changeTypeBreakdown: Record<string, number>;
  severityBreakdown: Record<string, number>;
  sourceBreakdown: { azureUpdates: number; msLearnDocs: number };
  deadlines: Array<{
    title: string;
    link: string;
    deadline: string;
    severity: string;
    changeType: string;
    actionRequired: string;
    affectedServices: string[];
    source: string;
    daysRemaining: number | null;
  }>;
  topAffectedServices: Array<{
    service: string;
    total: number;
    retirements: number;
  }>;
  blastRadiusTotalResources: number;
  blastRadiusItemsScanned: number;
  blastRadiusSubscriptions: number;
  blastRadiusLastScan?: string;
}

export interface WatchlistItem {
  id: string;
  serviceName: string;
  aliases: string[];
  searchTerms: string[];
  resourceProvider: string;
  addedAt: string;
}

export interface DocInsight {
  id: string;
  source: string;
  serviceName: string;
  docUrl: string;
  title: string;
  snippet: string;
  contentHash: string;
  llmAnalysis?: LlmAnalysis;
  firstSeenAt: string;
  lastAnalyzedAt: string;
  crawlJobId: string;
  commitSha?: string;
  commitDate?: string;
  changeKind?: string;
  repoUrl?: string;
  filePath?: string;
  relatedFeedItems?: RelatedFeedItem[];
}

export interface RelatedFeedItem {
  id: string;
  title: string;
  link: string;
  publishDate: string;
}

export interface RepoWatchItem {
  id: string;
  repoUrl: string;
  owner: string;
  repo: string;
  branch?: string;
  pathFilters: string[];
  label: string;
  cutoffDate: string;
  enabled: boolean;
  addedAt: string;
  lastScannedCommitSha?: string;
  lastScannedCommitDate?: string;
  lastScanAt?: string;
  lastScanStatus?: string;
  lastScanError?: string;
}

export interface CreateRepoWatchRequest {
  repoUrl: string;
  branch?: string;
  pathFilters?: string[];
  label?: string;
  cutoffDate?: string;
  enabled?: boolean;
}

export interface CalendarItem {
  id: string;
  title: string;
  link: string;
  deadline: string;
  changeType: string;
  severity: string;
  affectedServices: string[];
  actionRequired: string;
  source: string;
  briefSummary: string;
}

export interface AppConfig {
  id: string;
  value: string;
  description: string;
  updatedAt: string;
}

export interface BlastRadiusSummary {
  id: string;
  sourceItemId: string;
  sourceTitle: string;
  sourceType: string;
  changeType: string;
  severity: string;
  deadline?: string;
  resourceType: string;
  matchConfidence: string;
  totalResources: number;
  subscriptionCount: number;
  regionBreakdown: Record<string, number>;
  subscriptionBreakdown: Record<string, number>;
  topResources: AffectedResource[];
  scanJobId: string;
  scannedAt: string;
  sourceDescription: string;
  sourceLink: string;
  actionRequired: string;
  argQuery: string;
}

export interface JobDiagnosticEntry {
  id: string;
  jobId: string;
  timestamp: string;
  step: string;
  itemTitle: string;
  level: string;
  message: string;
  llmQuery?: string;
  argError?: string;
  attempt?: number;
  resultCount?: number;
  durationMs?: number;
}

export interface AffectedResource {
  subscriptionId: string;
  resourceGroup: string;
  name: string;
  location: string;
  sku: string;
  tags: Record<string, string>;
}

export type ServiceHealthEventType =
  | "ServiceIssue"
  | "PlannedMaintenance"
  | "HealthAdvisory"
  | "SecurityAdvisory";

export interface ServiceHealthSubscription {
  id: string;
  displayName: string;
  tenantId: string;
  status: string;
  diagnosticSettingName: string;
  eventHubName: string;
  provisioningIdentityClientId: string;
  lastVerifiedAt?: string;
  lastProvisioningAttemptAt?: string;
  lastErrorCode?: string;
  lastErrorMessage?: string;
}

export interface ServiceHealthChannel {
  id: string;
  displayName: string;
  type: "teams-workflow" | "teams-bot";
  secretUri: string;
  tenantId: string;
  teamId: string;
  teamName: string;
  channelId: string;
  channelName: string;
  conversationReferenceId: string;
  registrationStatus: "pending" | "registered" | "uninstalled";
  lastRegisteredAt?: string;
  subscribedEventTypes: ServiceHealthEventType[];
  enabled: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface ServiceHealthEvent {
  id: string;
  eventDataId: string;
  trackingId: string;
  subscriptionId: string;
  eventType: ServiceHealthEventType;
  status: string;
  level: string;
  title: string;
  summary: string;
  service: string;
  region: string;
  eventTimestamp: string;
  receivedAt: string;
  isSynthetic: boolean;
  llmAnalysis?: LlmAnalysis;
  routingStatus: string;
  matchingChannelIds: string[];
}

export interface ServiceHealthDeliveryIntent {
  id: string;
  eventId: string;
  channelId: string;
  channelDisplayName: string;
  eventType: ServiceHealthEventType;
  status: string;
  createdAt: string;
}

const API_BASE = import.meta.env.VITE_API_URL || "";

async function apiFetch<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    ...options,
    headers: { "Content-Type": "application/json", ...options?.headers },
  });
  if (!res.ok) {
    const body = await res.json().catch(() => null) as
      | { error?: string; lastErrorMessage?: string }
      | null;
    throw new Error(body?.error || body?.lastErrorMessage || `API error: ${res.status} ${res.statusText}`);
  }
  return res.json();
}

export const api = {
  getDashboardStats: () => apiFetch<DashboardStats>("/api/dashboard/stats"),

  getCrawlJobs: (limit = 50) => apiFetch<CrawlJob[]>(`/api/crawl-jobs?limit=${limit}`),

  getCrawlJob: (id: string) => apiFetch<CrawlJob>(`/api/crawl-jobs/${id}`),

  createCrawlJob: (jobType: string, skipLlmAnalysis = false) =>
    apiFetch<CrawlJob>("/api/crawl-jobs", {
      method: "POST",
      body: JSON.stringify({ jobType, skipLlmAnalysis }),
    }),

  deleteCrawlJob: (id: string) =>
    fetch(`${API_BASE}/api/crawl-jobs/${id}`, { method: "DELETE" }).then((r) => {
      if (!r.ok && r.status !== 404) throw new Error(`Delete failed: ${r.status}`);
    }),

  getFeedItems: (source?: string, limit = 50) => {
    const params = new URLSearchParams({ limit: String(limit) });
    if (source) params.set("source", source);
    return apiFetch<FeedItem[]>(`/api/feed-items?${params}`);
  },

  getFeedItem: (id: string) => apiFetch<FeedItem>(`/api/feed-items/${id}`),

  // Watchlist
  getWatchlist: () => apiFetch<WatchlistItem[]>("/api/watchlist"),

  addToWatchlist: (serviceName: string) =>
    apiFetch<WatchlistItem>("/api/watchlist", {
      method: "POST",
      body: JSON.stringify({ serviceName }),
    }),

  removeFromWatchlist: (id: string) =>
    fetch(`${API_BASE}/api/watchlist/${id}`, { method: "DELETE" }).then((r) => {
      if (!r.ok && r.status !== 404) throw new Error(`Delete failed: ${r.status}`);
    }),

  // Repository Watchlist (GitHub Change Radar)
  getRepoWatchlist: () => apiFetch<RepoWatchItem[]>("/api/repo-watchlist"),

  addRepoWatch: (request: CreateRepoWatchRequest) =>
    apiFetch<RepoWatchItem>("/api/repo-watchlist", {
      method: "POST",
      body: JSON.stringify(request),
    }),

  removeRepoWatch: (id: string) =>
    fetch(`${API_BASE}/api/repo-watchlist/${id}`, { method: "DELETE" }).then((r) => {
      if (!r.ok && r.status !== 404) throw new Error(`Delete failed: ${r.status}`);
    }),

  patchRepoWatch: (id: string, patch: Partial<Pick<RepoWatchItem, "enabled" | "cutoffDate" | "pathFilters" | "label">>) =>
    apiFetch<RepoWatchItem>(`/api/repo-watchlist/${id}`, {
      method: "PATCH",
      body: JSON.stringify(patch),
    }),

  // Doc Insights
  getDocInsights: (serviceName?: string, limit = 50) => {
    const params = new URLSearchParams({ limit: String(limit) });
    if (serviceName) params.set("serviceName", serviceName);
    return apiFetch<DocInsight[]>(`/api/doc-insights?${params}`);
  },

  getDocInsight: (id: string) => apiFetch<DocInsight>(`/api/doc-insights/${id}`),

  // Config
  getConfig: (key: string) => apiFetch<AppConfig>(`/api/config/${key}`).catch(() => null),

  setConfig: (key: string, value: string, description?: string) =>
    apiFetch<AppConfig>(`/api/config/${key}`, {
      method: "PUT",
      body: JSON.stringify({ value, description }),
    }),

  // Blast Radius
  getJobDiagnostics: (jobId: string) =>
    apiFetch<JobDiagnosticEntry[]>(`/api/crawl-jobs/${jobId}/diagnostics`),

  getBlastRadiusSummaries: (limit = 100) =>
    apiFetch<BlastRadiusSummary[]>(`/api/blast-radius?limit=${limit}`),

  getBlastRadiusSummary: (id: string) =>
    apiFetch<BlastRadiusSummary>(`/api/blast-radius/${id}`),

  getCalendarItems: () => apiFetch<CalendarItem[]>("/api/calendar"),

  // Service Health
  getServiceHealthSubscriptions: () =>
    apiFetch<ServiceHealthSubscription[]>("/api/service-health/subscriptions"),

  registerServiceHealthSubscription: (subscriptionId: string) =>
    apiFetch<ServiceHealthSubscription>("/api/service-health/subscriptions", {
      method: "POST",
      body: JSON.stringify({ subscriptionId }),
    }),

  verifyServiceHealthSubscription: (subscriptionId: string) =>
    apiFetch<ServiceHealthSubscription>(`/api/service-health/subscriptions/${subscriptionId}/verify`, {
      method: "POST",
    }),

  removeServiceHealthSubscription: (subscriptionId: string, removeDiagnosticSetting = false) =>
    fetch(
      `${API_BASE}/api/service-health/subscriptions/${subscriptionId}?removeDiagnosticSetting=${removeDiagnosticSetting}`,
      { method: "DELETE" },
    ).then((r) => {
      if (!r.ok && r.status !== 404) throw new Error(`Delete failed: ${r.status}`);
    }),

  getServiceHealthChannels: () =>
    apiFetch<ServiceHealthChannel[]>("/api/service-health/channels"),

  updateServiceHealthChannel: (channel: ServiceHealthChannel) =>
    apiFetch<ServiceHealthChannel>(`/api/service-health/channels/${channel.id}`, {
      method: "PUT",
      body: JSON.stringify({
        displayName: channel.displayName,
        secretUri: channel.secretUri || null,
        subscribedEventTypes: channel.subscribedEventTypes,
        enabled: channel.enabled,
      }),
    }),

  removeServiceHealthChannel: (id: string) =>
    fetch(`${API_BASE}/api/service-health/channels/${id}`, { method: "DELETE" }).then((r) => {
      if (!r.ok && r.status !== 404) throw new Error(`Delete failed: ${r.status}`);
    }),

  publishServiceHealthTestEvent: (
    subscriptionId: string,
    eventType: ServiceHealthEventType,
  ) =>
    apiFetch<{ eventDataId: string; trackingId: string; eventType: ServiceHealthEventType; publishedAt: string }>(
      "/api/service-health/test-events",
      {
        method: "POST",
        body: JSON.stringify({ subscriptionId, eventType }),
      },
    ),

  getServiceHealthEvents: (limit = 50) =>
    apiFetch<ServiceHealthEvent[]>(`/api/service-health/events?limit=${limit}`),

  getServiceHealthDeliveryIntents: (limit = 50) =>
    apiFetch<ServiceHealthDeliveryIntent[]>(`/api/service-health/delivery-intents?limit=${limit}`),
};
