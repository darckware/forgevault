using System.Diagnostics.Metrics;

namespace ForgeVault.Api.Observability;

// docs/modules/11_MCP_REGISTRY.md §11/§1 (open_blocking_questions) — "nenhuma métrica de
// observabilidade específica (ex.: mcp_server_renders_total) foi adicionada". Closes that
// one specifically. Deliberately just System.Diagnostics.Metrics (BCL, zero new NuGet
// dependency) rather than standing up a full OpenTelemetry export pipeline — no exporter
// (Prometheus scrape endpoint, OTLP, etc.) exists anywhere in this project yet, and picking
// one is a bigger infrastructure decision than this gap warrants. This still makes the
// counter real and observable today via `dotnet-counters monitor --process-id <pid>
// ForgeVault.Mcp` with zero extra configuration, and is the exact shape an exporter would
// hook into later (a one-line AddMeter("ForgeVault.Mcp") registration, not a rewrite).
internal static class McpMetrics
{
    private static readonly Meter Meter = new("ForgeVault.Mcp", "1.0.0");

    public static readonly Counter<long> ServerRendersTotal = Meter.CreateCounter<long>(
        "mcp_server_renders_total",
        description: "McpServerAssignment render attempts (REST /render and mcp.render_config), tagged by outcome.");
}
