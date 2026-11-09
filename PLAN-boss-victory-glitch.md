# PLAN — 보스 처치 연출 교체: ZZZ "WIPEOUT" 글리치 (2026-11-09 사용자 확정)

> **원본 문서**(브랜치 `feature/BossVictoryDebugScene`). 옛 처치 연출 문서 [PLAN-boss-victory-cinematic.md](PLAN-boss-victory-cinematic.md) 는 Legacy 기록.
> 진행(11-09): 1 Legacy 이동 ✅ · 2 시간 배율·소거·처치 연출 스킬 ✅ · 3 글리치 렌더·흑백·보스 노이즈·SO 타임라인 ✅ · 4 카메라 컷 구조 ✅(기본 목록 비움 — 게임 카메라만) · 사양 변경: 글리치 = 처치 판정 즉시 ✅ · 5 Rectify 글자 ✅ → **11-09 제거**(사용자) · 6 유지 → CRT 꺼짐 → 로비 결과 ✅ · 7 디버그 랩·문서 ✅ · **타임라인 에디터 창 ①~⑤ ✅**(곡선 → 클립 목록·마커, 창 `Tools/Boss Victory/처치 연출 타임라인`) — 아래 "진행 기록". ⏳ Unity 컴파일·셰이더·EditMode·Play(이 레인은 Unity 없음).

## 지금 흐름 한눈에 (최종 — 아래 진행 기록이 근거)
| 처치 판정 뒤(초, 게임 시간) | 일 | 어디서 |
|---|---|---|
| 0 | 서버: 막타·보스·시각 복제 · 무적 · 연출 잠금(스킬 취소·입력 차단) · 장판·투사체 디스폰. 각 피어: 이펙트 소거 · 그 외 플레이어 0.05 · 보스 0 · **글리치 시계 시작** | `TryStartVictoryServer` · `BossVictoryGlitchPresentation.Begin` |
| 0 ~ 스킬 End | 처치자: 하던 동작 ×0.5 그 클립 끝까지(또는 '처치자 동작 끝 → 백대쉬' 마커 중 이른 쪽, 255 — 기본 끔) → 백대쉬(등장 연출 밀림, ×1) → 처치 연출 스킬(기본 R, 표시 전용, ×1) → End 에서 처치자·보스 ×0.1 | `StartKillerSequence` · `BossEncounterDirector.StepSkid` · `PlayPresentationOnly` · `OnFinisherEnded` |
| 0.08 ~ 3.5 (클립) | 글리치 = **가로줄 좌우 찢김 + 찢긴 줄 밝은 부분 끌림만**(11-09) · 색: 흑백·호박색·주사선·보스 노이즈(카메라 이동 UV) — 흑백 플래시 삭제(11-09) · HUD 숨김 | `BossVictoryGlitchRender` + 셰이더 2개 |
| 0.20(마커 2개 — 첫 찢김 최고점) | 처치자 아닌 피어 카메라 → 처치자 즉시 컷 · 처치자 외 플레이어 모든 화면에서 숨김(렌더러·캔버스·머리 위 체력바) — 되감거나 정리 때 복구(11-09) | `TickMarkers` |
| (컷) | 기본 없음(게임 카메라) — 타임라인 '카메라 컷' 트랙에 클립을 넣으면 그 시작에 끊어 붙이고 다음 컷 시작까지 시작 → 끝 화면으로 이동(끝 없으면 고정) · 기준 좌표계 저장 · 컷별 '벽 숨김'(기본 켬, 255·255 후속 2) | `TickCuts` · `BossVictoryCameraCuts` |
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

