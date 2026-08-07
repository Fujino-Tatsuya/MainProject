# 보스 현행 구현 문제 감사 — 재작성 근거

> 작성 2026-08-06 · **독립 2개 레인 교차 조사** (Claude / Codex CLI, 서로 정보 공유 없음).
> Codex 에는 기존 설계 문서 4종을 **읽지 못하게 막고** 같은 범위를 조사시켰다.
> 원본: `codex-boss-audit.md` (스크래치패드) · 재작성 설계는 [boss-fsm-design.md](boss-fsm-design.md).

**결론: 부분 보수가 아니라 전면 재작성이 맞다.** 아래 문제들은 한 곳을 고쳐서 닫히는 종류가
아니라 "BT 가 상태 머신이고 Animator·이벤트 채널·애니 이벤트·블랙보드가 상태를 나눠 갖는"
구조 자체에서 나온다. **권위 상태가 없다는 것이 공통 뿌리다.**

---

## 1. 양쪽 레인이 모두 찾은 것 — 신뢰도 최상

### 1.1 🔴 23호 ↔ Wells 동기화에 권위 상태가 없다

- Wells 애니메이터 파라미터(`IsThrow`/`IsJump`/`IsGroggy`/`IsInit`/`IsDead`)를 세팅하는
  **C# 코드가 0건**이다 — 전부 BT 그래프가 구동한다. *(Claude)*
- Wells BT 는 `BossStateChanged` 를 **`StartOnEvent`** 로 받아 `TwentyThreeState` 블랙보드만
  갱신한다. `BossStateChanged` 는 `EventChannel<TwentyThreeState>` 하나뿐이다. *(Codex,
  `Wells.asset:2510/2559/2588`, `BossStateChanged.cs:10`)*
- 폭탄 취소도 상태가 아니라 **jump/die/groggy 클립의 time-0 `BombDestroyEvent`** 로 한다
  (`BombLauncher.cs:198` 주석에 명시). *(Claude)*
- `WellsState` enum 은 선언돼 있지만 **참조 0건 — 죽은 코드**다. *(양쪽)*

→ **이벤트 한 번 누락되면 Wells 는 23호와 분리된다.** "그로기인데 Wells 가 폭탄을 던진다"가
정확히 이 경로다. 코드 레벨 가드가 존재하지 않는다.

### 1.2 🔴 폭탄이 물리를 안 쓴다 — 수동 적분 + 수동 스윕

- `Launch()` 가 `isKinematic = true` · `useGravity = false` 로 만들고,
  `UpdateFlight()` 가 `Lerp + 4·h·t·(1−t)` **포물선 공식을 직접 계산**해 매 FixedUpdate
  `MovePosition` 한다. *(양쪽, `BombController.cs:189/203/308`)*
- 충돌은 물리가 아니라 구간 **SphereCast 수동 검사**(`CheckHitBetween`, `:375`). *(양쪽)*
- 착지는 `MakeFloor()` 의 `GroundProbe.TryFindGround` **레이캐스트**(`:499`). *(Claude)*

→ 반사·굴림·경사 같은 물리 상호작용이 **원천적으로 불가능**하고, 충돌 판정을 손으로 유지해야
한다. 실제로 이 수동 스윕에서 "폭탄이 보스 손 높이에 영구 정지" 버그가 두 번 났고, 그 흔적이
`:363~373` 의 장문 주석으로 남아 있다.

### 1.3 🔴 되쳐낸 거리 = 데미지 값 그대로

`BombHit()` → `LinearLaunch(hitContext.sourcePosition, attackInfo.damage)` —
두 번째 인자가 **거리(m)** 다. 계수가 없어 **데미지 1 = 1미터**. *(양쪽, `:346/211`)*

→ 데미지 밸런싱이 폭탄 비행 거리를 직접 바꾼다. 기획의 "거리 ∝ 피해량"은 맞지만
**비례 계수를 노출해야 한다.**

### 1.4 🔴 폭탄과 화염 장판이 한 오브젝트

- `BombController` 하나가 `bomb`(투사체)와 `floor`(장판)를 **자식으로 같이** 들고,
  폭발 후 **같은 NetworkObject 가 장판으로 변신**한다(`_bombState = Floor`, `:491/535`). *(양쪽)*
- `ApplyUniformScale()` 이 **루트를 스케일**해 장판·히트박스가 같이 커진다(주석도 인정, `:471`).
- 중첩 폭발은 기존 장판을 `OverlapGrow()` 하고 **새 폭탄은 자기를 despawn** →
  두 번째 폭탄의 장판은 아예 안 생긴다(`:515~531`). *(Claude)*

→ 폭탄 1개 = 장판 1개로 고정되고 수명·크기·판정이 전부 얽힌다. **분리가 필요하다**(팀장 지시).

### 1.5 두 아키텍처 병존

`Monster/Boss/BossBase` 는 서버 NetworkVariable 기반 코드 FSM, Wells&23 은 BT + 블랙보드.
같은 프로젝트 안에 상태 소유 방식이 두 개다. *(양쪽)*

