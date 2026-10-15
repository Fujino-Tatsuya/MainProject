# 인계 — 플레이어↔유닛 충돌 (몬스터 프리팹 콜라이더 활성화)

> 받는 사람: **경석**(몬스터 담당) · 보내는 사람: **은희**(Player) · 작성 2026-10-11 · 브랜치 `fix/PlayerUnitCollision`
> 기획 확정(김태현 10-11): 보스·중간보스·일반 몬스터 모두 **플레이어와 서로 통과 불가**, 플레이어끼리도 통과 불가,
> 겹치면 **플레이어만** 가장 가까운 빈 자리로 밀려난다 — **몬스터는 움직이지 않는다(AI·NavMesh 무변경).**

## 한 장 요약

| 무엇을 | 왜 |
|---|---|
| 일반 몹 5종(`ChompBot`·`HumanoidBot`·`MortarBot`·`PeekABot`·`TeslaBot`) + `WallBot` 의 **루트 몸 `CapsuleCollider` `m_Enabled: 0 → 1`** (각 프리팹 1줄, 그 외 무변경) | 플레이어 이동 스윕은 `Enemy` 레이어의 **활성 비트리거 콜라이더**만 장애물로 본다. 이 6종은 몸 캡슐이 꺼져 있어 플레이어가 그냥 통과했다. `GauntletBot`·`SpinnerBot`·23호는 이미 켜져 있어 막혔다 |

플레이어 쪽(스윕 마스크·밀어내기·대시 종료)은 전부 `Assets/1.Scripts/Player/` 안에서 처리했다. 몬스터 코드는 손대지 않았다.

## 꺼져 있던 이유 조사

- 6종 모두 최초 커밋 `a785c2b1`(2026-07-21 "몬스터 코드 FSM 프레임워크 + 일반몹5/중간보스3")부터 `m_Enabled: 0` 이었다.
  커밋 메시지·주석에 의도 표기 없음.
- 코드가 켜고 끄는 곳은 `MonsterBase` 사망 처리 하나다(`bodyCollider.enabled = false`, `MonsterBase.cs` 의 사망 정리).
  6종 모두 `bodyCollider` 가 바로 이 루트 캡슐을 가리킨다 → **죽으면 다시 꺼져 시체는 막지 않는다.** 런타임에 켜는 코드는 없다.
- 씬·다른 프리팹에 이 컴포넌트의 `m_Enabled` 오버라이드 없음(컴포넌트 fileID 전수 grep).

→ 의도적으로 끈 근거를 찾지 못해 켰다. 의도가 있었다면 알려 달라 — 되돌리고 플레이어 쪽에서 다른 방법을 찾는다.

## 몬스터 쪽 영향 확인 (에디터에서 프리팹 로드해 확인)

| 항목 | 결과 | 영향 |
|---|---|---|
| 루트 `Rigidbody` | 6종 모두 **`isKinematic = true`** (래그돌 뼈 RB 도 전부 kinematic) | kinematic↔kinematic 은 물리 반발이 없다. 몸 캡슐이 켜져도 **서로 밀거나 튕기지 않는다** |
| `NavMeshAgent` | Chomp·Humanoid·Mortar·Wall 에 있음(r 0.5, HighQuality 회피, prio 50). Peek·Tesla 는 없음(고정형) | NavMesh 회피는 **에이전트 반경**으로 하고 콜라이더를 보지 않는다 → 몬스터끼리 회피 동작 무변경. NavMeshObstacle 아님 → 카빙 없음 |
| 레이어 충돌 매트릭스 | Enemy↔Enemy · Enemy↔Player · Player↔Player 모두 **충돌 켜짐**(무시 아님) | 위 kinematic 이유로 반발 없음. 트리거 이벤트만 생길 수 있다(아래) |
| 트리거·히트박스 | `Hurtbox`(EnemyHurtBox, 트리거) · `MeleeHitbox`/`DashHitbox`(Weapon, 트리거)는 그대로 | 몸 캡슐(Enemy)이 켜지면 Weapon 트리거·플레이어 투사체가 **Enemy 레이어 몸에도 닿을 수 있다.** Gauntlet·Spinner·23호가 같은 구성으로 이미 돌고 있어 검증된 경로다. 플레이어 공격은 대상 중복을 HashSet 으로 거른다 |
| NavMesh 베이크 | 런타임 재베이크 없음 | 무관 |

## 남은 것 / 확인 부탁

- **웰즈(`Boss/Wells.prefab`)에는 Enemy 레이어 비트리거 몸 콜라이더가 없다** → 플레이어가 여전히 통과한다.
  이번엔 "m_Enabled 만" 범위라 콜라이더를 새로 달지 않았다. 웰즈도 막아야 하면 몸 캡슐(Enemy, 비트리거)을 달아 주거나 말해 달라.
- Play 확인(사람): 일반 몹 6종·중간보스·23호에 플레이어가 막히는지, 몬스터끼리 뭉칠 때 튀거나 멈추는 현상이 없는지.
