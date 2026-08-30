# -*- coding: utf-8 -*-
"""
새 환경이 제대로 셋업됐는지 검사한다. Unity 를 열기 전에 돌릴 것.

    python Docs/tech/check-environment.py

왜 있나 — 이 프로젝트에서 잘못된 셋업은 **시끄럽게 실패하지 않는다.**
`Assets/50.Art` 는 git 이 아니라 SVN 이 소유하는데, 없거나 낡아도 Unity 는 그냥 열린다.
그리고 git 이 추적하는 에셋 392개가 그 트리의 GUID 를 23,097번 참조하므로, 맵과 캐릭터가
조용히 비거나 다른 것으로 보인다. `git status` 에는 아무것도 안 나온다.

종료 코드: 0 = 전부 통과 / 1 = 게임 내용이 틀어지는 문제 있음 / 0 이지만 경고는 있을 수 있음
자세한 셋업 절차는 Docs/tech/environment-setup.md.
"""

import json
import os
import re
import shutil
import subprocess
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

# 이 파일은 Docs/tech/ 에 있다 - 레포 루트는 두 단계 위다.
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))).replace("\\", "/")
GUID_RE = re.compile(r"guid:\s*([0-9a-f]{32})")

fails = []
warns = []


def ok(msg):
    print("  [ OK ]  " + msg)


def fail(msg, fix=""):
    fails.append((msg, fix))
    print("  [FAIL]  " + msg)
    if fix:
        print("          -> " + fix)


def warn(msg, fix=""):
    warns.append((msg, fix))
    print("  [WARN]  " + msg)
    if fix:
        print("          -> " + fix)


def run(args):
    """명령 하나를 돌려 (성공여부, 출력) 을 준다. 없으면 (False, '')."""
    try:
        out = subprocess.run(args, capture_output=True, text=True, encoding="utf-8",
                             errors="replace", timeout=120)
        return out.returncode == 0, (out.stdout or "") + (out.stderr or "")
    except Exception as e:
        return False, str(e)


def svn_exe():
    p = shutil.which("svn")
    if p:
        return p
    for c in [r"C:/Program Files/SlikSvn/bin/svn.exe",
              r"C:/Program Files/TortoiseSVN/bin/svn.exe"]:
        if os.path.exists(c):
            return c
    return None


# ── 1. Unity 버전 ──────────────────────────────────────────────────────────
print("\n1. Unity 버전")
pv = os.path.join(ROOT, "ProjectSettings/ProjectVersion.txt")
want = None
if os.path.exists(pv):
    m = re.search(r"m_EditorVersion:\s*(\S+)", open(pv, encoding="utf-8").read())
    want = m.group(1) if m else None
if want:
    ok("프로젝트가 요구하는 버전: " + want + "  (Unity Hub 에 이 버전이 있어야 한다)")
else:
    fail("ProjectSettings/ProjectVersion.txt 를 읽을 수 없다", "클론이 불완전하다. 다시 clone 할 것")

# ── 2. 아트(SVN) 핀 ────────────────────────────────────────────────────────
print("\n2. 아트 (SVN)")
pin_path = os.path.join(ROOT, "Docs/tech/art-svn.json")
pin = None
if os.path.exists(pin_path):
    pin = json.load(open(pin_path, encoding="utf-8"))
else:
    fail("Docs/tech/art-svn.json 이 없다", "이 파일이 아트 리비전 핀이다. 클론이 불완전하다")

junction = os.path.join(ROOT, (pin or {}).get("junctionPath", "Assets/50.Art"))
if not os.path.isdir(junction):
    fail("Assets/50.Art 가 없다 - 아트가 통째로 빠진 상태다",
         "Docs/tech/environment-setup.md 의 2~3단계(svn checkout + 정션)를 할 것")
else:
    metas = 0
    for dp, dn, fn in os.walk(junction):
        metas += sum(1 for f in fn if f.endswith(".meta"))
    expect_meta = ((pin or {}).get("expected") or {}).get("metaFiles", 0)
    if metas == 0:
        fail("Assets/50.Art 가 비어 있다 (.meta 0개)",
             "정션은 걸렸는데 SVN 체크아웃이 안 됐거나 대상 경로가 틀렸다")
    elif expect_meta and metas < expect_meta * 0.9:
        warn("Assets/50.Art 의 .meta 가 %d 개다 (기대 %d)" % (metas, expect_meta),
             "아트가 낡았거나 부분 체크아웃이다. svn update 할 것")
    else:
        ok("Assets/50.Art 에 .meta %d 개 (기대 %d)" % (metas, expect_meta))

svn = svn_exe()
if not svn:
    warn("svn 클라이언트를 못 찾았다", "리비전을 대조할 수 없다. SlikSvn 등을 설치하면 이 검사가 켜진다")