---

## 2. Codex 만 찾은 것

### 2.1 🔴 Charge 종료 판정이 `==` 라 교착한다 — **검증 완료**

```csharp
if (_destroyCount == _max) _isDefeated = true;   // ChargeController.cs:168
if (_reachedCount == _max) _isReached  = true;   // :192
```

**파괴와 도달이 섞이면 두 플래그 모두 영원히 false** 가 되어 BT 가 Charging 에서 못 나온다.
코드에 **2026-07-30자 진단 주석과 에러 로그가 이미 이 교착을 정확히 기술**하고 있다(`:173~186`,
`:200~206`). 즉 알려져 있었고 아직 안 고쳐졌다.

→ 재작성에서 **`>=` + 파괴/도달 합산 완료** 로 바꾼다.

### 2.2 Wells 애니메이터 트리거가 sticky 하게 남을 수 있다

AnyState 에서 `IsGroggy`/`IsDead` 즉시 전이 + 일부 전이는 **조건 없이 Idle 복귀**
(`WellsController.controller:242`). 트리거 소비/리셋 순서가 보장되지 않으면 Throw/Jump/Groggy
재진입 또는 잘못된 복귀가 가능하다. *(추론 — 재현은 못 함)*

### 2.3 폭탄 기능이 두 갈래로 남아 있다

- 구버전: `HoldBombAction` / `ThrowBombAction` (`BombInstance` 기반)
- 현행: `BombAction` (`BombLauncher` 기반, Wells BT 가 실제로 쓰는 것)

→ **구버전 2개는 죽은 코드.** BT 제거 시 함께 삭제.

### 2.4 BT 가 Animator 를 직접 건드린다

`SetAnimatorTriggerAction` 이 BT 그래프 안에서 Animator 를 직접 조작한다
(`No.23.asset:42204`, `Wells.asset:4571`). BT 블랙보드 `CurrentState` · Animator `State` ·
트리거가 **서로 다른 타이밍에** 바뀌면 전이 경합과 Enter/Exit 비대칭이 난다.

→ "BT 조건을 제대로 못 넣어주니까 애니메이션이 꼬인다"의 구조적 원인이다.

---

## 3. Claude 만 찾은 것

### 3.1 🔴 Wells 는 **스폰되지 않는** 중첩 NetworkObject다 — 재작성 설계에 직접 영향

`WellsAnimEvents.cs:7~23` 의 주석이 사고 기록을 남기고 있다: NGO 는 프리팹의 중첩
NetworkObject 를 스폰하지 않는다(씬 오브젝트만 지원). `NetworkBehaviour.IsServer` 는 스폰 시
대입되는 **필드**라 미스폰 상태에서 영원히 false → 그 클래스가 `NetworkBehaviour` 였을 때
**모든 애니메이션 이벤트를 조용히 삼켰다.**

현재 우회 = `MonoBehaviour` + `NetworkManager.Singleton.IsServer` 직접 조회.

→ **Wells 는 자기 `NetworkVariable` 을 가질 수 없다.** 재작성에서 Wells 상태는 반드시
**23호의 NetworkObject 에 실어야** 한다(`_wellsStateRaw` 를 23호에 두고 각 클라가
`OnValueChanged` 로 로컬 Wells Animator 를 구동).

### 3.2 `MakeFloor()` 가 경사 회전을 계산한 직후 버린다

```
:501-502   transform.rotation = _baseRot * slopeRot;    // 경사면 정렬 계산
:533       transform.rotation = Quaternion.identity;    // ← 덮어씀
```

중첩 폭발 경로만 early return 이라 회전이 살아남는다. **정상 경로에서는 경사면 정렬이 항상
무효화**된다 → 경사에서 장판이 지면을 뚫거나 뜬다.

### 3.3 Wells 애니메이터의 `State` Int 는 죽은 파라미터

`WellsController` 에 `State`(Int) 가 선언돼 있지만 **전이 조건이 전부 트리거**라 쓰이지 않는다.
23호는 `State` 로 구동하는데 Wells 는 트리거로 구동 — **두 보스의 애니 구동 규약이 다르다.**

### 3.4 진단 로그가 프로덕션에 상시 출력된다

- `BombLauncher`: BombHold 성공 / BombThrow 호출 / `groundMask.value` — **폭탄마다 3줄**
- `BombController`: `ground.value` — 폭탄 스폰마다 1줄

교훈 로그 #8("진단이 신호를 덮음")에 정면으로 걸린다. 폭탄은 주기적으로 계속 생성되므로
콘솔이 이걸로 덮인다. → 재작성 시 제거하거나 `#if UNITY_EDITOR` 로 가둔다.

### 3.5 죽은 코드

`BombController.SetEnableClientRpc` (호출부 0건) · `Bomb.cs` 의 주석 처리된 레거시 트리거 브리지.

---

## 4. 검증에서 기각·완화된 것

