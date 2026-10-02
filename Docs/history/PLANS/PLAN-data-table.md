# PLAN — 데이터 테이블 파이프라인 xlsx → ScriptableObject (2026-10-01 초안 · 10-02 데이터 출처 모델 확정 · 🔴 승인 대기)

작성: 은희(Claude) / 브랜치(예정): `feature/DataTable`(development 에서 분기)
관련: [architecture.md](../../../Docs/tech/architecture.md) · [player-prefabs.md](../../../Docs/tech/player-prefabs.md)

> **목표 한 줄**: 기획이 게임의 모든 기획 수치를 **xlsx 하나에서** 고치고, 버튼 한 번으로 게임에 반영한다.
> ✅ 툴·Player 쪽 = 은희 영역. 🔴 몬스터·보스 테이블 연결과 보스 코드의 수치 이전은 **경석 합의 후**(§7 D4).
> 🔴 = 미확정 — §8 질문에 답이 나오면 이 문서를 고치고 승인받는다.

---

## 1. Goal

```
                 디스크의 SO = 개발자 작업대 (인스펙터에서 자유롭게 고친다)
                       │
 [데이터: 인스펙터] Play ── SO·프리팹 값 그대로
 [데이터: 테이블]  Play ── Play 시작 때 xlsx 값을 메모리에서만 덮어씀 → Play 종료 때 복구
 빌드                  ── 항상 xlsx 값
```

- **기획 값의 원본 = xlsx**, **개발자 값의 원본 = 디스크 SO**. 둘은 의도적으로 다를 수 있다.
- **빌드는 항상 xlsx 값이다.** 테이블 플레이 = 빌드와 같은 수치로 하는 에디터 테스트.
- 기획은 xlsx 만 SVN 에 커밋한다. **임포트 결과를 git 에 커밋할 필요가 없다**(테이블 플레이·빌드가 매번 xlsx 를 읽는다).
- 런타임은 지금처럼 SO 를 읽는다. **xlsx 파싱 코드는 빌드에 들어가지 않는다**(Editor 전용).

> 🔴 **2026-10-02 결정 변경**: 초안의 "xlsx 가 SO 의 유일한 원본, 인스펙터 수정은 덮어써진다" 를 폐기했다.
> 개발 중 인스펙터 실험과 기획 수치 확인을 둘 다 살리려고 **플레이 시작 옵션(데이터 출처)** 으로 나눈다.

## 2. 현재 코드 사실 (2026-10-01 조사)

| 영역 | 사실 | 계획 영향 |
|------|------|-----------|
| CSV/테이블 | 레포에 **없음**(`Assets/` 코드·`Docs/` 검색 0건) | 새로 만든다 |
| 외부 라이브러리 | `manifest.json` 에 Excel·JSON 패키지 없음, `Plugins` DLL 없음 | §4-1 자체 리더로 의존성 0 |
| 이미 SO 인 수치 | `MonsterDataSO`(약 30필드, 에셋 10개) · `PlayerDashData` · `PlayerGameRuleData` · `DefaultAttackData` · `Gunner*Data` · `FirstMelee*SkillData`(`PlayerSkillData` 파생) · `MapGenConfigSO` | **바로 테이블에 연결 가능** |
| **SO 가 아닌 수치** | `1.Scripts` 에서 MonoBehaviour `[SerializeField] float/int` **약 60파일·159필드**. 예: `Player.cs`(8) · `PlayerMovement`(6) · `DefaultAttackController`(9) · `Enemy`(6) · `ChargeController`(4) · `JumpController`(2) · `LinearKnockback`(4) | 🔴 **프리팹에 박혀 있어 테이블이 못 닿는다.** SO 로 옮기는 이전이 작업량의 대부분(§3 D3·D4) |
| Variant 오버라이드 | 플레이어 = `Player.prefab` base + `Player_Paladin`/`Player_Gunner` Variant. 인라인 수치가 Variant 에서 오버라이드됐을 수 있다 | 이전할 때 **base 값이 아니라 Variant 의 실제 값**을 옮겨야 한다 |
| 값 읽는 시점 | `MonsterBase.cs:200` 이 스폰 때 `Initialize(data.attackDamage, …)` 로 **스냅샷** | 테이블 Play 는 진입 때 한 번 적용 → Play 중 xlsx 수정은 반영 안 됨. 즉시 반영은 범위 밖(§6) |
| 테스트 | EditMode 테스트 폴더·asmdef 관례 있음(`Assets/Tests/EditMode/*`) | 파서·검증을 순수 클래스로 빼서 EditMode 테스트 |
| Play 진입 경로 | 기본 Play · Dev Boot 버튼(`DevBootLauncher.Launch`) · Dev_Boot 씬에서 직접 Play(`PrepareDirectDevBootIfNeeded`) · MPPM 가상 플레이어 | 모드를 버튼에 붙이면 경로마다 빠뜨린다 → **적용 지점 1곳**(§4-7) |
| 툴바 | Unity 6.3 `MainToolbarElement` 사용 중. 10-02 Dev Boot 를 `▶ Dev Boot` 버튼 + 씬 드롭다운으로 분리([DevBootToolbar.cs](../../../Assets/1.Scripts/Dev/Editor/DevBootToolbar.cs), 🔴 에디터 확인 대기) | 데이터 출처 드롭다운을 같은 방식으로 옆에 둔다 |
| 개인 설정 저장 | `DevBootTarget` = **워크트리별 EditorPrefs**(키 = `Application.dataPath` 해시) | 데이터 출처도 같은 저장소. 🔴 MPPM 클론은 경로가 달라 키가 어긋날 수 있다(R7) |
| Play 후 복구 패턴 | `DevBootLauncher` 가 빌드 씬 목록을 `SessionState` 스냅샷 → `EnteredEditMode` 에서 복원 | SO 메모리 복구에 같은 패턴 |

