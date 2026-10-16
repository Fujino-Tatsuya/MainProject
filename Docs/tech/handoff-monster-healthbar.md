# 핸드오프 — 일반 몬스터·중간 보스 발밑 체력바

> 2026-10-15 · 브랜치 `feature/MonsterHealthBar` · 작성: Claude(claude-alt).
> 1차(da67f1f6) = 머리 위 바 + 런타임 두께 배율. **2차(사용자 피드백) = 발밑 위치 + 등급별 프리팹 2개 + 튜토리얼 스피너봇 이전.**
> **3차(사용자 추가 지시) = 지연 감소(잔상) 바** — §8. **4차 = 중간 보스 바 굵은 빗금** — §9.
> 몬스터는 경석 담당 영역이라 몬스터 프리팹은 **튜토리얼 스피너봇 1개만**(바 제거) 고쳤고, 나머지는 코드 훅 1줄로 런타임에 붙인다.
> Play 확인은 아직 안 했다(아래 §7 체크리스트).

## 1. 사양 (사용자 확정)

| 항목 | 동작 |
|---|---|
| 위치 | **발밑**(2차). 몬스터 위치(발) 화면 좌표에서 바 중심을 아래로 N픽셀 — 등급별 데이터(§5) |
| 프리팹 | 일반 = `MonsterHealthBar_Normal.prefab`, 중간 보스 = `MonsterHealthBar_MidBoss.prefab`(더 두껍다). `MonsterRank` 로 고른다 |
| 표시 | 처음엔 숨김 → **체력이 줄면 켜짐** → **전투 상태가 끝난 뒤 3초**(데이터) 후 숨김 |
| 재피격 | 다시 맞으면 숨김 타이머가 처음부터. 전투 상태 동안은 계속 유지 |
| 사망 | `Dead` 상태 또는 체력 0 이하면 **즉시** 숨김 |
| 최종 보스(23호) | 제외 — 보스 HUD(`BossHealthHUD`)가 따로 있다 |
| 웰즈 | **바 없음 — 의도(사용자 결정 10-15).** `BossWells` 는 MonsterBase 가 아니라 훅이 돌지 않는다. 그대로 둔다 |
| 튜토리얼 스피너봇 | 같은 중간 보스 바(발밑)를 쓰되 표시·색·라벨은 기존 튜토리얼 규칙(§4) |
| 멀티 | 각 피어가 복제값(`Unit._currentHp`, `MonsterBase._state`)만 읽어 로컬 판단. RPC·서버 로직·NetworkVariable 추가 없음 |
| 색 | 빨강(0.9, 0.2, 0.2) — 각 프리팹의 `defaultFillColor`. 라벨 없음 |

## 2. "전투 중" 판단 근거 (`MonsterState`)

`MonsterBase.State`(이미 있던 public getter, `_state.Value`)를 읽는다. **MonsterBase 에 프로퍼티를 추가하지 않았다.**

| 값 | 전투? | 근거(`MonsterState.cs` 주석 · `MonsterBase.TickServer`) |
|---|---|---|
| `Chase` | ✅ | 타깃 추격. 보스·근접은 사거리 안에서 쿨 대기할 때도 `Chase` 를 유지한다(`SeekMelee`/`SeekBoss`) |
| `Attack` | ✅ | 공격 모션 |
| `Hit` | ✅ | 피격 경직 — 끝나면 Idle 로 가 재평가하므로 교전 중 상태 |
| `Groggy` | ✅ | 그로기 |
| `Knockback` | ✅ | 지속 넉백 |
| `Idle` | ❌ | 대기(타깃 없음) |
| `Return` | ❌ | 리쉬 이탈 후 귀환 = **추격 종료**. 귀환 중 `Revive()` 로 체력이 차는 건 피격이 아니라 바를 다시 켜지 않는다 |
| `Dead` | ❌ | 즉시 숨김 |

⚠️ 고정 터렛(PeekABot·TeslaBot·RangedTurret)은 교전 중에도 쿨 대기·조준 동안 `Idle` 이다(`SeekTurret`).
쿨(0.97~1.25초)이 3초보다 짧아 `Attack` 이 3초 안에 다시 와서 바가 꺼지지 않는다. 쿨을 3초 넘게 늘리면 쏘는 사이에 바가 깜빡일 수 있다.