| 주장 | 출처 | 검증 결과 |
|---|---|---|
| 프리팹이 `TwentyThreeInitializer` 를 참조하는데 실제 클래스는 `TwentyThreeWells_Initializer` — "매우 취약" | Codex #6 | **과장.** `m_Script` GUID(`d0c245cd…`)가 `TwentyThreeWells_Initializer.cs.meta` 와 **정확히 일치**한다. `m_EditorClassIdentifier` 는 에디터 표시용 **캐시 문자열**이라 클래스명 변경 후 갱신이 안 된 것뿐이고 바인딩은 정상이다. 프리팹을 한 번 재저장하면 사라진다 |
| 장판 성장이 클라에 복제 안 될 것 | Claude 초기 의심 | **기각.** `Bomb.prefab` 에 `NetworkTransform` 이 **2개**(루트 + 장판 자식)이고 둘 다 `SyncScale` 켜져 있다 |

---

## 5. 재작성이 반드시 닫아야 하는 것 — 체크리스트

| # | 문제 | 재작성에서 |
|---|---|---|
| 1 | 권위 상태 부재 | 서버 `NetworkVariable` 단일 소유. Animator 는 **결과 재생만** |
| 2 | Wells 동기화가 이벤트 채널 의존 | 23호 FSM 이 Wells 를 **단방향으로 밀어준다**. 이벤트 채널 폐기 |
| 3 | Wells 가 미스폰 중첩 NetworkObject | Wells 상태를 **23호 NetworkObject 에 실어** 복제 |
| 4 | 폭탄 = 장판 (한 오브젝트) | **투사체와 장판을 별도 오브젝트/수명**으로 분리 |
| 5 | 폭탄 물리 미사용 | 물리 채택 여부를 명시적으로 결정(§6 질문) |
| 6 | 되쳐낸 거리 = 데미지 | **비례 계수를 SO 로 노출** |
| 7 | Charge `==` 교착 | `>=` + 파괴/도달 **합산** 완료 |
| 8 | BT 가 Animator 직접 조작 | Animator 구동을 **`SetState()` 단일 지점**으로 |
| 9 | 경사 회전 덮어쓰기 | `MakeFloor` 정상 경로에서 회전 보존 |
| 10 | 죽은 코드 | `WellsState`, Hold/ThrowBombAction, `SetEnableClientRpc`, Wells `State` Int |
| 11 | 진단 로그 상시 출력 | 제거 또는 에디터 전용 |

---

## 6. 이 감사에서 새로 생긴 질문 — 2026-08-06 팀장 답변 완료

| # | 질문 | 결정 |
|---|---|---|
| **A1** | 폭탄을 물리로 바꿀까, 수동 보간 유지할까 | ✅ **물리로 바꾼다.** 내가 낸 "수동 보간 유지" 권고는 **틀렸다** — 보스는 서버 권한 + `NetworkTransform` 복제라 **클라가 물리를 재현할 필요가 없다.** 서버만 시뮬레이션하면 되고, 그러면 수동 스윕이 통째로 사라진다 |
| **A2** | 장판 분리 후 수명은 | ✅ **자기 타이머로 소멸.** 중첩 성장은 **같은 타입끼리만**. 🔴 그리고 **장판은 앞으로 여러 종류가 된다 — 늪 / 화염 / 독 / 번개 등** → "화염 장판" 전용이 아니라 **타입 있는 일반 `AreaZone`** 으로 설계해야 한다 |
| **A3** | 피격 리액션 클립(`getowned01/02`)을 쓸 것인가 | ⏸ **회의 안건.** **카운터 성공 시** 재생하는 방향으로 판단됨. 다만 실질 hit 리액션은 **23호 행동을 취소하고 hit 애니 재생**이라, 카운터에만 적용할지 전체 적용할지는 회의에서 정한다 |

### A2 가 설계에 미치는 영향

장판이 한 종류가 아니게 되므로 **축이 두 개**가 된다. 기존 `FloorAreaEffect.AreaType
{ None, GrowOnOverlap, GrowOverTime }` 은 **성장 방식**이지 원소 타입이 아니다.

- **원소 타입** (Fire / Swamp / Poison / Lightning …) → **중첩 성장 여부를 가르는 기준**
- **성장 방식** (중첩 성장 / 시간 성장) → 어떻게 커지는가

두 축을 한 enum 에 밀어 넣으면 종류가 늘 때마다 조합 폭발이 난다.

### A3 에 대한 구현 관점 메모

카운터 성공 경로는 **이미 행동을 취소하고 `Groggy` 로 간다.** 그래서 `getowned` 를
**`Groggy` 진입 직전 리액션**으로 끼우는 것은 구조 변경 없이 된다.

반면 **전체 적용**(일반 피격에도 행동 취소)은 "보스는 아무 공격에나 경직되지 않는다"는
기존 원칙과 정면 충돌한다 — 그쪽으로 가면 **공격 선택기·쿨다운·슈퍼아머 설계가 함께 바뀐다.**
회의 전까지는 **카운터 전용**을 가정하고 짠다.
