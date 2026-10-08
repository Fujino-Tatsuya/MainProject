# PLAN — 결과 화면 플레이어별 통계 (2026-10-07 · 은희 · 승인 ✅)

> 브랜치(안) `feature/ResultStats` ← `development`(`3217a052`). 구현 = **1단계 Codex(코드) → 2단계 claude-alt(씬 UI·Unity MCP 3001)**.
> 결정: 은희(10-07 그릴). 몬스터 파일에 이벤트 호출 한 줄씩 들어간다 → PR 이 없으므로 **문서로 경석 공유**(아래 1단계 9).

## 목표
5.ResultScene 에 **파티 전원**의 통계를 플레이어별로 보여 준다.

| 구분 | 항목 | 정의 |
|---|---|---|
| 판(팀) | 결과 | 클리어 / 전멸 / 중도 종료(ExitButton) — 중도 종료는 **누른 시점까지 집계한 값만** 그대로 표시(보정·추정 없음) |
| 판(팀) | 플레이 시간 | 기존 `SessionStatsTracker.ElapsedSeconds` (mm:ss) |
| 플레이어별 | 가한 데미지 | **몬스터(`MonsterBase`)의 HP+실드 실제 감소량.** 막타 초과분 제외. 더미·송전탑(`BossChargingPylon`)·`ChargingObject`·플레이어·상자 제외. 공격자 없는 피해(환경·추락·직접/비율 피해) 제외 |
| 플레이어별 | 처치 수 | **개인 단위**(기존 팀 단위 → 대체). 몬스터 HP 를 0 으로 만든 피해의 공격자(막타) |
| 플레이어별 | 간파 성공 | **23호 카운터 성공 + 중간보스 간파 창 소비 성공**만 1회. 일반몹 그로기 누적은 세지 않음 |
| 플레이어별 | 사망 횟수 | 서버에서 `Alive → DeadPresentation` 전이 1회 = 1. 추락 사망 포함 |

- **부활 횟수 없음** — 실제 부활 기능이 아직 없다(`PlayerReviveController` = F10 디버그 트리거).
- 표시 행 = 로비 슬롯 순서(clientId 오름차순, P1 = 호스트) · 캐릭터 이름·초상화(`CharacterRoster`) · 로컬 플레이어 "나" 강조.

## 지금 (조사, 10-07)
- 이미 있는 경로: `SessionStatsTracker`(MapScene, 서버 집계: 처치 수·시간) → 종료 지점 `Capture(cleared)` → 정적 `SessionResult` → `MapSceneManager.BroadcastGoToResultToClients` named message `"MapScene.GoToResult"`(고정 크기: HasValue·Cleared·SurvivalSeconds·Kills) → 클라 수신 시 `SessionResult.Capture` → 로컬 `SceneManager.LoadScene(5.ResultScene)` → `UI/ResultStatsView`(Text_Outcome/Survival/Kills).
- 종료 지점: 전멸 `PartyWipeWatcher.cs:66` · 클리어 `BossEncounterDirector.cs:689-702` · ExitButton `MapSceneManager.GoToResult`(**Capture 없음** → 지금은 대시 표시).
- ResultScene 시점: NetworkManager 연결 유지, 플레이어 NetworkObject 는 MapScene 과 함께 파괴 → **통계는 전환 전에 확정해 페이로드로 보낸다**(현 구조 유지).
- 데미지: 서버 단일 지점 `Unit.ApplyHealthDamage`(Unit.cs:107-156). 공격자 = `ReceiveAttack` 중 `_damageAttackerClientId`(Player `OwnerClientId`, 없으면 `ulong.MaxValue`). 서버 전역 데미지 이벤트 없음 → 선례 `MonsterDeathEvents` 처럼 정적 채널 추가.
- 간파 성공 판정 = 맞는 쪽: `TwentyThreeBoss.ReceiveAttack`(4873-4881, `EnterCounterSuccess`) · `GauntletBot.cs:240-248` / `SpinnerBot.cs:299` / `WallBot.cs:337` 의 `Counter.TryConsumeInterrupt()` 성공 분기(공격자 = `_damageAttackerClientId`, protected getter 필요).
- 사망: `PlayerLifeCycleController.LifeStateChanged`(전 피어 발생, Spawn 때 (s,s) 1회) → 서버에서 `prev==Alive && next==DeadPresentation` 만.

## 만드는 것
### 1단계 — 코드 (Codex)
1. **정적 서버 이벤트 채널**(몬스터·Unit 에 통계 의존성을 심지 않는다):
   - 데미지: `Unit.ApplyHealthDamage` 에서 실제 감소량(HP+실드)·공격자 clientId·치명 여부(이번 피해로 0 도달)를 발행. 서버(또는 오프라인)만.
   - 간파 성공: 23호·중간보스 3종 성공 분기에서 공격자 clientId 로 발행(각 1줄). `MonsterBase` 에 공격자 clientId protected getter.
