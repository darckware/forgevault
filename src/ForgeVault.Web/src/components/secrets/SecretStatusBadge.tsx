import { Badge } from "@/components/ui/Badge";
import { statusTone } from "@/lib/format";
import type { SecretStatus } from "@/types/api";

export function SecretStatusBadge({ status }: { status: SecretStatus }) {
  return (
    <Badge tone={statusTone(status)} showLockIcon>
      {status}
    </Badge>
  );
}
