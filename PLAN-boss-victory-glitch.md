# PLAN — 보스 처치 연출 교체: ZZZ "WIPEOUT" 글리치 (2026-11-09 사용자 확정)

> **원본 문서**(브랜치 `feature/BossVictoryDebugScene`). 옛 처치 연출 문서 [PLAN-boss-victory-cinematic.md](PLAN-boss-victory-cinematic.md) 는 Legacy 기록.
> 진행(11-09): 1 Legacy 이동 ✅ · 2 시간 배율·소거·처치 연출 스킬 ✅ · 3 글리치 렌더·흑백·보스 노이즈·SO 타임라인 ✅ · 4 카메라 컷 구조 ✅(기본 목록 비움 — 게임 카메라만) · 사양 변경: 글리치 = 처치 판정 즉시 ✅ · 5 Rectify 글자 ✅ → **11-09 제거**(사용자) · 6 유지 → CRT 꺼짐 → 로비 결과 ✅ · 7 디버그 랩·문서 ✅ · **타임라인 에디터 창 ①~⑤ ✅**(곡선 → 클립 목록·마커, 창 `Tools/Boss Victory/처치 연출 타임라인`) — 아래 "진행 기록". ⏳ Unity 컴파일·셰이더·EditMode·Play(이 레인은 Unity 없음).

## 지금 흐름 한눈에 (최종 — 아래 진행 기록이 근거)
| 처치 판정 뒤(초, 게임 시간) | 일 | 어디서 |
|---|---|---|
| 0 | 서버: 막타·보스·시각 복제 · 무적 · 연출 잠금(스킬 취소·입력 차단) · 장판·투사체 디스폰. 각 피어: 이펙트 소거 · 그 외 플레이어 0.05 · 보스 0 · **글리치 시계 시작** | `TryStartVictoryServer` · `BossVictoryGlitchPresentation.Begin` |
| 0 ~ 스킬 End | 처치자: 하던 동작 ×0.5 그 클립 끝까지 → 백대쉬(등장 연출 밀림, ×1) → 처치 연출 스킬(기본 R, 표시 전용, ×1) → End 에서 처치자·보스 ×0.1 | `StartKillerSequence` · `BossEncounterDirector.StepSkid` · `PlayPresentationOnly` · `OnFinisherEnded` |
| 0.08 ~ 3.5 (클립) | 글리치 = **가로줄 좌우 찢김 + 찢긴 줄 밝은 부분 끌림만**(11-09) · 색: 흑백·호박색·주사선·보스 노이즈(카메라 이동 UV) — 흑백 플래시 삭제(11-09) · HUD 숨김 | `BossVictoryGlitchRender` + 셰이더 2개 |
| 0.20(마커 2개 — 첫 찢김 최고점) | 처치자 아닌 피어 카메라 → 처치자 즉시 컷 · 처치자 외 플레이어 모든 화면에서 숨김(렌더러·캔버스·머리 위 체력바) — 되감거나 정리 때 복구(11-09) | `TickMarkers` |
| (컷) | 기본 없음(게임 카메라) — 목록에 넣으면 끊어 붙임 | `BossVictoryCameraCuts` |
| 3.5 ~ 4.5 | 정지 유지 클립(화면 효과 시각을 클립 시작에 고정) | `BossVictoryTimeline.EffectTime` |
| 4.5 ~ 5.6 | TV 꺼짐 클립(`Title/CRTOff`, 시작에 캡처·진행 = 클립 안 위치) → 검정 | `BossVictoryCrtOff` |
| 5.6(로비 전환 마커) | 호스트 화면이 지나면 서버가 결과 전환 → 로비(결과 패널) | `TickCrtAndLobby` → `TickVictoryServer` |
수치는 전부 `Resources/BossVictory/BossVictoryGlitchSettings.asset` — 시각·길이는 타임라인(`timeline`, 창 `Tools/Boss Victory/처치 연출 타임라인`). 디버그 = `0.Scenes/Debug/DebugBossVictory`(Dev Boot, 솔로 호스트, F7 처음부터·F8 일시정지·배속 · 창 재생 헤드 스크럽).

레퍼런스: `C:\Users\user\Desktop\Top WIPEOUTS and CAKEOUTS ver. 4 (3.1) - Zenless Zone Zero [KfjGNVzCIdA].webm` 0~1.2초.
프레임 시트: `_lane-artifacts/Work2/zzz_sheet.png`(0.1초 간격), `zzz_sheet2.png`(0.28~0.70초 30fps).

## 확정 사항
- Q1 대상 = **보스 처치 연출**(BossEncounterDirector.Victory).
- Q2 기존 처치 연출의 카메라 무빙·카메라 프리셋/슬라이더·주황 틴트·회색 원(246)·슬로우/줌 등 **전부 제거** → `Legacy/` 폴더로 이동(guid 유지), 런타임 연결 끊음. 처치 판정·복제 상태(VictoryState)·결과 전환 흐름은 유지.
- Q3 클로즈업 대상 = **막타 플레이어 1명을 모든 피어가 봄**. 막타 = 기존 복제값 `VictoryState.KillerClientId`(새 RPC/복제 값 없음).
- Q4 글자 = **"Rectify"**(Strings 키 신설, 글꼴 서울알림). 글자 색 채우기 순서는 레퍼런스처럼 왼쪽부터 다색.
- Q5 포즈 = 대기 동작 + 승리 애니 칸(비면 대기).
- Q6 연출 1.2초 → **유지 1초(SO 변수)** → **타이틀→로비 때 쓰는 모니터 CRT 꺼짐과 같은 효과로 화면 암전**(`TitleMonitorDisplay`/`TitleCrtFx` 꺼짐 재사용 또는 같은 셰이더 화면판) → **로비(결과 패널)**.
- Q7 캐릭터만 흑백 = 플레이어 레이어 마스크 방식(렌더 패스).
- Q8 Work2 `feature/BossVictoryDebugScene` 이어서. 디버그 씬으로 확인.
- **흰 링(충격파 2개)은 넣지 않음.**

## 처치 직후 대상별 시간 배율 (2026-11-09 사용자 추가)
전역 Time.timeScale 이 아니라 **대상별 로컬 시간 배율**(애니메이터 속도·이동·이펙트).
| 대상 | 보스 처치 순간 | 처치자 연출(처치 애니/특정 스킬) 끝난 뒤 |
|---|---|---|
| 처치자 | 1 | 0.1 |
| 처치자 외 플레이어 | 즉시 0.05 | 0.05 |
| 보스 | 마지막 피격 즉시 0 | 0.1 |
- Q9 = **처치 전용 연출(애니 또는 특정 스킬)을 새로 재생**하고 그 끝(End 애니 이벤트)에서 0.1 로. 캐릭터별(Player Variant/SO) "처치 연출" 칸 — 클립 또는 스킬 지정. 칸이 비면 막타 스킬의 End 이벤트로 대체. 새 타이머 필드 금지(클립 이벤트로 튜닝).
- Q10 = 글리치는 처치자가 0.1 로 느려지는 순간 시작, 클로즈업 컷은 글리치 속.
- Q11 = **처치 판정 즉시** 처치자의 처치 연출 이펙트를 제외한 보스·모든 플레이어의 스킬 이펙트·장판·데칼·투사체 등 **전부 소거**(서버 판정 장판은 서버에서 제거, 표시는 각 피어). 이후 남는 것은 처치자 처치 연출 이펙트뿐.
- Q12 = 각 피어가 자기 화면의 처치자 애니 이벤트로 판단(새 RPC·복제 값 없음, 피어 간 미세 차이 허용).

