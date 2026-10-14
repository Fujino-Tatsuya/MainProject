# handoff — 공용 벽 충돌 이벤트(할 일 32) · 몬스터 쪽 최소 훅

> 대상: 경석(몬스터 담당). 작성: 은희 레인(claude-alt) 2026-10-13. 설계·API·레이어 표: [wall-concept.md](wall-concept.md).
> 요약: 넉백으로 밀린 유닛이 **Wall 레이어 콜라이더**에 닿으면 서버 이벤트 `UnitWallImpact` 를 1회 낸다. **효과 없음(이벤트만)** — 몬스터 동작은 바뀌지 않는다.

## 1. 몬스터 파일에 넣은 것(최소 훅)
| 파일 | 바꾼 것 | 동작 영향 |
|---|---|---|
| `Monster/MonsterBase.cs` | 필드 `_wallImpactEpisode`·`_wallImpactPusher` · `TryEnterKnockback` 새 진입 때 `Begin()` · `ReceiveAttack` 이 넉백 상태면 민 쪽(`hitContext.sourceTransform` 의 Unit) 기록 · `HandleKnockback` 이동 뒤 `UnitWallImpacts.ServerProbe(...)` 1줄 · `ExitKnockback` 에서 `End()` | 없음(판정·이벤트만) |
| `Unit/Weapon/LinearKnockback.cs` | 임펄스 시작 때 에피소드 `Begin()` · 서버 `FixedUpdate` 에서 `ProbeWallImpact()` · `EndKnockback` 에서 `End()` | 없음 |
| `Monster/TurretHeadAim.cs` | `laserBlockers` 코드 기본값(프리팹 0 일 때)에 **Wall 추가** | 벽을 Wall 로 옮긴 뒤에도 예고선이 벽에서 끊긴다 |
| `Map/MapNavMeshBaker.cs` | 베이크 `layerMask` 에 **Wall 추가** | 벽을 Wall 로 옮긴 뒤에도 벽이 NavMesh 에서 빠지지 않는다(이전 Default 와 같은 집합) |

- Q 홀드 견인처럼 매 틱 `TryEnterKnockback` 이 갱신되는 경우는 같은 에피소드 — 벽 충돌 1회.
- 보스(23호)는 `MonsterBase` 지속넉백을 타지 않으므로(안 밀림) 이 훅이 이벤트를 내지 않는다.

## 2. 쓰는 법(몬스터 기믹에서 벽 충돌을 쓰고 싶을 때)
- 특정 몬스터: `unit.ServerWallImpacted += impact => { ... }` (서버). 전역: `UnitWallImpacts.ServerImpacted`.
- 새 '밀리는 이동'을 직접 만들면: 시작 때 `episode.Begin()`, 매 틱 이동 뒤 `UnitWallImpacts.ServerProbe(this, episode, dir, 요청거리, 실제거리, 속도, 민쪽, out _)`, 끝날 때 `episode.End()`.
- 연출은 `Unit.WallImpactPresented`(모든 피어)에 붙인다.

## 3. 23호 보스방 `InvisibleBoundaries` 벽 판정 — **바꾸지 않았다**. 이관 방법만
현재: `TwentyThreeBoss.TickVulnerableKnockback` → `HitsArenaWall(next, dir)` = `InvisibleBoundaries/Boundary_XMin|XMax|ZMin|ZMax` 박스 안쪽 면(방 로컬)과 몸 반경 비교 → `ApplyEnvironmentInteraction("외곽 벽 충돌")` + 반동.
옮길 때(경석 판단):
1. 보스방 벽(또는 `Boundary_*` 박스)이 **Wall 레이어**인지 확인 — `BossRoomAuthoring` 이 경계 박스 부모에 `wallLayer`(Wall)를 준다. 실제 프리팹 값 확인 필요.
2. `TickVulnerableKnockback` 에서 넉백 시작(`StartVulnerableKnockback`) 때 `WallImpactEpisode.Begin()`, 이동(`agent.Move`) **뒤** `UnitWallImpacts.ServerProbe(this, ep, _kbDir, step, moved, _kbSpeed, 민쪽, out _, 0.4f, 1.6f)`.
3. `HitsArenaWall` 분기를 `ServerWallImpacted` 결과(같은 틱 플래그)로 바꾼다 — 튜토리얼 스피너봇(`TutorialSpinnerPractice.OnServerWallImpacted`)과 같은 패턴. 반동·제압·`_environmentConsumed` 규칙은 그대로.
4. ⚠️ 차이: 지금 판정은 이동 **전** 다음 위치로 보고(`transform.position + delta`), 공용 판정은 이동 **뒤** 몸 캡슐 스윕이다. 보스 몸 반경(1.53)이 보스방 NavMesh 여유 띠(≈1.5)보다 크면 막힘 거리(1.6) 안이라 잡히지만, Play 로 확인할 것(09-29 `LogNavMeshMargin` 참고).
5. `PlayerBlocksLunge` 로 플레이어에 막히는 경우: 공용 판정은 플레이어를 '막힘'으로 보아 벽 충돌이 아니다 — 지금 동작과 같다.

## 4. 확인(사람이 Play)
- 일반 몬스터를 Q 넉백으로 벽에 박기 → 콘솔 `[WallImpact] <몬스터> → 벽 '<콜라이더>' · 민 쪽 <플레이어>` 1줄. 같은 넉백에서 두 번 안 나온다.
- 존 벽이 아직 Default 면 이벤트가 안 난다(정상 — 세팅 전).