## 3. 바꾼 파일 · 코드 훅

| 파일 | 내용 |
|---|---|
| `Assets/1.Scripts/Monster/MonsterBase.cs` | (1차) **`OnNetworkSpawn` 끝에 1줄** `MonsterOverheadHealthBarRule.TryInstall(this)`. 모든 피어에서 돈다 |
| `Assets/1.Scripts/UI/Combat/UnitOverheadHealthBar.cs` | `ConfigurePlacement(worldHeight, canvasOffset)` — 런타임에 붙인 바의 기준 높이·화면 오프셋만 바꾼다(2차에서 두께 배율 제거). `DefaultFillColor` getter 추가. 플레이어 바는 부르지 않아 동작 변화 없음 |
| `Assets/1.Scripts/UI/Combat/MonsterOverheadHealthBarRule.cs` | `IOverheadHealthBarRule` 구현 + `TryInstall`(부착 판단 → 등급별 프리팹 Instantiate → 발밑 배치). 색 = 바 프리팹의 `defaultFillColor` |
| `Assets/1.Scripts/UI/Combat/MonsterOverheadHealthBarVisibility.cs` | 표시 판단 순수 로직 + `DecideInstall`·`ForRank`·`FootScreenOffset`(2차) |
| `Assets/1.Scripts/UI/Combat/MonsterOverheadHealthBarSettings.cs` | 설정 SO — 프리팹 2개 + 숨김 지연 + 발밑 오프셋 2개만 |
| `Assets/Resources/MonsterOverheadHealthBarSettings.asset` | SO 에셋. `Resources.Load` 로 읽는다(씬 참조 없이 런타임 부착하려고) |
| `Assets/2.Prefabs/UI/MonsterHealthBar_Normal.prefab` (2차 새) | guid `5c33ecbb342fd194ea857586168d286b`. 120×14 |
| `Assets/2.Prefabs/UI/MonsterHealthBar_MidBoss.prefab` (2차 새) | guid `c4c5ec006feba194fafd4150eff23e56`. Normal 의 Variant, 120×**21** |
| `Assets/2.Prefabs/Monster/Tutorial/SpinnerBot_Tutorial.prefab` | (2차) 박혀 있던 `OverheadHealthBar` 자식 인스턴스 **제거**. §4 |
| `Assets/1.Scripts/Tutorial/SpinnerPractice/TutorialSpinnerHealthBarRule.cs`·`TutorialSpinnerSpawnOverride.cs` | (2차) 주석만 — 바 출처가 바뀌었다 |
| `Assets/1.Scripts/UI/Combat/Editor/MonsterOverheadHealthBarVisibilityTests.cs` | EditMode 테스트 |
| `Assets/1.Scripts/Editor/CombatEditModeTestRunner.cs` | `^MonsterOverheadHealthBar` 픽스처(`Tools/Tests/전투 EditMode 테스트 실행`) |

`OverheadHealthBar.prefab`(플레이어 바)은 **수정하지 않았다.**

**훅 위치를 MonsterBase 로 고른 이유**: 몬스터 스폰 경로가 여럿이다(`MonsterSpawner`·`MapContentSpawner`·`ZoneMonsterSpawnSet`·
`TutorialMonsterZones`·`BossEncounterDirector` …). 스포너마다 넣으면 빠지는 곳이 생기므로 모든 경로가 거치는 `OnNetworkSpawn` 한 곳에 둔다.
`Unit.OnNetworkSpawn` 이 `HitFlash`·`FloatingDamagePresenter` 를 자동 부착하는 것과 같은 패턴이다.

### 부착 판단 (`DecideInstall`, 순서가 규칙)
1. 이 규칙(`MonsterOverheadHealthBarRule`)이 이미 있음 → 표시 상태만 리셋(풀 재사용)
2. 자식에 `UnitOverheadHealthBar` 가 이미 있음 → 건너뜀 — **바 2개 방지**. 프리팹에 바가 박힌 경우와, 자체 규칙 몬스터(스피너봇)에 전에 붙인 바가 재스폰 때 남아 있는 경우 둘 다
3. `Rank == Boss`(No23 계열 rank 2) 또는 `BossHudTarget` → 건너뜀
4. 몬스터 루트에 다른 `IOverheadHealthBarRule` 이 있음 → **바만** 붙이고 표시·색·라벨은 그 규칙(튜토리얼 스피너봇)
5. 그 외 → 이 규칙 + 바