### 255 — 카메라 컷 트랙 · 처치자 동작 끝 마커 · 스킬 End 시각 표시 (11-09 사용자 — grill 확정, 4) 정정 반영)
- **카메라 컷 → 타임라인 트랙**: `BossVictoryTrack.CameraCut`(창 '카메라 컷' ·다음 컷까지). 클립 1개 = 컷 1개, **클립 시작 = 컷 시작, 다음 컷 시작까지 유지**(255 전 `ActiveIndex` 규칙 그대로 — 클립 길이는 표시용, 창이 저장마다 `FitCameraCutLengths` 로 다음 컷 시작·마지막은 로비 전환 마커까지 맞춤, 끝 잡기 없음·몸통 끌기만).
  - 컷 값 = `timeline.cameraCutShots`(클립과 같은 번호 — 종류·거리·내려다보는 각·방위·초점 높이·FOV·**벽 숨김**). `BossVictoryCameraCuts.Cut.startTime` 삭제(시작 = 클립). 창에서 컷 클립을 누르면 아래에서 값 편집, 클립 안에 "#번호 종류(· 벽 숨김 끔)" 표시. 추가·붙여넣기·삭제·복사가 값도 함께 · 개수 어긋나면 `SyncCameraCutShots`(모자라면 기본값 = 와이드 16·22·50·1.5·40·벽 숨김 켬).
  - **벽 숨김(컷별, 기본 켬)**: 켠 컷 = 이 컷 카메라에서 앞면이 안 보이는 벽을 더 끔(이어지는 켠 컷끼리 누적 — 255 전과 같음) · 끈 컷 = 숨겼던 벽 모두 다시 켬. 되감기로 앞 컷이면 모두 켠 뒤 그 컷 기준으로 다시 끔 · **첫 컷 앞으로 되감으면 게임 카메라로 복귀**(255 전 한계 해소). Play 중 창에서 컷 값을 고치면 `PreviewRecut` 으로 지금 컷을 다시 잡음.
  - **옛 `BossVictoryGlitchSettings.cuts` 삭제 · 이관 손실 0**: 삭제 시점 에셋 = `cuts: []`(옮길 값 없음 — 트랙도 빈 채로 시작 = 같은 화면). EditMode `옛_cuts_배열은_삭제됐고_에셋에_옮길_값이_남아_있지_않다` 가 칸 삭제·에셋 YAML 의 `cuts:` 가 없거나 `[]`·컷 값 개수 = 클립 개수를 확인(값이 있었다면 지우기 전에 옮기도록 막는 장치). Unity 가 에셋을 다시 저장하면 `cuts: []` 줄은 사라진다.
- **처치자 동작 끝 → 백대쉬 마커**(4) 정정 — 사용자: 'Animator speed 0 정지' 안 취소): `BossVictoryMarker.KillerActionEnd`(`killerActionEndTime`, **0 = 끔 = 기본**). 처치자 하던 동작 단계(×0.5)에서 글리치 시각 t ≥ 마커면 그 자리에서 끊고 `BeginBackDash`. 클립이 먼저 끝나면 클립 끝(둘 중 이른 쪽) · 하던 동작이 없으면(대기·이동) 처음부터 백대쉬 · 이후 백대쉬 → 처치 연출 스킬 → End 0.1 그대로. 순수 함수 `BossVictoryTimeline.ShouldEndKillerAction(마커, t)`(마커 > 0 && t ≥ 마커). 각 피어 로컬, 새 RPC·복제 값 없음. 창 마커 줄 '처치자 동작 끝 → 백대쉬'(0 이면 "(끔 — 클립 끝까지)", 선택 시 '끄기' 버튼).
  - 한계: 동작 **애니만** 끊는다 — 서버 게임플레이 동작(스킬 실행 상태)은 그대로 두고(255 전과 같이 연출 잠금이 처치자 동작을 취소하지 않음) 자기 End·서버 안전망으로 끝난다. 처치 연출 스킬은 255 전처럼 그 스킬이 끝난 뒤(`ActiveSkill == null`) 튼다 — 스킬(Q/E/R) 도중에 끊으면 백대쉬 뒤 처치 연출 스킬 시작이 그 스킬 끝(안전망)까지 늦을 수 있다(평타는 해당 없음). 스크럽으로 되돌아가지 않는다(한 번 지난 단계 — ⏮ 로 다시).
