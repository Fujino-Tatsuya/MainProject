# git·SVN 아트 에셋 중복 — 조사 결과 및 인수인계

> 작성 2026-07-30 / 브랜치 `feature/map-player-merge` / 조사·정리: 경석 세션
> 대상: art 폴더 메타 파일 충돌 작업 세션, 그리고 `.meta`가 왕복한다고 보고한 팀원

팀원 보고 증상: **"mat 파일이 예전 걸 참조해서, git과 SVN 양쪽에서 받아오면 꼬인다."**

원인은 **동명 에셋이 git과 SVN에 각각 존재한 것**이었다. 같은 이름이 Project 창에 둘 보이니
작업자가 고아 사본을 집게 되고, 편집이 엉뚱한 파일에 적용돼도 경고가 없다.

> 초판에서는 원인을 `.gitignore` 브랜치 차이로 적었으나 **오진이었다.** 증상이 난 두 브랜치의
> 규칙이 동일하다는 지적을 받고 재조사해 §1을 정정했다. `.gitignore` 불일치는 별개의
> 위생 문제로 §1 후반에 남겨두었다.

---

## 1. 원인 — 동명 중복 자체다. `.gitignore` 차이가 아니다

증상이 발생한 두 브랜치는 `feature/map-player-merge`와 `feature/PlayerSkillAnimation`이며,
**둘의 `.gitignore` 50.Art 규칙은 완전히 동일하다**(양쪽 모두 83~84행 `/[Aa]ssets/50.Art/`,
`/[Aa]ssets/51.Audio/`). `PlayerSkillAnimation`은 이미 `map-player-merge`에 머지된 상태다.

따라서 **브랜치 간 `.gitignore` 차이는 이 증상의 원인이 아니다.** 규칙이 같은 두 브랜치에서
발생했으므로 설명이 되지 않는다.

### 실제 기전 — Project 창에 같은 이름이 두 개 보인다

git 사본(`3.Materials/…`)과 SVN 사본(`50.Art/…`)이 **동명으로 공존**하므로, 에디터의
Project 창에는 이름이 같은 머티리얼이 둘 보인다. 작업자가 어느 쪽을 집었는지에 따라
프리팹·씬이 서로 다른 GUID에 바인딩된다. 한쪽은 실사용 사본, 다른 쪽은 아무도 안 쓰는
고아 사본이므로, 고아를 집으면 **"예전 걸 참조"하는 상태**가 된다.

직접 관측된 증거: 커밋 `fd1bf2c`에서 `MA_Wall_basic`에 베이스컬러를 연결했는데,
그 대상이 **참조 0건인 고아 사본**이었다. 실사용 SVN 사본에는 이미 같은 텍스처가
연결돼 있었다. 즉 편집이 엉뚱한 파일에 적용되고도 아무 경고가 없었다.

→ **§3의 중복 제거가 이 원인에 대한 수정이다.**

### 참고: 브랜치별 `.gitignore` 상태 (별개 문제, 위생 차원)

| 브랜치 | 50.Art 규칙 | 50.Art 추적 파일 |
|---|---|---|
| `feature/map-player-merge` | `/[Aa]ssets/50.Art/` 폴더 전체 제외 | 0건 |
| `feature/PlayerSkillAnimation` | **동일** | 0건 |
| `origin/development` | `**/*.fbx`, `*.png`, `*.wav` … 확장자별 | **236건** |
| `origin/main` | **규칙 없음** (50.Art·51.Audio·`.svn` 전부 미제외) | — |

`development`의 규칙에는 `# (폴더 내의 .meta 파일은 자동으로 추적되며…)` 주석이 붙어 있어
**아트 `.meta`를 git에 추적시키는 것이 의도**임을 알 수 있다. 이는 `AGENTS.md` §3
"`.meta` 는 **에셋과 같은 VCS** 로 함께 관리 (아트 .meta는 SVN)"를 위반한다.

`.gitignore` 자체는 feature 브랜치 쪽이 최신이다(2026-07-23). `development`(2026-06-22)와
`main`(2026-06-04)이 뒤처져 있다.

### 해야 할 일

- `development`: 규칙 통일 커밋을 `chore/dev-gitignore-unify` (`dc9c3f8`)로 준비해 뒀다.
  단 **이 커밋만으로는 아무것도 해소되지 않는다** — `.gitignore`는 이미 추적 중인 파일을
  제외하지 못하므로 236건은 그대로 남는다. §2의 untrack 절차가 함께 필요하다.
