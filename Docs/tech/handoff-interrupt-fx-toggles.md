# 공유 — 간파 연출 스위치가 다른 담당 영역에 한 줄씩 들어갔다 (할 일 104)

> ⚠️ **118 로 대체(2026-10-27).** `InterruptPresentationToggles` 는 삭제되고 스위치 10칸은 간파 연출 디렉터
> `InterruptProductionDirector`(같은 `NetworkManager.prefab`)로 옮겨졌다. 아래 내용은 104 당시 기록이다 — 지금 구조는
> [interrupt-presentation.md §6](interrupt-presentation.md#6-간파-연출-디렉터--interruptproductiondirector-할-일-118),
> 공유는 [handoff-interrupt-director-vfx.md](handoff-interrupt-director-vfx.md)(민경) · [handoff-interrupt-director-target-flash.md](handoff-interrupt-director-target-flash.md)(경석).

> 작성: 은희 레인(claude-alt), 2026-10-26. 브랜치 `feature/InterruptFxToggle`.
> 설계 원본: [interrupt-presentation.md §6](interrupt-presentation.md#6-연출-스위치--interruptpresentationtoggles-할-일-104).

간파 성공 연출을 슬로우만 남기고 전부 끄되 지우지 않으려고, `NetworkManager.prefab` 에 `InterruptPresentationToggles`(bool 10칸, 기본 전부 꺼짐)를 붙이고
재생 지점마다 스위치를 확인하는 **한 줄 게이트**만 넣었다. 연출 로직·이펙트 에셋·판정은 바뀌지 않았다.
**지금 기본값이라 아래 연출은 Play 에서 안 나온다** — 보이게 하려면 `NetworkManager.prefab` 인스펙터에서 해당 칸을 켠다.

## 경석 영역 — 보스·엘리트 섬광 (E1 `targetFlash`)

`PlayInterruptFlashRpc` 본문에 한 줄씩. 섬광 칸이 비어 있을 때의 경고(`WarnNoInterruptFlashOnce`)는 그 앞에 그대로 두고, 재생 직전에서만 막는다.

- `Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs`
- `Assets/1.Scripts/Monster/Boss/GauntletBot.cs`
- `Assets/1.Scripts/Monster/Boss/SpinnerBot.cs`
- `Assets/1.Scripts/Monster/Boss/WallBot.cs`

판정·그로기·통계·RPC 발신은 그대로다(섬광 RPC 는 그대로 날아가고 받는 쪽에서 재생만 건너뛴다).
같은 게이트가 은희 영역 `TrainingDummyInterrupt`·`TrainingSparringBot` 에도 있다.

## 민경 영역 — `Effects/EffectAnimEvents.cs` (B2·C1)

`PlayEffect`·`StartEffect` 에서 `effect.PlayOnce()`/`Play()` 앞에 `InterruptPresentationToggles.AllowsAnimEffect(id)` 확인 한 줄씩.

- 막히는 id 는 간파 시도 이펙트 넷뿐: `ShieldCharge`(Player_Paladin) · `Assassin_InsightWindup` / `Assassin_InsightPop` / `Assassin_InsightSlash`(Player_Assassin `VFX/RightClick/*`).
  목록은 `InterruptPresentationToggles` 상수 한 곳 — **이 소켓들의 id 를 바꾸면 거기도 같이 바꿀 것**(EditMode `프리팹_소켓_id_가_상수와_같다` 가 잡는다).
- 다른 id 는 전과 똑같다. `StopEffect` 는 막지 않는다(켜진 루프가 남지 않게).
- 재생을 막아도 `Played` 알림은 그대로 난다 — `AssassinSkillView` 가 올려치기 알림을 성공 이펙트(C2) 기준 프레임으로 쓰기 때문.

이펙트 작업 중 위 넷이 안 보이면 스위치가 꺼져 있는 것이다(에셋 문제 아님).

## 확인 필요 (사람 Play)

- 기본(전부 꺼짐)에서 간파 성공 시 슬로우만 걸리고 히트스톱·암순응·줌·섬광·캐릭터 이펙트가 안 나오는지.
- 칸을 하나씩 켰을 때 그 연출만 돌아오는지, 특히 C1 꺼짐 + C2 켜짐에서 어쌔신 성공 이펙트가 올려치기 프레임에 나오는지.
