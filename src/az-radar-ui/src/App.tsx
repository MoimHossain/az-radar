import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { BrowserRouter, Routes, Route } from "react-router-dom";
import { AppShell } from "./components/AppShell";
import { DashboardPage } from "./pages/DashboardPage";
import { CrawlJobsPage } from "./pages/CrawlJobsPage";
import { FeedItemsPage } from "./pages/FeedItemsPage";

function App() {
  return (
    <FluentProvider theme={webLightTheme}>
      <BrowserRouter>
        <AppShell>
          <Routes>
            <Route path="/" element={<DashboardPage />} />
            <Route path="/crawl-jobs" element={<CrawlJobsPage />} />
            <Route path="/feed-items" element={<FeedItemsPage />} />
          </Routes>
        </AppShell>
      </BrowserRouter>
    </FluentProvider>
  );
}

export default App