## 3. 접근 — 단계별 (각 단계 = 커밋 1개 이상, `dotnet build` 통과 후)

### D1. 테이블 적용기 코어 (툴만, 게임 코드 변경 0) — ✅ 2026-10-02 구현 · EditMode 34건 통과(메뉴 `Tools/Tests/데이터 테이블 EditMode 테스트 실행`)

> 구현 파일: `Assets/1.Scripts/DataTable/Editor/` — `XlsxReader` · `DataTableSchema` · `DataTableApplier`(Bind·메모리/디스크 적용·Restore·Diff) · `DataTableMenu`(`DataTableSource.Load` = 폴더 읽기→검증→연결 단일 진입점).
> 테스트: `Assets/Tests/EditMode/DataTable/Editor/` — 리더·스키마·적용기. xlsx 픽스처는 바이너리 대신 코드로 만든다(`TestXlsx`).
- `Assets/1.Scripts/DataTable/Editor/` (Editor 전용 asmdef)
  - `XlsxReader` — xlsx(zip+XML)를 `System.IO.Compression` + `XmlReader` 로 읽는다. 시트 → `string[,]`.
    수식 셀은 **Excel 이 저장해 둔 결과값**을 읽는다. 파일은 `FileShare.ReadWrite` 로 열어 **Excel 이 열고 있어도 읽힌다.**
  - `TableSchema` — 시트 형식 해석(§4-2). 순수 클래스.
  - `TableValidator` — 오류 수집(§4-4). 순수 클래스.
  - `TableApplier` — `SerializedObject` 로 SO 필드에 쓴다(private `[SerializeField]` 도 됨). 두 가지 쓰기:
    - **메모리 적용**(테이블 플레이용): 적용 전 값을 스냅샷 → 덮어씀. **`SetDirty`·저장 안 함** → 디스크 변경 0. `Restore()` 로 되돌린다.
    - **디스크 적용**(빌드용·수동 동기화용): Undo·Dirty·저장.
  - 메뉴:
    - `Tools/Data/Verify` — xlsx 와 디스크 SO 의 **다른 필드 목록**(= 개발자 값 ≠ 기획 값). 쓰지 않는다.
    - `Tools/Data/SO 를 테이블 값으로 덮어쓰기` — 개발자가 작업대를 기획 값으로 리셋하고 싶을 때만. 확인 대화상자.
- **오류가 하나라도 있으면 아무 SO 도 쓰지 않는다**(전부 성공 또는 전부 실패). 테이블 플레이라면 **Play 진입을 막고** 오류 목록을 띄운다.
- EditMode 테스트: 셀 타입 변환·빈 셀·주석 열·중복 Id·없는 필드·전부-또는-0 쓰기·메모리 적용 후 `Restore` 하면 원래 값.

### D1.5. 데이터 출처 드롭다운 (플레이 시작 옵션) — ✅ 2026-10-02 구현 · EditMode 37건 · ✅ 은희 Play·MPPM 확인(10-02)

> 구현: `DataSourcePlayMode`(적용 지점 = `ExitingEditMode` — 에디터 이벤트라 런타임 코드 없이 모든 Play 경로·MPPM 클론이 지난다. 계획의 `BeforeSceneLoad` 대신) ·
> `DataSourceToolbar`(드롭다운, 모드별 색: 테이블 피치 · 인스펙터 연두) · `DataSourceBadge`(화면 표시, 에디터 전용 런타임 클래스).
> **xlsx 가 하나도 없으면 막지 않고 SO 값으로 돈다**(표시 "TABLE (xlsx 없음)") — 테이블이 SVN 에 올라가기 전 팀원 Play 를 막지 않으려고.
> R7: `DevBootTarget.ComputeWorkspaceKey` 가 클론 경로(`…/Library/VP/mppm…/Assets`)를 메인 경로로 접는다(테스트 3건).
- 툴바 `[ 데이터: 테이블 ▾ ]` — `테이블(xlsx = 빌드와 같음)` / `인스펙터(SO·프리팹 값 그대로)`. Dev Boot 버튼·씬 드롭다운 옆.
- 선택값 = 워크트리별 EditorPrefs(`DevBootTarget` 과 같은 저장소). 기본값 = **테이블**(빌드와 같은 조건이 기본이 안전하다).
- **적용 지점은 1곳**: `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`(`#if UNITY_EDITOR`) — 어느 Play 경로든 첫 씬 로드 전에 메모리 적용.
  복구는 `EnteredEditMode` 에서(`DevBootLauncher` 와 같은 패턴).
