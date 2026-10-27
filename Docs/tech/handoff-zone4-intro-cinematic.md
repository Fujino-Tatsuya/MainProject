# handoff — 튜토리얼 4구역 진입 전 소개 연출 (2026-10-26 · 경석(Claude))

> 계획·결정 = [PLAN-zone4-intro-cinematic.md](../../PLAN-zone4-intro-cinematic.md) · 기획 = SVN r520(김태현) `2_Documents/20_Design/ReC/연출/Re_C_메인1~4구역_무전_카메라연출_개발명세_v01_20261026.md` §4.5~4.8.
> 튜토리얼(은희 영역)을 경석 지시(10-26 — "4구역 흐름은 기획 문서대로 바뀌기로 해서 수정해도 됨")로 고쳤다. 공유용.

## 무엇이 바뀌나 (플레이 기준)
1. 복도3 특성 선택이 끝나도 **G7(존4 입구)이 바로 안 열린다.** 살아 있는 전원이 복도3 에 있으면 연출이 나온다.
2. 연출: 조작 잠금·무적, HUD 숨김(**안내 자막은 남고 목표 패널·강조만 숨김**), 카메라 패널 4곳(화면 왼쪽 아래부터 시계 방향) → 끊어진 다리·중앙 섬 → 플레이어 카메라 복귀. 무전 캡션(무전기 그림 + 한 줄)으로 대사 2줄.
3. 스킵 = 보스 등장 연출과 같은 Space 1초 홀드 · 접속자 **전원**(관전자 포함) 동의 → **보던 자리에서 다리·중앙 섬 샷(중간보스)으로 0.8초 옮겨 가 1.2초 머문 뒤** 1초 복귀(경석 10-27 — 바로 플레이어로 가지 말고 중간보스를 한 번 보여 준다). 이미 그 샷이면 바로 복귀. 시간 = `skipToBossSeconds`·`skipBossHoldSeconds`·`skipReturnSeconds`.
3-1. 🔴 연출 동안 **층·시야 디밍(전장의 안개)을 줄인다**(경석 10-27 — 플레이어에게서 먼 존4 를 비추면 화면이 통째로 어두웠다): 디밍 기준점 = 카메라가 보는 점(`FogManager.dimPlayerOverride`), 세기 = 첫 블렌드 동안 0 → `dimSuppress`(인스펙터, 기본 1 = 안 누름), 복귀 블렌드 동안 0 으로. `FogManager.dimSuppress`(런타임 전용·저장 안 됨) 추가 — 프로필 에셋은 안 바꾼다. 같은 비율을 **시야 차폐(LoS — 맵 FogManager 프리팹 `losEnabled: 1`)** 에도 적용(LoS 는 기준점 한 곳에서만 레이를 쏴서, 섬 기준이면 다리 조각이 벽 너머로 어두웠다 — 경석 10-27).
3-2. 패널 샷 = 패널 메시 중심이 화면 한가운데(`panelTowardCenter` 0 · `panelFocusHeight` 0) + 그 패널에 근접 하이라이트와 같은 외곽선(`panelOutline`). 다리 샷 = 다리 조각 전부 + 존 중앙 범위를 화면 가운데·자동 거리(`bridgeAutoFit`, 여유 `bridgeFitMargin` 1.1 → 실측 42m) + 고정 다리 외곽선(`bridgeOutline`·`bridgeOutlineNames` 기본 `floor_bridge_001/002`, `ZoneBridgeGate.SetBridgeHighlighted`). 이동식 다리(`floor_MV_bridge_*`)는 끊긴 상태에서 고정 다리 안에 반쯤 묻혀 둘 다 외곽선을 그리면 서로 가려 끊겨 보였다(경석 10-27) → 뺐다. 다리 외곽선은 멀리서 비춰 흐려서 공용 머티리얼(`MA_ZoneInteractOutline`, 두께 2.5)을 **런타임 복제해 두께 7 로 그렸다(⚠️ 아래 정정으로 폐기). ⚠️ 정정(경석 10-27 Play): 두껍게 해도 근접 외곽선(반전 헐 + 깊이 판정)은 바닥·벽 홈에 붙은 다리에서 테두리가 묻혀 조각만 보였다 → **새 화면 공간 실루엣 외곽선** `Zone/CinematicSilhouetteOutline`(`Map/Shaders/`) · `MA_CinematicBridgeOutline`(`3.Materials/Map/`) — ① 스텐실 비트 128 로 다리 화면 영역 표시 ② 부풀린 메시를 깊이 무관 맨 위에 그리되 표시 영역 안쪽은 뺀다 → 실루엣 둘레 테두리만. 연출 SO `bridgeOutlineMaterial` 에 연결(비우면 근접 외곽선). **머티리얼을 그대로 쓴다**(런타임 복제 없음 — 경석 10-27) — 진하기·두께는 `Assets/3.Materials/Map/MA_CinematicBridgeOutline.mat` 의 `Width`(시작 3.5)·`Color` 알파(시작 0.55)에서 조절. 옛 `bridgeOutlineWidth/Color` 칸은 지웠다. ⚠️ 정정(경석 10-27 Play — 왼쪽 아래 다리 연결 지점에 테두리가 없었다): 끊긴 이동식 다리가 고정 다리보다 벽 쪽으로 1m 더 나와 있는데 외곽선에서 뺐기 때문 → **다리 4개 전부를 하나의 실루엣으로** 그린다. 영역 표시 `MA_CinematicBridgeOutlineMask.mat`(테두리 패스 끔·큐 3990)를 모든 조각에 먼저, 테두리 `MA_CinematicBridgeOutline.mat`(영역 표시 패스 끔·큐 4000)를 나중에. `bridgeOutlineNames` 기본 = 비움(전부). ⚠️ 정정(경석 10-27 Play — 이동식 다리 중 2층 바닥 아래로 들어간 부분까지 투시해 테두리가 바닥 위로 삐져나왔다): 셰이더가 **존 중심(xz)에서 반경 밖 픽셀을 버린다**(영역 표시 = 반경, 테두리 = 반경 + `bridgeClipEdgeWidth` 0.25m → 바닥 가장자리를 따라 선). 중심·반경은 셰이더 전역(`_CinematicClipCenter/Radius/Edge` — 머티리얼 에셋에 안 남게). 반경 = 인스펙터 `bridgeClipRadius`(0 = 자동) — 자동은 연출 시작 때 조각마다 바깥으로 0.1m 씩 위에서 레이를 쏴 "다리 윗면(대상 조각 중 최고)이 보인 뒤 처음 더 높은 것에 닿는 거리"의 최소값(`CinematicFraming.FirstCoveredAfterVisible`, 테스트). 실측 10-27: 고정 다리 10.9~12.3m 안 덮임 · 이동식 10.5~14.9m → 13.19·13.10 → **13.10m**. 로그 `[Zone4Intro] 다리 외곽선 반경 측정`. 실패하면 `bridgeClipFallbackRadius`. ⚠️ 정정(경석 10-27 Play): 자동 13.10m 는 바닥 쪽으로 1m 넘쳤다 — 자동은 **충돌체** 기준이라 보이는 바닥 메시 가장자리와 어긋난다(추정). → 에셋 `bridgeClipRadius` **12.3m**(고정 다리 바깥 끝 근처) 시작값 · 인스펙터 반경은 **Play 중 바꾸면 바로 반영**(매 프레임 전역 갱신). 자동 측정은 로그 참고용으로 늘 남긴다. 스킵 뒤 중간보스 샷도 다리 외곽선.
4. **복귀가 끝나는 순간** G7 열림 · 보스 타이머 시작 · 존4 일반 몬스터 기상(예전 3초 지연 없음) · 무전 "어서 움직여." → 이어서 기존 설정 자막 `TUT_Z4_LORE`·`TUT_Z4_TIMER_HINT`.
5. `TUT_Z4_START`·`TUT_Z4_PANEL_INTRO` 는 연출 판에서 안 나온다(행은 남김).

