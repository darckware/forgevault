import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { Button } from "@/components/ui/Button";
import { useServiceAccounts } from "@/hooks/useServiceAccounts";
import { ROLES, ROLE_SCOPE_TYPES } from "@/types/api";

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const schema = z.object({
  identityId: z.string().regex(UUID_RE, "Must be a valid GUID"),
  role: z.enum(ROLES),
  scopeType: z.enum(ROLE_SCOPE_TYPES),
  scopeId: z.string().regex(UUID_RE, "Must be a valid GUID"),
});

export type GrantRoleFormValues = z.infer<typeof schema>;

export interface GrantRoleFormProps {
  onSubmit: (values: GrantRoleFormValues) => void;
  isSubmitting?: boolean;
  serverError?: string;
}

// No identity-search endpoint exists in the API — Service Accounts are the one identity
// source that IS listable, so picking one auto-fills its id; a human User's id must still be
// pasted by hand (e.g. copied from /me or found in the audit log).
export function GrantRoleForm({ onSubmit, isSubmitting, serverError }: GrantRoleFormProps) {
  const { data: serviceAccounts } = useServiceAccounts();
  const [selectedAccountId, setSelectedAccountId] = useState("");

  const {
    register,
    handleSubmit,
    setValue,
    formState: { errors },
  } = useForm<GrantRoleFormValues>({
    resolver: zodResolver(schema),
    defaultValues: { role: "Agent", scopeType: "Environment" },
  });

  const handlePickServiceAccount = (id: string) => {
    setSelectedAccountId(id);
    if (id) {
      setValue("identityId", id, { shouldValidate: true });
    }
  };

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-3">
      <Select
        label="Pick a Service Account (optional)"
        placeholder="— paste an identity id manually instead —"
        value={selectedAccountId}
        onChange={(e) => handlePickServiceAccount(e.target.value)}
        options={(serviceAccounts ?? []).map((sa) => ({ value: sa.id, label: sa.name }))}
      />
      <Input
        label="Identity id"
        placeholder="a User or ServiceAccount GUID"
        hint="Don't know a user's ID? Ask them to copy it from /me, or find it in the audit log."
        {...register("identityId")}
        error={errors.identityId?.message}
      />
      <Select label="Role" options={ROLES.map((r) => ({ value: r, label: r }))} {...register("role")} error={errors.role?.message} />
      <Select
        label="Scope type"
        options={ROLE_SCOPE_TYPES.map((s) => ({ value: s, label: s }))}
        {...register("scopeType")}
        error={errors.scopeType?.message}
      />
      <Input
        label="Scope id"
        placeholder="the Organization/Project/Environment id matching the scope type above"
        {...register("scopeId")}
        error={errors.scopeId?.message}
      />
      {serverError && <p className="text-sm text-vault-danger">{serverError}</p>}
      <Button type="submit" isLoading={isSubmitting}>
        Grant role
      </Button>
    </form>
  );
}
