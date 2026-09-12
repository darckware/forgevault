import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useNavigate } from "react-router-dom";
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
import { useCreateServiceAccount, useServiceAccounts } from "@/hooks/useServiceAccounts";
import { ApiError } from "@/lib/api";
import type { ServiceAccountResponse } from "@/types/api";

const schema = z.object({ name: z.string().min(1, "Required") });
type FormValues = z.infer<typeof schema>;

export function ServiceAccountsPage() {
  const { data: accounts, isLoading } = useServiceAccounts();
  const createAccount = useCreateServiceAccount();
  const [modalOpen, setModalOpen] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const navigate = useNavigate();

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  const onCreate = async (values: FormValues) => {
    setServerError(null);
    try {
      await createAccount.mutateAsync(values);
      setModalOpen(false);
      reset();
    } catch (err) {
      setServerError(err instanceof ApiError ? (err.body?.error ?? err.message) : "Failed to create");
    }
  };

  return (
    <Card
      title="Service Accounts"
      actions={
        <Button size="sm" onClick={() => setModalOpen(true)}>
          <Plus className="h-4 w-4" />
          New Service Account
        </Button>
      }
    >
      {isLoading ? (
        <Spinner />
      ) : !accounts || accounts.length === 0 ? (
        <EmptyState message="No service accounts yet." />
      ) : (
        <Table<ServiceAccountResponse>
          keyField="id"
          rows={accounts}
          onRowClick={(a) => navigate(`/service-accounts/${a.id}`)}
          columns={[
            { key: "name", header: "Name" },
            { key: "id", header: "Identity id", render: (a) => <MonoId value={a.id} /> },
            { key: "isActive", header: "Status", render: (a) => <Badge tone={a.isActive ? "success" : "neutral"}>{a.isActive ? "Active" : "Inactive"}</Badge> },
            { key: "createdAt", header: "Created", render: (a) => formatDateTime(a.createdAt) },
          ]}
        />
      )}

      <Modal open={modalOpen} onClose={() => setModalOpen(false)} title="New Service Account">
        <form onSubmit={handleSubmit(onCreate)} className="flex flex-col gap-3">
          <Input label="Name" placeholder="agent-athos" {...register("name")} error={errors.name?.message} />
          {serverError && <p className="text-sm text-vault-danger">{serverError}</p>}
          <Button type="submit" isLoading={createAccount.isPending}>
            Create
          </Button>
        </form>
      </Modal>
    </Card>
  );
}
