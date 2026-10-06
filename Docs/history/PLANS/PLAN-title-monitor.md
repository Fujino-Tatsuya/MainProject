# PLAN — 타이틀 게임 시작 연출 · 인게임 모니터 패널 상호작용 연출 (2026-10-03 · 경석 · ✅ 승인 10-03 · ✅ 팀장 Play 10-04)

> 요청(팀장 10-03): ① 타이틀 모니터가 꺼지고 → 그 검은 화면 속으로 더 들어가는 느낌, **느리게 · 페이드 인/아웃**,
> 카메라 워크 도중에 꺼지고 (다음 화면에서) 켜진다. ② 인게임 상호작용 오브젝트에 플레이어가 가까우면 **외곽선**,
> **F** 로 상호작용하면 **Re:C 로고 모니터로 켜진다**(튜토리얼 대형 존). 브랜치 `feature/Boss23`.

## 1. 현재 (조사 10-03)

**타이틀** (`Assets/0.Scenes/MainFlow/1.TitleScene.unity`)
- 카메라 워크 = `TitleFlowDirector` — Cinemachine vcam(Far/Near/Closeup) 우선순위 블렌드, 도착 후 Brain 끔(`ReleaseCameraNextFrame`).
- 시작 = `StartGame` → `PowerOffThenLobby` → `TitlePowerOff.Play`(화면 캡처 → 전체 화면 CRT 꺼짐 1.1초, 검은 채 유지) → `StartGameImmediate`(페이드 없음) → 로비.
  ⚠️ 이 "딱 꺼지게"는 **팀장 09-23 결정**(코드 주석) — 이번 요청이 이를 바꾼다.
- 모니터 화면 = `TitleMonitorDisplay`(중앙 `monitor_screen` 머티리얼 인스턴스 교체: 로고 ↔ UI RT).
- 페이드 = `NemoSceneManager` blackImage(`FadeOut`/`FadeThenInvoke`). 로비는 진입 시 이미 `PlayEnterFade`.

**인게임 모니터 패널** (`ZoneL_typeB` 의 `PF_Prop_object_panel_001~004`)
- 🔴 **이 4개는 이미 `ZoneBridgeGate` 의 F 상호작용 패널이다**(인덱스 0~3 = 001~004). F 입력·거리 판정·서버 검증·
  `NetworkList` 복제·"한 번 켜면 유지"·레이트 조인 재현이 전부 `ZoneBridgeGateManager`(MapScene 상주)에 있다. 4개 다 켜면 다리가 열린다.
  지금 켜졌을 때 연출 = 바닥 링(`ZoneInteractRing`)뿐 → 붙일 자리는 `ZoneBridgeGate.SetPanelActivatedVisual`.
- 존은 각 피어 **로컬 Instantiate**(비네트워크, `MapContentSpawner`). 존에 NetworkBehaviour 금지(CONTEXT·`ZoneMonsterSpawnSet` 주석) → 연출은 전부 로컬.
- 패널 = `object_panel.prefab`(fbx 통짜 메쉬 1 · 머티리얼 1 `Generic_01_A` Synty 공용 아틀라스). **현재 머티리얼 구성으로는 화면만 켜기 어렵다**
  (공용 아틀라스·머티리얼을 고치면 다른 프랍까지 바뀜 — 인스턴스 머티리얼·UV 마스크 셰이더는 가능하나 더 무겁다). 지금 머티리얼엔 `Outline` 패스가 없어 Flat Kit 외곽선도 안 걸린다.
- `object_panel.prefab` 은 ZoneL_typeC · Zone_typeQuest01 에서도 쓰인다 → 원본 프리팹은 건드리지 않는다.

## 2. 확정 (grill 10-03 팀장)

| 항목 | 결정 |
|---|---|
| 타이틀 연출 시점 | **게임 시작할 때만**(메뉴 진입·종료는 그대로) |
| 인게임 켜짐 | 한 번 켜면 유지 · 전 피어 동기화 — **기존 게이트 복제를 그대로 쓴다**(새 네트워크 코드 없음) |
| 켜진 모습 | 화면 자리 앞 **화면 판(쿼드)** + CRT 켜짐 → **Re:C 로고** + 작은 포인트 라이트 |
| 대상 | 패널 4개 모두 · ZoneL_typeB 가 나오는 모든 곳(튜토리얼·Stage1·랜덤) |
| 브랜치 | `feature/Boss23` |

