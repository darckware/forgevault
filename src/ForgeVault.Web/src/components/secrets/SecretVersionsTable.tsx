import { Table } from "@/components/ui/Table";
import { MonoId } from "@/components/ui/MonoId";
import { Spinner } from "@/components/ui/Spinner";
import { formatDateTime } from "@/lib/format";
import { useSecretVersions } from "@/hooks/useSecrets";
import type { SecretVersionResponse } from "@/types/api";

export function SecretVersionsTable({ secretId }: { secretId: string }) {
  const { data: versions, isLoading } = useSecretVersions(secretId);

  if (isLoading) {
    return <Spinner />;
  }

  return (
    <div className="flex flex-col gap-2">
      <p className="text-xs text-slate-500">
        Version history is metadata-only — no ciphertext or plaintext is ever listed here. Use Reveal for the current
        value.
      </p>
      <Table<SecretVersionResponse>
        keyField="id"
        rows={versions ?? []}
        emptyLabel="No versions yet."
        columns={[
          { key: "version", header: "Version", render: (v) => `#${v.version}` },
          { key: "algorithm", header: "Algorithm", render: (v) => <span className="font-mono text-xs">{v.algorithm}</span> },
          { key: "createdBy", header: "Created by", render: (v) => <MonoId value={v.createdBy} /> },
          { key: "createdAt", header: "Created at", render: (v) => formatDateTime(v.createdAt) },
        ]}
      />
    </div>
  );
}
