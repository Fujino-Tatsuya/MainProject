# PLAN-settings.md — 설정창 1순위 (통합 패널 + 그래픽·오디오·조작·게임플레이)

> 상태: **✅ 승인(팀장 10-12) · 구현 중** · 2026-10-12 · 작업자: 경석(Claude) · 브랜치 `feature/Settings`(`feature/Boss23` 82c6df7c 에서 분기)
> 근거: 참고 게임 조사(Shape of Dreams·Ravenswatch — Steam 패치노트 전수, 채팅 10-12) + 그릴 4회(아래 결정).

## 0. 현재 상태 (10-12 확인)
- 타이틀 `TitleOptionsPanel`: 탭 4개(게임플레이·그래픽·조작·오디오) 중 **게임플레이·그래픽 비어 있음**, 조작 = `Hold Skill: Toggle` 1개, 오디오 = 마스터·BGM·효과음 슬라이더(**미동작** · `VolumeSlider` 저장 코드 주석 처리).
- 인게임 ESC(`MapSceneManager` → `4.MapScene` `OptionPanel`): 볼륨 3개 + 나가기. 탭 없음.
- 해상도: `DisplayResolutionBoot` 가 **실행마다** 2560×1440 FullScreenWindow 강제(은희 `c44ced45`).
- 프레임: `FrameRateCap` 144 고정. `runInBackground: 0`.
- 이미 있는 기능: 데미지 숫자 필터 3종(`FloatingDamageDisplayFilter`, 기본 OwnDealtOnly) · `CameraFeedback` 피격/타격 흔들림 · Input System(`InputSystem_Actions.inputactions`).
- 은희 `feature/DashDirectionOption`(10-12, **dev 미반영**, 3커밋): `DashDirectionRules` + 테스트 · `UserInputConfig` 대시 방향 · 타이틀 조작 탭 토글.

## 1. 결정 (팀장 10-12 그릴)
| # | 결정 |
|---|---|
| S1 | **설정 패널 하나**(프리팹)를 타이틀·인게임 ESC 가 같이 쓴다 |
| S2 | 해상도 후보 = `2560×1440 (권장)` · `1920×1080 (권장)` · `모니터 기본 해상도`(현재 모니터 네이티브). 4K 별도 항목 없음 |
| S3 | 기본 해상도 **2560×1440 유지** — 단 "첫 실행만", 이후엔 저장된 선택(실행마다 강제 → 폐지) |
| S4 | 창 모드 3종: 전체화면 · 테두리 없는 창 · 창 모드 |
| S5 | 해상도·창 모드만 "유지할까요? 10초 뒤 되돌림" 확인 창. 나머지는 조작 즉시 적용 |
| S6 | 프레임 제한 60/120/144/무제한(기본 144) + VSync(기본 끔, 켜면 프레임 제한 비활성) |
| S7 | 키 리바인드 = **핵심 키만**(키보드·마우스, 패드 제외, 이동 WASD 고정) |
| S8 | 데미지 숫자 = 기존 3종 + **끄기** (기본 = 지금 기본 OwnDealtOnly) |
| S9 | 화면 흔들림 = 슬라이더 0~100%(기본 100, 0 = 끔) |
| S10 | 대시 방향 = 은희 브랜치를 **흡수**, 선택지 캐릭터 정면 / 마우스 방향, **기본 = 캐릭터 정면** (⚠️ 10-09 "마우스 방향 기본" 변경을 되돌림) |
| S11 | 백그라운드 음소거 옵션(기본 켬): 포커스 잃으면 전체 출력만 0, 돌아오면 복구. 슬라이더·저장값 불변 |
| S13 | 모니터 네이티브보다 큰 해상도 후보는 **숨긴다**. 첫 실행 기본값도 네이티브가 1440 보다 작으면 네이티브로 내린다(팀장 10-12) |
| S14 | 상호작용 키도 리바인드 대상. 입력 에셋 `Interact` 를 E → **F** 로(SkillSub E 중복 해소), 게이트(`ZoneBridgeGateManager`, 지금 `Key.F` 직접 읽음)가 액션을 읽게. 안내(`InteractPrompt`)는 F 면 기존 이미지, 다른 키면 키 이름 텍스트(팀장 10-12) |
| S15 | 언어 = English/한국어, **설정창 문구만**. 문구는 **코드 사전 1개**(`SettingsStrings` — 키 → 영/한 평평한 표). 나중에 버튼 하나로 SO/xlsx 로 옮길 것이 확정이라 표는 `키·영·한` 3열 외 구조를 넣지 않는다(팀장 10-13)<br>→ **Loc 으로 통합됨**(10-13 할 일 38 — `SettingsUiStrings` `UI.Settings.*` + Strings 시트). [handoff-settings-localization.md](Docs/tech/handoff-settings-localization.md) 참조 |
| S16 | 첫 실행 언어 = 시스템 언어(Windows 한국어면 한국어, 그 외 English). 이후 저장된 선택(팀장 10-13)<br>→ **Loc 으로 통합됨**(`UserLanguageConfig` — 규칙 동일, 옛 정수 저장값 자동 이관). [handoff-settings-localization.md](Docs/tech/handoff-settings-localization.md) 참조 |
| S17 | 성능 표시 = 설정 게임플레이 탭 `끔 / FPS / FPS+핑`(기본 끔). 켜면 화면 우측 상단 구석. 전투 HUD 와 분리된 런타임 오버레이(씬 수정 없음) — 타이틀에서도 FPS 보임. 호스트는 핑 대신 `HOST`, 세션 밖은 FPS 만(팀장 10-13) |
| S12 | 씬 변경 최소화: 패널은 프리팹, 씬은 기존 패널 → 프리팹 인스턴스 교체만. **씬 작업은 은희 튜토리얼 브랜치(4.MapScene 수정 중 4개)가 dev 에 들어간 뒤** |

