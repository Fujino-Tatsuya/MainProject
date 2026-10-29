# 데이터 테이블 (xlsx → 게임 수치)

> 기획이 게임 수치를 **xlsx 하나**에서 고치고, 빌드는 항상 그 값으로 나간다.
> 설계·결정 원본: [PLAN-data-table.md](../history/PLANS/PLAN-data-table.md). 이 문서는 **쓰는 법**만 다룬다.

## 1. 한눈에

```
GameData.xlsx (SVN, 잠금)
   │
   ├─ [데이터: 테이블] Play ── Play 시작 때 xlsx 값을 메모리에서 덮어씀 → Play 끝나면 원래대로
   ├─ [데이터: 인스펙터] Play ── xlsx 무시, SO·프리팹 인스펙터 값 그대로(개발 중 실험용)
   └─ 빌드 ─────────────── 항상 xlsx 값(빌드 후 원래대로 — git 변경 0)
```

- 파일: **`Assets/50.Art/DataTable~/GameData.xlsx`** (SVN). 폴더 이름 끝 `~` = Unity 가 임포트하지 않는다.
- 고치기 전에 **SVN 잠금(Get Lock)**, 커밋하면 잠금 해제. xlsx 는 두 사람이 동시에 고치면 합칠 수 없다.
- 툴바 `[ 데이터: 테이블 ▾ ]` 로 모드를 고른다(개인 설정). 현재 모드는 툴바 색(테이블 피치 · 인스펙터 연두)으로 본다(10-23 Play 화면 배지 제거).

## 2. 시트 형식

**시트 = 카테고리**(2026-10-09 코드 기준 10장 — xlsx 에는 병합 Export 한 시트만 있다). 한 장 안에 관련 타입이 구역으로 나뉘어 세로로 쌓인다 — 가로 이동은 대상 수만큼(최대 Monster 11열).

