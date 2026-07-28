# CONTEXT.md - Shared Project Language

This file defines the shared vocabulary for the project. Keep it concise. It is not a full spec and should not contain implementation plans.

Update this file when a term becomes important enough that future agents or teammates must use it consistently.

## 현재 인수인계 (2026-07-28 · SVN r235 커밋 완료)

작업 세션: **경석(Claude)**. 브랜치 `feature/map-player-merge` (`30cf1df`, origin보다 1 앞).

### 이번 세션 완료 — SVN 최신화

**커밋 r235** (225 → 235). 신규 242 / 수정 104.

- 신규: `MapObj/mesh/level`(131) · `material`(48) · `texture`(32) · `mesh/object`(28)
- **⚠️ r234 GUID 사고 처리** — 팀원이 "누락된 meta 커밋"으로 MapObj meta 87개의 GUID를 새로
  발급해 올렸다. 그대로 update 했으면 git 쪽 41개 파일(존 프리팹 12·벽 10·머티리얼 17·씬 1)이
  전부 미싱 레퍼런스가 된다. `svn merge -c -234`로 GUID 재발급분만 역머지해 원본 유지.
  경위·재발 방지 = `Docs/_local/lessons.md` #14
- **콜라이더 플래그 복구** — floor/wall/urethane fbx meta 39개가 7/28 아트 교체로 `addColliders`·
  `isReadable` 0으로 리셋돼 있던 것을 r225 값(1/1)으로 되돌림.
- 검증: 컴파일 0에러/0경고, GUID 80/80 원본 일치, 이번 작업발 미싱 레퍼런스 0.
  (기존 미싱 5건 — MapScene FoW `maskTexture`(미사용) · `MA_Wall_basic.mat` 1 · `Stage1.prefab` 3 — 은 별개)
- git 정리 커밋 `30cf1df`: Tree/TutorialInfo 템플릿 잔재 + `all_mesh.unity` 제거
  (빌드 세팅 미등록·코드 참조 0 확인).

### 다음 작업

1. **MapScene Play 검증** — 맵 생성 → 이동/충돌 → 콘솔 0에러. 39개 FBX가 임포터 콜라이더를
   다시 달았으므로 이 확인 후 머지.
2. **머지** — `feature/map-player-merge`는 origin/development보다 **209 커밋 앞, 0 뒤짐**
   (fast-forward 가능). PR 여부는 검증 후 결정.

### 이전 세션 완료

1. **벽 투명화 per-pixel 재설계** — 오브젝트당 스칼라 불투명도(MPB) → 프래그먼트 월드좌표 기반
   셰이더 계산. 물리 쿼리 0, MPB 0, 약 1,600줄 → 473줄. Play 검증 통과.
   설계·검증·한계 = [Docs/tech/wall-occlusion-implementation.md](Docs/tech/wall-occlusion-implementation.md)
   - 삭제된 타입: `WallOcclusionUnit/Proxy/Manager/VisibilityContributor/Core/RuntimeBinder/ProjectBridge`
   - 현행 타입: `WallOcclusionDriver`(Assembly-CSharp) + `Globals`/`MaterialBinder`/`Settings`(Occlusion asmdef)
2. **맵 프리팹 콜라이더 복구** — 아트 교체로 12개 프리팹 콜라이더 0개였던 것 복구.
3. **`Assets/level` 아트팩 145개 재배치** — GUID 참조 그래프로 사용/미사용 판정 후 이전.
   `AssetDatabase.MoveAsset`만 사용, **GUID 145/145 보존, 미싱 레퍼런스 0**.
   - `50.Art/MapGen/MapObj/{mesh/level, material, texture}` ← FBX·머티리얼·셰이더그래프·텍스처 (**SVN**)
   - `2.Prefabs/Map/Props` ← 프롭 프리팹 38개 (git)
   - `99.Settings` ← `PP.asset`, `PP_Renderer.asset` (미참조 URP 파이프라인, 위치만 잡아둠)
   - 이름 충돌 3건은 `_level` 접미사로 개명 (`MA_prop03_level` 등 — 기존 50.Art와 이름만 같고 별개 에셋)
4. **오클루전 머티리얼 매핑 5쌍 → 14쌍** — level 폴더 머티리얼 9종이 매핑에서 빠져 프롭들이
   디더 셰이더를 못 달고 있던 문제 수정.

### 다음 작업 (이어서)

1. **SVN 최신화** — `50.Art/MapGen/MapObj` 아래 신규 파일들을 SVN에 add/commit. git에는 안 보인다
   (`.gitignore:83`이 `50.Art/` 제외, `Assets/50.Art.meta` 1개만 추적).
2. **머지** — SVN 상태 맞춘 뒤 진행.

### 주의

