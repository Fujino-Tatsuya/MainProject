# PLAN-boss-prep-clips.md — 예고 구간을 **prep 클립**으로 교체 (2026-09-23)

> 그릴 4문항 확정. **승인 후 구현.** 구현에는 **에디터가 열려 있어야 한다**(저작 스크립트 + 컴파일).
> 상위 문서 — [PLAN.md](PLAN.md) §G2 · [PLAN-boss-backlog.md](PLAN-boss-backlog.md) · [CONTEXT.md](CONTEXT.md).

## 0. 한 줄

이지원 님이 올린 `_prep` 클립 4종(SVN r322)을 예고 구간에 재생한다.
지금은 **공격 클립을 0.15 지점에서 얼려** 준비 자세를 만들고 있는데, 그걸 **실제 준비동작**으로 바꾼다.

## 1. 실측 (r326 기준)

| 클립 | 길이 | `loopTime` |
|---|---:|---:|
| `Boss_23_dash_prep` · `hookL_prep` · `hookR_prep` · `uppercut_prep` | **각 0.5초**(30f@60) | **0** |

✅ **`loopTime: 0` 이라 나가는 전이만 없으면 마지막 프레임에서 자동으로 멈춘다.**
홀드를 위한 추가 코드가 필요 없다 — 교훈 #87 이 경고한 지점을 먼저 확인했고, 이번엔 조건이 성립한다.

⚠️ 참고 — `Boss_23_dash` 본체는 `loopTime: 1`(루프)이다. 이번 범위는 아니지만 기억해 둘 것.

## 2. 예고 경로가 **둘**이다

| 공격 | 경로 | 예고 길이 | prep | 처리 |
|---|---|---:|---|---|
| LeftHook · RightHook · Uppercut | `BeginTelegraph` | 0.7초 | ✅ | prep 0.5 재생 → **0.2초 홀드** |
| **MagneticGrab** | `BeginTelegraph` | 0.7초 | ❌ 없음 | 🔴 **현행(얼리기) 유지** |
| **DashAttack** | 카운터 선딜 게이트(`_counterWindup`) | 1.5초 | ✅ | prep 0.5 재생 → **1.0초 홀드** |
| Leap · Charging | 예고 없음 | — | — | 손대지 않음 |

## 3. 확정 사항

| # | 결정 |
|---|---|
| P1 | 훅L·훅R·어퍼 — prep 재생 후 **마지막 프레임 홀드**(예고 길이 0.7 유지) |
| P2 | 돌진 — prep 재생 후 **1초 홀드**(카운터 창 1.5초 유지) |
| P3 | 잡기 — **현행 얼리기 유지.** prep 상태명이 비면 자동으로 기존 경로로 폴백 |
| P4 | 컨트롤러 상태 4개는 **저작 스크립트로 멱등 생성**(수작업·YAML 직접 편집 금지) |

🔴 **P1·P2 가 "예고 길이를 클립에 맞추지 않는다"는 뜻이다.** 예고 길이는 반응 시간이자 밸런스 값이라
클립이 정하게 두면 안 된다 — 클립이 바뀔 때마다 난이도가 조용히 흔들린다.

## 4. 🔴 재생 시작점은 **바꾸지 않는다** (가장 중요한 판단)

`ReleaseTelegraph` 는 지금 공격 클립을 **`telegraphPoseNormalized`(0.15) 지점에서 이어** 재생한다.
prep 을 넣었다고 이걸 0 으로 바꾸면 **두 가지가 깨진다**:

1. **보스가 두 번 준비한다.** 공격 클립의 첫 0.15 가 그 클립 자체의 준비동작이고,
   prep 이 그 구간을 대신하는 것이다. 0 부터 틀면 겹친다.
