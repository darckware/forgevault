# Restricted secret values

`secrets.restricted` defaults to false. Existing ordinary secrets retain their behavior.
When true, every value read requires an active `SecretAccessGrant` for the authenticated
caller and that secret. No role, including Owner/Admin, supplies value access. Revoking
that grant takes effect on the next request; nothing is cached by the authorizer.
Metadata remains visible to the existing scope roles/grantees. Expired, revoked or otherwise
inactive secrets still cannot be revealed even with a grant.

Management API:

- `PUT /api/v1/secrets/{id}/restriction` with `{"restricted":true|false}`.
- MCP `admin.secret.set_restriction {secretId, restricted}`.
- Both require `SecretWrite` in scope and return metadata only, including `restricted`.
- Changes emit `SECRET_RESTRICTION_CHANGED` with actor, secret ID, request ID and the
  previous/new boolean in audit metadata, atomically with the change. An unchanged flag
  is an idempotent no-op, without a duplicate change event.
- Create/update/rotate never change the flag implicitly. Rotation remains available to
  existing writers and returns metadata without the value.
- MCP `capability.check` with `SecretReadValue` now checks the actual per-secret policy,
  including direct grants and restriction. Other permissions retain scope RBAC behavior.

## Value paths checked

The only API decryption sites are REST `/secrets/{id}/value`, MCP `credential.request`,
and the shared MCP assignment renderer (REST `/mcp-assignments/{id}/render` and MCP
`mcp.render_config`). All enforce this policy for restricted secrets, including an
identity rendering its **own** assignment: the assignment alone is no longer sufficient.
The existing assignment-based self-render behavior remains for ordinary secrets.

`/secrets/{id}/versions` returns version metadata (ID/version/algorithm/creator/time),
never ciphertext or plaintext. No historical-value route exists. Lists, metadata,
create/update/rotate/revoke/impact responses do not decrypt or return values. CLI `fv
export` and `fv exec` reveal through the same REST `/value` endpoint, so a denied read
fails export/injection. Infrastructure database backups contain encrypted version rows;
this policy is an API authorization boundary, not a database/host security boundary.

Denied REST/MCP value reads and render attempts emit `FAILED_ACCESS`; neither these nor
restriction/grant events contain values. ForgeHub's `host-bridge/forgevault_proxy.py`
continues using `GET /api/v1/secrets/{id}/value?mode=REVEAL` and
`credentials.value`; grant its `forgehub-remote-access` identity explicitly.

## Governance limitation and proposed alerts

This does **not** prevent a privileged writer from changing policy. Owner/Admin may
unrestrict a key or grant themselves access and then read it; both operations are audited.
The current permission matrix also grants `SecretWrite` to SecurityAdmin, ProjectAdmin,
Developer, Operator, Agent and ServiceAccount in their assigned scope. Those identities
can change restrictions and grants too. Use narrow grants without scope roles for the
consumer. Role-management changes, rotation to a known replacement value, database
access, backups, and host/master-key access are outside the value-read restriction.

Proposed monitoring (not installed by this change):

1. Alert on every `SECRET_RESTRICTION_CHANGED` for the protected SSH key inventory,
   especially true → false; include actor/request/time and booleans only.
2. Alert on `SECRET_ACCESS_GRANTED`/`SECRET_ACCESS_REVOKED` joined from resource ID to
   `secret_access_grants.secret_id`, for every protected key. Escalate self-grants or any
   grantee other than `forgehub-remote-access`; retain grant rows for correlation.
3. Alert on `FAILED_ACCESS` for those keys, including render context; correlate a later
   `SECRET_REVEAL` by the same actor with recent restriction/grant changes.
4. Monitor role changes and key rotation as additional governance events. Send alerts to
   the operator's chosen destination only after explicit configuration/authorization.

## Publication and activation (operator only)

This branch does not deploy, apply production migrations or activate restrictions.
Review and publish application code plus migration `20261108153222_AddRestrictedSecrets`
together. Migrate **before** routing traffic to the new application; the new authorizer
queries the column. The column is additive with default false, so old code can coexist
only **before activation**. Old binaries must not serve requests after restrictions are
activated, since they ignore the column. Do not roll back the application to old binaries
with protected keys still active; coordinate a safe service-level rollback instead.

Prepare an operator-owned JSON manifest containing no values/tokens:

```json
{
  "consumerIdentityId": "<forgehub-remote-access UUID>",
  "secrets": [
    {"id": "<secret UUID>", "name": "<HOST>_SSH_KEY"}
  ]
}
```

Supply exactly the eight approved key IDs/names. Offline validation:

