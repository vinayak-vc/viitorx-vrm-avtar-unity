#!/usr/bin/env python3
"""Tests for git_install_block.py.   python tools/release/test_git_install_block.py"""
import json
import os
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import git_install_block as G  # noqa: E402

PASS = 0
FAIL = 0
P = "com.viitorx.avtar.tracking"
REPO = "https://example.com/r.git"


def check(cond, label, detail=""):
    global PASS, FAIL
    if cond:
        PASS += 1
        print("  PASS %-84s %s" % (label, detail))
    else:
        FAIL += 1
        print("  FAIL %-84s %s" % (label, detail))


def raises(fn):
    try:
        fn()
    except SystemExit as e:
        return str(e)
    return None


manifests = {
    P: {"dependencies": {P + ".core": "1.0.0", P + ".vision": "1.0.0"}},
    P + ".core": {"dependencies": {}},
    P + ".sidecar": {"dependencies": {P + ".core": "1.0.0", "com.unity.nuget.newtonsoft-json": "3.2.2"}},
    P + ".vision": {"dependencies": {P + ".core": "1.0.0", P + ".sidecar": "1.0.0"}},
    P + ".cutout": {"dependencies": {P + ".core": "1.0.0", P + ".vision": "1.0.0", "com.unity.render-pipelines.universal": "17.3.0"}},
}

print("THE CLOSURE")
check(G.closure(manifests, [P + ".vision"]) == [P + ".core", P + ".sidecar", P + ".vision"], "vision brings core and sidecar")
check(G.closure(manifests, [P + ".cutout"]) == [P + ".core", P + ".cutout", P + ".sidecar", P + ".vision"], "cutout brings vision, which brings core and sidecar")
check(G.closure(manifests, [P + ".core", P + ".core"]) == [P + ".core"], "a package named twice is named once")
check("no package" in (raises(lambda: G.closure(manifests, [P + ".ghost"])) or ""), "an unknown package is refused, with the family listed")
check(all(not d.startswith("com.unity") for d in G.closure(manifests, [P + ".cutout"])), "somebody else's packages are not in the block")

print("THE BLOCK")
b = G.block(manifests, [P + ".vision"], "v1.2.3", REPO)
check(b[P + ".core"] == REPO + "?path=/Packages/" + P + ".core#v1.2.3", "a line is repo ?path=/Packages/<name> #ref")
check(set(b) == {P + ".core", P + ".sidecar", P + ".vision"}, "exactly the closure")
check("cannot be installed from git" in (raises(lambda: G.block(manifests, [P], "v1", REPO)) or ""), "the umbrella is refused")
check(P not in G.block(manifests, [P + ".vision"], "v1", REPO), "the umbrella is never pulled in by a dependency")

print("THE COMMAND LINE")
d = tempfile.mkdtemp(prefix="gib-")
m = os.path.join(d, "manifest.json")
json.dump({"dependencies": {"com.unity.ugui": "2.0.0", P + ".core": "OLD", "com.zzz.last": "1.0.0"}}, open(m, "w"))
real = G.family(os.path.join(os.path.dirname(os.path.dirname(HERE)), "Packages"))
check(len(real) == 6, "the real family has six packages", ", ".join(sorted(k.split(".")[-1] for k in real)))
G.main(["v9.9.9", "vision", "--repo", REPO, "--apply", m])
merged = json.load(open(m))["dependencies"]
check(merged["com.unity.ugui"] == "2.0.0", "--apply keeps what was in the manifest")
check(merged[P + ".core"] == REPO + "?path=/Packages/" + P + ".core#v9.9.9", "--apply replaces an older line of the same package")
check(P + ".vision" in merged and P + ".sidecar" in merged, "--apply adds the closure")
check(list(merged) == sorted(merged), "the manifest's dependencies stay sorted")
all_five = G.block(real, [P + "." + n for n in ("core", "sidecar", "vision", "cutout", "body")], "v1", REPO)
check(len(all_five) == 5, "all five names give five lines against the real family")
check(P + ".core" in G.closure(real, [P + ".body"]) and P + ".sidecar" in G.closure(real, [P + ".body"]), "in the real family body needs core and sidecar")

print("\n" + "-" * 78)
print("%d/%d assertions passed" % (PASS, PASS + FAIL))
sys.exit(1 if FAIL else 0)
