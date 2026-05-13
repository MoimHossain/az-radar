import {
  makeStyles,
  tokens,
  Text,
  Button,
  Tooltip,
  mergeClasses,
} from "@fluentui/react-components";
import {
  NavigationRegular,
  BoardRegular,
  TaskListSquareLtrRegular,
  DocumentTextRegular,
  SettingsRegular,
  DocumentSearchRegular,
  ShieldCheckmarkRegular,
  PlugConnectedRegular,
  CalendarRegular,
} from "@fluentui/react-icons";
import { useNavigate, useLocation } from "react-router-dom";
import { useState } from "react";

const NAV_WIDTH_EXPANDED = 220;
const NAV_WIDTH_COLLAPSED = 48;

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
    padding: "0 12px",
    height: "48px",
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
    gap: "8px",
    zIndex: 10,
  },
  hamburger: {
    color: tokens.colorNeutralForegroundOnBrand,
    minWidth: "32px",
    "&:hover": {
      color: tokens.colorNeutralForegroundOnBrand,
      backgroundColor: "rgba(255,255,255,0.1)",
    },
  },
  logo: {
    fontSize: "16px",
    fontWeight: 700,
    letterSpacing: "-0.02em",
  },
  body: {
    display: "flex",
    flex: 1,
    overflow: "hidden",
  },
  sidebar: {
    display: "flex",
    flexDirection: "column",
    backgroundColor: tokens.colorNeutralBackground1,
    borderRight: `1px solid ${tokens.colorNeutralStroke2}`,
    overflow: "hidden",
    transitionProperty: "width",
    transitionDuration: "200ms",
    transitionTimingFunction: "ease",
    flexShrink: 0,
  },
  sidebarExpanded: {
    width: `${NAV_WIDTH_EXPANDED}px`,
  },
  sidebarCollapsed: {
    width: `${NAV_WIDTH_COLLAPSED}px`,
  },
  navList: {
    display: "flex",
    flexDirection: "column",
    padding: "8px 0",
    gap: "2px",
  },
  sectionHeader: {
    fontSize: "11px",
    fontWeight: 600,
    textTransform: "uppercase" as const,
    color: tokens.colorNeutralForeground3,
    whiteSpace: "nowrap" as const,
    overflow: "hidden",
    userSelect: "none" as const,
  },
  navItem: {
    display: "flex",
    alignItems: "center",
    gap: "12px",
    padding: "8px 12px",
    cursor: "pointer",
    borderRadius: "0",
    border: "none",
    background: "none",
    color: tokens.colorNeutralForeground2,
    fontSize: "14px",
    whiteSpace: "nowrap" as const,
    overflow: "hidden",
    minHeight: "36px",
    textAlign: "left" as const,
    width: "100%",
    "&:hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
      color: tokens.colorNeutralForeground1,
    },
  },
  navItemActive: {
    backgroundColor: tokens.colorNeutralBackground1Selected,
    color: tokens.colorBrandForeground1,
    fontWeight: 600,
    borderLeft: `3px solid ${tokens.colorBrandForeground1}`,
    paddingLeft: "9px",
    "&:hover": {
      backgroundColor: tokens.colorNeutralBackground1Selected,
      color: tokens.colorBrandForeground1,
    },
  },
  navIcon: {
    fontSize: "20px",
    flexShrink: 0,
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    width: "20px",
  },
  navLabel: {
    overflow: "hidden",
    textOverflow: "ellipsis",
  },
  content: {
    flex: 1,
    overflow: "auto",
    backgroundColor: tokens.colorNeutralBackground2,
  },
});

interface NavItem {
  path: string;
  label: string;
  icon: React.ReactNode;
}

interface NavSection {
  title: string;
  items: NavItem[];
}

const navSections: NavSection[] = [
  {
    title: "CloudLens",
    items: [
      { path: "/", label: "Dashboard", icon: <BoardRegular /> },
    ],
  },
  {
    title: "Drilldowns",
    items: [
      { path: "/feed-items", label: "Azure Updates", icon: <DocumentTextRegular /> },
      { path: "/doc-insights", label: "Docs Intelligence", icon: <DocumentSearchRegular /> },
      { path: "/impact-analysis", label: "Impact Analysis", icon: <ShieldCheckmarkRegular /> },
      { path: "/lifecycle-calendar", label: "Lifecycle Calendar", icon: <CalendarRegular /> },
    ],
  },
  {
    title: "Services",
    items: [
      { path: "/crawl-jobs", label: "Crawling Jobs", icon: <TaskListSquareLtrRegular /> },
    ],
  },
  {
    title: "Settings",
    items: [
      { path: "/watchlist", label: "Service Watchlist", icon: <SettingsRegular /> },
      { path: "/blast-radius-config", label: "Blast Radius Config", icon: <PlugConnectedRegular /> },
    ],
  },
];

interface AppShellProps {
  children: React.ReactNode;
}

export function AppShell({ children }: AppShellProps) {
  const styles = useStyles();
  const navigate = useNavigate();
  const location = useLocation();
  const [expanded, setExpanded] = useState(true);

  return (
    <div className={styles.shell}>
      <div className={styles.topBar}>
        <Button
          appearance="subtle"
          icon={<NavigationRegular />}
          className={styles.hamburger}
          onClick={() => setExpanded((v) => !v)}
          size="small"
        />
        <Text className={styles.logo}>🔬 CloudLens</Text>
        <Text size={200} style={{ opacity: 0.8 }}>
          Sharp focus on what's changing in your Azure estate
        </Text>
      </div>

      <div className={styles.body}>
        <nav
          className={mergeClasses(
            styles.sidebar,
            expanded ? styles.sidebarExpanded : styles.sidebarCollapsed
          )}
        >
          <div className={styles.navList}>
            {navSections.map((section, sectionIdx) => (
              <div key={section.title}>
                {expanded && (
                  <div
                    className={styles.sectionHeader}
                    style={{
                      padding: sectionIdx === 0
                        ? "16px 12px 4px 12px"
                        : "20px 12px 4px 12px",
                    }}
                  >
                    {section.title}
                  </div>
                )}
                {section.items.map((item) => {
                  const isActive = location.pathname === item.path;
                  const button = (
                    <button
                      key={item.path}
                      className={mergeClasses(
                        styles.navItem,
                        isActive && styles.navItemActive
                      )}
                      onClick={() => navigate(item.path)}
                    >
                      <span className={styles.navIcon}>{item.icon}</span>
                      {expanded && (
                        <span className={styles.navLabel}>{item.label}</span>
                      )}
                    </button>
                  );

                  return expanded ? (
                    button
                  ) : (
                    <Tooltip
                      key={item.path}
                      content={item.label}
                      relationship="label"
                      positioning="after"
                    >
                      {button}
                    </Tooltip>
                  );
                })}
              </div>
            ))}
          </div>
        </nav>

        <div className={styles.content}>{children}</div>
      </div>
    </div>
  );
}
