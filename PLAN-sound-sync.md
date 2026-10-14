# ▶▶▶ 계획 = **사운드 동기화 — xlsx 한 곳에서 라이브러리·엔티티·클립·SoundID 연결까지** (2026-10-14 · ✅ 승인 · loop 0/1/2 확정 · ✅ S1~S5 구현 · ⏳ xlsx 병합 Export·Play)

> 브랜치 `feature/SFX`. 앞 작업 = [PLAN.md](PLAN.md) 맨 위 "사운드 데이터 테이블"(10-08, 키 엔트리 + 수치).
> 이번 작업은 그 위에 **인스펙터·Library Manager 가 하던 일(배열 크기·SoundID 연결·라이브러리/엔티티 생성·클립 구성)** 까지 xlsx 로 옮긴다.

## 목표
- 새 사운드 추가 = **xlsx `Sound` 시트에 행 작성 → `Tools/Data/사운드 동기화`** 두 단계로 끝난다. Library Manager·`SoundCatalog` 인스펙터는 열지 않는다.
- BGM 도 키 엔트리로 옮겨 사운드 호출 경로를 `AudioManager.Play*(key)` 하나로 통일한다.
- 기획 수치(볼륨·피치·랜덤·3D·재생 제한)는 **엔트리 한 곳**에서만 만진다. 엔티티는 "무엇을 어떻게 고를지(구성)"만 갖는다.

## 결정 (grill 2026-10-12~14)
| 항목 | 결정 |
|---|---|
| 방식 | **메뉴 수동 실행으로 디스크에 반영.** 테이블 Play·빌드의 "메모리 적용 후 되돌리기"에는 끼우지 않는다. 자동 쓰기 없음 |
| 라이브러리 | xlsx `library` 칸. **없으면 만든다**(BroAudio 출력 폴더 `Assets/51.Audio/Data/SoundLibrary` — 10-14 이전 전 `9.ScriptableObject/SoundLibrary`) |
| 엔티티 | xlsx `entity` 칸 = 엔티티 이름. **없으면 만든다**(`AudioEntity.CreateNewInstance` — Library Manager 와 같은 기본값). 프로젝트 전체에서 이름이 겹치면 오류 |
| 소속 변경 | 기존 엔티티의 `library` 가 실제와 다르면 **엔티티를 옮긴다**(`AssetDatabase.MoveAsset`, guid 유지 + 라이브러리 역참조 갱신) |
| 엔티티 공유 | **금지.** 두 키가 같은 엔티티를 가리키면 오류 |
| 고아 엔티티 | 어느 키도 안 쓰는 엔티티는 **경고만**(목록 표시). 삭제는 사람이 — `EffectPart` 등이 SoundID 로 직접 쓸 수 있다 |
| xlsx 엔티티 범위 | **구성만**: `library`·`entity`·`audioType`·`clipPaths`·`playMode`·`loop`. 볼륨·피치·랜덤·3D·재생 제한은 **엔트리 배율/수치**로만(엔티티는 생성 기본값 유지) |
| 클립 경로 | `clipPaths` = **프로젝트 기준 전체 경로**(`Assets/51.Audio/...`), 여러 개면 **셀 안 줄바꿈(Alt+Enter)**. `Assets/51.Audio/` 밖이면 오류 |
| 클립 반영 | **xlsx 가 원본 — 엔티티 `Clips` 를 통째로 교체.** 같은 클립이 남으면 그 원소의 Volume·Weight·Fade 등은 보존. `-` 면 클립을 건드리지 않는다 |
| 볼륨 랜덤 | 엔트리에 **`volumeRandomMin`/`volumeRandomMax` 추가**(피치 랜덤과 같은 방식, 기본 1/1 = 흔들지 않음) |
| 배열 크기 | **동기화가 `entries` 크기를 xlsx 에 맞춘다.** 중간 인덱스가 비면 오류, 줄어들면(끝 원소 삭제) 지워지는 키 경고 |
| BGM | `UIClick`·`TitleBGM`·`LobbyBGM`·`InGameBGM` 고정 필드를 **키 엔트리로 이전**하고 필드 삭제. 호출부는 키로 |

