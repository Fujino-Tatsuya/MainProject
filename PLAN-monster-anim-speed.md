# PLAN — 몬스터 고유 공격속도(애니 재생 배율) · 이동 애니 속도 맞춤 (2026-10-02 · 경석 · ✅ 승인·구현 · development 반영 10-03)

> 요청(기획 → 팀장 10-02): ① 공격속도 = **실제 공격 애니 클립 재생 속도**를 줄이거나 늘리는 것. ② 이동이 임시값이라
> 클립에 맞지 않아 **미끄러지듯** 움직인다 — 고쳐야 한다.
> 범위: 일반·중간보스 **8종**(ChompBot · GauntletBot · HumanoidBot · MortarBot · PeekABot · SpinnerBot · TeslaBot · WallBot). **23호 제외**.

## 1. 현재 (조사 10-02)

- `MonsterDataSO.attackSpeed` = **초당 공격 횟수**. 쿨다운 폴백(`1/attackSpeed`, `MonsterBase.cs:1525`)에만 쓰인다. 재생 속도와 무관.
- 공격 애니 상태는 전부 speed 1 · 배수 파라미터 없음. 일반 공격 판정은 **애니 이벤트**(`OnAttackHit`/`OnAttackEnd`) → 재생 속도를 바꾸면 따라온다.
  **코드 타이머**인 것: `attackDuration` 안전망 · SpinnerBot 회전 예고/돌진/반복 · WallBot 방패 3단(창+돌진+충격) · GauntletBot 펀치 예고 · Smash 카운터 창(1.5초).
- `animator.speed` 는 자세 홀드(`MonsterBase.cs:2018`)·23호가 이미 쓴다 → 배율로 쓰면 충돌. **상태별 Speed Multiplier 파라미터**로 간다.
- 이동: 루트모션 전부 꺼짐. `agent.speed` = 데이터, Animator 에는 실제 속도(`RunBlend`)만. 클립 재생 속도는 고정 →
  예) Humanoid 추격 4.5 인데 Walk 블렌드 임계 2 → 미끄러짐. 복귀는 `moveSpeed × 5` 라 항상 미끄러진다.
- 돌진 속도가 `moveSpeed` 에 묶여 있다(Spinner ×8 · WallBot ×dashSpeedMultiplier) — 이번엔 이동 수치를 안 바꾸므로 영향 없음.
- 네트워크: 애니는 상태 복제 + 피어 로컬 재생. 판정은 **서버 Animator** 이벤트. 배율이 SO 상수면 복제 불필요(각 피어가 같은 SO).

## 2. 확정 (grill 10-02 팀장)

| 항목 | 결정 |
|---|---|
| `attackSpeed` 의미 | **몬스터 고유 공격속도 = 공격 애니 재생 배율**(1 = 원본, 2 = 두 배 빠르게). 인스펙터 설명도 그렇게 |
| 쿨다운 | **새 필드로 분리**(`attackCooldown`, 초). 기존 값은 `1 / 옛 attackSpeed` 로 이전 → 현재 밸런스 유지 |
| 코드 타이머 | **모두 배율대로**(예고·지속·카운터 창 포함 ÷ 배율). 화면과 판정이 안 어긋나게 |
| 이동 | **애니를 속도에 맞춘다** — 이동 수치는 그대로, 이동 클립 재생 속도 = 실제 속도 ÷ 클립 고유 속도 |
| 범위 | 8종. 23호 제외(잡기·점프가 이미 `animator.speed` 사용 · 행별 수치) |
| 터렛 조준 예고 | **배율을 따른다**(초록·주황 추적 + 빨강 고정 = `telegraphSeconds`·`aimHoldSeconds` ÷ 배율) |
| 돌진 | **시간 ÷ · 속도 × · 가속 ×** 배율, 최대 거리(Spinner 40m · WallBot 8m)는 그대로 |

