import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useNavigate, useParams } from "react-router-dom";
import { Plus } from "lucide-react";
import { Card } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { Table } from "@/components/ui/Table";
import { Modal } from "@/components/ui/Modal";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { Badge } from "@/components/ui/Badge";
import { Spinner } from "@/components/ui/Spinner";
import { EmptyState } from "@/components/ui/EmptyState";
import { Breadcrumbs } from "@/components/layout/Breadcrumbs";
import { statusTone, environmentKindTone, formatDateTime } from "@/lib/format";
import { useProject } from "@/hooks/useProjects";
import { useCreateEnvironment, useEnvironments } from "@/hooks/useEnvironments";
import { ApiError } from "@/lib/api";
import type { EnvironmentResponse } from "@/types/api";

const ENVIRONMENT_KINDS = ["Development", "Staging", "Production", "Shared"] as const;
const schema = z.object({ name: z.enum(ENVIRONMENT_KINDS), slug: z.string().min(1, "Required") });
type FormValues = z.infer<typeof schema>;

export function ProjectDetailPage() {
  const { projectId } = useParams<{ projectId: string }>();
  const { data: project, isLoading: projectLoading } = useProject(projectId);
  const { data: environments, isLoading: envsLoading } = useEnvironments(projectId);
  const createEnvironment = useCreateEnvironment(projectId!);
  const [modalOpen, setModalOpen] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const navigate = useNavigate();

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { name: "Development" } });

  const onCreate = async (values: FormValues) => {
    setServerError(null);
    try {
      await createEnvironment.mutateAsync(values);
      setModalOpen(false);
      reset();
    } catch (err) {
      setServerError(err instanceof ApiError ? (err.body?.error ?? err.message) : "Failed to create");
    }
  };

  if (projectLoading || !project) {
    return <Spinner />;
  }

  return (
    <div className="flex flex-col gap-6">
      <Breadcrumbs
        items={[
          { label: "Organizations", to: "/organizations" },
          { label: "Organization", to: `/organizations/${project.organizationId}` },
          { label: project.name },
        ]}
      />

      <Card title="Project">
        <div className="flex items-center gap-4">
          <span className="text-lg font-medium text-slate-100">{project.name}</span>
          <Badge tone={statusTone(project.status)}>{project.status}</Badge>
        </div>
        <p className="mt-1 text-sm text-slate-500">Slug: {project.slug}</p>
        {project.description && <p className="mt-1 text-sm text-slate-400">{project.description}</p>}
      </Card>

      <Card
        title="Environments"
        actions={
          <Button size="sm" onClick={() => setModalOpen(true)}>
            <Plus className="h-4 w-4" />
            New Environment
          </Button>
        }
      >
        {envsLoading ? (
          <Spinner />
        ) : !environments || environments.length === 0 ? (
          <EmptyState message="No environments yet." />
        ) : (
          <Table<EnvironmentResponse>
            keyField="id"
            rows={environments}
            onRowClick={(e) => navigate(`/environments/${e.id}`)}
            columns={[
              { key: "name", header: "Kind", render: (e) => <Badge tone={environmentKindTone(e.name)}>{e.name}</Badge> },
              { key: "slug", header: "Slug" },
              { key: "createdAt", header: "Created", render: (e) => formatDateTime(e.createdAt) },
            ]}
          />
        )}

        <Modal open={modalOpen} onClose={() => setModalOpen(false)} title="New Environment">
          <form onSubmit={handleSubmit(onCreate)} className="flex flex-col gap-3">
            <Select
              label="Kind"
              options={ENVIRONMENT_KINDS.map((k) => ({ value: k, label: k }))}
              {...register("name")}
              error={errors.name?.message}
            />
            <Input label="Slug" {...register("slug")} error={errors.slug?.message} />
            {serverError && <p className="text-sm text-vault-danger">{serverError}</p>}
            <Button type="submit" isLoading={createEnvironment.isPending}>
              Create
            </Button>
          </form>
        </Modal>
      </Card>
    </div>
  );
}