2. 🔴 **`OnAttackHit` 타이밍이 밀린다.** 히트는 애니 이벤트 전용이고 타이머 폴백이 없다
   (정본 §3.3). 시작점을 0.15 → 0 으로 옮기면 판정이 `0.15 × 클립길이`만큼 늦게 나가는데,
   **컴파일도 테스트도 안 깨지고 데미지 타이밍만 어긋난다**(교훈 #109 와 같은 부류).

→ **바꾸는 것은 "예고 구간에 무엇을 보여 주는가" 하나뿐이다.** 릴리스 이후 경로는 전부 그대로다.
`telegraphPoseNormalized` 는 **얼리는 자세**가 아니라 **재개 지점**으로 의미가 좁아진다(주석 갱신).

## 5. 구현

### 5-1. 데이터 (`BossAttackEntry`)

```csharp
[Tooltip("[G2] 예고 구간에 재생할 준비동작 상태명. 비우면 공격 클립을 " +
         "telegraphPoseNormalized 지점에서 얼리는 기존 방식으로 폴백한다(잡기가 그렇다).")]
public string prepStateName = "";
```

- `No23.asset` — 훅L `LeftHookPrep` / 훅R `RightHookPrep` / 어퍼 `UppercutPrep` / 돌진 `DashPrep`
- 잡기·점프·차징은 **빈 문자열** → 기존 경로
- 🔴 `ValidateState` 에 추가한다(상태명 오타는 조용히 애니가 안 나온다 — 이 레포의 상습 사고)

### 5-2. 컨트롤러 (`No23Controller.controller`, git)

저작 스크립트 `Assets/1.Scripts/Monster/Editor/Boss23PrepStateAuthoring.cs`(신규):

- 메뉴에서 실행 → 없는 상태만 만든다(**멱등**)
- 상태 4개: `LeftHookPrep` · `RightHookPrep` · `UppercutPrep` · `DashPrep`
- 각 상태의 motion 을 **FBX 안의 해당 클립**으로 배선(이름으로 찾아 연결 — 문자열 오타 방지)
- 🔴 **나가는 전이를 만들지 않는다.** `loopTime: 0` + 전이 없음이 "마지막 프레임 홀드"의 조건이다.
- 🔴 클립은 SVN(FBX), 컨트롤러는 git 이다 — **한쪽만 받은 사람에게는 조용히 깨진다.**
  스크립트가 클립을 못 찾으면 `LogError` 로 크게 울린다.

### 5-3. 예고 재생 (`TwentyThreeBoss`)

`BeginTelegraph` 에서 갈린다:

```csharp
if (!string.IsNullOrEmpty(e.prepStateName))
    PlayPrepPoseClientRpc(CurrentAttackSlot);      // prep 재생 → 끝나면 자동 홀드
else
    HoldAttackPoseClientRpc(CurrentAttackSlot, e.telegraphPoseNormalized);   // 현행(잡기)
```

```csharp
[ClientRpc]
void PlayPrepPoseClientRpc(int slot)
{
    BossAttackEntry e = EntryFor(slot);
    if (e == null || animator == null) return;
    int hash = Animator.StringToHash(e.prepStateName);
    if (!animator.HasState(0, hash)) return;       // 검증은 스폰 시 ValidateState 가 이미 함
    animator.Play(hash, 0, 0f);
    animator.Update(0f);                            // 직전 자세가 한 프레임 보이지 않게(기존 규약)
}
```

- **자세 홀드 래치(`_counterAnimatorHeldLocally`)를 쓰지 않는다** — prep 은 speed 0 으로 얼리는 게
  아니라 클립이 스스로 끝까지 가서 멈춘다. 따라서 `RestoreCounterPose` 복원 대상이 아니다.
- ⚠️ 그래서 `ReleaseTelegraph` 의 `SetCounterPoseHeldClientRpc(false)`/`RestoreCounterPose()` 는
  **prep 경로에서도 그대로 불러야 안전하다**(멱등 — 안 걸려 있으면 아무 일도 안 한다).

### 5-4. 돌진 (다른 경로)

돌진은 `BeginTelegraph` 를 타지 않는다(`telegraphDuration: 0`).
`StartAttack` 의 `case BossAttackId.Dash: ShowDashTelegraph(e);` 옆에서 같이 prep 을 재생한다.

- 카운터 창 1.5초 동안 prep(0.5) → 마지막 프레임 1초 홀드
- 🔴 **돌진은 `_counterWindup` 자세 홀드를 쓴다.** prep 을 틀면 그 홀드와 충돌할 수 있다 —
  `SetCounterPoseHeldClientRpc` 가 언제 걸리는지 구현 중에 확인하고, 겹치면 prep 경로에서는
  홀드를 걸지 않도록 가른다.

## 6. 🔴 함정

1. **재생 시작점을 0 으로 바꾸지 말 것**(§4). 이번 작업의 가장 큰 위험이다.
2. **prep 상태에 나가는 전이를 만들지 말 것**(§5-2). 만들면 홀드가 깨진다.
3. **클립(SVN) / 컨트롤러(git) 이중 VCS** — 한쪽만 받으면 조용히 깨진다. 스폰 시 검증으로 막는다.
4. **잡기는 prep 이 없다**(§2). 두 방식이 공존하므로, 나중에 `grab_prep` 이 오면
   `No23.asset` 에 상태명만 채우면 끝나도록 데이터로 갈라 둔다.
5. `telegraphPoseNormalized` 의 의미가 **"얼리는 자세" → "재개 지점"** 으로 좁아진다. Tooltip 갱신.

## 7. 완료 기준

1. 훅L·훅R·어퍼 예고에 **준비동작이 움직여 보인다**(얼어 있지 않다).
2. 0.5초 뒤 **마지막 자세에서 멈춰** 있고, 0.7초에 공격이 나간다(예고 길이 불변).
3. 돌진 예고도 같은 그림 — prep 0.5초 + 1초 홀드, 카운터 창 1.5초 불변.
4. 🔴 **데미지가 그대로 들어간다** — `OnAttackHit` 타이밍이 안 밀렸는지 실측.
5. 잡기는 **기존과 동일**(얼린 자세).
6. 예고 부채꼴·띠가 **차오르는 속도 그대로**(예고 길이를 안 바꿨으므로).
7. 저작 스크립트를 두 번 돌려도 상태가 중복 생성되지 않는다(멱등).
8. 컴파일 에러 0 · 콘솔 경고 0.

## 8. 범위 밖

- `grab_prep` 요청 — 아트 일정. 오면 데이터만 채운다
- `Boss_23_dash` 의 `loopTime: 1` — 별개 건
- 예고 길이(`telegraphDuration`) 튜닝 — 이번엔 **안 바꾼다**(§3 P1·P2)