- 게임 화면 구석에 `DATA: TABLE` / `DATA: INSPECTOR` 상시 표시(에디터 전용). Dev Boot 버튼 툴팁에도 현재 데이터 출처를 적는다.
- 테이블 플레이 중 테이블 관리 SO 를 인스펙터에서 고치려 하면 경고(R8).

### D1.6. 빌드 = 항상 테이블 — ✅ 2026-10-02 구현 · EditMode 50건 · ✅ 은희 실제 빌드 확인(10-02)

> 구현: `DataTableBuild` — `BuildPlayerWindow.RegisterBuildPlayerHandler` 로 **Build 버튼 자체를 감싼다**(전용 메뉴 대신: 팀이 Build Profiles 창으로 빌드하므로 메뉴를 따로 두면 안 거치는 빌드가 생긴다).
> 빌드 전 **디스크 적용**(R9 — 빌드가 메모리/디스크 중 무엇을 읽든 테이블 값) → `try/finally` 로 **빌드 실패해도 원복**. 원복 파일이 원본과 바이트 동일함을 테스트로 고정.
> xlsx 없음·테이블 오류 = `BuildFailedException`(개발자 값으로 빌드되는 것보다 낫다). R9 해소.
- 빌드 전 디스크 적용 → 빌드 → 빌드 후 원래 값으로 복구. 🔴 빌드 파이프라인이 메모리 값을 쓰는지 디스크 값을 쓰는지 확인 후 방식 확정(R9).
- 안전한 쪽: `IPreprocessBuildWithReport`/`IPostprocessBuildWithReport` 대신 **`Tools/Build/Build (Table)` 전용 메뉴**로 적용·빌드·복구를 한 함수에서 `try/finally` 로 묶는다.
- 테이블 오류가 있으면 빌드 중단.

### D2. 이미 SO 인 Player 쪽 수치 연결 (은희 영역) — ✅ 2026-10-02 Export Template · 시트 12 · 필드 149 · Verify 차이 0

> 구현: `[DataTableSheet]`(내보낼 SO 표시) · `[DataTableIgnore]`(기술 값) — 어트리뷰트 전용 asmdef `MainProject.DataTable.Attributes`(Dash asmdef 가 참조).
> `XlsxWriter`(의존성 0) · `DataTableTemplate`(정수·실수만, 구조체·배열 원소 포함, 에셋 1개 = 키-값 시트 / 여럿 = 행 테이블).
> 표시 붙인 SO 12종: `PlayerDashData` · `DefaultAttackData` · `Gunner{Heat,BasicAttack,ChargeLaser,CoolBackstep,Interrupt,TrackingLaser}Data` · `FirstMelee{Interrupt,Main,Sub,Ultimate}SkillData`.
> 기술 값 제외: 스냅샷 크기·허용오차(서버 검증) · `maxHitResults`(판정 버퍼) · `muzzleHeight`(연출 정렬). `PlayerGameRuleData`(물리 규칙)는 표시 안 함.
> 검증: **실제 Excel 이 경고 없이 열고, Excel 로 다시 저장한 파일도 Verify 차이 0**(공유 문자열·테마 등 Excel 고유 형식 읽힘). EditMode 49건.
> 🔴 `GameData.xlsx` 는 SVN 폴더에 생성만 됨 — **SVN add·커밋은 은희 판단**(git 에는 안 들어감).
- `PlayerDashData` · `PlayerGameRuleData` · `DefaultAttackData` · `Gunner*Data` · `FirstMelee*SkillData`.
- **첫 xlsx 는 현재 SO 값을 내보내서 만든다**(`Tools/Data/Export Template`) → 직후 `Verify` 결과 = **차이 0** 이 기준선.

### D3·D4. 프리팹 인라인 수치 — **옮기지 않고 테이블이 프리팹을 직접 덮어쓴다** (✅ 2026-10-02 구현 · EditMode 54건 · ✅ 은희 Export → Verify 0)

> 🔴 **2026-10-02 설계 변경(은희)**: 초안의 "프리팹 값을 SO 로 옮기고 인라인 필드 삭제" 를 폐기했다.
> 원칙 = **인스펙터 모드는 SO·프리팹 인스펙터 값만, 테이블 모드·빌드는 xlsx 만.** 프리팹 값을 SO 로 옮기면 인스펙터 모드에서 프리팹 값이 사라진다.
> 그래서 테이블 대상에 **프리팹 안의 컴포넌트**를 더했다 — SO 와 똑같이 메모리 적용(테이블 Play)·디스크 적용 후 파일째 원복(빌드).

