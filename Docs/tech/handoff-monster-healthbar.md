# 핸드오프 — 일반 몬스터·중간 보스 머리 위 체력바

> 2026-10-15 · 브랜치 `feature/MonsterHealthBar` · 작성: Claude(claude-alt).
> 몬스터는 경석 담당 영역이라 **프리팹은 하나도 고치지 않았고**, 코드 훅 1줄로 런타임에 붙인다.
> Play 확인은 아직 안 했다(아래 §6 체크리스트).

## 1. 사양 (사용자 확정)

| 항목 | 동작 |
|---|---|
| 위치 | 머리 위. 플레이어와 같은 `UnitOverheadHealthBar`·`OverheadHealthBar.prefab` 을 그대로 쓴다. 높이만 데이터 |
| 표시 | 처음엔 숨김 → **체력이 줄면 켜짐** → **전투 상태가 끝난 뒤 3초**(데이터) 후 숨김 |
| 재피격 | 다시 맞으면 숨김 타이머가 처음부터. 전투 상태 동안은 계속 유지 |
| 사망 | `Dead` 상태 또는 체력 0 이하면 **즉시** 숨김 |
| 중간 보스 | 같은 바, 높이(두께) × `midBossHeightScale`(기본 1.5) |
| 최종 보스 | 제외 — 보스 HUD(`BossHealthHUD`)가 따로 있다 |
| 멀티 | 각 피어가 복제값(`Unit._currentHp`, `MonsterBase._state`)만 읽어 로컬 판단. RPC·서버 로직·NetworkVariable 추가 없음 |
| 색 | 빨강(0.9, 0.2, 0.2) — 튜토리얼 스피너봇 본 모드와 같은 색. 라벨 없음 |

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
| `Assets/1.Scripts/Monster/MonsterBase.cs` | **`OnNetworkSpawn` 끝에 1줄** `MonsterOverheadHealthBarRule.TryInstall(this)`. FSM·상태 로직은 그대로. 모든 피어에서 돈다 |
| `Assets/1.Scripts/UI/Combat/UnitOverheadHealthBar.cs` | `ConfigurePlacement(worldHeight, heightScale)` 추가 — 런타임에 붙인 바의 높이 오프셋·두께 배율을 Awake 뒤에 바꾼다. 플레이어 동작 변화 없음 |
| `Assets/1.Scripts/UI/Combat/MonsterOverheadHealthBarRule.cs` (새) | `IOverheadHealthBarRule` 구현 + `TryInstall`(제외 판정 → 규칙 AddComponent → 바 프리팹을 몬스터 자식으로 Instantiate) |
| `Assets/1.Scripts/UI/Combat/MonsterOverheadHealthBarVisibility.cs` (새) | 표시 판단 순수 로직(시간은 인자). `IsCombatState`·`HeightScale`·`ShouldAttach` 포함 |
| `Assets/1.Scripts/UI/Combat/MonsterOverheadHealthBarSettings.cs` (새) | 수치 SO |
| `Assets/Resources/MonsterOverheadHealthBarSettings.asset` (새) | SO 에셋. `Resources.Load` 로 읽는다(씬 참조 없이 런타임 부착하려고) |
| `Assets/1.Scripts/UI/Combat/Editor/MonsterOverheadHealthBarVisibilityTests.cs` (새) | EditMode 테스트 |
| `Assets/1.Scripts/Editor/CombatEditModeTestRunner.cs` | `^MonsterOverheadHealthBar` 픽스처 추가(`Tools/Tests/전투 EditMode 테스트 실행`) |

**훅 위치를 MonsterBase 로 고른 이유**: 몬스터 스폰 경로가 여럿이다(`MonsterSpawner`·`MapContentSpawner`·`ZoneMonsterSpawnSet`·
`TutorialMonsterZones`·`BossEncounterDirector` …). 스포너마다 넣으면 빠지는 곳이 생기므로 모든 경로가 거치는 `OnNetworkSpawn` 한 곳에 둔다.
`Unit.OnNetworkSpawn` 이 `HitFlash`·`FloatingDamagePresenter` 를 자동 부착하는 것과 같은 패턴이다. 풀 재사용으로 다시 스폰되면 `TryInstall` 은 표시 상태만 초기화한다(멱등).

**프리팹 변경: 없음.**

