#!/usr/bin/env python3
"""Tests for release_check.py: a small valid family (core, sidecar, body, umbrella) is built in a temporary folder and must pass; then each rule is broken, one at a time,
and the gate must say so. A rule that cannot fail is not a rule.

    python tools/release/test_release_check.py
"""
import json
import os
import shutil
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import release_check as R  # noqa: E402

PASS = 0
FAIL = 0
LICENSE = "MIT License\n\nCopyright (c) 2026 Example\n"
N = 0


def check(cond, label, detail=""):
    global PASS, FAIL
    if cond:
        PASS += 1
        print("  PASS %-86s %s" % (label, detail))
    else:
        FAIL += 1
        print("  FAIL %-86s %s" % (label, detail))


def put(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def meta(path, guid=None):
    global N
    N += 1
    guid = guid or ("%032x" % N)
    put(path + ".meta", "fileFormatVersion: 2\nguid: %s\n" % guid)


def asmdef(name, refs, constraints=None):
    return json.dumps({"name": name, "references": refs, "defineConstraints": constraints or []}, indent=2)


def make_package(root, name, version, deps, asm=None, samples=None, code=True):
    folder = os.path.join(root, name)
    short = name.split(".")[-1]
    pj = {
        "name": name, "version": version, "displayName": short, "description": "d", "unity": "6000.3",
        "documentationUrl": "https://example.com/x/Packages/%s/README.md" % name,
        "changelogUrl": "https://example.com/x/Packages/%s/CHANGELOG.md" % name,
        "licensesUrl": "https://example.com/x/Packages/%s/LICENSE.md" % name,
        "dependencies": deps,
    }
    if samples:
        pj["samples"] = samples
    put(os.path.join(folder, "package.json"), json.dumps(pj, indent=2))
    put(os.path.join(folder, "README.md"), "# %s\n" % name)
    put(os.path.join(folder, "CHANGELOG.md"), "# Changelog\n\n## %s\n- x\n" % version)
    put(os.path.join(folder, "LICENSE.md"), LICENSE)
    put(os.path.join(folder, "Third Party Notices.md"), "# none\n")
    files = ["package.json", "README.md", "CHANGELOG.md", "LICENSE.md", "Third Party Notices.md"]
    if asm:
        put(os.path.join(folder, "Runtime", asm[0] + ".asmdef"), asmdef(asm[0], asm[1], asm[2] if len(asm) > 2 else None))
        files.append("Runtime/" + asm[0] + ".asmdef")
        put(os.path.join(folder, "Runtime", "A.cs"), "class A {}\n")
        files.append("Runtime/A.cs")
        meta(os.path.join(folder, "Runtime"))
        put(os.path.join(folder, "Runtime.meta"), "fileFormatVersion: 2\nguid: %032x\nfolderAsset: yes\n" % (N + 5000))
    for f in files:
        meta(os.path.join(folder, f))
    return folder


def family(root):
    global N
    N = 0
    os.makedirs(root, exist_ok=True)
    make_package(root, "com.viitorx.avtar.tracking.core", "1.2.3", {}, ("VirtualMirror.Core", []))
    make_package(root, "com.viitorx.avtar.tracking.sidecar", "1.2.3", {"com.viitorx.avtar.tracking.core": "1.2.3", "com.unity.nuget.newtonsoft-json": "3.2.2"},
                 ("VirtualMirror.Sidecar", ["VirtualMirror.Core", "Newtonsoft.Json", "Unity.Burst"]))
    make_package(root, "com.viitorx.avtar.tracking.body", "1.2.3", {"com.viitorx.avtar.tracking.core": "1.2.3", "com.viitorx.avtar.tracking.sidecar": "1.2.3"},
                 ("VirtualMirror.Body", ["VirtualMirror.Core", "VirtualMirror.Sidecar", "VRM10"], ["VIITORX_UNIVRM"]))
    make_package(root, "com.viitorx.avtar.tracking", "1.2.3", {"com.viitorx.avtar.tracking.core": "1.2.3", "com.viitorx.avtar.tracking.sidecar": "1.2.3",
                                                              "com.viitorx.avtar.tracking.body": "1.2.3"})


def problems(root, release=False):
    return R.run(root, release, os.path.join(root, "..", "LICENSE")).problems


def has(found, text):
    return any(text in p for p in found)


def edit_json(path, fn):
    with open(path, encoding="utf-8") as f:
        d = json.load(f)
    fn(d)
    put(path, json.dumps(d, indent=2))


def broken(label, mutate, expect, release=False):
    """Copy the valid family, break one thing, expect exactly the named problem."""
    work = tempfile.mkdtemp(prefix="rc-")
    try:
        root = os.path.join(work, "Packages")
        shutil.copytree(BASE, root)
        put(os.path.join(work, "LICENSE"), LICENSE)
        mutate(root)
        found = problems(root, release)
        check(has(found, expect), label, "(%s)" % (found[0][:90] if found else "no problem reported"))
        return found
    finally:
        shutil.rmtree(work, ignore_errors=True)


WORK = tempfile.mkdtemp(prefix="rc-base-")
BASE = os.path.join(WORK, "Packages")
family(BASE)
put(os.path.join(WORK, "LICENSE"), LICENSE)
P = lambda root, name, *rest: os.path.join(root, name, *rest)  # noqa: E731
CORE, SIDE, BODY, UMB = "com.viitorx.avtar.tracking.core", "com.viitorx.avtar.tracking.sidecar", "com.viitorx.avtar.tracking.body", "com.viitorx.avtar.tracking"

print("A VALID FAMILY")
base = problems(BASE)
check(base == [], "the valid family has nothing to report", "; ".join(base)[:200])

print("package.json")
broken("a name that is not the folder's", lambda r: edit_json(P(r, SIDE, "package.json"), lambda d: d.update(name="com.viitorx.avtar.tracking.other")), "name is")
broken("a missing field", lambda r: edit_json(P(r, SIDE, "package.json"), lambda d: d.pop("description")), "has no 'description'")
broken("a version that is not semantic", lambda r: edit_json(P(r, SIDE, "package.json"), lambda d: d.update(version="1.2")), "not semantic")
broken("two versions in one family", lambda r: edit_json(P(r, BODY, "package.json"), lambda d: d.update(version="1.2.4")), "do not share one version")
broken("a dependency on another version of a family package", lambda r: edit_json(P(r, BODY, "package.json"), lambda d: d["dependencies"].update({CORE: "1.0.0"})), "depends on " + CORE)
broken("a dependency on a family package that is not there", lambda r: edit_json(P(r, BODY, "package.json"), lambda d: d["dependencies"].update({"com.viitorx.avtar.tracking.ghost": "1.2.3"})), "which is not in")
broken("a changelog URL that points at another package", lambda r: edit_json(P(r, SIDE, "package.json"), lambda d: d.update(changelogUrl="https://example.com/x/Packages/%s/CHANGELOG.md" % CORE)), "changelogUrl")
broken("a package.json that is not JSON", lambda r: put(P(r, SIDE, "package.json"), "{ nope"), "not valid JSON")
broken("a sample whose folder is missing", lambda r: edit_json(P(r, SIDE, "package.json"), lambda d: d.update(samples=[{"displayName": "S", "description": "d", "path": "Samples~/S"}])), "does not exist")

print("the umbrella")
broken("an umbrella that leaves a package out", lambda r: edit_json(P(r, UMB, "package.json"), lambda d: d["dependencies"].pop(SIDE)), "does not name " + SIDE)
broken("an umbrella that names something else", lambda r: edit_json(P(r, UMB, "package.json"), lambda d: d["dependencies"].update({"com.viitorx.avtar.tracking.extra": "1.2.3"})), "not a package of the family")
broken("an umbrella with code", lambda r: put(P(r, UMB, "X.asmdef"), asmdef("X", [])), "no code of its own")

print("documents")
broken("no README", lambda r: os.remove(P(r, SIDE, "README.md")), "has no README.md")
broken("no licence", lambda r: os.remove(P(r, BODY, "LICENSE.md")), "has no LICENSE.md")
broken("a licence that differs from the repository's", lambda r: put(P(r, BODY, "LICENSE.md"), "Something else\n"), "differs from the repository's LICENSE")
broken("no third-party notices", lambda r: os.remove(P(r, CORE, "Third Party Notices.md")), "has no 'Third Party Notices.md'")
broken("a changelog without the version's heading", lambda r: put(P(r, SIDE, "CHANGELOG.md"), "# Changelog\n\n## 0.0.1\n"), "no '## 1.2.3' heading")
broken("a changelog that still says unreleased, when releasing", lambda r: put(P(r, SIDE, "CHANGELOG.md"), "# Changelog\n\n## 1.2.3 (unreleased)\n- x\n"), "unreleased", release=True)
unreleased_ok = []
work = tempfile.mkdtemp(prefix="rc-")
try:
    root = os.path.join(work, "Packages")
    shutil.copytree(BASE, root)
    put(os.path.join(work, "LICENSE"), LICENSE)
    put(P(root, SIDE, "CHANGELOG.md"), "# Changelog\n\n## 1.2.3 (unreleased)\n- x\n")
    unreleased_ok = problems(root, release=False)
finally:
    shutil.rmtree(work, ignore_errors=True)
check(unreleased_ok == [], "'(unreleased)' is fine while the release is not being cut", "; ".join(unreleased_ok)[:100])

print("Unity metas")
broken("a file without a meta", lambda r: os.remove(P(r, BODY, "Runtime", "A.cs.meta")), "has no .meta")
broken("a meta without its file", lambda r: put(P(r, BODY, "Runtime", "Gone.cs.meta"), "fileFormatVersion: 2\nguid: %032x\n" % 999999), "orphan meta")
broken("a meta without a guid", lambda r: put(P(r, BODY, "Runtime", "A.cs.meta"), "fileFormatVersion: 2\n"), "no well-formed guid")
broken("a GUID used twice", lambda r: put(P(r, BODY, "Runtime", "A.cs.meta"), open(P(r, CORE, "Runtime", "A.cs.meta")).read()), "is also used by")
broken("a folder without a meta", lambda r: os.remove(P(r, SIDE, "Runtime.meta")), "Runtime has no .meta")

print("assemblies")
broken("a reference to an assembly of the application", lambda r: put(P(r, SIDE, "Runtime", "VirtualMirror.Sidecar.asmdef"), asmdef("VirtualMirror.Sidecar", ["VirtualMirror.Core", "VirtualMirror.App"])), "part of the application")
broken("a reference to the in-process inference plug-in", lambda r: put(P(r, SIDE, "Runtime", "VirtualMirror.Sidecar.asmdef"), asmdef("VirtualMirror.Sidecar", ["VirtualMirror.Core", "Mediapipe.Runtime"])), "part of the application (or an in-process inference plug-in)")
broken("a reference to a package this one does not declare", lambda r: put(P(r, SIDE, "Runtime", "VirtualMirror.Sidecar.asmdef"), asmdef("VirtualMirror.Sidecar", ["VirtualMirror.Core", "VirtualMirror.Body"])), "does not declare")
broken("a reference to nothing known", lambda r: put(P(r, CORE, "Runtime", "VirtualMirror.Core.asmdef"), asmdef("VirtualMirror.Core", ["Some.Where"])), "neither in this package")
broken("a reference by GUID", lambda r: put(P(r, CORE, "Runtime", "VirtualMirror.Core.asmdef"), asmdef("VirtualMirror.Core", ["GUID:0123456789abcdef0123456789abcdef"])), "by GUID")
broken("UniVRM referenced without a define constraint", lambda r: put(P(r, BODY, "Runtime", "VirtualMirror.Body.asmdef"), asmdef("VirtualMirror.Body", ["VirtualMirror.Core", "VRM10"])), "without a define constraint")
broken("UniVRM referenced under only the test constraint", lambda r: put(P(r, BODY, "Runtime", "VirtualMirror.Body.asmdef"), asmdef("VirtualMirror.Body", ["VRM10"], ["UNITY_INCLUDE_TESTS"])), "without a define constraint")
broken("an asmdef that is not JSON", lambda r: put(P(r, CORE, "Runtime", "VirtualMirror.Core.asmdef"), "{ nope"), "not valid JSON")

print("hygiene")
broken("a leftover .orig file", lambda r: put(P(r, SIDE, "Runtime", "A.cs.orig"), "x"), "leftover")
broken("a developer's drive path in a source file", lambda r: put(P(r, SIDE, "Runtime", "A.cs"), "// D:\\Unity\\project\\x\n"), "machine")
broken("a developer's profile path in a document", lambda r: put(P(r, SIDE, "README.md"), "see C:/Users/bob/Documents\n"), "machine")
broken("a developer's name in a document", lambda r: put(P(r, SIDE, "README.md"), "written by Rushit\n"), "machine")
placeholder = tempfile.mkdtemp(prefix="rc-")
try:
    root = os.path.join(placeholder, "Packages")
    shutil.copytree(BASE, root)
    put(os.path.join(placeholder, "LICENSE"), LICENSE)
    put(P(root, SIDE, "README.md"), "for example C:/Users/me/AppData/Local\n")
    check(problems(root) == [], "a placeholder profile path (C:/Users/me/...) in an example is not a finding")
finally:
    shutil.rmtree(placeholder, ignore_errors=True)

print("the real family is not tested here: run release_check.py itself")
print("\n" + "-" * 78)
print("%d/%d assertions passed" % (PASS, PASS + FAIL))
shutil.rmtree(WORK, ignore_errors=True)
sys.exit(1 if FAIL else 0)