**구조**
- 시트 이름 = 컴포넌트 타입 이름(예: `GauntletBot`), **Id = 그 컴포넌트를 가진 프리팹 파일 이름**(Variant 도 각자 행 — `Player_Paladin`·`Player_Gunner`).
- 프리팹 안(자식 포함)에 그 타입이 **정확히 하나**여야 한다(둘 이상이면 오류). 조회는 Id 로 프리팹 파일을 찾아 그 프리팹만 연다.
- 템플릿·미리보기에서 **레거시 폴더·SVN 아트 폴더(`Assets/50.Art/` — 검수용 `*_Review` 프리팹) 제외**. `NetworkVariable` 안쪽(네트워크 상태)은 자동 제외.
- 빌드 원복 = 값 되쓰기가 아니라 **파일 백업 복사**(Variant 오버라이드 목록까지 바이트 동일). 백업 목록은 `Library/DataTableBuildBackup` — 빌드 중 크래시면 다음 에디터 시작 때 자동 원복.
- `Tools/Data/Export Template 필드 목록 보기 (파일 안 씀)` — 시트별로 나갈 필드를 Console 에. 기술 값이 섞였는지 점검용.

**표시한 타입**(`[DataTableSheet]`) · 나가는 필드(미리보기 실측)

| 영역 | 시트 | 대상 | 나가는 필드 |
|---|---|---|---|
| Player | `Player` | Player · Player_Paladin · Player_Gunner · Paladin_VFX | 인터럽트 2 · 공격력·공속·HP·방어 |
| Player | `PlayerMovement` | 위 4 | maxSpeed · midSpeed · acceleration |
| Player | `FirstMeleePassive` | Player_Paladin · Paladin_VFX | 쿨·적중 감소·추가 피해 배율/고정·회복% |
| Player | `PlayerFallRecovery` · `PlayerLandingProtection` · `PlayerSoulController` | 위 4 | 낙하 입력잠금·무적 · 착지 보호 · 영혼 속도 |
| Player | `PlayerGameRuleData`(SO) | 1 | **fallDamageRatio 만**(물리 값은 제외) |
| 몬스터 | `MonsterDataSO`(SO) | 10 | 기본 수치 26 |
| 몬스터 | `BossDataSO`(SO) | No23 · No23_Solo | 239 — 🔴 기술 값 판단은 경석 |
| 몬스터 | `MonsterMeleeAttack` | 10 | knockbackStrength |
| 몬스터 | `LinearKnockback` | 11(몬스터 8 + 더미 3) | maxDistance |
| 몬스터 | `MonsterCounterWindow` | TwentyThree · 중간보스 3 | windowDuration · groggyDuration |
| 몬스터 | `TurretHeadAim` | PeekABot · TeslaBot | 회전속도 · 최대 각 · 예고 · 조준 유지 |
| 몬스터 | `GauntletBot` · `SpinnerBot` · `WallBot` | 각 1 | 패턴 수치 10 · 8 · 10 |

**기술·애매 값 `[DataTableIgnore]`**: 판정 버퍼(`maxHitCount`·`maxDetectionResults`·`maxNearbyResults`·`smash/shockMaxHitCount`) · 연출 정렬(`sideLateralOffset`·`laserWidth`) ·
네트워크(`yawSendThreshold`·`replicationSmoothing`) · 조작감·카메라(`rotate_Speed`·`alignThreshold`·`viewYaw`·`landedFollowCameraDelay`) ·
물리(`PlayerGameRuleData` 경사·턱·낙하속도·넉백 4, `LinearKnockback` min/max 시간·정지속도) · 애니 타이밍(`DefaultAttackStep` motionDuration·trackRotationSpeed·loopBackEntryTime,
`BossDataSO` hitEventFallbackNormalized·telegraphPoseNormalized) · 소유자가 덮어쓰는 값(`BaseAttack.damage`) · 기타(`MonsterDataSO` avoidanceRadius·attackWindup(미사용)·despawnDelay,
`platformGroundCheckDistance`·`fallReturnDelay`·`airborneFailsafeSeconds`·`blinkInterval`·`additionalHitScale`, `TurretHeadAim` 복귀속도·재조준 지연).

**같이 한 정리**
- **Q7** `Player.moveSpeed` 삭제 — 이동 원본 = `PlayerMovement.maxSpeed`, `Unit` 이동속도 스탯도 이 값(플레이어 쪽 소비자 0 확인, 10 → 5 로 바뀌지만 읽는 곳 없음).
- `fallDamageRatio` 씬별 `FallBoundarySettings` → `PlayerGameRuleData`(네 씬 모두 0.25). 씬에는 경계 높이만.
- 🐞 `PlayerMovement.Start()` 의 `rotate_Speed = 10` 덮어쓰기 제거(프리팹 값 전부 10 — 동작 동일).

**안 한 것**
- `DefaultAttackProjectile.lifetime`(P5) — 어떤 프리팹·씬도 이 컴포넌트를 안 쓴다(참조 0).
- ~~`Temp_MultiGameRule.defaultLifeCount`~~ → 은희가 `GameRule.prefab` 으로 분리(Q10) → 시트 추가.
- **M6**(23호 폭탄·장판·송전기) — Q9, 경석 장판 작업 후.
- ~~레거시 `Enemy/*`~~ → ✅ 10-02 은희 결정으로 삭제(죽은 스크립트 11 · `AttackTriggerRelay` · `ModularRobots_R1` 프리팹·네트워크 등록). 현역 4개는 이동: `GrabController`·`TwentyThreeArenaContext`·`ChargingObject` → `Monster/Boss/`, `FloorAreaEffect` → `Effects/`(GUID 유지).

