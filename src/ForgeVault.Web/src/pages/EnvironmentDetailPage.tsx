import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { Plus } from "lucide-react";
import { Card } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { Table } from "@/components/ui/Table";
import { Modal } from "@/components/ui/Modal";
import { Badge } from "@/components/ui/Badge";
import { MonoId } from "@/components/ui/MonoId";
import { Spinner } from "@/components/ui/Spinner";
import { EmptyState } from "@/components/ui/EmptyState";
import { Breadcrumbs } from "@/components/layout/Breadcrumbs";
import { SecretStatusBadge } from "@/components/secrets/SecretStatusBadge";
import { SecretForm, type SecretFormValues } from "@/components/secrets/SecretForm";
import { environmentKindTone } from "@/lib/format";
import { SECRET_TYPE_LABELS } from "@/lib/secretValue";
import { useEnvironment } from "@/hooks/useEnvironments";
import { useCreateSecret, useSecrets } from "@/hooks/useSecrets";
import { ApiError } from "@/lib/api";
import type { SecretResponse } from "@/types/api";

export function EnvironmentDetailPage() {
  const { envId } = useParams<{ envId: string }>();
  const { data: environment, isLoading: envLoading } = useEnvironment(envId);
  const { data: secrets, isLoading: secretsLoading } = useSecrets(envId);
  const createSecret = useCreateSecret(envId!);
  const [modalOpen, setModalOpen] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const navigate = useNavigate();

  const onCreate = async (values: SecretFormValues) => {
    setServerError(null);
    try {
      await createSecret.mutateAsync({
        ...values,
        expiresAt: values.expiresAt ? new Date(values.expiresAt).toISOString() : undefined,
      });
      setModalOpen(false);
    } catch (err) {
      setServerError(err instanceof ApiError ? (err.body?.error ?? err.message) : "Failed to create");
    }
  };

  if (envLoading || !environment) {
    return <Spinner />;
  }

  return (
    <div className="flex flex-col gap-6">
      <Breadcrumbs
        items={[
          { label: "Organizations", to: "/organizations" },
          { label: "Project", to: `/projects/${environment.projectId}` },
          { label: environment.name },
        ]}
      />

      <Card title="Environment">
        <div className="flex items-center gap-4">
          <Badge tone={environmentKindTone(environment.name)}>{environment.name}</Badge>
          <span className="text-sm text-slate-400">Slug: {environment.slug}</span>
        </div>
      </Card>

      <Card
        title="Secrets"
        actions={
          <Button size="sm" onClick={() => setModalOpen(true)}>
            <Plus className="h-4 w-4" />
            New Secret
          </Button>
        }
      >
        {secretsLoading ? (
          <Spinner />
        ) : !secrets || secrets.length === 0 ? (
          <EmptyState message="No secrets yet." />
        ) : (
          <Table<SecretResponse>
            keyField="id"
            rows={secrets}
            onRowClick={(s) => navigate(`/secrets/${s.id}`)}
            columns={[
              { key: "name", header: "Name", render: (s) => <MonoId value={s.name} truncate={false} copyable={false} /> },
              { key: "type", header: "Type", render: (s) => SECRET_TYPE_LABELS[s.type] },
              { key: "status", header: "Status", render: (s) => <SecretStatusBadge status={s.status} /> },
              { key: "currentVersion", header: "Version", render: (s) => `#${s.currentVersion}` },
            ]}
          />
        )}

        <Modal open={modalOpen} onClose={() => setModalOpen(false)} title="New Secret">
          <SecretForm onSubmit={onCreate} isSubmitting={createSecret.isPending} serverError={serverError ?? undefined} />
        </Modal>
      </Card>
    </div>
  );
}
