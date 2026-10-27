# 공유 — 맞은 쪽 섬광 재생 판단이 간파 연출 디렉터로 (할 일 118, 경석)

> 작성: 은희 레인(claude-alt), 2026-10-27. 브랜치 `feature/InterruptProductionDirector` (커밋 C6).
> 설계 원본: [interrupt-presentation.md §6](interrupt-presentation.md#6-간파-연출-디렉터--interruptproductiondirector-할-일-118).
> 104 공유 문서 [handoff-interrupt-fx-toggles.md](handoff-interrupt-fx-toggles.md) 의 경석 영역 절을 대체한다.

## 1. 무엇이 바뀌었나

간파 연출은 `NetworkManager.prefab` 의 `InterruptProductionDirector` 가 "틀지 말지" 정하고 부른다(104 스위치 `InterruptPresentationToggles` 는 삭제).
맞은 쪽 섬광(E1 `targetFlash`)도 같다 — **섬광 RPC 는 그대로 날아가고, 받는 쪽에서 디렉터가 칸을 보고 재생한다.** 보이는 동작은 104 와 같다.

## 2. 경석 파일 변경

- `Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs`
- `Assets/1.Scripts/Monster/Boss/GauntletBot.cs`
- `Assets/1.Scripts/Monster/Boss/SpinnerBot.cs`
- `Assets/1.Scripts/Monster/Boss/WallBot.cs`

각 파일:
- 클래스 선언에 `IInterruptCueSink` 추가.
- `PlayInterruptFlashRpc` 본문 = 섬광 빈 칸 경고(`WarnNoInterruptFlashOnce`, 그대로 앞에) → `InterruptProductionDirector.PlayCue(TargetFlash, InterruptProductionEvent.LocalCue(Success), this)` 한 줄.
- 실제 재생(`interruptFlash.PlayOnceOnSurface(EffectSurfaceCollider)`)은 명시적 인터페이스 구현 `IInterruptCueSink.PlayCue` 로 옮겼다(줄 그대로).

**불변**: 판정·카운터 창·그로기·통계(`CombatStatsEvents`)·RPC 발신 지점(`ReceiveAttack` 의 카운터 분기)·RPC 속성(Unreliable)·빈 칸 경고. 105 병합 뒤 `TwentyThreeBoss` 실제 코드 기준으로 작업했다.
같은 변경이 은희 영역 `TrainingDummyInterrupt`(`PlayOnce`)·`TrainingSparringBot` 에도 들어갔다.

## 3. 새 대상(보스·엘리트)을 추가할 때

1. 카운터 성공 지점(서버)에서 지금처럼 전 피어 RPC 를 보낸다.
2. 수신부 = `InterruptProductionDirector.PlayCue(InterruptProductionCue.TargetFlash, InterruptProductionEvent.LocalCue(InterruptProductionPhase.Success), this)`.
3. 클래스에 `IInterruptCueSink` 를 붙이고 `PlayCue` 에서 `cue == TargetFlash` 일 때 섬광을 낸다.
- 칸을 직접 읽지 않는다(디렉터가 본다). 섬광 칸이 비었을 때의 경고는 `PlayCue` 앞에 둔다.
- EditMode `InterruptProductionChannelTests.섬광_대상_6종은_IInterruptCueSink` 에 새 타입을 더하면 회귀를 잡는다.

## 4. 스위치(E1) 위치·기본값

- `NetworkManager.prefab › InterruptProductionDirector › targetFlash` — **기본 꺼짐**(104 와 같다). 섬광이 안 보이면 칸이 꺼져 있는 것이다(에셋 문제 아님).
- 피어마다 자기 값(동기화 없음) — MPPM 은 화면마다 켠다. Play 중 Hierarchy `DontDestroyOnLoad › NetworkManager › InterruptProductionDirector` 에서 켜면 다음 간파부터 반영.

## 5. 확인 부탁 (Play)

- `targetFlash` 꺼짐: 23호·건틀릿·스피너·월봇 간파 성공 = 슬로우·그로기는 그대로, 섬광 없음.
- 켬: 같은 순간 몸 표면에서 섬광(호스트·클라 둘 다, 각 화면에서 켠 경우).
- 섬광 칸이 빈 대상: 켜든 끄든 경고 1회(지금과 같음).