## 다른 영역 파일 변경
| 파일 | 변경 | 영향 |
|---|---|---|
| `Map/Tutorial/TutorialFlowModel.cs` | 게이트 **보류**(`HoldGate`·`ReleaseHeldGate`·`IsGateReadyHeld`·`GateReadyWhileHeld`) — `Evaluate` 의 자동 열림 3곳이 `OpenOrHold` 를 거친다. `TryOpenGate`(직접·디버그 강제)는 보류와 무관하게 열고 보류를 지운다. 준비된 보류 게이트는 "동료 대기"로 안 보인다 | 보류를 안 건 게이트는 동작 동일 · 테스트 `TutorialGateHoldTests` 6건 |
| `Map/Tutorial/TutorialDirector.cs` | `OnNetworkSpawn` 에 표시 컴포넌트 부착 1줄 | — |
| 새 `Map/Tutorial/TutorialDirector.Zone4Intro.cs`(partial) | 복제 `Zone4IntroState` · 스킵 표 ServerRpc · 보류 API | 새 NetworkVariable 1개(호스트·클라 같은 빌드 필수) |
| `Map/Tutorial/TutorialSkipController.cs` | `IsRunning` 읽기 전용 | 구간 스킵(Alt+숫자) 중엔 연출 없이 G7 보류만 푼다 |
| `Map/Tutorial/Flow/TutorialZone4Flow.cs` · `TutorialZone4Rules.cs` | 연출 판: `startOnGateOpen` · 설정 대기 0 · 기상 지연 0 · 설정 자막 지연 · START/PANEL_INTRO 생략 | 연출을 끄면 예전 그대로 |
| `Tutorial/Guide/TutorialGuideUI.cs` | `SetObjectiveSuppressed(bool)`(목표 패널·강조 레이어만 숨김) · 🔴 `TryResolveText` 가 자막과 같은 출처(Strings 우선 → SO)를 본다 | 전엔 SO 만 봐서 영어로 안 바뀌었다(보스 독백 포함) |
| `Tutorial/Guide/TutorialGuideIds.cs` · `TutorialGuideTexts.asset` | 행 `TUT_Z4_INTRO_DEVICES`·`_BRIDGE`·`_RETURN`(문장 경계 `|`) | 다음 Strings Export 때 키 `Tutorial.Guide.TUT_Z4_INTRO_*` 생김 |
| `Map/ZoneBridgeGateManager.cs` | `TryGetFirstGate` 읽기 전용 | — |
| `Rendering/Fog/FogManager.cs` | `Active`(읽기) · `dimSuppress`(런타임 전용 0~1 — 넘기는 디밍 채도·명도를 1 쪽으로 당김) | 기본 0 = 동작 동일 |
| `Map/Tutorial/TutorialSkipController.cs`(추가) | `IsSkippingThrough(gate)` — 4구역 연출은 **G7 을 지나는 스킵(Alt+6 이상)만** 보류를 푼다 | 🔴 처음엔 스킵이 돌기만 하면 풀어서 Alt+5 뒤 연출이 사라졌다(경석 10-27 Play) |

