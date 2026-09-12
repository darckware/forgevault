using ForgeVault.Api.Authentication;
using ForgeVault.Api.Authorization;
using ForgeVault.Api.Endpoints;
using ForgeVault.Api.Mcp;
using ForgeVault.Application.Auth;
using ForgeVault.Application.Authorization;
using ForgeVault.Application.Security;
using ForgeVault.Infrastructure.Auth;
using ForgeVault.Infrastructure.Authorization;
using ForgeVault.Infrastructure.Persistence;
using ForgeVault.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Enum request/response fields (SecretType, EnvironmentKind, ...) are read/written as
// their string names (e.g. "ApiKey") rather than raw integers — matches what module 03/01
// specs show in every example payload.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddDbContext<ForgeVaultDbContext>(options =>
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("ForgeVault"))
        .UseSnakeCaseNamingConvention());

// M3 (docs/architecture/IMPLEMENTATION_READINESS.md): login + JWT + refresh.
// Fails fast at startup (ValidateOnStart) if Jwt:SigningKey is missing/too short —
// same "don't serve traffic in a broken state" philosophy as LocalFileKeyProvider (M2).
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection("Jwt"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.Configure<MfaEnforcementOptions>(builder.Configuration.GetSection("Auth:Mfa"));
builder.Services.AddSingleton<IAuthorizationHandler, MfaAuthorizationHandler>();

builder.Services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddScoped<ITotpService, TotpService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// M5 (docs/architecture/IMPLEMENTATION_READINESS.md): RBAC over Project/Environment/Secret
// writes and the Secret reveal endpoint.
builder.Services.AddScoped<IPermissionChecker, PermissionChecker>();

// M2/M4 (docs/architecture/IMPLEMENTATION_READINESS.md): envelope encryption wired
// end-to-end. Singleton — LocalFileKeyProvider reads and validates the Master Key file
// once and caches it in memory for the process lifetime (docs/ForgeVault.md §13).
builder.Services.Configure<MasterKeyOptions>(builder.Configuration.GetSection("MasterKey"));
builder.Services.AddSingleton<IKeyManagementProvider, LocalFileKeyProvider>();
builder.Services.AddSingleton<IEnvelopeEncryptionService, AesGcmEnvelopeEncryptionService>();

// M7 (docs/architecture/IMPLEMENTATION_READINESS.md): a request authenticates as either a
// human (JwtBearer, a signed JWT from /auth/login) or a service account
// (ServiceAccountAuthenticationHandler, a raw "fv_sa_..." token, hash-checked against
// service_account_tokens) — a policy scheme picks the right handler per request by sniffing
// the bearer token's prefix, so both flow through the same `Authorization: Bearer <token>`
// header without a second, bespoke header convention.
const string SmartAuthScheme = "SmartAuth";

var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services
    .AddAuthentication(SmartAuthScheme)
    .AddPolicyScheme(SmartAuthScheme, "JWT or Service Account token", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var header = context.Request.Headers.Authorization.ToString();
            var isServiceAccountToken = header.StartsWith("Bearer fv_sa_", StringComparison.Ordinal);
            return isServiceAccountToken
                ? ServiceAccountAuthenticationDefaults.AuthenticationScheme
                : JwtBearerDefaults.AuthenticationScheme;
        };
    })
    .AddJwtBearer(options =>
    {
        // Keep our own JWT claim names ("sub", "email") intact instead of letting the
        // handler remap them to the legacy ClaimTypes XML-schema URIs.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(jwtSection["SigningKey"] ?? string.Empty)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    })
    .AddScheme<AuthenticationSchemeOptions, ServiceAccountAuthenticationHandler>(
        ServiceAccountAuthenticationDefaults.AuthenticationScheme, _ => { });

builder.Services.AddAuthorization(options =>
{
    // Applied to GET /secrets/{id}/value as of M6 — see ForgeVault.Api.Authorization.MfaRequirement.
    options.AddPolicy("RequireMfa", policy => policy.Requirements.Add(new MfaRequirement()));
});

// M8 (docs/modules/09_FORGEHUB_FORGEROUTER_MCP_INTEGRATION.md): native MCP server, same
// process/host as the REST API. Stateless Streamable HTTP is the SDK-recommended remote
// transport (no server-side session affinity needed). RequireAuthorization() below reuses
// SmartAuth as-is — a human JWT or a service:forgehub fv_sa_... token both work unchanged.
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<VaultTools>();

var app = builder.Build();

// Fail fast if the Master Key is missing/misconfigured — same philosophy as JwtOptions'
// ValidateOnStart above: don't start accepting traffic in a broken state
// (LocalFileKeyProvider's constructor throws if the file is absent or not permission 600).
app.Services.GetRequiredService<IKeyManagementProvider>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapAuthEndpoints();
app.MapOrganizationEndpoints();
app.MapProjectEndpoints();
app.MapEnvironmentEndpoints();
app.MapSecretEndpoints();
app.MapServiceAccountEndpoints();
app.MapAuditEndpoints();

app.MapMcp("/mcp").RequireAuthorization();

// M0 (docs/architecture/IMPLEMENTATION_READINESS.md): liveness has no DB dependency.
app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));

// M1: readiness reflects real DB connectivity. Never includes secret data
// (docs/ForgeVault.md §148 / docs/modules/10_RESILIENCE_HA_OPERATIONS.md §7).
app.MapGet("/health/ready", async (ForgeVaultDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    var dependencies = new { postgres = canConnect ? "ok" : "unreachable" };

    return canConnect
        ? Results.Ok(new { status = "ok", dependencies })
        : Results.Json(new { status = "unavailable", dependencies }, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.Run();

// Exposes the top-level-statement-generated Program class to
// ForgeVault.E2E.Tests via WebApplicationFactory<Program>.
public partial class Program;
