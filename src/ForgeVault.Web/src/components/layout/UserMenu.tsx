import { useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  Camera,
  Check,
  ExternalLink,
  Info,
  KeyRound,
  Laptop,
  Loader2,
  LogOut,
  Moon,
  ShieldCheck,
  Sun,
  User as UserIcon,
} from "lucide-react";
import { cn } from "@/lib/cn";
import { useClickOutside } from "@/hooks/useClickOutside";
import QRCode from "qrcode";
import { useChangeMyPassword, useLogout, useMe, useMfaDisable, useMfaEnroll, useMfaVerify, useUpdateMe } from "@/hooks/useAuth";
import { useSystemVersion } from "@/hooks/useSystem";
import { useTheme } from "@/lib/theme";
import { Modal } from "@/components/ui/Modal";
import { Input } from "@/components/ui/Input";
import { Button } from "@/components/ui/Button";
import { Badge } from "@/components/ui/Badge";
import { MonoId } from "@/components/ui/MonoId";
import { Spinner } from "@/components/ui/Spinner";
import { ApiError } from "@/lib/api";
import type { MeResponse } from "@/types/api";

const MAX_AVATAR_BYTES = 2_000_000;

const THEME_OPTIONS = [
  { value: "light" as const, label: "Claro", icon: Sun },
  { value: "dark" as const, label: "Escuro", icon: Moon },
  { value: "system" as const, label: "Sistema", icon: Laptop },
];

function displayName(me: MeResponse): string {
  const full = [me.firstName, me.lastName].filter(Boolean).join(" ");
  return full.length > 0 ? full : me.email;
}

// Shared between the sidebar trigger and the account modal — an uploaded photo, or the
// first letter of the display name as a fallback, same convention as ForgeHub's avatar.
function UserAvatar({ me, className }: { me: MeResponse; className: string }) {
  if (me.avatarDataUrl) {
    return <img src={me.avatarDataUrl} alt="" className={cn(className, "rounded-full object-cover")} />;
  }
  return (
    <span
      className={cn(
        className,
        "flex items-center justify-center rounded-full bg-vault-accent-dark/30 font-bold uppercase text-vault-accent-bright",
      )}
    >
      {displayName(me)[0]}
    </span>
  );
}

