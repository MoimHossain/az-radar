import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { BrowserRouter, Routes, Route } from "react-router-dom";
import { AppShell } from "./components/AppShell";
import { DashboardPage } from "./pages/DashboardPage";
import { CrawlJobsPage } from "./pages/CrawlJobsPage";
import { FeedItemsPage } from "./pages/FeedItemsPage";
import { WatchlistPage } from "./pages/WatchlistPage";
import { DocInsightsPage } from "./pages/DocInsightsPage";
import { ImpactAnalysisPage } from "./pages/ImpactAnalysisPage";
import { BlastRadiusConfigPage } from "./pages/BlastRadiusConfigPage";

function App() {
  return (
    <FluentProvider theme={webLightTheme}>
      <BrowserRouter>
        <AppShell>
          <Routes>
            <Route path="/" element={<DashboardPage />} />
            <Route path="/crawl-jobs" element={<CrawlJobsPage />} />
            <Route path="/feed-items" element={<FeedItemsPage />} />
            <Route path="/watchlist" element={<WatchlistPage />} />
            <Route path="/doc-insights" element={<DocInsightsPage />} />
            <Route path="/impact-analysis" element={<ImpactAnalysisPage />} />
            <Route path="/blast-radius-config" element={<BlastRadiusConfigPage />} />
          </Routes>
        </AppShell>
      </BrowserRouter>
    </FluentProvider>
  );
}

export default App
