export interface CrawlJob {
  id: string;
  jobType: string;
  status: string;
  createdAt: string;
  startedAt?: string;
  completedAt?: string;
  result?: CrawlJobResult;
  error?: string;
  attemptCount: number;
}

export interface CrawlJobResult {
  newItems: number;
  totalChecked: number;
  skippedItems: number;
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
  firstSeenAt: string;
  crawlJobId: string;
}

export interface LlmAnalysis {
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

const API_BASE = import.meta.env.VITE_API_URL || "";

async function apiFetch<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    ...options,
    headers: { "Content-Type": "application/json", ...options?.headers },
  });
  if (!res.ok) throw new Error(`API error: ${res.status} ${res.statusText}`);
  return res.json();
}

export const api = {
  getDashboardStats: () => apiFetch<DashboardStats>("/api/dashboard/stats"),

  getCrawlJobs: (limit = 50) => apiFetch<CrawlJob[]>(`/api/crawl-jobs?limit=${limit}`),

  getCrawlJob: (id: string) => apiFetch<CrawlJob>(`/api/crawl-jobs/${id}`),

  createCrawlJob: (jobType: string) =>
    apiFetch<CrawlJob>("/api/crawl-jobs", {
      method: "POST",
      body: JSON.stringify({ jobType }),
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
};
