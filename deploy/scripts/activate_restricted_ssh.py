#!/usr/bin/env python3
"""Operator-run activation only; no token, response body or credential is printed."""
import argparse
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
import uuid


class ActivationError(Exception):
    pass


class Api:
    def __init__(self, base, token):
        self.base = base.rstrip("/")
        self.token = token

    def call(self, method, path, data=None, expected=(200,)):
        request = urllib.request.Request(
            self.base + path, method=method,
            data=None if data is None else json.dumps(data).encode(),
            headers={"Authorization": "Bearer " + self.token, "Content-Type": "application/json"},
        )
        # Reject redirects: never send a credential to an unexpected endpoint.
        class NoRedirect(urllib.request.HTTPRedirectHandler):
            def redirect_request(self, *args, **kwargs):
                return None
        try:
            with urllib.request.build_opener(NoRedirect).open(request, timeout=30) as response:
                status, body = response.status, response.read()
        except urllib.error.HTTPError as error:
            status, body = error.code, b""
            error.close()
        except (OSError, ValueError):
            raise ActivationError("API connection failed; response omitted") from None
        if status not in expected:
            raise ActivationError(f"Unexpected HTTP {status}; response omitted")
        if not body:
            return None
        try:
            return json.loads(body)
        except (ValueError, UnicodeError):
            raise ActivationError("Invalid API JSON; response omitted") from None


def manifest_validated(manifest, *, expected_count=8):
    identity = str(uuid.UUID(manifest["consumerIdentityId"]))
    entries = manifest["secrets"]
    if expected_count < 1 or len(entries) != expected_count:
        raise ActivationError(f"Exactly {expected_count} SSH key entries are required")
    ids, names = set(), set()
    for entry in entries:
        entry["id"] = str(uuid.UUID(entry["id"]))
        if not entry["name"].endswith("_SSH_KEY") or entry["id"] in ids or entry["name"] in names:
            raise ActivationError("SSH key entries must have unique IDs and *_SSH_KEY names")
        ids.add(entry["id"]); names.add(entry["name"])
    return identity, entries


def verify_read(consumer, secret_id, consumer_id):
    envelope = consumer.call("GET", f"/api/v1/secrets/{secret_id}/value?mode=REVEAL")
    if (envelope.get("identity") != consumer_id or envelope.get("resource") != secret_id
            or envelope.get("accessMode") != "REVEAL"
            or not isinstance(envelope.get("credentials", {}).get("value"), str)
            or not envelope["credentials"]["value"]):
        raise ActivationError("Consumer read verification failed; envelope omitted")
    # Value stays only in this short-lived process; never printed, hashed or persisted.


def activate(manager, consumer, denied_admin, manifest, *, expected_count=8):
    consumer_id, entries = manifest_validated(manifest, expected_count=expected_count)
    accounts = manager.call("GET", "/api/v1/service-accounts")
    if not any(a["id"] == consumer_id and a["name"] == "forgehub-remote-access" and a.get("isActive", True) for a in accounts):
        raise ActivationError("Consumer identity must be active forgehub-remote-access")
    originals = {}
    # Complete preflight before any mutation. Never collect secret values here.
    for entry in entries:
        sid = entry["id"]
        metadata = manager.call("GET", f"/api/v1/secrets/{sid}")
        if (metadata["name"] != entry["name"] or not isinstance(metadata.get("restricted"), bool)
                or metadata["status"] != "Active"):
            raise ActivationError("Metadata mismatch, inactive key, or restriction API not deployed")
        originals[sid] = metadata["restricted"]
        # Prove the denial probe can administer this exact scope; an unrelated token
        # receiving 403 is not evidence that restriction defeats administrative access.
        probe = denied_admin.call("PUT", f"/api/v1/secrets/{sid}/restriction", {"restricted": metadata["restricted"]})
        if probe.get("restricted") != metadata["restricted"]:
            raise ActivationError("Administrative denial-probe preflight failed")
    changed = []
    try:
        # Keep grants on rollback: they are the intended consumer's access and prevent
        # an outage even if a PUT times out after committing on the server.
        for entry in entries:
            manager.call("POST", f"/api/v1/secrets/{entry['id']}/access-grants", {"identityId": consumer_id}, (200, 201))
        for entry in entries:
            verify_read(consumer, entry["id"], consumer_id)
        for entry in entries:
            sid = entry["id"]
            if not originals[sid]:
                changed.append(sid)  # before PUT: commit followed by timeout is ambiguous
                manager.call("PUT", f"/api/v1/secrets/{sid}/restriction", {"restricted": True})
        for entry in entries:
            sid = entry["id"]
            verify_read(consumer, sid, consumer_id)
            denied_admin.call("GET", f"/api/v1/secrets/{sid}/value?mode=REVEAL", expected=(403,))
    except BaseException:
        rollback_failed = []
        for sid in reversed(changed):
            try:
                restored = manager.call("PUT", f"/api/v1/secrets/{sid}/restriction", {"restricted": originals[sid]})
                if restored.get("restricted") != originals[sid]:
                    rollback_failed.append(sid)
            except Exception:
                rollback_failed.append(sid)
        # IDs only, safe for an operator to remediate; never a response body/token.
        if rollback_failed:
            raise ActivationError("Rollback incomplete; consumer grants retained. Restore restriction=false for IDs: " + ", ".join(rollback_failed)) from None
        raise ActivationError("Activation failed; original restriction flags restored and consumer grants retained") from None
    return len(entries)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest", help="JSON consumerIdentityId and active secrets [{id,name}]")
    parser.add_argument("--expected-key-count", type=int, default=8,
                        help="Exact independently verified active inventory count (default: 8)")
    parser.add_argument("--url", default="http://127.0.0.1:8080")
    parser.add_argument("--apply", action="store_true", help="Perform activation; omit for offline manifest validation")
    args = parser.parse_args()
    try:
        with open(args.manifest, encoding="utf-8") as source:
            manifest = json.load(source)
        manifest_validated(manifest, expected_count=args.expected_key_count)
        if not args.apply:
            print("Manifest valid. No network calls or changes. Use --apply during authorized publication.")
            return 0
        url = urllib.parse.urlparse(args.url)
        if url.scheme not in ("http", "https") or url.username or url.password or url.query or url.fragment:
            raise ActivationError("Invalid API URL")
        if url.scheme == "http" and url.hostname not in ("localhost", "127.0.0.1", "::1"):
            raise ActivationError("Plain HTTP is permitted only on loopback")
        tokens = [os.environ[name] for name in ("FORGEVAULT_ACTIVATION_ADMIN_TOKEN", "FORGEVAULT_REMOTE_ACCESS_TOKEN", "FORGEVAULT_DENIED_ADMIN_TOKEN")]
        if any(not token for token in tokens) or tokens[1] in (tokens[0], tokens[2]):
            raise ActivationError("Distinct nonempty consumer and administrator tokens are required")
        count = activate(*(Api(args.url, token) for token in tokens), manifest,
                         expected_count=args.expected_key_count)
        print(f"Verified explicit consumer access and administrative denial for {count} restricted keys. No values emitted.")
        return 0
    except (ActivationError, KeyError, ValueError, OSError):
        # Preserve only our controlled messages; malformed JSON/UUIDs may include input.
        error = sys.exc_info()[1]
        print(str(error) if isinstance(error, ActivationError) else "Invalid manifest, missing token environment variable, or input failure", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
