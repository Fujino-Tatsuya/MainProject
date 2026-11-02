# handoff — 메인 1~4구역 무전 · 2구역 적 소개 연출 (2026-11-02 · 경석(Claude))

> 계획·결정 = [PLAN-main-radio-zone1-4.md](../../PLAN-main-radio-zone1-4.md) · 기획 = `Re_C_메인1~4구역_무전_카메라연출_개발명세_v01_20261026.md`(김태현) §4.1~4.4 · §4.9~4.10 · §5.
> 4구역 소개 연출(§4.5~4.8)은 [handoff-zone4-intro-cinematic.md](handoff-zone4-intro-cinematic.md). 이번 판은 그 틀을 2구역에 재사용하고 나머지 무전을 붙였다.

## 무엇이 바뀌나 (플레이 기준)
1. **1구역**: **로딩이 걷히고 포드 착지 연출이 끝난 뒤**(조작 복귀 — `MapPodArrival.IsArrivalDoneLocal` ∧ `GameManager.IsMainGameReady`) 1초 뒤 무전 "들려? 좋아, 무사히 도착했네. / 여기가 그 시설이야." — 이동 안 막음. 포드가 없는 판(Dev Boot·재시작)은 로딩이 걷히면 바로. 30초 안에 신호가 안 오면 그냥 낸다. **G1(존1 → 복도1)은 이 무전의 마지막 조각이 꺼진 뒤 열린다**(경석 11-02 Play — 전엔 활성 순간 열려 대사를 안 듣고 지나갔다). 끝 시각 = 무전 시작 + 호스트 표시와 같은 조각 수 × `MainRadioLines` 시간(`MapSceneRadioPresentation.TryGetLineSeconds`), 못 재면 무전 시작 15초 뒤. 구간 스킵이 먼저 열면 그대로 둔다. ⚠️ 정정(경석 11-02 Play): 처음엔 G1 순간(활성) 1.5초 뒤였는데 포드 착지 연출(~10초) 중에 끝나 아무것도 안 보였다.
2. **2구역 진입 전**: 복도1 특성 선택이 끝나도 **G2 가 바로 안 열린다.** 살아 있는 전원이 복도1 에 모이면 조작 잠금·무적·HUD 숨김 → 카메라가 존2 적 무리 → **샷 도착 0.4초 뒤** "잠깐, 저 녀석들 우리를 발견했어. / 조심해." → 복귀 1초 → **복귀가 끝나는 순간 G2 열림 + 존2 전체 기상**(예전 "첫 진입 4초 뒤 기상" 없음). 스킵 = 4구역과 같은 Space 1초 홀드 · 전원 동의 → 그 자리에서 0.8초 복귀.
2-1. 2구역 샷에서 카메라 쪽 존 벽(왼쪽 아래)이 일부 가리는 것은 **그대로 둔다**(경석 11-02 — 벽 투명화·연출 중 벽 숨김을 시도했다가 전부 뺐다). 참고: 벽 투명화 `WallOcclusionDriver` 는 `MapGenerator.prefab` 에서 꺼져 있고(`m_Enabled: 0`) 켜져 있어도 Stage1 벽만 대상이라 존 프리팹 벽엔 안 걸린다.
3. **2구역 클리어**(장부 = G3 를 여는 사건) → "괜찮아? 아직 안쪽은 확인이 안 돼. / 조금만 더 신중하게 가자."
4. **3구역**: G4 열린 뒤 살아 있는 플레이어 누군가와 존3 핵심 몬스터(스피너봇)의 수평 거리 ≤ 14m(같은 층, 높이 차 4m 안)인 첫 순간 → "저 녀석은 다른 놈들보다 좀 강해 보이는데." 강제 카메라·잠금 없음.
5. **3구역 클리어**(G6) → "잠깐. 다음 구역은 좀 살펴봐야겠어."
6. **4구역**: 다리 개통 완료(진행도 1) → "중앙으로 가는 다리가 연결됐어. / 어서 그쪽으로 가봐."(옛 자막 `TUT_Z4_BRIDGE` 대체) · 중간보스 처치 뒤 **G9 가 실제로 열린 순간** → "문이 열렸어. / 역시 저 녀석 때문이었나 봐."(옛 자막 `TUT_Z4_CLEAR`·`TUT_Z4_GO_BOSS` 삭제 — GO_BOSS 는 목표 줄로만 남음).
7. 모든 무전 = 4구역과 같은 **무전 캡슐**(한 줄씩, `|` 에서 끊음). 같은 무전은 판마다 한 번. 나중 줄이 앞 줄을 대신한다(대기 줄 없음). 2·4구역 소개가 돌면 숨기고, 줄이 시작된 뒤 소개가 시작되면 그 줄은 버린다. 늦게 붙은 클라는 표시 시간 안인 줄만 본다.

