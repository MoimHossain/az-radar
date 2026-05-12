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
  totalJobs: number;
  pendingJobs: number;
  completedJobs: number;
  failedJobs: number;
  totalFeedItems: number;
  criticalItems: number;
  highItems: number;
  totalDocInsights: number;
  latestCrawl?: string;
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
};