### 프리팹: Variant 대신 독립 복제를 고른 이유
- `MonsterHealthBar_Normal` = `OverheadHealthBar.prefab` 을 **완전 언팩한 독립 복제**. 플레이어 바는 플레이어 전용으로 계속 바뀐다
  (대시 아이콘·특성 게이지 줄·머리 위 높이·화면 오프셋). Variant 면 이런 변경(새 자식, 크기·위치 튜닝)이 몬스터 바에 조용히 새어 들어오고,
  몬스터 쪽은 발밑 배치라 오프셋 의미부터 다르다. 그래서 끊었다. 플레이어 바의 공통 스타일 변경(배경 스프라이트 등)이 필요하면 손으로 옮긴다.
- 복제하면서 플레이어 전용 `TraitBar`·`DashIcon` 자식과 참조를 지웠다. `NameLabel` 은 튜토리얼 잠금 라벨용으로 남기고 **바 아래**로 옮겼다
  (발밑 바 위에 라벨이 있으면 몬스터 몸을 가린다).
- `MonsterHealthBar_MidBoss` = **Normal 의 Variant**. 몬스터 바끼리는 스타일을 같이 가져가는 게 맞아서다. 오버라이드: 루트 높이 21(일반 14의 1.5배 —
  1차 `midBossHeightScale` 1.5 그대로), `screenOffset.y` -100, 이름. 색은 같은 빨강이지만 각 프리팹 `defaultFillColor` 로 따로 바꿀 수 있다.

## 4. 튜토리얼 스피너봇 (`SpinnerBot_Tutorial`)

**표시 규칙: 기존 튜토리얼 규칙이 우선한다.** `TutorialSpinnerHealthBarRule.ShouldShow` = 살아 있는 동안 항상(시나리오 §3.5 회색·잠금 표시,
Z3-7 "스피너봇 체력 바 활성"). 연습 중(`IsHealthLocked`) 체력이 안 깎여 "피격 → 표시" 규칙으로는 바가 영영 안 켜지므로 일반 전투 규칙을 쓸 수 없다.
- 연습 중: 회색(0.55) + "연습 중 · 체력 잠금" 라벨 / 본 모드: 빨강(0.9, 0.2, 0.2) · 라벨 없음 — 그대로.
- 바: 중간 보스 바(`MonsterHealthBar_MidBoss`, Rank=MidBoss), 발밑 100px.

**프리팹 수정(최소)**: `SpinnerBot_Tutorial.prefab` 에서 Variant 가 추가했던 `OverheadHealthBar` 자식 인스턴스 **1개 제거**.
남겨 두면 판단 2번(자식에 바 있음)으로 몬스터 바가 안 붙고 머리 위 플레이어 바가 그대로 나온다. 제거하고 판단 4번으로 바만 런타임 부착한다.
같은 저장에서 `TutorialSpinnerPractice` 의 `combo*` 7개 필드가 직렬화됐다(코드 기본값과 같은 값 — 동작 변화 없음, 저장 시 Unity 가 채움).

## 5. 수치 (SO) · 데이터 테이블

`MonsterOverheadHealthBarSettings` 에 `[DataTableSheet("Monster", Order = 5)]`. 프리팹 참조 2개는 `[DataTableIgnore]`.

| 필드 | 기본값 | 의미 |
|---|---|---|
| `hideDelaySeconds` | 3 | 전투 종료 후 숨김까지(초), `[Min(0)]` |
| `footScreenOffset` | 50 | 일반 몬스터: 발밑에서 바 중심을 아래로(1080p 기준 픽셀) |
| `midBossFootScreenOffset` | 100 | 중간 보스: 같은 의미 |
| `normalBarPrefab` / `midBossBarPrefab` | 프리팹 | 테이블 제외 |

