import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Plus } from "lucide-react";
import { Card } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { Table } from "@/components/ui/Table";
import { Modal } from "@/components/ui/Modal";
import { Input } from "@/components/ui/Input";
import { Badge } from "@/components/ui/Badge";
import { MonoId } from "@/components/ui/MonoId";
import { Spinner } from "@/components/ui/Spinner";
import { EmptyState } from "@/components/ui/EmptyState";
import { formatDateTime } from "@/lib/format";
import { useCreateUser, useDeleteUser, useSetUserActive, useUpdateUser, useUsers } from "@/hooks/useUsers";
import { useMe } from "@/hooks/useAuth";
import { ApiError } from "@/lib/api";
import type { UserResponse } from "@/types/api";

const schema = z.object({
  email: z.string().email("Invalid email"),
  password: z.string().min(8, "At least 8 characters"),
  username: z.string().optional(),
  firstName: z.string().optional(),
  lastName: z.string().optional(),
});
type FormValues = z.infer<typeof schema>;

export function UsersPage() {
  const { data: users, isLoading } = useUsers();
  const { data: me } = useMe();
  const createUser = useCreateUser();
  const setActive = useSetUserActive();
  const updateUser = useUpdateUser();
  const deleteUser = useDeleteUser();
  const [modalOpen, setModalOpen] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);

  const onDelete = (u: UserResponse) => {
    const label = [u.firstName, u.lastName].filter(Boolean).join(" ") || u.email;
    if (confirm(`Permanently delete "${label}"? This cannot be undone — use Deactivate instead if you just want to revoke access.`)) {
      deleteUser.mutate(u.id);
    }
  };

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  const onCreate = async (values: FormValues) => {
    setServerError(null);
    try {
      await createUser.mutateAsync(values);
      setModalOpen(false);
      reset();
    } catch (err) {
      setServerError(err instanceof ApiError ? (err.body?.error ?? err.message) : "Failed to create user");
    }
  };

  return (
    <Card
      title="Users"
      actions={
        <Button size="sm" onClick={() => setModalOpen(true)}>
          <Plus className="h-4 w-4" />
          New User
        </Button>
      }
    >
      <p className="mb-4 text-sm text-slate-500">
        Human accounts. Access to Organizations/Projects/Environments is granted separately in{" "}
        <span className="font-medium text-slate-300">Access & Roles</span> — creating a user here only gives them a login.
        The "Admin" badge is cosmetic (a display label) — it never bypasses RBAC; real access always goes through Access &
        Roles.
      </p>

      {isLoading ? (
        <Spinner />
      ) : !users || users.length === 0 ? (
        <EmptyState message="No users yet." />
      ) : (
        <Table<UserResponse>
          keyField="id"
          rows={users}
          columns={[
            {
              key: "email",
              header: "Name / Email",
              render: (u) => (
                <div>
                  <p className="text-slate-100">{[u.firstName, u.lastName].filter(Boolean).join(" ") || u.email}</p>
                  <p className="text-xs text-slate-500">{u.email}{u.username && ` · @${u.username}`}</p>
                </div>
              ),
            },
            { key: "id", header: "Identity id", render: (u) => <MonoId value={u.id} /> },
            { key: "mfaEnabled", header: "MFA", render: (u) => <Badge tone={u.mfaEnabled ? "success" : "neutral"}>{u.mfaEnabled ? "On" : "Off"}</Badge> },
            { key: "isActive", header: "Status", render: (u) => <Badge tone={u.isActive ? "success" : "danger"}>{u.isActive ? "Active" : "Deactivated"}</Badge> },
            { key: "isAdmin", header: "Admin", render: (u) => (u.isAdmin ? <Badge tone="success">Admin</Badge> : null) },
            { key: "createdAt", header: "Created", render: (u) => formatDateTime(u.createdAt) },
            {
              key: "actions",
              header: "",
              render: (u) => (
                <div className="flex justify-end gap-2">
                  <Button
                    size="sm"
                    variant="secondary"
                    isLoading={updateUser.isPending}
                    onClick={() => updateUser.mutate({ id: u.id, isAdmin: !u.isAdmin })}
                  >
                    {u.isAdmin ? "Remove admin badge" : "Grant admin badge"}
                  </Button>
                  {u.id !== me?.id && (
                    <>
                      <Button
                        size="sm"
                        variant={u.isActive ? "danger" : "secondary"}
                        isLoading={setActive.isPending}
                        onClick={() => setActive.mutate({ id: u.id, active: !u.isActive })}
                      >
                        {u.isActive ? "Deactivate" : "Reactivate"}
                      </Button>
                      <Button size="sm" variant="danger" isLoading={deleteUser.isPending} onClick={() => onDelete(u)}>
                        Delete
                      </Button>
                    </>
                  )}
                </div>
              ),
            },
          ]}
        />
      )}

      <Modal open={modalOpen} onClose={() => setModalOpen(false)} title="New User">
        <form onSubmit={handleSubmit(onCreate)} className="flex flex-col gap-3">
          <Input label="Email" type="email" placeholder="dev@example.com" {...register("email")} error={errors.email?.message} />
          <Input label="Initial password" type="password" {...register("password")} error={errors.password?.message} />
          <div className="flex gap-2">
            <Input label="First name (optional)" {...register("firstName")} />
            <Input label="Last name (optional)" {...register("lastName")} />
          </div>
          <Input label="Username (optional)" placeholder="jdoe" {...register("username")} />
          <p className="text-xs text-slate-500">Share this password with the user out of band — they can change it after logging in.</p>
          {serverError && <p className="text-sm text-vault-danger">{serverError}</p>}
          <Button type="submit" isLoading={createUser.isPending}>
            Create
          </Button>
        </form>
      </Modal>
    </Card>
  );
}