### 2-1. Codex 설계 회의(10-02) — 반영
Codex 는 시간 한도로 최종 보고 전에 멈췄고 중간 판정 4건을 건졌다. 숫자로 확인 가능한 것은 전부 맞았다.
| 지적 | 확인 | 반영 |
|---|---|---|
| 🔴 Gauntlet 펀치 예고 중 상태는 이미 Attack 인데 공격 클립은 아직 — `State==Attack` 으로 배율을 정하면 예고 동안 대기·이동 애니가 빨라진다 | 구조상 맞음 | **배율 판단 = 실제 재생 중인 애니 상태**(로코모션 블렌드 상태면 이동 규칙, 그 밖 공격 상태면 attackSpeed) |
| 🔴 돌진 `속도× · 시간÷` 만으로는 NavMeshAgent 가속 때문에 거리가 안 맞는다 | 타당 | 가속도도 × 배율 |
| 🟡 프리팹 저장값 ≠ 코드 기본값 | ✅ Gauntlet 카운터 창 **2초** · Spinner 돌진 ×15·1.2초·40m · WallBot ×6·0.8초·8m · Spinner/Wall 창 1.5초 | 저장값 기준 |
| 🟡 Peek·Tesla 터렛 컨트롤러 2 레이어 | ✅ | `animator.speed` 가 두 레이어 모두에 — Play 확인 |
| 🟡 Mortar 발사 단계 · WallBot 평타 2단 트리거가 서버에서만 나가는 경로 | 배율과 별개의 기존 문제일 수 있음 | 구현 때 확인 · 있으면 보고 |
| ⚪ 데이터 에셋 = 8종 + `RangedMobileData`·`RangedTurretData` + `No23`·`No23_Solo` | ✅ 12개 | 이전 대상 12개 |
| ⚪ Chomp 컬링 모드 1(CullUpdateTransforms) — 서버 히트 이벤트 누락 위험 | 이 모드는 트랜스폼 쓰기만 멈추고 상태 머신·이벤트는 도는 것으로 앎(미실측) | Play 확인 |
| ⚪ 카운터 표시 0.15초 보정 · 데이터 이전 재실행 방지 | — | 포함 |

## 3. 접근

**S1 데이터 · 이전**
- `MonsterDataSO`: `attackSpeed` 툴팁 = "몬스터 고유 공격속도(공격 애니 재생 배율)". 신규 `attackCooldown`(초, 0 이면 쿨 없음 대신 경고).
- 쿨다운 폴백 `1/AttackSpeed` → `attackCooldown` 로 교체(`MonsterBase`). 23호 행 쿨 0 폴백도 이 값.
- 이전 메뉴 1회: 모든 `MonsterDataSO` 에셋 **12개**(8종 + `RangedMobileData`·`RangedTurretData` + `No23`·`No23_Solo`)
  `attackCooldown = 1/옛값`, `attackSpeed = 1`. 재실행 방지 = SO 에 `attackSpeedMigrated` 표시(true 면 건너뜀).
  - ✅ **10-02 실행 — 메뉴 대신 YAML 직접 수정**(Unity 꺼진 상태라 메뉴를 돌릴 수 없었다). 1회성 편집이라 재실행 위험이 없어
    `attackSpeedMigrated` 표시는 **만들지 않았다**. 12개 전부 표대로(float32 `1/옛값`: 1.6666666 · 1.4285715 · 1.25 · 2 · 1). `.meta` 미변경.
  | 몬스터 | 옛 attackSpeed | → attackCooldown |
  |---|---:|---:|
  | Chomp · Mortar · Wall | 0.5 | 2.0 초 |
  | Gauntlet | 0.6 | 1.67 초 |
  | Humanoid · Tesla | 0.7 | 1.43 초 |
  | Peek · Spinner | 0.8 | 1.25 초 |
  | No23 · No23_Solo | 1.0 | 1.0 초(행 쿨 0 폴백용) |

