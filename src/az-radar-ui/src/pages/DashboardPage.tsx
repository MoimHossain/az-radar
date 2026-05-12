import {
  makeStyles,
  tokens,
  Card,
  CardHeader,
  Text,
} from "@fluentui/react-components";
import {
  ShieldCheckmarkRegular,
  WarningRegular,
  ErrorCircleRegular,
  CheckmarkCircleRegular,
  ClockRegular,
  ArrowSyncRegular,
} from "@fluentui/react-icons";
import { useEffect, useState } from "react";
import { api, type DashboardStats } from "../api/client";

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    gap: "24px",
    padding: "24px",
  },
  header: {
    display: "flex",
    flexDirection: "column",
    gap: "4px",
  },
  statsGrid: {
    display: "grid",
    gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))",
    gap: "16px",
  },
  statCard: {
    padding: "20px",
    display: "flex",
    flexDirection: "column",
    gap: "8px",
  },
  statValue: {
    fontSize: "32px",
    fontWeight: 700,
    lineHeight: 1,
  },
  statLabel: {
    fontSize: "13px",
    color: tokens.colorNeutralForeground3,
  },
  statIcon: {
    fontSize: "24px",
    marginBottom: "8px",
  },
  criticalColor: {
    color: tokens.colorPaletteRedForeground1,
  },
  warningColor: {
    color: tokens.colorPaletteYellowForeground1,
  },
  successColor: {
    color: tokens.colorPaletteGreenForeground1,
  },
  infoColor: {
    color: tokens.colorBrandForeground1,
  },
  welcomeCard: {
    padding: "24px",
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
  },
});

export function DashboardPage() {
  const styles = useStyles();
  const [stats, setStats] = useState<DashboardStats | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    api
      .getDashboardStats()
      .then(setStats)
      .catch(console.error)
      .finally(() => setLoading(false));
  }, []);

  if (loading) {
    return (
      <div className={styles.container}>
        <Text size={400}>Loading dashboard...</Text>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <Text size={800} weight="bold">
          CloudLens
        </Text>
        <Text size={300} style={{ color: tokens.colorNeutralForeground3 }}>
          Sharp focus on what's changing in your Azure estate.
        </Text>
      </div>

      <div className={styles.statsGrid}>
        <Card className={styles.statCard}>
          <ArrowSyncRegular className={`${styles.statIcon} ${styles.infoColor}`} />
          <Text className={styles.statValue}>{stats?.totalJobs ?? 0}</Text>
          <Text className={styles.statLabel}>Total Crawl Jobs</Text>
        </Card>

        <Card className={styles.statCard}>
          <CheckmarkCircleRegular
            className={`${styles.statIcon} ${styles.successColor}`}
          />
          <Text className={styles.statValue}>{stats?.completedJobs ?? 0}</Text>
          <Text className={styles.statLabel}>Completed Jobs</Text>
        </Card>

        <Card className={styles.statCard}>
          <ClockRegular className={`${styles.statIcon} ${styles.warningColor}`} />
          <Text className={styles.statValue}>{stats?.pendingJobs ?? 0}</Text>
          <Text className={styles.statLabel}>Pending Jobs</Text>
        </Card>

        <Card className={styles.statCard}>
          <ShieldCheckmarkRegular
            className={`${styles.statIcon} ${styles.infoColor}`}
          />
          <Text className={styles.statValue}>{stats?.totalFeedItems ?? 0}</Text>
          <Text className={styles.statLabel}>Feed Items Tracked</Text>
        </Card>

        <Card className={styles.statCard}>
          <ErrorCircleRegular
            className={`${styles.statIcon} ${styles.criticalColor}`}
          />
          <Text className={styles.statValue}>{stats?.criticalItems ?? 0}</Text>
          <Text className={styles.statLabel}>Critical Items</Text>
        </Card>

        <Card className={styles.statCard}>
          <WarningRegular className={`${styles.statIcon} ${styles.warningColor}`} />
          <Text className={styles.statValue}>{stats?.highItems ?? 0}</Text>
          <Text className={styles.statLabel}>High Severity Items</Text>
        </Card>
      </div>

      {stats?.latestCrawl && (
        <Card className={styles.statCard}>
          <CardHeader
            header={<Text weight="semibold">Last Crawl</Text>}
            description={
              <Text size={200}>
                {new Date(stats.latestCrawl).toLocaleString()}
              </Text>
            }
          />
        </Card>
      )}
    </div>
  );
}