## 데이터 모양 — `SoundEntry`
기존 칸(`key`·배율·3D·재생 제한·`description`)은 그대로. 아래를 추가한다. `sound`(SoundID)는 계속 `[DataTableIgnore]` — **동기화만 쓴다.**

| 칸 | 형식 | 예 | 의미 |
|---|---|---|---|
| `library` | 텍스트 | `SFXLibrary` | 엔티티가 속할 라이브러리. 없으면 생성 |
| `entity` | 텍스트 | `Footstep` | 엔티티 이름. 없으면 생성 |
| `audioType` | 텍스트 | `SFX` | `Music`·`UI`·`Ambience`·`SFX`·`VoiceOver` |
| `clipPaths` | 텍스트(줄바꿈) | `Assets/51.Audio/SFX/Step_01.wav`⏎`Assets/51.Audio/SFX/Step_02.wav` | 클립 목록. `-` = 유지 |
| `playMode` | 텍스트 | `Random` | `Single`·`Sequence`·`Random`·`Shuffle`·`Velocity`·`Chained`. `-` = 유지 |
| `loop` | 정수 | `0`/`1`/`2` | 0 = 반복 없음, 1 = `Loop`, 2 = `SeamlessLoop`(현재 BGM 3종이 이 방식) |
| `volumeRandomMin`/`Max` | 실수 | `0.85`/`1` | 재생마다 볼륨에 추가로 곱할 랜덤 배율 범위 |

- 텍스트 칸은 `[DataTableText]`. 데이터 테이블은 bool·enum 을 내보내지 않으므로 `audioType`·`playMode` 는 텍스트, `loop` 는 정수.
- SO 에 남는 이 값들은 "마지막으로 동기화한 값"이다. 병합 Export 는 이걸로 행을 만든다.
- 최종 볼륨 = 엔티티 볼륨(1) × `volumeMultiplier` × Random(`volumeRandomMin`, `volumeRandomMax`).

## 접근
1. **`SoundCatalog`** — 위 칸 추가 · 고정 필드 4개 삭제. 기존 연결은 키 엔트리로: `UI_Click`=`LobbyLibrary/UI_Click_01`(있음), `BGM_Title`=`TitleLibrary/TitleBGM_01`, `BGM_Lobby`=`LobbyLibrary/LobbyBGM_01`, `BGM_InGame`=`IngameLibrary/IngameBGM_01`(모두 `loop`=2).
2. **`AudioManager`** — `PlayEntry` 에 볼륨 랜덤 적용. `PlayBGM(string key)` 추가(엔트리 SoundID + 볼륨 배율, 재생 제한 미사용). `PlayBGM(SoundID)` 는 유지.
3. **호출부** — `TitleSceneManager`·`LobbySceneManager`·`MapSceneManager` 를 `PlayBGM("BGM_…")` 로.
4. **동기화 계획(순수 클래스, 에디터)** — 입력: xlsx 의 `SoundCatalog` 행 + 현재 라이브러리/엔티티 목록 + 경로별 클립 존재 여부. 출력: 생성·이동·갱신·연결 목록 + 오류/경고. Unity API 없이 → **EditMode 테스트 대상.**
5. **동기화 실행(메뉴 `Tools/Data/사운드 동기화`)**
   - `XlsxReader`+`DataTableSchema.Parse` 로 `Sound` 시트를 읽어 4 로 계획.
   - **오류가 하나라도 있으면 아무것도 쓰지 않음**(데이터 테이블과 같은 원칙).
   - 실행 전 **변경 요약 다이얼로그**(새 라이브러리 N · 새 엔티티 N · 이동 N · 클립 변경 N · 연결 N · 경고).
   - 쓰기 순서: 라이브러리 생성(`AudioAsset` + `AssetName`·`AssetGUID`) → 엔티티 생성/이동 → 엔티티 구성(`Clips`·`MulticlipsPlayMode`·`AudioType`·`Loop`/`SeamlessLoop`) → `SoundCatalog.asset`(배열 크기·문자열·수치·`sound`) → Undo 등록 → `SaveAssets`.
   - BroAudio 패키지 코드는 고치지 않는다(public API + `SerializedProperty`).