function AccountModal({ onClose }: { onClose: () => void }) {
  const { data: me } = useMe();
  const [enrolling, setEnrolling] = useState(false);
  const [code, setCode] = useState("");
  const [verifyError, setVerifyError] = useState<string | null>(null);
  const [qrDataUrl, setQrDataUrl] = useState<string | null>(null);
  const enroll = useMfaEnroll();
  const verify = useMfaVerify();
  const updateMe = useUpdateMe();

  const [disabling, setDisabling] = useState(false);
  const [disablePassword, setDisablePassword] = useState("");
  const [disableError, setDisableError] = useState<string | null>(null);
  const disable = useMfaDisable();

  const [firstName, setFirstName] = useState(me?.firstName ?? "");
  const [lastName, setLastName] = useState(me?.lastName ?? "");
  const [profileError, setProfileError] = useState<string | null>(null);
  const [profileSaved, setProfileSaved] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const handleEnroll = async () => {
    const enrollment = await enroll.mutateAsync();
    setEnrolling(true);
    setQrDataUrl(await QRCode.toDataURL(enrollment.otpAuthUri, { margin: 1, width: 176 }));
  };

  const handleVerify = async () => {
    setVerifyError(null);
    try {
      await verify.mutateAsync(code);
      setEnrolling(false);
      setQrDataUrl(null);
    } catch (err) {
      setVerifyError(err instanceof ApiError ? (err.body?.error ?? err.message) : "Failed to verify code");
    }
  };

  const handleDisable = async () => {
    setDisableError(null);
    try {
      await disable.mutateAsync(disablePassword);
      setDisabling(false);
      setDisablePassword("");
    } catch (err) {
      setDisableError(err instanceof ApiError && err.status === 401 ? "Senha atual incorreta." : "Falha ao desativar MFA.");
    }
  };

  const handleAvatarPick = (file: File) => {
    setProfileError(null);
    if (file.size > MAX_AVATAR_BYTES) {
      setProfileError("Image too large (max ~1.5MB).");
      return;
    }

    const reader = new FileReader();
    reader.onload = () => updateMe.mutate({ avatarDataUrl: reader.result as string });
    reader.onerror = () => setProfileError("Failed to read the image file.");
    reader.readAsDataURL(file);
  };

  const handleSaveName = async () => {
    setProfileError(null);
    setProfileSaved(false);
    try {
      await updateMe.mutateAsync({ firstName: firstName.trim(), lastName: lastName.trim() });
      setProfileSaved(true);
    } catch {
      setProfileError("Failed to save your name.");
    }
  };

  return (
    <Modal open onClose={onClose} title="Perfil da conta">
      <div className="flex flex-col gap-4">
        <div className="flex items-center gap-3">
          <div className="relative">
            {me && <UserAvatar me={me} className="h-12 w-12 shrink-0 text-lg" />}
            <button
              type="button"
              onClick={() => fileInputRef.current?.click()}
              className="absolute -bottom-1 -right-1 flex h-5 w-5 items-center justify-center rounded-full border border-vault-surface-border bg-vault-bg text-slate-300 hover:text-vault-accent-bright"
              aria-label="Change profile photo"
            >
              <Camera className="h-3 w-3" />
            </button>
            <input
              ref={fileInputRef}
              type="file"
              accept="image/*"
              className="hidden"
              onChange={(e) => {
                const file = e.target.files?.[0];
                if (file) {
                  handleAvatarPick(file);
                }
                e.target.value = "";
              }}
            />
          </div>
          <div className="min-w-0">
            <p className="truncate text-sm font-medium text-slate-100">
              {me && displayName(me)}
              {me?.isAdmin && (
                <span className="ml-2 inline-block align-middle">
                  <Badge tone="success">Admin</Badge>
                </span>
              )}
            </p>
            <p className="truncate text-xs text-slate-500">{me?.email}</p>
            {me && <MonoId label="ID" value={me.id} />}
          </div>
        </div>

        <div className="flex flex-col gap-2 rounded-md border border-vault-surface-border bg-vault-surface-dim/40 p-3">
          <div className="flex gap-2">
            <Input label="Nome" value={firstName} onChange={(e) => setFirstName(e.target.value)} />
            <Input label="Sobrenome" value={lastName} onChange={(e) => setLastName(e.target.value)} />
          </div>
          {me?.username && <p className="text-xs text-slate-500">Username: {me.username}</p>}
          {profileError && <p className="text-xs text-vault-danger">{profileError}</p>}
          {profileSaved && <p className="text-xs text-vault-accent-bright">Salvo.</p>}
          <Button size="sm" className="w-fit" isLoading={updateMe.isPending} onClick={handleSaveName}>
            Salvar nome
          </Button>
        </div>

        <div className="rounded-md border border-vault-surface-border bg-vault-surface-dim/40 p-3">
          <div className="flex items-center justify-between">
            <span className="text-sm text-slate-300">Autenticação em duas etapas (MFA)</span>
            <Badge tone={me?.mfaEnabled ? "success" : "neutral"}>{me?.mfaEnabled ? "Ativo" : "Inativo"}</Badge>
          </div>

          {!me?.mfaEnabled && !enrolling && (
            <Button variant="secondary" size="sm" className="mt-3" isLoading={enroll.isPending} onClick={handleEnroll}>
              Ativar MFA
            </Button>
          )}

          {!me?.mfaEnabled && enrolling && enroll.data && (
            <div className="mt-3 flex flex-col gap-3">
              <p className="text-xs text-slate-400">
                Escaneie o QR code com seu aplicativo autenticador (Google Authenticator, 1Password, etc.) — ou digite a
                chave manualmente — depois informe o código de 6 dígitos abaixo.
              </p>
              {qrDataUrl && (
                <div className="flex justify-center rounded-md bg-white p-3">
                  <img src={qrDataUrl} alt="QR code de configuração do MFA" width={176} height={176} />
                </div>
              )}
              <MonoId label="Chave" value={enroll.data.base32Secret} truncate={false} />
              <Input
                label="Código de 6 dígitos"
                placeholder="123456"
                value={code}
                onChange={(e) => setCode(e.target.value)}
                error={verifyError ?? undefined}
              />
              <div className="flex justify-end gap-2">
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => {
                    setEnrolling(false);
                    setQrDataUrl(null);
                  }}
                >
                  Cancelar
                </Button>
                <Button size="sm" isLoading={verify.isPending} onClick={handleVerify}>
                  Confirmar
                </Button>
              </div>
            </div>
          )}

          {me?.mfaEnabled && !disabling && (
            <Button variant="danger" size="sm" className="mt-3" onClick={() => setDisabling(true)}>
              Desativar MFA
            </Button>
          )}

          {me?.mfaEnabled && disabling && (
            <div className="mt-3 flex flex-col gap-2">
              <p className="text-xs text-slate-400">
                Confirme sua senha atual para desativar a autenticação em duas etapas.
              </p>
              <Input
                label="Senha atual"
                type="password"
                value={disablePassword}
                onChange={(e) => setDisablePassword(e.target.value)}
                error={disableError ?? undefined}
              />
              <div className="flex justify-end gap-2">
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => {
                    setDisabling(false);
                    setDisablePassword("");
                    setDisableError(null);
                  }}
                >
                  Cancelar
                </Button>
                <Button variant="danger" size="sm" isLoading={disable.isPending} onClick={handleDisable}>
                  Desativar
                </Button>
              </div>
            </div>
          )}
        </div>

        <div className="flex justify-end">
          <Button variant="ghost" size="sm" onClick={onClose}>
            Fechar
          </Button>
        </div>
      </div>
    </Modal>
  );
}