2. **순수 집계기**(MonoBehaviour 아님, EditMode 테스트 대상): clientId 별 {데미지, 처치, 간파, 사망} 누적. 필터 규칙(MonsterBase 만, 공격자 없음 제외, 음수/0 무시)은 호출부가 아니라 집계기 입력 규칙으로 고정.
3. `SessionStatsTracker` 확장: 위 채널 + 플레이어 사망 구독, 판 종료 시 집계 스냅샷을 `SessionResult` 에 확정. 처치 수는 개인 합으로 대체(팀 합은 표시하지 않음). 판 중 접속 끊긴 플레이어 행은 유지(끊김 표시 여부는 2단계에서 판단 안 함 — 그냥 남김).
4. `SessionResult` 확장: 결과 종류(클리어/전멸/중도 종료) + 플레이어 행 목록 {clientId, characterId, 데미지, 처치, 간파, 사망}. characterId 는 서버 `ServerCharacterSelectionStore` 에서.
5. GoToResult 페이로드: 고정 크기 → 플레이어 수에 맞는 가변 크기(최대 인원 기준 상한). 클라 수신 검증 유지.
6. ExitButton 경로(호스트/오프라인)도 전환 직전에 `Capture(중도 종료)` — 그 시점까지의 누적값·경과 시간으로 확정하고 페이로드로 보낸다.
7. `ResultStatsView` 를 플레이어 행 렌더링 가능한 형태로 확장(행 프리팹/컨테이너 직렬화 필드, 이름으로 찾는 기존 방식과 일관). **씬·프리팹 파일은 수정하지 않는다** — 필요한 오브젝트 이름·필드·계층을 배선 지시서(`Docs/tech/result-stats-ui-setup.md`)로 남긴다.
8. EditMode 테스트: 집계기(필터·막타·중복 사망 방지·Spawn (s,s) 무시) + 페이로드 직렬화 왕복. `Managers/Editor/` + `Tools/Tests/결과 통계 EditMode 테스트 실행` 러너.
9. **문서 갱신**(PR 대신 — 실제 코드 기준으로):
   - 신규 `Docs/tech/handoff-result-stats-monster-hooks.md` — 받는 사람 **경석**. 몬스터 파일 4곳(`TwentyThreeBoss`·`GauntletBot`·`SpinnerBot`·`WallBot`)과 `MonsterBase` getter 에 무엇을 넣었는지, 판정 로직은 불변이라는 것, 새 보스·중간보스가 간파 창을 쓰면 성공 분기에서 같은 이벤트를 발행해야 한다는 규칙.
   - `Docs/tech/boss-rebuild-standard.md` §3.4 판정/권한 — 위 규칙 한 단락 + handoff 링크. §8 조립 체크리스트에 한 줄.
   - `Docs/tech/floating-damage-design.md` — `ApplyHealthDamage` 가 통계용 정적 이벤트를 발행한다는 것(판정 무수정 원칙 유지).
   - `Docs/tech/scene-flow-audit.md` 의 Result 전환/페이로드 서술이 있으면 가변 페이로드·ExitButton Capture 로 갱신.
   - `CONTEXT.md` 작업 세션 항목.

### 2단계 — 씬 UI (claude-alt, Unity MCP 포트 3001)
- 배선 지시서대로 `5.ResultScene.unity` 에 플레이어 행 컨테이너·행 프리팹 생성/배선, 저작 메뉴(멱등)로 만들 것. Refresh → 컴파일 확인 → EditMode 러너 → 통과 수 보고 → 커밋.

## 범위 밖
- 부활 횟수 · 받은 피해 · 일반몹 그로기 · 플레이어 이름(없음) · 통계 연출/애니메이션 · 데이터 테이블 노출.

## 리스크
- 몬스터 파일(경석 영역) 4곳 수정 — 이벤트 발행 1줄씩만, 판정 로직 불변. handoff 문서로 공유(1단계 9).
- `Unit.ApplyHealthDamage` 는 전투 핫패스 — 구독자 없으면 비용 0 이어야 함(할당 금지).
- 다중 히트 투사체·스킬 owner 누락 시 데미지가 귀속 안 됨 → 테스트/Play 로그로 확인.
- 페이로드 크기: 플레이어 수 × 고정 필드라 작음. 상한 초과 시 잘라서 경고.

## 검증
- EditMode 러너 전부 통과(claude-alt 가 실행·보고).
- ⏳ 은희 MPPM 3인 Play: 보스 클리어 / 전멸 / ExitButton 3경로에서 호스트·클라 결과 화면 값이 같은지, 데미지·간파·사망이 플레이어별로 갈리는지.