6. **불일치 경고** — 테이블 모드 Play 시작 때 `entity` 문자열과 `sound` 의 엔티티 이름이 다르면 "사운드 동기화를 실행하세요" 경고(쓰지 않음). `Tools/Data/Verify` 에도 같은 검사.
7. **문서** — `Docs/tech/data-table.md` 사운드 항목을 새 절차로 교체 · `PLAN.md` 맨 위 리스크("배열 크기는 인스펙터에서") 갱신 · 다른 담당 영역(데이터 테이블·사운드 = 은희) 공유용 `Docs/tech/handoff-sound-sync.md`.
8. **xlsx** — 에이전트는 xlsx 를 쓰지 않는다. 사람이 SVN 잠금 → `Export Template` **병합**(새 칸 생성) → BGM 3행 추가 → 동기화 → 커밋.

## 오류 / 경고 규칙
| 상황 | 처리 |
|---|---|
| 같은 이름 엔티티가 프로젝트에 2개 이상 | 오류 |
| 두 키가 같은 엔티티 | 오류 |
| 라이브러리·엔티티 이름에 파일 이름으로 못 쓰는 문자 | 오류 |
| 클립 경로에 파일 없음 · AudioClip 아님 · `Assets/51.Audio/` 밖 | 오류 |
| `audioType`·`playMode` 이름 오타, `loop` 가 0~2 밖 | 오류 |
| `entries[i]` 인덱스가 중간에 빔 | 오류 |
| 새로 만드는 엔티티인데 `clipPaths` 가 `-` | 경고(빈 엔티티 생성 — 재생 안 됨) |
| `Single` 인데 클립 2개 이상 | 경고(첫 클립만 재생됨) |
| 배열이 줄어 키가 지워짐 | 경고(지워지는 키 목록) |
| 어느 키도 안 쓰는 엔티티 | 경고(목록만, 삭제 안 함) |
| 빈 칸 | 기존 데이터 테이블 규칙대로 오류 — 유지하려면 `-` |

## 리스크
- **xlsx 에 행을 추가하고 동기화 전에 테이블 Play 하면** 배열 범위 밖이라 데이터 테이블이 오류로 멈춘다(`DataTableApplier.Bind`). 오류 문구에 "사운드 동기화 먼저" 안내를 붙인다.
- **`51.Audio` .meta 가 SVN 에 없으면 PC 마다 guid 가 달라** 엔티티의 클립 참조가 다른 PC 에서 끊긴다(10-08 기록). 동기화는 경로로 찾으므로 다시 돌리면 고쳐지지만 git diff 가 생긴다 → 새 클립 .meta 는 반드시 SVN 커밋(문서에 명시).
- 결과물은 저장소가 나뉜다 — 라이브러리·엔티티·`SoundCatalog` = git, 클립·.meta·xlsx = SVN. 같이 올려야 한다.
- 열려 있는 Library Manager 창은 새 라이브러리·엔티티를 바로 못 볼 수 있다 → 동기화 후 창을 다시 연다(문서에 명시).
- 엔티티 이동은 파일 이동이다 — 동기화는 Unity 안에서 `AssetDatabase` 로 하므로 에디터를 끌 필요는 없다(CLAUDE.md 의 "에디터 밖 이동" 과 다름).
- 고정 필드 삭제는 호출부를 같은 커밋에서 바꿔야 컴파일된다. `SceneBgmSwitcher`(씬 배치 없음)·`EffectPart.sound` 는 이번 범위 밖 — SoundID 직접 연결 유지.