elif pin and os.path.isdir(junction):
    real = os.path.realpath(junction).replace("\\", "/")
    good, info = run([svn, "info", real])
    if not good:
        warn("Assets/50.Art 가 SVN 작업사본이 아니다",
             "git 으로 받은 사본이거나 손으로 복사한 것일 수 있다. 낡아도 아무도 모른다")
    else:
        u = re.search(r"URL:\s*(\S+)", info)
        want_url = (pin["url"].rstrip("/") + "/" + pin["subtree"].strip("/"))
        if u and u.group(1).rstrip("/") != want_url:
            fail("SVN URL 이 핀과 다르다: %s" % u.group(1),
                 "기대: " + want_url)
        elif u:
            ok("SVN URL 이 핀과 같다")

        good2, ver = run([os.path.join(os.path.dirname(svn), "svnversion"), real])
        ver = (ver or "").strip()
        if good2 and ver:
            if re.search(r"[MS]|:", ver):
                warn("작업사본이 혼합/변경 상태다: %s" % ver,
                     "svn revert -R . / svn update 로 정리한 뒤 다시 검사할 것")
            elif ver.isdigit() and pin.get("pinnedRevision"):
                if int(ver) != int(pin["pinnedRevision"]):
                    warn("아트 리비전 r%s 가 핀 r%s 와 다르다" % (ver, pin["pinnedRevision"]),
                         "같게 맞추려면: svn update -r %s <작업사본>  "
                         "(의도적으로 새 아트를 쓰는 중이면 Docs/tech/art-svn.json 의 핀을 올릴 것)")
                else:
                    ok("아트 리비전 r%s 가 핀과 같다" % ver)

# ── 3. 아트 참조가 실제로 풀리나 ───────────────────────────────────────────
print("\n3. 아트 GUID 가 실제로 해석되나")
if os.path.isdir(junction):
    owned = set()
    for dp, dn, fn in os.walk(junction):
        for f in fn:
            if not f.endswith(".meta"):
                continue
            try:
                m = GUID_RE.search(open(os.path.join(dp, f), encoding="utf-8", errors="replace").read())
            except Exception:
                continue
            if m:
                owned.add(m.group(1))
    good, out = run(["git", "-C", ROOT, "ls-files"])
    hit_files, occ = 0, 0
    if good:
        for rel in out.split("\n"):
            rel = rel.strip()
            if not rel:
                continue
            p = os.path.join(ROOT, rel)
            if not os.path.isfile(p):
                continue
            try:
                t = open(p, encoding="utf-8", errors="replace").read()
            except Exception:
                continue
            n = sum(1 for g in GUID_RE.findall(t) if g in owned)
            if n:
                hit_files += 1
                occ += n
    exp = ((pin or {}).get("expected") or {})
    e_files = exp.get("gitFilesReferencing", 0)
    if e_files and hit_files < e_files * 0.9:
        warn("git 에셋 %d 개만 아트를 참조한다 (기대 %d, 등장 %d)" % (hit_files, e_files, occ),
             "아트가 낡아 GUID 가 바뀐 것일 수 있다. 리비전을 핀에 맞출 것")
    else:
        ok("git 에셋 %d 개가 아트 GUID 를 %d 번 참조하고, 전부 해석된다" % (hit_files, occ))
else:
    print("  (건너뜀 - 50.Art 가 없다)")

# ── 4. MCP 패키지 ──────────────────────────────────────────────────────────
print("\n4. MCP 패키지 (게임 실행에는 불필요, 도구용)")
mani = os.path.join(ROOT, "Packages/manifest.json")
committed = None
good, out = run(["git", "-C", ROOT, "show", "HEAD:Packages/manifest.json"])
if good:
    m = re.search(r'"com\.community\.unity-mcp":\s*"([^"]+)"', out)
    committed = m.group(1) if m else None
working = None
if os.path.exists(mani):
    m = re.search(r'"com\.community\.unity-mcp":\s*"([^"]+)"', open(mani, encoding="utf-8").read())
    working = m.group(1) if m else None

if committed and working and committed != working:
    warn("manifest 의 커밋된 값과 워킹트리 값이 다르다",
         "커밋됨: %s / 지금 도는 것: %s  - 로컬 개발(skip-worktree) 이면 정상이고, "
         "아니라면 이 프로젝트는 커밋된 것과 다른 패키지를 쓰고 있다" % (committed, working))
elif working:
    ok("MCP 패키지: " + working)

if not shutil.which("node"):
    warn("node 를 못 찾았다", "MCP 브릿지 실행에 Node.js 가 필요하다. 게임 편집·빌드에는 무관하다")
else:
    ok("node 있음")

# ── 요약 ───────────────────────────────────────────────────────────────────
print("\n" + "=" * 68)
if fails:
    print("실패 %d 건 - 이 상태로 Unity 를 열면 게임 내용이 틀어진다." % len(fails))
    for m, _ in fails:
        print("  - " + m)
elif warns:
    print("통과. 다만 경고 %d 건 - 무엇을 보고 있는지 알고 넘어갈 것." % len(warns))
else:
    print("전부 통과. Unity 를 열어도 된다.")
print("절차: Docs/tech/environment-setup.md")
sys.exit(1 if fails else 0)