**발밑 오프셋을 화면 픽셀로 둔 이유와 기본값 근거**
- 카메라: `PlayerFollowCamera.prefab` — Cinemachine `FollowOffset (7, 17, -7)`, FOV 34. 피치 ≈ 59.8°, 거리 ≈ 19.7m, 고정 회전(`CameraTargetSwitcher` 가 고정).
  1080p 기준 화면 1px ≈ 시선 수직 0.011m → **지면에서 카메라 쪽 1m ≈ 78px**, 높이 1m ≈ 45px.
- 화면 아래 = 지면 위 "카메라 쪽(앞)"이다. 월드 오프셋은 카메라 방향을 따로 알아야 하지만 화면 오프셋은 카메라 회전과 무관하게 항상 발 앞에 놓이고,
  바 자체가 거리와 무관한 고정 픽셀 크기라 같은 단위로 맞췄다. 기준 높이는 0(몬스터 위치 = 발).
- 측정(같은 카메라로 렌더해 후보 위치를 그린 캡처 — `C:\UnityProject\_lane-artifacts\MainProject-WorkTree\monster-healthbar-foot\*.png`):
  일반(HumanoidBot)은 발끝이 발 기준점 아래 약 20px까지 → 50px 이면 바(높이 13px) 위 가장자리가 발끝과 ~24px 떨어진다(≈0.6m 앞).
  중간 보스 WallBot 은 몸 아래 가장자리가 기준점 아래 약 60px → 100px 이면 바(19px) 위로 ~30px 여유(≈1.3m 앞).
  GauntletBot·SpinnerBot 은 팔·회전 칼날이 더 멀리 나오지만(캡처는 T-포즈) 몸통 기준으로 잡았다. 바는 오버레이라 가려지지 않는다.
- 플레이어 바 배치 코드(`OverheadHealthBarScreenPlacement`)는 기준 높이 0 + 음수 오프셋으로 그대로 동작해 **고치지 않았다**.

🔴 **GameData.xlsx 는 건드리지 않았다.** 1차 대비 테이블 변경(데이터 담당이 SVN 잠금 후 `Tools/Data/Export Template` 병합):
- 시트 **`Monster`**, 구역 `#■ MonsterOverheadHealthBarSettings`(Order 5), 대상 열 **`MonsterOverheadHealthBarSettings`**
- 행 **삭제**: `midBossHeightScale`, `worldHeightOffset`, `midBossWorldHeightOffset`
- 행 **추가**: `footScreenOffset`, `midBossFootScreenOffset`
- 유지: `hideDelaySeconds`

테이블에 행이 생기기 전까지는 SO 값으로 돈다(data-table.md §3).

## 6. BossHudTarget 부착 확인 (프리팹 YAML · guid)

`BossHudTarget.cs` guid `0ee932d817fa4e58ac747ef1da75f242`, `TwentyThreeBoss.cs` guid `751e09c95b91dfa41b3df99e28850aa7`, `BossWells.cs` guid `952a2e9a9d43c7044b1de7f43ceef404`.

| 프리팹 | TwentyThreeBoss | BossHudTarget | 비고 |
|---|---|---|---|
| `Monster/Boss/TwentyThree.prefab` | ✅ | ✅ | |
| `Monster/Boss/TwentyThree_Solo.prefab` | ✅ | ✅ | |
| `Monster/Boss/TwentyThree_Raid.prefab` | (Variant) | (Variant) | `TwentyThree.prefab` 의 Prefab Variant — 둘 다 상속 |
| `Monster/Boss/Wells.prefab` | — | ❌ | `BossWells`(MonoBehaviour) 만. MonsterBase 가 아니라 훅이 돌지 않는다 — **바 없음이 의도**(§1) |

## 7. Play 에서 확인할 것 (사람)