**S1-b 쿨다운 = 종료 기준 (10-02 팀장 추가 결정)**
- `attackCooldown` = 공격이 **끝난 뒤**(Attack 상태 이탈 — 어느 경로든) 쉬는 시간. attackSpeed 와 독립. 23호는 시작 기준 유지(`CooldownFromAttackEnd => false`).
- ✅ 코드: `MonsterBase.SetState` 에서 Attack 이탈 시 슬롯 재도장.
- ✅ 값 재이전(**팀장 10-02 A안 승인** — 현재 밸런스 유지) = `옛 쿨 − 공격 길이`, 측정 = `Tools/Monster/공격 클립 길이 보고`.
  공격 종류가 여럿인 몹은 값 1개로 둘 다 못 맞춰 **자주 쓰는 공격**(평타·채찍·펀치) 기준. 공격 길이는 전이 구간을 뺀 근사 → S5 Play 에서 재측.
  | Chomp | Gauntlet | Humanoid | Mortar | Peek | Spinner | Tesla | WallBot |
  |---:|---:|---:|---:|---:|---:|---:|---:|
  | 1.46 | 0.45 | 0 | 0.32 | 0.97 | 0.1 | 1.14 | 0.83 |
  - 0 = 공격이 끝나자마자 다음 공격(Humanoid 는 이전에도 공격 길이 1.6 > 옛 쿨 1.43 이라 이미 0 이었다). 0 경고는 **뺐다** — 의도된 0 이 매 스폰 경고를 내서.
  - `RangedMobileData`·`RangedTurretData` 는 어떤 프리팹·씬도 안 써서 1/옛값 그대로 둠.
  - ⚠️ 위 S1 표(1/옛값)는 **중간 단계**였다 — 지금 값은 이 표다.

**S2 재생 속도 단일 관리자 — 컨트롤러 무수정** (10-02 변경: 움직이는 6종의 컨트롤러가 전부 **SVN 아트 팩**에 있다)
- 컨트롤러에 파라미터를 추가하면 SVN 아트 수정 + git 코드와의 교차 의존(한쪽만 업데이트한 PC 에서 조용히 안 먹음) → **하지 않는다**.
- 대신 `MonsterBase` 가 `animator.speed` 를 **한 곳에서만** 정한다(전 피어, 매 프레임):
  | 지금 재생 중인 애니 상태(로직 상태 아님 — Codex 🔴) | animator.speed |
  |---|---|
  | 로코모션 블렌드(Idle/Walk) — 속도 > 문턱 | 실제 속도 ÷ `locomotionClipSpeed`, 범위 제한 |
  | 로코모션 블렌드 — 서 있음 | 1 |
  | 그 밖 상태 + 로직 `State == Attack` | `attackSpeed`(몬스터 고유 공격속도) |
  | 그 밖(피격 · 그로기 · 사망) | 1 |
  | 자세 홀드 | 0 — 기존 코드가 "1 로 복원"하던 것을 위 값으로 복원 |
- 23호는 자체 `animator.speed` 로직이 있어 이 관리자에서 빠진다(virtual 끄기).
- 판정은 서버 Animator 이벤트 → 서버도 같은 값으로 재생하므로 따라온다. 값이 SO 상수 + 복제되는 상태·속도에서 계산 → 추가 복제 없음.

**S3 코드 타이머 ÷ 배율**
- `attackDuration` 안전망 · Spinner(회전 예고·돌진 시간·반복 간격) · WallBot(창·돌진·충격) · Gauntlet(펀치 예고) · 카운터 창(`MonsterCounterWindow`, 표시 0.15초 보정 포함) ·
  터렛 조준 예고(`TurretHeadAim.telegraphSeconds`·`aimHoldSeconds`, 색 전환점도 같은 비율).
- 돌진: 시간 ÷ · 속도 × · **가속 ×** 배율(가속을 안 곱하면 NavMeshAgent 가 최고속에 늦게 닿아 거리가 줄어든다). 최대 거리는 그대로.
- 몬스터별 전수 목록은 구현 첫 커밋에 표로 남긴다(빠진 타이머 = 그 공격만 화면과 판정이 어긋남).