At publication on 2026-11-08, live metadata and ForgeHub machine references identify seven
active keys: the former `srv-app05` key is revoked and unreferenced. Do not reactivate it.
An operator may supply `--expected-key-count 7` to validation and activation after independently
reconciling the manifest. The default remains eight; mismatched, empty and duplicate inventories
are rejected before network mutations. Nine activation/rollback tests cover this option.

```sh
python3 deploy/scripts/activate_restricted_ssh.py /secure/path/ssh-key-manifest.json
```

For authorized activation, inject tokens into the process environment securely:
`FORGEVAULT_ACTIVATION_ADMIN_TOKEN`, `FORGEVAULT_REMOTE_ACCESS_TOKEN` and
`FORGEVAULT_DENIED_ADMIN_TOKEN`. The third token must belong to a real Admin/Owner in
those keys' scope and must have no explicit grant for them. It may be the management
token. No token is a command-line argument. Disable shell tracing. Then run:

```sh
python3 deploy/scripts/activate_restricted_ssh.py /secure/path/ssh-key-manifest.json --apply
```

The script verifies the consumer account's name, exact key names/status/flag presence and
that the denial probe has write access to each secret (using an unchanged restriction
request). It grants all eight keys idempotently, verifies all consumer reads without
printing/hashing/storing values, restricts them, verifies consumer reads again, and
requires HTTP 403 for every administrative probe. Do not provide a preexisting explicitly
granted Admin as the denial probe; that correctly causes activation to fail.

On a failed verification or ambiguous PUT timeout, restore only flags changed by this
run to their original values. Retain intended consumer grants even on failure, preserving
ForgeHub's access. Flags already restricted on entry remain restricted. Rollback failure
returns a nonzero exit code and only the affected IDs; retain consumer grants and repair
those flags using the management identity before retrying. Abrupt process termination
or a sustained API outage cannot guarantee rollback; inspect metadata and rerun after
recovery. Run in a single operator session without concurrent policy changes.

After success verify the ForgeHub remote-access flow with its normal identity, confirm
audit events/alerts, and discard the activation process environment. No secret-bearing
responses should be pasted into a terminal, ticket or report.

## Proposed skill text (not installed)

Replace the blanket claim "Org Admin reads every secret" with:

> Org Admin reads ordinary secrets through scope RBAC. For `restricted=true`, all
> identities, including Owner/Admin, need an explicit active `admin.secret.grant_access`
> for that key. Metadata and rotation remain available through existing scope permissions.
> Change restrictions with `admin.secret.set_restriction {secretId, restricted}`; changes
> are audited as `SECRET_RESTRICTION_CHANGED`. Administrative writers can still unrestrict
> or self-grant, so this is an audited policy boundary rather than absolute isolation.
> MCP assignment self-render also requires an explicit grant for restricted values.
> `capability.check` with `SecretReadValue` evaluates restriction and direct grants.
> Grant `forgehub-remote-access` the protected SSH keys before activation. Only activate
> using the reviewed operator script after publication; never print values while verifying.

The installed skill remains untouched. Synchronizing runtime skill copies requires its
own operator-authorized publication step.

## Verification for this change

- `dotnet build src/ForgeVault.Api --no-restore`: passes, zero warnings/errors.
- `dotnet test ForgeVault.slnx -m:1 --no-restore --verbosity quiet`: **100 passed**
  (22 E2E, 4 integration, 57 security, 2 restricted HTTP/MCP cases, 15 unit).
  Used a freshly created temporary PostgreSQL container at loopback port 55437, database
  `forgevault_restricted_test`, and a synthetic 32-byte master key. Migrations, including
  the new column, were applied only to that isolated database. Existing test projects
  emit EF Relational 10.0.4/10.0.12 reference warnings; the new test project pins 10.0.12.
- `python3 -m unittest discover -s deploy/scripts -p test_activate_restricted_ssh.py -v`:
  **7 passed**, using only a fake API. Covers idempotence, correct ordering, failure
  before/after restriction, timeout after a committed PUT, rollback failure and validation.
- `git diff --check`: passes.

The initial full run had one expected MCP tool-inventory assertion failure caused by the
new tool; the inventory was updated and the final complete run passed. No production
migration, deployment, credential read, activation, live bridge request or alert delivery
was executed in the initial implementation session. Publication is recorded in the companion
ForgeHub `docs/implementation/2026-11-08-remote-access-continuation.md` and its private release manifest.
ForgeHub proxy integration verification belongs to the companion ForgeHub
change; the API tests here verify its unchanged REST envelope contract with fake values.