**기본값(팀장이 다르게 말하지 않으면)** — 상호작용 키 F(게이트와 동일) · 반경 = 게이트 `InteractRadius` 그대로 ·
외곽선은 **로컬 플레이어**가 범위 안일 때 그 화면에만, **아직 안 켠 패널**에만 · 로고 = 타이틀과 같은 `monitor_screen.png`.

## 3. 접근

**T1 타이틀 — 시작 연출 교체** (`TitleFlowDirector` · `TitleMonitorDisplay` · 새 `TitleStartDive`)
1. 시작 누름 → 입력 잠금 + **모니터 UI 레이캐스트·버튼도 잠근다**(`TryScreenToUIPixel` 은 Starting 을 모른다 — Codex). **모니터 화면이 월드에서 꺼진다**(≈0.6초).
   - 담당 = `TitleMonitorDisplay` 가 전원 진행도를 제어. 월드용 `TitleCRTScreen` 에 전원(수축·발광 소멸) 진행도를 추가한다 — UI용 `TitleCRTOff`(투명·ZWrite Off·`_ScreenParams`)는 월드 화면에 못 쓴다(Codex).
     꺼진 끝 상태는 **불투명 검정 + 깊이 기록** 유지. 전체 화면 캡처형 `TitlePowerOff` 는 시작에선 안 쓴다(종료는 그대로).
2. 꺼짐과 겹쳐 **카메라가 화면 중심으로 천천히 밀고 들어간다**(≈2.5초, 느리게 시작 → 빨라짐). 끝에서 시야가 검은 화면으로 꽉 찬다.
3. 밀고 들어가는 후반에 **기존 `TitleSceneManager.StartGame()`** 호출 = 검정 페이드 완료 후 `GoToLobby` → 로비 페이드 인(기존 `PlayEnterFade`).
   🔴 `StartGameImmediate` 를 `FadeThenInvoke` 콜백으로 쓰면 `IsTransitioning` 잠금에 걸려 로비로 안 간다(Codex 확인). 전환은 고정 대기 아닌 **페이드 완료 기준**.
   ⚠️ `NemoSceneManager.FadeIn()` = 검게(알파 1), `FadeOut()` = 밝게 — 이름이 일반 용어와 반대다. 페이드 길이는 지금 타이틀 설정 1.5초(필요하면 조정).
4. **카메라 제어권을 시작 연출이 명시적으로 가져간다** — Brain 해제는 메뉴 도착 1프레임 뒤라 그 전에 시작을 누르면 Brain 이 켜진 채다(Codex).
   Near 반영 보장 → Brain 끔 → 현재 자세 기록 → 이동. 도착점은 **화면 중심·법선 앵커**(FBX 피벗은 메시 중심이 아님 — `TitleRigAuthoring` 기록), 근접 클리핑(0.1) 여유,
   **화면을 뚫기 전에 검정 페이드 완료**. 시간·이징·거리는 인스펙터 값.
- 메뉴 진입·설정·종료 흐름은 손대지 않는다.

**T2 인게임 — 패널 화면 켜짐** (`ZoneBridgeGate` 로컬 연출 확장)
- 게이트에 **옵트인** 설정 묶음(기본 꺼짐 → ZoneL_typeB 게이트만 켬): 화면 판 로컬 위치·회전·크기(패널 기준, 4개 같은 메쉬라 하나), 로고 텍스처, 라이트 색·세기·범위.
- 화면 판 = 런타임에 패널 밑에 생성(프리팹 구조 무수정, 기존 `BuildRings` 와 같은 방식 — 링 목록 성공 여부와 무관하게). 화면 면 위치는 **에디터에서** 메쉬를 분석해
  로컬 위치·회전·크기를 게이트 값으로 저장한다(런타임엔 저장값만 — fbx `isReadable: 0`, 임포트 스케일 0.98, Codex). 인스펙터로 미세 조정.