- **처치 연출 스킬 End 시각(읽기 전용)**: 창 아래 수치 칸 위 한 줄. 랩 재생 중 = 이번 회차 실제 글리치 시각(`BossVictoryGlitchPresentation.FinisherEndedAt`, 아직이면 "아직 안 옴") · 아니면 캐릭터별 추정 = 열린 씬 `BossEncounterDirector.IntroSkidDuration`(백대쉬) + 처치 연출 스킬 상태 클립의 Skill End 이벤트 시각(가정: 하던 동작 없음·공속 100%, '다시 계산' 버튼). 새 타이머 칸 없음 — 처치자 순서·시간 배율은 애니 이벤트 그대로.
- 그대로(3): 이펙트 소거(t=0)·입력 잠금(끝까지)은 코드.
- EditMode: `^BossVictoryCameraCut`(트랙 구조·다음 컷까지 유지·컷 값 짝/보정·기본값 벽 숨김 켬·길이 자동·옛 cuts 이관) · `^BossVictoryTimeline`(에셋 구조에 컷 값 짝 · 동작 끝 마커 끔/지남/이름·음수). ⏳ Unity 컴파일·EditMode·Play 미검증(이 레인 Unity MCP 없음 — 오프라인 컴파일 3종 통과).

### 255 후속 — 처치 연출 팔라딘 R '하늘에서 떨어지는 칼' (11-09 사용자)
- 원인: 마무리 낙뢰(`PaladinSkillView.targetStrike`, FX_Lightening_Strike_Entry)는 서버 전용 `OnAnimationEvent(Custom0)` → `ServerPlayTargetStrike` RPC 로만 나오고, 자리도 서버 `OnServerStart` 의 표식이 정한다. 표시 전용 재생은 둘 다 안 돌아(OnLocalAnimationEvent 만) 빠졌다. 칼 꽂기(Stick)는 클립 이벤트라 원래 나왔다.
- 수정: `PlayPresentationOnly(skill, dir, target)` → `PlayerSkillBase.OnPresentationOnlyStart(target)`(기본 no-op). 팔라딘 R 만 대상(= 보스)을 받아 두고 Custom0 에서 같은 엔트리·`VfxPlayRate`·자리 규칙(대상 아래 바닥 — `LockTargetPosition`, 표식과 공유)으로 각 피어가 로컬 재생. 표식·판정·슈퍼아머 없음, 새 RPC 없음, 프리팹 수정 없음.
- 소거·배율: 처치 순간 이펙트 소거(t=0)보다 뒤(Custom0)에 나와 안 지워진다. 월드 자리 원샷이라 처치자 이펙트 배율(`SetPlayRateForTarget` = 따라붙은 이펙트만)은 안 탄다 — 스킬 재생 중 처치자는 ×1 이라 같고, End(×0.1) 뒤까지 남는 꼬리는 ×1 로 흐른다(실전 R 과 같은 방식).