## 2. 구현
1. **`GameSettings`**(정적 서비스) — 값 정의·기본값·PlayerPrefs 저장/로드·적용을 한 곳에. 키 접두 `Settings.`. 부팅 시(`BeforeSceneLoad`) 로드·적용.
   기존 `UserInputConfig`(Hold/Toggle, 키 `Input.HoldSkillAsToggle`)는 그대로 읽어 호환(키 이름 유지).
2. **그래픽**: `DisplayResolutionBoot` → "저장값 있으면 그 값, 없으면 2560×1440 FullScreenWindow". 창 모드 3종 매핑(ExclusiveFullScreen / FullScreenWindow / Windowed). `FrameRateCap` 이 설정값을 읽게. VSync = `QualitySettings.vSyncCount`.
   확인 창: 적용 직후 10초 카운트다운 → 유지 / 되돌림(시간 초과 = 되돌림).
3. **오디오**: `VolumeSlider` 미동작 원인 수정 + 저장 복구. 백그라운드 음소거 = `OnApplicationFocus` 에서 `AudioListener.volume` 0/1(슬라이더 값과 독립).
4. **조작**: Hold/Toggle(기존) · 대시 방향(은희 브랜치 머지 → 기본 CharacterFacing) · 리바인드 대상 = `Attack`(LMB) · `Interrupt`(RMB) · `SkillMain`(Q) · `SkillSub`(E) · `SkillUltimate`(R) · `Dash`(Space) · 상호작용. Input System `PerformInteractiveRebinding` + `SaveBindingOverridesAsJson`. 중복 키 = 교체 확인. "기본값 복원" 버튼.
5. **게임플레이**: 데미지 숫자 4종(SO 에셋은 안 고치고 런타임 오버라이드) · 화면 흔들림 배율(`CameraFeedback` 진폭 × 설정).
6. **패널 프리팹** `Assets/2.Prefabs/UI/SettingsPanel.prefab` — 탭 4개. 타이틀·ESC 에서 열기/닫기만 각 씬 매니저가 한다.

## 3. 리스크 / 확인할 것
- ✅ 확인(10-12): 에셋 `Interact`(E)는 **사용처 0** — 실제 상호작용은 `ZoneBridgeGateManager` 가 `Key.F` 직접 읽기(부활은 F 미사용, F10 은 디버그). → S14.
- 모니터가 1440 보다 작을 때 → S13(숨김) 확정.
- 데미지 숫자·흔들림은 **각 피어 로컬 설정**(네트워크 동기화 없음).
- 은희 브랜치 흡수: `UserInputConfig`·`1.TitleScene` 을 둘 다 고친다 → 브랜치 머지 후 토글을 통합 패널로 옮기고 타이틀 씬의 개별 배치는 제거. 은희 님께 공유 필요.
- 범위 밖: `runInBackground`(호스트 Alt+Tab 서버 정지 — 별건) · 2·3순위(렌더 스케일·이펙트 품질·커서·언어·자막·핑) · 패드.

