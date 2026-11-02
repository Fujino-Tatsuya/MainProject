# PLAN — 메인 1~4구역 무전·2구역 적 소개 연출 (4구역 소개 연출 다음 판)

> 상태: ✅ 경석 승인(2026-11-02) → ✅ 구현(미커밋) · 컴파일 0 · ✅ xlsx 병합 Export(Cinematic 2구역·Radio·Strings 7행) SVN r574 · ⏳ Play·MPPM. 공유: [Docs/tech/handoff-main-radio-zone1-4.md](Docs/tech/handoff-main-radio-zone1-4.md). 브랜치 `feature/SettingsDdol`(현재 브랜치 그대로).
> 기획 원본: `Re_C_메인1~4구역_무전_카메라연출_개발명세_v01_20261026.md`(김태현, 10-26) — 4구역 소개(§4.5~4.8)는 [PLAN-zone4-intro-cinematic.md](PLAN-zone4-intro-cinematic.md) 에서 끝났다. 이번 판 = 나머지 전부(§4.1~4.4 · §4.9~4.10).
> 이전 판 D1 "1~3구역 무전은 다음" → 이번에 넣는다(사용자 11-02 — "시나리오 확정, 전부 넣고 몬스터 푸는 타이밍과 텍스트 시간 맞춰서").

## 1. 목표
1구역 도착부터 4구역 출구까지 명세 §5 무전 10줄이 정해진 사건에 한 번씩 나오고, 2구역은 진입 전에 적을 먼저 비춘 뒤 경고 → 복귀 순간 문 열림·몬스터 기상으로 바로 첫 전투가 시작된다.

## 2. 확정 사항 (사용자 11-02 질의)
| # | 항목 | 결정 |
|---|---|---|
| R1 | 2구역 시작 | **4구역 방식 그대로** — 복도1 특성 전원 완료(60초 교착 방지 포함) ∧ 살아 있는 전원 복도1 → G2 보류 → 존2 적 무리 샷 → 경고 무전 → 복귀 끝나는 순간 G2 열림 + 존2 **즉시 기상**(지금의 첫 진입 4초 지연 없앰) |
| R2 | 2구역 HUD·스킵 | **4구역과 동일** — 조작 잠금·무적, 개인 HUD·목표 패널 숨김, 무전 캡션 유지, 레터박스, Space 1초 홀드 전원 동의 스킵(스킵 = 지금 자리에서 바로 복귀) |
| R3 | 3구역 상대 무전 | 대상 = 존3 핵심 몬스터(스피너봇). **살아 있는 플레이어 누군가와 거리 ≤ 화면 거리(인스펙터)** 가 된 첫 순간 1회. 강제 카메라·잠금 없음 |
| R4 | 일반 무전 표시 | **4구역 무전 캡슐**(무전기 배지 + 한 줄, 조각 나눔·등장 움직임) 하나로 통일. 자막 상자 안 씀 |

## 3. 사건 → 무전 매핑 (코드 실측 기준)
| 명세 ID | 재생 사건(서버) | 문구 id(자막 표 새 행) | 비고 |
|---|---|---|---|
| MAIN_Z01_ENTER | 로딩 걷힘 ∧ 포드 착지 끝(조작 복귀) + 지연 1초 (⚠️ 정정 11-02 — G1 순간에 틀면 착지 연출 중에 끝났다) | `TUT_MAIN_Z01_ENTER` | 이동 막지 않음 |
| MAIN_Z02_ENTER | 2구역 소개 샷 도착 + 지연 | `TUT_MAIN_Z02_ENTER` | 소개 연출 대사 |
| MAIN_Z02_CLEAR | `ZoneCleared(Zone2)`(G3 를 여는 장부) | `TUT_MAIN_Z02_CLEAR` | |
| MAIN_Z03_ENEMY | R3 거리 판정 첫 순간 | `TUT_MAIN_Z03_ENEMY` | |
| MAIN_Z03_CLEAR | `ZoneCleared(Zone3)`(G6) | `TUT_MAIN_Z03_CLEAR` | |
| Z04_INTRO_* | (완료) | `TUT_Z4_INTRO_*` | 변경 없음 |
| Z04_BRIDGE_OPEN | `ZoneBridgeGateManager.ServerBridgeOpened`(열림 진행도 1 = 실제 통행 가능) | `TUT_Z4_BRIDGE_OPEN` | 지금 `TUT_Z4_BRIDGE` 자막 호출을 **대체**. 중간보스 기상은 같은 순간 그대로 |
| Z04_EXIT_OPEN | `GateOpened(G9)`(문이 실제로 열림) | `TUT_Z4_EXIT_OPEN` | 지금 `ZoneCleared(Zone4)` 의 `TUT_Z4_CLEAR`+`TUT_Z4_GO_BOSS` 자막을 **대체**(GO_BOSS 는 특성 언급 — 명세 금지) |