### 255 후속 2 — 정지 유지 삭제 · 카메라 컷 = 지금 보는 화면 2점 (11-09 사용자 — grill 확정)
- **A 정지 유지(Hold) 트랙 삭제**(`27ef940a`): enum·`hold` 목록·`EffectTime`·창 색/새 클립·테스트. 화면 효과·HUD 는 시각 t 그대로. 끝의 정지 느낌은 처치자·보스 ×0.1 과 TV 꺼짐. 에셋의 `hold` 칸은 Unity 가 다음 저장 때 버린다.
- **B 카메라 컷 형식**(`012dec55`): 컷 = **기준**(가운데·처치자·보스 발밑) + **기준 좌표계**(원점 = 기준점, +Z = 처치자→보스 수평 축, +Y = 위 — 옛 yaw 0 과 같은 방향 정의)의 카메라 위치·회전·FOV.
  시작 화면 + (선택) 끝 화면. 컷 구간 = 클립 시작 ~ 다음 컷 시작(마지막 = 로비 전환 — `CameraCutEnd`, 저장된 클립 길이와 무관) 동안 곡선(선형·ease in/out·ease in·ease out)으로 위치 Lerp·회전 Slerp·FOV Lerp. 끝 없으면 고정 컷. 컷 사이는 블렌드 없이 끊음(그대로).
  - **옛 매개변수 형식과의 관계 = 새 형식으로 통일**: 종류(와이드/보스 클로즈업/처치자 정면)·거리·내려다보는 각·방위·초점 높이 칸 삭제. 에셋 컷이 0개(`cameraCutShots: []`)라 손실 0. 옛 기본 와이드(거리 16·각 22·방위 50·높이 1.5·FOV 40)는 새 컷 기본값으로 변환(`BossVictoryCameraCuts.Orbit`). 옛 '보스 클로즈업'의 몸 중심(렌더러 바운즈) 기준은 없어졌다 — 보스 기준 + 높이로 찍는다.
  - 런타임(`TickCuts`): 컷이 바뀌는 순간 처치자·보스 위치를 잡아 컷 동안 고정, 매 프레임 `CameraCutProgress(t)` 로 자세 계산 → 스크럽·되감기도 t 그대로. 벽 숨김 켠 컷은 이동 중 매 프레임 지금 카메라 위치로 판정(누적), 끈 컷은 다시 켬. 창 수정은 매 프레임 배치라 바로 반영(`PreviewRecut` 삭제).
  - 창: 컷 클립 선택 시 **[지금 Scene 뷰로 시작 화면] [지금 Scene 뷰로 끝 화면]**(끝을 찍으면 이동 켜짐) · 아래 **[Scene 뷰로 새 카메라 컷(재생 헤드)]**(기준 가운데·고정) · 기준·곡선·위치/회전(°)/FOV 칸 · Scene 뷰 FOV 도 기록. Undo(`Record`) + 에셋 저장.
  - 'Scene 뷰로' 기준 위치: Play 중 연출이 돌면 **지금 회차**(컷이 켜져 있으면 그 컷이 잡은 위치, 아니면 지금 위치 — `TryGetCutSubjects`) · 아니면 이 에디터 세션의 **마지막 회차**(창이 Play 중 SessionState 에 기억). 없으면 버튼을 막고 이유("디버그 씬에서 Play 해 연출을 한 번 돌리면 생긴다"). 랩의 배치 값으로 미리 계산하는 기준은 넣지 않았다(보스방이 런타임 생성이라).
  - 각 피어 로컬, 새 RPC·복제 값 없음. EditMode `^BossVictoryCameraCut`: 축 방향·상대 변환 왕복(+다른 자리에서 같은 구도)·끝 없으면 고정·보간·곡선·t 진행·퇴화 회전.
- 에셋(`BossVictoryGlitchSettings.asset`)은 손대지 않았다 — 사용자 값(처치자 동작 끝 0.13 등) 결정 대기. Unity 가 다시 저장하면 사용자 값은 메모리 그대로 남고 `hold` 칸만 빠진다.

