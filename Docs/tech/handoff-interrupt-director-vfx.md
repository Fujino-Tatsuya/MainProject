# 공유 — 간파 연출은 이제 디렉터가 튼다 (할 일 118, 민경)

> 작성: 은희 레인(claude-alt), 2026-10-27. 브랜치 `feature/InterruptProductionDirector`.
> 설계 원본: [interrupt-presentation.md §6](interrupt-presentation.md#6-간파-연출-디렉터--interruptproductiondirector-할-일-118).
> 104 공유 문서 [handoff-interrupt-fx-toggles.md](handoff-interrupt-fx-toggles.md) 를 대체한다.

## 1. 무엇이 바뀌었나

- 간파 연출(슬로우 위의 히트스톱·암순응·줌·성공 이펙트 칸·캐릭터 이펙트·섬광)은 **`InterruptProductionDirector` 하나가 "틀지 말지" 정하고 부른다.**
  디렉터는 `NetworkManager.prefab` 루트에 있다(옛 `InterruptPresentationToggles` 자리 — 그 스크립트는 삭제됨).
- **이펙트 에셋·클립 이벤트·소켓(`EffectSocketPlayer`)·RPC 전송로는 그대로다.** 받는 쪽 재생 지점이 디렉터에게 묻는 형태로만 바뀌었다 → 보이는 동작은 같다.
- 끄기·회수(`StopEffect`·`FadeOut`·루프 `Stop`·`EndInterrupt`)는 디렉터를 거치지 않고 늘 돈다.

## 2. 민경 파일 변경 — `Effects/EffectAnimEvents.cs`

- `PlayEffect`·`StartEffect` 의 한 줄 게이트가 `InterruptProductionDirector.AllowsAnimEffect(id)` 로 바뀌었다(118 C1). 의미는 104 와 같다 — 간파 id 넷만 칸을 보고, 다른 id 는 늘 통과.
- `Played` 알림은 재생을 막아도 그대로 난다. `StopEffect` 는 막지 않는다. 둘 다 불변.

## 3. 간파 id 상수 위치

- `ShieldCharge`(Player_Paladin) · `Assassin_InsightWindup` / `Assassin_InsightPop` / `Assassin_InsightSlash`(Player_Assassin `VFX/RightClick/*`)
  → **`InterruptProductionRules`** 상수(`Assets/1.Scripts/Player/InterruptProduction/InterruptProductionRules.cs`).
- 이 소켓들의 id 를 바꾸면 거기도 같이 바꿀 것 — EditMode `InterruptProductionDirectorTests` 의 프리팹 소켓 id 검사가 잡는다(`Tools/Tests/슬로우 모션 EditMode 테스트 실행`).

## 4. 은희 영역이지만 민경이 배선한 곳 (118 C5)

| 연출 | 칸 | 바뀐 점 |
|---|---|---|
| 팔라딘 방패 발광·루프 | B1 | `FirstMeleeInterruptSkill.OnClientPlay` → `Director.LocalAttempt` → 디렉터가 칸을 보고 스킬의 `PlayCue` 로 `shieldGlow.FadeIn`·`glowLoop.Play`. `OnEnd` 의 끄기·회수 그대로 |
| 팔라딘 충격파 | B3 | 서버: 정적 `ServerInterruptSucceeded` 구독 → 디렉터 성공 단계가 스킬의 relay(`IInterruptSuccessRelay`)를 부른다 → 기존 `PlayerShieldVfx.ServerInterruptWave`·`InterruptWaveRpc`. 각 피어: `PlayInterruptWave` → `Director.PlayCue(PaladinWave, …, this)` → `interruptWave.PlayOnce` |
| 어쌔신 간파 성공 | C2 | 서버: 정적 구독 → `AssassinSkillView` 의 relay → 기존 `InterruptSucceededRpc`. 각 피어: 성공 대기 ∧ 올려치기 프레임 맞추기는 View 에 그대로, 재생만 `Director.PlayCue(AssassinInsightSuccess, …, this)` |

- 정적 `PlayerInterruptSkillBase.ServerInterruptSucceeded` 는 튜토리얼·통계용으로 남아 있다 — 연출 쪽에서 새로 구독하지 말 것.

## 5. 새 연출 넣는 법

- **성공 이펙트 하나 더**: 먼저 성공 이펙트 칸 A4(`InterruptSuccessFx` 의 `casterFx`·`targetFx`, 캐릭터 Variant 에서 소켓 연결)로 해결되는지 본다 — 코드 변경 없음.
  절차는 [interrupt-presentation.md §4](interrupt-presentation.md#4-성공-이펙트-넣는-법-이펙트-담당--민경).
- **새 종류의 cue 가 필요하면 은희에게** — 칸(스위치)·단계(시도/판정/성공/실패)·채널(개인/공용)을 정해 디렉터에 더한다.
  `GlobalTimeScale.Started` 를 직접 구독하지 않는다(디렉터가 유일한 구독자).

## 6. 스위치 위치·Play 중 바꾸기

- `NetworkManager.prefab › InterruptProductionDirector` 칸 10개, **기본 전부 꺼짐**(104 와 같다). 이펙트 작업 중 위 연출이 안 보이면 칸이 꺼져 있는 것이다.
- Play 중엔 Hierarchy `DontDestroyOnLoad › NetworkManager › InterruptProductionDirector` 에서 켜면 다음 간파부터 바로 반영된다(캐싱 없음).
- **피어별 값**: 히트스톱(A1)·슬로우 프로필은 호스트 값만, 나머지 칸은 각 화면 자기 값(동기화 없음). MPPM 가상 플레이어 화면은 호스트에서 켜도 안 바뀐다.

## 7. 확인 부탁 (Play)

- 기본(전부 꺼짐): 간파 성공 = 슬로우만, 캐릭터 이펙트 없음.
- B1 켬: 팔라딘 우클릭 시작에 방패 발광, 끝나면 꺼짐(판정 전 경직·사망 때도 남지 않음).
- B2 켬: 방패 기 모으기가 판정 순간 꺼짐. B3 켬: 간파 성공 때만 충격파(간파 창 없는 대상·일반 몹엔 없음).
- C1 꺼짐 + C2 켬: 어쌔신 성공 이펙트가 올려치기 프레임에 나옴. C1 켬: 기 모으기·튀기기·올려치기.
- 호스트·클라 두 화면 모두에서(칸은 화면마다 켜야 한다).