- `main`은 `.svn` 제외 규칙조차 없어서, main 기준으로 작업하면 SVN 내부 폴더가
  git에 올라갈 수 있다.
- 규칙을 옮길 때 **feature 브랜치의 `.gitignore`를 통째로 복사하지 말 것.**
  `/AGENTS.md`, `/CONTEXT.md`, `/PLAN.md`, `/Tools/` 등 개인 환경용 규칙이 섞여 있다.
  이 파일들은 feature 브랜치에서는 **이미 추적 중이라 규칙이 무효**하지만,
  `development`에서는 미추적이므로 규칙만 옮기면 팀원이 커밋할 수 없게 된다.
  50.Art·51.Audio·`.svn` 규칙만 옮기는 것이 맞다.

---

## 2. 이미 git에 추적돼 버린 50.Art `.meta` 5건

`.gitignore`는 **이미 추적 중인 파일을 제외하지 못한다.** 그래서 현재 브랜치의
폴더 전체 제외 규칙에도 불구하고 아래 5건이 git에 남아 있다.

```
Assets/50.Art/TestAssets/Temp_Images/Images.meta
Assets/50.Art/TestAssets/Temp_Images/Images/title.png.meta
Assets/50.Art/TestAssets/Temp_Images/Images/title_Background.png.meta
Assets/50.Art/TestAssets/Temp_Images/ResultScene.meta
Assets/50.Art/TestAssets/Temp_Images/ResultScene/resultscene_background.png.meta
```

확인된 사실:

- **5건 모두 SVN에도 존재한다** (`Assets/50.Art/.svn/pristine`에서 확인). 즉 **이중 관리 상태**다.
- `resultscene_background.png.meta`의 GUID `f2cd2d0e…`는
  `Assets/0.Scenes/MainFlow/5.ResultScene.unity`가 참조한다. **GUID가 뒤집히면 결과 화면 배경이 깨진다.**

### ⚠️ untrack 할 때 주의 — 그냥 지우면 팀원 워킹카피가 깨진다

`git rm --cached` 로 추적만 끊어도, **그 커밋을 pull한 팀원의 워킹카피에서는 git이 파일을 삭제한다**
(gitignore 여부와 무관하게, 커밋에 담긴 삭제는 체크아웃 시 적용된다). 파일은 SVN 관리물이므로
SVN 입장에서 "로컬에서 사라진 파일"이 되고, 그 상태로 SVN 커밋이 들어가면 `.meta`가
SVN에서도 삭제되어 GUID가 재발급된다 → `5.ResultScene`의 배경 참조가 깨진다.

그래서 untrack은 **아래 절차를 팀에 공지한 뒤** 진행할 것.

1. git에서 `git rm --cached` 로 5건 추적 해제 후 커밋
2. 팀 전원에게 공지: **pull 직후 `Assets/50.Art`에서 SVN revert(또는 update)로 `.meta` 복구**
3. 복구 확인 전에는 SVN 커밋 금지

이 작업은 아직 **하지 않았다.** 팀 공지가 선행되어야 해서 남겨둔다.

---

## 3. 완료된 정리 — 참조 없는 git 사본 14건 삭제

`3.Materials` 아래 git 사본과 `50.Art` 아래 SVN 사본이 **동명으로 공존**하고 있었다.
프리팹·씬은 SVN 사본을 참조하고, git 사본은 외부 참조가 0건인 고아였다.

| 커밋 | 내용 |
|---|---|
| `5e0d4c3` | `MA_Wall_basic.mat` 1건 |
| `cc5a4e1` | mat 6건 + shadergraph 7건 |

```
mat          MA_Wall_basic, MA_Floor_convayorblet, MA_Wall_window,
             MA_floor, MA_floor_urethane, MA_prop01, MA_prop02
shadergraph  convayorbelt, floor, prop01, prop02, urethene,
             wall_Basic, wall_window
```

삭제 조건 3개를 항목별로 검증했다.

1. `50.Art`에 동명 SVN 사본이 존재
2. 자기 `.meta`를 제외한 외부 참조 0건
3. git 추적 중

예시로 `MA_Wall_basic.mat`의 근거:

| | GUID | 참조 수 |
|---|---|---|
| SVN `50.Art/MapGen/MapObj/material/` | `faf6786b…` | **20** (벽 프리팹 9 + Zone 프리팹 10 + `WallOcclusionSettings.asset`) |
| git `3.Materials/Level1_Materials/` | `fcc0b1d7…` | **0** |

### 🔴 "SVN이 정본"은 일반 규칙이 아니다 — 삭제하면 깨지는 것들