## 4. 검증
- 컴파일 `error CS` 0 · 전투 EditMode(기존 실패 2 외 0) · `DashDirectionRulesTests` 통과 · 설정 저장/로드 EditMode 테스트(신규, 기본값·범위 클램프·키 호환).
- Play(팀장): 타이틀·ESC 양쪽에서 같은 패널 · 각 항목 즉시/확인 적용 · 재실행 후 값 유지 · 해상도 10초 되돌림 · 리바인드 후 스킬 동작 · Alt+Tab 음소거·복구.
- 빌드 1회: 첫 실행 2560×1440, 변경 후 재실행 시 유지.

## 진행
### 1단계 (10-12) — 저장·적용 계층 ✅ (UI 없음) · 컴파일 error CS 0 · 설정 EditMode 17/17 · 전투 108/2(기존 실패 2)
- 신규 `Assets/1.Scripts/Settings/`: `DisplayConfig`(해상도 후보·기본값·폴백·창 모드·프레임 제한·VSync, 부팅 적용) · `SoundConfig` + `BackgroundAudioMute`(볼륨 저장·Alt+Tab 음소거, 빌드에서만) · `GameplayConfig`(데미지 숫자·흔들림) · `KeyBindingConfig`(핵심 7액션 오버라이드 저장·적용·충돌 검사·초기화) · 테스트 `SettingsRulesTests` + 메뉴 `Tools/Tests/설정창 EditMode 테스트 실행`.
- 삭제 `Managers/DisplayResolutionBoot.cs`(실행마다 1440 강제). `FrameRateCap` 부팅 적용 제거(DisplayConfig 가 한다 — BeforeSceneLoad 순서 미보장).
- `AudioManager.Awake` 에 저장 볼륨 적용 1줄 · `VolumeSlider` 저장값 기준(NRE 원인 제거). `FloatingDamageDisplayFilter.Off` 추가(끝) · `DisplayFilter` 속성이 설정값 우선. `CameraFeedback` 진폭 × 흔들림 배율(흔들림 경로는 이 함수 하나뿐 확인).
- 입력: 에셋 `Interact` E → **F** · 게이트가 설정 키를 읽는 독립 액션 사용 · `PlayerInputReader` 가 오버라이드 재적용.
- 은희 `feature/DashDirectionOption` 머지(기본 캐릭터 정면).
### 2단계 (10-12) — 항목 UI ✅ · 컴파일 error CS 0 · ⏳ 팀장 Play
- 팀장 10-12: 비주얼은 1차 기능용(CRT 조정 때 손봄) · **언어 = 버튼만**(로컬라이제이션 담당이 채움) · 2·3단계 바로 진행.
- 런타임 `Settings/UI/`: `GraphicsSettingsController`(해상도·창 모드는 Apply → 10초 확인, 프레임·VSync 즉시) · `GameplaySettingsController`(데미지 숫자 4종·흔들림·언어 자리) · `MuteInBackgroundOption` · `KeyRebindRow`(Esc 취소·충돌 시 맞바꿈) · `KeyRebindPanel`(초기화·상태 줄).
- Esc 충돌: 키 변경 중엔 `TitleSceneManager`·`MapSceneManager`·`TitleFlowDirector` 가 Esc 로 옵션을 닫지 않는다(`KeyRebindRow.IsRebinding`).
- `InteractPromptView`: 키가 F 가 아니면 그림 대신 키 이름(텍스트 칸 `keyLabel` 은 프리팹에 아직 없음 — 비어 있으면 F 그림 유지).
- 저작 `Tools/UI/Settings/설정 패널 항목 배치 (멱등)`: 탭마다 `Rows`(세로 정렬)에 `Settings_*` 행 생성, 기존 Hold·대시 토글·볼륨 3개는 Rows 로 이동. 글꼴 = MasterVolumeLabel. UI 텍스트는 영어 — 기존 탭·항목(`Gameplay`·`Hold Skill: Toggle`)이 영어라 맞췄다. ⚠️ 정정: 처음엔 "글꼴에 한글 없음"이라 적었으나 확인해 보니 글꼴 = NotoSansKR(한글 11,172자 포함) — 한글로 바꾸는 데 제약 없음.
### 3단계 (10-12) — 프리팹 공유 ✅ · ⏳ 팀장 Play
- 저작 `Tools/UI/Settings/설정 패널 프리팹화 + ESC 메뉴 적용 (멱등)`: 타이틀 탭·내용 + `TitleOptionsPanel` → `SettingsBody`(탭 OnClick 재연결) → `Assets/2.Prefabs/UI/SettingsBody.prefab`. 맵 `OptionPanel` 의 옛 `Audio_Content`(볼륨 3개) 삭제 → 프리팹 인스턴스.
- 검증: 맵 씬 변경 = Audio_Content 하위만 삭제 + 인스턴스(덩어리 20개). 타이틀 GO 249 → 씬 51 + 프리팹 199(+SettingsBody 1), 사라진 이름 0.
- ⚠️ 팀장 지시로 튜토리얼 브랜치 반영 전에 했다(S12 순서 변경) — 은희 튜토리얼 브랜치가 `4.MapScene` OptionPanel 을 건드렸다면 머지 때 충돌 확인.
- 앞으로 항목을 바꾸면: 항목 배치 메뉴 → 프리팹화 메뉴(인스턴스 변경을 프리팹에 반영) 순서.
### 타이틀 설정창 가독성 (10-12) — CRT 70% 감소 ✅ · ⏳ 팀장 Play
- 팀장: 타이틀 모니터 CRT 가 너무 강해 설정창이 안 읽힘 → **타이틀에서 연 설정창만** CRT 효과 70% 감소(인게임 ESC 는 CRT 없음).
- `TitleMonitorDisplay.SetSettingsReadable`: 중앙 모니터 머티리얼의 `_ChromaPx·_ScanStrength·_Glow·_GlowRadiusPx·_Grain·_Jitter·_Vignette`(→0)·`_Brightness`(→1)를 원본 값에서 70% 당김. `TitleCrtFx.SetEffectScale` 로 그 머티리얼의 찢김 폭·글리치도 ×0.3(벽 모니터는 그대로). 열기 = `TitleFlowDirector.OpenSettings`, 복귀 = `EnterMenu`.
- 흰 번짐 원인 = 셰이더 `_Glow`·`_Brightness 1.25`(전역 볼륨 Bloom 은 intensity 0 — 확인).
### 그래픽 확장 (10-12) — 품질 프리셋·렌더 스케일·그림자·AA·후처리·비네트 ✅ · 설정 EditMode 20/20 · ⏳ 팀장 Play
- 팀장 결정: 항목 = 렌더 스케일·그림자 품질·안티앨리어싱·후처리 켜기/끄기 · 비네트 = 크기·경계 슬라이더 2개(끄기 없음) · 프리셋 기본 = 상(지금 화면 그대로). 마우스 감도 = **보류**(그래픽 다 넣은 뒤). 키 가이드(이미지) = 예외.
- "비네트" 정체 = URP Vignette 가 아니라(맵 볼륨 Vignette active 0) **안개 시야 디밍**(`FogProfile.viewRange 18m · viewFade 4m`) → `FogManager` 가 셰이더에 넘길 때 배율(크기 70~150% · 경계 50~200%).
- `GraphicsQualityConfig`: 프리셋 상 = 렌더 100%·그림자 28m/2048·MSAA 끔·후처리 켬 / 중 = 85%·20m/1024 / 하 = 70%·12m/512·후처리 끔. 항목을 따로 바꾸면 Custom. 그림자 끄기 = 거리 0(URP `maxShadowDistance > 0` 일 때만 그림). 에디터 Play 종료 때 URP 에셋 값 복원(에셋 파일 무변경 확인).
- 후처리 끄기 = 블룸·색수차만 0 으로 덮는 전역 볼륨(우선순위 1000, 런타임 생성) — 톤매핑·색 보정은 유지.
- ⚠️ 정정: URP `supportsMainLightShadows`·`supportsSoftShadows` 는 `internal set`(읽기 전용). 처음 확인 스크립트가 "set" 문자열만 세어 쓰기 가능으로 잘못 판단 → 컴파일 오류로 발견. 부드러운 그림자 단계 전환은 뺐다.
- UI: 그래픽 탭 12줄(해상도 5 + 품질 7) — 줄 높이 34→30, 간격 6→4 로 내용 영역에 맞춤. `GraphicsQualityController` 신규.
### 10-12 Play 후 수정 — 드롭다운 목록·오디오 배치 ✅(팀장 확인)
- 드롭다운 목록 안 뜸 = 저작이 만든 오브젝트가 **Default 레이어** → 목록(별도 캔버스)이 UI 레이어만 찍는 모니터 카메라에서 빠짐(차단막은 루트 레이어라 클릭만 먹음). 생성물 전부 UI 레이어 + `SettingsDropdown.CreateDropdownList` 가 레이어 재지정.
- 드롭다운 항목이 안 눌림 = 목록·차단막의 기본 GraphicRaycaster(좌표 변환 없음) → `MonitorAwareRaycaster`(타이틀이면 RT 좌표, 아니면 기본).
- 오디오 배치 = `Audio_Content` 의 기존 VerticalLayoutGroup 이 Rows 를 끌고 감 → Rows `ignoreLayout`. 라벨 겹침 = Rows 가 자식 높이를 제어하지 않아 rect 높이(20)로 쌓임 → 기존 행은 `Row_<이름>` 감싸개(볼륨 50·토글 30).
- 팀장 확인(10-12): 드롭다운·오디오 OK · 프레임 제한 OK · 키 변경 OK. 해상도·창 모드·렌더 스케일·그림자·AA·후처리·비네트·Alt+Tab 음소거는 **에디터라 미확인 → 빌드에서**.

