import {
  makeStyles,
  tokens,
  Card,
  Text,
  Badge,
  Spinner,
  Input,
  Button,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Tooltip,
} from "@fluentui/react-components";
import {
  AddRegular,
  DeleteRegular,
  SettingsRegular,
} from "@fluentui/react-icons";
import { useEffect, useState } from "react";
import { api, type WatchlistItem } from "../api/client";

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    gap: "16px",
    padding: "24px",
  },
  header: {
    display: "flex",
    flexDirection: "column",
    gap: "4px",
  },
  addBar: {
    display: "flex",
    alignItems: "center",
    gap: "12px",
  },
  addInput: {
    minWidth: "320px",
  },
  tableCard: {
    overflow: "hidden",
  },
  emptyState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    gap: "12px",
    padding: "48px 24px",
    textAlign: "center" as const,
  },
});

export function WatchlistPage() {
  const styles = useStyles();
  const [items, setItems] = useState<WatchlistItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [newService, setNewService] = useState("");
  const [adding, setAdding] = useState(false);

  const loadWatchlist = () => {
    setLoading(true);
    api
      .getWatchlist()
      .then(setItems)
      .catch(console.error)
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    loadWatchlist();
  }, []);

  const handleAdd = async () => {
    const name = newService.trim();
    if (!name) return;
    setAdding(true);
    try {
      const item = await api.addToWatchlist(name);
      setItems((prev) => [...prev, item]);
      setNewService("");
    } catch (err) {
      console.error(err);
    } finally {
      setAdding(false);
    }
  };

  const handleRemove = async (id: string) => {
    try {
      await api.removeFromWatchlist(id);
      setItems((prev) => prev.filter((i) => i.id !== id));
    } catch (err) {
      console.error(err);
    }
  };

  if (loading) {
    return (
      <div className={styles.container}>
        <Spinner label="Loading watchlist..." />
      </div>
    );
  }

  return (
    <div className={styles.container}>
      {/* Header */}
      <div className={styles.header}>
        <Text size={700} weight="bold">
          Service Watchlist
        </Text>
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
          Configure Azure services to monitor for lifecycle changes
        </Text>
      </div>

      {/* Add bar */}
      <div className={styles.addBar}>
        <Input
          className={styles.addInput}
          placeholder="Enter Azure service name (e.g. Azure Kubernetes Service)"
          value={newService}
          onChange={(_, d) => setNewService(d.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") handleAdd();
          }}
        />
        <Button
          appearance="primary"
          icon={<AddRegular />}
          onClick={handleAdd}
          disabled={adding || !newService.trim()}
        >
          Add
        </Button>
      </div>

      {/* Table or empty state */}
      {items.length === 0 ? (
        <Card>
          <div className={styles.emptyState}>
            <SettingsRegular
              style={{ fontSize: 48, color: tokens.colorNeutralForeground3 }}
            />
            <Text
              block
              size={400}
              style={{ color: tokens.colorNeutralForeground3 }}
            >
              No services in your watchlist yet.
            </Text>
            <Text
              block
              size={200}
              style={{ color: tokens.colorNeutralForeground4 }}
            >
              Add Azure service names above to start monitoring them for
              retirements, deprecations, and breaking changes.
            </Text>
          </div>
        </Card>
      ) : (
        <Card className={styles.tableCard}>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Service Name</TableHeaderCell>
                <TableHeaderCell style={{ width: 140 }}>
                  Date Added
                </TableHeaderCell>
                <TableHeaderCell style={{ width: 60 }}></TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {items.map((item) => (
                <TableRow key={item.id}>
                  <TableCell>
                    <Badge appearance="tint" size="medium">
                      {item.serviceName}
                    </Badge>
                  </TableCell>
                  <TableCell>
                    <Text size={200}>
                      {new Date(item.addedAt).toLocaleDateString()}
                    </Text>
                  </TableCell>
                  <TableCell>
                    <Tooltip
                      content="Remove from watchlist"
                      relationship="label"
                    >
                      <Button
                        appearance="subtle"
                        icon={<DeleteRegular />}
                        size="small"
                        onClick={() => handleRemove(item.id)}
                      />
                    </Tooltip>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Card>
      )}
    </div>
  );
}