- 🔴 **최초 켜짐과 재적용을 구분한다** — 지금 `ApplyAllStates` 는 목록 변경 하나에도 모든 패널의 true 를 다시 보낸다(Codex 확인) → true 마다 재생하면 반복된다.
  매니저 상태 적용을 **즉시 / 전환 연출** 두 모드로: 등록·스폰·레이트 조인 = 즉시(연출 없이 켜진 상태), 목록 **값 변경** 이벤트에서 이전/현재 마스크를 비교해 **새로 켜진 비트만** 연출.
  새 RPC·복제 필드는 없다(매니저 적용 코드만 수정).
- 켜짐 연출: 화면 판 CRT 켜짐(가로선 → 전체, ≈0.4초) → 로고 + 라이트 페이드 인.

**T3 인게임 — 근접 외곽선** (`ZoneBridgeGateManager` 로컬 + `ZoneBridgeGate`)
- 매니저의 "가장 가까운 미활성 패널" 탐색(`TryFindNearestPanel`)을 재사용 — 단 지금은 **F 를 눌렀을 때만** 돈다(Codex) → 매 프레임 표시 갱신을 키보드 검사와 분리.
  공통 조건 = `IsSpawned` · 로컬 플레이어 있음 · `Alive` · 연출 잠금 아님. 서버 전용·사망·디스폰·범위 이탈 시 **이전 외곽선 반드시 해제**. F 진단 로그는 표시 갱신에서 반복 출력 금지.
- 외곽선 = 패널 렌더러에 **`LightMode="Outline"` 단일 패스 머티리얼을 덧붙여 기존 Flat Kit `Outline` 렌더러 피처가 그리게** 한다(Codex 추천 —
  피처는 머티리얼 브랜드를 안 본다, 반전 헐 `Cull Front`). 원본 머티리얼 배열을 보관했다가 대상 바뀔 때만 추가·복원. 일반 표면 패스가 있는 FlatKit 머티리얼 통째로는 붙이지 않는다.
  ⚠️ 각진 메쉬 모서리 갈라짐은 Play 확인. 부족하면 대안 = 렌더링 레이어 마스크·합성 피처(`PlayerSilhouetteFeature` 방식, 새 비트 배정 — 0 조명·1 데칼·2·3 실루엣 사용 중).
- 켜진 패널은 외곽선 없음(이미 상호작용 끝).

## 3-1. 구현 기록 (10-03) — ✅ T1·T2·T3 코드 · ⏳ 팀장 Play
- **T1**: `TitleStartDive`(신규, 디렉터가 없으면 자동 부착 → 씬 무수정) · `TitleMonitorDisplay.PowerOff` · `Title/CRTScreen` 에 `_Power`(기본 1 = 벽 모니터 무영향).
  시간 기본값: 꺼짐 0.6 · 밀고 들어감 2.8(가속 지수 2.2) · 1.2초에 `TitleSceneManager.StartGame()`(페이드 1.5 → 2.7초에 완전 검정) · 화면 앞 0.12m 정지.
- **T2**: `ZoneBridgeGate` 모니터 화면(옵트인) — 화면 판(Quad) + `MA_TitleMonitorUI` 인스턴스(로고) + 포인트 라이트, 켜짐 = `_Power` 0→1(점 → 가로선 → 전체).
  화면 면 = 메시 평면 분석(`Tools/Map/Authoring/Zone Monitor Screen/1·3`) 후보 2개를 프리뷰 씬에 렌더해 비교 → **앞면 #8** 채택(뒤쪽 #9 는 원래 화면이 가장자리로 비침).
  ZoneL_typeB 게이트 값은 YAML 직접 삽입(인스펙터 안 엶) — Unity 재직렬화 없음 확인.
  매니저: 게이트별 마지막 적용 마스크 → 첫 적용은 즉시, 이후 새 비트만 연출.
- **T3**: `Zone/InteractOutline`(단일 `Outline` 패스) · `MA_ZoneInteractOutline` · 매니저 `UpdateHighlight`(키보드 검사 앞, F 와 같은 조건·탐색) · 게이트 `SetPanelHighlighted`(sharedMaterials 배열 덧붙임·복원).
- 회귀: 컴파일 0 오류 · EditMode 전투 106 통과(기존 실패 2 무관).

