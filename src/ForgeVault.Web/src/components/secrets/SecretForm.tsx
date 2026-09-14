import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { Button } from "@/components/ui/Button";
import { SECRET_TYPES } from "@/types/api";
import type { SecretType } from "@/types/api";
import { SECRET_TYPE_LABELS, STRUCTURED_FIELD_DEFS, encodeStructuredValue, isStructuredSecretType } from "@/lib/secretValue";

const schema = z.object({
  name: z.string().min(1, "Required"),
  type: z.enum(SECRET_TYPES),
  provider: z.string().optional(),
  description: z.string().optional(),
  value: z.string().optional(),
  structuredFields: z.record(z.string(), z.string()).optional(),
  expiresAt: z.string().optional(),
});

type RawFormValues = z.infer<typeof schema>;

export interface SecretFormValues {
  name: string;
  type: SecretType;
  provider?: string;
  description?: string;
  value: string;
  expiresAt?: string;
}

export interface SecretFormProps {
  onSubmit: (values: SecretFormValues) => void;
  isSubmitting?: boolean;
  submitLabel?: string;
  serverError?: string;
}

export function SecretForm({ onSubmit, isSubmitting, submitLabel = "Create secret", serverError }: SecretFormProps) {
  const {
    register,
    handleSubmit,
    watch,
    formState: { errors },
  } = useForm<RawFormValues>({
    resolver: zodResolver(schema),
    defaultValues: { type: "ApiKey" as SecretType },
  });

  const selectedType = watch("type");
  const structured = isStructuredSecretType(selectedType) ? STRUCTURED_FIELD_DEFS[selectedType] : null;

  const submit = handleSubmit((raw) => {
    const value = structured
      ? encodeStructuredValue(selectedType as "Password" | "DatabaseCredential", raw.structuredFields ?? {})
      : (raw.value ?? "");

    onSubmit({
      name: raw.name,
      type: raw.type,
      provider: raw.provider,
      description: raw.description,
      value,
      expiresAt: raw.expiresAt,
    });
  });

  return (
    <form onSubmit={submit} className="flex flex-col gap-3">
      <Input label="Name" placeholder="OPENAI_API_KEY" {...register("name")} error={errors.name?.message} />
      <Select
        label="Type"
        options={SECRET_TYPES.map((t) => ({ value: t, label: SECRET_TYPE_LABELS[t] }))}
        {...register("type")}
        error={errors.type?.message}
      />
      <Input label="Provider (optional)" placeholder="openai" {...register("provider")} />
      <Input label="Description (optional)" {...register("description")} />

      {structured ? (
        <div className="flex flex-col gap-3 rounded-md border border-vault-surface-border p-3">
          <p className="text-xs text-slate-500">
            {selectedType === "Password" ? "Site login" : "Database connection"} — stored as one encrypted credential.
          </p>
          {structured.map((field) => (
            <Input
              key={field.key}
              label={field.label}
              placeholder={field.placeholder}
              type={field.sensitive ? "password" : "text"}
              {...register(`structuredFields.${field.key}`, { required: true })}
            />
          ))}
        </div>
      ) : (
        <Input label="Value" type="password" {...register("value", { required: true })} error={errors.value?.message} />
      )}

      <Input label="Expires at (optional)" type="datetime-local" {...register("expiresAt")} />
      {serverError && <p className="text-sm text-vault-danger">{serverError}</p>}
      <Button type="submit" isLoading={isSubmitting}>
        {submitLabel}
      </Button>
    </form>
  );
}