### 제외 규칙 (`ShouldAttach`)
- `Rank == Boss` (No23·No23_Solo·No23_Raid 데이터 = rank 2) → 제외
- `BossHudTarget` 부착 → 제외
- 이미 `UnitOverheadHealthBar` 가 자식에 있음 → 제외(`SpinnerBot_Tutorial` 은 자체 바 + `TutorialSpinnerHealthBarRule` 유지)

## 4. BossHudTarget 부착 확인 (프리팹 YAML · guid)

`BossHudTarget.cs` guid `0ee932d817fa4e58ac747ef1da75f242`, `TwentyThreeBoss.cs` guid `751e09c95b91dfa41b3df99e28850aa7`, `BossWells.cs` guid `952a2e9a9d43c7044b1de7f43ceef404`.

| 프리팹 | TwentyThreeBoss | BossHudTarget | 비고 |
|---|---|---|---|
| `Monster/Boss/TwentyThree.prefab` | ✅ (1892행) | ✅ (1976행) | |
| `Monster/Boss/TwentyThree_Solo.prefab` | ✅ (570행) | ✅ (650행) | |
| `Monster/Boss/TwentyThree_Raid.prefab` | (Variant) | (Variant) | `TwentyThree.prefab`(guid f3f87bba…) 의 Prefab Variant — 둘 다 상속 |
| `Monster/Boss/Wells.prefab` | — | ❌ | `BossWells`(MonoBehaviour) 만 있다. **Unit/MonsterBase 가 아니라** 훅이 돌지 않아 바가 붙지 않는다 |

→ 23호는 `BossHudTarget` + `Rank=Boss` 둘 다로 제외, 웰즈는 MonsterBase 가 아니라 대상 자체가 아니다.

## 5. 수치 (SO) · 데이터 테이블

`MonsterOverheadHealthBarSettings` 에 `[DataTableSheet("Monster", Order = 5)]`. 비주얼 참조(`barPrefab`)·색(`fillColor`)은 `[DataTableIgnore]`.

| 필드 | 기본값 | 의미 |
|---|---|---|
| `hideDelaySeconds` | 3 | 전투 종료 후 숨김까지(초), `[Min(0)]` |
| `midBossHeightScale` | 1.5 | 중간 보스 바 두께 배율, `[Min(1)]` |
| `worldHeightOffset` | 2.2 | 일반 몬스터 바 기준점 높이(m) |
| `midBossWorldHeightOffset` | 3 | 중간 보스 바 기준점 높이(m) |

높이 기본값은 프리팹 렌더러 높이 측정값(일반 2.1~2.75m, WallBot 3.3m, GauntletBot 5.7m — 팔 포함, SpinnerBot 2.1m)에서 잡았다. 화면 오프셋(위로 80px)은 플레이어 바와 같다.

🔴 **GameData.xlsx 는 건드리지 않았다.** 테이블에 추가할 것(데이터 담당이 SVN 잠금 후 `Tools/Data/Export Template` 병합):
- 시트 **`Monster`**, 구역 `#■ MonsterOverheadHealthBarSettings`(Order 5 — TurretHeadAim 다음)
- 대상 열 **`MonsterOverheadHealthBarSettings`**
- 행: `hideDelaySeconds`, `midBossHeightScale`, `worldHeightOffset`, `midBossWorldHeightOffset`

테이블에 행이 생기기 전까지는 SO 값으로 돈다(data-table.md §3).

## 6. Play 에서 확인할 것 (사람)

1. 일반 몬스터: 맞기 전엔 바 없음 → 첫 타에 빨간 바 → 머리 위 높이 적당한지(특히 PeekABot·TeslaBot 처럼 높은 터렛).
2. 전투 중 계속 표시, 리쉬로 귀환(`Return`)하거나 타깃을 잃으면 약 3초 뒤 사라지는지. 그 3초 안에 다시 때리면 유지되는지.
3. 처치 순간 바가 즉시 사라지는지(붕괴 사망 연출 중 남지 않는지).
4. 중간 보스(Gauntlet·Spinner·Wall): 바가 더 두꺼운지, 높이(특히 GauntletBot 3m 오프셋이 몸에 묻히지 않는지).
5. 23호·웰즈 전투에 머리 위 바가 **안** 생기는지(보스 HUD 만).
6. 튜토리얼 스피너봇: 기존 회색/잠금 바 그대로, 바가 2개 겹치지 않는지.
7. MPPM 클라이언트에서도 같은 타이밍으로 켜지고 꺼지는지(복제값만 읽음).