1. 일반 몬스터: 맞기 전엔 바 없음 → 첫 타에 빨간 바가 **발밑 앞**에 → 발·몸을 가리지 않는지(50px). 터렛(PeekABot·TeslaBot)도.
2. 전투 중 계속 표시, 리쉬로 귀환(`Return`)하거나 타깃을 잃으면 약 3초 뒤 사라지는지. 그 3초 안에 다시 때리면 유지되는지.
3. 처치 순간 바가 즉시 사라지는지(붕괴 사망 연출 중 남지 않는지).
4. 중간 보스(Gauntlet·Spinner·Wall): 바가 더 두꺼운지, 발밑 100px 이 몸에 묻히지 않고 너무 떨어지지도 않는지(특히 SpinnerBot 회전 칼날·GauntletBot 팔).
5. 23호·웰즈 전투에 바가 **안** 생기는지(보스 HUD 만).
6. 튜토리얼 스피너봇: 존3 진입부터 **발밑** 바가 항상 보이는지, 연습 중 회색 + 잠금 라벨(바 **아래**), 본 모드 빨강·라벨 없음, 바가 1개뿐인지.
7. MPPM 클라이언트에서도 같은 타이밍·위치로 켜지고 꺼지는지(복제값만 읽음).
8. 플레이어 머리 위 바(대시 아이콘·특성 줄 포함)가 이전과 같은지. **(3차) 플레이어 바의 기존 빨간 잔상도 그대로인지.**
9. (3차) 일반·중간 보스 피격 시 회색 잔상이 빨간 채움 뒤에 0.4초 머물다 내려오는지. 연타·도트에서 잔상이 시작 체력에 고착되지 않는지.
10. (3차) 첫 타(숨김 → 표시)에 잔상이 보이는지. 전투 종료로 숨겨졌다가 다시 맞아 켜질 때 잔상이 옛 체력에서 튀어 내려오지 않는지.
11. (3차) 튜토리얼 스피너봇: 연습 중 회색 바에 잔상이 없고(피해 0), 본 모드 전환 순간 잔상 없이 빨강으로 바뀌는지, 본 모드 피격 잔상 정상·잠금 라벨 그대로인지.
12. (3차) 처치 순간 잔상까지 바와 함께 즉시 사라지는지.

## 8. 지연 감소(잔상) 바 — 3차

| 항목 | 내용 |
|---|---|
| 로직 | 보스·플레이어 HUD 공용 `DelayedHealthBar`(순수 클래스) **그대로 재사용**. `UnitOverheadHealthBar` 에 `delayed` 필드(직렬화) 추가 → `OnEnable` 에서 `Unit.ClientHpChanged` 구독 + `Bind`, `OnDisable` 해제, `LateUpdate` 에서 `Tick` |
| null 안전 | 잔상 Image(`delayed.delayedFill`)가 비면 구독·Tick 을 아예 안 한다(`DelayedHealthBar.HasFill` 추가). **플레이어 바(`OverheadHealthBar.prefab`)는 비어 있어 동작 변화 없음** — 플레이어 바는 기존 `trailFill`(`OverheadHealthTrail`)을 계속 쓴다. 프리팹 파일도 안 바꿨다 |
| 프리팹 | `MonsterHealthBar_Normal`: `Bar/DelayedFill`(HpFill 복제 — 같은 앵커·크기·스프라이트·Filled Horizontal) 을 맨 뒤(ShieldFill·HpFill 앞 형제)에 추가. 구 `TrailFill`(빨강 0.95,0.35,0.3 — 빨간 채움 뒤라 거의 안 보였다)은 **삭제**하고 `trailFill` 참조 비움 — 잔상 이중 그리기 방지 |
| MidBoss | Normal 의 Variant 라 `DelayedFill`·`delayed` 값을 **상속**한다. `MonsterHealthBar_MidBoss.prefab` 파일은 바뀌지 않았다(오버라이드 = 높이 21·`screenOffset.y` -100·이름 그대로) |
| 분모 | 체력 게이지와 같은 스케일 `max(최대 체력, 체력+실드)`(`OverheadHealthBarLayout.Scale`) — 실드가 붙어도 잔상과 채움 길이 단위가 같다 |
| 스냅 규칙 | `OverheadDelayedBarRules.ShouldSnap` (순수, 테스트 고정): ① 바가 **숨겨지는 순간** 현재 체력으로 스냅 ② 표시 중 게이지 **색이 바뀌면** 스냅. 숨김 중에도 Tick 은 돈다 |

**잔상 기본값 — `BossHealthHUD.prefab` 직렬화 값을 따랐다**(적 쪽 HUD라서):

