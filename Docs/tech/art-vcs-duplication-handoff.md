# git·SVN 아트 에셋 중복 — 조사 결과 및 인수인계

> 작성 2026-07-30 / 브랜치 `feature/map-player-merge` / 조사·정리: 경석 세션
> 대상: art 폴더 메타 파일 충돌 작업 세션, 그리고 `.meta`가 왕복한다고 보고한 팀원

팀원 보고 증상: **"mat 파일이 예전 걸 참조해서, git과 SVN 양쪽에서 받아오면 꼬인다."**
조사해보니 개별 파일 문제가 아니라 **`.gitignore` 규칙이 브랜치마다 갈린 구조적 원인**이 있었다.

---

## 1. 근본 원인 — `.gitignore`의 50.Art 규칙이 브랜치마다 다르다

| 브랜치 | 50.Art 규칙 | `.gitignore` 최종 변경 |
|---|---|---|
| `feature/map-player-merge` (현재) | `/[Aa]ssets/50.Art/` — **폴더 전체 제외** | 2026-07-23 `572e2a7` |
| `origin/development` | `[Aa]ssets/50.Art/**/*.fbx`, `*.png`, `*.wav` … — **확장자별 제외** | 2026-06-22 `711232c` |
| `origin/main` | **규칙 없음** (50.Art·51.Audio·`.svn/` 전부 미제외) | 2026-06-04 `9101b8c` |

`development`의 규칙에는 이런 주석이 붙어 있다:

> `# (폴더 내의 .meta 파일은 자동으로 추적되며, 아래 확장자들만 제외됩니다)`

즉 **`development`는 아트 `.meta`를 git에 추적시키는 것이 의도**다. 반면 현재 브랜치는 50.Art를
폴더째 제외한다. 두 규칙은 정면으로 충돌한다.

### 어느 쪽이 맞는가 — 현재 브랜치가 맞다

`AGENTS.md` §3:

> `.meta` 는 **에셋과 같은 VCS** 로 함께 관리 (아트 .meta는 SVN). GUID 깨지면 프리팹 참조 전부 깨짐.

`development`의 확장자별 규칙은 이 팀 규칙을 위반한다. 아트 실체는 SVN,
`.meta`는 git으로 갈라지므로 **같은 에셋의 GUID를 두 VCS가 각자 관리**하게 되고,
받아오는 순서에 따라 GUID가 왕복한다. 이것이 팀원이 겪는 증상의 정체다.

> `.gitignore` 자체는 현재 브랜치가 가장 최신이다(2026-07-23). 팀장 로컬이 구버전이라는
> 의심이 있었으나 사실은 반대이고, `development`·`main`이 뒤처져 있다.

### 해야 할 일

- `development`와 `main`의 `.gitignore`를 현재 브랜치 규칙으로 통일.
- 특히 `main`은 `.svn/` 제외 규칙조차 없어서, main 기준으로 작업하면 SVN 내부 폴더가
  git에 올라갈 수 있다.

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

## 5. `.meta` GUID 중복 — 같이 볼 패턴

`Vent.prefab.meta`의 GUID가 `FOG_NEXT_PLAN.md.meta`와 **중복**(`45b6fe3f…`)이어서
에디터가 Vent 쪽에 새 GUID(`b1de5f8d…`)를 발급했다. 커밋 `fd1bf2c`로 반영했다.

`Vent.prefab`을 GUID로 참조하는 에셋이 없어 참조 파괴는 없었다. **커밋하지 않으면
프로젝트를 열 때마다 새 GUID가 재발급되어 같은 diff가 무한 반복된다.**

`.meta` 복사나 잘못된 머지로 GUID가 복제되면 이 형태가 된다. art 폴더에서 같은 패턴을
더 찾을 수 있다.

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

---

## 7. 관련 문서

- [AGENTS.md](../../AGENTS.md) §3 — 하이브리드 VCS 정책, `.meta` 동일 VCS 규칙
- [Docs/tech/workflow.md](workflow.md) — 브랜치·PR 절차
- `Docs/tech/map-monster-boss-handoff.md` — 맵·몬스터·보스 인수인계
