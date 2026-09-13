import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { Textarea } from "@/components/ui/Textarea";
import { Button } from "@/components/ui/Button";
import { useOrganizations } from "@/hooks/useOrganizations";
import { useMcpServerDefinitions } from "@/hooks/useMcpServers";
import type { McpParamValue } from "@/types/api";

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const schema = z.object({
  identityId: z.string().regex(UUID_RE, "Must be a valid GUID"),
  organizationId: z.string().min(1, "Pick an organization"),
  mcpServerDefinitionId: z.string().min(1, "Pick a server"),
  paramValues: z.string().optional(),
});

type FormValues = z.infer<typeof schema>;

export interface GrantMcpAssignmentSubmitValues {
  identityId: string;
  mcpServerDefinitionId: string;
  paramValues: Record<string, McpParamValue>;
}

export interface GrantMcpAssignmentFormProps {
  onSubmit: (values: GrantMcpAssignmentSubmitValues) => void;
  isSubmitting?: boolean;
  serverError?: string;
}

// KEY=value for a literal, KEY=secret:<secretId> for a reference resolved (decrypted) only at
// render time — same one-line-per-entry convention as McpServerForm's static env, kept
// consistent rather than building a bespoke per-row literal/secret toggle widget.
function parseParamValues(text: string | undefined): Record<string, McpParamValue> {
  if (!text?.trim()) {
    return {};
  }
  const result: Record<string, McpParamValue> = {};
  for (const rawLine of text.split("\n")) {
    const line = rawLine.trim();
    if (!line) {
      continue;
    }
    const eq = line.indexOf("=");
    if (eq === -1) {
      continue;
    }
    const key = line.slice(0, eq).trim();
    const rawValue = line.slice(eq + 1).trim();
    result[key] = rawValue.startsWith("secret:") ? { secretId: rawValue.slice("secret:".length).trim() } : rawValue;
  }
  return result;
}

export function GrantMcpAssignmentForm({ onSubmit, isSubmitting, serverError }: GrantMcpAssignmentFormProps) {
  const { data: organizations } = useOrganizations();
  const [organizationId, setOrganizationId] = useState("");
  const { data: definitions } = useMcpServerDefinitions(organizationId || undefined);

  const {
    register,
    handleSubmit,
    setValue,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  const handlePickOrganization = (id: string) => {
    setOrganizationId(id);
    setValue("organizationId", id, { shouldValidate: true });
    setValue("mcpServerDefinitionId", "", { shouldValidate: true });
  };

  const submit = (values: FormValues) => {
    onSubmit({
      identityId: values.identityId,
      mcpServerDefinitionId: values.mcpServerDefinitionId,
      paramValues: parseParamValues(values.paramValues),
    });
  };

  return (
    <form onSubmit={handleSubmit(submit)} className="flex flex-col gap-3">
      <Input
        label="Identity id"
        placeholder="a User or ServiceAccount GUID"
        hint="The identity must already hold at least one active role assignment somewhere."
        {...register("identityId")}
        error={errors.identityId?.message}
      />
      <Select
        label="Organization"
        placeholder="— pick an organization —"
        value={organizationId}
        onChange={(e) => handlePickOrganization(e.target.value)}
        options={(organizations ?? []).map((o) => ({ value: o.id, label: o.name }))}
        error={errors.organizationId?.message}
      />
      <Select
        label="MCP server"
        placeholder={organizationId ? "— pick a server —" : "pick an organization first"}
        disabled={!organizationId}
        options={(definitions ?? []).map((d) => ({ value: d.id, label: `${d.name} (${d.transport})` }))}
        {...register("mcpServerDefinitionId")}
        error={errors.mcpServerDefinitionId?.message}
      />
      <Textarea
        label="Parameter values (optional)"
        placeholder={"FORGEHUB_AGENT_TOKEN=secret:3fa85f64-...\nAGENT_SLUG=athos"}
        hint="One KEY=value per line. Use KEY=secret:<secretId> to reference an existing Secret — never paste a raw value here for anything sensitive."
        {...register("paramValues")}
      />
      {serverError && <p className="text-sm text-vault-danger">{serverError}</p>}
      <Button type="submit" isLoading={isSubmitting}>
        Grant access
      </Button>
    </form>
  );
}
