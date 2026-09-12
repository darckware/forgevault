import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { Button } from "@/components/ui/Button";
import { SECRET_TYPES } from "@/types/api";
import type { SecretType } from "@/types/api";

const schema = z.object({
  name: z.string().min(1, "Required"),
  type: z.enum(SECRET_TYPES),
  provider: z.string().optional(),
  description: z.string().optional(),
  value: z.string().min(1, "Required"),
  expiresAt: z.string().optional(),
});

export type SecretFormValues = z.infer<typeof schema>;

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
    formState: { errors },
  } = useForm<SecretFormValues>({
    resolver: zodResolver(schema),
    defaultValues: { type: "ApiKey" as SecretType },
  });

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-3">
      <Input label="Name" placeholder="OPENAI_API_KEY" {...register("name")} error={errors.name?.message} />
      <Select
        label="Type"
        options={SECRET_TYPES.map((t) => ({ value: t, label: t }))}
        {...register("type")}
        error={errors.type?.message}
      />
      <Input label="Provider (optional)" placeholder="openai" {...register("provider")} />
      <Input label="Description (optional)" {...register("description")} />
      <Input label="Value" type="password" {...register("value")} error={errors.value?.message} />
      <Input label="Expires at (optional)" type="datetime-local" {...register("expiresAt")} />
      {serverError && <p className="text-sm text-vault-danger">{serverError}</p>}
      <Button type="submit" isLoading={isSubmitting}>
        {submitLabel}
      </Button>
    </form>
  );
}