- ✅ **S3 구현 (10-02) — 전수 목록.** 배율 = `MonsterBase.AttackAnimSpeed` = **SO `attackSpeed` 직접**
  (🔴 `Unit.AttackSpeed` 는 서버에서만 초기화·비복제라 원격 클라에서 0 → 애니가 0.05배로 멈춘다. 구현 중 발견).
  | 몬스터 | ÷ 배율 (시간) | × 배율 (속도·가속) |
  |---|---|---|
  | 공통 base | `attackDuration` 안전망 · 공격 슈퍼아머 · 간파 표시 조기 소등 0.15초 | — |
  | 공통 `MonsterCounterWindow` | 창 길이(`WindowDuration`·`Open`) | — |
  | Gauntlet | 펀치 예고(타이머·안전망·슈퍼아머·바닥 예고) · 스매시 창 | — |
  | Spinner | 채찍 예고 · 준비(창 또는 `spinWindup`) · 돌진 시간 · 반복 히트 간격 | 돌진 속도·가속 |
  | WallBot | 창 · 돌진 시간 · 충격 시간 · 반복 히트 간격 | 돌진 속도·가속 |
  | Peek · Tesla | `TurretHeadAim` 추적·고정 시간(색 전환점은 비율이라 따라옴) | — |
  | Chomp · Humanoid · Mortar | 코드 타이머 없음(base 만) | — |
  - 돌진: 속도 ×r · **가속 ×r²** · 시간 ÷r → 궤적 그대로. 가속은 `ApplyAttackDashAgent`/`RestoreAttackDashAgent` 한 쌍.
    ⚠️ 뒤집음(10-02 교차검증 Codex·Claude 공통): 처음 계획은 가속 ×r 였다 — 가속 8 로는 최고속에 못 닿아 거리가 ½aT² 라 ×r 이면 1/r 로 준다.
  - 안 나눈 것(공격 밖): 스핀·방패 재사용 대기(6·8초) · 피격/그로기/넉백 · 자세 홀드 대기 한도(3초).

**S4 이동 애니 맞춤**
- 측정 도구(에디터): 이동 클립을 샘플링해 **발이 땅에 닿아 있는 동안 뒤로 미끄러지는 속도** = 클립 고유 속도(m/s, 블렌드 timeScale 반영). 몬스터별 SO `locomotionClipSpeed` 에 기록.
- 런타임: S2 관리자가 이동 중 `animator.speed = 실제 속도 / locomotionClipSpeed`, SO 범위(`min~max`, 기본 0.5~2.5)로 제한.
  `_animSpeed`(이미 복제되는 실제 속도)에서 각 피어가 계산 → 추가 복제 없음. 블렌드(`RunBlend`)는 그대로.
- 고정 터렛(Peek · Tesla)은 이동 없음 — 해당 없음.
- 복귀(×5)는 범위 상한에 걸려 여전히 약간 미끄러질 수 있다 → 상한·복귀 배수는 Play 보고 조정.

- ✅ **S4 구현 (10-02).** 측정 = `Tools/Monster/이동 클립 고유 속도 측정 (보고만 / → SO 기록)` (`MonsterLocomotionClipProbe`).
  - 🔁 **계획 보완:** 블렌드(Idle↔이동 클립, `RunBlend`=m/s) 임계값 th 아래에선 이동 클립 비중이 v/th 라 발도 그만큼 덜 나간다.
    `v / W` 만 쓰면 느리게 걸을 때 슬로모션이 된다 → **재생 속도 = max(v, th) / W**. th 를 SO `locomotionFullBlendSpeed` 로 추가(런타임은 블렌드 트리를 못 읽음).
  - 루트모션 클립 없음(전부 averageSpeed 0) → 발 측정: 매 샘플 더 낮은 발 = 디딘 발, 그 발의 −z 변위 속도 중앙값 × (timeScale × 상태 speed).
  | 몬스터 | 방법 | W (m/s) | th | 배회 → 재생 | 추격 → 재생 |
  |---|---|---:|---:|---:|---:|
  | Chomp | 발 | 2.36 | 4.5 | 2.5 → 1.90 | 4 → 1.90 |
  | Gauntlet | 발 | 2.93 | 2 | 2.5 → 0.85 | 3.5 → 1.19 |
  | Humanoid | 발 | 2.49 | 2 | 3 → 1.20 | 4.5 → 1.80 |
  | Mortar | 🟡 다리 뿌리 본(발 본 없음 — 과소 측정 의심) | 1.11 | 1.5 | 2.5 → 2.26 | 3 → 2.5(상한) |
  | WallBot | 바퀴(각속도 × 본 높이) | 3.24 | 2 | 2 → 0.62 | 3 → 0.93 |
  | Spinner | 측정 불가(본 없음) → 0 = 맞추지 않음 | — | 4 | 1 | 1 |
  - 복귀(moveSpeed × 5)는 상한 2.5 에 걸린다 — Play 보고 상한 조정.

