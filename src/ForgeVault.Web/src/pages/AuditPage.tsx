import { useState } from "react";
import { ShieldCheck } from "lucide-react";
import { Card } from "@/components/ui/Card";
import { Input } from "@/components/ui/Input";
import { Button } from "@/components/ui/Button";
import { Table } from "@/components/ui/Table";
import { MonoId } from "@/components/ui/MonoId";
import { Spinner } from "@/components/ui/Spinner";
import { EmptyState } from "@/components/ui/EmptyState";
import { formatDateTime } from "@/lib/format";
import { useAudit, type AuditFilters } from "@/hooks/useAudit";
import type { AuditLogResponse } from "@/types/api";

const PAGE_SIZE = 25;

export function AuditPage() {
  const [filters, setFilters] = useState<AuditFilters>({});
  const [page, setPage] = useState(1);
  const [expandedId, setExpandedId] = useState<string | null>(null);

  const { data, isLoading } = useAudit({ ...filters, page, pageSize: PAGE_SIZE });

  const updateFilter = (key: keyof AuditFilters, value: string) => {
    setPage(1);
    setFilters((prev) => ({ ...prev, [key]: value || undefined }));
  };

  const totalPages = data ? Math.max(1, Math.ceil(data.total / PAGE_SIZE)) : 1;

  return (
    <Card title="Audit Log">
      <div className="mb-4 grid grid-cols-2 gap-3 md:grid-cols-4">
        <Input label="Actor id" onChange={(e) => updateFilter("actorId", e.target.value)} />
        <Input label="Resource type" onChange={(e) => updateFilter("resourceType", e.target.value)} />
        <Input label="Resource id" onChange={(e) => updateFilter("resourceId", e.target.value)} />
        <Input label="Action" onChange={(e) => updateFilter("action", e.target.value)} />
        <Input label="From" type="datetime-local" onChange={(e) => updateFilter("from", e.target.value)} />
        <Input label="To" type="datetime-local" onChange={(e) => updateFilter("to", e.target.value)} />
      </div>

      {isLoading ? (
        <Spinner />
      ) : !data || data.items.length === 0 ? (
        <EmptyState message="No audit entries match these filters." />
      ) : (
        <>
          <Table<AuditLogResponse>
            keyField="id"
            rows={data.items}
            onRowClick={(row) => setExpandedId(expandedId === row.id ? null : row.id)}
            columns={[
              { key: "timestamp", header: "Timestamp", render: (r) => formatDateTime(r.timestamp) },
              { key: "actorId", header: "Actor", render: (r) => <MonoId value={r.actorId} /> },
              { key: "actorType", header: "Type" },
              { key: "action", header: "Action" },
              { key: "resourceType", header: "Resource" },
              { key: "resourceId", header: "Resource id", render: (r) => <MonoId value={r.resourceId} /> },
            ]}
          />

          {expandedId && (
            <div className="mt-3 rounded-md border border-vault-surface-border bg-vault-ink/30 p-3">
              <div className="mb-1 flex items-center gap-1.5 text-xs text-vault-accent-bright">
                <ShieldCheck className="h-3.5 w-3.5" />
                Metadata — never contains a secret value
              </div>
              <pre className="overflow-x-auto font-mono text-xs text-slate-300">
                {JSON.stringify(JSON.parse(data.items.find((i) => i.id === expandedId)?.metadata ?? "{}"), null, 2)}
              </pre>
            </div>
          )}

          <div className="mt-4 flex items-center justify-between text-sm text-slate-400">
            <span>
              Page {page} of {totalPages} ({data.total} total)
            </span>
            <div className="flex gap-2">
              <Button variant="ghost" size="sm" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
                Previous
              </Button>
              <Button variant="ghost" size="sm" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>
                Next
              </Button>
            </div>
          </div>
        </>
      )}
    </Card>
  );
}