## 검증 (완료 조건)
- Refresh 후 컴파일 에러 0.
- EditMode: 동기화 계획 — 정상 · 라이브러리/엔티티 신규 생성 · 소속 이동 · 중복 엔티티 이름 · 엔티티 공유 · 경로 없음/`51.Audio` 밖 · 줄바꿈 다중 경로(빈 줄·공백·`\` 허용) · `-` 유지 · 이름 오타(audioType·playMode)·loop 범위 · 인덱스 구멍 · 배열 축소 경고 · 고아 엔티티 경고 · 기존 클립 설정 보존. 볼륨 랜덤 범위(min>max 뒤집힘). 기존 사운드 테스트(9개) 유지.
- 메뉴 실행: 새 라이브러리 + 클립 2개 `Random` 엔티티가 생성·연결됨 / 기존 엔티티 이동 시 guid 불변 / 오류 케이스에서 파일 변경 0 / `SoundCatalog.asset` diff 가 의도한 필드만.
- 사람 확인(Play): Title·Lobby·InGame BGM 정상(Seamless 반복) / `UI_Click` / Random 엔티티 반복 재생 시 클립·볼륨이 섞여 나옴 / Library Manager 에 새 라이브러리·엔티티가 보임.

## 작업 순서
S1 순수 계획 클래스 + 테스트 → S2 메뉴(생성·이동·쓰기) → S3 `SoundCatalog`·`AudioManager`(볼륨 랜덤·BGM 키)·호출부 → S4 불일치 경고 → S5 문서. (xlsx 병합 Export·SVN 커밋은 사람)

## 진행 (2026-10-14)
- ✅ S1~S5. EditMode 93/93(SoundSyncPlannerTests 22). 실제 에셋 엔드투엔드 18/18(임시 스크립트, 결과물 정리함).
- 추가 결정(구현 중): **시트 전체에 구성 칸이 하나도 없으면 오류** — 병합 Export 전 옛 시트로 돌리면 배열이 줄어 BGM 키가 지워지는 사고를 막는다.
- 새 원소의 재생 수치는 SO 에 기본값으로 들어간다(테이블 모드·빌드는 xlsx 값).
- ✅ xlsx `Sound` 시트 새 구성 작성(병합 Export 결과에서 Sound 시트만 원본에 이식 — 전체 병합은 이 브랜치에 없는 다른 시트 필드 20개를 버려서 쓰지 않음). 로드 오류 0 · 동기화 "바꿀 것 없음".
- ⏳ 사람: xlsx SVN 커밋 → Play 청취. 인수인계 [handoff-sound-sync.md](Docs/tech/handoff-sound-sync.md).

## 이전 (2026-10-14) — 사운드 데이터를 SVN 으로
- `SoundCatalog.asset` → `Assets/51.Audio/Data/SoundCatalog.asset`, `SoundLibrary/` → `Assets/51.Audio/Data/SoundLibrary/`(Unity 끈 채 이동, 24개 파일·.meta guid 바이트 동일). `SoundCatalog.cs` 는 git 에 남겨 `Assets/1.Scripts/Sound/` 로.
- BroAudio 출력 폴더 `BroEditorSetting.asset` `AssetOutputPath` 를 새 경로로 — 안 바꾸면 Library Manager·사운드 동기화가 옛 위치(git 영역)에 다시 만든다.
- 목적: xlsx·클립·엔티티·`SoundCatalog` 를 한 SVN 커밋으로 묶어 "PC 마다 엔티티가 따로 생기는" 위험을 없앤다. Play·빌드 자동 동기화는 아직 결정 전(검사만 자동 = 권장안 A).
- 주의: SVN 은 git 브랜치와 따로 간다 — `SoundEntry` 구조(코드)를 바꾸는 작업은 development 반영 뒤 SVN 커밋.