### S4-b 교차검증 반영 (10-02 Codex + 맥락 없는 Claude 에이전트, 같은 프롬프트)
| 지적 | 출처 | 반영 |
|---|---|---|
| 🔴 터렛 사격이 레이어 1 → 0 번만 보는 관리자가 '이동 재생 중'으로 읽어 공격속도 미적용 | Codex | `UpperLayerActing` — 레이어 1+ 의 평소 상태(공격 밖에서 처음 본 해시)와 다르면 공격으로 본다 |
| 🔴 가속 ×r 로는 돌진 거리 1/r | 공통 | ×r² |
| 🟡 1D 블렌드는 자식 정규화 시간을 맞춘다 → 섞인 구간 이동 클립이 Lm/(w·Lm+(1−w)·Li) 배로 느려짐 | Claude | **팀장 (a) 정확식** — SO `locomotionIdle/MoveCycleSeconds` 추가, `FootSpeed` 산식 |
| 🟡 측정 실패 시 SO 를 0 으로 덮음 | Codex | 실패면 기록 안 함 |
| 🟡 PLAN·툴팁·`boss-rebuild-standard.md` 낡음 | 공통 | 갱신 |
| 🔴(기존) Mortar 발사·WallBot 평타 2단 트리거가 서버 로컬만 | 공통 | **팀장: 함께 작업** — `ServerSetFinishTrigger`(서버 + `Rpc(NotServer)`), WallBot 자체 ClientRpc(호스트 이중 발동) 제거 |
| 🟡(기존) Spinner 예고 40m vs 실제 ≈5.8m(가속 8) | Claude | **팀장: 예고를 실제에 맞춤** — `AttackDashReach`(가속·최고속 운동학). 목적지는 멀리 유지(autoBraking 회피) |
| ↳ Chomp 후속 | 팀장 | (a) 정확식만으론 Chomp 배회 9.1배 필요 → 상한 2.5 에 걸려 ≈72% 미끄러짐. **팀장 10-02: Chomp 만 움직이는 동안 블렌드 100%** — SO `locomotionFullBlendWhileMoving`(블렌드 값 = max(v, th)) → 재생 배회 1.06 · 추격 1.69. 출발 순간 대기→달리기 섞임은 사라진다(Play 확인) |
| ↳ Chomp Play(10-02) | 팀장 | 미끄러짐은 사라졌으나 **입이 너무 빠름** — 입·발이 한 클립(Run)에 묶여 둘 다 못 맞춘다. **팀장: 이동 속도를 낮춤** → `chaseSpeed` 4 → **2.8**(재생 추격 1.69 → 1.18 · 입 0.24 → 0.34초/회). 배회 2.5 는 그대로(1.06). Mortar 는 변경 후가 더 자연스러움(유지). 이어서 **`moveSpeed` 2.5 → 2.0**(배회 재생 1.06 → 0.85 · 입 0.38 → 0.47초/회, 팀장 10-02) |
| 버림 | — | 발 본 같은 다리 중복(로그상 `Foot_L,Foot_R` 확인) · RPC/NV 도착 순서 1~2틱(연출만) · 23호 타이머(attackSpeed=1, 툴팁 경고만) |

**S5 검증**
- 컴파일 · EditMode(이전 산식) · 데이터 이전 전후 쿨다운 동일 확인(표).
- 팀장 Play: 몬스터별 `attackSpeed` 0.5 / 1 / 2 로 공격 애니·판정·예고 일치, 걷기/추격/복귀 발 미끄러짐.

## 4. 리스크

- 🔴 `animator.speed` 를 쓰는 다른 코드가 관리자와 싸우면 값이 튄다 → 쓰는 곳 전수(자세 홀드 등)를 관리자 경유로 바꾼다.
- 🔴 `attackDuration` 을 안 나누면 배율 < 1 에서 클립이 끝나기 전에 공격이 끊긴다.
- `animator.speed` 는 레이어 전체에 걸린다 — 공격 중 다른 레이어(상체 등)도 같이 빨라진다. 몬스터 컨트롤러는 단일 레이어로 보이나 확인.
- Attack 상태로 들어가는 전이(크로스페이드) 구간도 배율을 받는다 — 짧아서 영향 작음, Play 확인.
- 클립 고유 속도 측정은 발 본 이름·접지 판정에 의존 — 측정값을 표로 보이고, 이상하면 SO 에서 직접 수정.
- 데이터 이전은 1회성 — 두 번 돌리면 `1/1` 로 덮인다 → 이전 표시로 막는다.
