import {
  makeStyles,
  tokens,
  Tab,
  TabList,
  Text,
} from "@fluentui/react-components";
import {
  BoardRegular,
  TaskListSquareLtrRegular,
  DocumentTextRegular,
} from "@fluentui/react-icons";
import { useNavigate, useLocation } from "react-router-dom";

const useStyles = makeStyles({
  shell: {
    display: "flex",
    flexDirection: "column",
    height: "100vh",
    backgroundColor: tokens.colorNeutralBackground2,
  },
  topBar: {
    display: "flex",
    alignItems: "center",
    padding: "0 24px",
    height: "48px",
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
    gap: "12px",
  },
  logo: {
    fontSize: "16px",
    fontWeight: 700,
    letterSpacing: "-0.02em",
  },
  nav: {
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground1,
    paddingLeft: "24px",
  },
  content: {
    flex: 1,
    overflow: "auto",
    backgroundColor: tokens.colorNeutralBackground2,
  },
});

const navItems = [
  { path: "/", label: "Dashboard", icon: <BoardRegular /> },
  { path: "/crawl-jobs", label: "Crawling Jobs", icon: <TaskListSquareLtrRegular /> },
  { path: "/feed-items", label: "Azure Updates", icon: <DocumentTextRegular /> },
];

interface AppShellProps {
  children: React.ReactNode;
}

export function AppShell({ children }: AppShellProps) {
  const styles = useStyles();
  const navigate = useNavigate();
  const location = useLocation();

  return (
    <div className={styles.shell}>
      <div className={styles.topBar}>
        <Text className={styles.logo}>⚡ AzRadar</Text>
        <Text size={200} style={{ opacity: 0.8 }}>
          Azure Lifecycle Sentinel
        </Text>
      </div>

      <div className={styles.nav}>
        <TabList
          selectedValue={location.pathname}
          onTabSelect={(_, d) => navigate(d.value as string)}
          size="medium"
        >
          {navItems.map((item) => (
            <Tab key={item.path} value={item.path} icon={item.icon}>
              {item.label}
            </Tab>
          ))}
        </TabList>
      </div>

      <div className={styles.content}>{children}</div>
    </div>
  );
}