## 타임라인(전부 SO 곡선/시각으로 조절)
| 시각 | 요소 |
|---|---|
| 0.00~0.28 | 처치 순간(평소 화면) |
| ~0.31 | 가로줄 물결 왜곡 시작 · 채도 빠짐 · HUD 페이드 아웃 |
| 0.35~ | 완전 흑백 + CRT 주사선(끝까지 유지) · 계단식 세로 어긋남 · 가로 늘어남 |
| ~0.41 | **카메라 컷 → 막타 캐릭터 정면 클로즈업**(글리치 속에 숨김) · "Rectify" 외곽선 옅게 · 블록 모자이크 글리치 |
| 0.44~0.48 | 가로 스미어(캐릭터 실루엣) · 아래부터 노란색 차오름 |
| 0.55~0.70 | 배경 호박색 틴트(캐릭터는 흑백 유지) · 글자 왼쪽부터 다색 채움 |
| ~0.80 | 슬라이스 글리치 + RGB 분리 1회 |
| 0.90~1.00 | 글자 흰→노랑 글로우 섬광 · 캐릭터 글리치 1회 |
| 1.0~1.2 | 안정 |
| +유지(기본 1초) | 정지 화면 |
| → | CRT 꺼짐 암전 → 로비 결과 패널 |

## 구현 단위(커밋 단위)
1. 기존 처치 연출 Legacy 이동 + 연결 끊기(처치→결과 흐름만 남음, 컴파일 확인).
2. 화면 글리치 렌더 패스(흑백·틴트·주사선·물결·블록·슬라이스/RGB) + 플레이어 레이어 흑백 마스크. 세기는 하나의 SO 타임라인.
3. 클로즈업 카메라 컷(막타 캐릭터 정면, 위치·거리·높이 SO) + 포즈.
4. "Rectify" 글자 UI(외곽선→다색 채움→섬광) + Strings 키.
5. 유지 → CRT 꺼짐 암전 → 로비 결과 패널 연결(기존 결과 전환 하한 시각과 맞춤).
6. 디버그 씬에서 재생 버튼·문서(`PLAN-boss-victory-cinematic.md` 갱신).

## 리스크
- 렌더 패스·셰이더는 Unity 에서만 검증 가능.
- 클라이언트마다 처치 시각이 서버 시각 기준이어야 컷 타이밍이 같음(기존 VictoryState 시각 재사용).
- 결과 전환을 연출 끝까지 붙잡는 기존 하한(`_victoryMinResultAt`) 값을 새 길이로 바꿔야 함.

## 검증
- 디버그 씬에서 재생 → 시간표대로 보이는지.
- MPPM 호스트/클라 둘 다 같은 막타 캐릭터 클로즈업, 같은 타이밍.
- 끝나고 CRT 꺼짐 → 로비 결과 패널.

## 진행 기록
### 1단계 — 옛 처치 연출 Legacy 이동 (11-09)
- 이동(guid 유지): `Map/BossVictory/{BossVictoryConfig,BossVictoryPresentation,BossVictoryRules}.cs`·`Editor/{BossVictoryConfigAuthoring,BossVictoryRulesTests}.cs` → `Map/BossVictory/Legacy/` · `Rendering/BossVictory/{BossVictoryRender.cs,Shaders/}` → `Rendering/BossVictory/Legacy/` · `Dev/BossVictoryLab/BossVictoryCameraPresets.{cs,asset}` → `Dev/BossVictoryLab/Legacy/`. Legacy 코드는 컴파일만 된다(아무도 부르지 않음).
- `Resources/BossVictory/BossVictoryConfig.asset` 은 제자리(사용자 값 커밋 719635a2) — 런타임이 더는 읽지 않는다.
- 연결 끊기: 디렉터가 `BossVictoryPresentation` 을 붙이지 않는다 · `TryStartVictoryServer` 는 막타·타격점·보스·시각만 복제(구도 계산·추가 연출 구간·2.00초 잠금 없음) · 사망 표현 배율 = 1(대상별 시간 배율은 2단계 이후) · 결과 전환 하한 없음(5단계에서 새 길이로).
- `VictoryState` 의 구도 칸(Final/End/Fov)·`PreludeSeconds`·`StageTime` 은 Legacy 표시가 컴파일되도록 남겨 둔 빈 칸이다(새 코드는 안 씀).
- 랩: 카메라 구도 슬라이더·라이브·프리셋·회색 원 슬라이더·점프·시계 밀기 삭제. 남은 것 = 배치·팀원·처음부터·일시정지·배속(Time.timeScale)·자동 반복. 랩 규칙 테스트 3개(단계 이름·점프·시계) 삭제.
- 레퍼런스 프레임 시트는 저장소 밖 `C:UnityProject_lane-artifactsWork2zzz_sheet*.png`(4MB — git 에 안 넣음).

### 2단계 — 대상별 시간 배율·이펙트 소거·처치 연출 스킬 (11-09)
사용자 결정(11-09): 처치 연출 = **스킬 재생**(표시 전용 — 판정·쿨다운·자원·RPC 없음) · End 는 그 클립 이벤트에서만(대체 분기·안전망 타이머 없음, End 없으면 EditMode 테스트·실행 오류) · 시간 배율·소거를 2단계로 · 소거 1차 범위 = 추천안.
- **표시 컴포넌트** `Map/BossVictory/BossVictoryGlitchPresentation`(디렉터가 런타임 부착, 모든 피어) — 복제 상태(막타·보스·처치 시각)만 읽는다.
- **처치 판정 즉시**
  - 서버(`TryStartVictoryServer`): 전원 무적 · 전원 연출 잠금(`PlayerEncounterLock.SetCinematicInputLockServer` — 실행 중 스킬·평타 취소 + 입력 차단) · `AreaZone`·`MonsterProjectile` 전부 디스폰.
  - 각 피어: `EffectManager.ReleaseAllImmediate()`(새 API — 전 이펙트 즉시 반납) · 23호 `StopPresentationEffectsLocal()` · `DefaultAttackProjectile` 파괴 · 입력 보류(`PlayerLifeInputPolicy.SetVictoryInputHeld`).
