import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useNavigate } from "react-router-dom";
import { Plus, Trash2 } from "lucide-react";
import { Card } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { Table } from "@/components/ui/Table";
import { Modal } from "@/components/ui/Modal";
import { Input } from "@/components/ui/Input";
import { Badge } from "@/components/ui/Badge";
import { MonoId } from "@/components/ui/MonoId";
import { Spinner } from "@/components/ui/Spinner";
import { EmptyState } from "@/components/ui/EmptyState";
import { statusTone, formatDateTime } from "@/lib/format";
import { useCreateOrganization, useDeleteOrganization, useOrganizations } from "@/hooks/useOrganizations";
import { ApiError } from "@/lib/api";
import type { OrganizationResponse } from "@/types/api";

const schema = z.object({ name: z.string().min(1, "Required"), slug: z.string().min(1, "Required") });
type FormValues = z.infer<typeof schema>;

export function OrganizationsPage() {
  const { data: organizations, isLoading } = useOrganizations();
  const createOrg = useCreateOrganization();
  const deleteOrg = useDeleteOrganization();
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
      await createOrg.mutateAsync(values);
      setModalOpen(false);
      reset();
    } catch (err) {
      setServerError(err instanceof ApiError ? err.body?.error ?? err.message : "Failed to create");
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await deleteOrg.mutateAsync(id);
    } catch (err) {
      alert(err instanceof ApiError ? (err.body?.error ?? err.message) : "Failed to delete");
    }
  };

  return (
    <Card
      title="Organizations"
      actions={
        <Button size="sm" onClick={() => setModalOpen(true)}>
          <Plus className="h-4 w-4" />
          New Organization
        </Button>
      }
    >
      {isLoading ? (
        <Spinner />
      ) : !organizations || organizations.length === 0 ? (
        <EmptyState message="No organizations yet." />
      ) : (
        <Table<OrganizationResponse>
          keyField="id"
          rows={organizations}
          onRowClick={(org) => navigate(`/organizations/${org.id}`)}
          columns={[
            { key: "name", header: "Name" },
            { key: "slug", header: "Slug", render: (o) => <MonoId value={o.slug} copyable={false} truncate={false} /> },
            { key: "status", header: "Status", render: (o) => <Badge tone={statusTone(o.status)}>{o.status}</Badge> },
            { key: "createdAt", header: "Created", render: (o) => formatDateTime(o.createdAt) },
            {
              key: "actions",
              header: "",
              render: (o) => (
                <button
                  onClick={(e) => {
                    e.stopPropagation();
                    if (confirm(`Delete organization "${o.name}"?`)) {
                      handleDelete(o.id);
                    }
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

      <Modal open={modalOpen} onClose={() => setModalOpen(false)} title="New Organization">
        <form onSubmit={handleSubmit(onCreate)} className="flex flex-col gap-3">
          <Input label="Name" {...register("name")} error={errors.name?.message} />
          <Input label="Slug" {...register("slug")} error={errors.slug?.message} />
          {serverError && <p className="text-sm text-vault-danger">{serverError}</p>}
          <Button type="submit" isLoading={createOrg.isPending}>
            Create
          </Button>
        </form>
      </Modal>
    </Card>
  );
}