## 3-2. 팀장 Play 1차 (10-04) 반영
- **T1 순서 변경**: 1판(꺼짐과 접근 동시)이 "하나도 안 바뀐" 것처럼 보였다 → **모니터 화면만 처음 만든 CRT 꺼짐(Title/CRTOff 수식 그대로, 1.1초) — 카메라 고정 → 끝나면 천천히 다가감(2.8초) + 1.0초에 검정 페이드 → 로비**.
  `Title/CRTScreen` 의 `_Power` 경로 = `PowerOffColor`(CRTOff 수식 이식, 비율만 `_ScreenAspect`). 전체 화면 꺼짐(`TitlePowerOff`)은 종료(EXIT)에만.
- **T2/T3 '하나도 안 바뀜' 원인**: YAML 로 넣은 게이트 값 12줄이 **사라져 있었다** — ZoneL_typeB 가 **프리팹 모드(Auto Save)로 열려 있어** 메모리의 옛 내용이 테스트 후 씬 리로드 때 디스크를 덮었다.
  → 값 쓰기를 Unity API 로(`Tools/Map/Authoring/Zone Monitor Screen/5`, 프리팹 모드면 그 스테이지에 쓰고 저장). 재현 PNG(`/4`)로 화면 켜짐·외곽선 렌더 확인.
  패널 켜짐 = 같은 꺼짐 수식 역재생(0.9초).

## 3-3. 팀장 Play 2차 (10-04) — ✅ 전부 OK
- 팀장: "다 괜찮다". 후속 = F 활성 표시용 **파란 바닥 링 삭제**(임시였다). 게이트의 링 생성·표시·필드 제거, `ZoneBridgeGateWiring` 의 링 기본값 복원 제거,
  프리팹의 링 필드 5줄은 Unity 재저장으로 정리. `ZoneInteractRing.cs` 클래스는 Visual Scripting 생성 코드(`AotStubs.cs`)가 참조해 **파일은 남김**(미사용).

## 4. 리스크

- 🔴 타이틀 시작 연출은 **팀장 09-23 "딱 꺼지게" 결정을 뒤집는다** — 이번 요청이 우선(기록용).
- 🔴 ZoneL_typeB 루트에 **NetworkObject 가 붙어 있다**(존 규약 위반 — 존재는 확인). 원인(09-18 `4134577e`)·"인스펙터 열면 재부착"은 **추정**(Codex: 미확인).
  이번 연출엔 NetworkBehaviour 불필요. 안전하게 게이트 값은 스크립트/YAML 로 넣는다. 정리는 별건(작업 칩).
- 반전 헐 외곽선 품질(위 ⚠️). 화면 판 Z-fighting → 화면 면에서 몇 mm 띄우고 뒷면 컬링.
- `object_panel` 을 쓰는 다른 존(L_typeC·Quest01)은 옵트인이 꺼져 있어 변화 없음.

## 5. 검증

- 컴파일 · EditMode(전투 · 데이터 테이블) 회귀.
- 팀장 Play: 타이틀 시작 연출(속도·페이드 타이밍) · 튜토리얼 대형 존에서 패널 접근 시 외곽선 → F → 화면 켜짐·라이트 · 4개 다 켜면 다리 열림(기존 동작 유지).
- MPPM: 다른 피어가 켠 패널이 내 화면에서도 켜짐 · 외곽선은 내 위치 기준.
- 추가(Codex): 한 패널 켠 뒤 레이트 조인 · 네 패널 켠 뒤 레이트 조인(연출 반복 없음) · 다른 패널 변경 시 이미 켜진 패널 재연출 없음 · 게이트 등록/매니저 스폰 순서 차이 ·
  근접 중 사망·디스폰 시 외곽선 해제 · 메뉴 도착 직후 바로 시작 · 낮은 FPS 에서 페이드 완료 후 전환.

## 6. Codex 설계 회의 (10-03) — 반영 요약
13건 중 🔴 2 확인: 페이드 잠금(`StartGameImmediate` 콜백 불가) · 재적용 반복(즉시/전환 모드 분리). 🟡: Brain 제어권 · 월드용 CRT 진행도 · UI 입력 잠금 ·
도착 앵커 · 에디터 저작 · 외곽선 매 프레임 갱신 · `Outline` 피처 활용. ⚪: 불가 표현 완화 · NetworkObject 원인은 추정. 모두 §3·§4·§5 에 반영.
