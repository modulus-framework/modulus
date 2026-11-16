"""Regression gate for the messaging benchmarks (NFR-06).

Reads the BenchmarkDotNet CSV report of a run and fails when a benchmark's median is more than the tolerance above its
entry in build/benchmark-baseline.json. A benchmark missing from the report, or missing from the baseline, fails too,
so a renamed benchmark cannot silently drop out of the gate.

Usage: python3 build/check-benchmark-regression.py <report-dir> [tolerance-percent]
"""
import csv
import glob
import json
import os
import re
import sys

UNIT_NS = {"ns": 1.0, "us": 1000.0, "µs": 1000.0, "μs": 1000.0, "ms": 1_000_000.0, "s": 1_000_000_000.0}
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BASELINE = os.path.join(ROOT, "build", "benchmark-baseline.json")


def to_nanoseconds(text: str) -> float:
    match = re.fullmatch(r"\s*([0-9.,]+)\s*(ns|µs|μs|us|ms|s)\s*", text)
    if not match:
        raise ValueError(f"Cannot read a duration from '{text}'.")
    return float(match.group(1).replace(",", "")) * UNIT_NS[match.group(2)]


def medians(report_dir: str) -> dict[str, float]:
    found: dict[str, float] = {}
    # Only the messaging benchmarks: other benchmark reports in the same folder have no median column.
    for report in glob.glob(os.path.join(report_dir, "**", "*MessagingBenchmarks-report.csv"), recursive=True):
        with open(report, newline="", encoding="utf-8") as handle:
            for row in csv.DictReader(handle):
                found[row["Method"]] = to_nanoseconds(row["Median"])
    return found


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    tolerance = float(sys.argv[2]) if len(sys.argv) > 2 else 10.0
    with open(BASELINE, encoding="utf-8") as handle:
        baseline = json.load(handle)
    current = medians(sys.argv[1])

    failed = False
    for method, expected in baseline.items():
        if method not in current:
            print(f"::error::Benchmark {method} is missing from the report.")
            failed = True
            continue
        limit = expected["medianNs"] * (1 + tolerance / 100)
        measured = current[method]
        change = 100.0 * (measured - expected["medianNs"]) / expected["medianNs"]
        print(f"{method}: median {measured:.0f} ns, baseline {expected['medianNs']:.0f} ns ({change:+.1f}%), limit {limit:.0f} ns")
        if measured > limit:
            print(f"::error::{method} median {measured:.0f} ns is more than {tolerance:.0f}% above its baseline.")
            failed = True
    for method in current:
        if method not in baseline:
            print(f"::error::Benchmark {method} has no baseline in build/benchmark-baseline.json.")
            failed = True
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