### 255 후속 3 — 타임라인 창 노드 인스펙터를 노드 전체로 (11-09 사용자 — grill 확정)
- **방식**: 노드 선택 → 창 아래 칸에서 편집(카메라 컷 클립과 같음). 데이터는 설정 SO 칸 그대로(SO 인스펙터에도 보임), 창은 `SerializedObject` 로 고쳐 Undo·저장. 트랙 이름·마커 이름 클릭 = 그 노드 선택, 'End 예상' 줄 앞 **[시간 배율]** = 배율 노드.
- **처치자 동작 끝 → 백대쉬 마커**: 백대쉬 거리(m)·시간(s)·곡선(선형/ease in·out/ease in/ease out)·방향 보정 각(°, 기본 0 = 보스 반대쪽, + = 위에서 봐 시계 방향). 처치 연출 전용 칸(`backDash*`) — 기본 = 보스 등장 밀림(`BossEncounterDirector` impactSkid 1.5m·0.35s·SkidEase = ease out) 복사. 등장 연출·리액션 애니 그대로(`StepSkid` 에 곡선 인자만 추가). 마커가 꺼져 있어도 값은 적용. 방향 = `BossVictoryBackDash.Direction`(겹치면 -정면 → 월드 뒤).
- **처치자 주목 마커**: 즉시 컷(기본) 또는 블렌드 초(`focusKillerBlendSeconds`) — 양수면 원래 카메라 대상 위치의 대리 오브젝트를 처치자로 ease in/out 이동시켜 따라가게 한다.
- **다른 플레이어 숨김 마커**: 대상 = 팀원만(기본) / 팀원 + 붙은 이펙트(`hideOthersTarget`) — 팀원을 따라다니는 풀 이펙트 렌더러를 `EffectManager.CollectFollowingRenderers`(읽기 전용 추가)로 매 프레임 모아 끄고, 빠진 것·끝나면 되켬. 소환물은 지금 플레이어 캐릭터에 없다.
- **로비 전환 마커**: 시각만. **TV 꺼짐 클립**: `crtOffGlowColor`. **효과 트랙(이름 또는 클립)**: 노란 틴트 색·주사선 수·보스 노이즈 7칸·찢김 5칸·끌림 길이·카메라 컷 벽 기준 2칸. **시간 배율**: 처치자 하던 동작 0.5·End 후 0.1·팀원 0.05·보스 End 후 0.1·기본 스킬 칸.
- 런타임 변화 = 0(백대쉬 분리도 같은 기본값). EditMode `^BossVictoryBackDash`: 기본 거리·시간 = 프리팹 밀림 값, 곡선 = SkidEase, 마커 옵션 기본값, 방향 보정(0도·±90도·퇴화).

### 255 후속 4 — 처치자 순서를 타임라인 독립 노드로 (11-09 사용자 — grill 확정, 동기: 백대쉬 뒤 후딜레이가 너무 김)
- **원인 2개 제거**: ① `StepSkid` 의 Idle 복귀 지연 0.35 상수 → 처치 연출은 설정 SO `backDashIdleDelay`(기본 0.35, 인자로 넘김 — 등장 연출은 상수 그대로) ② 처치 연출 스킬이 `ActiveSkill == null` 을 기다리던 것 → '스킬 연출 시작' 마커 시각에 바로.
- **노드**(전부 창에 보이고 선택 → 창 아래 칸·Undo, 각 피어 로컬, 새 RPC·복제 값 없음):
  - '처치자 평타' 트랙(1개, 시작 0 고정, 끝 = 끊는 시각): 처치 순간 하던 동작을 ×`killerCurrentActionScale` 로 이어 가다 클립 끝에서 Idle 로 끊고 대기(먼저 끝나면 그 사이 대기, 대기·이동 중이었으면 빈 구간). 클립이 없으면 자기 클립 끝까지.
  - '백대쉬 시작' 마커: 거리·시간·곡선·각도·후 대기 칸. 하던 동작이 남아 있으면 끊는다. '스킬 연출 시작 = 백대쉬 시작 + 시간 + 후 대기' 버튼.
  - '스킬 연출 시작' 마커: 백대쉬가 덜 끝났으면 끊고 표시 전용 재생. 이전 스킬이 남아 있으면 `PlayerSkillController.PresentationPoseHeld`(재생 시작 ~ 연출 정리)가 그 스킬의 0층 자세 복귀를 막고 애니 이벤트를 처치 연출 스킬로만 보낸다 — 서버 취소(cancelCurrentAction)는 쓰지 않았다(하던 동작 ×0.5 를 보여야 해서).
  - '느려짐 시작' 마커: 처치자 ×`killerAfterFinisherScale`. 0 = 끔 → 스킬 End 이벤트(지금과 같음).
