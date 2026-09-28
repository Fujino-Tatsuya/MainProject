# PLAN-boss-death-telegraph.md — 사망 연출 순서 + 중간보스 공격 예고 (2026-09-28)

> 상태: **구현 완료 · Play 확인 대기** · 작업자: 경석(Claude) · 브랜치 `feature/Boss23`
> 팀장 확정(09-28 채팅): ① 죽는 애니 끝 → 디졸브 → 사라짐 → 넘어감 ② Gauntlet·Spinner 는 범위를 보여 준 뒤 공격.

## 1. 사망 연출 순서

**전**: 치명타 순간 사망 트리거·디졸브·디스폰 타이머가 **동시에** 시작 → 23호 사망 클립(≈2.58초)이 끝나기 전에 녹고(2.0초)
디스폰(2.5초), 결과 화면은 `Died` 기준 **고정 3초** 타이머라 연출 길이와 무관하게 넘어갔다.

**후** (`MonsterBase.PlayDeathEffectAfterClip`)
- 사망 트리거 파라미터가 있는 몹(23호 `Death`, Gauntlet `Defeat`, 원거리 `Death`)은 **실제 사망 상태의 남은 길이를 Animator 에서 재서** 기다린 뒤 디졸브.
  클립 이름·상태 이름이 몹마다 달라 이름으로 찾지 않는다. 사망 상태 진입 감지 0.5초, 대기 상한 5초(루프·오저작 방어).
- 파라미터가 없는 7종은 예전처럼 즉시(애니는 base 가 얼린다).
- 디졸브 RPC `Unreliable → Reliable` — 이제 한 번뿐인 신호라 유실되면 녹지 않고 툭 사라진다.
- `MonsterBase.ServerDeathSequenceCompleted`(디스폰 직전) → `BossEncounterDirector.HandleBossVanished` → **사라진 뒤 1초**(`resultDelayAfterVanishSeconds`) 결과 화면.
  안전망 `defeatResultTimeoutSeconds` 12초(치명타 기준). 옛 `defeatResultDelaySeconds` 는 삭제(씬에 남은 직렬화 줄은 무해).
- 23호 예상 타임라인: 0 치명타 → 2.58 디졸브 시작 → 4.58 다 녹음 → 5.08 디스폰 → 6.08 결과 화면.

## 2. 중간보스 공격 예고

| 공격 | 예고 | 길이 |
|---|---|---|
| Gauntlet 펀치 4종(L/R) | 판정 박스(`MeleeHitbox`) 그대로 바닥 띠 → 다 차면 펀치 모션 | `punchTelegraphDuration` 0.5초 |
| Gauntlet 스매시 | **기존 원형**(클립 이벤트) 유지 | — |
| Spinner 채찍 | 판정 박스 그대로 → 다 차면 채찍 | `whipTelegraphDuration` 0.5초 |
| Spinner 스핀 | 준비(카운터 창) 구간 전체 동안 **돌진 경로 띠**(박스 × 돌진 거리) | 창 길이(1.5초) |

- 표시는 23호 `BossAttackConeTelegraph` 재사용(빨간 윤곽 = 어디에 / 채움 = 언제). 재질은 `MonsterBase.attackTelegraphMaterial`
  (23호 표식과 같은 `c3d3d212…`) — `BossAttackConeTelegraph.MaterialOverride` 로 넘긴다. 23호는 칸을 비워 둔다.
- 🔴 예고와 판정이 **같은 콜라이더에서** 나온다(`TryMeasureBox`) — 따로 적으면 어긋난다.
- 🔴 두 중간보스 `FaceTargetWhileAttacking => false` — 예고 중 몸이 돌면 박스가 따라 돌아 "피했는데 맞는다". 조준은 시작 1회.
- 스핀 돌진 거리 = `min(dashMaxDistance, 속도 × dashDuration)` 후 navmesh 클램프 — `dashMaxDistance` 만 쓰면 과대 표시(교훈 #112).
  예고와 실제 돌진이 같은 `_dashTarget` 을 쓴다.
- 예고 중 모션 보류 · 안전망 타이머와 슈퍼아머를 예고만큼 연장. 공격이 끊기면 `OnStateChanged` 가 전 피어에서 예고를 끈다.

### 2-1. 1차 Play 후 수정 (팀장 09-28)
- 예고가 안 보였다 → 데칼 수신자가 **BossRoom 존에만** 표시돼 있었다. `MapContentSpawner` 가 **전 존**을 표시하게 변경.
- 사망 후 디졸브가 늦어 보였다 → `DissolveDeath.leadBeforeClipEnd` 0.3초(클립 끝 0.3초 전 시작). `[Death]` 로그 1줄.
- 폭이 너무 좁다 → **판정 박스 폭 ×2.5**(Gauntlet 1.8 → 4.5m, Spinner 1.5 → 3.75m). 예고는 박스에서 재므로 자동으로 같이 커진다.
  ⚠️ Spinner 는 같은 박스를 스핀 돌진에도 쓴다 → 스핀 경로 폭도 3.75m.
- 좌/우 공격은 방향에 맞춰 **옆으로 민다**(23호 훅 비율 ≈26%): Gauntlet `sideLateralOffset` 1.2m · Spinner 1.0m.
  판정은 `MonsterBase.MeleeHitShifted`(그 순간 박스 트랜스폼을 옮겼다 되돌림), 예고는 같은 값을 `lateralShift` 로.
- 인터럽트 실패 관찰: Gauntlet 카운터 창 1.5초 < 스매시 명중 ≈2.2초 → **마지막 0.7초는 예고만 있고 창은 닫혀 있다.**
  중간보스엔 창 열림 표시(`IBossTelegraph`)도 없다. → 취약 문서와 함께 결정(보류). 판정 로그 `[Gauntlet]` 추가.

## 3. 확인할 것 (Play)
- 23호·Gauntlet 사망 클립 끝까지 → 디졸브 → 사라짐 → (23호) 1초 뒤 결과 화면. 클라이언트 화면에서도 디졸브.
- 펀치·채찍·스핀 예고가 판정과 일치(예고 밖에서 맞지 않는가). 예고 중 회전 안 함.
- 예고 중 그로기·사망 시 예고가 남지 않는가.
