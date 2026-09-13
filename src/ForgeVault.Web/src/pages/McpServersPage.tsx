import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { Plus, Trash2 } from "lucide-react";
import { Card } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { Table } from "@/components/ui/Table";
import { Modal } from "@/components/ui/Modal";
import { Select } from "@/components/ui/Select";
import { Badge } from "@/components/ui/Badge";
import { MonoId } from "@/components/ui/MonoId";
import { Spinner } from "@/components/ui/Spinner";
import { EmptyState } from "@/components/ui/EmptyState";
import { McpServerForm, type McpServerFormSubmitValues } from "@/components/mcp/McpServerForm";
import { formatDateTime } from "@/lib/format";
import { useOrganizations } from "@/hooks/useOrganizations";
import { useCreateMcpServerDefinition, useDeleteMcpServerDefinition, useMcpServerDefinitions } from "@/hooks/useMcpServers";
import { ApiError } from "@/lib/api";
import type { McpServerDefinitionResponse } from "@/types/api";

// Catalog of "servers that exist and how to connect to them", scoped per Organization —
// mirrors the ForgeHub sibling repo's config.yaml mcp_servers: block, but as the single
// source of truth instead of a hand-edited file per agent host.
export function McpServersPage() {
  const { data: organizations, isLoading: organizationsLoading } = useOrganizations();
  const [organizationId, setOrganizationId] = useState("");
  const { data: definitions, isLoading: definitionsLoading } = useMcpServerDefinitions(organizationId || undefined);
  const createDefinition = useCreateMcpServerDefinition(organizationId);
  const deleteDefinition = useDeleteMcpServerDefinition(organizationId);
  const [modalOpen, setModalOpen] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const navigate = useNavigate();

  const onCreate = async (values: McpServerFormSubmitValues) => {
    setServerError(null);
    try {
      await createDefinition.mutateAsync(values);
      setModalOpen(false);
    } catch (err) {
      setServerError(err instanceof ApiError ? (err.body?.error ?? err.message) : "Failed to register server");
    }
  };

  const handleDelete = async (server: McpServerDefinitionResponse) => {
    if (!confirm(`Remove MCP server "${server.name}" from the catalog? Every assignment using it goes with it.`)) {
      return;
    }
    try {
      await deleteDefinition.mutateAsync(server.id);
    } catch (err) {
      alert(err instanceof ApiError ? (err.body?.error ?? err.message) : "Failed to delete");
    }
  };

  return (
    <Card
      title="MCP Servers"
      actions={
        <Button size="sm" onClick={() => setModalOpen(true)} disabled={!organizationId}>
          <Plus className="h-4 w-4" />
          New Server
        </Button>
      }
    >
      <div className="mb-4 max-w-xs">
        <Select
          label="Organization"
          placeholder="— pick an organization —"
          value={organizationId}
          onChange={(e) => setOrganizationId(e.target.value)}
          options={(organizations ?? []).map((o) => ({ value: o.id, label: o.name }))}
          disabled={organizationsLoading}
        />
      </div>

      {!organizationId ? (
        <EmptyState message="Pick an organization to see its MCP server catalog." />
      ) : definitionsLoading ? (
        <Spinner />
      ) : !definitions || definitions.length === 0 ? (
        <EmptyState message="No MCP servers registered for this organization yet." />
      ) : (
        <Table<McpServerDefinitionResponse>
          keyField="id"
          rows={definitions}
          onRowClick={(d) => navigate(`/mcp-servers/${d.id}`)}
          columns={[
            { key: "name", header: "Name" },
            { key: "transport", header: "Transport", render: (d) => <Badge tone="info">{d.transport}</Badge> },
            { key: "target", header: "Command / URL", render: (d) => <span className="font-mono text-xs">{d.command ?? d.url ?? "—"}</span> },
            { key: "id", header: "Definition id", render: (d) => <MonoId value={d.id} /> },
            { key: "createdAt", header: "Registered", render: (d) => formatDateTime(d.createdAt) },
            {
              key: "actions",
              header: "",
              render: (d) => (
                <button
                  onClick={(e) => {
                    e.stopPropagation();
                    handleDelete(d);
                  }}
                  className="text-slate-500 hover:text-vault-danger"
                  aria-label="Delete"
                >
                  <Trash2 className="h-4 w-4" />
                </button>
              ),
            },
          ]}
        />
      )}

      <Modal open={modalOpen} onClose={() => setModalOpen(false)} title="Register MCP Server">
        <McpServerForm onSubmit={onCreate} isSubmitting={createDefinition.isPending} serverError={serverError ?? undefined} />
      </Modal>
    </Card>
  );
}
