import { useRef, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useLocation, useNavigate } from "react-router-dom";
import { motion } from "framer-motion";
import ReCAPTCHA from "react-google-recaptcha";
import { Eye, EyeOff, KeyRound, Loader2, Lock, ScrollText, ShieldCheck, Users } from "lucide-react";
import { Input } from "@/components/ui/Input";
import { Button } from "@/components/ui/Button";
import { ApiError } from "@/lib/api";
import { useLogin } from "@/hooks/useAuth";
import { getRememberMe, setRememberMe } from "@/stores/authStore";

const schema = z.object({
  email: z.string().email(),
  password: z.string().min(1, "Required"),
  mfaCode: z.string().optional(),
});
type FormValues = z.infer<typeof schema>;

const RECAPTCHA_SITE_KEY = import.meta.env.VITE_RECAPTCHA_SITE_KEY;

/** Deterministic glint field (module-level so positions don't reshuffle on re-render) —
 * teal embers rising off the vault door, echoing ForgeHub's spark field but in ForgeVault's
 * own accent color, not amber. */
const GLINTS = Array.from({ length: 16 }, (_, i) => ({
  left: `${(i * 137.5) % 100}%`,
  size: 1.5 + ((i * 7) % 4),
  duration: 8 + ((i * 11) % 8),
  delay: (i * 1.6) % 9,
}));

/** Full-viewport animated backdrop: a rotating vault-dial motif instead of ForgeHub's
 * pipeline DAG — three concentric rings turning at different speeds, like a safe's
 * combination dial, plus drifting teal glow orbs and rising glints. */
function VaultBackdrop() {
  return (
    <div className="absolute inset-0 overflow-hidden" aria-hidden="true">
      <div className="absolute inset-0 bg-vault-bg-deep" />

      {/* Slow-panning grid, teal-tinted */}
      <motion.div
        className="absolute -inset-[100%] opacity-[0.3]"
        style={{
          backgroundImage:
            "linear-gradient(rgba(45,212,191,0.12) 1px, transparent 1px), linear-gradient(90deg, rgba(45,212,191,0.12) 1px, transparent 1px)",
          backgroundSize: "56px 56px",
          maskImage: "radial-gradient(ellipse 70% 60% at 40% 45%, black 30%, transparent 75%)",
          WebkitMaskImage: "radial-gradient(ellipse 70% 60% at 40% 45%, black 30%, transparent 75%)",
        }}
        animate={{ x: [0, 56], y: [0, 56] }}
        transition={{ duration: 16, repeat: Infinity, ease: "linear" }}
      />

      {/* Rotating vault-dial rings, centered */}
      <div className="absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 opacity-[0.14]">
        {[420, 320, 220].map((size, i) => (
          <motion.div
            key={size}
            className="absolute rounded-full border"
            style={{
              width: size,
              height: size,
              left: -size / 2,
              top: -size / 2,
              borderColor: "rgba(45,212,191,0.5)",
              borderStyle: i === 1 ? "dashed" : "solid",
            }}
            animate={{ rotate: i % 2 === 0 ? 360 : -360 }}
            transition={{ duration: 40 + i * 20, repeat: Infinity, ease: "linear" }}
          />
        ))}
        <img src="/forgevault-icon.svg" alt="" className="absolute -left-16 -top-16 h-32 w-32" />
      </div>

      <motion.div
        className="absolute h-[30rem] w-[30rem] rounded-full bg-vault-accent-dark/20 blur-[110px]"
        style={{ top: "-10%", left: "-8%" }}
        animate={{ x: [0, 90, 20, 0], y: [0, 50, 110, 0] }}
        transition={{ duration: 26, repeat: Infinity, ease: "easeInOut" }}
      />
      <motion.div
        className="absolute h-[26rem] w-[26rem] rounded-full bg-vault-surface/30 blur-[100px]"
        style={{ bottom: "-14%", right: "-6%" }}
        animate={{ x: [0, -80, -20, 0], y: [0, -60, -120, 0] }}
        transition={{ duration: 30, repeat: Infinity, ease: "easeInOut" }}
      />

      {GLINTS.map((g, i) => (
        <motion.span
          key={i}
          className="absolute rounded-full bg-vault-accent-bright"
          style={{
            left: g.left,
            bottom: -8,
            width: g.size,
            height: g.size,
            boxShadow: "0 0 6px 1px rgba(94,234,212,0.55)",
          }}
          animate={{ y: ["0vh", "-92vh"], opacity: [0, 0.9, 0] }}
          transition={{ duration: g.duration, delay: g.delay, repeat: Infinity, ease: "linear" }}
        />
      ))}

      <div className="absolute inset-0 bg-[radial-gradient(ellipse_at_center,transparent_35%,rgba(2,6,23,0.85)_100%)]" />
    </div>
  );
}

