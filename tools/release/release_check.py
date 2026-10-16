#!/usr/bin/env python3
"""The release gate for the Viitorx tracking packages (`Packages/com.viitorx.avtar.tracking*`).

    python tools/release/release_check.py [--packages DIR] [--release] [--root-license FILE]

Reads the packages the way Unity's Package Manager and a reviewer would, and reports everything that would make a release wrong. It changes nothing.
Exit 0 when there is nothing to report. `--release` adds the checks that only matter for the version that is being published (no "(unreleased)" in the
changelog of the version being released).

What it checks, per package and across the family:
  * package.json: valid, `name` equals the folder, a semantic version, the fields Unity shows, the three URLs end in the package's OWN README / CHANGELOG / LICENSE
    and those files exist; every package of the family has the SAME version; every dependency on a family package is exactly that version; the umbrella names exactly the
    other packages of the family and nothing else.
  * README.md, CHANGELOG.md (with a heading for the version), LICENSE.md (the same text as the repository's LICENSE), Third Party Notices.md.
  * Unity metas: every file and folder (outside `name~` folders, but including the contents of `Samples~`) has a `.meta`, no `.meta` is without its file, no GUID occurs
    twice, and the GUID of a meta is well formed.
  * assemblies: every `.asmdef` is valid JSON; every reference resolves to another assembly of the same package, of a package this one DECLARES as a dependency, or to a
    known external; a reference to an external that is not a declared dependency (UniVRM, which is not on Unity's registry) must be under a define constraint, so the
    package still compiles without it; no reference to an assembly of the application (`VirtualMirror.App`, ...) and none to the in-process inference plug-ins.
  * samples: every `samples[].path` of package.json exists and has a README.
  * hygiene: no leftover `.orig`, `.bak`, `.tmp`; no absolute path of a developer's machine in any text file.
"""
import argparse
import json
import os
import re
import sys

PREFIX = "com.viitorx.avtar.tracking"
UMBRELLA = PREFIX
SEMVER = re.compile(r"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$")
GUID = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.M)
REQUIRED_FIELDS = ("name", "version", "displayName", "description", "unity", "dependencies")
# assemblies a package may reference without declaring a package for them: Unity's own, and what the declared registry dependencies bring
EXTERNAL_PREFIXES = ("Unity.", "UnityEngine", "UnityEditor", "Newtonsoft.Json")
# externals that come from a package that is NOT on Unity's registry: allowed only under a define constraint
OPTIONAL_EXTERNALS = ("VRM10", "UniGLTF")
FORBIDDEN_REFERENCES = ("VirtualMirror.App", "VirtualMirror.IO", "VirtualMirror.Settings", "VirtualMirror.UI", "VirtualMirror.Camera", "VirtualMirror.Rendering",
                        "VirtualMirror.Diagnostics", "VirtualMirror.SkeletonShow", "VirtualMirror.Experiences", "VirtualMirror.Tracking.Inference", "Mediapipe.Runtime",
                        "Unity.InferenceEngine", "Utility")
LEFTOVER = (".orig", ".bak", ".tmp", ".rej")
# a drive path into a developer's profile or checkout, or a developer's name; the placeholder user names examples use (me, user, name, ...) are not flagged
MACHINE_PATH = re.compile(r"(?i)([a-z]:[\\/](?:unity|viitorx[^\\/\s]*)[\\/]|[a-z]:[\\/]users[\\/](?!(?:me|user|name|username|test|example|you|public|default)(?![A-Za-z0-9_]))[^\\/\s]+|rushit|vaghela)")
TEXT_EXTENSIONS = (".cs", ".md", ".json", ".asmdef", ".txt", ".shader", ".hlsl", ".cginc", ".unity", ".mat", ".yml", ".yaml", ".meta", ".uss", ".uxml")


class Report:
    def __init__(self):
        self.problems = []

    def add(self, where, text):
        self.problems.append("%s: %s" % (where, text))


def read(path):
    with open(path, encoding="utf-8-sig", errors="replace") as f:
        return f.read()


def load_json(path, where, report):
    try:
        return json.loads(read(path))
    except (OSError, ValueError) as e:
        report.add(where, "is not valid JSON (%s)" % e)
        return None


def package_dirs(packages):
    return sorted(d for d in os.listdir(packages) if d.startswith(PREFIX) and os.path.isdir(os.path.join(packages, d)))


def walk_package(folder):
    """Every (relative path, is_dir) in the package that Unity imports: `name~` folders are skipped, except the contents of `Samples~`."""
    found = []
    for d, dirs, files in os.walk(folder):
        rel_dir = os.path.relpath(d, folder).replace("\\", "/")
        inside_samples = "Samples~" in rel_dir.split("/")
        keep = []
        for name in dirs:
            if name.endswith("~") and name != "Samples~":
                continue
            keep.append(name)
        dirs[:] = keep
        for name in dirs + files:
            if name == "Samples~":
                continue
            rel = name if rel_dir == "." else rel_dir + "/" + name
            found.append((rel, os.path.isdir(os.path.join(d, name))))
    return found