- **처치 연출 스킬(Q9)** 칸 = 캐릭터 Variant 의 `PlayerVictoryFinisher.finisherSlot`(지금 아무 Variant 에도 안 붙임) → 없으면 설정 `defaultFinisherSlot`(기본 **R 궁극기**). 각 피어가 막타 캐릭터의 실행 중 스킬이 없어진 뒤(서버 취소가 닿은 뒤) `PlayerSkillController.PlayPresentationOnly(skill, 보스 방향)` — 그 스킬의 `AnimatorStateName` CrossFade + `OnClientPlay`(이펙트). 애니 이벤트는 표시 경로(`OnLocalAnimationEvent`)만, End 에서 `OnEnd(Completed)` + `PresentationSkillEnded`.
- **대상별 시간 배율**(각 피어 로컬 — Animator.speed 를 실행 순서 10001 에서 덮음 + 몸 아래 파티클 simulationSpeed + `EffectManager.SetPlayRateForTarget`)
  | 대상 | 처치 판정 ~ End | End 이후 |
  |---|---|---|
  | 처치자 | 1(덮지 않음) | `killerAfterFinisherScale` 0.1 |
  | 그 외 플레이어(랩 팀원 복제본 포함) | `otherPlayersScale` 0.05 | 0.05 |
  | 보스(웰즈 포함) | 0 | `bossAfterFinisherScale` 0.1 |
  서버 사망 표현 대기도 호스트 화면의 보스 배율로 흐른다(`VictoryDeathTimeScaleFor` → `BossTimeScale`).
- End 순간 = `BossVictoryGlitchPresentation.FinisherEnded`(static event) — **글리치 시작 신호**(3단계가 구독).
- 설정 SO `Resources/BossVictory/BossVictoryGlitchSettings.asset`(클래스 `BossVictoryGlitchSettings`) — 단계마다 칸을 더한다.
- 검증 `BossVictoryFinisherTests`(필터 ^BossVictoryFinisher) — 팔라딘·거너·어쌔신 처치 연출 스킬 시작 상태 클립에 Skill End.
- 알려진 위험: 스킬 `OnClientPlay` 가 오너 화면에서 이동(돌진류)까지 하면 처치자가 움직인다(오너 권위 복제라 전 피어 같은 모습) · 평타 취소의 자세 복귀가 처치 연출 시작보다 늦게 닿으면 덮을 수 있다(스킬 취소는 기다리지만 평타는 안 기다림) — Play 에서 확인.

## 타임라인 갱신 (11-09 — 레퍼런스 `zzz_sheet3_boss_32s.png`·`zzz_boss_ref.png` 직접 확인)
기준 t = **글리치 시작**(처치자 처치 연출 스킬 End = 처치자 0.1 순간). 값은 전부 설정 SO(곡선·시각·컷 목록).
| t(초) | 화면 | 컷 |
|---|---|---|
| 0.00 | 평소 화면(처치 연출 스킬 끝 자세) | 게임 카메라 |
| ~0.17 | 흑백 + "Rectify" 외곽선 글자 + 보스 자리 흰 글리치 덩어리 · HUD 페이드 아웃 · 물결/계단 어긋남 | 게임 카메라 |
| ~0.33 | **컷 1 — 와이드**(처치자 흑백 + 보스, 배경 호박색) · 글자 부분 채색 | 와이드 |
| ~0.50 | 글자 왼쪽부터 다색 채움 | 와이드 |
| ~0.67 | 글자 흰 섬광 | 와이드 |
| 0.80~1.20 | 글자 노랑 안정 | 와이드 |
| ~1.33 | 흑백 플래시 글리치 | 와이드 |
| ~1.50 | 슬라이스 + RGB 분리 | 와이드 |
| 1.67~2.33 | **컷 2 — 보스 클로즈업**(보스 몸이 화면을 채움, 처치자는 뒤에 작게) | 보스 클로즈업 |
| ~2.50 | 글리치 전환 | — |
| 2.67~3.50 | **컷 3 — 와이드로 복귀**, 안정 | 와이드 |
| +유지(기본 1초, SO) | 정지 화면 | — |
| → | CRT 꺼짐 암전 → 로비 결과 패널 | — |
- 컷은 **목록 SO**(시작 시각 · 종류 = 와이드/보스 클로즈업/처치자 정면 클로즈업 · 거리·높이·방위). 기본 = 와이드 → 보스 클로즈업 → 와이드. 처음 계획의 "처치자 정면 클로즈업"은 종류로 남겨 목록에 넣으면 쓴다(기본 목록엔 없음 — 레퍼런스엔 처치자 클로즈업이 없다).
- 전체 길이 ≈ 3.5초 + 유지 1초 → 암전. 처음 계획의 "1.2초"는 글자 안정까지의 길이였다.
- **색**: 처치자·팀원(플레이어) = 흑백(렌더링 레이어 비트 마스크) · 배경 = 호박색 틴트 · 글자 = 노랑.
- **보스 = 노이즈 텍스처 UV 애니메이션**(사용자 정정 11-09 — 과노출+줄무늬 아님): 보스 실루엣(렌더링 레이어 비트) 안에 흑백 노이즈 `BossNoise_Stripes.png`(흰/검 가로 사선 쐐기 띠)를 흘린다. 흰 = 과노출 밝은 색, 검 = 어두운 적갈색(색 2칸 SO). 주 흐름 = 가로 스크롤(속도·방향·타일링 SO).
  - UV 공간: **화면 공간 기본**(레퍼런스에서 와이드·클로즈업 모두 띠 굵기가 화면 기준으로 거의 같다) + **오브젝트(보스 기준) 토글 칸**으로 둘 다 지원.
  - 외곽 가로 지글(실루엣 가장자리 가로 흔들림)은 글리치 패스가 같이 처리.
- 흰 링(충격파)은 넣지 않는다(그대로).

### 3단계 — 화면 글리치 렌더 패스·플레이어 흑백·보스 노이즈·SO 타임라인 (11-09)
- **렌더** `Rendering/BossVictory/BossVictoryGlitchRender`(static) — 렌더러 에셋 무수정, 연출 동안만 `beginCameraRendering` 에서 게임 카메라에 패스 2개(옛 BossVictoryRender 방식). 마스터 세기 0 이면 비용 0.
  1. 마스크(반투명 전): 렌더링 레이어 비트 4 = 플레이어(처치자·팀원·랩 복제본), 6 = 보스 → ID 텍스처(R 플레이어 · B 보스). 셰이더 `Hidden/Rendering/BossVictoryGlitchMask`(깊이 비교 + 여유 8cm).
  2. 합성(반투명 뒤, 후처리 전): `Hidden/Rendering/BossVictoryGlitch` — 가로 늘어남·물결·계단·슬라이스·블록 모자이크(마스크도 같은 왜곡 UV) → RGB 분리·스미어 → 흑백 → 배경만 호박색(플레이어는 흑백 유지) → 보스 노이즈 → 흑백 플래시 → CRT 주사선.
