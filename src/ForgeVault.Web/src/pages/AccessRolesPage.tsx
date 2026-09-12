import { useState } from "react";
import { Card } from "@/components/ui/Card";
import { Input } from "@/components/ui/Input";
import { Button } from "@/components/ui/Button";
import { Table } from "@/components/ui/Table";
import { Badge } from "@/components/ui/Badge";
import { MonoId } from "@/components/ui/MonoId";
import { Spinner } from "@/components/ui/Spinner";
import { EmptyState } from "@/components/ui/EmptyState";
import { GrantRoleForm, type GrantRoleFormValues } from "@/components/roles/GrantRoleForm";
import { formatDateTime } from "@/lib/format";
import { useGrantRole, useRevokeRoleAssignment, useRoleAssignmentsForIdentity } from "@/hooks/useRoleAssignments";
import { ApiError } from "@/lib/api";
import type { RoleAssignmentResponse } from "@/types/api";

export function AccessRolesPage() {
  const [grantError, setGrantError] = useState<string | null>(null);
  const [grantSuccess, setGrantSuccess] = useState(false);
  const [lookupId, setLookupId] = useState("");
  const [activeLookupId, setActiveLookupId] = useState<string | undefined>(undefined);

  const grantRole = useGrantRole();
  const { data: assignments, isLoading: assignmentsLoading } = useRoleAssignmentsForIdentity(activeLookupId);
  const revokeAssignment = useRevokeRoleAssignment(activeLookupId ?? "");

  const onGrant = async (values: GrantRoleFormValues) => {
    setGrantError(null);
    setGrantSuccess(false);
    try {
      await grantRole.mutateAsync(values);
      setGrantSuccess(true);
    } catch (err) {
      setGrantError(err instanceof ApiError ? (err.body?.error ?? err.message) : "Failed to grant role");
    }
  };

  return (
    <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
      <Card title="Grant Role">
        <GrantRoleForm onSubmit={onGrant} isSubmitting={grantRole.isPending} serverError={grantError ?? undefined} />
        {grantSuccess && <p className="mt-2 text-sm text-vault-accent-bright">Role granted.</p>}
      </Card>

      <Card title="Lookup Assignments">
        <form
          onSubmit={(e) => {
            e.preventDefault();
            setActiveLookupId(lookupId);
          }}
          className="mb-4 flex items-end gap-2"
        >
          <div className="flex-1">
            <Input label="Identity id" value={lookupId} onChange={(e) => setLookupId(e.target.value)} placeholder="paste a GUID" />
          </div>
          <Button type="submit">Load</Button>
        </form>

        {!activeLookupId ? (
          <EmptyState message="Paste an identity id and click Load." />
        ) : assignmentsLoading ? (
          <Spinner />
        ) : !assignments || assignments.length === 0 ? (
          <EmptyState message="No role assignments for this identity." />
        ) : (
          <Table<RoleAssignmentResponse>
            keyField="id"
            rows={assignments}
            columns={[
              { key: "role", header: "Role" },
              { key: "scopeType", header: "Scope" },
              { key: "scopeId", header: "Scope id", render: (r) => <MonoId value={r.scopeId} /> },
              { key: "status", header: "Status", render: (r) => <Badge tone={r.status === "active" ? "success" : "danger"}>{r.status}</Badge> },
              { key: "createdAt", header: "Granted", render: (r) => formatDateTime(r.createdAt) },
              {
                key: "actions",
                header: "",
                render: (r) =>
                  r.status === "active" && (
                    <Button size="sm" variant="danger" onClick={() => revokeAssignment.mutate(r.id)} isLoading={revokeAssignment.isPending}>
                      Revoke
                    </Button>
                  ),
              },
            ]}
          />
        )}
      </Card>
    </div>
  );
}