**제약**
- **씬에 배치된 프리팹 인스턴스가 그 필드를 덮어쓰고 있으면** 테이블 값이 안 닿는다(플레이어·몬스터·보스는 런타임 스폰이라 해당 없음).
- 메모리의 프리팹 에셋 값은 런타임 `Instantiate`·네트워크 스폰 사본에 들어간다 — 🔴 실제 테이블 Play 로 확인 필요(§10).

### D6. 운영 보강 (2026-10-02 은희 지시 — 1·2·3·4·5) — ✅ 전부 구현 · EditMode 64건

| # | 기능 | 방식 |
|---|---|---|
| 1 | **병합 Export** | 파일이 있으면 [병합 / 덮어쓰기 / 취소]. 병합 = **기존 칸 값 그대로**, 없는 시트·열·행만 현재 인스펙터 값으로 추가. 코드에서 사라진 필드·대상은 지우지 않고 `#` 를 붙여 메모로(가져오기 오류 방지). 쓰기 전 원본을 `Library/DataTableExportBackup` 에 복사. 🔴 xlsx 를 새로 쓰므로 **서식·수식·열 너비는 사라진다**(수식은 결과값으로) |
| 2 | **범위 검증** | 필드의 `[Range]`·`[Min]` 을 읽어 벗어나면 오류(테이블 Play·빌드 중단). 배열 필드의 범위는 원소마다 |
| 3 | **기획 가이드** | `Docs/tech/data-table.md` — 시트 형식·메모·잠금·모드·Verify·자주 나는 오류 |
| 4 | **씬 오버라이드 경고** | Verify 가 씬(`.unity`) 의 프리팹 인스턴스가 테이블 필드를 오버라이드하는지 YAML 로 찾아 경고(그 인스턴스엔 테이블 값이 안 닿는다) |
| 5 | **인스펙터 표시** | 선택한 SO·프리팹(·인스턴스)이 테이블 대상이면 인스펙터 머리에 "데이터 테이블 관리 필드 N개, 테이블과 다른 값" 박스. xlsx 바뀔 때만 다시 읽는다 |

### D7. 시트 개편 — 사람이 보고 고치기 쉽게 (2026-10-02 은희 승인 · ✅ 구현)

**문제**: `BossDataSO` 2행 × 261열(가로 스크롤), `MonsterDataSO` 10 × 30, 1~2필드짜리 작은 시트 9장. 19장.

**결정**
| # | 항목 | 결정 |
|---|---|---|
| 1 | 형식 | **세로 표** — 행 = 필드, 열 = 대상. 가로는 대상 수만큼(최대 11, 보스 2) |
| 2 | 열 순서 | `필드 | #설명 | 대상…` — **설명은 필드 바로 옆**(은희) |
| 3 | 구역 | 타입마다 `#■ 타입` 제목 + 자기 머리글 행(`필드 | #설명 | 대상…`). 코드 `[Header]` = `#  ─ 제목` 구역 행. 구조체 배열 원소 = `#  ▸ attacks[3] · JumpAttack`(원소 첫 enum/string 필드로 이름표) |
| 4 | 카테고리 | **6장**: `Player`(스탯·이동·대시·낙하·착지·영혼·낙하 비율·목숨) · `Paladin` · `Gunner` · `Monster`(스탯 SO·근접 넉백·밀림·카운터 창·터렛 조준) · `MidBoss`(Gauntlet·Spinner·Wall) · `Boss`(23호) |
| 5 | `Paladin_VFX` | 테이블 대상에서 제외(스폰 안 되는 보관용) |
| 6 | 이전 | 병합 Export 가 **모든 옛 시트(어떤 형식이든)의 값을 (대상, 필드) 로 뽑아** 새 배치의 같은 칸에 덮는다. 옛 형식(행·키-값)은 가져오기에서 계속 읽는다 |
| 7 | 틀 고정 | 필드 열(A) 고정. 머리글·제목 행 굵게 |

🔴 병합 Export 가 이제 **배치를 매번 새로 짠다** — 기획 값은 (대상, 필드) 로 보존되지만, 기획이 시트에 직접 넣은 `#` 메모 행·열은 남지 않는다(원본은 `Library/DataTableExportBackup`).

**결과(2026-10-02)**: 19장 → 6장, 필드 1020개(`Paladin_VFX` 18칸 제외), 병합 직후 Verify 차이 0. 가장 넓은 시트 = `Monster` M열(11 대상). 테스트 74/0.

### D5. 나머지 (범위 확정 후)
- 상태이상 수치(지속·배율 — 현재 코드에서 넘기는지 SO 인지 조사 필요) · 맵 생성(`MapGenConfigSO`) 등 §8 Q1 결과대로.

## 4. 핵심 결정