- **보스 노이즈** = `Assets/3.Materials/BossVictory/BossNoise_Stripes.png`(사용자 제공 1024², Repeat·선형·밉맵) — 실루엣 안에서 흑백을 밝은 색(`bossNoiseBrightColor` × `bossNoiseBrightIntensity` 1.6 — 과노출)·어두운 적갈색(`bossNoiseDarkColor` #3A1712)으로. ~~스크롤 `bossNoiseScroll`~~(11-09 삭제 — 아래 "노이즈 UV = 카메라 이동")·타일링. 공간 `bossNoiseSpace` = **Screen(기본 — 화면 세로 1)** / Boss(보스 화면 중심·높이 기준 — 보스와 같이 움직이고 클로즈업에서 커짐). 외곽 가로 지글 `bossJiggle`.
- **SO 타임라인**(`BossVictoryGlitchSettings` "글리치 타임라인") — 곡선 16개(master·desaturate·amber·scanline·wave·stepShift·stretch·block·smear·slice·rgbSplit·flash·characterGlitch·bossNoise·bossJiggle·hudHide), 길이 `glitchDuration` 3.5초(뒤로는 마지막 값 정지). 기본 곡선은 위 "타임라인 갱신" 표에 맞춘 코드 기본값(에셋에 곡선을 안 써 둬서 인스펙터에서 처음 고치는 순간 저장된다). 블록 펄스가 컷 전환 시각(0.33·1.67·2.6)을 가린다(컷은 4단계).
- 시계 = 표시의 `GlitchTime`(게임 시간 — 랩 배속·일시정지에 같이 멈춤, 실게임은 timeScale 1). HUD 숨김 = `CinematicOverlay.HideHud`(hudHide > 0.5), 정리 때 복구.
- 셰이더 2개·텍스처는 설정 에셋이 참조(빌드 포함).
- 알려진 한계: 셰이더는 오프라인 컴파일 불가(Unity 에서만 확인) · 기본 곡선 세기는 레퍼런스를 눈으로 맞춘 첫 값.

### 4단계 — 카메라 컷(목록 SO) (11-09)
- 순수 계산 `Map/BossVictory/BossVictoryCameraCuts`(테스트 `BossVictoryCameraCutTests`, 필터 ^BossVictoryCameraCut) · 목록 = 설정 `cuts`(시작 시각·종류·거리·내려다보는 각·방위·초점 높이·FOV).
  - 종류: **Wide**(처치자·보스 가운데) · **BossCloseup**(보스 렌더러 바운즈 중심) · **KillerFront**(처치자 발밑 + 초점 높이 — 기본 목록엔 없음).
  - 방위 기준 = 처치자 → 보스 수평 방향: 0 = 처치자 쪽에서 보스를 봄, 180 = 반대편에서 처치자 쪽을 봄. 카메라 = 초점 − 시선 × 거리.
  - 기본 = 와이드(0.33, 거리 16·각 22·방위 50) → 보스 클로즈업(1.67, 7·6·−160 — 보스 너머에서 처치자 쪽, 처치자는 뒤에 작게) → 와이드(2.6). **첫 값 — Play 에서 보스 크기에 맞춰 조정 필요.**
- 시계 = 글리치 시각. 첫 컷 전엔 게임 카메라, 첫 컷에서 `CinematicOverlay.EnterCamera(블렌드 0)`, 이후 컷은 `PlaceCamera`(끊어 붙임 — 전환은 블록 펄스가 가린다). 구도는 **컷이 바뀌는 순간 이 피어가 본 위치로 한 번 계산해 고정**(복제 위치라 피어 간 미세 차이 허용, 새 RPC 없음).
- 첫 컷에 들어갈 때 한 번: 월드 UI(머리 위 체력바 등)·플레이어 실루엣·적 외곽선·벽 가림(WallOcclusion·구역 벽 투명화)·카메라 흔들림 리스너 끔 + 반경 `wallSearchRadius` 40m 안 벽 후보 → 컷마다 앞면이 카메라를 향하지 않는 벽 숨김(`wallFacingThreshold` 0.3 — 옛 연출 할 일 200 과 같은 기준, 함수는 Legacy 에 기대지 않게 옮김). 디밍 기준점 = 컷 초점.
- 정리(랩 처음부터·씬 끝): 벽·월드 UI·흔들림·억제 플래그 복구 → `CinematicOverlay.Dispose`(HUD·카메라·디밍).

### 사양 변경 — 글리치는 처치 판정 즉시 (11-09 사용자)
- **글리치(흑백·호박색·주사선·보스 노이즈·HUD 숨김·컷 등 전부) 시계 기준 = 처치 판정 순간**(복제 `VictoryState.StartServerTime` — 늦게 받은 피어는 그만큼 앞에서 시작, 이후는 게임 시간). 처치 연출 스킬 End 는 **글리치 시작 신호가 아니다**.
- 처치 연출 스킬은 글리치가 진행되는 동안 처치자가 재생한다(그대로 — 실행 중 스킬 취소가 닿은 뒤).
- 시간 배율(해석 — 사용자 확인 필요): 처치 순간 그 외 플레이어 0.05·보스 0 그대로 · 처치자는 스킬 재생 중 1 → End 에서 처치자·보스 0.1(그대로). `FinisherEnded` 는 0.1 순간 알림으로만 남김.
- 이 문서 위쪽 표·3·4단계 기록의 "t = 글리치 시작 = End" 는 "t = 처치 판정 순간"으로 읽는다. 기본 곡선·컷 시각 숫자는 바꾸지 않았다(처치 연출 스킬 길이만큼 앞당겨진 셈 — Play 에서 조정).
- 나중 계획: 카메라가 움직이며 처치자 애니를 보여 준다(아직 안 함). 4단계 고정 컷(0.33 와이드부터)과 겹치는 문제 → 보고·질문 중.
- **결정(11-09 사용자 2번)**: 기본 컷 목록 = **빈 목록**(게임 카메라만). 컷 구조·종류·테스트는 남겨 두고 처치자 애니를 보여 주는 카메라 이동과 함께 다시 짠다(옛 기본값은 `cuts` 툴팁에 예시로). 시간 배율 해석(처치자 1 → End 0.1 · 그 외 0.05 · 보스 0 → End 0.1)은 사용자 원래 사양과 같다고 확인.

### 5단계 — "Rectify" 글자 (11-09)
- `Map/BossVictory/BossVictoryRectifyText`(로컬 표시, 연출 오버레이 캔버스 위 런타임 생성 — 글리치 렌더 패스는 카메라 화면에만 걸려 글자는 깨끗). 처치 판정 순간(글리치 시작)에 만들고 정리 때 지운다.
- 문구 = 코드 키 `UiStrings.BossVictoryRectify`(`UI.BossVictory.Rectify`, ko 폴백 "Rectify") — 시트 행은 다음 Export 때 붙는다(그 전엔 폴백). 글꼴 = 설정 `textFont` = **서울알림 Heavy SDF**(`Assets/50.Art/UI/Fonts/SeoulAlrimTTF-Heavy SDF.asset`, 정적 아틀라스에 R·e·c·t·i·f·y 있음), 비면 디렉터 `introFont`.
- 두 겹: 외곽선 글자(면 투명·노란 외곽선 `textOutlineWidth` 0.18, 세기 곡선 `textOutline` 0.15~0.2) + 채움 글자(글자마다 꼭짓점 색 — 앞머리 `textFill` 0.3→0.6 이 왼쪽부터 지나며 띠 `textFillBand` 0.35 안 글자는 `textFillPalette` 4색으로 24fps 깜빡, 다 지나면 `textColor` 노랑) · 흰 섬광 `textFlash`(0.67, 크기 +5%) · 가로 흔들림 `textJitter`(블록·슬라이스 펄스와 같은 시각, ±24px).
- 위치·크기 = `textViewportPosition`(0.5, 0.5)·`textSize` 220·자간 6.

### 6단계 — 유지 → CRT 꺼짐 암전 → 로비 결과 패널 (11-09)
- 표시: 글리치 시각이 `glitchDuration`(3.5) + `holdSeconds`(1) 를 넘으면 그 프레임 끝 화면을 캡처(`ScreenCapture.CaptureScreenshotAsTexture`, WaitForEndOfFrame)해 맨 위 캔버스(정렬 900)에 `Title/CRTOff`(타이틀 → 로비 꺼짐과 같은 셰이더·수식) 로 띄우고 `_Progress` 0 → 1(`crtOffSeconds` 1.1, 게임 시간). 끝 = 불투명 검정 → `Finished`·`PresentationFinished`. 클래스 `Map/BossVictory/BossVictoryCrtOff`, 셰이더 참조 = 설정 `crtOffShader`(빌드 포함), 발광 색 `crtOffGlowColor`(타이틀 파랑).
- 서버: `_victoryMinResultAt`(시각 하한) → `_victoryResultReady`. `TickVictoryServer` 가 **호스트 피어 표시가 같은 회차를 끝낸 순간** `_resultTransitionAt = 지금` → 기존 `TickResultTransition` → `MapSceneManager.GoToResult()`(로비 + 결과 패널, 기존 흐름). 그 전엔 `HoldsResultForVictory` 가 처치 안전망(12초)도 붙잡는다. 연출 시계는 처치 판정부터 게임 시간으로 흘러 스킬 End 와 무관하게 반드시 끝난다.
- 클라 화면은 자기 시계로 꺼지고, 호스트가 먼저 끝나면 꺼지는 도중 로비로 넘어갈 수 있다(피어 간 미세 차이 허용).
- 총 길이(기본) ≈ 3.5 + 1 + 1.1 = 5.6초(처치 판정부터).

### 7단계 — 디버그 랩·문서 (11-09)
- 랩(`Dev/BossVictoryLab/DebugBossVictoryLab`): 기존 "처음부터 (F7)"·일시정지(F8)·배속·자동 반복 그대로. 패널에 표시 단계·글리치 시각·컷 번호·CRT 꺼짐 끝 표시 · **재생 중 패널 접기**(기본 켬 — 오른쪽 아래 한 줄, CRT 꺼짐 캡처에 IMGUI 패널이 안 찍히게) · 끝 판정 = 호스트 화면 CRT 꺼짐 끝(결과 차단 신호), 안전망 15초 툴팁 갱신.
- 문서: 이 문서 맨 위 "지금 흐름 한눈에" · AGENTS.md 색인 문구 갱신.
- Play 확인(사용자): 아래 6항목 — ① 처치 즉시 흑백·호박색·보스 노이즈·HUD 숨김 ② 처치자 스킬 애니가 글리치 속에서 재생·End 에서 느려짐 ③ Rectify 외곽선 → 다색 채움 → 섬광 → 노랑 ④ 3.5초 뒤 1초 정지 → CRT 꺼짐 → 검정 ⑤ 랩: 끝나면 원상복구 → 반복, 카메라·HUD·벽 복구 ⑥ 실게임/MPPM: 호스트 꺼짐 끝에 로비 결과 패널.

### 변경 — 보스 노이즈 UV = 카메라 이동으로만 (11-09 사용자)
- 시간 기반 자동 스크롤(`bossNoiseScroll` 가로 0.6/초) **삭제**. 매 프레임 `Camera.main` 위치 변화를 카메라 right/up 축에 투영해 오프셋에 누적: 오른쪽 이동 → U 감소 · 왼쪽 → U 증가 · 위 → V 증가 · 아래 → V 감소. 카메라가 멈추면 노이즈도 멈춘다.
- 배율 = `bossNoiseCameraScale`(Vector2, **U·V 따로** — 기본 0.1/m 둘 다). 따로 둔 이유: 화면 비율·탑다운 카메라 특성상 가로·세로 이동량 느낌이 달라 따로 맞추는 게 편하다(같게 쓰려면 같은 값).
- 첫 프레임·컷 전환 프레임(텔레포트)은 누적에서 빼고 기준점만 다시 잡는다. 화면 공간/보스 기준 토글은 그대로, 두 모드 같은 규칙(셰이더 `nUV * 타일링 + 오프셋`).
- 외곽 가로 떨림(`bossJiggle`)·플레이어 떨림·글리치 난수는 **시간 기반 그대로 유지**(24fps 난수) — 사용자 확인 필요.

### 변경 — 처치자 순서: 하던 동작 ×0.5 끝까지 → 백대쉬 → 처치 연출 스킬 (11-09 사용자)
- 순서(각 피어 자기 화면, 새 RPC·복제 값 없음): **처치 순간 처치자가 하던 동작**(평타·스킬 등 — 지금/전이 중 다음 상태가 루프 아닌 클립)을 `killerCurrentActionScale` **×0.5 로 그 클립 끝까지** → **백대쉬(×1)** → **처치 연출 스킬(×1)** → End 에서 처치자·보스 ×0.1(그대로). 대기·이동 루프였으면(하던 동작 없음) 바로 백대쉬.
- 평타/스킬 구분 없음 → 막타 종류 복제 칸은 **추가하지 않음**, "평타 아니면 백대쉬부터" 분기도 없앰(메커니즘 하나).
- 서버: 연출 잠금이 **처치자만** 실행 중 스킬·평타를 취소하지 않는다(`PlayerEncounterLock.SetCinematicInputLockServer(locked, cancelCurrentAction)` 새 인자, 기본 true — 다른 호출 불변). 입력은 막히고 판정은 무적·보스 사망으로 막힌 그대로. 그 외 플레이어는 기존대로 취소 + ×0.05.
- 클립 끝 판정 = 시작 상태에서 다른 상태로 넘어가기 시작했거나 끝 프레임(0.98)에 닿음(옛 연출 `ReachedClipEnd` 와 같은 기준 — 애니 기준, 타이머 없음).
- 백대쉬 = **보스 등장 연출 밀림 재사용**: `BossEncounterDirector.TickSkid` 의 본체를 공용 정적 `StepSkid` 로 뽑아 등장 연출·처치 연출이 같이 씀(등장 연출 동작 불변). 리액션 상태 `SkidReactionStates`(SubSkillGuardIdle / Gunner_E_Backstep / Dodge 중 있는 것) · 거리 `impactSkidDistance` 1.5m · 시간 `impactSkidDuration` 0.35초 · `BossIntroFormation.SkidEase` · 끝 + `ReactionIdleDelay` 0.35초에 Idle → 다음 단계(처치 연출 스킬). 방향 = 보스 반대쪽. 이동은 **처치자 오너 피어만**(motor, 오너 권위 복제), 다른 피어는 리액션 애니만 틀고 위치는 복제로 본다. 시계 = 게임 시간(랩 배속·일시정지 같이).
- 랩 패널에 처치자 단계 표시.
- 알려진 위험: 처치자가 누르고 있어야 끝나는 스킬(방어 유지 등) 중이었다면 입력이 막혀 스킬이 안 끝나 처치 연출 스킬이 시작되지 않을 수 있다(글리치·결과 전환은 독립이라 진행된다) — Play 에서 보이면 처치자 스킬만 백대쉬 시작 때 끊는 처리를 더한다.

### 변경 — "Rectify" 배경 글자 제거 (11-09 사용자 "일단 백그라운드 글자 제거해")
- 5단계 글자를 연출에서 **완전히 뺐다**(끄기 토글 없음 — 되살리려면 git 기록 `d42f2688`). 삭제: `BossVictoryRectifyText`(+meta) · 표시의 생성·갱신·정리 · 설정 SO 글자 칸(글꼴·위치·크기·색·팔레트·곡선 4개) · 에셋 `textFont` 참조 · Strings 코드 키 `UiStrings.BossVictoryRectify`(`UI.BossVictory.Rectify`, 시트 행은 아직 Export 전이라 없음).
- 위 5단계 기록은 이력으로 남긴다. 흐름표에서 글자 줄을 뺐다.

### 변경 — 글리치는 가로줄 좌우 찢김만 · 보스 외곽 떨림 삭제 (11-09 사용자)
- 사용자: "글리치 효과는 양 옆으로 찢어지는 효과만 넣어, 사각형 노이즈 절대 넣지 마." (레퍼런스 `_lane-artifacts/Work2/glitch_ref_tear.png` — 가는 가로줄마다 다른 길이로 좌우 밀림·밝은 부분 가로 끌림 / 싫은 것 `glitch_current_bad_blocks.png` — 네모 블록·모자이크) + "보스 자체의 노이즈도 제거해"(보스 실루엣 가장자리 들쭉날쭉·조각 튐).
- **삭제(토글 없음)**: 블록 모자이크 · 계단식 세로 어긋남 · 가로 늘어남 · 물결 · 굵은 띠 슬라이스 · RGB 분리 · 플레이어 실루엣 띠 흔들림 · **보스 외곽 가로 지글** — 셰이더 코드·곡선(`wave`·`stepShift`·`stretch`·`block`·`slice`·`rgbSplit`·`characterGlitch`·`bossJiggle`)·칸(`waveFrequency`·`blockPixelSize`·`sliceCount`)·Frame 칸 전부.
- **남김 = 가로줄 찢김**: 줄 굵기 `tearLinePixels` 3px × 1~`tearLineMaxMultiplier` 3(4줄 묶음마다 섞임, 픽셀 기준) · 켜지는 줄 비율 `tearDensity` 0.35 × 곡선 `tear` · 밀림 = 좌우 무작위 × r² × `tearMaxShift` 0.12(화면 너비 — 짧은 밀림이 많고 긴 것은 드묾) · 무늬 갱신 `tearRefreshRate` 24/초 · 끌림 = 찢긴 줄에서만 밀린 방향 뒤 6탭 중 밝은 값(`smear` 곡선 · 길이 `tearStreakLength` 0.08). 마스크도 같은 찢김 UV 라 인물·보스는 찢긴 줄에서만 같이 밀린다.
- 기본 곡선: `tear` 펄스 0.2·0.33·1.52·1.67·2.55 · `smear` 0.2·0.33·1.52·2.55.
- **유지(글리치 아님)**: 흑백 · 호박색 틴트 · CRT 주사선 · 보스 내부 노이즈 텍스처(카메라 이동 UV — 아래 결정으로 유지). ~~흑백 플래시~~(아래에서 삭제).

### 변경 — 흑백 플래시 삭제 · 보스 내부 노이즈 유지 (11-09 사용자 결정)
- 사용자: 1) 보스 안쪽 줄무늬 노이즈(카메라 이동 UV)는 **유지**. 2) 화면이 하얗게 번쩍이는 흑백 플래시는 **삭제**(토글 없음).
- 삭제: 셰이더 플래시 줄 · 곡선 `flash` · Frame `Flash` 칸. `_GlitchB.w` 는 비워 둠(0 — 미사용).

