"""Offline activation sequencing and rollback tests; all IDs and values are synthetic."""
import contextlib
import io
import unittest
import uuid
from activate_restricted_ssh import ActivationError, activate, manifest_validated


class State:
    def __init__(self):
        self.consumer = str(uuid.uuid4())
        self.entries = [{"id": str(uuid.uuid4()), "name": f"FAKE_{i}_SSH_KEY"} for i in range(8)]
        self.restricted = {e["id"]: False for e in self.entries}
        self.grants = set()
        self.operations = []
        self.leak = False
        self.fail_consumer_after = False
        self.fail_consumer_before = False
        self.fail_put = False
        self.fail_rollback = False

    def manifest(self):
        return {"consumerIdentityId": self.consumer, "secrets": self.entries}

    def client(self, who):
        state = self
        class FakeApi:
            def call(self, method, path, data=None, expected=(200,)):
                state.operations.append((who, method, path, data))
                if path == "/api/v1/service-accounts":
                    return [{"id": state.consumer, "name": "forgehub-remote-access", "isActive": True}]
                sid = path.split("/")[4]
                if method == "PUT":
                    if state.fail_rollback and not data["restricted"] and state.restricted[sid]:
                        raise ActivationError("fake rollback unavailable")
                    state.restricted[sid] = data["restricted"]
                    if who == "manager" and data["restricted"] and state.fail_put:
                        raise ActivationError("fake timeout after committed PUT")
                    return {"restricted": state.restricted[sid]}
                if path.endswith("access-grants"):
                    state.grants.add(sid)
                    return {"id": "fake-grant"}
                if "/value?" in path:
                    if who == "consumer":
                        if state.fail_consumer_before or (state.fail_consumer_after and state.restricted[sid]):
                            raise ActivationError("fake consumer outage")
                        if sid not in state.grants:
                            raise ActivationError("fake missing grant")
                    status = 403 if who == "denied" and state.restricted[sid] and not state.leak else 200
                    if status not in expected:
                        raise ActivationError("fake unexpected status")
                    if status == 403:
                        return None
                    return {"identity": state.consumer, "resource": sid, "accessMode": "REVEAL", "credentials": {"value": "synthetic-secret-never-print"}}
                entry = next(e for e in state.entries if e["id"] == sid)
                return {**entry, "restricted": state.restricted[sid], "status": "Active"}
        return FakeApi()

    def activate(self):
        return activate(*(self.client(who) for who in ("manager", "consumer", "denied")), self.manifest())


class ActivationTests(unittest.TestCase):
    def test_explicit_active_inventory_count_after_key_retirement(self):
        state = State()
        retired = state.entries.pop()["id"]
        count = activate(*(state.client(who) for who in ("manager", "consumer", "denied")),
                         state.manifest(), expected_count=7)
        self.assertEqual(7, count)
        self.assertNotIn(retired, state.grants)
        self.assertFalse(state.restricted[retired])
        self.assertTrue(all(state.restricted[e["id"]] for e in state.entries))
        with self.assertRaises(ActivationError):
            manifest_validated(state.manifest())

    def test_explicit_inventory_count_must_be_positive_and_exact(self):
        for expected in (0, -1, 7, 9):
            with self.assertRaises(ActivationError):
                manifest_validated(State().manifest(), expected_count=expected)

    def test_success_idempotent_order_and_no_output(self):
        state = State()
        output = io.StringIO()
        with contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
            self.assertEqual(8, state.activate())
            self.assertEqual(8, state.activate())
        self.assertEqual("", output.getvalue())
        self.assertTrue(all(state.restricted.values()))
        self.assertEqual(8, len(state.grants))
        first_write = next(i for i, op in enumerate(state.operations) if op[0] == "manager" and op[1] == "PUT")
        self.assertEqual(8, sum(op[0] == "consumer" and "/value?" in op[2] for op in state.operations[:first_write]))

    def test_failed_consumer_before_restriction_changes_nothing(self):
        state = State(); state.fail_consumer_before = True
        with self.assertRaisesRegex(ActivationError, "restored"):
            state.activate()
        self.assertFalse(any(state.restricted.values()))
        self.assertEqual(8, len(state.grants))

    def test_denial_failure_restores_flags_and_retains_consumer_grants(self):
        state = State(); state.leak = True
        original = state.entries[0]["id"]; state.restricted[original] = True
        with self.assertRaisesRegex(ActivationError, "restored"):
            state.activate()
        self.assertEqual({original}, {sid for sid, flag in state.restricted.items() if flag})
        self.assertEqual(8, len(state.grants))

    def test_consumer_failure_after_restriction_rolls_back(self):
        state = State(); state.fail_consumer_after = True
        with self.assertRaisesRegex(ActivationError, "restored"):
            state.activate()
        self.assertFalse(any(state.restricted.values()))

    def test_timeout_after_commit_rolls_back(self):
        state = State(); state.fail_put = True
        with self.assertRaisesRegex(ActivationError, "restored"):
            state.activate()
        self.assertFalse(any(state.restricted.values()))

    def test_rollback_failure_reports_only_ids_and_keeps_grants(self):
        state = State(); state.leak = True; state.fail_rollback = True
        with self.assertRaisesRegex(ActivationError, "Rollback incomplete") as error:
            state.activate()
        self.assertNotIn("synthetic-secret", str(error.exception))
        self.assertEqual(8, len(state.grants))

    def test_manifest_rejects_wrong_count_names_and_duplicates(self):
        for mutation in (lambda s: s.entries.pop(), lambda s: s.entries[0].update(name="OTHER"), lambda s: s.entries[0].update(id=s.entries[1]["id"])):
            state = State(); mutation(state)
            with self.assertRaises(ActivationError):
                manifest_validated(state.manifest())


if __name__ == "__main__":
    unittest.main()