### 4-1. xlsx 를 직접 읽는다 (CSV 단계 없음)
- 한국어 Windows Excel 의 "CSV 저장" = **CP949** → 한글 깨짐. xlsx 는 내부가 UTF-8 이라 문제가 없다.
- 리더는 **자체 구현**(약 200줄). ExcelDataReader 같은 외부 DLL 을 넣지 않는다 → 패키지 핀·Unity 재시작 이슈 0.
  수식은 저장된 결과값만, 날짜·서식은 지원 안 함(수치 테이블엔 불필요).

### 4-2. 시트 형식 — 두 가지
**행 테이블** (같은 종류가 여러 개 — 몬스터·스킬):

| Id | maxHp | moveSpeed | chaseSpeed | #메모 |
|----|------:|----------:|-----------:|-------|
| (설명 행 — 무시) | 최대 체력 | 배회 속도 | 추격 속도 | |
| GauntletBotData | 300 | 2.5 | 4 | 중간보스 |

- 1행 = **SO 필드 이름**(중첩은 `charge.speed`, 배열은 `phases[0].hp`). 2행 = 설명(무시).
- `Id` = **SO 에셋 파일 이름**. `#` 로 시작하는 열·시트는 무시(기획 메모용).

**키-값 테이블** (하나뿐인 것 — 플레이어 대시·게임 규칙):

| Asset | Field | Value | #설명 |
|-------|-------|------:|-------|
| PlayerDashData | cooldown | 1.2 | 대시 쿨다운(초) |

- **시트 이름 = SO 타입 이름**(네임스페이스 없이, 예: `MonsterDataSO`). 에셋은 그 타입과 **정확히 같은 타입**만 `Assets/` 전체에서 찾는다(하위 타입 섞지 않음).
  > 2026-10-02 구현 중 변경: 초안의 C# 등록표(`DataTableRegistry`)는 두지 않는다 — 관리할 곳이 하나 줄고, SO 를 새로 만들어도 등록 단계가 없다.
  > 이름이 같은 SO 타입이 둘이거나 같은 이름의 에셋이 둘이면 오류로 알려 준다.
- xlsx 폴더 = **`Assets/50.Art/DataTable~/`**(SVN, Q2 잠정). 이름 끝 `~` = Unity 가 임포트하지 않음 → Excel 잠금 파일 `~$*.xlsx` 에 `.meta` 가 안 생긴다.
  위치는 `DataTableSource.Folder` 상수 하나.

### 4-3. 구조는 SO/코드, 수치는 테이블
보스 패턴처럼 중첩·참조가 많은 데이터는 흐름을 테이블로 표현하지 않는다(시트 안에서 프로그래밍하게 된다).
프리팹·VFX·AnimationClip 참조는 **SO 에서 계속 인스펙터로 연결**. 적용기는 테이블에 있는 필드만 건드린다.

### 4-4. GUID 보존 — SO 를 새로 만들지 않는다
- 적용기는 **기존 SO 를 찾아 값만 바꾼다.** 지우고 다시 만들면 GUID 가 바뀌어 프리팹 참조가 전부 끊긴다.
- 테이블에 있는데 에셋이 없는 `Id` = **에러**(새 몬스터는 프로그래머가 SO 를 먼저 만들고 참조를 연결한다).
- 에셋은 있는데 테이블에 행이 없음 = 경고.
- 에러 목록: 중복 Id · 없는 에셋 · 없는 필드 · 타입 변환 실패(`"1,2"`·빈 칸) · 등록표에 없는 시트.

### 4-5. "기획 수치" 분류 기준 (D3·D4 이전 대상)
- ✅ 옮긴다: 피해·체력·방어·속도·쿨다운·지속시간·범위·확률·배율·개수 — **밸런스가 바뀌는 값**.
- ❌ 안 옮긴다: 렌더링·UI 레이아웃·카메라 연출·네트워크 동기화 주기·물리 레이어·디버그 값 — **기술 값**.
- 애매하면 기획에게 묻는다. 목록은 D3 시작 때 이 문서에 표로 붙인다.

### 4-6. 개발자 값 ↔ 기획 값 — Verify
- `Tools/Data/Verify` 는 xlsx 와 디스크 SO 를 비교해 **다른 필드만 보고**한다(쓰지 않음).
- 의미 = "개발자가 인스펙터에서 바꿨는데 기획 값에는 아직 없는 것". 좋은 값을 찾았으면 이 목록을 보고 **기획이 xlsx 에 반영**한다.
- xlsx 에 자동으로 다시 쓰는 기능은 범위 밖(Excel 잠금·서식 보존 문제).

### 4-7. 데이터 출처는 플레이 버튼이 아니라 독립 옵션
- Play 진입 경로가 여러 개(§2)라 버튼마다 모드를 붙이면 `경로 × 모드` 가 늘고 새 경로가 생길 때 빠뜨린다.
- 그래서 **선택은 드롭다운 1개, 적용은 `BeforeSceneLoad` 1곳.** 어떤 버튼으로 들어가도 같은 규칙.
- 테이블 적용 대상이 아닌 값(아직 SO 로 안 옮긴 프리팹 인라인 수치)은 **두 모드에서 같다** — 모드 차이는 테이블에 연결된 필드에서만 생긴다.