### 변경 — 흑백 시작 순간: 피어 카메라 처치자로 · 처치자 외 캐릭터 숨김 (11-09 사용자)
- 사용자: "흑백 효과 들어올 때 피어들 카메라 처치자로 포커스 옮기고 피어들의 캐릭터는 SetActive false."
- **시각** = 설정 `desaturate` 곡선이 0 을 넘는 첫 프레임(기본 0.10초) — 새 시각 칸 없음, 곡선을 옮기면 같이 옮겨진다. 늦게 받은 피어는 첫 프레임에 바로.
- **카메라**: 처치자가 아닌 피어만 `CameraTargetSwitcher.SetTemporaryFollow(처치자 CameraFollowTarget 자식)` — 즉시 컷(PreviousStateIsValid false). 처치자 본인 화면 그대로. 정리 때 `ClearTemporaryFollow`(지금 대상 = 오너·관전 대상으로 컷 복귀). 노이즈 UV 누적은 그 다음 프레임을 뺀다.
- **숨김**: 처치자 외 플레이어(디버그 랩 팀원 복제본 포함)를 모든 피어에서 — 자식 Renderer(그림자 포함)·Canvas·`UnitOverheadHealthBar`(바 본체는 공용 오버레이 캔버스에 있어 컴포넌트를 끄면 OnDisable 이 숨김) `enabled = false`. 매 프레임 렌더러를 켜는 오너 조준 쿼드(`PlayerAimIndicator.LateUpdate`) 때문에 LateUpdate(실행 순서 10001)에서 다시 끈다.
- **루트 SetActive(false) 안 씀**: NetworkObject 루트를 끄면 NGO 동기화·RPC 가 깨지고, `CameraFollowTarget` 이 비활성이 되면 카메라 추적 목록(`IsValidFollowTarget` = activeInHierarchy)에서 빠져 복구 때 카메라가 대상을 잃는다.
- 한계: 기본 첫 찢김은 0.2초라 0.10초 컷은 가려지지 않는다(가리려면 `tear` 곡선에 0.1 펄스 추가 또는 흑백 시작을 0.2 로). 숨긴 뒤 새로 붙는 이펙트(처치 판정 때 소거됨)·DecalProjector 는 대상 아님.