### 10-13 — 한국어(S15·S16) + 성능 표시(S17) ✅ · 컴파일 error CS 0 · 설정 EditMode 25/25 · ⏳ 빌드
- 신규 `Settings/Localization/`: `SettingsStrings`(키·영·한 표 61줄, `All` = 나중에 SO/xlsx 로 내보낼 원본) · `LanguageConfig`(키 `Settings.Language`, 첫 실행 = 시스템 언어) · `LocalizedText`(고정 라벨).
- 고정 라벨 연결 = 저작 메뉴가 **영어 원문 → 키 역조회**로 `LocalizedText` 를 붙인다(`SettingsLocalizationLinker`, 항목 배치 끝에 자동 · 단독 메뉴 `설정 패널 문구 키 연결 (멱등)`). 결과 39개(탭 이름·은희 토글 2개 포함). 영어 원문 중복은 테스트가 막는다.
- 코드 문구(드롭다운 항목·확인 창 카운트다운·키 입력 대기·맞바꿈·초기화 알림)는 각 컨트롤러가 언어 변경 때 다시 채운다. 언어 버튼 글자 = 지금 언어 이름(번역 안 함). 한국어에서 "창 모드" 선택지는 "창"(라벨과 겹침 회피).
- 성능 표시: `GameplayConfig.Performance`(키 `Settings.Gameplay.Performance`) + 게임플레이 탭 드롭다운 · `PerformanceOverlay`(AfterSceneLoad 에 스스로 생성, DontDestroyOnLoad, 정렬 32767, 0.5초 갱신). 핑 = `NetworkTransport.GetCurrentRtt(ServerClientId)`.
- 팀장 Play(10-13): 타이틀 설정창 글자가 뭉개지고 흰 컨트롤이 번짐. 원인 = ① 설정창이 RT(1536×1080)에 그려져 3D 모니터에 다시 찍혀 18px 글자가 화면에서 약 12px ② 드롭다운·버튼이 Unity 기본 **흰 스프라이트** → CRT 셰이더 밝기 ×1.25 + 글로우로 흰 면 포화 ③ 남은 CRT 30%. 내용 영역은 710×540 으로 모니터 캔버스(약 1717×1207)의 절반만 썼다.
  - 수정(팀장 선택): **타이틀만 통째로 1.4배**(`TitleFlowDirector.ScaleSettingsBody` — 공유 프리팹·ESC 는 그대로) + **어두운 컨트롤**(`SettingsDarkTheme`, 탭 버튼색 0.16/0.19/0.24 기준, 타이틀·ESC 공통, 항목 배치 메뉴 끝에 자동). 거절: 프리팹 글자 자체 키우기(ESC 까지 커지고 그래픽 12줄이 넘침) · 설정창 CRT 완전 끄기.
  - 에디터 Play 스크린샷으로 확인: 번짐 없음, 그래픽 12줄 모니터 안, Options 제목과 겹치지 않음(간격은 좁음).
  - 팀장 2차 Play(10-13): 키운 뒤에도 글자가 흐림 → CRT 탓으로 판단. 설정창 CRT 감소 70% → **95%**(`TitleMonitorDisplay.SettingsCrtReduction`). 셰이더에 곡면 왜곡은 없음 — 흐림 원인은 색수차(2텍셀)·글로우(4텍셀)·주사선·노이즈·떨림, 모두 감소 목록에 포함. ⏳ 팀장 눈 확인.
  - ⚠️ 정정(10-13): CRT 를 1(완전 끔)로 해도 흐렸다 — **진짜 원인 = 렌더 스케일 50%**(팀장 저장값). 모니터 글씨는 3D 표면이라 URP 렌더 스케일을 맞는다. 100% 로 올리자 선명(팀장 확인). 인게임 UI 는 전부 오버레이 캔버스(데미지 팝업도 오버레이 캔버스 아래)라 무관 — 글씨를 3D 로 그리는 곳은 타이틀뿐.
  - 수정(팀장: "렌더 스케일·품질 프리셋은 글씨 빼고"): `GraphicsQualityConfig.SetForceFullRenderScale` — `TitleMonitorDisplay` 켜져 있는 동안 URP 렌더 스케일 100%, 꺼지면 저장값. 저장값·설정창 표시는 불변(전투 맵부터 적용). CRT 감소율은 팀장이 1f 로 둠(유지).
  - 인스펙터 노출(팀장 10-13 — 아트가 스크립트를 열지 않고 Play 중 바로 조절): `TitleMonitorDisplay` = CRT 화면 효과 10개(색수차·주사선 개수/세기·글로우/반경·노이즈·떨림·밝기·비네트·색조) + **설정창 CRT 감소율 0.75**(팀장 의도값). 🔴 이제 CRT 값의 정본은 이 컴포넌트 — 머티리얼 `MA_TitleMonitorUI` 값은 안 쓴다(기본값 = 10-13 머티리얼 값, 화면 동일 확인). `TitleFlowDirector` = 전환 지직거림 5종(접근·메뉴·설정·시작·종료, x=세기 y=길이) + 설정창 배율 1.4. `TitleCrtFx` 찢김·상시 지직거림은 원래 필드 — 툴팁만 추가. 모두 OnValidate/매 프레임으로 Play 중 반영.