## 5. 네트워크 / 권한

- 데이터는 **빌드에 구워진 SO** — 호스트와 클라이언트가 같은 빌드면 같은 값이다. 런타임에 데이터를 주고받지 않는다.
- 권한 구조 변경 0: 데미지·보스·상태이상은 계속 서버가 SO 값으로 계산한다.
- 🔴 버전이 다른 빌드끼리 접속하면 값이 어긋난다 — 기존에도 같은 문제(코드도 어긋남)라 새 위험은 아니다.
- 🔴 **MPPM**: 플레이어 이동·스킬은 **오너(클라이언트) 권한**이라 클론도 호스트와 **같은 데이터 출처**여야 한다.
  클론은 별도 에디터 프로세스라 각자 `BeforeSceneLoad` 에서 xlsx 를 읽어 적용한다. 클론이 같은 설정·같은 xlsx 경로를 보는지 확인 필요(R7).
- 데이터 출처가 다른 두 에디터(예: 다른 워크트리)끼리 접속하면 수치가 어긋난다 — 개발 중에만 생기는 상황이라 화면 표시(`DATA: …`)로 알아보게 한다.

## 6. 범위 밖

- Play 중 즉시 반영(핫 리로드) — 스냅샷 구조(`MonsterBase.Initialize`)를 바꿔야 해서 별도 작업. xlsx 를 고쳤으면 **Play 를 다시 시작**한다.
- SO(개발자 값) → xlsx 자동 역반영.
- Dev Boot 툴바 자체 개선(버튼·씬 드롭다운 분리) — 10-02 별도로 처리함. 이 계획은 그 옆에 드롭다운을 하나 추가할 뿐이다.
- 런타임 데이터 로드·CDN 패치(라이브 서비스용). 필요해지면 `TableApplier` 옆에 JSON 출력을 추가한다.
- 로컬라이제이션(문자열 테이블).
- 새 SO 자동 생성.
- 보스 패턴 흐름의 테이블화.

## 7. 리스크

| # | 리스크 | 대응 |
|---|--------|------|
| R1 | 이전(D3·D4) 중 현재 튜닝 값 유실 | 이전 툴이 Variant 실제 값을 읽어 옮기고 전후 비교 보고. 손으로 옮겨 적지 않는다 |
| R2 | xlsx 는 바이너리 — 두 사람이 동시에 고치면 머지 불가 | 🔴 §8 Q2: SVN **잠금(lock)** 으로 한 번에 한 명 |
| R3 | ~~기획이 임포트 후 git 커밋을 안 하면 SO 가 로컬에만 남음~~ | ✅ 10-02 해소 — 테이블 플레이·빌드가 매번 xlsx 를 읽으므로 SO 커밋이 필요 없다 |
| R4 | SO 필드 이름을 바꾸면(리팩터) 시트 헤더가 깨짐 | 적용·Verify 가 에러로 알려 준다(조용히 무시하지 않음). `FormerlySerializedAs` 쓸 때는 시트도 같이 고친다 |
| R5 | 에셋 파일 이름 변경 시 `Id` 불일치 | 에러로 잡힘. 에셋 이름 바꿀 때 시트도 고친다 |
| R6 | 경석 영역(몬스터·보스) 수정 충돌 | D4 는 경석 합의 후. D1~D3 은 경석 파일을 건드리지 않는다 |
| R7 | MPPM 클론의 `Application.dataPath`(`Library/VP/…`)가 달라 **워크트리 키가 어긋나** 데이터 출처 설정을 못 읽음 → 호스트=테이블, 클론=인스펙터 | D1.5 에서 클론으로 직접 확인(은희 수동 Play). 어긋나면 클론이 메인 프로젝트 경로 기준으로 키를 계산하게 고친다 |
| R8 | 테이블 플레이 중 테이블 관리 SO 를 인스펙터에서 고치면 dirty 가 되어 **테이블 값까지 디스크에 저장**될 수 있음 | 테이블 플레이 중 해당 SO 수정 시 경고, Play 종료 복구가 dirty 여부와 무관하게 원래 값을 되돌림 |
| R9 | 빌드가 메모리 값/디스크 값 중 무엇을 직렬화하는지 불확실 | D1.6 에서 확인. 안전한 쪽 = 전용 빌드 메뉴에서 디스크 적용 → 빌드 → 복구 |
| R10 | 에디터 크래시 시 메모리 적용분 | 디스크에 안 썼으므로 손실 없음. 디스크 적용(빌드) 중 크래시는 `git checkout` 으로 복구 가능(SO 는 git) |
| R11 | 모드 착각("왜 수치가 안 바뀌지?") | 화면 상시 표시 + 툴바 라벨 + Dev Boot 툴팁 |

## 8. 🔴 미확정 질문 (답 → 이 문서 갱신 → 승인)

