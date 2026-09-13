import { useNavigate, useParams } from "react-router-dom";
import { Trash2 } from "lucide-react";
import { Card } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { Badge } from "@/components/ui/Badge";
import { MonoId } from "@/components/ui/MonoId";
import { Spinner } from "@/components/ui/Spinner";
import { Breadcrumbs } from "@/components/layout/Breadcrumbs";
import { formatDateTime } from "@/lib/format";
import { useDeleteMcpServerDefinition, useMcpServerDefinition } from "@/hooks/useMcpServers";
import { ApiError } from "@/lib/api";

export function McpServerDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { data: definition, isLoading } = useMcpServerDefinition(id);
  const deleteDefinition = useDeleteMcpServerDefinition(definition?.organizationId ?? "");
  const navigate = useNavigate();

  const handleDelete = async () => {
    if (!definition || !confirm(`Remove MCP server "${definition.name}" from the catalog? Every assignment using it goes with it.`)) {
      return;
    }
    try {
      await deleteDefinition.mutateAsync(definition.id);
      navigate("/mcp-servers");
    } catch (err) {
      alert(err instanceof ApiError ? (err.body?.error ?? err.message) : "Failed to delete");
    }
  };

  if (isLoading || !definition) {
    return <Spinner />;
  }

  return (
    <div className="flex flex-col gap-6">
      <Breadcrumbs items={[{ label: "MCP Servers", to: "/mcp-servers" }, { label: definition.name }]} />

      <Card
        title="MCP Server"
        actions={
          <Button size="sm" variant="danger" onClick={handleDelete} isLoading={deleteDefinition.isPending}>
            <Trash2 className="h-3.5 w-3.5" />
            Remove
          </Button>
        }
      >
        <div className="flex items-center gap-3">
          <span className="text-lg font-medium text-slate-100">{definition.name}</span>
          <Badge tone="info">{definition.transport}</Badge>
        </div>
        <div className="mt-3 grid grid-cols-1 gap-x-6 gap-y-2 text-sm sm:grid-cols-2">
          <MonoId label="Definition id" value={definition.id} truncate={false} />
          <MonoId label="Organization id" value={definition.organizationId} truncate={false} />
          {definition.command && (
            <div>
              <span className="text-xs text-slate-500">Command</span>
              <p className="font-mono text-sm text-slate-200">{definition.command}</p>
            </div>
          )}
          {definition.args && definition.args.length > 0 && (
            <div>
              <span className="text-xs text-slate-500">Args</span>
              <p className="font-mono text-sm text-slate-200">{definition.args.join(" ")}</p>
            </div>
          )}
          {definition.url && (
            <div>
              <span className="text-xs text-slate-500">URL</span>
              <p className="font-mono text-sm text-slate-200">{definition.url}</p>
            </div>
          )}
          {(definition.timeout || definition.connectTimeout) && (
            <div>
              <span className="text-xs text-slate-500">Timeouts</span>
              <p className="font-mono text-sm text-slate-200">
                {definition.timeout ? `timeout=${definition.timeout}ms` : ""} {definition.connectTimeout ? `connect=${definition.connectTimeout}ms` : ""}
              </p>
            </div>
          )}
          <div>
            <span className="text-xs text-slate-500">Registered</span>
            <p className="text-sm text-slate-200">{formatDateTime(definition.createdAt)}</p>
          </div>
        </div>
      </Card>

      {definition.staticEnv && Object.keys(definition.staticEnv).length > 0 && (
        <Card title="Static env (shared, non-sensitive)">
          <pre className="overflow-auto rounded-md bg-vault-ink/40 p-3 font-mono text-xs text-slate-300">
            {Object.entries(definition.staticEnv)
              .map(([k, v]) => `${k}=${v}`)
              .join("\n")}
          </pre>
        </Card>
      )}

      {definition.secretParamNames && definition.secretParamNames.length > 0 && (
        <Card title="Required secret parameters">
          <div className="flex flex-wrap gap-2">
            {definition.secretParamNames.map((name) => (
              <Badge key={name} tone="warning">
                {name}
              </Badge>
            ))}
          </div>
          <p className="mt-2 text-xs text-slate-500">
            Every identity assigned this server must supply these as a reference to an existing Secret — see Access & MCP.
          </p>
        </Card>
      )}
    </div>
  );
}
