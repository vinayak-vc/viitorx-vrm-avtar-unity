#!/usr/bin/env python3
"""Tests for bump_version.py, on a small copy of a family in a temporary folder.   python tools/release/test_bump_version.py"""
import json
import os
import shutil
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import bump_version as B  # noqa: E402
import release_check as R  # noqa: E402

PASS = 0
FAIL = 0


def check(cond, label, detail=""):
    global PASS, FAIL
    if cond:
        PASS += 1
        print("  PASS %-84s %s" % (label, detail))
    else:
        FAIL += 1
        print("  FAIL %-84s %s" % (label, detail))


def put(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def make(root):
    for name, deps in (("com.viitorx.avtar.tracking.core", {}), ("com.viitorx.avtar.tracking.body", {"com.viitorx.avtar.tracking.core": "0.1.0", "com.unity.animation.rigging": "1.4.1"}),
                       ("com.viitorx.avtar.tracking", {"com.viitorx.avtar.tracking.core": "0.1.0", "com.viitorx.avtar.tracking.body": "0.1.0"})):
        put(os.path.join(root, name, "package.json"), json.dumps({"name": name, "version": "0.1.0", "dependencies": deps}, indent=2))
        put(os.path.join(root, name, "CHANGELOG.md"), "# Changelog\n\n## 0.1.0 (unreleased)\n- first\n")


def load(root, name):
    return json.load(open(os.path.join(root, name, "package.json"), encoding="utf-8"))


def raises(fn, *args):
    try:
        fn(*args)
    except SystemExit as e:
        return str(e)
    return None


tmp = tempfile.mkdtemp(prefix="bump-")
try:
    root = os.path.join(tmp, "Packages")
    make(root)
    print("BUMP")
    old, new = B.bump(root, "0.2.0")
    check((old, new) == ("0.1.0", "0.2.0"), "reports the old and the new version")
    for n in ("com.viitorx.avtar.tracking.core", "com.viitorx.avtar.tracking.body", "com.viitorx.avtar.tracking"):
        check(load(root, n)["version"] == "0.2.0", "%s is at 0.2.0" % n)
    body = load(root, "com.viitorx.avtar.tracking.body")
    check(body["dependencies"]["com.viitorx.avtar.tracking.core"] == "0.2.0", "a dependency on a family package follows the bump")
    check(body["dependencies"]["com.unity.animation.rigging"] == "1.4.1", "a dependency on somebody else's package is NOT touched")
    text = open(os.path.join(root, "com.viitorx.avtar.tracking.core", "CHANGELOG.md"), encoding="utf-8").read()
    check(text.index("## 0.2.0 (unreleased)") < text.index("## 0.1.0 (unreleased)"), "the new changelog section is on top of the old one")
    check("- first" in text, "the old entries are kept")
    B.bump(root, "0.3.0")
    text = open(os.path.join(root, "com.viitorx.avtar.tracking.core", "CHANGELOG.md"), encoding="utf-8").read()
    check(text.count("## 0.3.0 (unreleased)") == 1 and text.count("## 0.2.0 (unreleased)") == 1, "bumping again adds one section, not two")
    check("already at" in (raises(B.bump, root, "0.3.0") or ""), "bumping to the version it is at is refused")
    check("not a version" in (raises(B.bump, root, "v0.4") or ""), "a version that is not major.minor.patch is refused")
    print("MIXED VERSIONS")
    edit = load(root, "com.viitorx.avtar.tracking.body")
    edit["version"] = "9.9.9"
    put(os.path.join(root, "com.viitorx.avtar.tracking.body", "package.json"), json.dumps(edit))
    check("share one version" in (raises(B.bump, root, "1.0.0") or ""), "a family that does not share a version is not bumped")
    check("share one version" in (raises(B.finalize, root, "2026-10-17") or ""), "nor finalized")
    print("FINALIZE")
    root2 = os.path.join(tmp, "Packages2")
    make(root2)
    version, changed = B.finalize(root2, "2026-10-17")
    check(version == "0.1.0" and changed == 3, "three changelogs are marked", "%s %d" % (version, changed))
    text = open(os.path.join(root2, "com.viitorx.avtar.tracking.core", "CHANGELOG.md"), encoding="utf-8").read()
    check("## 0.1.0 (2026-10-17)" in text and "unreleased" not in text, "the heading carries the date and no longer says unreleased")
    check("not a date" in (raises(B.finalize, root2, "17-10-2026") or ""), "a date that is not YYYY-MM-DD is refused")
    print("AGREES WITH THE GATE")
    root3 = os.path.join(tmp, "Packages3")
    make(root3)
    B.bump(root3, "1.0.0")
    versions = {load(root3, n)["version"] for n in B.family(root3)}
    check(versions == {"1.0.0"}, "after a bump every package has one version")
    found = R.run(root3, False, None).problems
    check(not any("do not share one version" in p or "depends on" in p for p in found), "the gate sees no version or dependency mismatch after a bump", "; ".join(found)[:120])
finally:
    shutil.rmtree(tmp, ignore_errors=True)

print("\n" + "-" * 78)
print("%d/%d assertions passed" % (PASS, PASS + FAIL))
sys.exit(1 if FAIL else 0)