function ChangePasswordModal({ onClose }: { onClose: () => void }) {
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [localError, setLocalError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);
  const changePassword = useChangeMyPassword();

  const handleSave = async () => {
    setLocalError(null);
    if (newPassword.length < 8) {
      setLocalError("A nova senha precisa ter ao menos 8 caracteres.");
      return;
    }
    if (newPassword !== confirmPassword) {
      setLocalError("As senhas não coincidem.");
      return;
    }
    try {
      await changePassword.mutateAsync({ currentPassword, newPassword });
      setSuccess(true);
    } catch (err) {
      setLocalError(err instanceof ApiError && err.status === 401 ? "Senha atual incorreta." : "Falha ao trocar a senha.");
    }
  };

  return (
    <Modal open onClose={onClose} title="Trocar senha">
      {success ? (
        <div className="flex flex-col gap-4">
          <p className="text-sm text-vault-accent-bright">
            Senha alterada. Suas outras sessões ativas foram desconectadas.
          </p>
          <div className="flex justify-end">
            <Button size="sm" onClick={onClose}>
              Fechar
            </Button>
          </div>
        </div>
      ) : (
        <div className="flex flex-col gap-3">
          <Input
            label="Senha atual"
            type="password"
            value={currentPassword}
            onChange={(e) => setCurrentPassword(e.target.value)}
          />
          <Input label="Nova senha" type="password" value={newPassword} onChange={(e) => setNewPassword(e.target.value)} />
          <Input
            label="Confirmar nova senha"
            type="password"
            value={confirmPassword}
            onChange={(e) => setConfirmPassword(e.target.value)}
          />
          {localError && <p className="text-sm text-vault-danger">{localError}</p>}
          <div className="mt-1 flex justify-end gap-2">
            <Button variant="ghost" size="sm" onClick={onClose}>
              Cancelar
            </Button>
            <Button size="sm" isLoading={changePassword.isPending} onClick={handleSave}>
              Salvar
            </Button>
          </div>
        </div>
      )}
    </Modal>
  );
}

// Mirrors ForgeHub's UserSettingsMenu AboutModal (GET /api/v1/system/version) field-for-field.
function AboutModal({ onClose }: { onClose: () => void }) {
  const version = useSystemVersion(true);

  return (
    <Modal open onClose={onClose} title="Sobre">
      {version.isPending && (
        <div className="flex items-center gap-2 text-sm text-slate-500">
          <Spinner />
          Carregando...
        </div>
      )}
      {version.isError && <p className="text-sm text-vault-danger">Falha ao carregar a versão do sistema.</p>}
      {version.data && (
        <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-2 text-sm">
          <dt className="text-slate-500">Versão</dt>
          <dd className="font-mono text-slate-200">{version.data.appVersion}</dd>
          <dt className="text-slate-500">Commit</dt>
          <dd className="min-w-0 break-all font-mono text-slate-200">
            {version.data.gitCommitUrl ? (
              <a
                className="inline-flex items-center gap-1 text-vault-accent-bright hover:underline"
                href={version.data.gitCommitUrl}
                target="_blank"
                rel="noreferrer"
              >
                {version.data.gitSha}
                <ExternalLink className="h-3 w-3" />
              </a>
            ) : (
              version.data.gitSha
            )}
          </dd>
          <dt className="text-slate-500">Build</dt>
          <dd className="break-all font-mono text-xs text-slate-300">{version.data.buildDate}</dd>
          <dt className="text-slate-500">PostgreSQL</dt>
          <dd className="break-words text-xs text-slate-300">{version.data.postgresVersion ?? "indisponível"}</dd>
          <dt className="text-slate-500">Migration</dt>
          <dd className="break-all font-mono text-xs text-slate-300">{version.data.latestMigrationBundled ?? "indisponível"}</dd>
          <dt className="text-slate-500">Repositório</dt>
          <dd>
            <a
              className="inline-flex items-center gap-1 text-vault-accent-bright hover:underline"
              href={version.data.githubRepoUrl}
              target="_blank"
              rel="noreferrer"
            >
              GitHub
              <ExternalLink className="h-3 w-3" />
            </a>
          </dd>
        </dl>
      )}
      <div className="mt-5 flex justify-end">
        <Button variant="ghost" size="sm" onClick={onClose}>
          Fechar
        </Button>
      </div>
    </Modal>
  );
}

