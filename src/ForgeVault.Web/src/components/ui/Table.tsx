import type { ReactNode } from "react";
import { cn } from "@/lib/cn";

export interface TableColumn<T> {
  key: string;
  header: string;
  render?: (row: T) => ReactNode;
  className?: string;
}

export interface TableProps<T> {
  columns: TableColumn<T>[];
  rows: T[];
  keyField: keyof T;
  onRowClick?: (row: T) => void;
  emptyLabel?: string;
}

export function Table<T extends object>({
  columns,
  rows,
  keyField,
  onRowClick,
  emptyLabel = "Nothing here yet.",
}: TableProps<T>) {
  if (rows.length === 0) {
    return <p className="py-6 text-center text-sm text-slate-500">{emptyLabel}</p>;
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-sm">
        <thead>
          <tr className="border-b border-vault-surface-border text-xs uppercase tracking-wide text-slate-500">
            {columns.map((col) => (
              <th key={col.key} className="px-3 py-2 font-medium">
                {col.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => {
            const record = row as Record<string, unknown>;
            return (
              <tr
                key={String(record[keyField as string])}
                onClick={onRowClick ? () => onRowClick(row) : undefined}
                className={cn(
                  "border-b border-vault-surface-border/50 last:border-0",
                  onRowClick && "cursor-pointer hover:bg-vault-surface-dim/60",
                )}
              >
                {columns.map((col) => (
                  <td key={col.key} className={cn("px-3 py-2.5 text-slate-200", col.className)}>
                    {col.render ? col.render(row) : String(record[col.key] ?? "—")}
                  </td>
                ))}
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