| # | 질문 | 추천 |
|---|------|------|
| Q1 | "모든 수치"의 범위 — 전투·성장·이동만? 연출 타이밍·맵 생성·UI 까지? | 전투·성장·이동·스킬·상태이상·몬스터. 맵 생성·연출은 나중 |
| ~~Q2~~ | ~~xlsx 위치~~ | ✅ 10-02 은희 확정 — **SVN `Assets/50.Art/DataTable~/` + 잠금(lock)** |
| ~~Q3~~ | ~~임포트와 SO 커밋은 누가?~~ | ✅ 10-02 해소 — 기획은 xlsx 만 SVN 커밋. 기획이 확인할 때는 Unity 에서 `데이터: 테이블` 로 Play |
| ~~Q4~~ | ~~파일 단위~~ | ✅ 10-02 은희 확정 — **`GameData.xlsx` 1개 + 시트 여러 개**(한 번에 한 명만 편집 = 잠금 충돌은 감수). 코드는 폴더의 xlsx 를 전부 읽으므로 나중에 쪼개도 비용 0 |
| Q5 | 시점 — 지스타(11월 중순) 전? | D1·D2 는 지스타 전(작고 독립적). D3·D4 이전은 지스타 이후 권장 — 이전 중 값 유실 위험을 시연 직전에 지지 않는다 |
| ~~Q6~~ | ~~경석 합의~~ | ✅ 10-02 은희 지시로 D4 진행(M1~M5). 🔴 경석에게 변경 공유 필요 |
| ~~Q7~~ | ~~플레이어 이동속도 원본~~ | ✅ 10-02 은희 — **`PlayerMovement.maxSpeed` 로 일원화, `Player.moveSpeed` 삭제** |
| ~~Q8~~ | ~~중간보스 패턴 SO 형태~~ | ~~하위 타입~~ → **불필요**(10-02 설계 변경 — 테이블이 `GauntletBot` 등 컴포넌트를 직접 덮어씀) |
| ~~Q9~~ | ~~23호 M6 시점~~ | ✅ 10-02 은희 — **경석 장판 작업 후로 미룸**(이번엔 M1~M5) |
| Q11 | SO 의 기술 값도 테이블로? | ✅ 10-02 은희 — **몬스터 SO(`MonsterDataSO`·`BossDataSO`)는 `[DataTableIgnore]` 없이 전부 테이블로**(관리를 테이블로 넘긴다). 폭탄(`BossBomb`)은 버려진 코드라 대상 아님 |
| ~~Q10~~ | ~~목숨 수(씬 배치)~~ | ✅ 10-02 은희 — **`Assets/2.Prefabs/Player/GameRule.prefab` 으로 분리** → `Temp_MultiGameRule` 시트(Id `GameRule`, `defaultLifeCount`). 프리팹 인스턴스가 아닌 디버그 씬 오브젝트(PlayerBossTest 의 1)는 테이블이 안 건드려 두 모드 모두 씬 값 |

## 9. 완료 조건

- [ ] 오류가 하나라도 있으면 SO 를 하나도 쓰지 않고, 시트·행·열 위치와 함께 오류 목록을 보여 준다(테이블 Play·빌드 중단).
- [ ] 첫 xlsx(Export Template) 직후 `Verify` 차이 0.
- [ ] Excel 이 파일을 열고 있어도 읽힌다.
- [ ] `Tools/Data/Verify` 가 인스펙터에서 바꾼 값을 차이로 보고한다.
- [ ] **`데이터: 테이블` Play**: xlsx 에서 대시 쿨다운을 바꾸면 Play 에서 바뀐 값이 동작한다.
- [ ] **`데이터: 인스펙터` Play**: 같은 상황에서 SO 인스펙터 값이 동작한다.
- [ ] 기본 Play · Dev Boot 버튼 · Dev_Boot 씬 직접 Play **세 경로 모두** 선택한 데이터 출처를 따른다.
- [ ] 테이블 Play 종료 후 **디스크 SO 변경 0**(git diff 0), 모든 SO `.meta` guid 그대로.
- [ ] MPPM 호스트+클론이 같은 데이터 출처로 동작한다.
- [ ] `Build (Table)` 빌드가 xlsx 값으로 동작하고, 빌드 후 디스크 SO 변경 0.
- [ ] D3 이전 후 가붕이·거너의 체감 동작이 이전과 같다(값 비교 보고 = 차이 0).

## 10. 검증 계획

- EditMode: `XlsxReader`(실제 xlsx 픽스처) · `TableSchema` · `TableValidator` · 전부-또는-0 · 메모리 적용 → `Restore` 왕복.
- 에디터: Export → `Verify` 0 → xlsx 값 하나 바꿈 → `Verify` 에 그 필드만 → 테이블 Play 후 `git diff` 0·`.meta` guid 비교.
- Play(은희 수동, **MCP Play 금지**): 두 데이터 출처 × 세 진입 경로에서 대시 쿨다운·거너 과열 수치 반영 확인, 화면 `DATA: …` 표시 확인.
- MPPM 호스트+클론(은희 수동): 클론 화면 표시가 호스트와 같은지, 클론 플레이어의 대시 쿨다운이 테이블 값인지(R7).
- 빌드: `Build (Table)` 결과물에서 테이블 값 확인 → 빌드 후 `git diff` 0.