## 데이터 — 기획이 고치는 곳
- **문구** = xlsx `Strings` 시트 `Tutorial.Guide.TUT_Z4_INTRO_*`(ko/en). `|` = 그 자리에서 무조건 다음 조각. 한 줄 최대 폭을 넘으면 띄어쓰기에서 자동으로 다음 조각.
- **시간** = xlsx 새 시트 `Cinematic`(대상 `Zone4IntroCinematic`) — `shots[0..4].moveSeconds/holdSeconds` · `returnBlendSeconds` · `skipHoldSeconds` · `skipReturnSeconds` · `lines[0..1].shotIndex/delaySeconds/chunkSeconds/gapSeconds` · `returnLine*` · `maxLineWidth`. ⏳ 시트는 병합 Export 로 만든다(SVN 잠금 필요 — 아직 안 함).
- **카메라 구도·화면·무전 그림** = `Assets/Resources/Tutorial/Zone4IntroCinematic.asset` 인스펙터(xlsx 제외). 구도는 임시값 — 기획팀 확인 뒤 조정.
- **무전 캡션**(경석 10-27): 위치 = 화면 비율 `captionAnchor`(기본 0.5, 0.28 — 가운데 아래, 세로 1/4~1.25/4) + `captionOffset`. 강조는 아웃라인 대신 **등장 움직임** — 조각이 나타날 때 `captionRiseDistance` 14 를 `captionRiseSeconds` 0.25초 동안 올라오며 페이드 + 무전기 배지 `badgePulseScale` 0.18 을 `badgePulseSeconds` 0.35초 한 번 펄스(`CinematicCaptionMotion`, 테스트 2건). 장치 대사는 마지막 조각을 **다음 대사(다리·중앙 섬)가 시작될 때까지 붙잡는다**(`lines[i].holdUntilNextLine` — 인스펙터 체크, 장치를 다 보기 전에 내려가면 헷갈린다, 경석 10-27).

