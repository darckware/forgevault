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
import { Badge } from "@/components/ui/Badge";
import { Spinner } from "@/components/ui/Spinner";
import { EmptyState } from "@/components/ui/EmptyState";
import { Breadcrumbs } from "@/components/layout/Breadcrumbs";
import { statusTone, formatDateTime } from "@/lib/format";
import { useOrganization } from "@/hooks/useOrganizations";
import { useCreateProject, useProjects } from "@/hooks/useProjects";
import { ApiError } from "@/lib/api";
import type { ProjectResponse } from "@/types/api";

const schema = z.object({
  name: z.string().min(1, "Required"),
  slug: z.string().min(1, "Required"),
  description: z.string().optional(),
});
type FormValues = z.infer<typeof schema>;

export function OrganizationDetailPage() {
  const { orgId } = useParams<{ orgId: string }>();
  const { data: organization, isLoading: orgLoading } = useOrganization(orgId);
  const { data: projects, isLoading: projectsLoading } = useProjects(orgId);
  const createProject = useCreateProject(orgId!);
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
      await createProject.mutateAsync(values);
      setModalOpen(false);
      reset();
    } catch (err) {
      setServerError(err instanceof ApiError ? (err.body?.error ?? err.message) : "Failed to create");
    }
  };

  if (orgLoading || !organization) {
    return <Spinner />;
  }

  return (
    <div className="flex flex-col gap-6">
      <Breadcrumbs items={[{ label: "Organizations", to: "/organizations" }, { label: organization.name }]} />

      <Card title="Organization">
        <div className="flex items-center gap-4">
          <span className="text-lg font-medium text-slate-100">{organization.name}</span>
          <Badge tone={statusTone(organization.status)}>{organization.status}</Badge>
        </div>
        <p className="mt-1 text-sm text-slate-500">Slug: {organization.slug}</p>
      </Card>

      <Card
        title="Projects"
        actions={
          <Button size="sm" onClick={() => setModalOpen(true)}>
            <Plus className="h-4 w-4" />
            New Project
          </Button>
        }
      >
        {projectsLoading ? (
          <Spinner />
        ) : !projects || projects.length === 0 ? (
          <EmptyState message="No projects yet." />
        ) : (
          <Table<ProjectResponse>
            keyField="id"
            rows={projects}
            onRowClick={(p) => navigate(`/projects/${p.id}`)}
            columns={[
              { key: "name", header: "Name" },
              { key: "slug", header: "Slug" },
              { key: "status", header: "Status", render: (p) => <Badge tone={statusTone(p.status)}>{p.status}</Badge> },
              { key: "createdAt", header: "Created", render: (p) => formatDateTime(p.createdAt) },
            ]}
          />
        )}

        <Modal open={modalOpen} onClose={() => setModalOpen(false)} title="New Project">
          <form onSubmit={handleSubmit(onCreate)} className="flex flex-col gap-3">
            <Input label="Name" {...register("name")} error={errors.name?.message} />
            <Input label="Slug" {...register("slug")} error={errors.slug?.message} />
            <Input label="Description (optional)" {...register("description")} />
            {serverError && <p className="text-sm text-vault-danger">{serverError}</p>}
            <Button type="submit" isLoading={createProject.isPending}>
              Create
            </Button>
          </form>
        </Modal>
      </Card>
    </div>
  );
}