## 타임라인 에디터 창 (11-09 사용자 승인 — 전부 추천대로)
영상 편집기처럼 구간·단발 효과를 배치·조절하는 창. 커밋 단위: ① 데이터 모델 교체 + 런타임 평가 + EditMode ② 창 기본 표시 ③ 편집 기능 ④ 스크럽 미리보기 ⑤ 문서.
- **Q1 대상** = 보스 처치 연출(`BossVictoryGlitchSettings`)만. 공용화는 나중.
- **Q2 데이터** = SO 의 효과별 AnimationCurve 를 **트랙별 클립 목록으로 교체**(클립 = 시작·길이·페이드 인/아웃·세기). 창과 런타임이 같은 데이터. 기존 곡선 삭제(메커니즘 하나).
  - 클립 값 = 세기 × min(페이드 인, 페이드 아웃), 페이드 모양 = smoothstep(옛 곡선의 기울기 0 키 사이 보간과 같은 식 — 기본 클립이 옛 기본 곡선과 같은 화면). 같은 트랙 클립이 겹치면 큰 값.
- **Q3 트랙**: 구간 클립 = ~~전체 세기~~(11-09 삭제)·흑백·노란 틴트·주사선·보스 노이즈·HUD 숨김(0.5 초과면 숨김)·정지 유지(hold — 클립 동안 화면 효과 시각을 클립 시작에 고정)·TV 꺼짐(crtOff — 클립 시작에 화면 캡처, 길이 = 꺼짐 길이) / 단발 펄스 클립 = 가로 찢김·밝은 부분 끌림 / 순간 마커 = 카메라 처치자 포커스·처치자 외 캐릭터 숨김·로비 전환.
  - 카메라 전환·숨김은 desaturate 기준에서 떼어 독립 마커. 기본 = **첫 찢김 최고점 0.20초**(찢김에 가려짐 — 앞서 물은 a/b 는 사용자가 마커로 직접 맞춘다).
  - `glitchDuration`·`holdSeconds`·`crtOffSeconds` 는 클립 길이로 흡수(삭제). 로비 전환 = 마커(기본 5.6 = TV 꺼짐 끝) — 호스트 화면이 이 마커를 지나면 서버가 결과로 넘긴다.
  - ~~전체 세기(master) 트랙~~ → **11-09 사용자 결정으로 삭제**(아래 변경 기록).
