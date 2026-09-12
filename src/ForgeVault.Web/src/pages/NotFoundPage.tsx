import { Link } from "react-router-dom";

export function NotFoundPage() {
  return (
    <div className="flex flex-col items-center gap-3 py-16 text-center">
      <h1 className="text-2xl font-semibold text-slate-100">404</h1>
      <p className="text-sm text-slate-500">This page doesn't exist.</p>
      <Link to="/organizations" className="text-sm text-vault-accent-bright hover:underline">
        Back to Organizations
      </Link>
    </div>
  );
}
