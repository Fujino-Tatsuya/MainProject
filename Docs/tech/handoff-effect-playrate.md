# 공유: 이펙트 재생 배율(playRate) — 민경 님께

> 할 일 52.1(팔라딘 R 1.2배)에서 이펙트 공용 코드를 고쳤다. 기본값으로 부르면 예전과 같다.
> 커밋: `abf5fd55`(배율 도입), 후속 커밋(원샷 회수용 `PlayRecallable`).

## 1. 바뀐 것

### Shuriken 드라이버 (`ShurikenEffectSystem` / `ShurikenPartCache`)
- `SetPlayRate(rate)` 가 이제 **저작 simulationSpeed × rate** 를 넣는다. 예전엔 `rate` 를 그대로 덮었다.
- 저작값은 인스턴스 캐시를 처음 만들 때(첫 재생 전) `ShurikenPartCache.authoredSpeeds` 에 한 번 읽어 둔다.
- 풀 반납 때 **저작값으로 복원**한다. 예전엔 1 로 덮어서, 저작값이 1 이 아닌 프리팹은
  **두 번째 재생부터 1배속**이 됐다(버그). 이번에 고쳐졌다.
- 히트스톱 감속(`EffectManager.SetPlayRateForTarget`)도 같은 함수를 타므로 이제 **저작값 기준 상대값**이다
  (rate 0.1 = 저작 속도의 10%).
- 이 방식(저작값 × 배율, 반납 시 저작값 복원)은 **사용자 승인(2026-10-16)** — 유지한다.

### 영향 받는 프리팹 (simulationSpeed ≠ 1, EffectManager 로 재생되는 것)
| 프리팹 | 저작 simulationSpeed | 예전 | 지금 |
|---|---|---|---|
| `Players/Player1/RSkill/FX_Sword_Electric` | 2 | 첫 재생 2, 이후 1 | 항상 2 (팔라딘 R 은 2 × 1.2 = 2.4) |
| `Boss/Grab/FX_Grab_ArmElectric` | 2 (전 파트) | 첫 재생 2, 이후 1 | 항상 2 |
| `Boss/Grab/FX_Magnetic_Pull` | 2 (6파트, 나머지 2파트 1) | 첫 재생 2, 이후 1 | 항상 저작값 |

(`Dissolve/FX_Dissolve_Death` 도 3 이지만 엔트리·코드 참조가 없어 해당 없음.)
→ 보스 잡기 두 이펙트는 **두 번째부터 의도보다 느렸던 것이 저작 속도로 돌아온다.** 눈으로 한 번 확인 부탁.

### EffectManager API (`Assets/1.Scripts/Effects/EffectManager.cs`)
- `Play(entry, pos, rot, scale, durationOverride, playRate = 1)` · `PlayLooping(..., partDuration, playRate = 1)` — 선택 인자. 안 넘기면 기존 동작.
- `PlayRecallable(...)` — 원샷인데 **핸들을 돌려준다**. 수명이 끝나면 스스로 반납되니 버려도 새지 않는다.
  도중에 걷을 때 `ReleaseImmediate(handle)`. (팔라딘 R 마무리 낙뢰용)
- `EffectSocketPlayer.SetPlayRate(rate)` — 소켓도 배율을 받는다. `Play`/`PlayOnce` **전에** 부를 것(대출 시점에 확정).

### 배율이 걸리는 범위
- 파티클 simulationSpeed(저작값에 곱), 그래서 **프리팹 startDelay 도 같이 줄어든다**(0.4 → 1.2배면 0.333초, 실측).
- 엔트리 **파트 delay·수명(duration)** 도 ÷rate (매니저 시계가 `deltaTime × playRate` 로 돈다).
  - 그래서 팔라딘 R 칼날 전기(`FX_Sword_Electric`) 엔트리 delay 0.45 가 `vfxPlayRate` 1.2 에서 실제 **0.375초**로 나온다.
    **보류 중(2026-10-16 사용자 결정)** — 코드·데이터는 손대지 않았다.
- 트레일은 배율 속성이 없어 그대로.

## 2. 팔라딘 R 에서 쓰는 곳
값의 원본: `Assets/9.ScriptableObject/Player/Garen/FirstMeleeUltimateSkillData.asset` 의 `vfxPlayRate`(1.2).

| 이펙트 | 부르는 곳 |
|---|---|
| 칼 낙뢰·칼날 전기(소켓) | `PlayerSkillVfx.PlayLocal` → `EffectSocketPlayer.SetPlayRate` |
| 발밑 표식 | `PlayerSkillVfx.PlayTargetFloorLocal` → `PlayLooping(..., playRate)` |
| 마무리 낙뢰(하늘 칼) | `PlayerSkillVfx.PlayTargetStrikeLocal` → `PlayRecallable(..., playRate)` |
| 칼 꽂기 Stick(클립 이벤트) | `FirstMeleeUltimateSkill.OnClientPlay` → `stickVfx.SetPlayRate` |

마무리 낙뢰는 클립 `Garen_Ultimate_Skill` 의 **Custom0 이벤트**(클립 1.2초 = 애니 배율 1.2 로 실제 1.0초, Stick 과 같은 시각)에
서버가 시전당 1회 쏘고, End 이벤트(Completed)로 끝나지 않으면(대상 소실·사망·안전망 종료) 걷힌다. 시각을 바꾸려면 클립에서 Custom0 을 옮긴다(데이터 필드 없음).
시각표: `C:\UnityProject\_lane-artifacts\MainProject\52-paladin-ult\521-timing-table.md`.
