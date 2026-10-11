# Handoff — 튜토리얼 스테이지가 몬스터 코드에 넣은 훅 (경석 공유용)

> 할 일 15 / `feature/TutorialStage`. 몬스터는 경석 영역이라 **최소 훅**만 넣고 여기에 무엇을·왜·어디를 바꿨는지 남긴다.
> 튜토리얼 쪽 설계: [tutorial-director.md](tutorial-director.md) §8 · 시나리오 [../design/tutorial-scenario.md](../design/tutorial-scenario.md) §2.2·§4.
> 스피너봇 연습/본 모드(`feature/TutorialSpinnerPractice`)는 별도 문서 `handoff-tutorial-spinner.md` — 이 문서와 겹치는 지점은 §2.

## 1. S4 — 몬스터 휴면 (`MonsterBase`)

**왜**: 튜토리얼은 몬스터를 맵 생성 때 전부 스폰한다(기존 그대로). 그런데 존2~4 몬스터는 "자기 차례"(존 진입·타이머 시작·다리 개통)까지
가만히 있어야 하고, 배리어 너머 원거리(거너 평타 14m·Q 16m·대상 지정 R, 팔라딘 R)에 맞아도 피해·넉백이 없어야 한다(§2.2).

**무엇을** — `Assets/1.Scripts/Monster/MonsterBase.cs`

| 자리 | 바뀐 것 |
|---|---|
| 필드 `_dormant` | 서버 전용 플래그. **기본 false**, 복제 안 함 |
| `public bool IsDormant` | 읽기(서버에서만 의미 있음 — 클라는 항상 false) |
| `public void SetDormantServer(bool)` | 켜기/끄기. 켤 때 진행 중 이동·공격·넉백을 정리하고 Idle(넉백 중이면 꺼 둔 에이전트를 다시 켬)·타깃 해제·간파 표시 끔 — `SetServerLogicSuspended(true)` 의 정리와 같은 순서 |
| `Update` | `if (_serverLogicSuspended \|\| _dormant)` — FSM(인지·추격·공격·`HandleKnockback` 밀기/Warp) 정지, 이동 블렌드 0 |
| `ReceiveAttack` 첫 줄 | `if (_dormant) return false;` — 죽은 대상과 같은 "거절". 피해·`PlayHitVFXRpc`·넉백·경직·인터럽트 누적·파생 `TakeDamage`(스피너봇/중간보스 간파 소비)·피해 숫자(`ClientDamageDealtClientRpc`)가 모두 빠진다 |
| `CanApplyHealthDamage` override (신규) | `!_dormant && base` — `ReceiveAttack` 을 안 타는 직접·비율 피해, `DevMonsterKill`(F9)의 `TakeDamage` 직접 호출도 막는다 |
| `OnKnockback` override (신규) | 휴면이면 무시 — `Unit.Knockback` → `LinearKnockback` 임펄스 경로 |

**영향 범위**: 호출처는 `TutorialMonsterZones`/`TutorialDirector` 뿐이고, 디렉터는 `TutorialStageMarker` 가 있는 스테이지(튜토리얼)에서만 켜진다.
절차 맵·Debug 씬·다른 스테이지의 몬스터는 `_dormant` 가 영원히 false 라 동작이 같다(추가된 분기 3개가 모두 false 로 지나간다).
`SetServerLogicSuspended`(보스 연출 정지 — 무적 아님)와는 플래그가 따로라 서로를 풀지 않는다.

**한계**
- 휴면은 서버에만 있다. 클라 쪽 타깃 선택(대상 지정 R 등)은 휴면 몬스터도 고를 수 있고, 맞으면 아무 일도 없다(피해·숫자·피격 이펙트 없음).
  빔·투사체 자체의 착탄 이펙트(공격 쪽 연출)는 그대로 보일 수 있다 — Play 에서 확인할 것.
- `ReceiveAttack` 이 false 를 돌려주므로 "죽은 대상이면 관통"하는 공격이 있다면 휴면 몬스터도 관통한다(의도 — 무적 대상이 탄을 먹지 않음).
- 상태이상: 몬스터 CC 는 전부 `ReceiveAttack`(AttackInfo 의 경직·넉백·인터럽트)과 `Unit.Knockback` 으로만 들어온다(`MonsterStatusEffect.ApplyStatus` 는 몬스터 자기 코드만 부른다) → 둘 다 막힘. 휴면 진입 때 기존 상태(스폰 슈퍼아머 등)는 지우지 않는다.

## 2. 🔴 스피너봇 레인과 병합 때 정리할 지점

- 이 브랜치: **`MonsterBase.SetDormantServer(bool)` / `MonsterBase.IsDormant`** — 모든 몬스터 공통 휴면(존 장부가 존3 스피너봇에도 건다).
- `feature/TutorialSpinnerPractice`: `SpinnerBot.SetDormant` / `EnterPractice` / `EnterRealMode`(진행 중).
- 정리 방향(제안): 스피너봇의 휴면은 base 의 `SetDormantServer` 로 합치고(`SpinnerBot.SetDormant` → base 호출 또는 삭제), 연습/본 모드만 스피너봇에 남긴다.
  이름이 겹치면(`IsDormant` 를 스피너봇도 선언) CS0108 경고 — base 쪽 하나로 정리.
- 존3 순서(S9): 활성 때 존 장부가 스피너봇을 휴면 → 존3 첫 진입 때 `SetMonsterDormantServer(spinner, false)` 후 `EnterPractice` → G5 → `EnterRealMode` → 처치 = G6(장부가 자동).
  연습 모드의 "죽지 않음"은 스피너봇 레인 몫이다(휴면 무적과 별개 — 연습 모드는 간파가 맞아야 하므로 휴면을 풀고 들어가야 한다).

## 3. 그 밖에 몬스터 쪽은 안 바꿨다
- `MonsterDeathEvents.ServerMonsterDied` 는 구독만 한다(세션 통계와 같은 채널).
- `MapContentSpawner`·존 프리팹·몬스터 프리팹·데이터 무수정. 존 배정은 스폰 뒤 위치로 튜토리얼 쪽이 계산한다.
