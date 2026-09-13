import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { Textarea } from "@/components/ui/Textarea";
import { Button } from "@/components/ui/Button";
import { MCP_TRANSPORT_TYPES } from "@/types/api";

const schema = z
  .object({
    name: z.string().min(1, "Required"),
    transport: z.enum(MCP_TRANSPORT_TYPES),
    command: z.string().optional(),
    args: z.string().optional(),
    url: z.string().optional(),
    timeout: z.string().optional(),
    connectTimeout: z.string().optional(),
    staticEnv: z.string().optional(),
    secretParamNames: z.string().optional(),
  })
  .refine((v) => v.transport !== "Stdio" || !!v.command?.trim(), {
    message: "Required for a Stdio server",
    path: ["command"],
  })
  .refine((v) => v.transport !== "Http" || !!v.url?.trim(), {
    message: "Required for an Http server",
    path: ["url"],
  });

export type McpServerFormValues = z.infer<typeof schema>;

export interface McpServerFormSubmitValues {
  name: string;
  transport: (typeof MCP_TRANSPORT_TYPES)[number];
  command?: string;
  args?: string[];
  url?: string;
  timeout?: number;
  connectTimeout?: number;
  staticEnv?: Record<string, string>;
  secretParamNames?: string[];
}

export interface McpServerFormProps {
  onSubmit: (values: McpServerFormSubmitValues) => void;
  isSubmitting?: boolean;
  serverError?: string;
}

// KEY=value, one per line — kept as plain lines rather than a dynamic row-editor to match the
// rest of this design system's preference for simple primitives over bespoke widgets.
function parseEnvLines(text: string | undefined): Record<string, string> | undefined {
  if (!text?.trim()) {
    return undefined;
  }
  const entries = text
    .split("\n")
    .map((line) => line.trim())
    .filter(Boolean)
    .map((line) => {
      const eq = line.indexOf("=");
      return eq === -1 ? [line, ""] : [line.slice(0, eq).trim(), line.slice(eq + 1).trim()];
    });
  return entries.length > 0 ? Object.fromEntries(entries) : undefined;
}

function parseCommaList(text: string | undefined): string[] | undefined {
  if (!text?.trim()) {
    return undefined;
  }
  const items = text
    .split(",")
    .map((s) => s.trim())
    .filter(Boolean);
  return items.length > 0 ? items : undefined;
}

export function McpServerForm({ onSubmit, isSubmitting, serverError }: McpServerFormProps) {
  const {
    register,
    handleSubmit,
    watch,
    formState: { errors },
  } = useForm<McpServerFormValues>({
    resolver: zodResolver(schema),
    defaultValues: { transport: "Stdio" },
  });

  const transport = watch("transport");

  const submit = (values: McpServerFormValues) => {
    onSubmit({
      name: values.name,
      transport: values.transport,
      command: values.transport === "Stdio" ? values.command?.trim() || undefined : undefined,
      args: values.transport === "Stdio" ? parseCommaList(values.args) : undefined,
      url: values.transport === "Http" ? values.url?.trim() || undefined : undefined,
      timeout: values.transport === "Http" && values.timeout ? Number(values.timeout) : undefined,
      connectTimeout: values.transport === "Http" && values.connectTimeout ? Number(values.connectTimeout) : undefined,
      staticEnv: parseEnvLines(values.staticEnv),
      secretParamNames: parseCommaList(values.secretParamNames),
    });
  };

  return (
    <form onSubmit={handleSubmit(submit)} className="flex flex-col gap-3">
      <Input label="Name" placeholder="forgehub-messages" {...register("name")} error={errors.name?.message} />
      <Select
        label="Transport"
        options={MCP_TRANSPORT_TYPES.map((t) => ({ value: t, label: t }))}
        {...register("transport")}
        error={errors.transport?.message}
      />

      {transport === "Stdio" ? (
        <>
          <Input label="Command" placeholder="uv" {...register("command")} error={errors.command?.message} />
          <Input label="Args (comma-separated, optional)" placeholder="run, /path/script.py" {...register("args")} />
        </>
      ) : (
        <>
          <Input label="URL" placeholder="https://forgehub.internal/mcp" {...register("url")} error={errors.url?.message} />
          <div className="grid grid-cols-2 gap-3">
            <Input label="Timeout ms (optional)" type="number" {...register("timeout")} />
            <Input label="Connect timeout ms (optional)" type="number" {...register("connectTimeout")} />
          </div>
        </>
      )}

      <Textarea
        label="Static env (optional)"
        placeholder={"FORGEHUB_API_URL=http://localhost:8000"}
        hint="One KEY=value per line. Never put a sensitive value here — it's shared by every identity assigned this server, unencrypted."
        {...register("staticEnv")}
      />
      <Input
        label="Required secret parameters (comma-separated, optional)"
        placeholder="FORGEHUB_AGENT_TOKEN"
        hint="Names each assignment of this server must supply as a reference to an existing Secret."
        {...register("secretParamNames")}
      />

      {serverError && <p className="text-sm text-vault-danger">{serverError}</p>}
      <Button type="submit" isLoading={isSubmitting}>
        Register server
      </Button>
    </form>
  );
}
