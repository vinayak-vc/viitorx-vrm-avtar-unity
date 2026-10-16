#!/usr/bin/env python3
"""Moves the whole Viitorx tracking package family to one new version, or marks the current version as released.

    python tools/release/bump_version.py bump 0.2.0 [--packages DIR]      every package.json gets "version": "0.2.0", every dependency on a family package
                                                                           becomes 0.2.0, and every CHANGELOG.md gets a new "## 0.2.0 (unreleased)" section on top
    python tools/release/bump_version.py finalize YYYY-MM-DD [--packages DIR]
                                                                           "## <current version> (unreleased)" becomes "## <current version> (YYYY-MM-DD)" in every CHANGELOG.md

The family has ONE version (release_check.py fails when two packages differ), so a release is always all six. Run release_check.py afterwards.
"""
import argparse
import json
import os
import re
import sys

PREFIX = "com.viitorx.avtar.tracking"
SEMVER = re.compile(r"^\d+\.\d+\.\d+$")
DATE = re.compile(r"^\d{4}-\d{2}-\d{2}$")


def family(packages):
    return sorted(d for d in os.listdir(packages) if d.startswith(PREFIX) and os.path.isfile(os.path.join(packages, d, "package.json")))


def read(path):
    with open(path, encoding="utf-8") as f:
        return f.read()


def write(path, text):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def current_version(packages, names):
    versions = {json.loads(read(os.path.join(packages, n, "package.json")))["version"] for n in names}
    if len(versions) != 1:
        raise SystemExit("the packages do not share one version (%s): fix that first (release_check.py says which)" % ", ".join(sorted(versions)))
    return versions.pop()


def bump(packages, new):
    if not SEMVER.match(new):
        raise SystemExit("'%s' is not a version like 1.2.3" % new)
    names = family(packages)
    old = current_version(packages, names)
    if new == old:
        raise SystemExit("the family is already at %s" % new)
    for n in names:
        path = os.path.join(packages, n, "package.json")
        data = json.loads(read(path))
        data["version"] = new
        for dep in list(data.get("dependencies", {})):
            if dep.startswith(PREFIX):
                data["dependencies"][dep] = new
        write(path, json.dumps(data, indent=2) + "\n")
        changelog = os.path.join(packages, n, "CHANGELOG.md")
        text = read(changelog) if os.path.isfile(changelog) else "# Changelog\n"
        heading = "## %s (unreleased)\n" % new
        if heading in text:
            continue
        head, sep, rest = text.partition("\n## ")
        if sep:
            text = head + "\n" + heading + "- \n\n## " + rest
        else:
            text = text.rstrip("\n") + "\n\n" + heading + "- \n"
        write(changelog, text)
    return old, new


def finalize(packages, date):
    if not DATE.match(date):
        raise SystemExit("'%s' is not a date like 2026-10-17" % date)
    names = family(packages)
    version = current_version(packages, names)
    changed = 0
    for n in names:
        changelog = os.path.join(packages, n, "CHANGELOG.md")
        if not os.path.isfile(changelog):
            continue
        text = read(changelog)
        old = "## %s (unreleased)" % version
        if old in text:
            write(changelog, text.replace(old, "## %s (%s)" % (version, date), 1))
            changed += 1
    return version, changed


def main(argv=None):
    here = os.path.dirname(os.path.abspath(__file__))
    game = os.path.dirname(os.path.dirname(here))
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("command", choices=("bump", "finalize"))
    ap.add_argument("value", help="the new version (bump) or the release date (finalize)")
    ap.add_argument("--packages", default=os.path.join(game, "Packages"))
    a = ap.parse_args(argv)
    packages = os.path.abspath(a.packages)
    if a.command == "bump":
        old, new = bump(packages, a.value)
        print("family moved from %s to %s; fill in the new CHANGELOG sections, then run release_check.py" % (old, new))
    else:
        version, changed = finalize(packages, a.value)
        print("%s marked as released on %s in %d changelog(s)" % (version, a.value, changed))
    return 0


if __name__ == "__main__":
    sys.exit(main())