- **`8.BehaviorTreeGraph/**` 는 다른 담당자 작업물이다. 수정·커밋하지 말 것.** (현재 워킹트리에
  수정 상태로 있으나 의도적으로 커밋에서 제외했다.)
- 미커밋으로 남긴 것: `TitleScene`·`ProjectSettings`(줄바꿈만 변경, 내용 0), `all_mesh.unity`,
  `FogProfile.asset`, BT 에셋 2종.
- 아트(FBX·텍스처·머티리얼)는 SVN, 코드·씬·프리팹은 git. 이 경계를 넘기지 말 것.

## 이전 인수인계 (2026-07-21 → Codex)

작업 세션: **경석(Claude)** — MapScene 몬스터/보스입장 통합 완료(컴파일 0, 1차 플레이 검증). 다음 작업자 = Codex.

- **상세 현황·남은작업·조사항목 = [Docs/tech/map-monster-boss-handoff.md](Docs/tech/map-monster-boss-handoff.md)** (이 세션 산출물 전체 + 우선순위 목록).
- 계획 잠금: `PLAN.md` §"MapScene 몬스터 통합" + §6(보스 입장).
- 최근 수정 파일(동시수정 주의): `1.Scripts/{Map/*, Monster/MonsterBase.cs, Player/PlayerMovement.cs·PlayerAimIndicator.cs, Unit/Weapon/BaseAttack.cs, Player/Skill/FirstMeleeMainSkill.cs}`, `MapScene.unity`, 존 프리팹 12개.
- **아직 push/커밋 안 됨.** git + SV( 50.Art meta·MapGenConfig) 분리 커밋 예정 — 핸드오프 문서 §4.
- 즉시 다음 후보: 패드 y 가림 조치 / 멀티(MPPM) 텔레포트 검증 / 터렛 스폰 재확인 / **MortarBot 복귀 후 간헐 Idle 회귀 조사**(핸드오프 §3).

## Project Summary

A top-down cooperative action game inspired by Ravenswatch-style structure.

Current near-term target:
- Start game
- Boss intro sequence
- Boss combat
- Listen-server network vertical slice

Later scope:
- Map expansion
- Growth systems
- General mobs
- Additional content

## Core Terms

- Player: A human-controlled networked unit.
- Host: The player running the listen server.
- Client: A connected player that is not the host.
- Server authority: Logic owned and decided by the server/host, then replicated.
- Owner authority: Logic controlled by the owning client, usually player input and movement.
- Unit: A gameplay actor with common state and snapshot behavior.
- UnitBase: The common base for shared unit state and snapshot only. Movement, abilities, status effects, and networking behavior should be composed with components where possible.
- Boss: A server-authoritative enemy with encounter flow, patterns, state, and network-visible presentation.
- Boss intro: The sequence before combat begins, including presentation and state transition into battle.
- State abnormality: Status effect or condition applied to a unit.
- Build: A player growth or ability configuration concept.
- Skill: A player or boss action/pattern defined by data and executed by runtime logic.
- ScriptableObject data: Authoring-time gameplay data for skills, builds, bosses, patterns, and tuning values.
- Vertical slice: A thin but complete path through gameplay, networking, UI/presentation, and verification.

## Networking Language

- Player input: Usually owner-authoritative.
- Player movement: Usually owner-authoritative unless a specific anti-cheat or server correction rule is chosen.
- Boss state: Server-authoritative.
- Enemy state: Server-authoritative.
- Damage: Server-authoritative.
- Drops/rewards: Server-authoritative.
- Scene progression: Server-authoritative.
- Snapshot: A compact representation of state needed for synchronization, save, debug, or replay-like inspection.

## Design Preferences

- Prefer composition over deep inheritance.
- Prefer data-driven tuning for gameplay content.
- Prefer small vertical slices over broad unfinished systems.
- Prefer clear module interfaces that hide meaningful implementation.
- Prefer names from this file and `Docs/` over ad hoc synonyms.

## Open Vocabulary To Resolve

Add definitions when these become concrete:
- Exact boss encounter phase names
- Player class names
- Ability categories
- Build/growth terminology
- State abnormality taxonomy
- Scene/session flow terms
- Network room/lobby terms

## Resolved Terms (2026-07-21)

- Boss enter pad: BossRoom 역할 존 중앙의 진입 패드(트리거+테두리 표시). 생존 플레이어 점유 시 카운트다운(3·2·1), 전원 이탈 시 취소. 완주 시 생존자 전원 보스룸으로 텔레포트. 튜닝은 BossTeleportManager 인스펙터.
- RangedTurret: 고정 포탑 몬스터 아키타입(PeekABot·TeslaBot). 넉백 면역, 경직만 적용.
- Knockback direction: 공격이 AttackInfo.knockbackDirection으로 명시(방향성 공격). zero면 수신측이 방사형(대상-공격자)으로 폴백(장판/폭발형).