- 팀장 빌드 확인 후 수정(10-13 · 11:09 재빌드 성공):
  - VSync 60 고정 = 정상(이 PC 모니터 3840×2160 @ 60Hz).
  - 상호작용 "Hold 1" = 키 이름에 액션의 Hold 인터랙션이 붙음 → `DisplayName` 에 `DontIncludeInteractions`(설정창 키 표시도 같이). 라벨 크기 38.4 → 최대 24·자동 맞춤 최소 12(`InteractPrompt.prefab`).
  - 물이 떠 보임 = **MSAA 4x 와 안개 피처 충돌**(추정 — 피처가 결과를 MSAA 1 컬러로 갈아 끼우는데 깊이는 MSAA 4 → 반투명 물이 깊이 판정 없이 그려짐). AA 를 MSAA → **카메라 FXAA/SMAA** 로 교체(MSAA 는 에셋 원래 값 유지). 맵 카메라는 원래 SMAA 였음 → 상 프리셋 = SMAA, 중 = FXAA, 하 = 끔. 저장 키 새로(`Settings.Graphics.AntiAliasing`). 타이틀 동안 AA 끔(글씨). 그래도 문제면 항목 제거 검토(팀장).
  - 분홍 타원 = 저체력 비네트(HP 18/220) — 설정 비네트(안개 시야 디밍)와 별개였다. 팀장 결정: **저체력 비네트도 설정 크기/경계로 조절**(`HealthVignetteHUD` 생성 텍스처 반경 × 크기, 폭 × 경계). 실드 비네트는 미적용.
