#!/usr/bin/env python3
"""Summarize VSTest TRX files without third-party dependencies."""
import argparse
from collections import Counter, defaultdict
from datetime import datetime
from decimal import Decimal, InvalidOperation
import json
import os
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET


def duration_seconds(value):
    if not value:
        return Decimal(0)
    try:
        hours, minutes, seconds = value.split(":")
        return Decimal(hours) * 3600 + Decimal(minutes) * 60 + Decimal(seconds)
    except (ValueError, InvalidOperation):
        return Decimal(0)


def local_name(tag):
    return tag.rsplit("}", 1)[-1]


def elements(root, tag):
    return (node for node in root.iter() if local_name(node.tag) == tag)


def read_trx(path):
    root = ET.parse(path).getroot()
    tests = {}
    for unit in elements(root, "UnitTest"):
        method = next(elements(unit, "TestMethod"), None)
        if method is not None:
            tests[unit.get("id")] = (
                method.get("className", "Unknown"),
                method.get("name", unit.get("name", "Unknown")),
            )
    cases = []
    for result in elements(root, "UnitTestResult"):
        cls, name = tests.get(result.get("testId"), ("Unknown", result.get("testName", "Unknown")))
        display_name = result.get("testName") or name
        if display_name.startswith(cls + "."):
            display_name = display_name[len(cls) + 1:]
        cases.append((cls, display_name, result.get("outcome", "Unknown"), duration_seconds(result.get("duration"))))
    times = next(elements(root, "Times"), None)
    wall = None
    if times is not None and times.get("start") and times.get("finish"):
        try:
            start = datetime.fromisoformat(times.get("start").replace("Z", "+00:00"))
            finish = datetime.fromisoformat(times.get("finish").replace("Z", "+00:00"))
            wall = (finish - start).total_seconds()
        except ValueError:
            pass
    return cases, wall


def short_project(path):
    name = str(path)
    for project in ("CSharpGit.Git.Tests", "CSharpGit.Desktop.Tests", "CSharpGit.Application.Tests"):
        if project in name:
            return project.removeprefix("CSharpGit.")
    return path.parent.parent.name.removeprefix("CSharpGit.")


def table(lines, headings, rows):
    lines.append("| " + " | ".join(headings) + " |")
    lines.append("| " + " | ".join("---" for _ in headings) + " |")
    for row in rows:
        lines.append("| " + " | ".join(str(x).replace("|", r"\|").replace("\n", " ") for x in row) + " |")
    lines.append("")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default="tests")
    parser.add_argument("--metrics", default="TestDiagnostics")
    args = parser.parse_args()
    paths = sorted(Path(args.root).rglob("*.trx"))
    groups = defaultdict(list)
    walls = defaultdict(list)
    errors = []
    for path in paths:
        try:
            cases, wall = read_trx(path)
            name = short_project(path)
            groups[name].extend(cases)
            if wall is not None:
                walls[name].append(wall)
        except (ET.ParseError, OSError, ValueError) as exc:
            errors.append(f"{path}: {exc}")
    try:
        sdk = subprocess.check_output(["dotnet", "--version"], text=True).strip()
    except (OSError, subprocess.CalledProcessError):
        sdk = "unavailable"
    lines = [
        "## Test timing report",
        "",
        f"Commit: \`{os.getenv('GITHUB_SHA', 'local')}\`  ",
        f"OS: \`{os.getenv('RUNNER_OS', sys.platform)}\`  ",
        f".NET SDK: \`{sdk}\`",
        "",
        f"TRX files: **{len(paths)}**",
        "",
    ]
    table(lines, ["Project", "Passed", "Failed", "Skipped/other", "Sum of test durations", "TRX wall duration"],
          [(p, sum(o == "Passed" for _, _, o, _ in cases),
            sum(o == "Failed" for _, _, o, _ in cases),
            sum(o not in ("Passed", "Failed") for _, _, o, _ in cases),
            f"{sum((d for _, _, _, d in cases), Decimal(0)):.2f} s",
            f"{sum(walls[p]):.2f} s" if walls[p] else "not available")
           for p, cases in sorted(groups.items())])
    lines.append("Test duration sums are **not** elapsed CI time; tests may overlap. TRX wall durations are per test run, not total job time.")
    lines.append("")
    lines.extend(["### Top 20 slowest test cases (all projects)", ""])
    all_cases = [(project, *case) for project, cases in groups.items() for case in cases]
    all_cases.sort(key=lambda c: c[4], reverse=True)
    table(lines, ["Project", "Test", "Outcome", "Duration"],
          [(project, f"{cls}.{name}", outcome, f"{elapsed:.3f} s")
           for project, cls, name, outcome, elapsed in all_cases[:20]])
    for project, cases in sorted(groups.items()):
        lines.extend([f"### {project}", ""])
        slow = sorted(cases, key=lambda c: c[3], reverse=True)
        table(lines, ["Test", "Outcome", "Duration"],
              [(f"{cls}.{name}", outcome, f"{duration:.3f} s")
               for cls, name, outcome, duration in slow[:20]])
        totals = defaultdict(Decimal)
        for cls, _, _, elapsed in cases:
            totals[cls] += elapsed
        lines.append("**Top 10 classes by summed durations**")
        lines.append("")
        table(lines, ["Class", "Sum"],
              [(cls, f"{elapsed:.2f} s") for cls, elapsed in sorted(totals.items(), key=lambda x: x[1], reverse=True)[:10]])
        lines.append("Tests slower than 1/5/10 s: " + " / ".join(
            str(sum(duration > limit for _, _, _, duration in cases)) for limit in (1, 5, 10)))
        lines.append("")
    # Metrics only count instrumented test helpers. Production Git commands and
    # direct Process.Start calls outside these helpers are NOT represented.
    metric_files = sorted(Path(args.metrics).glob("*.jsonl"))
    metrics = []
    for file in metric_files:
        try:
            for line in file.read_text(encoding="utf-8").splitlines():
                metrics.append(json.loads(line))
        except (OSError, ValueError) as exc:
            errors.append(f"{file}: {exc}")
    lines.extend(["### Instrumented Git fixture metrics", "",
                  "Partial instrumentation only: counts below exclude production-service Git processes and uninstrumented test helpers.", ""])
    by_kind = defaultdict(list)
    for m in metrics:
        by_kind[(m.get("kind", "?"), m.get("phase", "?"))].append(m.get("elapsed_ms", 0))
    table(lines, ["Event", "Phase", "Count", "Total", "Mean"],
          [(kind, phase, len(samples), f"{sum(samples):.0f} ms", f"{sum(samples) / len(samples):.1f} ms")
           for (kind, phase), samples in sorted(by_kind.items())])
    if errors:
        lines.extend(["### Report parsing warnings", ""])
        lines.extend(f"- {error}" for error in errors)
    if not paths:
        lines.append("No TRX files were found. The build or test execution may have failed before producing results.")
    report = "\n".join(lines) + "\n"
    print(report)
    if os.getenv("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
            summary.write(report)


if __name__ == "__main__":
    main()