## 4. 접근
### 4.1 일반 무전(서버 → 모든 피어)
- 디렉터 partial 새 파일 `MapSceneDirector.Radio.cs`: 복제 값 `RadioState { LineId(FixedString64), StartServerTime, Sequence }` 1개 + `PlayRadioServer(id)`.
  - 회차·사건별 1회 = 서버가 이번 판에 낸 id 집합으로 막는다(영역 재진입·중복 이벤트 무시). 판 재시작 시 초기화.
  - **나중 줄이 앞 줄을 대신한다**(대기 줄 없음 — 명세 §8.1 "의미 없어진 대사 폐기"). 재접속 클라는 아직 표시 시간 안인 줄만 본다(과거 줄 재생 없음).
- 표시 `MapSceneRadioPresentation`(모든 피어, 디렉터에 부착): `CinematicRadioCaption` 재사용, 캡슐 모양 값은 `Zone4IntroCinematic` 에셋 것을 그대로 쓴다(한 곳에서 조절). 2·4구역 소개가 돌고 있으면 숨기고, 소개 시작 전에 시작한 줄은 버린다(§8.1 강제 소개 시 정리).
- 줄마다 시간(지연·조각 표시·간격) = 새 SO `Resources/MapScene/MainRadioLines.asset`(xlsx 시트 `Radio` 대상 — 병합 Export 는 4구역 `Cinematic` 시트와 같이, SVN 잠금 뒤).

### 4.2 2구역 적 소개 연출
- 4구역 흐름에서 **공통 부분을 뽑는다**: 잠금(`PlayerEncounterLock`)·스킵 투표·"살아 있는 전원 그 구간" 판정 → 새 `MapSceneAreaIntroServer`(순수 서버 도우미). 4구역 흐름은 이걸 쓰도록 바꾸되 **동작 무변경**.
- 새 `MapSceneZone2IntroFlow`(서버) · `MapSceneZone2IntroPresentation`(모든 피어) · `MapSceneZone2IntroData`(SO `Resources/MapScene/Zone2IntroCinematic.asset`, xlsx `Cinematic` 시트에 같이) · 디렉터 partial `MapSceneDirector.Zone2Intro.cs`(복제 상태 + 스킵 RPC).
- 구도: 연출 시작 때 **서버가** 존2 장부 몬스터 위치로 중심·반경을 재서 복제 상태에 넣는다(클라마다 몬스터 목록이 달라도 같은 화면) → 자동 맞춤 거리(4구역 다리 샷과 같은 `CinematicFraming.FitDistance`).
- 타임라인: 샷 1개(게임 카메라 → 적 무리 블렌드 `moveSeconds` → 머묾 `holdSeconds`) · 경고 대사 = **샷 도착 뒤** `delaySeconds`(명세 §6.3 적 구도 도착 → 경고 순서 보장) · 머묾 ≥ 대사 길이 + 여유가 되게 기본값을 잡는다 · 복귀 블렌드.
- **복귀 블렌드가 끝나는 그 프레임**(서버): 잠금 해제 → G2 보류 해제(열림) → 존2 전체 기상. `MapSceneZoneFlow` 의 "첫 진입 4초 뒤 기상"은 연출이 켜진 판에서 끈다(연출을 끄면 예전 그대로).
- QA 구간 스킵(Alt+2 이상, G2 를 지나는 것)은 4구역과 같이 연출 없이 보류만 푼다. 연출 중 G2 가 다른 길로 열리면 정리.

### 4.3 3구역
- `MapSceneZoneFlow`(서버) 에 스피너봇(존3 KeyOnly) 거리 판정 → `PlayRadioServer(TUT_MAIN_Z03_ENEMY)`. 거리 = 인스펙터(기본 14m — 게임 카메라 화면 세로 절반 정도, Play 로 맞춤). 기상 전이어도 보이면 낸다(공격 준비·판정 창과 무관 — 명세 §4.4).