- 창 조작(팀장 10-13 · 11:25 빌드): `WindowFocusKeeper`(빌드 전용, 런타임 생성) — **Alt+Enter** 창 ↔ 테두리 없는 전체 창 전환 + 설정 저장(Unity 자체 allowFullscreenSwitch 와 같은 모드로 맞춤) · **커서 가두기**(포커스 중 Confined). 처음엔 "Alt+Tab"으로 잘못 듣고 복귀 시 모드 복원을 넣었다가 뺐다.
- ⚠️ 빌드 1차 실패(10-13): Windows 애플리케이션 제어 정책이 `Unity.Burst.Cecil` 로드를 차단(0x800711C7) — 코드 무관. 팀장 Windows 설정 확인 후 **2차 빌드 성공**(10:55, `../MainProjectBuilds/Windows/`). ⏳ 팀장 빌드 확인.

## ▶ 다음 세션 (10-13~)
1. **한국어 추가(설정창부터)** — 언어 버튼 = English/한국어 전환. 팀장: 정식 로컬라이제이션(SO 또는 엑셀 파서)은 담당자가 하지만 설정창 문구 정도는 지금 해도 된다 → 한국어로 전부 확인. ⚠️ 시작 전 grill: 저장 방식(SO 테이블 / 코드 사전 / xlsx 시트) · 전환 범위(설정창만 / 타이틀 메뉴 포함) · 로컬라이제이션 담당과 키 이름 규약 맞출지. 글꼴 = NotoSansKR(한글 11,172자 포함 — 제약 없음).
2. **빌드 확인** — 해상도 3후보·모니터보다 큰 후보 숨김 · 창 모드 3종 + 10초 되돌림 · 렌더 스케일·그림자·AA·후처리·프리셋 · 비네트 크기/경계 · Alt+Tab 음소거 · 첫 실행 1440(레지스트리 PlayerPrefs `Settings.*` 지우고) · 재실행 값 유지.
3. 마우스 감도(보류 — 소프트웨어 커서 vs OS 커서 보정, 그래픽 확인 뒤).
4. 예외(나중): 키 가이드 HUD(이미지) · runInBackground(호스트 Alt+Tab 서버 정지).
5. 커밋 범위 결정: `feature/Settings`(10-12 작업 전부 미커밋) → Boss23·development 반영 여부. 은희 튜토리얼 브랜치가 `4.MapScene` OptionPanel 을 건드렸는지 머지 때 확인. 은희 `feature/DashDirectionOption` 은 흡수함(따로 머지 금지 — 공유 필요).
- ⚠️ 정정(10-13 빌드 · 팀장): "비네트 크기"를 줄였더니 오히려 크게 그려짐 — 값을 **보이는 범위 배율**로 곱해 방향이 반대였다(안개 시야 반경·저체력 비네트 투명 반경 둘 다). → `GraphicsQualityConfig.VignetteOpenScale = 1 / vignetteSize` 로 뒤집음(크기 ↑ = 어두운 테두리 ↑). 기본 100% 화면 동일. ⏳ 팀장 확인.
- 팀장 후속(10-13): 실드 비네트(`ShieldVignetteHUD`)도 같은 설정을 따르게 연결 — 처음엔 저체력만 연결해 실드는 그대로였다. 계산 공용화 `VignetteSpriteFactory.ApplySettings`. 최소 크기에서 경고 비네트가 통째로 사라지던 것 → 투명 반경 상한 1.05(화면 모서리에 남음).
- 팀장 후속(10-13): 비네트 슬라이더 표시 70~150% / 50~200% → **0~100(50 = 기본)**. 아래 반 = 최소~1, 위 반 = 1~최대(`GraphicsQualityConfig.Vignette*FromSlider/ToSlider`, 저장값은 배율 그대로 — 기존 설정 유지). "후처리 (블룸 등)" → **"블룸·색수차" / "Bloom & Chromatic Aberration"**(실제로 끄는 두 효과의 게임 표준 이름). 설정 EditMode 26/26.