| 필드 | 몬스터 바 | BossHealthHUD.prefab | CombatHUD.prefab(플레이어) | 코드 기본 |
|---|---|---|---|---|
| `holdSeconds` | 0.4 | 0.4 | 0.4 | 0.4 |
| `drainRatePerSecond` | 0.8 | 0.8 | 0.8 | 0.8 |
| `maxHeldHits` | 5 | 5 | 5 | 5 |
| `resetHoldOnDamage` | **false** | false | true | true |
| 잔상 색 | **(0.509, 0.509, 0.509)** | (0.509, 0.509, 0.509) | (0.275, 0.275, 0.275) | — |

두 HUD 가 `resetHoldOnDamage`·색에서 갈려 **보스 HUD 쪽**을 골랐다(연타 중 잔상이 계속 멈춰 있지 않고 내려온다). 플레이어 HUD 쪽이 맞다면 Normal 프리팹 두 값만 바꾸면 된다.

**왜 "표시 순간"이 아니라 "숨김 순간" 스냅인가**: 일반 몬스터 바는 **피격으로 켜진다.** 표시 순간에 스냅하면 바를 켠 첫 타의 잔상이 지워진다.
그래서 숨김 순간에 끊고, 숨김 중에도 Tick 을 돌려 — 다시 켜질 때는 현재 체력(+그 순간 막 맞은 조각)에서 시작한다. 옛 값에서 튀어 내려오지 않는다.

**튜토리얼 스피너봇(잠금 모드)**: 연습 중엔 `SpinnerBot.TakeDamage` 가 피해를 0 으로 만들어 체력 복제값이 안 바뀐다 → `ClientHpChanged` 가 없고 잔상 = 체력(회색 채움 뒤에 가려짐).
연습 → 본 모드 전환은 규칙의 색이 회색 → 빨강으로 바뀌는 프레임에 스냅되어 이전 모드 잔상이 남지 않는다. 회색/빨강·잠금 라벨은 `TutorialSpinnerHealthBarRule` 그대로(잔상 Image 색은 회색 고정, 채움 색만 규칙이 바꾼다).

**사망**: 즉시 숨김 그대로. 체력 0 복제값에서 `DelayedHealthBar.OnHpChanged` 가 0 으로 Bind 하고, 숨김 순간 스냅도 겹친다.

| 3차 파일 | 내용 |
|---|---|
| `Assets/1.Scripts/UI/Combat/UnitOverheadHealthBar.cs` | `delayed` 필드 + 구독/Tick/스냅 |
| `Assets/1.Scripts/UI/Combat/DelayedHealthBar.cs` | `HasFill` getter 1개만 추가(동작 무변경) |
| `Assets/1.Scripts/UI/Combat/OverheadDelayedBarRules.cs` (새) | 스냅 판단 순수 함수 |
| `Assets/2.Prefabs/UI/MonsterHealthBar_Normal.prefab` | `DelayedFill` 추가 · `TrailFill` 삭제 · `delayed` 값 |
| `Assets/1.Scripts/UI/Combat/Editor/MonsterOverheadHealthBarDelayedTests.cs` (새) | 프리팹 연결·보스 HUD 값/색 일치·플레이어 바 null·스냅·스피너 잠금 — `^MonsterOverheadHealthBar` 로 전투 러너에 포함 |

검증(3차, Play 없이): 컴파일 에러 0 · EditMode 전체 통과 1646 / 실패 0 / 건너뜀 20 · 전투 러너 218/0 · 튜토리얼 러너 376/0 · 새 픽스처 12/0.
Play 확인은 §7 의 9~12 (사람).

## 9. 중간 보스 바 굵은 빗금 — 4차

2026-10-16 · 사용자가 직접 고친 Normal·MidBoss 프리팹(9ddff5d0) 위에 이어서 작업. **코드 변경 없음**, 플레이어 바·`MonsterHealthBar_Normal.prefab` 무수정.

