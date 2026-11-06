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

## 대시 관통 — 일반 몬스터 2마리 (할 일 209)

> 2026-11-04 · 은희 · 브랜치 `feature/DashPassThroughSmall`. 위 기획(10-11 "모두 통과 불가")에서 **일반 대시만** 예외를 둔다.
> 몬스터 코드·프리팹은 손대지 않았다(플레이어 이동 스윕만).

| 대상 | 일반 대시(Shift) |
|---|---|
| 일반 몬스터(`MonsterBase`, `MonsterDataSO.rank = Normal`) | **2마리까지 뚫고 지나간다**, 3번째에서 그 직전에 멈춘다(대시 종료) |
| 중간보스(`rank = MidBoss` — Gauntlet·Spinner·Wall) · 보스(`rank = Boss` — 23호) | 처음부터 막힌다(종전 그대로) |
| 몬스터가 아닌 Enemy 몸(보스방 파일런 등) · 다른 플레이어 몸 | 막힌다(종전 그대로) |
| 벽·지형 | 종전 그대로 — 막혀도 대시 상태·무적은 종료 시각까지 유지 |

- 전 캐릭터 공통. 관통 수 **2 = 코드 상수 `PlayerUnitCollisionRules.DashSmallMonsterPassLimit`**(xlsx 칸 없음). 통과 여부 = `IsDashPassableUnit`.
- 어쌔신 Q·거너 간파 후폭풍처럼 유닛을 **전부** 통과하는 이동(`PassThroughUnitsOverride`)과는 별개다 — 그쪽은 Enemy·Player 를 마스크에서 빼고, 이쪽은 마스크는 그대로 두고 수를 센다.
- 213 어쌔신 대시 화살표 미리보기(`GetGroundedPreviewDistance`)는 관통 장부를 받지 않는다 — 벽에서만 끊기는 미리보기라 무관.

### 동작 (`Assets/1.Scripts/Player/`)

- **장부 `PlayerDashUnitPass`**(`Motor/PlayerUnitCollisionRules.cs`) — 이번 대시에서 통과한 몬스터 id 와 남은 수. `PlayerDashState.Enter` 가 `PlayerMotor.BeginDashUnitPass(2)`, `Exit` 가 `EndDashUnitPass()`.
- **이동 스윕 `PlayerMotionSweep.TryCast`** — 가장 가까운 막는 것이 Enemy 레이어 콜라이더면 그 몬스터(`GetComponentInParent<MonsterBase>`, 콜라이더 여러 개도 한 마리)를 장부에 올리고 다음으로 가까운 것을 다시 고른다.
  실제 경로에서 **가장 먼저 만난** 몬스터만 센다(벽 너머 몬스터는 세지 않는다). 이미 통과한 몬스터의 콜라이더는 대시 내내 무시한다.
- **유닛 막힘 검사(`IsUnitBlockingAhead`)·겹침 밀어내기(`ApplyUnitSeparation`)** 도 같은 장부를 읽는다 — 통과 중인 몬스터 때문에 대시가 끝나거나 옆으로 밀려나지 않는다.
  대시가 몬스터와 겹친 채 끝나면(3번째에 막힌 경우 등) 장부가 꺼지는 다음 틱부터 기존 밀어내기가 빼낸다.
- **대시 경로 예측(`PlayerMotor.PredictDashPath`, 간파 판정 191)** 은 진행 중 장부를 **베껴** 굴린다 — 실제 대시와 같은 자리에서 끝나고 진짜 장부는 건드리지 않는다.

### 네트워크

- 장부는 **시뮬레이션하는 피어가 각자 로컬로** 든다(복제 없음, 새 RPC 없음) — 오너 권위 이동에서는 오너만 센다. 서버 위치 보정 경로(`forceSnap` 등)는 바뀌지 않는다.
- 서버 권위 이동(`UsesServerAuthoritativeMovement`)에서는 서버 관측 시뮬레이션도 같은 장부(서버 쪽 대시 상태가 켬)로 같은 결과를 낸다.
- 오너 권위에서 서버의 대시 끝 예측은 빈 장부로 시작한다 — [dash-interrupt.md §8](dash-interrupt.md#8-알려진-한계).

### Play 확인

- **확인 씬 `Assets/0.Scenes/Debug/DebugDashPassLab.unity`**(빌드 목록 밖, 저작 메뉴 `Tools/Debug/209 대시 관통 테스트 씬 만들기` 로 다시 만듦 — `DebugInterruptLab` 복사 + 허수아비 제거 + 레인 A~E). 툴바 Dev Boot 씬 드롭다운 → `DebugDashPassLab` → ▶.
  몬스터는 `DebugDashPassLab` 컴포넌트가 서버에서 스폰하고 FSM 을 멈춘다(`SetServerLogicSuspended`, 피격은 됨 · 패널 "몬스터 다시 놓기"). 각 레인 START 칸에 서서 W(+Z)를 누른 채 Shift. 대시 쿨 0·F1 캐릭터 순환·배속은 원본 랩 그대로. 23호 줄(E)은 인스펙터 `enabled` 로 끌 수 있다. NavMesh 는 없다(정지 몬스터라 무관 — 에이전트 경고는 날 수 있다).
- 일반 몬스터 3마리를 일렬로 두고 대시 → 2마리를 뚫고 3번째 직전에서 멈추는지. 콘솔 `[Dash] 종료: … 관통유닛=2`.
- 중간보스·23호에 대시 → 지금처럼 바로 앞에서 멈추는지(`관통유닛=0`).
- 몬스터 1마리 뒤 벽 → 몬스터를 지나 벽 앞에서 멈추고 대시 시간 끝까지 상태 유지되는지.
- MPPM 클라 오너: 뚫는 동안·끝난 뒤 위치가 튀거나 되돌아가지 않는지(관찰자 화면 포함).
- **어쌔신 Q 확인(할 일 243, `DashPassLabAssassinQ`)** — 패널 아래 구역: `어쌔신으로 바꾸기`(F1 과 같은 길) · `그림자 회귀 특성(ASN_2_RETURN) 넣기`(기본 켬 — 랩에 `TraitDirector` 가 없어 처음 한 번 서버가 네트워크 프리팹 목록에서 스폰, 어쌔신이 아니면 자동으로 뺌, 특성 쿨 ×1.2 포함) · `Q·E·R 쿨 0`(기본 끔) · 사거리 라인(하늘색 원 = 플레이어 중심 반경 Q `dashDistance` = 회귀 최대 거리, 회귀 창 동안 출발점 표식·선 초록 닿음/주황 못 닿음) · 마지막 Q/회귀 이동 거리·시작→끝 시간. 어쌔신 Q 는 유닛 전부 통과(`PassThroughUnitsOverride`)·레인 C 벽에서 정지.
