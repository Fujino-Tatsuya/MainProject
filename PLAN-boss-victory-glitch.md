# PLAN — 보스 처치 연출 교체: ZZZ "WIPEOUT" 글리치 (2026-11-09 사용자 확정)

> **원본 문서**(브랜치 `feature/BossVictoryDebugScene`). 옛 처치 연출 문서 [PLAN-boss-victory-cinematic.md](PLAN-boss-victory-cinematic.md) 는 Legacy 기록.
> 진행: 1단계(옛 연출 Legacy 이동·연결 끊기) ✅ — 아래 "진행 기록".

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