| 항목 | 내용 |
|---|---|
| 텍스처 | `Assets/2.Prefabs/UI/Textures/MonsterHealthBarHatch.png` (git 추적, `.meta` 포함 — 50.Art 는 SVN 이라 피했다). 64×64, 45° 대각(`\`) 흰 줄 + 나머지 완전 투명(RGB 는 전부 흰색, 알파만 줄). 대각 주기 32px(64 안에 2주기 → 이음매 없음), 줄 굵기 = 주기의 절반, 가장자리 ~1px 안티앨리어싱. 에디터 C# 로 생성(Texture2D → EncodeToPNG) |
| 임포트 | Sprite(Single) · PPU 100 · Mesh Type **Full Rect** · Wrap **Repeat** · sRGB · Alpha Is Transparency · mipmap 끔 · Bilinear · 무압축 |
| 구조 | `Bar/HpFill` 에 **`Mask`(showMaskGraphic = true)** 추가 + 자식 **`Hatch`**(Image, Tiled, 앵커 전체 늘림·sizeDelta 0, raycastTarget 끔). 둘 다 MidBoss Variant 의 추가 오버라이드 |
| 왜 Mask(코드 없음) | Filled 이미지는 fillAmount 만큼만 메쉬를 만들고, Mask 는 그 메쉬를 스텐실로 쓴다 → 빗금이 **체력 채움 모양을 그대로 따라 줄어든다.** `UnitOverheadHealthBar` 는 HpFill 의 `fillAmount`·`color` 만 바꾸고(활성은 안 건드림) `DelayedHealthBar` 는 `DelayedFill` 만 만지므로 오버레이가 깨지지 않는다. `DelayedFill`·`ShieldFill` 은 HpFill 의 형제(뒤)라 빗금이 깔리지 않는다. RectMask2D 는 사각 영역 클리핑이라 fillAmount 를 따라가려면 코드가 필요해 고르지 않았다 |
| 타일 원점 | Tiled 는 rect 왼쪽 아래 기준이라 체력이 줄어도 줄무늬가 제자리에 있고 오른쪽 끝만 잘린다(흐르지 않음) |

**조정 (MidBoss 프리팹 `Bar/HpFill/Hatch` 의 Image)**
- 색·불투명도: `Color` — 기본 **검정 알파 0.25**. 빨강(0.9,0.2,0.2) 위에서 R 230 → 203 정도로 어둡게 깔린다.
- 줄 간격: `Pixels Per Unit Multiplier` — 기본 **1.6**. 타일 = 64 / 값 (Canvas 단위), 가로 방향 한 주기 = 32 / 값 = **20** ≈ 바 높이(21). 값을 줄이면 굵고 넓게, 키우면 촘촘하게.
- 빗금을 끄려면 `Hatch` 오브젝트를 비활성화(Mask 는 showMaskGraphic 이라 채움 표시엔 영향 없음).

**튜토리얼 스피너봇 회색 바**: 회색(0.55) 위에서도 같은 알파 0.25 로 대비가 비슷하게 나와 따로 조정하지 않았다(캡처 비교).

**캡처**(Play 없이 프리뷰 씬 오프스크린 렌더, ×4 와 실제 크기 ×0.9): `C:\UnityProject\_lane-artifacts\MainProject-WorkTree\monster-healthbar-hatch\`
`red_fill1.0_x4.png` · `red_fill0.5_delayed0.7_x4.png`(잔상 회색 구간엔 빗금 없음) · `gray_fill1.0_x4.png` · `*_x1.png`.

| 4차 파일 | 내용 |
|---|---|
| `Assets/2.Prefabs/UI/Textures/MonsterHealthBarHatch.png` (+ `.meta`, 폴더 `.meta`) (새) | 빗금 텍스처 |
| `Assets/2.Prefabs/UI/MonsterHealthBar_MidBoss.prefab` | HpFill 에 Mask + `Hatch` 자식 |

**Play 확인 (사람)**
13. 중간 보스(Gauntlet·Spinner·Wall) 바에 빗금이 보이고, 맞을 때 빗금이 빨간 채움과 **같이** 줄어드는지. 잔상(회색)·실드 구간엔 빗금이 없는지.
14. 체력 0 근처(채움 몇 px)·0 에서 빗금이 삐져나오지 않는지.
15. 튜토리얼 스피너봇: 연습 중 회색 바·본 모드 빨강 바 모두 빗금이 어색하지 않은지, 잠금 라벨이 가려지지 않는지.
16. 일반 몬스터 바에는 빗금이 **없는지**, 플레이어 머리 위 바가 그대로인지.
17. 화면 해상도(1080p 외)에서 줄무늬가 깨지거나 모아레가 생기지 않는지(mipmap 없음 + Bilinear).
