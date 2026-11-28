"""Validate NuGet v1 vulnerability-filtered JSON without treating an empty project as failure."""
import json
import sys
from pathlib import Path

EXPECTED_PROJECTS = {
    "src/CrabSenseBE.Api/CrabSenseBE.Api.csproj",
    "src/CrabSenseBE.Application/CrabSenseBE.Application.csproj",
    "src/CrabSenseBE.Domain/CrabSenseBE.Domain.csproj",
    "src/CrabSenseBE.Infrastructure/CrabSenseBE.Infrastructure.csproj",
    "tests/CrabSenseBE.UnitTests/CrabSenseBE.UnitTests.csproj",
}
SOURCE = "https://api.nuget.org/v3/index.json"

def check(report):
    if not isinstance(report, dict) or report.get("version") != 1:
        raise ValueError("Unexpected NuGet audit schema")
    params = report.get("parameters")
    if not isinstance(params, str) or not {"--vulnerable", "--include-transitive"} <= set(params.split()):
        raise ValueError("Required vulnerability/transitive filters missing")
    sources = report.get("sources")
    if not isinstance(sources, list) or SOURCE not in sources:
        raise ValueError("NuGet advisory source missing")
    if report.get("problems"):
        raise ValueError("NuGet audit reported problems")
    projects = report.get("projects")
    if not isinstance(projects, list) or not projects:
        raise ValueError("No audited projects")
    counts = {k: 0 for k in ("low", "moderate", "high", "critical")}
    seen = set()
    for project in projects:
        if not isinstance(project, dict) or project.get("problems"):
            raise ValueError("Invalid project or audit problems")
        path = project.get("path")
        if not isinstance(path, str):
            raise ValueError("Project path missing")
        normalized = path.replace(chr(92), "/")
        matches = [p for p in EXPECTED_PROJECTS if normalized == p or normalized.endswith("/" + p)]
        if len(matches) != 1 or matches[0] in seen:
            raise ValueError("Unexpected or duplicate audited project")
        seen.add(matches[0])
        # Actual SDK 8 filtered artifact omits frameworks for clean projects.
        # Missing frameworks is NOT equivalent to a missing project or source.
        rows = project.get("frameworks", [])
        if not isinstance(rows, list):
            raise ValueError("Invalid framework list")
        names = set()
        for framework in rows:
            if not isinstance(framework, dict) or framework.get("problems"):
                raise ValueError("Invalid framework or audit problems")
            name = framework.get("framework")
            if not isinstance(name, str) or not name or name in names:
                raise ValueError("Invalid or duplicate framework")
            names.add(name)
            for key in ("topLevelPackages", "transitivePackages"):
                packages = framework.get(key, [])
                if not isinstance(packages, list):
                    raise ValueError("Invalid package list")
                for package in packages:
                    if not isinstance(package, dict) or package.get("problems"):
                        raise ValueError("Invalid package or audit problems")
                    if not all(isinstance(package.get(k), str) and package[k] for k in ("id", "resolvedVersion")):
                        raise ValueError("Package identity missing")
                    advisories = package.get("vulnerabilities")
                    if not isinstance(advisories, list) or not advisories:
                        raise ValueError("Filtered package missing advisory details")
                    for advisory in advisories:
                        if not isinstance(advisory, dict):
                            raise ValueError("Invalid advisory")
                        severity = str(advisory.get("severity", "")).lower()
                        if severity not in counts:
                            raise ValueError("Unknown vulnerability severity")
                        counts[severity] += 1
                        print(f"{severity}: {package['id']} {package['resolvedVersion']}")
    if seen != EXPECTED_PROJECTS:
        raise ValueError("Incomplete project audit coverage")
    print(f"Audited {len(seen)} projects; advisory occurrences: {counts}")
    if counts["high"] or counts["critical"]:
        raise ValueError("High/critical NuGet advisories require remediation")
    return counts

if __name__ == "__main__":
    try:
        check(json.loads(Path(sys.argv[1]).read_text(encoding="utf-8-sig")))
    except (OSError, ValueError, KeyError, TypeError, AttributeError, IndexError) as exc:
        print(f"NuGet audit gate failed: {exc}", file=sys.stderr)
        sys.exit(1)