## 파일
| 파일 | 내용 |
|---|---|
| 새 `Map/MapScene/MapSceneDirector.Radio.cs` | 복제 `RadioState`(id·시작 ServerTime) · `PlayRadioServer(id)`(판마다 1회) |
| 새 `Map/MapScene/MapSceneDirector.Zone2Intro.cs` | 복제 `Zone2IntroState`(단계·회차·복귀·스킵·적 무리 중심/반경) · 스킵 RPC · `TryGetAreaRectServer` |
| 새 `Flow/MapSceneRadio{Data,Rules,Presentation,CaptionDriver}.cs` | 무전 시간 SO(xlsx `Radio` 시트) · 순수 규칙 · 모든 피어 표시 · 캡슐 한 줄 그리기(2구역과 공용) |
| 새 `Flow/MapSceneZone2Intro{Data,Rules,Flow,Presentation}.cs` | 2구역 연출(서버 진행·모든 피어 표시). 시간 = xlsx `Cinematic` 시트(4구역과 같은 시트, Order 1) |
| 새 `Flow/MapSceneAreaIntroServer.cs` | 2·4구역 공용 서버 몫 — 잠금·스킵 표·"살아 있는 전원이 그 구간" |
| `Flow/MapSceneZone4IntroFlow.cs` | 공용 도우미 사용(동작 동일). ⚠️ 하나 달라짐: **이미 다른 계통이 잠근 사람은 잠그지도 풀지도 않는다**(명세 §7 — 로비 `LobbyBriefingLockLedger` 와 같은 규칙) |
| `MapSceneDirector.Zone4Intro.cs` · `MapSceneZone4IntroPresentation.cs` | 단계 enum 이름만 `Zone4IntroPhase` → `IntroPhase`(2·4구역 공용) |
| `Flow/MapSceneZoneFlow.cs` | 1구역 무전 · 연출 켠 판은 존2 기상을 연출 흐름에 넘김 · 존3 거리 판정 · 존2/3 클리어 무전 |
| `Flow/MapSceneBossApproachGuide.cs` | 다리·출구 무전(위 6) |
| `MapSceneDirector.cs` | 표시 컴포넌트 2개 부착(2줄) |
| `Tutorial/Guide/TutorialGuideIds.cs` · `TutorialGuideTexts.asset` | 행 7개 `TUT_MAIN_Z0*`·`TUT_Z4_BRIDGE_OPEN`·`TUT_Z4_EXIT_OPEN` |
| 새 에셋 `Resources/MapScene/Zone2IntroCinematic.asset` · `MainRadioLines.asset` | 시작값(아래) |
| 테스트 `Map/MapScene/Editor/MapSceneRadioRulesTests.cs` | 맵씬 테스트 메뉴에 추가(+ `CinematicRulesTests`) |

씬·프리팹 수정 없음. 디렉터 NetworkVariable 2개 추가 — **호스트·클라 같은 빌드 필수**.

## 데이터 — 기획이 고치는 곳
- **문구** = xlsx `Strings` 시트 `Tutorial.Guide.TUT_MAIN_Z0*`·`TUT_Z4_BRIDGE_OPEN`·`TUT_Z4_EXIT_OPEN`(✅ 11-02 추가 — ko = 명세 문구, en = Claude 초안 `[EN 초안·임시 — 검수 필요]`) → `StringTable.asset` 갱신(7키). `|` = 그 자리에서 다음 조각.
- **시간** = `MainRadioLines`(사건 → 시작 지연 · 조각 표시 · 간격 · 3구역 거리) / `Zone2IntroCinematic`(진입 1.2 · 머묾 5.4 · 경고 지연 0.4 · 조각 2.2 · 간격 0.2 · 복귀 1.0 · 스킵 복귀 0.8). ✅ 11-02 병합 Export — `Cinematic` 시트에 `Zone2IntroCinematic` 구역(10칸), 새 `Radio` 시트 `MainRadioLines`(22칸). 기존 값 변경·삭제 0(원본 대비 비교). SVN **r574**(잠금 해제됨) · 아트 핀 572 → 574.
- 🔴 2구역 머묾을 줄이면 경고가 복귀 전에 안 끝날 수 있다 — 테스트 `기본값_2구역_경고는_…` 는 **코드 기본값**만 본다. 에셋을 바꾸면 Play 로 확인.
- **구도** = `Zone2IntroCinematic` 인스펙터(각 55° · FOV 40 · 여유 1.3 · 거리 14~40m · 높이 +1m). 임시값 — 기획 확인 뒤 조정.

## 알려진 한계 / 확인 대기
- 존2 적은 연출 동안 휴면 그대로다(명세 — "발견" 표현용 새 애니메이션 없음). 복귀 끝에 일제히 깬다.
- 3구역 거리 판정은 서버 거리 근사(카메라 화면 판정 아님). 14m 는 Play 로 맞출 것.
- 연출 중 재합류한 클라는 잠금이 안 걸린다(4구역과 같은 한계).
- Play·MPPM 미확인.

## Play 체크리스트
1. 1인: 시작 → 포드 착지·조작 복귀 1초 뒤 도착 무전(이동 가능) → 복도1 특성 → 2구역 연출(잠금·HUD 숨김·적 무리 → 경고 → 복귀) → 복귀 끝에 G2 열림·존2 기상 → 클리어 무전.
2. 3구역: G4 지나 스피너봇에 다가가면 상대 무전 1번 → 클리어 무전 → 4구역 연출(기존) → 다리 연결 무전 → 중간보스 처치 → G9 열림과 함께 문 개방 무전. 옛 자막("다리가 열렸습니다"·"길이 열렸습니다"·"복도에서 마지막 특성…")이 안 나와야 한다.
3. 2구역 스킵: Space 1초 → 0.8초 복귀 → G2·기상. 구간 스킵 Alt+2 이상: 연출 없이 G2 열림 + 존2 기상.
4. MPPM: 같은 샷·같은 무전 타이밍 · 중복 재생 없음 · 전원 동의해야 스킵 · 한 명이 늦게 복도1 에 오면 그때 시작.