| 시트 | 들어 있는 것 |
|---|---|
| `Player` | 스탯(`Player`) · 이동 · 대시 · 낙하 복귀 · 착지 보호 · 영혼 · 게임 규칙(낙하 비율) · 목숨 · 선입력 버퍼(`PlayerInputReader` — `inputBufferSeconds`, 우선순위 `bufferPriority*` 6칸, [skill-reject-feedback.md §0.2](skill-reject-feedback.md)) |
| `Paladin` | 가붕이 기본 공격 · 패시브 · 스킬 SO 4종 |
| `Gunner` | 거너 기본 공격 · 과열 · 스킬 SO 4종 |
| `Assassin` | 어쌔신 기본 공격 · 상태 · 스킬 SO |
| `Monster` | 몬스터 스탯 SO · 근접 공격 · 넉백 · 카운터 창 · 터렛 조준 |
| `MidBoss` | Gauntlet · Spinner · Wall |
| `Boss` | 23호(`BossDataSO`) |
| `Sound` | 문자열 키별 사운드(`SoundCatalog`) — 재생 수치 + 엔티티 구성(라이브러리·클립·재생 모드). 구성은 `Tools/Data/사운드 동기화` 로 반영(§6 사운드 키) |
| `Map` | 허수아비 인터럽트 순환(`TrainingDummyInterrupt`) |
| `SuppressHaste` | 제압 가속 버프(할 일 91, `SuppressHasteData` — `Assets/Resources/Player/`) · 배율 3개(`moveSpeedMultiplier`·`attackSpeedMultiplier`·`cooldownRateMultiplier`) · `durationOverride`(0 = 이번 제압 길이) · 비네팅 페이드 `vignetteFadeIn`/`vignetteFadeOut`. [handoff-suppress-haste.md](handoff-suppress-haste.md) |
| `Cinematic` | 연출 시간(2026-10-26 — ⏳ 시트는 아직 병합 Export 전) — 튜토리얼 4구역 소개(`MapSceneZone4IntroData` → `Assets/Resources/MapScene/Zone4IntroCinematic.asset`): 샷 이동·머묾 `shots[0..4]` · 복귀·스킵 블렌드 · 대사 `lines[i]` 시작 지연·조각 표시·조각 간격 · 한 줄 최대 폭. 문구는 `Strings`(`Tutorial.Guide.TUT_Z4_INTRO_*`), 카메라 구도는 에셋 인스펙터(제외). [handoff-zone4-intro-cinematic.md](handoff-zone4-intro-cinematic.md) |
| `UI` | 스킬 툴팁 태그 칩 색(할 일 114 후속, `SkillTooltipTagStyles` — `Assets/2.Prefabs/UI/SkillTooltipGenerated/`) — `defaultBackground`·`defaultText` · `entries[i].tagId`·`entries[i].background`·`entries[i].text`. ⏳ 병합 Export 전. [§9 태그 칩 색](#태그-칩-색--ui-시트-할-일-114-후속) |
| `Traits_Paladin` · `Traits_Gunner` · `Traits_Assassin` | 특성 정의 수치(할 일 125 — ⏳ 병합 Export 전) — 열 = 정의 에셋(`PAL_1_Q_DAMAGE` 등), 행 = `effects[i].필드`(효과 클래스 `[SerializeReference]` 칸, 원소 이름표 `effects[0] · TraitDamageEffect`). 비율 0.20 = 20%, 시간 초, 거리 m. 대상 타입 = 캐릭터별 `<캐릭터>TraitDefinition`. 문구는 Strings `Trait.<ID>.*`. 시트가 없으면 인스펙터 값(오류 없음). [tutorial-traits.md §11.5](tutorial-traits.md#115-데이터-테이블-시트-traits_paladin--traits_gunner--traits_assassin) |
| `Strings` | **게임 문구(한/영)** — 열 `key` · `#설명` · `ko` · `en`, 세로 표가 아니다. SO 에 쓰지 않고 `StringTable` 로 간다. 쓰는 법·검증 규칙은 [localization.md](localization.md) |
| `ErrorCodes` | **로비·네트워크 오류 문구(한/영)** — 열 `code` · `#분류` · `#원인(개발자용)` · `#조치(QA·CS용)` · `ko` · `en`. `Strings` 와 같은 규칙으로 `StringTable` 로 간다. [localization.md §2](localization.md) |
| `SlowMotion` | 인터럽트 슬로우 모양 `interruptProfile.*`(`scale`·`enterDuration`·`holdDuration`·`exitDuration`·`failExitDuration`, 대상 `NetworkManager` — 타입 `InterruptProductionDirector`(Order 0, 할 일 118 C2 에서 `GlobalTimeScale` 에서 옮김, 필드 경로 그대로라 xlsx 무변경. 시트 구역 제목 `#■ GlobalTimeScale` 은 메모라 그대로. 디렉터 스위치 bool 10칸은 `[DataTableIgnore]`. 할 일 132: `interruptProfile` = **강** 시도(기본 0.15·hold 0.5) · 🟨 **반영 예정**(메인 xlsx Export — 열이 생기기 전까지 프리팹 값) 새 칸 **약** 시도 `interruptWeakProfile.*`(같은 7칸, 기본 0.5·hold 0.2) · 강/약 시도 오너 줌 배율 `interruptZoomFactor`(0.7)·`interruptWeakZoomFactor`(0.93). 오너 암전·적중 섬광 칸 `ownerDim*`·`hitFlash*` 는 `[DataTableIgnore]`)) · 간파 성공 슬로우 `interruptSuccessProfile.*`(같은 5칸, 할 일 87 C1) · 맨 앞 히트스톱 `*.hitStopDuration`(실시간 초, 0 = 없음)·`*.hitStopScale`(히트스톱 배율, 두 프로필 공통 칸 — 기본 성공 0.05초·0.02, 시도 0, C2) — 프로필당 7칸 · 오너 줌 배율 `slowMotionZoomFactor`(대상 `CameraSwitcher`) · 간파 성공 암순응(`InterruptDarkAdaptation`, 대상 `NetworkManager`, C3) `selfIntensity`·`othersIntensity`·`fadeInDuration`·`holdDuration`·`fadeOutDuration`·`luminanceThreshold`·`thresholdSoftness`·`maxDarken`·`revealRadius`·`revealSoftness`(원 중심 기준 `revealCenter` 는 enum 이라 열 없음 — 인스펙터 전용, C5). 의미는 [PLAN-interrupt-slowmo.md](../history/PLANS/PLAN-interrupt-slowmo.md) · [interrupt-presentation.md](interrupt-presentation.md) |

### 세로 표 — 행 = 필드, 열 = 대상

| 필드 | #설명 | ChompBotData | GauntletBotData |
|---|---|---|---|
| `#■ MonsterDataSO` | | | |
| `#  ─ 스탯` | | | |
| maxHp | 최대 체력 | 120 | 300 |
| `#  ▸ attacks[3] · JumpAttack` | | | |
| attacks[3].damage | | 15 | - |

- 타입마다 `#■ 타입` 제목 행 → **`필드 | #설명 | 대상…` 머리글 행** → 값 행. 구역마다 머리글이 다시 나오고 대상(열)도 구역마다 다르다.
- `#  ─` = 코드 `[Header]` 묶음, `#  ▸` = 배열 원소(첫 이름표 필드로 표시), `#     ·` = 원소 안 묶음. 전부 메모 행(무시).
- 대상 = SO 에셋 파일 이름, 또는 그 컴포넌트를 가진 **프리팹 파일 이름**(Variant 도 각자 — `Player_Paladin`, `Player_Gunner`).
- A열(필드)은 틀 고정 — 오른쪽으로 가도 필드 이름이 보인다. **필드 칸·머리글은 고치지 않는다**(값 칸만).
- 배열 원소는 `stageThresholds[0]`, 구조체 안쪽은 `charge.speed`.
- 옛 형식(1행 `Id` 의 행 테이블, 1행 `Asset | Field | Value` 키-값)도 계속 읽는다. 병합 Export 하면 새 배치로 옮겨진다.

## 3. 값 규칙

| 규칙 | 설명 |
|---|---|
| **`#` = 메모** | `#` 로 시작하는 시트·열·행은 무시. 행을 잠깐 빼고 싶으면 필드 앞에 `#`(그 행의 모든 대상이 빠진다) |
| **테이블에 없으면 = SO·프리팹 값** | 필드 **행이 없거나** 대상 **열이 없으면** 그 값은 건드리지 않는다 — 인스펙터(SO·프리팹) 값이 그대로 쓰인다(Play·빌드 공통). 대상 열이 없으면 경고만 낸다(`… 의 행이 테이블에 없다`). 새로 추가한 필드는 병합 Export 전까지 SO 값으로 돈다(`DataTableApplier.Bind` 는 테이블에 있는 칸만 쓴다) |
| **빈 칸 = 오류** | 행·열은 있는데 칸만 비면 오류다 — "안 채움" 과 "0" 을 구분해야 해서. 0 이면 0 을 적는다. 이 대상에서 빼려면 `-`. **예외: 문자열 칸(`[DataTableText(Localize = false)]` — 사운드 `key`·`description`)은 빈 칸 = 빈 문자열** |
| **문구는 수치 시트에 없다** | 화면 문구(툴팁 `tooltip.*`·이름 등 `[DataTableText]`)는 **`Strings` 시트가 단일 원본**이다 — 수치 시트로 Export 하지 않고, 옛 xlsx 에 남은 칸은 적용·Verify 하지 않는다(시트마다 경고 한 줄). 병합 Export 가 Strings 로 옮긴다([§9](#9-프로그래머기획--툴팁-문구와-자리표시자)) |
| **`-` = 이 대상엔 없는 칸** | 배열 길이가 대상마다 다를 때(예: 23호 `No23` 공격 8개 · `No23_Solo` 6개 → Solo 의 `attacks[6]·[7]` 은 `-`) |
| 숫자 | 소수점은 `.`(쉼표 X). 정수 칸에 소수 넣으면 오류 |
| 참/거짓 | `TRUE` / `FALSE` (또는 1 / 0) |
| 열거형 | 이름 그대로(`Run`, `Walk` — 대소문자 무관) |
| **범위** | 코드에 `[Range]`·`[Min]` 이 있으면 테이블도 같은 범위. 벗어나면 오류 |
| 배열 크기 | 테이블은 배열 크기를 못 바꾼다(원소 값만) |
| 수식 | 써도 된다 — **계산된 결과값**을 읽는다 |

**오류가 하나라도 있으면 아무 값도 적용하지 않는다.** 테이블 모드 Play·빌드는 시작 전에 멈추고 오류 위치(`GameData.xlsx › 시트!C3`)를 보여 준다.

## 4. 메뉴 (`Tools/Data/`)

| 메뉴 | 하는 일 |
|---|---|
| **Verify** | 인스펙터 값과 테이블 값이 다른 필드를 Console 에. 씬 인스턴스가 테이블 필드를 덮어쓰면 경고. 아무것도 쓰지 않는다 |
| **Export Template** | 현재 인스펙터 값으로 xlsx 를 만든다. 파일이 있으면 **병합**(배치는 새로 짜고 기획 값은 (대상, 필드) 로 유지 — 시트에 직접 넣은 `#` 메모 행·열은 남지 않는다, `#` 메모 시트는 유지) / **덮어쓰기** 선택. 원본은 `Library/DataTableExportBackup` 에 보관. 옛 수치 시트의 문구 칸은 Strings 로 옮긴 뒤 뺀다([§9](#9-프로그래머기획--툴팁-문구와-자리표시자)) |
| Export Template 필드 목록 보기 | 파일을 안 쓰고 시트별로 나갈 필드만 Console 에 |
| 인스펙터 값을 테이블 값으로 덮어쓰기 | 디스크의 SO·프리팹을 테이블 값으로 바꾼다(되돌릴 생각이 없을 때만, Undo 가능) |
| StringTable 갱신 (Strings·ErrorCodes 시트 → 에셋) | `Strings`·`ErrorCodes` 시트만 읽어 검증하고 `StringTable.asset` 을 갱신([localization.md](localization.md)) |
| 테이블 폴더 열기 | 탐색기로 연다 |

🔴 Export 는 xlsx 를 새로 쓴다 — **서식·색·열 너비·수식은 사라진다**(수식은 결과값으로 남는다). 병합이어도 같다.
`Strings`·`ErrorCodes` 시트는 예외로 병합·덮어쓰기 모두 기존 행을 그대로 옮긴다(문구는 인스펙터 값이 없다) — [localization.md §2](localization.md).

## 5. 인스펙터에서 보이는 것

테이블이 관리하는 SO·프리팹(·씬 인스턴스)을 선택하면 인스펙터 맨 위에 박스가 뜬다:
"필드 N개는 테이블 모드 Play·빌드에서 xlsx 값으로 덮어쓴다" + **테이블과 다른 값**(인스펙터 / 테이블).
인스펙터에서 바꿔도 테이블 모드에선 xlsx 값이 이긴다 — 좋은 값을 찾았으면 xlsx 에 옮겨 적는다.

## 6. 알아 둘 것

- **xlsx 를 고쳤으면 Play 를 다시 시작**한다. Play 중 수정은 반영 안 된다.
- 스폰 때 한 번 읽는 값(몬스터 HP·공격력 등)은 그 뒤 스폰부터 반영된다.
- **몬스터 `attackSpeed` = 공격 애니 재생 배율**(1 = 원본, 2 = 두 배 빠르게). 공격 판정·예고·간파 창·돌진 시간도 따라간다. **공격 간격은 `attackCooldown`(공격이 끝난 뒤 쉬는 시간, 초)** — 2026-10-02 의미 변경, 상세 `PLAN-monster-anim-speed.md`. `attackCooldown` 은 아직 시트에 행이 없어 SO 값을 쓴다(§3).
- **씬에 배치한 프리팹 인스턴스가 그 필드를 오버라이드하면** 테이블 값이 안 닿는다 → Verify 경고 → 씬에서 필드 우클릭 → Revert.
- 프리팹 하나에 같은 컴포넌트가 둘 이상이면 행 하나로 가리킬 수 없어 오류.
- 씬 배치 오브젝트(프리팹 인스턴스 아님)는 테이블 대상이 아니다 — 필요하면 프리팹으로 분리(예: `GameRule.prefab`).
- MPPM 클론도 같은 모드·같은 xlsx 를 쓴다.
- 테이블 Play 가 끝나면 테이블 대상 **프리팹은 디스크에서 다시 임포트**해 되돌린다(SO 는 스냅샷 값으로). Variant 에 메모리로 쓴 값은 숨은 오버라이드·dirty 를 남겨,
  값만 되돌리면 다음 Save Project 가 `Player_Gunner` 에 `maxHp`/`attackDamage` 오버라이드를 써 넣었다(2026-10-09). 그래서 프리팹 에셋을 Prefab Mode 밖에서 고친 채 저장 안 하고 테이블 Play 하면 그 변경은 사라진다.
- **저장 가드**(`DataTableSaveGuard`): 테이블 Play 준비(`ExitingEditMode`)에서 켜지고 Play 종료 후 스냅샷 복구(`EnteredEditMode`)가 끝나야 풀린다. 그 사이 어떤 저장(Ctrl+S·Save Project·다른 툴의 `SaveAssets`)도 테이블 대상 SO·프리팹은 쓰지 않는다(Console 경고). 기준은 Play 여부가 아니라 "복구 전" 이다 —
  Play 여부로 막던 때, Play 종료 직후 `DevBootLauncher` 의 `SaveAssets` 가 복구보다 먼저 돌아 `Player_Gunner`·`Player_Paladin` 에 테이블 값이 저장됐다(2026-10-09 재발). 메뉴 덮어쓰기·빌드는 가드가 풀린 상태에서 돌므로 정상 저장된다.

### 사운드 키 — 사운드 동기화

> 결정·설계: [PLAN-sound-sync.md](../../PLAN-sound-sync.md). 사운드 추가 = **`Sound` 시트에 행 작성 → `Tools/Data/사운드 동기화`**. Library Manager·`SoundCatalog` 인스펙터는 열지 않는다.

`Sound` 시트의 `SoundCatalog` 열, `entries[i].*` 행 한 묶음 = 키 하나.

| 칸 | 예 | 누가 쓰나 | 의미 |
|---|---|---|---|
| `key` | `Footstep` | 데이터 테이블 + 동기화 | 코드에서 부르는 키 |
| `library` | `SFXLibrary` | 동기화 | BroAudio 라이브러리. 없으면 만든다(`Assets/51.Audio/Data/SoundLibrary`). 다르면 엔티티를 옮긴다(guid 유지) |
| `entity` | `Footstep` | 동기화 | BroAudio 엔티티 이름(프로젝트 전체 고유). 없으면 만들고 SoundID 를 연결한다. 한 엔티티는 한 키만 |
| `audioType` | `SFX` | 동기화 | `Music`·`UI`·`Ambience`·`SFX`·`VoiceOver` |
| `clipPaths` | `Assets/51.Audio/SFX/Step_01.wav`⏎`Assets/51.Audio/SFX/Step_02.wav` | 동기화 | 프로젝트 기준 전체 경로, 여러 개면 **셀 안 줄바꿈(Alt+Enter)**. 엔티티 클립을 통째로 바꾼다(남는 클립의 Volume·Weight 등은 보존). `-` = 유지 |
| `playMode` | `Random` | 동기화 | `Single`·`Sequence`·`Random`·`Shuffle`·`Velocity`·`Chained`. `-` = 유지 |
| `loop` | `0`/`1`/`2` | 동기화 | 0 = 반복 없음, 1 = Loop, 2 = SeamlessLoop(BGM). `-` = 유지 |
| `volumeMultiplier`·`volumeRandomMin/Max` | `0.8`·`0.85`/`1` | 데이터 테이블 | 엔티티 볼륨에 곱하는 배율 × 재생마다 랜덤 배율(1/1 = 흔들지 않음) |
| `pitchMultiplier`·`pitchRandomMin/Max` | `1`·`0.95`/`1.05` | 데이터 테이블 | 피치 배율 × 재생마다 랜덤 배율 |
| `is3D`·`minDistance`·`maxDistance` | `1`·`2`·`30` | 데이터 테이블 | 0 = 2D, 1 = 3D와 감쇠 거리 |
| `maxSimultaneous`·`retriggerCooldown` | `3`·`0.05` | 데이터 테이블 | 같은 키 동시 재생 수·재트리거 간격(0 = 제한 없음) |

- **엔티티의 볼륨·피치·랜덤·3D 설정은 쓰지 않는다**(생성 기본값 1). 수치는 위 엔트리 칸에서만 고친다. SeamlessLoop 전환·Priority·클립별 Weight/Fade·SpatialSetting·PlaybackGroup 은 필요할 때 Library Manager 에서 — 동기화가 건드리지 않는다.
- **처음 한 번은 `Export Template` 병합 필수** — 그 전의 시트엔 구성 칸이 없어 동기화가 오류로 멈춘다(배열이 줄어 키가 지워지는 걸 막는 장치).
- 순서: SVN 잠금 → 행 추가 → **`Tools/Data/사운드 동기화`** → 변경 요약 확인 → **SVN 한 번에 커밋**: xlsx · 클립 · `Assets/51.Audio/Data/`(라이브러리·엔티티·`SoundCatalog.asset`) · **각 .meta**. 2026-10-14 부터 사운드 데이터는 전부 SVN 이다(코드 `SoundCatalog.cs` 만 git).
- **자동 동기화(2026-10-14)**: **테이블 모드 Play 진입**과 **빌드** 직전에 확인 없이 같은 동기화가 돈다(변경은 Console `[SoundSync]`). 오류면 Play 진입 취소·빌드 실패. 인스펙터 모드 Play·MPPM 클론은 하지 않는다. 테이블 값과 달리 **결과는 디스크에 남는다** — Play·빌드 뒤 `51.Audio/Data` 에 변경이 보이면 xlsx 와 같이 SVN 커밋. 메뉴는 확인 대화상자를 보고 싶을 때 쓴다.
- 오류가 하나라도 있으면 아무것도 쓰지 않는다(이름 오타·없는 클립·`51.Audio` 밖 경로·엔티티 공유·같은 이름 엔티티 2개·인덱스 구멍). 어느 키도 안 쓰는 엔티티·지워지는 키는 경고만 — 삭제는 사람이.
- 새로 늘어난 엔트리의 수치는 SO 에 기본값으로 들어간다 — 인스펙터 모드 Play 에선 기본값, 테이블 모드·빌드에선 xlsx 값.
- 새 클립의 `.meta` 는 반드시 SVN 에 같이 커밋한다 — 없으면 PC 마다 guid 가 달라 엔티티 클립 참조가 끊긴다(다시 동기화하면 그 PC 에선 고쳐지지만 git diff 가 생긴다).
- 동기화 뒤 열려 있던 Library Manager 창은 다시 연다(새 라이브러리·엔티티 반영).
- 호출: 2D `AudioManager.Instance.Play("키")`, 3D `Play("키", worldPosition)`, BGM `PlayBGM("BGM_Title")`. 로컬 재생이며 사운드 전용 RPC는 없다. 없는 키·무효 SoundID 는 경고하고 재생하지 않는다.
- 현재 BGM 키: `BGM_Title`·`BGM_Lobby`·`BGM_InGame`(2026-10-14 고정 필드에서 이전).

## 7. 자주 나는 오류

| 메시지 | 원인 · 해결 |
|---|---|
| `시트 이름 'X' 과 같은 이름의 … 타입이 없다` | 시트 이름 오타, 또는 코드에서 타입 이름이 바뀜 |
| `에셋 'X' 가 없다` / `프리팹 'X' 가 없다` | Id 오타, 파일 이름 변경. 새 대상이면 프로그래머가 에셋·프리팹을 먼저 만든다 |
| `필드 'X' 가 없다` | 필드 이름 오타 또는 코드에서 이름이 바뀜(병합 Export 하면 `#` 처리된다) |
| `값이 비어 있다` | 빈 칸 — 값을 채우거나 행·열을 `#` 로 |
| `범위 … 밖이다` | 코드의 `[Range]`·`[Min]` 위반 |
| `'X.y' 를 … 에서도 쓴다` | 같은 필드를 두 곳에서 씀 |
| `xlsx 가 없다` (빌드) | SVN 업데이트 안 함. 빌드는 테이블 없이 하지 않는다 |

## 8. 프로그래머 — 새 수치를 테이블에 내보내기

1. SO 또는 컴포넌트 클래스에 카테고리 시트 `[DataTableSheet("Monster", Order = 3)]`(Order = 시트 안 구역 순서). 카테고리를 바꾸면 병합 Export 가 값을 새 자리로 옮기고 옛 시트를 뺀다.
2. 기술 값(버퍼 크기·네트워크 보정·연출 정렬·물리 상수 등)에 `[DataTableIgnore]`. 구조체·배열에 붙이면 그 아래 전부.
3. 필드에 `[Tooltip("…")]` — 테이블 설명 칸이 된다. 범위가 있으면 `[Range]`·`[Min]`(테이블도 검증).
4. `Export Template 필드 목록 보기` 로 확인 → `Export Template` **병합** → SVN 커밋.

- 수치 시트로 내보내는 건 **정수·실수 + `[DataTableText(Localize = false)]` 문자열**(구조체·배열 원소 포함). bool·enum·일반 문자열은 템플릿에 넣지 않는다.
- 화면 문구 문자열에는 `[DataTableText]`를 붙인다 — **Strings 시트 자동 키로만** 나가고 수치 시트 Export·병합·Verify·적용에서는 빠진다([localization.md §5](localization.md#5-l2--so-문구-자동-키)).
  코드 ID·기획 메모처럼 번역 대상이 아닌 문자열은 `[DataTableText(Localize = false)]` — 수치 시트 문자열 칸이 된다. 둘 다 없는 문자열(`animatorStateName` 등)은 어디에도 안 나간다.
- `[SerializeReference]` 목록(특성 효과 등)도 원소 안쪽 숫자 칸을 내보내고 적용한다 — 필드·`[Range]`·`[Min]`·`[DataTableIgnore]` 는 **들어 있는 값의 실제 타입**에서 찾는다(`DataTableFields.Resolve(target, path)`, 할 일 125). 원소가 비었거나 칸 번호가 없으면 오류.
- `NetworkVariable` 안쪽은 자동 제외. 레거시 폴더·`Assets/50.Art/` 프리팹(검수용)은 템플릿 대상이 아니다.
  다른 프리팹 안에만 중첩돼 단독 스폰되지 않는 프리팹은 `DataTableTemplate.NestedOnlyPrefabs`(파일 이름)에 넣어 뺀다 — 바깥 프리팹 행이 실제 값(예: `Elevator` → `BossElevatorRig` 은 `ZoneS_typeBossEnter` 행만). 보관용은 `ArchivedPrefabs`.
- 필드 이름을 바꾸면(`FormerlySerializedAs`) 시트 머리글도 같이 바꾼다 — 안 그러면 오류.
- 테스트: `Tools/Tests/데이터 테이블 EditMode 테스트 실행`.

## 9. 프로그래머·기획 — 툴팁 문구와 자리표시자

툴팁 문구(`displayName`·`subtitle`·`description`)는 **`Strings` 시트에서 고친다** — 키 `주인ID/tooltip.description`(예 `FirstMeleeMainSkillData/tooltip.description`), ko·en 열.
수치 시트(`Paladin` 등)에는 문구 칸이 없다. 셀 줄바꿈(`Alt+Enter`)은 게임에서도 줄바꿈이다.

**옛 xlsx 이관(병합·덮어쓰기 Export 공통)** — 수치 시트에 `tooltip.*` 칸이 남아 있으면:

1. 그 값을 Strings 시트의 같은 자동 키 행 **ko** 로 옮긴다(새 행, en 빈 칸). 인스펙터(SO) 값이 아니라 **xlsx 값**이 들어간다 — 기획이 고친 문구 보존.
2. Strings 에 그 키 행이 **이미 있으면 Strings 행을 유지**한다(L2 뒤 고친 곳은 Strings — en 과 짝이 맞는 쪽). 값이 다르면 Console 경고에 두 값을 남긴다(버린 값은 Export 백업에).
3. 옮긴 칸만 수치 시트에서 빠진다(병합 보고 `문구 칸 N개는 Strings 시트로 옮겨…`). Strings 머리글을 못 읽는 등 **옮기지 못하면 Export 를 멈추고 파일을 쓰지 않는다**.
4. 같은 값이 두 번 들어가지 않는다 — 다시 Export 해도 결과가 같다. 원본은 `Library/DataTableExportBackup`.

순수 로직은 `TableTextMigration`(테스트 `TableTextMigrationTests`). 이관 전 옛 xlsx 로 테이블 Play 하면 문구 칸은 **적용하지 않고** 시트마다 경고 한 줄 — 그동안 화면 문구는 Strings 행 → 없으면 SO 인라인 값이다.

스킬 툴팁 `description`은 같은 대상(키 주인 — SO·컴포넌트)의 직렬화 필드를 자리표시자로 읽는다.

| 표기 | 의미 | 예 |
|---|---|---|
| `{필드}` | 숫자를 간결하게 표시 | `{cooldownTime}` → `8` |
| `{필드:0.0}` | .NET 숫자 형식 적용 | `{shieldDuration:0.0}` → `2.5` |
| `{필드:%}` | 0~1 비율을 백분율로 표시 | `{healPercent:%}` → `15%` |
| `{dmg}` | 현재 최종 공격력으로 계산한 피해와 공격력 아이콘 — 판정 스냅샷과 같은 식(기본%·전체%·가하는 피해%, [player-stats.md](player-stats.md)) | Shift를 누르면 계산식(계수·고정값)도 표시 |

- 필드 경로는 A열 표기와 같다. 구조체는 `{charge.speed}`, 배열 원소는 `{stages[0]}`처럼 쓴다.
- 다른 에셋의 값은 참조할 수 없다. 키 주인(SO·프리팹 컴포넌트) 안 필드만 쓴다.
- 없는 필드, 잘못된 숫자 형식, 닫히지 않은 `{` 또는 `<style>` 태그는 **Strings 검증**(테이블 Play·빌드·StringTable 갱신)에서 **ko·en 각각** 셀 위치와 함께 오류가 난다 — 테이블 Play·빌드는 멈춘다([localization.md §2 검증 규칙](localization.md#검증-규칙)).
- 테이블을 거치지 않는 인스펙터 모드에서는 잘못된 문구가 툴팁에 `[오류: …]`로 보이고 Console에 경고가 남는다.

키워드 색은 TMP style 태그를 사용한다. 색상 값은 문구에 직접 쓰지 않고 공용 Style Sheet에서 관리한다.

```text
<style=stun>기절</style>
<style=shield>보호막</style>
<style=heal>회복</style>
<style=overheat>과열</style>
<style=slow>둔화</style>
```

여는 태그와 `</style>`을 반드시 짝지어 쓴다. 새 키워드 색이 필요하면 `Tools/UI/스킬 툴팁 구성`이 만드는 `SkillTooltipStyles.asset`에 스타일을 추가하고 문구에서는 그 이름만 사용한다.

### 태그 칩 — `tooltip.tags` (할 일 114)

툴팁 이름 아래 칩(넉백·기절 등)은 **수치 시트**(`Paladin`·`Gunner`·`Assassin` 등)의 `tooltip.tags` 행에서 고친다 — `[DataTableText(Localize = false)]` 문자열 칸이라 Strings 시트로 가지 않는다.

| 규칙 | 설명 |
|---|---|
| 형식 | 태그 ID 쉼표 구분 — `Knockback,Stun`, `Knockback, Stun`(공백 허용), 대소문자 무관. 칩 순서 = 적은 순서 |
| 빈 칸 | 태그 없음(문자열 칸 예외 — §3) |
| 태그 ID 목록 | 코드 한 곳 `SkillTooltipTag`(`Assets/1.Scripts/Player/Skill/SkillTooltipTags.cs`): `Knockback`·`Stun`·`Vulnerable`·`Shield`·`Heal`·`Overheat`·`Unstoppable`·`Invulnerable` |
| 오류 | 목록에 없는 ID·같은 태그 두 번·쉼표 사이 빈 칸 → 테이블 Play·빌드 검증 오류(셀 위치와 함께 멈춤). 인스펙터 모드는 아는 태그만 그리고 경고 한 번 |
| 화면 이름 | Strings 키 `UI.SkillTooltip.Tag.<ID>`(예 `UI.SkillTooltip.Tag.Knockback`) — 행이 없으면 코드 ko(넉백 등). [localization.md §8](localization.md#8-l4b--코드에-박힌-ui-문구) |

- 새 태그 = 프로그래머가 `SkillTooltipTag` 에 한 줄 + `SkillTooltipTags` ko 이름 한 줄 → 병합 Export 로 Strings 행이 생긴다. 색 줄은 아래 `UI` 시트(없으면 기본색 + 경고).
  칩 배경 = 둥근 사각형 9-slice `Assets/2.Prefabs/UI/Textures/SkillTooltipTagChip.png`(저작 메뉴가 한 번 구운 흰 PNG · 64px · 반지름 22px · 보더 24px · PPU 200, Image Type Sliced). 기획 이미지가 나오면 이 스프라이트만 바꾼다.
  칩 테두리 = 자식 `Ring` Image(같은 규격의 흰 둥근 링 9-slice `SkillTooltipTagChipRing.png`, Sliced, Raycast 끔) — **색은 그 태그의 글자색**(따로 칸 없음). 두께 기본 3.5px(= 1.75 UI 단위, 칩 높이 26 의 약 6.7%). 바꾸려면 `Tools/UI/스킬 툴팁 태그 칩 테두리 두께` 에서 고르고 **다시 굽기**(PNG 를 그 자리에 다시 써 guid 유지 — 프리팹 수정 없음, 두께는 링 `.meta` userData 에 기록). UI `Outline` 컴포넌트는 모서리가 어긋나 쓰지 않는다.

### 태그 칩 색 — `UI` 시트 (할 일 114 후속)

칩 배경·글자색은 **`UI` 시트**의 `SkillTooltipTagStyles` 열에서 고친다(세로 표 — 행 = 필드, 열 = 에셋 하나). 태그 한 개 = `entries[i]` 3행.

| 행 | 예 | 의미 |
|---|---|---|
| `defaultBackground` · `defaultText` | `#2A2E36E6` · `#C9CED6` | 표에 줄이 없는 태그의 색 |
| `entries[i].tagId` | `Knockback` | 태그 ID(`SkillTooltipTag` 이름, 대소문자 무관) |
| `entries[i].background` | `#3D2A12E6` | 칩 배경 |
| `entries[i].text` | `#F2B84B` | 칩 글자 **+ 둥근 테두리** |

- 색 = **`#RRGGBB` 또는 `#RRGGBBAA`**(16진수, `#` 필수, 대소문자 무관). 색 이름(`red`)·3자리 축약·빈 칸은 **오류**(테이블 Play·빌드 중단, 셀 위치 표시). 파서 `HexColor`.
- 없는 태그 ID·빈 ID·같은 태그 두 줄 = 오류. **줄이 없는 태그 = 경고**(기본색으로 그린다). 인스펙터 모드는 경고 한 번 + 기본색.
- 줄 수(배열 크기)는 xlsx 에서 못 바꾼다(§3) — 새 태그를 넣으면 프로그래머가 에셋 `entries` 를 한 줄 늘린다(EditMode 테스트 `ProjectAsset_CoversEveryTag_WithoutErrors` 가 잡는다).
- 에셋이 없으면 `Tools/UI/스킬 툴팁 레이아웃만 갱신` 이 기본 색으로 만들고 툴팁(`SkillTooltipView.tagStyles`)에 잇는다. 있으면 값은 건드리지 않는다.

기본 색 초안(2026-10-27 2차 — 배경 = 같은 계열 어두운 반투명(알파 `E6`), 글자·테두리 = 같은 계열 밝은 색. 반투명 배경은 툴팁 패널 `#090B11` 위에 합성한 색으로 글자 대비 4.5:1 이상을 테스트가 확인 — 초안·프로젝트 에셋 둘 다):

| 태그 | 배경 | 글자·테두리 |
|---|---|---|
| Knockback 넉백 | `#3D2A12E6` | `#F2B84B` |
| Stun 기절 | `#38330EE6` | `#EEDC5A` |
| Vulnerable 취약 | `#3D1530E6` | `#FF8FCF` |
| Shield 보호막 | `#13294AE6` | `#7FB8FF` |
| Heal 회복 | `#14351CE6` | `#7EE08F` |
| Overheat 과열 | `#45160FE6` | `#FF7B63` |
| Unstoppable 저지불가 | `#2A1A45E6` | `#C29BFF` |
| Invulnerable 무적 | `#2E3238E6` | `#E8ECF2` |
| (기본색 — 표에 없음) | `#2A2E36E6` | `#C9CED6` |
- 이 행은 새 필드라 병합 Export 전까지 시트에 없다 — 그동안은 SO 값(지금 전부 빈 칸 = 태그 없음)으로 돈다(§3).

### 툴팁 배치 (할 일 114)

`CombatHUD.prefab › SkillTooltipHost/Panel`. 저작 메뉴 `Tools/UI/스킬 툴팁 레이아웃만 갱신`(CombatHUD 만 — 문구 시드·인라인 아이콘 아틀라스는 안 건드림) 또는 `Tools/UI/스킬 툴팁 구성`(전부).

- `Header`: 좌상단 `Icon` / 오른쪽 `DisplayName`(크게)·`Subtitle`(슬롯 줄 — 툴팁 부제, 부제가 없는 패시브는 키 배지 글자)·`TagRow`(칩, 폭을 넘으면 줄바꿈하고 Header 가 늘어난다) / 우상단 `CooldownGroup`(시계 `CooldownIcon` + 초, 쿨다운 없거나 0 이면 숨김) / 바닥 `Divider`.
- 그 아래 `Description`(수치 강조 그대로) → 오른쪽 아래 `ShiftHint`(`UI.SkillTooltip.ShiftHint`).
- 저작 메뉴는 `Header/TagRow` 가 없을 때만 배치를 한 번 다시 놓는다(옛 `KeyBadge` 는 지운다). 이후 위치·색·크기는 프리팹 인스펙터에서 고친다 — 재실행이 덮지 않는다.
- Shift 상세 갱신·화면 경계 보정·표시 지연은 그대로다.