- 단계 판정 순수 함수 `BossVictoryKillerSequence.Target`(늦은 단계가 이김 — 스킬 시작이 백대쉬 시작보다 앞이면 백대쉬 건너뜀). 런타임은 앞으로만 넘어간다 → **스크럽·되감기로 이 단계는 되돌아가지 않는다(⏮ 로 다시 보기)**. 늦게 받은 피어는 그 시각 단계부터.
- **이관**: 옛 '처치자 동작 끝 → 백대쉬' 마커(`killerActionEndTime`, 칸 이름 그대로 숨김) 값 L 을 설정 SO `OnAfterDeserialize` 가 평타 클립 [0, L] · 백대쉬 시작 L · 스킬 시작 L + 백대쉬 시간 + 후 대기로 옮긴다(값 손실 0). 코드 기본 0.13 / 0.13 / 0.83 / 꺼짐. 기본에서 '이전 스킬 대기'만 사라지는 것은 의도.
- **알려진 한계**: 처치 연출 스킬로 CrossFade 하는 0.05초 동안 옛 클립의 End 이벤트가 오면 처치 연출 스킬 End 로 읽힐 수 있다(드묾). 서버가 안 끊은 이전 스킬은 애니 이벤트가 끊겨 컨트롤러 안전망 시간으로 끝난다(보스 사망·무적이라 판정 영향 없음). 평타(DefaultAttack 등)의 Idle CrossFade 경로는 막지 않았다 — 서버가 처치자 평타를 안 끊으므로 연출 중엔 안 탄다.
- EditMode `^BossVictoryKillerSequence`.

### 255 후속 5 — 23호 처치 애니 = ParryHit_InPlace · 보스 노드 세분화 (11-09 사용자)
- **노드**: '보스 피격 애니 시작'(기본 0 = 처치 순간) · '보스 정지'(0 = 끔) · '보스 슬로우'(0 = 끔 → '느려짐 시작' → 그것도 끔이면 스킬 End). 처치자 느려짐과 보스 슬로우 분리.
- **피격 애니**: 설정 SO `bossHitStateName`(기본 `getowned` — No23Controller 의 간파 피격 상태, 클립 `Boss_23_ParryHit_InPlace` guid 1481cf2e, 1.42초·루프 아님·루트모션 없음·프리팹 ApplyRootMotion 0 → 위치 그대로) · `bossHitFadeSeconds` 0.05. 그 상태가 있는 보스 Animator 만, 처음부터. 비우면 마지막 피격 자세(255 후속 4 까지와 같음). 피격 애니가 쥔 동안 사망 트리거(`MonsterData.deathTrigger`)를 매 프레임 비운다 — 사망 상태 복제가 늦게 와도 AnyState→Death 가 빼앗지 않게.
- **배율** = 순수 함수 `BossVictoryBossTimeScale.Scale`: 처치 순간 0 → 지난 노드 중 **시각이 가장 늦은 것**이 이김(같은 시각 = 피격 애니 < 정지 < 슬로우). 피격 애니 1 · 정지 0 · 슬로우 `bossAfterFinisherScale`. 애니·이펙트·서버 사망 표현 대기(`BossTimeScale`)가 같은 값. 피격 애니는 한 번(앞으로만, ⏮).
- 기본값 판단: 애니 시작 0 · 정지 끔 · 슬로우 끔 → 피격 애니가 ×1 로 재생되다 처치자 느려짐 순간(기본 = 스킬 End) ×0.1. 옛 '처치 순간 정지'를 원하면 정지 마커를 켠다.
- EditMode `^BossVictoryBossTimeScale`(순서 규칙·기본값·23호 컨트롤러 상태 클립 guid).

