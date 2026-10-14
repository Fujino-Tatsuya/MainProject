# 인수인계 — 사운드 동기화 (2026-10-14 · SimJangBounce(Claude) · 브랜치 `feature/SFX`)

> 사운드·데이터 테이블 담당(은희)에게 공유. 계획·결정 원본 = [PLAN-sound-sync.md](../../PLAN-sound-sync.md), 쓰는 법 = [data-table.md](data-table.md) §6 "사운드 키".

## 무엇이 바뀌었나
- **새 메뉴 `Tools/Data/사운드 동기화`** — xlsx `Sound` 시트를 읽어 BroAudio 라이브러리·엔티티를 만들거나 옮기고, 클립·재생 모드·AudioType·반복을 반영하고, `SoundCatalog.entries` 크기와 SoundID 연결까지 디스크에 쓴다. 오류가 하나라도 있으면 아무것도 쓰지 않는다.
  - 판정 = `1.Scripts/Sound/Editor/SoundSyncPlanner.cs`(순수, 테스트 22개), 쓰기 = `SoundSyncMenu.cs`.
  - BroAudio 패키지 코드는 고치지 않았다(public `AudioEntity.CreateNewInstance` + `SerializedProperty`).
- **`SoundEntry` 새 칸**: `library`·`entity`·`audioType`·`clipPaths`·`playMode`(텍스트) · `loop`(0/1/2) · `volumeRandomMin/Max`.
- **고정 필드 삭제**: `SoundCatalog.UIClick`·`TitleBGM`·`LobbyBGM`·`InGameBGM` → 키 `UI_Click`·`BGM_Title`·`BGM_Lobby`·`BGM_InGame`. 호출은 `AudioManager.PlayBGM(string key)`(새 오버로드, 볼륨 배율 적용·재생 제한 미사용). `PlayBGM(SoundID)` 는 남겼다.
- **`AudioManager.PlayEntry`** 가 볼륨에 `volumeRandomMin~Max` 랜덤 배율을 곱한다.
- **데이터 테이블 코드 2줄**: `DataSourcePlayMode.PrepareTablePlay`·`DataTableMenu.Verify` 가 `SoundSyncMenu.WarnIfPending(...)` 을 부른다 — xlsx 와 에셋이 어긋났으면 경고만(쓰지 않음, 예외는 삼킨다).

## xlsx — `Sound` 시트만 새 구성으로 교체함 (로컬, 🔴 SVN 미커밋)
- 사용자 요청으로 에이전트가 `GameData.xlsx` 를 고쳤다(2026-10-14). 병합 Export 를 배치 모드로 돌린 뒤 **`Sound` 시트(`sheet7.xml`)만 원본 xlsx 에 이식**했다 — 나머지 9개 시트는 원본과 바이트 동일.
  - 전체 병합 결과를 그대로 쓰지 않은 이유: 이 브랜치 코드에 없는 필드 값 20개(Player·Gunner·Monster·MidBoss — 다른 브랜치의 새 필드)가 버려졌기 때문.
  - `Sound` 시트 = `UI_Click`(기존 값 유지) + `BGM_Title`·`BGM_Lobby`·`BGM_InGame`, 새 칸(library·entity·audioType·clipPaths·playMode·loop·volumeRandomMin/Max) 포함. 로드 오류 0 · 동기화 "바꿀 것 없음".
  - 원본 백업: `Library/DataTableExportBackup/GameData_20261014_111745.xlsx`.
- ⚠️ 이 브랜치에서 테이블 Load 시 다른 시트 오류 10개는 **원래 있던 것**(브랜치가 development 보다 뒤처져 필드가 없음) — development 를 받으면 사라진다.

## 사운드 데이터 SVN 이전 (2026-10-14)
- `SoundCatalog.asset`·`SoundLibrary/`(라이브러리·엔티티) → `Assets/51.Audio/Data/`(SVN). `SoundCatalog.cs` → `Assets/1.Scripts/Sound/`(git). 모든 .meta guid 유지.
- BroAudio 출력 폴더 = `Assets/51.Audio/Data/SoundLibrary`(`BroEditorSetting.asset`, git).
- 🔴 **git pull 만 하고 SVN 업데이트를 안 하면** 옛 위치 파일이 사라져 `AudioManager` 의 카탈로그·BGM 이 비어 소리가 안 난다 → SVN 업데이트 필수.
- 🔴 SVN 에 `Assets/51.Audio/Data` 와 `Data.meta` 를 **add + 커밋**해야 다른 PC 에 간다(에이전트 PC 에는 svn 명령줄이 없어 미실행).

## Play·빌드 자동 동기화 (2026-10-14, 결정 C)
- `DataSourcePlayMode.PrepareTablePlay` 맨 앞 · `DataTableBuild.WithTableValues` 맨 앞에서 `SoundSyncMenu.AutoSync` — 확인 없이 디스크에 쓰고 원복하지 않는다. 오류면 Play 취소·빌드 실패.
- 테이블 모드에서만(인스펙터 모드는 xlsx 를 무시). MPPM 클론(`Library/VP/`)은 건너뛴다. xlsx 가 없으면 건너뛴다. 바꿀 것이 없으면 로그도 없다.
- Verify 는 계속 읽기 전용 경고(`WarnIfPending`).

## 🔴 다음에 사람이 할 일
1. SVN Get Lock → 로컬 `GameData.xlsx` 커밋(Sound 시트만 바뀜).
2. `Tools/Data/사운드 동기화` → "바꿀 구조 없음" 이 나오면 정상.
3. Play 확인: Title·Lobby·InGame BGM(Seamless 반복) · `UI_Click`.

## 알아 둘 것
- 동기화 경고 "어느 키도 쓰지 않는 엔티티" 에 지금 `LobbyLibrary/Slash_01`(클립 파일 없음)이 나온다 — 삭제하지 않는다.
- 새 클립 `.meta` 는 SVN 에 같이 커밋해야 다른 PC 에서 클립 참조가 안 끊긴다(10-08 기록과 같은 문제).
- `EffectPart.sound`·`SceneBgmSwitcher` 는 범위 밖 — SoundID 직접 연결 그대로.

## 검증
- EditMode 93/93(데이터 테이블 + 사운드, Unity 6000.3.16f1 배치 모드).
- 실제 에셋에서 임시 스크립트로 엔드투엔드 18항목 통과 후 정리: 같은 행 → `SoundCatalog.asset` 바이트 동일 / 새 라이브러리·엔티티(클립 2개·Random·Loop) 생성·연결 / 새 원소 수치 기본값 / 라이브러리 이동 guid 유지·역참조 갱신 / 클립 교체 시 남은 클립 Volume 보존 / 엔티티 공유 오류 시 변경 0 / 축소.
- ⏳ 에디터 GUI 메뉴·확인 대화상자, Play 청취는 미확인.
