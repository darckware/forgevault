import { useState } from "react";
import { Card } from "@/components/ui/Card";
import { Input } from "@/components/ui/Input";
import { Button } from "@/components/ui/Button";
import { Table } from "@/components/ui/Table";
import { Badge } from "@/components/ui/Badge";
import { MonoId } from "@/components/ui/MonoId";
import { Spinner } from "@/components/ui/Spinner";
import { EmptyState } from "@/components/ui/EmptyState";
import { GrantMcpAssignmentForm, type GrantMcpAssignmentSubmitValues } from "@/components/mcp/GrantMcpAssignmentForm";
import { RenderMcpConfigButton } from "@/components/mcp/RenderMcpConfigButton";
import { formatDateTime } from "@/lib/format";
import { useGrantMcpAssignment, useMcpAssignmentsForIdentity, useRevokeMcpAssignment } from "@/hooks/useMcpAssignments";
import { ApiError } from "@/lib/api";
import type { McpServerAssignmentResponse } from "@/types/api";

// Which identity is wired to which MCP server, with what parameters — the assignment side of
// the registry. No identity-search endpoint exists (same limitation as Access & Roles), so
// lookup is by pasted identity id, mirroring AccessRolesPage's pattern.
export function McpAssignmentsPage() {
  const [grantError, setGrantError] = useState<string | null>(null);
  const [grantSuccess, setGrantSuccess] = useState(false);
  const [lookupId, setLookupId] = useState("");
  const [activeLookupId, setActiveLookupId] = useState<string | undefined>(undefined);

  const grantAssignment = useGrantMcpAssignment();
  const { data: assignments, isLoading: assignmentsLoading } = useMcpAssignmentsForIdentity(activeLookupId);
  const revokeAssignment = useRevokeMcpAssignment(activeLookupId ?? "");

  const onGrant = async (values: GrantMcpAssignmentSubmitValues) => {
    setGrantError(null);
    setGrantSuccess(false);
    try {
      await grantAssignment.mutateAsync(values);
      setGrantSuccess(true);
      if (values.identityId === activeLookupId) {
        setActiveLookupId(values.identityId);
      }
    } catch (err) {
      setGrantError(err instanceof ApiError ? (err.body?.error ?? err.message) : "Failed to grant access");
    }
  };

  return (
    <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
      <Card title="Grant MCP Access">
        <GrantMcpAssignmentForm onSubmit={onGrant} isSubmitting={grantAssignment.isPending} serverError={grantError ?? undefined} />
        {grantSuccess && <p className="mt-2 text-sm text-vault-accent-bright">Access granted.</p>}
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
            <Input
              id="mcp-assignment-lookup-identity-id"
              label="Identity id"
              value={lookupId}
              onChange={(e) => setLookupId(e.target.value)}
              placeholder="paste a GUID"
            />
          </div>
          <Button type="submit">Load</Button>
        </form>

        {!activeLookupId ? (
          <EmptyState message="Paste an identity id and click Load." />
        ) : assignmentsLoading ? (
          <Spinner />
        ) : !assignments || assignments.length === 0 ? (
          <EmptyState message="No MCP server assignments for this identity." />
        ) : (
          <Table<McpServerAssignmentResponse>
            keyField="id"
            rows={assignments}
            columns={[
              { key: "mcpServerDefinitionId", header: "Server", render: (a) => <MonoId value={a.mcpServerDefinitionId} /> },
              { key: "status", header: "Status", render: (a) => <Badge tone={a.status === "active" ? "success" : "danger"}>{a.status}</Badge> },
              { key: "createdAt", header: "Granted", render: (a) => formatDateTime(a.createdAt) },
              {
                key: "actions",
                header: "",
                render: (a) => (
                  <div className="flex items-center gap-2">
                    <RenderMcpConfigButton assignmentId={a.id} />
                    {a.status === "active" && (
                      <Button size="sm" variant="danger" onClick={() => revokeAssignment.mutate(a.id)} isLoading={revokeAssignment.isPending}>
                        Revoke
                      </Button>
                    )}
                  </div>
                ),
              },
            ]}
          />
        )}
      </Card>
    </div>
  );
}