// Bottom-of-sidebar identity + account menu — same UX shape as ForgeHub's sidebar user row
// (avatar trigger, dropdown with Account/Change password/Admin/Logout), rebuilt on
// ForgeVault's own vault-* design system rather than ForgeHub's shadcn/ui tokens
// (docs/architecture/TARGET_ARCHITECTURE.md §10 — ForgeVault never reuses that design system).
export function UserMenu({ collapsed = false }: { collapsed?: boolean }) {
  const { data: me } = useMe();
  const [open, setOpen] = useState(false);
  const [modal, setModal] = useState<"account" | "password" | "about" | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  useClickOutside(containerRef, () => setOpen(false), open);
  const navigate = useNavigate();
  const logout = useLogout();
  const { theme, setTheme } = useTheme();

  if (!me) {
    return (
      <div className="flex items-center gap-2 border-t border-vault-surface-border px-3 py-3 text-slate-500">
        <Loader2 className="h-4 w-4 animate-spin" />
      </div>
    );
  }

  return (
    <>
      <div className="relative border-t border-vault-surface-border p-3" ref={containerRef}>
        <button
          type="button"
          onClick={() => setOpen((v) => !v)}
          title={collapsed ? displayName(me) : undefined}
          className={cn(
            "flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left transition-colors hover:bg-vault-surface-dim/60",
            collapsed && "justify-center px-0",
            open && "bg-vault-surface-dim/60",
          )}
        >
          <UserAvatar me={me} className="h-7 w-7 shrink-0 text-xs" />
          {!collapsed && (
            <>
              <span className="min-w-0 flex-1 truncate text-xs text-slate-300">{displayName(me)}</span>
              {me.isAdmin && (
                <span className="shrink-0">
                  <Badge tone="success">Admin</Badge>
                </span>
              )}
            </>
          )}
        </button>

        {open && (
          <div
            className={cn(
              "absolute bottom-full z-20 mb-1 w-56 overflow-hidden rounded-md border border-vault-surface-border bg-vault-bg py-1 shadow-xl",
              collapsed ? "left-2" : "inset-x-3 w-auto",
            )}
          >
            <p className="px-3 py-1 text-[10px] font-semibold uppercase tracking-wider text-slate-500">Conta</p>
            <button
              type="button"
              className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-slate-300 hover:bg-vault-surface-dim hover:text-slate-100"
              onClick={() => {
                setModal("account");
                setOpen(false);
              }}
            >
              <UserIcon className="h-3.5 w-3.5" />
              Perfil da conta
            </button>
            <button
              type="button"
              className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-slate-300 hover:bg-vault-surface-dim hover:text-slate-100"
              onClick={() => {
                setModal("password");
                setOpen(false);
              }}
            >
              <KeyRound className="h-3.5 w-3.5" />
              Trocar senha
            </button>
            <div className="my-1 border-t border-vault-surface-border" />
            <button
              type="button"
              className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-slate-300 hover:bg-vault-surface-dim hover:text-slate-100"
              onClick={() => {
                setOpen(false);
                navigate("/access");
              }}
            >
              <ShieldCheck className="h-3.5 w-3.5" />
              Controle administrativo
            </button>
            <div className="my-1 border-t border-vault-surface-border" />
            <p className="px-3 py-1 text-[10px] font-semibold uppercase tracking-wider text-slate-500">Tema</p>
            {THEME_OPTIONS.map((opt) => (
              <button
                key={opt.value}
                type="button"
                className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-slate-300 hover:bg-vault-surface-dim hover:text-slate-100"
                onClick={() => setTheme(opt.value)}
              >
                <opt.icon className="h-3.5 w-3.5" />
                <span className="flex-1">{opt.label}</span>
                {theme === opt.value && <Check className="h-3.5 w-3.5 text-vault-accent-bright" />}
              </button>
            ))}
            <div className="my-1 border-t border-vault-surface-border" />
            <button
              type="button"
              className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-slate-300 hover:bg-vault-surface-dim hover:text-slate-100"
              onClick={() => {
                setModal("about");
                setOpen(false);
              }}
            >
              <Info className="h-3.5 w-3.5" />
              Sobre
            </button>
            <button
              type="button"
              className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-slate-400 hover:bg-vault-surface-dim hover:text-slate-100"
              onClick={logout}
            >
              <LogOut className="h-3.5 w-3.5" />
              Sair
            </button>
          </div>
        )}
      </div>

      {modal === "account" && <AccountModal onClose={() => setModal(null)} />}
      {modal === "password" && <ChangePasswordModal onClose={() => setModal(null)} />}
      {modal === "about" && <AboutModal onClose={() => setModal(null)} />}
    </>
  );
}
