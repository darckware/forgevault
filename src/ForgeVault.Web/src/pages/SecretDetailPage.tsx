import { useState } from "react";
import { useForm } from "react-hook-form";
import { useParams } from "react-router-dom";
import { RefreshCw, ShieldOff } from "lucide-react";
import { Card } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Modal } from "@/components/ui/Modal";
import { Tabs } from "@/components/ui/Tabs";
import { Spinner } from "@/components/ui/Spinner";
import { Breadcrumbs } from "@/components/layout/Breadcrumbs";
import { RevealSecretButton } from "@/components/secrets/RevealSecretButton";
import { SecretStatusBadge } from "@/components/secrets/SecretStatusBadge";
import { SecretVersionsTable } from "@/components/secrets/SecretVersionsTable";
import { formatDateTime } from "@/lib/format";
import { useEnvironment } from "@/hooks/useEnvironments";
import { useRevokeSecret, useRotateSecret, useSecret, useUpdateSecret } from "@/hooks/useSecrets";
import { ApiError } from "@/lib/api";

interface ValueFormValues {
  value: string;
}

export function SecretDetailPage() {
  const { secretId } = useParams<{ secretId: string }>();
  const { data: secret, isLoading } = useSecret(secretId);
  const { data: environment } = useEnvironment(secret?.environmentId);
  const updateSecret = useUpdateSecret(secretId!);
  const rotateSecret = useRotateSecret(secretId!);
  const revokeSecret = useRevokeSecret(secretId!);

  const [activeTab, setActiveTab] = useState("details");
  const [rotateOpen, setRotateOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const updateForm = useForm<ValueFormValues>();
  const rotateForm = useForm<ValueFormValues>();

  if (isLoading || !secret) {
    return <Spinner />;
  }

  const isActive = secret.status === "Active";

  const onUpdate = async (values: ValueFormValues) => {
    setError(null);
    try {
      await updateSecret.mutateAsync(values);
      updateForm.reset();
    } catch (err) {
      setError(err instanceof ApiError ? (err.body?.error ?? err.message) : "Failed to update");
    }
  };

  const onRotate = async (values: ValueFormValues) => {
    setError(null);
    try {
      await rotateSecret.mutateAsync(values);
      setRotateOpen(false);
      rotateForm.reset();
    } catch (err) {
      setError(err instanceof ApiError ? (err.body?.error ?? err.message) : "Failed to rotate");
    }
  };

  const onRevoke = async () => {
    if (!confirm(`Revoke secret "${secret.name}"? This cannot be undone.`)) {
      return;
    }
    try {
      await revokeSecret.mutateAsync();
    } catch (err) {
      setError(err instanceof ApiError ? (err.body?.error ?? err.message) : "Failed to revoke");
    }
  };

  return (
    <div className="flex flex-col gap-6">
      <Breadcrumbs
        items={[
          { label: "Organizations", to: "/organizations" },
          ...(environment ? [{ label: "Environment", to: `/environments/${environment.id}` }] : []),
          { label: secret.name },
        ]}
      />

      <Card>
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div>
            <h1 className="font-mono text-lg text-vault-accent-bright">{secret.name}</h1>
            <p className="mt-1 text-sm text-slate-500">
              {secret.type}
              {secret.provider && ` · ${secret.provider}`} · version #{secret.currentVersion}
            </p>
            {secret.description && <p className="mt-1 text-sm text-slate-400">{secret.description}</p>}
          </div>
          <div className="flex items-center gap-3">
            <SecretStatusBadge status={secret.status} />
            <RevealSecretButton secretId={secret.id} secretName={secret.name} />
          </div>
        </div>
        {secret.expiresAt && <p className="mt-2 text-xs text-slate-500">Expires: {formatDateTime(secret.expiresAt)}</p>}
      </Card>

      <Card>
        <Tabs
          tabs={[
            { id: "details", label: "Details" },
            { id: "versions", label: "Versions" },
          ]}
          activeId={activeTab}
          onChange={setActiveTab}
        >
          {activeTab === "details" ? (
            <div className="flex flex-col gap-6">
              <form onSubmit={updateForm.handleSubmit(onUpdate)} className="flex flex-col gap-3">
                <h3 className="text-sm font-medium text-slate-300">Update value (creates a new version)</h3>
                <Input
                  label="New value"
                  type="password"
                  disabled={!isActive}
                  {...updateForm.register("value", { required: true })}
                />
                <Button type="submit" variant="primary" isLoading={updateSecret.isPending} disabled={!isActive} className="w-fit">
                  Save
                </Button>
                {!isActive && <p className="text-xs text-slate-500">This secret is {secret.status.toLowerCase()} and can't be updated.</p>}
              </form>

              <div className="flex flex-wrap gap-3 border-t border-vault-surface-border pt-4">
                <Button variant="secondary" onClick={() => setRotateOpen(true)} disabled={!isActive}>
                  <RefreshCw className="h-4 w-4" />
                  Rotate
                </Button>
                <Button variant="danger" onClick={onRevoke} disabled={!isActive} isLoading={revokeSecret.isPending}>
                  <ShieldOff className="h-4 w-4" />
                  Revoke
                </Button>
              </div>

              {error && <p className="text-sm text-vault-danger">{error}</p>}
            </div>
          ) : (
            <SecretVersionsTable secretId={secret.id} />
          )}
        </Tabs>
      </Card>

      <Modal open={rotateOpen} onClose={() => setRotateOpen(false)} title="Rotate secret">
        <form onSubmit={rotateForm.handleSubmit(onRotate)} className="flex flex-col gap-3">
          <p className="text-sm text-slate-400">
            Rotating creates a new version and is audited as a distinct action from a regular update.
          </p>
          <Input label="New value" type="password" {...rotateForm.register("value", { required: true })} />
          <Button type="submit" isLoading={rotateSecret.isPending}>
            Rotate
          </Button>
        </form>
      </Modal>
    </div>
  );
}
