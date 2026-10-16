#!/usr/bin/env python3
"""Prints the `dependencies` lines to paste into a Unity project's Packages/manifest.json to install the Viitorx tracking packages from git.

    python tools/release/git_install_block.py v0.1.0                       core, sidecar, vision, cutout and body at the tag v0.1.0
    python tools/release/git_install_block.py package-v1 vision cutout     only vision and cutout (their dependencies core and sidecar are added)
    python tools/release/git_install_block.py v0.1.0 --apply PATH          merge the lines into the manifest.json at PATH instead of printing them
    options: --repo URL (default: this repository's `origin`), --packages DIR (to read each package's dependencies)

Unity does not follow a git URL written inside a package's own dependencies, so every package, and each package it needs, has to be named in the manifest: this tool names them.
The umbrella package is never included (installed alone from git it cannot find its siblings). UniVRM is not included either: see docs/INSTALL.md.
"""
import argparse
import json
import os
import subprocess
import sys

PREFIX = "com.viitorx.avtar.tracking"
UMBRELLA = PREFIX
DEFAULT_REPO = "https://github.com/vinayak-vc/viitorx-vrm-avtar-unity.git"


def family(packages):
    names = {}
    for d in sorted(os.listdir(packages)):
        path = os.path.join(packages, d, "package.json")
        if d.startswith(PREFIX) and os.path.isfile(path):
            with open(path, encoding="utf-8") as f:
                names[d] = json.load(f)
    return names


def origin_url(here):
    try:
        out = subprocess.run(["git", "-C", here, "remote", "get-url", "origin"], capture_output=True, text=True, timeout=20)
        url = out.stdout.strip()
        return url if out.returncode == 0 and url else DEFAULT_REPO
    except (OSError, subprocess.SubprocessError):
        return DEFAULT_REPO


def closure(manifests, wanted):
    """The wanted packages and, transitively, the family packages they depend on."""
    result, queue = [], list(wanted)
    while queue:
        name = queue.pop(0)
        if name in result:
            continue
        if name not in manifests:
            raise SystemExit("there is no package '%s' (the family: %s)" % (name, ", ".join(sorted(manifests))))
        result.append(name)
        queue.extend(d for d in manifests[name].get("dependencies", {}) if d.startswith(PREFIX))
    return sorted(result)


def block(manifests, wanted, ref, repo):
    names = closure(manifests, wanted)
    if UMBRELLA in names:
        raise SystemExit("the umbrella package cannot be installed from git (its siblings are not found): name the packages you want")
    sep = "?path=/Packages/"
    return {n: "%s%s%s#%s" % (repo, sep, n, ref) for n in names}


def main(argv=None):
    here = os.path.dirname(os.path.abspath(__file__))
    game = os.path.dirname(os.path.dirname(here))
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("ref", help="a tag or a branch of the repository")
    ap.add_argument("packages", nargs="*", help="short names (core, sidecar, vision, cutout, body); none means all five")
    ap.add_argument("--repo", default=None)
    ap.add_argument("--packages-dir", default=os.path.join(game, "Packages"))
    ap.add_argument("--apply", default=None, help="a manifest.json to merge the lines into")
    a = ap.parse_args(argv)
    manifests = family(a.packages_dir)
    shorts = a.packages or ["core", "sidecar", "vision", "cutout", "body"]
    wanted = [s if s.startswith(PREFIX) else PREFIX + "." + s for s in shorts]
    lines = block(manifests, wanted, a.ref, a.repo or origin_url(game))
    if a.apply:
        with open(a.apply, encoding="utf-8") as f:
            manifest = json.load(f)
        manifest.setdefault("dependencies", {}).update(lines)
        manifest["dependencies"] = dict(sorted(manifest["dependencies"].items()))
        with open(a.apply, "w", encoding="utf-8", newline="\n") as f:
            json.dump(manifest, f, indent=2)
            f.write("\n")
        print("%d packages merged into %s" % (len(lines), a.apply))
    else:
        print(json.dumps(lines, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