- **Q4** Unity Timeline 패키지 안 씀 — 직접 만든 EditorWindow 가 SO 편집.
- **Q5 미리보기** = Play 중 디버그 씬에서 재생 헤드 스크럽 → 그 시각으로 고정 표시, 창에서 재생/일시정지/처음부터. 편집값은 SO 에 바로 저장(Play 끝나도 유지, Undo).
  - 스크럽이 움직이는 것 = 타임라인이 정하는 것(화면 효과·HUD·마커·TV 꺼짐). 처치자 순서·애니 배율은 애니 이벤트로 흘러 스크럽 대상이 아니다.
- **Q6 첫 버전 기능**: 클립 드래그 이동·양 끝 리사이즈·페이드 손잡이·추가/삭제·복사/붙여넣기·시간축 줌·0.01초 스냅·Undo·선택 클립 수치 직접 입력. 키프레임 곡선 편집 제외.
- **Q7** Work2 `feature/BossVictoryDebugScene` 이어서.
- 완료 조건: 기본 클립 평가 = 옛 기본 곡선(에셋 값) — EditMode `^BossVictoryTimeline` 로 0~5.6초 표본 비교.

### 타임라인 에디터 창 ①~⑤ (11-09 — 위 "타임라인 에디터 창" 사양)
- **① 데이터·런타임**(`881ac08a`): `BossVictoryTimeline`(SO 칸 `timeline`) — 트랙 10개 클립 목록(`BossVictoryClip` 시작·길이·페이드 인/아웃·세기, smoothstep 페이드, 겹치면 큰 값) + 마커 3개. 곡선 8개·`glitchDuration`·`holdSeconds`·`crtOffSeconds` 삭제. 표시는 매 프레임 시각 t 상태로 맞춤 — HUD 숨김(되돌림 포함)·마커(되감으면 카메라·숨김 되돌림)·TV 꺼짐(`BossVictoryCrtOff.SetProgress`, 앞으로 되감으면 걷음)·로비 전환 마커(지날 때 `PresentationFinished`). 에셋 YAML 을 기본 클립으로 다시 씀. EditMode `^BossVictoryTimeline` — 코드·에셋 기본 클립 vs 교체 직전 에셋 곡선 0~5.6초 0.0025초 간격 오차 1e-3.
- **② 창 표시**(`0ba65feb`): `Editor/BossVictoryTimelineWindow.cs` — 눈금자(줌에 맞춘 0.01~2초 눈금)·트랙 이름(·펄스/·1개)·클립 색(구간 파랑·펄스 주황·정지 유지/TV 꺼짐 보라)+값 모양 선·마커 마름모+세로선·재생 헤드(빨강)·휠 줌(마우스 기준)·가운데 버튼 이동·가로 스크롤·전체 보기.
- **③ 편집**(`174d395e`): 끌기 = 이동 · 양 끝 5px = 길이 · 선택 클립 위쪽 흰 네모 = 페이드 인/아웃 · 마커 끌기 · 우클릭 = 클립 추가(구간 1초·페이드 0.1 / 펄스 0.2초·페이드 0.1씩 / TV 꺼짐 1개만)·복사·붙여넣기·삭제·마커 여기/재생 헤드로 · Del · Ctrl+C/V(재생 헤드에)/D(바로 뒤에) · 0.01초 스냅 토글 · Undo(RecordObject) · 아래에 선택 클립/마커 수치 칸. 고칠 때마다 SetDirty + SaveAssetIfDirty(Play 끝나도 유지).
- **④ 스크럽 미리보기**(`a1ea85f5`): Play 중 디버그 씬 — 눈금자 클릭/끌기 = 랩 일시정지(`DebugBossVictoryLab.Paused`, Time.timeScale 0) + `BossVictoryGlitchPresentation.PreviewSeek(t)`(에디터·개발 빌드 전용) → 그 시각 화면 고정. 도구 막대 ⏮ 처음부터(= F7, `Replay`)·▶/❚❚. 재생 중엔 재생 헤드가 글리치 시계를 따라간다.
- 한계: 스크럽은 타임라인이 정하는 것만(처치자 순서·애니 배율·이펙트는 애니 이벤트로 흘러 그대로 멈춰 있음) · TV 꺼짐 구간으로 스크럽하면 그 순간 화면을 다시 캡처 · 카메라 컷(기본 비어 있음)은 컷 앞으로 되감아도 게임 카메라로 안 돌아감 · 공용화(다른 연출)는 나중.
- ~~전체 세기(master) 트랙~~ — 11-09 사용자 결정으로 삭제(아래).