def check_metas(folder, name, report, guids):
    entries = walk_package(folder)
    paths = {rel for rel, _ in entries}
    for rel, is_dir in entries:
        if rel.endswith(".meta"):
            asset = rel[:-5]
            if asset not in paths:
                report.add(name, "%s has no file or folder beside it (an orphan meta)" % rel)
                continue
            text = read(os.path.join(folder, rel))
            m = GUID.search(text)
            if not m:
                report.add(name, "%s has no well-formed guid" % rel)
                continue
            if m.group(1) in guids and guids[m.group(1)] != name + "/" + asset:
                report.add(name, "GUID %s of %s is also used by %s" % (m.group(1), asset, guids[m.group(1)]))
            guids[m.group(1)] = name + "/" + asset
        elif rel + ".meta" not in paths:
            report.add(name, "%s has no .meta (Unity would create one with a new GUID on import)" % rel)


def check_hygiene(folder, name, report):
    for d, dirs, files in os.walk(folder):
        for f in files:
            full = os.path.join(d, f)
            rel = os.path.relpath(full, folder).replace("\\", "/")
            base = f[:-5] if f.endswith(".meta") else f
            if base.endswith(LEFTOVER):
                report.add(name, "%s is a leftover file" % rel)
            if f.lower().endswith(TEXT_EXTENSIONS) and os.path.getsize(full) < 5 * 1024 * 1024:
                text = read(full)
                m = MACHINE_PATH.search(text)
                if m:
                    report.add(name, "%s contains a path or name of a developer's machine: %r" % (rel, m.group(0)))


def asmdefs_of(folder):
    found = {}
    for d, dirs, files in os.walk(folder):
        dirs[:] = [x for x in dirs if not (x.endswith("~") and x != "Samples~")]
        for f in files:
            if f.endswith(".asmdef"):
                found[os.path.join(d, f)] = os.path.relpath(os.path.join(d, f), folder).replace("\\", "/")
    return found


def check_assemblies(name, folder, package_json, family_assemblies, owner_of, report):
    declared = set(package_json.get("dependencies", {}).keys())
    own = {}
    for path, rel in asmdefs_of(folder).items():
        data = load_json(path, name + "/" + rel, report)
        if data is not None:
            own[data.get("name", "")] = (rel, data)
    for asm_name, (rel, data) in own.items():
        where = "%s/%s" % (name, rel)
        constrained = bool(data.get("defineConstraints")) and any(c != "UNITY_INCLUDE_TESTS" for c in data.get("defineConstraints", []))
        for ref in data.get("references", []):
            if ref.startswith("GUID:"):
                report.add(where, "references an assembly by GUID (%s): use its name, so the reference reads in review" % ref)
                continue
            if ref in FORBIDDEN_REFERENCES:
                report.add(where, "references '%s', which is part of the application (or an in-process inference plug-in), not of the package family" % ref)
                continue
            if ref in own:
                continue
            owner = owner_of.get(ref)
            if owner is not None:
                if owner != name and owner not in declared:
                    report.add(where, "references '%s' of %s, which package.json does not declare as a dependency" % (ref, owner))
                continue
            if ref.startswith(EXTERNAL_PREFIXES):
                continue
            if ref in OPTIONAL_EXTERNALS:
                if not constrained:
                    report.add(where, "references '%s' (UniVRM, not on Unity's registry) without a define constraint: the package would not compile without it" % ref)
                continue
            report.add(where, "references '%s', which is neither in this package, a declared dependency, nor a known external" % ref)


