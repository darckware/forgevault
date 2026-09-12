import { useState } from "react";
import { useParams } from "react-router-dom";
import { KeyRound, ShieldOff } from "lucide-react";
import { Card } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { Modal } from "@/components/ui/Modal";
import { Table } from "@/components/ui/Table";
import { MonoId } from "@/components/ui/MonoId";
import { Spinner } from "@/components/ui/Spinner";
import { EmptyState } from "@/components/ui/EmptyState";
import { Breadcrumbs } from "@/components/layout/Breadcrumbs";
import { formatDateTime } from "@/lib/format";
import { useIssueServiceAccountToken, useRevokeServiceAccountToken, useServiceAccount } from "@/hooks/useServiceAccounts";

interface IssuedToken {
  tokenId: string;
  token: string;
  tokenPrefix: string;
  issuedAt: string;
  revoked: boolean;
}

// The API has no GET for existing tokens (only issue/revoke) — this page can only track
// tokens issued during the CURRENT browser session, kept in local state and cleared on
// refresh. If a token-listing endpoint is added later, this is the natural place for it.
export function ServiceAccountDetailPage() {
  const { saId } = useParams<{ saId: string }>();
  const { data: account, isLoading } = useServiceAccount(saId);
  const issueToken = useIssueServiceAccountToken(saId!);
  const revokeToken = useRevokeServiceAccountToken(saId!);
  const [sessionTokens, setSessionTokens] = useState<IssuedToken[]>([]);
  const [freshToken, setFreshToken] = useState<{ token: string; tokenPrefix: string } | null>(null);

  const handleIssue = async () => {
    const result = await issueToken.mutateAsync();
    setFreshToken(result);
    setSessionTokens((prev) => [
      { tokenId: crypto.randomUUID(), token: result.token, tokenPrefix: result.tokenPrefix, issuedAt: result.issuedAt, revoked: false },
      ...prev,
    ]);
  };

  const handleRevoke = async (tokenId: string) => {
    await revokeToken.mutateAsync(tokenId);
    setSessionTokens((prev) => prev.map((t) => (t.tokenId === tokenId ? { ...t, revoked: true } : t)));
  };

  if (isLoading || !account) {
    return <Spinner />;
  }

  return (
    <div className="flex flex-col gap-6">
      <Breadcrumbs items={[{ label: "Service Accounts", to: "/service-accounts" }, { label: account.name }]} />

      <Card title="Service Account">
        <div className="flex items-center gap-3">
          <span className="text-lg font-medium text-slate-100">{account.name}</span>
        </div>
        <div className="mt-2">
          <MonoId label="Identity id" value={account.id} truncate={false} />
        </div>
      </Card>

      <Card
        title="Tokens issued this session"
        actions={
          <Button size="sm" onClick={handleIssue} isLoading={issueToken.isPending}>
            <KeyRound className="h-4 w-4" />
            Issue Token
          </Button>
        }
      >
        {sessionTokens.length === 0 ? (
          <EmptyState message="No tokens issued in this browser session yet." />
        ) : (
          <Table<IssuedToken>
            keyField="tokenId"
            rows={sessionTokens}
            columns={[
              { key: "tokenPrefix", header: "Prefix", render: (t) => <span className="font-mono text-xs">{t.tokenPrefix}</span> },
              { key: "issuedAt", header: "Issued", render: (t) => formatDateTime(t.issuedAt) },
              {
                key: "actions",
                header: "",
                render: (t) =>
                  t.revoked ? (
                    <span className="text-xs text-slate-500">Revoked</span>
                  ) : (
                    <Button size="sm" variant="danger" onClick={() => handleRevoke(t.tokenId)}>
                      <ShieldOff className="h-3.5 w-3.5" />
                      Revoke
                    </Button>
                  ),
              },
            ]}
          />
        )}
      </Card>

      <Modal open={!!freshToken} onClose={() => setFreshToken(null)} title="Copy this token now">
        <div className="flex flex-col gap-3">
          <p className="text-sm text-amber-200">This value will not be shown again. Store it in the agent's own vault immediately.</p>
          {freshToken && <MonoId value={freshToken.token} truncate={false} />}
        </div>
      </Modal>
    </div>
  );
}