const PILLARS = [
  { icon: Lock, title: "Envelope encryption", text: "AES-256-GCM por versão, chave protegida por uma Master Key própria." },
  { icon: Users, title: "RBAC hierárquico", text: "Organization → Project → Environment, com herança de permissão." },
  { icon: ScrollText, title: "Auditoria completa", text: "Toda leitura e escrita fica registrada — nunca o valor do secret." },
];

export function LoginPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const from = (location.state as { from?: { pathname: string } } | null)?.from?.pathname ?? "/overview";

  const [needsMfa, setNeedsMfa] = useState(false);
  const [showPassword, setShowPassword] = useState(false);
  const [rememberMe, setRememberMeChecked] = useState(getRememberMe);
  const [recaptchaToken, setRecaptchaToken] = useState<string | null>(null);
  const [serverError, setServerError] = useState<string | null>(null);
  const recaptchaRef = useRef<ReCAPTCHA>(null);
  const login = useLogin();

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  const onSubmit = async (values: FormValues) => {
    if (RECAPTCHA_SITE_KEY && !recaptchaToken) {
      return;
    }

    setServerError(null);
    try {
      // Set before the login mutation so setSession's persisted write already lands in the
      // right storage — no second write or page reload needed for the choice to take effect.
      setRememberMe(rememberMe);
      await login.mutateAsync({ ...values, recaptchaToken });
      navigate(from, { replace: true });
    } catch (err) {
      recaptchaRef.current?.reset();
      setRecaptchaToken(null);
      if (err instanceof ApiError) {
        // Only reveal the MFA field for the two error codes that actually mean "try again
        // with a code" — every other 401 (recaptcha_failed, invalid_credentials,
        // account_disabled) used to also flip this on, which showed a confusing "enter your
        // MFA code" prompt to an account that never enabled MFA in the first place.
        const code = err.body?.error;
        if (code === "mfa_required" || code === "invalid_mfa_code") {
          setNeedsMfa(true);
        }
        setServerError(code ?? "Login failed");
      }
    }
  };

  return (
    <div className="relative min-h-screen text-slate-200">
      <VaultBackdrop />

      <div className="relative z-10 grid min-h-screen lg:grid-cols-[1.15fr_1fr]">
        {/* Left: branding panel (desktop only) */}
        <div className="hidden flex-col justify-between p-12 lg:flex">
          <div className="flex items-center gap-2">
            <img src="/forgevault-icon.svg" alt="ForgeVault" className="h-9 w-9" />
            <span className="text-lg font-semibold tracking-tight text-slate-100">ForgeVault</span>
          </div>

          <motion.div
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.6, ease: "easeOut" }}
            className="max-w-lg"
          >
            <p className="mb-4 inline-flex items-center gap-2 rounded-full border border-vault-accent-dark/30 bg-vault-accent-dark/10 px-3 py-1 text-xs font-medium tracking-wide text-vault-accent-bright">
              <span className="h-1.5 w-1.5 rounded-full bg-vault-accent" />
              Security Plane do ecossistema Darckware
            </p>
            <h1 className="text-4xl font-semibold leading-tight tracking-tight text-slate-100">
              O cofre central de credenciais para humanos, agentes e serviços.
            </h1>

            <div className="mt-10 flex flex-col gap-6">
              {PILLARS.map((p, i) => (
                <motion.div
                  key={p.title}
                  initial={{ opacity: 0, x: -16 }}
                  animate={{ opacity: 1, x: 0 }}
                  transition={{ duration: 0.5, delay: 0.2 + i * 0.12 }}
                  className="flex items-start gap-4"
                >
                  <div className="rounded-lg border border-vault-surface-border bg-white/[0.04] p-2.5">
                    <p.icon className="h-5 w-5 text-vault-accent-bright" />
                  </div>
                  <div>
                    <p className="font-medium text-slate-100">{p.title}</p>
                    <p className="text-sm text-slate-400">{p.text}</p>
                  </div>
                </motion.div>
              ))}
            </div>
          </motion.div>

          <p className="font-mono text-xs text-slate-500">
            ForgeVault · API v1 · organization → project → environment → secret
          </p>
        </div>

        {/* Right: sign-in panel */}
        <div className="flex items-center justify-center p-6 lg:border-l lg:border-vault-surface-border lg:bg-black/30 lg:backdrop-blur-md">
          <motion.div
            initial={{ opacity: 0, y: 16 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.5, ease: "easeOut" }}
            className="w-full max-w-sm"
          >
            <div className="mb-8 flex flex-col items-center gap-2 lg:hidden">
              <img src="/forgevault-icon.svg" alt="ForgeVault" className="h-14 w-14 drop-shadow-[0_0_18px_rgba(45,212,191,0.45)]" />
              <span className="text-xl font-semibold tracking-tight text-slate-100">ForgeVault</span>
            </div>

            <div className="rounded-xl border border-vault-surface-border bg-vault-surface-dim/60 p-8 shadow-2xl shadow-black/40 backdrop-blur-xl">
              <div className="mb-6">
                <h2 className="text-2xl font-semibold tracking-tight text-slate-100">Bem-vindo de volta</h2>
                <p className="mt-1 text-sm text-slate-400">Entre para acessar seu cofre</p>
              </div>

              <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
                <div className="relative">
                  <Users className="pointer-events-none absolute left-3 top-[34px] h-4 w-4 -translate-y-1/2 text-slate-500" />
                  <Input label="Email" type="email" className="pl-9" {...register("email")} error={errors.email?.message} />
                </div>

                <div className="relative">
                  <KeyRound className="pointer-events-none absolute left-3 top-[34px] h-4 w-4 -translate-y-1/2 text-slate-500" />
                  <Input
                    label="Password"
                    type={showPassword ? "text" : "password"}
                    className="pl-9 pr-9"
                    {...register("password")}
                    error={errors.password?.message}
                  />
                  <button
                    type="button"
                    onClick={() => setShowPassword((s) => !s)}
                    className="absolute right-2.5 top-[34px] -translate-y-1/2 text-slate-500 hover:text-slate-200"
                    tabIndex={-1}
                    aria-label={showPassword ? "Hide password" : "Show password"}
                  >
                    {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                  </button>
                </div>

                {needsMfa && <Input label="MFA code" placeholder="123456" {...register("mfaCode")} />}

                <label className="flex items-center gap-2 text-sm text-slate-400 select-none">
                  <input
                    type="checkbox"
                    checked={rememberMe}
                    onChange={(e) => setRememberMeChecked(e.target.checked)}
                    className="h-3.5 w-3.5 rounded border-vault-surface-border bg-white/5 accent-vault-accent"
                  />
                  Manter conectado
                </label>

                {RECAPTCHA_SITE_KEY && (
                  <div className="flex justify-center">
                    <ReCAPTCHA
                      ref={recaptchaRef}
                      sitekey={RECAPTCHA_SITE_KEY}
                      theme="dark"
                      onChange={(token) => setRecaptchaToken(token)}
                      onExpired={() => setRecaptchaToken(null)}
                    />
                  </div>
                )}

                {serverError && <p className="text-xs text-vault-danger">{serverError}</p>}

                <Button
                  type="submit"
                  isLoading={login.isPending}
                  disabled={Boolean(RECAPTCHA_SITE_KEY) && !recaptchaToken}
                  className="mt-2 gap-2"
                >
                  {login.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <ShieldCheck className="h-4 w-4" />}
                  Entrar
                </Button>
              </form>
            </div>

            <p className="mt-6 text-center text-xs text-slate-500">
              Acesso restrito — toda atividade neste sistema é auditada.
            </p>
          </motion.div>
        </div>
      </div>
    </div>
  );
}