## 공용으로 뽑은 것 (보스 등장 연출과 공유)
- `Map/Cinematic/CinematicOverlay.cs` — 레터박스·비네팅·스킵 위젯·HUD 끄기·연출 vcam(`BossIntroPresentation` 에서 분리, 보스 연출 동작 무변경). `Map/Cinematic/CinematicRules.cs` — 한 줄 나누기·조각 스케줄·샷 타임라인·구도(테스트 `CinematicRulesTests` 13건). `Map/Cinematic/CinematicRadioCaption.cs` — 무전 캡션.
- 🔴 보스 등장 연출 스킵 투표자도 **접속자 전원(관전자 포함)** 으로 바뀌었다(`BossEncounterDirector.SkipVoterCount`, 경석 10-26 — 10-23 "영구 사망 제외" 뒤집음). 잠금·워크인·인원 보정 대상은 그대로.

## 알려진 한계 / 확인 대기
- 타이머 칸 펄스 강조(단계 `Zone4Lore`)는 G8 이 즉시 열려 거의 안 보인다(다음 프레임에 `Zone4Combat`).
- 연출 중 재합류한 클라는 잠금이 안 걸린다(첫 스폰 자리) — 연출이 끝나면 정상.
- 튜토리얼 재시작(Alt+0 처음부터) 뒤 보류가 다시 걸리는지 미확인.
- Play·MPPM 미확인(아래 체크리스트).

## Play 체크리스트
1. 1인: 복도3 특성 선택 → 연출(패널 4 → 다리·적 → 복귀) → 복귀 끝에 G7 열림·타이머·몬스터 움직임·"어서 움직여." → 설정 자막 2줄.
2. 연출 중 HUD 숨김·자막 유지·목표 패널 숨김 → 복귀 끝에 되살아남. 무전 캡션 두 줄이 한 줄씩 나왔다 꺼짐.
3. 스킵: Space 1초 → 외곽선 채움 → 1초 복귀 → 정상 진행. 스킵 직후 대시 오발 없음.
4. MPPM 호스트+클라: 같은 샷·같은 대사 타이밍 · 한 명 먼저 채우고 손 떼도 유지 · 전원 동의해야 스킵 · 한 명이 늦게 복도3 에 오면 그때 시작.
5. 관전자(영구 사망) 화면에서 연출이 보이고 스킵 표가 들어가는지(4구역 · 보스 등장 둘 다).
6. 60초 교착 방지로 G7 이 준비된 경우: 특성 창이 닫히고(자동 획득) 연출 시작.
7. 구간 스킵 Alt+6 이상: 연출 없이 바로 열림. Alt+5(복도3) 뒤에는 연출이 나와야 한다.
8. 디버그 메뉴 `Tools/Tutorial/Debug/4구역 소개 연출 — …`: Dev Boot 진입 · 복도3 특성 정식 완료 · 접속자 전원 스킵 동의(서버) · 상태 로그. MCP 로 Space 홀드를 넣으면 Game 뷰 포커스가 없어 풀린다 — 홀드는 사람이 확인.

## Play 결과(2026-10-27 · Claude MCP · 1인 Dev Boot)
- ✅ Alt+5 뒤 보류 유지 → 특성 정식 완료 → 연출(16.3+1.2초) → 복귀 끝에 G7(AllDone)·G8·기상 0초.
- ✅ 서버 스킵(전원 동의) 1.59초 → 중간보스 샷 0.8+1.2초 → 복귀 1초 → G7·G8. 연출 중 무적(몬스터 근접 3타 피해 거부 — Alt+5 강제 개방으로 남은 존3 몬스터).
- ✅ 디밍 줄이기: 2층 바닥·몬스터 색이 보인다. ⏳ 구도·무전 캡션·Space 홀드 위젯은 경석 눈으로.