### 변경 — 창 Ctrl+Z · 전체 세기(master) 삭제 (11-09 사용자)
- **Ctrl+Z**(`b2c8c016`): 창에 포커스가 있고 수치 칸 입력 중이 아닐 때 Ctrl+Z = 되돌리기, Ctrl+Y·Ctrl+Shift+Z = 다시 하기(창이 직접 `Undo.PerformUndo/Redo` 하고 키를 먹어 Unity 기본 단축키와 두 번 돌지 않는다. 칸 입력 중엔 칸의 글자 되돌리기). 편집 한 번 = 한 단계(`IncrementCurrentGroup`), 끌기는 매 이벤트 기록 후 손 떼면 `CollapseUndoOperations` 로 한 묶음. `undoRedoPerformed` → 선택 범위 보정·SO 저장·Play 중이면 `QueuePlayerLoopUpdate`(연출이 매 프레임 SO 를 읽어 바로 새 값)·다시 그림.
- **전체 세기 삭제**: 트랙·셰이더 마지막 `lerp(원래 화면, 효과, master)`·조기 반환·Frame 칸·패스 조건(`_master` → 효과 하나라도 0 초과면 패스) 전부 삭제(토글 없음). `_GlitchA.x` 는 비움(0).
- 옛 master 0.08~0.15 램프를 클립에 녹임(옛 값 × master 에 가장 가까운 0.005초 격자): 흑백·주사선 0.1/페이드 0.1 → **0.105/0.09** · 첫 찢김·끌림 0.08~0.25 페이드 0.12 → **0.09~0.25 페이드 0.105** · 보스 노이즈(0.12/0.05)·노란 틴트(0.3)·HUD 숨김(master 무관)은 그대로. 차이 = 0.08~0.26초 안 최대 0.04(몇 프레임). EditMode `^BossVictoryTimeline` 기준 = 옛 곡선 × 옛 master, 그 구간만 오차 0.06 허용.

### 변경 — 창 끝 잡기 · 처치 연출 스킬 재생 속도 = 실제 전투 (11-09 사용자)
- **창 끝 잡기**(`dc44d862`): 원인 = 안 보이는 페이드 손잡이 판정이 선택 안 한 클립에도 위쪽 8px 에서 돌아 끝(특히 페이드 0 인 오른쪽 끝)을 가로챘고, 끝 판정이 ±5px·첫 클립 순서라 이웃 펄스 클립에서 엉뚱한 것이 잡혔다. → 끝 = 몸통보다 먼저·행 안 가장 가까운 끝, 폭 = 밖 6px + 안 클립 폭 30%(4~10px) · 페이드 네모 = 선택 클립 위쪽 띠에서만 · 좌우 화살표/이동/밀기 커서 · 끌기 동안 hotControl. 왼쪽 끝 = 시작 이동(끝 고정) · 오른쪽 끝 = 길이 · 펄스·정지 유지·TV 꺼짐 클립 동일 · 0.01 스냅 · Undo 한 묶음.
- **처치 연출 스킬 속도**: 실제 시전 경로의 속도 = ① 스킬 고유 상태 속도 파라미터(팔라딘 R `UltimateSpeed` = AnimationSpeed × Hit 전/후 배율 — `OnClientPlay`·`OnLocalAnimationEvent(Hit)`) ② 공격속도 `Animator.speed`(`PlayerAttackSpeedAnimator` ← `PlayerSkillController.ScalesAnimatorWithAttackSpeed`) ③ 상태 파라미터 `AttackSpeedRate`(항상 씀). 표시 전용 재생은 ①·③ 은 이미 같은 함수를 탔고 **②가 빠져 있었다** — 창구가 `activeSkill` 만 봐서 표시 전용(`presentationSkill`)이면 1. → 창구가 표시 전용 스킬도 같은 `ScalesWithAttackSpeed` 로 본다(값 복제 없음). 거너·어쌔신 R 도 같은 경로(거너 R 은 타이머 방식이라 원래 공속 안 탐 — 실제 전투와 같음). 히트스톱은 플레이어 스킬 재생 경로에 없다(전역 슬로우는 간파 전용).
- **처치자 배율과의 관계**: 하던 동작 ×0.5 · 백대쉬 · **처치 연출 스킬 동안은 처치자 배율을 덮지 않는다(=1)** → 속도 = 실제 전투와 같음(공속 × 상태 파라미터). End 뒤 ×0.1 = 그 순간 `Animator.speed`(스킬이 끝나 공속 창구가 1 로 돌려 놓은 값) × 0.1, 상태 속도 파라미터(Hit 뒤 배율)는 상태 안에서 계속 곱해진다.

### 변경 — 사용자 창 튜닝 값 채택 · 에셋 테스트 = 구조만 (11-09 사용자 "그대로 쓸 거임")
- 에셋(`8d8c5a19`): 찢김 1.42·1.62·2.43 / 끌림 1.42·2.43 클립 삭제 · 정지 유지 시작 3.49 · **TV 꺼짐 클립 없음**. 코드 기본값(`CreateDefault`)은 옛 화면 그대로 둔다.
- EditMode: 에셋 ↔ 옛 곡선 비교 삭제 → 에셋은 구조 유효성(시작 ≥ 0·길이 > 0·페이드 ≤ 길이·세기 0~1·TV 꺼짐 ≤ 1개·마커 ≥ 0·로비 전환 > 0). 코드 기본값 ↔ 옛 곡선 × 옛 master 비교는 유지.
- **TV 꺼짐 없을 때 흐름(코드 확인)**: `TickCrtAndLobby` — `TryGetCrtOff` false → 꺼짐을 만들지 않음(코루틴 안 띄움, `_crt` null 그대로, 정리 `DisposeCrt` 는 null 안전) · 로비 전환 마커(5.6)를 지나면 `_finished` + `PresentationFinished` · 서버 `TickVictoryServer` 는 호스트 표시의 `Finished` 만 보고 결과 전환(TV 꺼짐 무관) → 검정 없이 글리치 정지 화면에서 바로 로비로. 디버그 랩도 같은 `DevVictoryResultReached` 로 반복.

### 변경 — 창 마커 잘 보이게 (11-09 사용자 "몇 초에 로비로 가는지 세팅할 노드")
- 새 데이터 없음 — 기존 마커 `lobbyTransitionTime` 표시만 고침. 안 보였던 이유: 마커 줄이 맨 아래 + 시간 숫자만 + 기본 줌(160px/초)이면 5.6초가 창 오른쪽 밖.
- 마커 줄을 **맨 위**(눈금자 바로 아래)로 · 처음 열 때 전체 보기 · 마커마다 "이름 시각s" 라벨 상시("로비 전환 5.60s" — 초록·굵게·큰 마름모·굵은 세로선) · 화면 밖이면 줄 끝에 ◀/▶ 라벨(누르면 그쪽으로 이동) · 마름모 ±14px 또는 라벨을 끌어 시각 변경(이동량 기준·0.01 스냅·Undo 한 묶음) · 좌우 화살표 커서 · 선택 시 아래 수치 칸 + 설명.
- **규칙 확인(코드)**: 실게임 — 호스트 표시가 마커를 지나면 `TickVictoryServer` 가 결과 전환 시각을 지금으로(보스 디스폰 대기보다 늦어도/빨라도 마커 시각에 넘어감) → `TickResultTransition` → 로비 결과. 디버그 씬 — 같은 순간 `DevBlockResultTransition` 이 막고 한 바퀴 끝(`DevVictoryResultReached`) → 랩 `loopHoldSeconds`(기본 1.5 실제 초) 뒤 처음부터. 로비로 안 간다.