동명 중복 중 아래 4건은 **git 사본이 실사용**이다. 규칙대로 일괄 삭제하면 깨진다.

```
Assets/3.Materials/R1/M_AtlasBase.mat
Assets/3.Materials/R1/M_AtlasEmissive.mat
Assets/3.Materials/R1/M_AtlasOffset.mat
      ↑ 3건 모두 Assets/2.Prefabs/Enemy/ModularRobots_R1.prefab 이 참조

Custom_OffsetShader.shadergraph  → 참조 1건
```

**동명 중복을 발견하면 이름이 아니라 참조 수로 정본을 판정할 것.**

---

## 4. 남은 문제 — 끊긴 텍스처 참조

실사용 `MA_Wall_basic.mat`(SVN 사본)의 슬롯
`_SampleTexture2D_92ed2c08da274af5a65e4eb15008568f_Texture_1_Texture2D` 가
GUID `9f8b480eab36cbf41a1a2b5420c01c62` 를 참조하는데, **이 GUID가 프로젝트 어디에서도
해석되지 않는다** (Assets `.meta`·`Library/PackageCache` 모두 없음).

고아 사본을 지워도 사라지지 않는다. **실사용 파일 쪽 문제이므로 art 세션에서 처리할 것.**
SVN에서 텍스처가 아직 안 왔거나, 경로 이동으로 `.meta`가 갈린 경우로 보인다.

### 정상이므로 추적하지 말 것

`d0353a89b1f911e48b9e16bdc9f2e058` — URP 패키지의 `Editor/AssetVersion.cs`.
프로젝트의 mat 88개가 정상적으로 사용한다. 검색 범위에 `Library/PackageCache`를
넣지 않으면 "끊긴 참조"로 오판하게 된다.

---

## 5. 🔴 GUID 가 반복해서 뒤집히는 진짜 원인 — `.meta` 에 커밋된 충돌 마커

**이것이 이 문서에 적힌 GUID 분기 현상 전체의 근본 원인이다.** 2026-07-30 `feature/Sound-Boss`
머지 중에 발견했다.

과거 머지에서 **미해결 충돌 마커가 그대로 커밋된 `.meta` 파일이 3건** 있었다:

```
Assets/2.Prefabs/Map/Vent/Vent.prefab.meta            ← fe75533 (Merge feature/Platforms)
Assets/1.Scripts/Rendering/Fog/FOG_NEXT_PLAN.md.meta  ← 같은 커밋
Assets/0.Scenes/MonsterScene.unity.meta               ← e43d733 (Merge feature/PlayerSkill)
```

내용이 거의 같은 `.meta` 들을 git 이 **rename/copy 관계로 오인**해 서로의 내용을 섞어 넣었다.
그 결과 한 파일 안에 `guid:` 가 두 개, 임포터가 두 개 들어갔다 — `Vent`(프리팹)가
`TextScriptImporter` 를, `FOG`(마크다운)가 `PrefabImporter` 를 갖는 상태였다:

```
fileFormatVersion: 2
<<<<<<<< HEAD:Assets/1.Scripts/Rendering/Fog/FOG_NEXT_PLAN.md.meta
guid: 45b6fe3ffe0fe1d418111431b9583ab1
TextScriptImporter:
========
guid: 407dffc28a409494d9173e24a4cfdd5c
PrefabImporter:
>>>>>>>> feature/Platforms:Assets/2.Prefabs/Map/Vent/Vent.prefab.meta
  externalObjects: {}
```

**Unity 는 이런 `.meta` 를 파싱하지 못해 GUID 를 새로 발급한다.** 그것이 커밋될 때마다
참조가 갈리고, 다른 사람이 받으면 또 재발급된다. 하루에 세 번 뒤집힌 이유가 이것이다.
`convayorbelt.shadergraph.meta` 의 SVN r249 GUID 교체도 같은 계열로 의심된다.

복구한 정본 (임포터 종류가 파일 확장자와 맞는지가 판별 기준이다):

| 파일 | GUID | 임포터 |
|---|---|---|
| `Vent.prefab.meta` | `407dffc28a409494d9173e24a4cfdd5c` | `PrefabImporter` |
| `FOG_NEXT_PLAN.md.meta` | `45b6fe3ffe0fe1d418111431b9583ab1` | `TextScriptImporter` |
| `MonsterScene.unity.meta` | `8066623a2cd82c946bb900abdefd5f04` | `DefaultImporter` |

