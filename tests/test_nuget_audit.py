import copy
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location(
    "audit", Path(__file__).resolve().parents[1] / "scripts" / "check-nuget-audit.py")
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)

def report():
    return {"version": 1, "parameters": "--vulnerable --include-transitive",
            "sources": [audit.SOURCE],
            "projects": [{"path": "/runner/repo/" + p} for p in sorted(audit.EXPECTED_PROJECTS)]}

def vulnerable(doc, severity, package_kind="transitivePackages"):
    doc["projects"][0]["frameworks"] = [{"framework": "net8.0", package_kind: [
        {"id": "fixture", "resolvedVersion": "8.0.0",
         "vulnerabilities": [{"severity": severity, "advisoryurl": "https://example.test"}]}]}]
    return doc

class AuditGateTests(unittest.TestCase):
    def test_filtered_clean_projects_can_omit_frameworks(self):
        doc = report()
        doc["projects"][1]["frameworks"] = []
        self.assertEqual(sum(audit.check(doc).values()), 0)

    def test_high_and_critical_fail_even_with_clean_projects(self):
        for severity in ("High", "Critical"):
            for kind in ("topLevelPackages", "transitivePackages"):
                with self.subTest(severity=severity, kind=kind), self.assertRaisesRegex(ValueError, "High/critical"):
                    audit.check(vulnerable(report(), severity, kind))

    def test_low_and_moderate_reported(self):
        for severity in ("Low", "Moderate"):
            self.assertEqual(audit.check(vulnerable(report(), severity))[severity.lower()], 1)

    def test_missing_or_duplicate_project_rejected(self):
        for duplicate in (True, False):
            doc = report()
            if duplicate:
                doc["projects"].append(copy.deepcopy(doc["projects"][0]))
            else:
                doc["projects"].pop()
            with self.assertRaises(ValueError):
                audit.check(doc)

    def test_missing_filter_or_source_and_problems_fail(self):
        for patch in ({"parameters": "--vulnerable"}, {"sources": []}, {"problems": ["feed unavailable"]}):
            with self.assertRaises(ValueError):
                audit.check({**report(), **patch})
        doc = report()
        doc["projects"][0]["problems"] = ["feed unavailable"]
        with self.assertRaises(ValueError):
            audit.check(doc)

    def test_malformed_framework_or_advisory_rejected(self):
        for rows in (None, {}, [{"framework": "net8.0", "transitivePackages": [
                {"id": "fixture", "resolvedVersion": "8.0.0"}]}]):
            doc = report()
            doc["projects"][0]["frameworks"] = rows
            with self.assertRaises(ValueError):
                audit.check(doc)
        with self.assertRaises(ValueError):
            audit.check(vulnerable(report(), "unknown"))

if __name__ == "__main__":
    unittest.main()
