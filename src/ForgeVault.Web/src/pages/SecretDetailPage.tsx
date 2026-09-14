import { useState, type FormEvent } from "react";
import { useForm } from "react-hook-form";
import { useParams } from "react-router-dom";
import { RefreshCw, ShieldOff } from "lucide-react";
import { Card } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Modal } from "@/components/ui/Modal";
import { Tabs } from "@/components/ui/Tabs";
import { Spinner } from "@/components/ui/Spinner";
import { Badge } from "@/components/ui/Badge";
import { Table } from "@/components/ui/Table";
import { MonoId } from "@/components/ui/MonoId";
import { EmptyState } from "@/components/ui/EmptyState";
import { Breadcrumbs } from "@/components/layout/Breadcrumbs";
import { RevealSecretButton } from "@/components/secrets/RevealSecretButton";
import { SecretStatusBadge } from "@/components/secrets/SecretStatusBadge";
import { SecretVersionsTable } from "@/components/secrets/SecretVersionsTable";
import { formatDateTime } from "@/lib/format";
import { useEnvironment } from "@/hooks/useEnvironments";
import { useRevokeSecret, useRotateSecret, useSecret, useUpdateSecret } from "@/hooks/useSecrets";
import { useGrantSecretAccess, useRevokeSecretAccessGrant, useSecretAccessGrants } from "@/hooks/useSecretAccessGrants";
import { ApiError } from "@/lib/api";
import { SECRET_TYPE_LABELS } from "@/lib/secretValue";
import type { SecretAccessGrantResponse } from "@/types/api";

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
  const [grantIdentityId, setGrantIdentityId] = useState("");
  const [grantError, setGrantError] = useState<string | null>(null);

  const updateForm = useForm<ValueFormValues>();
  const rotateForm = useForm<ValueFormValues>();

  const { data: accessGrants, isLoading: grantsLoading } = useSecretAccessGrants(secretId);
  const grantAccess = useGrantSecretAccess(secretId!);
  const revokeAccessGrant = useRevokeSecretAccessGrant(secretId!);

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

  const onGrantAccess = async (e: FormEvent) => {
    e.preventDefault();
    setGrantError(null);
    try {
      await grantAccess.mutateAsync(grantIdentityId);
      setGrantIdentityId("");
    } catch (err) {
      setGrantError(err instanceof ApiError ? (err.body?.error ?? err.message) : "Failed to grant access");
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
              {SECRET_TYPE_LABELS[secret.type]}
              {secret.provider && ` · ${secret.provider}`} · version #{secret.currentVersion}
            </p>
            {secret.description && <p className="mt-1 text-sm text-slate-400">{secret.description}</p>}
          </div>
          <div className="flex items-center gap-3">
            <SecretStatusBadge status={secret.status} />
            <RevealSecretButton secretId={secret.id} secretName={secret.name} secretType={secret.type} />
          </div>
        </div>
        {secret.expiresAt && <p className="mt-2 text-xs text-slate-500">Expires: {formatDateTime(secret.expiresAt)}</p>}
      </Card>

      <Card>
        <Tabs
          tabs={[
            { id: "details", label: "Details" },
            { id: "versions", label: "Versions" },
            { id: "access", label: "Access" },
          ]}
          activeId={activeTab}
          onChange={setActiveTab}
        >
          {activeTab === "access" ? (
            <div className="flex flex-col gap-4">
              <p className="text-sm text-slate-400">
                Identities granted here can read this secret's value directly, regardless of any Role they hold — use it to
                give a single agent exactly one credential (e.g. a site login, a database credential, a provider token)
                without also handing it everything else in this Environment.
              </p>

              <form onSubmit={onGrantAccess} className="flex items-end gap-2">
                <div className="flex-1">
                  <Input
                    label="Identity id"
                    value={grantIdentityId}
                    onChange={(e) => setGrantIdentityId(e.target.value)}
                    placeholder="paste a User or ServiceAccount id"
                  />
                </div>
                <Button type="submit" isLoading={grantAccess.isPending}>
                  Grant access
                </Button>
              </form>
              {grantError && <p className="text-sm text-vault-danger">{grantError}</p>}

              {grantsLoading ? (
                <Spinner />
              ) : !accessGrants || accessGrants.length === 0 ? (
                <EmptyState message="No identity has been granted direct access to this secret." />
              ) : (
                <Table<SecretAccessGrantResponse>
                  keyField="id"
                  rows={accessGrants}
                  columns={[
                    { key: "identityId", header: "Identity", render: (g) => <MonoId value={g.identityId} /> },
                    { key: "status", header: "Status", render: (g) => <Badge tone={g.status === "active" ? "success" : "danger"}>{g.status}</Badge> },
                    { key: "createdAt", header: "Granted", render: (g) => formatDateTime(g.createdAt) },
                    {
                      key: "actions",
                      header: "",
                      render: (g) =>
                        g.status === "active" && (
                          <Button
                            size="sm"
                            variant="danger"
                            onClick={() => revokeAccessGrant.mutate(g.id)}
                            isLoading={revokeAccessGrant.isPending}
                          >
                            Revoke
                          </Button>
                        ),
                    },
                  ]}
                />
              )}
            </div>
          ) : activeTab === "details" ? (
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