`MonsterScene` 의 GUID 는 `ProjectSettings/EditorBuildSettings.asset` 이 참조하므로 유지가 맞다.
`Vent` 는 `cce1ff2`(Vent 추가 커밋)의 원본 값으로 되돌렸다.

> ⚠️ **정정**: 초판에서는 이 현상을 "Vent 와 FOG 가 같은 GUID 를 공유하는 중복"으로 적고
> `fd1bf2c` 에서 Vent 에 새 GUID 를 발급해 "중복 해소"했다고 기록했다. **오진이었다.**
> 실제로는 두 파일이 같은 마커 블록을 품고 있어서 같은 `guid:` 줄이 양쪽에 보인 것이고,
> `fd1bf2c` 는 그 마커 블록 **안쪽의 한 줄**을 고친 것이었다. diff 에 나온 `guid:` 한 줄만
> 보고 판단해 파일 전체를 열어보지 않았다.

### 재발 점검 명령

머지 직후, 특히 `.meta` 가 걸린 머지 뒤에는 반드시 돌린다.

```bash
grep -rlE "^<{7} |^<{8} |^={7}$|^>{7} " Assets/ ProjectSettings/ Packages/ | grep -v "\.svn/"
```

출력이 있으면 그 파일은 **커밋된 손상**이다. 임포터 종류를 확장자와 맞춰 한쪽을 고르고,
GUID 는 그 에셋을 추가한 원본 커밋에서 가져온다.

---

## 6. 재사용 가능한 검증 명령

### 동명 중복 전수 조사

```bash
git ls-files -- 'Assets/3.Materials/**/*.mat' | while read -r p; do
  b=$(basename "$p")
  hit=$(find Assets/50.Art -name "$b" -not -path "*/.svn/*" | head -1)
  [ -n "$hit" ] && echo "중복: $b"
done
```

### 어떤 에셋의 참조 수 세기 (정본 판정)

```bash
g=$(grep -oE "guid: [0-9a-f]{32}" "<경로>.meta" | head -1 | awk '{print $2}')
grep -rl "guid: $g" Assets/ ProjectSettings/ | grep -v "\.svn/" | grep -vF "<경로>"
```

**함정 2개:**

- 자기 자신의 `.meta`가 자기 GUID를 담고 있다. 제외하지 않으면 항상 "참조 1건"이 나온다.
  경로 접두 매칭으로 제외할 것 (`grep -vF "$p"`). `$` 앵커를 쓰면 `.meta`가 안 걸러진다.
- 패키지 스크립트 GUID는 `Assets/` 안에 `.meta`가 없다. `Library/PackageCache`까지
  찾아보기 전에는 "끊긴 참조"로 단정하지 말 것.

### 끊긴 GUID 판정

```bash
g=<guid>
grep -rl "guid: $g" --include=*.meta Assets/ Library/PackageCache/ | head -1
# 출력이 없을 때만 실제로 끊긴 참조
```

### 🔴 브랜치 간 비교 — Git Bash 경로 변환 함정

Windows + Git Bash에서 `git show <rev>:<path>` 는 **조용히 깨진다.** MSYS가 인자를
Windows 경로로 변환하면서 `origin/development:.gitignore` → `origin\development;.gitignore`
가 되어, "그런 리비전 없음" 에러가 나거나 **빈 출력**이 돌아온다.

이 조사에서 실제로 이 함정에 빠져 "모든 브랜치에 50.Art 규칙이 없다"는 **거짓 결론**을
한 번 냈다. 우회:

```bash
MSYS_NO_PATHCONV=1 git show "origin/development:.gitignore"
```

단 `MSYS_NO_PATHCONV=1` 을 `export` 해두면 **반대 방향으로 깨진다** — `/tmp/foo` 같은
unix 경로가 변환되지 않아 git이 파일을 못 찾는다. 파일 인자가 있는 명령은 stdin으로 우회:

```bash
git hash-object -w --stdin < /tmp/foo
```

**교훈: 브랜치 비교 결과가 "전부 없음"으로 나오면 결론 내기 전에 양성 대조를 넣을 것.**
확실히 존재하는 값으로 같은 명령을 돌려 도구가 살아 있는지 먼저 확인한다.
`git diff <revA> <revB> -- <path>` 는 콜론을 안 쓰므로 이 함정이 없다.

---

## 7. 관련 문서

- [AGENTS.md](../../AGENTS.md) §3 — 하이브리드 VCS 정책, `.meta` 동일 VCS 규칙
- [Docs/tech/workflow.md](workflow.md) — 브랜치·PR 절차
- `Docs/tech/map-monster-boss-handoff.md` — 맵·몬스터·보스 인수인계