### 255 후속 6 — 타임라인 레인에 시작·중간·끝 키 (11-09 사용자 — 1차 = 카메라 컷 + 효과 세기 트랙)
- **키 시각 = 클립 비율 u(0 = 클립 시작, 1 = 끝)** — 결정: 클립을 옮기거나 양 끝을 끌면 키가 **비율대로 따라간다**(초 고정 아님). 카메라 컷은 컷 구간(다음 컷 시작·마지막 = 로비 전환까지, 런타임 진행과 같은 식) 비율.
- **공통**: 시작 키(u 0)·끝 키(u 1)는 늘 있고 클립 끝에 고정(카메라 컷은 키 1개 = 고정 컷도 허용). 중간 키 = 클립 위 더블클릭 또는 [재생 헤드에 중간 키] — 지금 곡선 값으로 넣어 모양이 안 바뀐다. 끌기 = 시각(앞뒤 키 사이로 자름) · Del/[선택 키 삭제] · Undo. 구간 곡선 = 그 구간 **시작 키**의 '다음 키까지 곡선'(선형·ease in/out(smoothstep)·ease in·ease out·계단). 마름모로 그림(세기 키 = 높이가 세기).
- **효과 세기 트랙**(흑백·호박색·스캔라인·보스 노이즈·찢김·스미어·HUD 숨김 — 펄스 트랙 포함): 클립 `keys`(u·세기·곡선). 옛 페이드 인/아웃·세기 → 같은 모양 키(0 →ease in/out→ 페이드 인 끝 세기 →선형→ 페이드 아웃 시작 세기 →ease in/out→ 0, 페이드 0 이면 그 끝은 세기 그대로). 페이드가 겹치는 클립(인 + 아웃 > 길이)만 만나는 지점 꼭대기로 근사 — 기본값·지금 에셋엔 없음. 키 없는 클립(코드로 만든 것)은 옛 식.
- **카메라 컷**: 화면 키 목록(u·기준 좌표 위치·회전·FOV·곡선) — 옛 시작→끝 2점은 시작 = 키 0(곡선 = 옛 ease 번호 그대로)·끝 = 키 1, 끝 없으면 키 1개(손실 0). '부드러운 경로' = 키 3개 이상에서 위치를 Catmull-Rom 으로(키를 지남), 회전 Slerp·FOV Lerp 는 구간별. 창 [지금 Scene 뷰로 이 키 (#n)]·[지금 Scene 뷰로 끝 화면](키 1개면 끝 키를 만든다).
- **이관 시점** = 설정 SO `OnAfterDeserialize` → `timeline.MigrateKeys()`(메모리). 에셋은 창에서 저장할 때 키 형식으로 다시 써진다 — 옛 칸은 숨김으로 남겨 둔다. **런타임 결과 변화 0**.
- **구조체 복사 주의**: 클립·컷은 struct 인데 키 목록은 참조 — 붙여넣기·복제는 `WithCopiedKeys()`.
- EditMode `^BossVictoryKeys`(곡선·구간·계단·정렬/삽입·페이드 이관 Evaluate 같음·기본 타임라인 전부 키·옛 2점 컷 자세 같음·Catmull-Rom 이 키를 지남) · `^BossVictoryCameraCut` 기본 컷 = 키 1개.

### 255 후속 7 — '스킬 연출 시작' 마커 → '처치 연출 스킬' 클립 · 끝 자르기 (11-09 사용자)
- **데이터**: 시작 = 기존 `finisherStartTime` 그대로(이관 손실 0 — 마커 API·기본값·백대쉬 맞추기 버튼 동일) · 새 칸 `finisherTrimSeconds` = 자른 길이(**클립 시간 초, 공속 100% 기준·프레임 단위**), 0 = 안 자름(지금 동작).
- **창**: 그 줄을 클립으로 — 길이 = 미리보기 캐릭터(팔라딘·거너·어쌔신 고름) 또는 디버그 랩 재생 중 실제 처치자의 처치 연출 스킬 클립 전체(공속 100%). 프레임 눈금·End 이벤트 노란 선·"42/60f". 몸통 끌기 = 시작 이동, 오른쪽 끝 앞으로 끌기 = 자르기(원래 끝 이상 = 안 자름, 늘릴 수 없음), 아래 칸 끝 프레임 슬라이더·[안 자름], 우클릭 '끝 자르기 풀기'. 자른 값은 하나라 캐릭터마다 클립 길이가 다르면 프레임 표시가 다르다(더 짧은 캐릭터는 안 잘림).
- **런타임**(각 피어, 새 RPC 없음): 처치자 0층이 그 상태를 재생 중이고 클립 시간(normalizedTime × 클립 길이 — 공속과 무관)이 자른 길이에 닿으면 그 프레임으로 맞추고(`Animator.Play` 뒤로 1프레임 안 — 이벤트 없음) 처치자 배율 0. '느려짐 시작' 꺼짐 + 자른 끝이 End 이벤트 앞(또는 End 없음)이면 그 순간 느려짐(보스 슬로우 꺼짐이면 보스도). End 가 먼저면 End 에서(지금 규칙), 마커가 켜져 있으면 마커.
- **의도된 동작**: 이미 생긴 이펙트는 지금 규칙(스킬 동안 ×1 · 느려짐 뒤 End 후 배율) 그대로 계속. 자른 뒤 애니 이벤트(End 포함)는 안 난다 — 멈춰서. 자른 프레임을 넘친 1프레임 안 이벤트는 이미 났을 수 있다. 클립 끝 전에 상태를 떠나는 전이가 있으면(Exit 시간 < 자른 시각) 멈추지 않는다.
- **23호 피격 애니에 붙이기**: `BossVictoryAnimTrim.TryReachTrim(animator, 상태 해시, 자른 길이, out clip)` 이 캐릭터·스킬을 모른다 — 보스 노드에 `bossHitTrimSeconds` 칸 + `PlayBossHitAnim` 뒤 매 프레임 호출 + 멈추면 보스 배율 0(`BossVictoryBossTimeScale` 에 한 칸)으로 붙는다. **적용은 사용자 결정 대기.**
- EditMode `^BossVictoryAnimTrim`(프레임·자르기 값·재생 길이/표시·멈춤·잘린 끝 느려짐·기본 안 자름).

### 255 후속 8 — 카메라 컷 사이 블렌드 (11-09 사용자)
- **칸**(컷 값): `blendSeconds`(0 = 툭 끊김·기본 → 런타임 변화 0) · `blendCurve`(선형·ease in/out·ease in·ease out).
- **구간 결정 = [들어오는 컷 시작 − 블렌드, 컷 시작)** — 창 쐐기(앞 클립 위로 겹침)·최대 = 앞 컷 길이·첫 컷 최대 = 컷 시작 시각과 맞는 쪽(Unity Timeline 크로스페이드). 컷 시작 순간 블렌드가 끝나 들어오는 컷 화면 그대로.
- **앞 화면 결정 = 그 시각 앞 컷 화면(계속 진행)** — 구간이 앞 컷 시간 안이라 외삽이 필요 없고, 블렌드 시작 순간 지금 화면과 같아 안 튄다. 들어오는 쪽 = 그 컷 첫 화면(u 0, 기준 위치는 블렌드 시작 순간 잡아 컷 동안 그대로). 첫 컷 = 컷 카메라를 켜기 직전 게임 카메라 화면(잡아 둔 값 — 되감아도 같음, 첫 컷 전으로 나가면 버림).
- 섞기 = 월드 위치 Lerp·회전 Slerp·FOV Lerp(`BossVictoryCameraCuts.Blend`) — 기준점이 달라도 월드 기준. 블렌드 중 벽 숨김·초점 = 들어오는 컷 · 컷 진입 처리(실루엣·윤곽선·월드 캔버스·흔들림) = 블렌드 시작 순간.
- 창: 쐐기(삼각형 + 페이드아웃 대각선 + 곡선 선 + '블렌드 0.30s'), 왼쪽 끝 끌기(0 이면 사라짐), 컷 클립 우클릭 '블렌드 추가(0.3초)/제거', 아래 칸 시간·곡선, Undo. 스크럽 = 시각 t 순수 결과.
- EditMode `^BossVictoryCameraCut`(블렌드 0 = 기존 · 구간·곡선 · 제한 · 첫 컷 게임 카메라 · 월드 보간).