def check_package(packages, name, report, versions, guids, owner_of, root_license):
    folder = os.path.join(packages, name)
    pj_path = os.path.join(folder, "package.json")
    if not os.path.isfile(pj_path):
        report.add(name, "has no package.json")
        return None
    pj = load_json(pj_path, name + "/package.json", report)
    if pj is None:
        return None
    for field in REQUIRED_FIELDS:
        if field not in pj or pj[field] in ("", None):
            report.add(name + "/package.json", "has no '%s'" % field)
    if pj.get("name") != name:
        report.add(name + "/package.json", "name is '%s' but the folder is '%s'" % (pj.get("name"), name))
    version = pj.get("version", "")
    if not SEMVER.match(str(version)):
        report.add(name + "/package.json", "version '%s' is not semantic (major.minor.patch)" % version)
    versions[name] = version
    if not os.path.isfile(os.path.join(folder, "Third Party Notices.md")):
        report.add(name, "has no 'Third Party Notices.md' (say so even when there is nothing to attribute)")
    for key, filename in (("documentationUrl", "README.md"), ("changelogUrl", "CHANGELOG.md"), ("licensesUrl", "LICENSE.md")):
        url = pj.get(key, "")
        if not url:
            report.add(name + "/package.json", "has no '%s'" % key)
        elif not url.endswith("/%s/%s" % (name, filename)):
            report.add(name + "/package.json", "%s '%s' does not end in /%s/%s (it must point at this package's own file)" % (key, url, name, filename))
        if not os.path.isfile(os.path.join(folder, filename)):
            report.add(name, "has no %s" % filename)
    license_path = os.path.join(folder, "LICENSE.md")
    if os.path.isfile(license_path) and root_license is not None and read(license_path).strip() != root_license.strip():
        report.add(name, "LICENSE.md differs from the repository's LICENSE")
    changelog = os.path.join(folder, "CHANGELOG.md")
    if os.path.isfile(changelog) and not re.search(r"^## %s\b" % re.escape(str(version)), read(changelog), re.M):
        report.add(name, "CHANGELOG.md has no '## %s' heading" % version)
    for sample in pj.get("samples", []):
        path = sample.get("path", "")
        if not path or not os.path.isdir(os.path.join(folder, path)):
            report.add(name + "/package.json", "sample '%s' points at '%s', which does not exist" % (sample.get("displayName"), path))
        elif not os.path.isfile(os.path.join(folder, path, "README.md")):
            report.add(name, "sample '%s' has no README.md" % sample.get("displayName"))
    check_metas(folder, name, report, guids)
    check_hygiene(folder, name, report)
    return pj


def run(packages, release=False, root_license_path=None):
    report = Report()
    if not os.path.isdir(packages):
        report.add(packages, "is not a folder")
        return report
    root_license = read(root_license_path) if root_license_path and os.path.isfile(root_license_path) else None
    names = package_dirs(packages)
    if not names:
        report.add(packages, "holds no %s* package" % PREFIX)
        return report
    versions, guids, manifests = {}, {}, {}
    owner_of = {}
    for name in names:
        for path in asmdefs_of(os.path.join(packages, name)).keys():
            try:
                owner_of[json.loads(read(path)).get("name", "")] = name
            except ValueError:
                pass
    for name in names:
        manifests[name] = check_package(packages, name, report, versions, guids, owner_of, root_license)
    family_versions = {v for v in versions.values() if v}
    if len(family_versions) > 1:
        report.add("family", "the packages do not share one version: %s" % ", ".join("%s %s" % (n, versions[n]) for n in sorted(versions)))
    version = sorted(family_versions)[0] if family_versions else ""
    for name, pj in manifests.items():
        if pj is None:
            continue
        for dep, dep_version in pj.get("dependencies", {}).items():
            if dep.startswith(PREFIX):
                if dep not in names:
                    report.add(name + "/package.json", "depends on %s, which is not in %s" % (dep, packages))
                elif dep_version != versions.get(dep):
                    report.add(name + "/package.json", "depends on %s %s but that package is %s" % (dep, dep_version, versions.get(dep)))
        check_assemblies(name, os.path.join(packages, name), pj, set(), owner_of, report)
    umbrella = manifests.get(UMBRELLA)
    if umbrella is None:
        report.add("family", "there is no umbrella package %s" % UMBRELLA)
    else:
        named = {d for d in umbrella.get("dependencies", {}) if d.startswith(PREFIX)}
        expected = {n for n in names if n != UMBRELLA}
        for missing in sorted(expected - named):
            report.add(UMBRELLA + "/package.json", "does not name %s" % missing)
        for extra in sorted(named - expected):
            report.add(UMBRELLA + "/package.json", "names %s, which is not a package of the family" % extra)
        if asmdefs_of(os.path.join(packages, UMBRELLA)):
            report.add(UMBRELLA, "has an assembly: the umbrella has no code of its own")
    if release:
        for name in names:
            changelog = os.path.join(packages, name, "CHANGELOG.md")
            if os.path.isfile(changelog) and re.search(r"^## %s\b[^\n]*unreleased" % re.escape(version), read(changelog), re.M | re.I):
                report.add(name, "CHANGELOG.md still says '%s (unreleased)': a release is not unreleased" % version)
    return report


def main(argv=None):
    here = os.path.dirname(os.path.abspath(__file__))
    game = os.path.dirname(os.path.dirname(here))
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--packages", default=os.path.join(game, "Packages"))
    ap.add_argument("--root-license", default=os.path.join(game, "LICENSE"))
    ap.add_argument("--release", action="store_true", help="also require that the changelogs no longer say unreleased")
    a = ap.parse_args(argv)
    report = run(os.path.abspath(a.packages), a.release, a.root_license)
    names = package_dirs(os.path.abspath(a.packages)) if os.path.isdir(a.packages) else []
    if report.problems:
        for p in report.problems:
            print("PROBLEM  " + p)
        print("\n%d problem(s) in %d package(s)." % (len(report.problems), len(names)))
        return 1
    print("release_check: %d packages, nothing to report." % len(names))
    return 0


if __name__ == "__main__":
    sys.exit(main())
