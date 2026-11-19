"""Line-coverage gate for the core messaging package (NFR-16).

Reads every Cobertura report under a directory and measures only the source files of
src/messaging/Modulus.Messaging itself. The sub-packages (transports, sagas, adapters) are
separate directories and are not counted. When a report holds more than one target (the
merge keeps working), a line covered in any of them counts as covered: the highest hit count per line wins.

Usage: python3 build/check-messaging-coverage.py <coverage-dir> [floor-percent]
"""
import glob
import os
import sys
import xml.etree.ElementTree as ET

PACKAGE_DIR = os.path.join("src", "messaging", "Modulus.Messaging") + os.sep


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    root = sys.argv[1]
    floor = float(sys.argv[2]) if len(sys.argv) > 2 else 85.0

    hits: dict[tuple[str, int], int] = {}
    for report in glob.glob(os.path.join(root, "**", "coverage.cobertura.xml"), recursive=True):
        for cls in ET.parse(report).iter("class"):
            filename = (cls.get("filename") or "").replace("\\", "/")
            if PACKAGE_DIR.replace(os.sep, "/") not in filename:
                continue
            for line in cls.iter("line"):
                key = (filename, int(line.get("number")))
                hits[key] = max(hits.get(key, 0), int(line.get("hits")))

    if not hits:
        print(f"::error::No coverage for {PACKAGE_DIR} under {root}.")
        return 1

    valid = len(hits)
    covered = sum(1 for count in hits.values() if count > 0)
    percent = 100.0 * covered / valid
    print(f"Modulus.Messaging line coverage: {percent:.1f}% ({covered} of {valid} lines, floor {floor:.0f}%)")
    if percent < floor:
        print(f"::error::Modulus.Messaging line coverage {percent:.1f}% is below the {floor:.0f}% floor (NFR-16).")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
