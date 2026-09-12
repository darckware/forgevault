import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useLocation, useNavigate } from "react-router-dom";
import { Input } from "@/components/ui/Input";
import { Button } from "@/components/ui/Button";
import { ApiError } from "@/lib/api";
import { useLogin } from "@/hooks/useAuth";

const schema = z.object({
  email: z.string().email(),
  password: z.string().min(1, "Required"),
  mfaCode: z.string().optional(),
});
type FormValues = z.infer<typeof schema>;

export function LoginPage() {
  const [needsMfa, setNeedsMfa] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const login = useLogin();
  const navigate = useNavigate();
  const location = useLocation();

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  const onSubmit = async (values: FormValues) => {
    setServerError(null);
    try {
      await login.mutateAsync(values);
      const from = (location.state as { from?: { pathname: string } } | null)?.from?.pathname ?? "/organizations";
      navigate(from, { replace: true });
    } catch (err) {
      if (err instanceof ApiError) {
        // The API doesn't distinguish "wrong password" from "needs MFA" beyond a generic
        // error code — once a plain attempt 401s, reveal the MFA field for a retry.
        setNeedsMfa(true);
        setServerError(err.body?.error ?? "Login failed");
      }
    }
  };

  return (
    <div className="flex min-h-screen items-center justify-center bg-vault-bg-deep px-4">
      <div className="w-full max-w-sm rounded-lg border border-vault-surface-border bg-vault-surface-dim/60 p-8">
        <div className="mb-6 flex flex-col items-center gap-2">
          <img src="/forgevault-icon.svg" alt="ForgeVault" className="h-14 w-14" />
          <h1 className="text-lg font-semibold text-slate-100">ForgeVault</h1>
        </div>
        <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-3">
          <Input label="Email" type="email" {...register("email")} error={errors.email?.message} />
          <Input label="Password" type="password" {...register("password")} error={errors.password?.message} />
          {needsMfa && <Input label="MFA code" placeholder="123456" {...register("mfaCode")} />}
          {serverError && <p className="text-sm text-vault-danger">{serverError}</p>}
          <Button type="submit" isLoading={login.isPending} className="mt-2">
            Entrar
          </Button>
        </form>
      </div>
    </div>
  );
}
