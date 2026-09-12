import { Activity, AlertTriangle, Database, ShieldCheck, ShieldOff } from "lucide-react";
import { Card } from "@/components/ui/Card";
import { Badge } from "@/components/ui/Badge";
import { MonoId } from "@/components/ui/MonoId";
import { Spinner } from "@/components/ui/Spinner";
import { EmptyState } from "@/components/ui/EmptyState";
import { formatDateTime } from "@/lib/format";
import { useHealthLive, useHealthReady, useSystemStats } from "@/hooks/useSystemStats";
import { isRecentlyActive, useAgentActivity } from "@/hooks/useAgentActivity";
import { useAudit } from "@/hooks/useAudit";

function StatusDot({ ok }: { ok: boolean | undefined }) {
  return (
    <span
      className={`inline-block h-2.5 w-2.5 rounded-full ${
        ok === undefined ? "bg-slate-600" : ok ? "bg-vault-accent shadow-vault-glow" : "bg-vault-danger"
      }`}
    />
  );
}

function SystemHealthCard() {
  const { data: live } = useHealthLive();
  const { data: ready } = useHealthReady();
  const { data: stats, isLoading } = useSystemStats();

  return (
    <Card title="Saúde do sistema">
      <div className="mb-4 flex flex-wrap gap-6">
        <div className="flex items-center gap-2">
          <StatusDot ok={live?.status === "ok"} />
          <span className="text-sm text-slate-300">API</span>
        </div>
        <div className="flex items-center gap-2">
          <StatusDot ok={ready?.dependencies?.postgres === "ok"} />
          <span className="text-sm text-slate-300">Postgres</span>
        </div>
      </div>

      {isLoading ? (
        <Spinner />
      ) : stats ? (
        <>
          <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
            {[
              { label: "Organizations", value: stats.organizationCount },
              { label: "Projects", value: stats.projectCount },
              { label: "Environments", value: stats.environmentCount },
              { label: "Secrets ativos", value: `${stats.activeSecretCount}/${stats.secretCount}` },
            ].map((item) => (
              <div key={item.label} className="rounded-md bg-vault-ink/30 p-3">
                <div className="text-2xl font-semibold text-vault-accent-bright">{item.value}</div>
                <div className="text-xs text-slate-500">{item.label}</div>
              </div>
            ))}
          </div>

          {stats.expiringSoon.length > 0 && (
            <div className="mt-4 rounded-md border border-vault-warning/40 bg-vault-warning/10 p-3">
              <div className="mb-1 flex items-center gap-1.5 text-sm font-medium text-amber-200">
                <AlertTriangle className="h-4 w-4" />
                {stats.expiringSoon.length} secret(s) expirando nos próximos 7 dias
              </div>
              <ul className="flex flex-col gap-1">
                {stats.expiringSoon.map((s) => (
                  <li key={s.id} className="text-xs text-amber-100">
                    <MonoId value={s.name} truncate={false} copyable={false} /> — expira em {formatDateTime(s.expiresAt)}
                  </li>
                ))}
              </ul>
            </div>
          )}
        </>
      ) : null}
    </Card>
  );
}

function AgentActivityCard() {
  const { activities, isLoading } = useAgentActivity();

  return (
    <Card title="Atividade dos agentes">
      <p className="mb-3 text-xs text-slate-500">
        "Ativo" = usou seu token do ForgeVault nos últimos 15 minutos — não é o status do gateway do Hermes, é a
        própria trilha de auditoria do ForgeVault.
      </p>
      {isLoading ? (
        <Spinner />
      ) : activities.length === 0 ? (
        <EmptyState message="Nenhum Service Account cadastrado ainda." />
      ) : (
        <div className="flex flex-col gap-2">
          {activities.map(({ serviceAccount, lastAction, lastSeenAt }) => {
            const active = isRecentlyActive(lastSeenAt);
            return (
              <div
                key={serviceAccount.id}
                className="flex items-center justify-between rounded-md bg-vault-ink/20 px-3 py-2"
              >
                <div className="flex items-center gap-2">
                  {active ? (
                    <ShieldCheck className="h-4 w-4 text-vault-accent-bright" />
                  ) : (
                    <ShieldOff className="h-4 w-4 text-slate-600" />
                  )}
                  <span className="text-sm text-slate-200">{serviceAccount.name}</span>
                </div>
                <div className="flex items-center gap-2 text-xs text-slate-500">
                  {lastAction && <Badge tone={active ? "success" : "neutral"}>{lastAction}</Badge>}
                  <span>{lastSeenAt ? formatDateTime(lastSeenAt) : "sem atividade"}</span>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </Card>
  );
}

function LiveActivityFeed() {
  const { data, isLoading } = useAudit({ pageSize: 15 }, { refetchInterval: 10_000 });

  return (
    <Card
      title="Atividade recente"
      actions={
        <span className="flex items-center gap-1.5 text-xs text-slate-500">
          <Activity className="h-3.5 w-3.5 animate-pulse text-vault-accent" />
          atualiza a cada 10s
        </span>
      }
    >
      {isLoading ? (
        <Spinner />
      ) : !data || data.items.length === 0 ? (
        <EmptyState message="Nenhum evento de auditoria ainda." />
      ) : (
        <div className="flex flex-col gap-1.5">
          {data.items.map((item) => (
            <div key={item.id} className="flex items-center justify-between border-b border-vault-surface-border/40 py-1.5 text-sm last:border-0">
              <div className="flex items-center gap-2">
                <Database className="h-3.5 w-3.5 text-slate-600" />
                <span className="font-mono text-xs text-vault-accent-bright">{item.action}</span>
                <span className="text-xs text-slate-500">{item.resourceType}</span>
                <MonoId value={item.resourceId} />
              </div>
              <span className="text-xs text-slate-500">{formatDateTime(item.timestamp)}</span>
            </div>
          ))}
        </div>
      )}
    </Card>
  );
}

export function OverviewPage() {
  return (
    <div className="flex flex-col gap-6">
      <SystemHealthCard />
      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        <AgentActivityCard />
        <LiveActivityFeed />
      </div>
    </div>
  );
}