### 4.4 4구역 §4.9~4.10
- `MapSceneBossApproachGuide`: 다리 개통 → `TUT_Z4_BRIDGE` 자막 대신 무전 `TUT_Z4_BRIDGE_OPEN`. 존4 클리어 자막(`TUT_Z4_CLEAR`·`TUT_Z4_GO_BOSS`) 삭제 → `GateOpened(G9)` 에 무전 `TUT_Z4_EXIT_OPEN`.
- 그대로 두는 것: 복귀 뒤 설정 자막 `TUT_Z4_LORE`·`TUT_Z4_TIMER_HINT`(이전 판 D7), 목표 패널 `TUT_Z4_GO_BOSS`(무전이 아닌 목표 줄), 타이머 만료·패드 자막.

## 5. 시간 맞춤 (시작값 — 전부 데이터, Play 로 조정)
| 구간 | 시작값 |
|---|---|
| 1구역 도착 무전 | 활성 1.5초 뒤 · 조각 2.6초 · 간격 0.25 (2조각) |
| 2구역 샷 | 진입 블렌드 1.2초 → 머묾 5.4초(대사 0.4초 뒤 시작, 조각 2.2초 ×2 + 간격 0.2 = 4.6초, 끝난 뒤 0.4초 여유) → 복귀 1.0초 = 총 7.6초 · 복귀 끝 = G2 열림·기상 · 스킵 복귀 0.8초 |
| ⚠️ 정정(구현 11-02) | 처음 잡은 6.4초(머묾 4.2·조각 2.0)는 대사 끝(5.8초)이 복귀 시작(5.4초)보다 늦었다 → 위 값으로. 테스트가 코드 기본값을 검사한다 |
| 2구역 종료 / 3구역 상대 / 3구역 종료 | 사건 0.3초 뒤 · 조각 2.6초 · 간격 0.25 |
| 다리 연결 / 문 개방 | 사건 0.3초 뒤 · 조각 2.4초 · 간격 0.25 |
- 조각 표시 시간 기준: 한국어 낭독 ≈ 초당 7~8자 + 0.6초. 2구역 머묾은 "경고 대사가 다 끝난 뒤 0.4초" 에 복귀가 시작되게 데이터에서 계산 검증(EditMode 테스트 — 대사 끝 ≤ 복귀 시작).

## 6. 수정 파일
- 새: `Map/MapScene/MapSceneDirector.Radio.cs` · `MapSceneDirector.Zone2Intro.cs` · `Map/MapScene/Flow/MapSceneRadioPresentation.cs` · `MapSceneRadioData.cs` · `MapSceneAreaIntroServer.cs` · `MapSceneZone2Intro{Flow,Presentation,Data}.cs` · 에셋 `Resources/MapScene/MainRadioLines.asset`·`Zone2IntroCinematic.asset` · 테스트.
- 수정: `MapSceneZone4IntroFlow.cs`(공통 도우미 사용 — 동작 무변경) · `MapSceneZoneFlow.cs`(1구역 무전·존2 기상 조건·3구역 판정·클리어 무전) · `MapSceneBossApproachGuide.cs`(4.4) · `MapSceneDirector.cs`(표시 컴포넌트 부착 1줄) · `MapSceneSkipController.cs`(G2 통과 판정 재사용 확인만) · `TutorialGuideIds.cs`·`TutorialGuideTexts.asset`(행 7).
- 씬·프리팹 수정 없음(4구역처럼 런타임 부착 + 디렉터 partial NetworkVariable — 호스트·클라 같은 빌드 필수).

## 7. 위험·열린 것
- 2구역 샷 구도·3구역 거리값은 임시 — Play 로 대충 맞추고 기획 확인.
- xlsx `Cinematic`·`Radio` 시트 Export 는 SVN 잠금 필요(4구역 것과 같이 대기).
- 디렉터 NetworkVariable 2개 추가 — 옛 빌드 클라와 섞이면 안 된다.
- 4구역 흐름 리팩터링 회귀 → 4구역 Play 체크리스트 1~3 재확인.

## 8. 완료 조건
- 컴파일 0 · 전투/튜토리얼 EditMode 통과(새 규칙 테스트: 무전 1회 막기·나중 줄 대체·2구역 대사 끝 ≤ 복귀 시작).
- Play 1인: 명세 §11.1 체크리스트 전부(1구역 이동 중 무전 · 2구역 잠금 → 적 → 경고 → 복귀 끝 G2·기상 · 2/3구역 클리어 · 스피너봇 보일 때 · 다리 완료 · G9 열림).
- MPPM: 같은 타이밍 · 중복 재생 없음 · 2구역 전원 동의 스킵.
