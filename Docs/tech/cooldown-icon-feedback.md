# 쿨다운 아이콘 연출 (할 일 259)

전투 HUD 의 쿨다운 표시가 있는 슬롯 전부(스킬 Q/E/우클릭/R · 대시 · 패시브)에 같은 연출을 얹는다.

1. **어둡게** — 쿨다운 중 아이콘을 약간 어둡게(기본 밝기 0.6), 끝나면 원래 색.
2. **펄스** — 끝나는 순간 아이콘이 1 → 1.15 → 1 (기본 0.25초).
3. **번쩍** — 끝나는 순간 아이콘이 매우 밝아졌다가(흰 덮개 알파 0.85 → 0, 기본 0.3초) 원래 색. ②③ 은 동시에 한 번.

## 1. 구조

- 코드: `Assets/1.Scripts/UI/Combat/CooldownIconFeedback.cs` 한 파일.
  - `CooldownIconFeedbackSettings` — 튜닝 값(인스펙터). 각 HUD 가 하나씩 든다(스킬 HUD 는 슬롯 4개 공용).
  - `CooldownIconFeedbackRules` — 순수 규칙(끝남 판정·대시 충전 증가·어둡게 알파·펄스·번쩍 곡선). EditMode `CooldownIconFeedbackRulesTests`.
  - `CooldownIconFeedback` — 슬롯 하나의 런타임 상태(MonoBehaviour 아님). HUD 가 `Refresh` 에서 매 프레임 `Tick`.
- 쓰는 곳: `SkillCooldownHUD`(슬롯마다) · `DashCooldownHUD` · `PassiveHUD`. 판정 값은 각 HUD 의 기존 출처 그대로(스킬 = 쿨 장부 남은 초, 패시브 = `RemainingCooldown`, 대시 = 예측 충전 수).

## 2. 어떻게 그리나 (프리팹 수정 없음)

- **어둡게·번쩍 = 덮개 Image 하나.** 처음 필요할 때 아이콘의 부모(`Icon_mask` — Mask)의 맨 뒤 자식으로 `CooldownFeedbackOverlay` 를 런타임에 만든다.
  스프라이트 없음 + 늘림 앵커라 마스크가 아이콘 창 모양으로 잘라 준다.
  - 쿨다운 중: 검정, 알파 = 1 − `cooldownBrightness` (0.6 → 0.4). 결과 색 = 아이콘 × 0.6 과 같다.
  - 끝나는 순간: `flashColor`(흰색), 알파 = `flashPeakAlpha` × (1 − t)² 로 사라짐.
  - 알파 0 이면 `enabled = false`(그리기 없음).
  - **아이콘 `Image.color` 는 건드리지 않는다** — 차단 색(Soul)·후속 tint·특성 회색조·패시브 Ready/쿨 색과 따로 곱해진다.
  - UI Image 색은 1 에서 막혀 아이콘 자체를 더 밝힐 수 없어서 덮개 알파로 한다. 블룸·포스트 프로세싱·전용 셰이더 없음.
  - 마스크 창 전체가 밝아진다(아이콘 그림 안의 투명 부분도 마스크 안이면 같이 밝아짐).
- **펄스 = `Icon_mask` 의 localScale.** 시작 순간 스케일을 기억해 그 × 배율로, 끝나면 그 값으로 되돌린다.
  - 아이콘 자체(대시 아이콘 x −1 뒤집기)와 덮개 마스크 `Icon_mask_CooldownFill`(다른 브랜치의 1.02 배)은 건드리지 않는다.
  - 프레임·단축키 배지는 커지지 않는다 — 아이콘 창만 프레임 위로 살짝 커진다.
  - HUD 가 꺼지면(`OnDisable`) 스케일을 되돌리고 덮개를 숨긴다.
- 차단 색 캐시(`SetBlocked` 의 `GetComponentsInChildren<Graphic>`)는 `Awake` 에 잡혀서 나중에 만든 덮개는 들어가지 않는다 — 차단 중에도 덮개는 제 색.

## 3. 판정 규칙

| 슬롯 | 어둡게 | 펄스·번쩍 |
|---|---|---|
| 스킬 Q/E/우클릭/R | 남은 쿨 > 0 | 남은 쿨 > 0 → 0 이 된 프레임 |
| 패시브 | 남은 쿨 > 0 | 남은 쿨 > 0 → 0 이 된 프레임 |
| 대시(충전식) | **충전 0**(대시 불가) 동안만 | **충전이 하나 늘어난 프레임**(0→1, 1→2 모두) |

- 대시는 충전이 1개 이상 남은 재충전 중엔 쓸 수 있으니 어둡게 하지 않는다(덮개 진행 표시·남은 초는 그대로).
  최대 충전 1 이면 일반 쿨다운과 같다.
- 바인딩이 바뀐 프레임(플레이어 재바인딩, 대시 최대 충전이 바뀐 프레임)은 기준을 새로 잡아 가짜 연출을 막는다. 첫 프레임도 연출 없음.
- 끝 연출 중 쿨이 다시 시작되면 덮개는 바로 어둡게로, 펄스는 남은 시간만큼 계속.

## 4. 시간

- 연출 시간은 `Time.unscaledDeltaTime`(실시간) — 간파 슬로모션 중에도 같은 속도. 보스 HP 인트로(`BossHealthHUD`)와 같은 HUD 연출 규칙.
- 끝나는 시점 자체는 각 HUD 의 쿨 시계(스킬·대시 = `PlayerCooldownClock` 기준 남은 초) 그대로라, 쿨 속도 배율이 있으면 그만큼 빨리 끝난다.

## 5. 튜닝

`CombatHUD.prefab` 의 `SkillCooldownHUD` / `DashCooldownHUD` / `PassiveHUD` 인스펙터 **Cooldown Feedback** 묶음.

| 칸 | 기본 | 뜻 |
|---|---|---|
| `cooldownBrightness` | 0.6 | 쿨다운 중 밝기(1 = 그대로) |
| `pulsePeakScale` | 1.15 | 펄스 최대 배율(원래 스케일 기준) |
| `pulseSeconds` | 0.25 | 펄스 시간(0 = 끔) |
| `flashPeakAlpha` | 0.85 | 번쩍 덮개 최대 알파 |
| `flashSeconds` | 0.3 | 번쩍 사라지는 시간(0 = 끔) |
| `flashColor` | 흰색 | 번쩍 색 |

- 프리팹에 저장된 값이 없으면 위 기본값(코드 초기값)을 쓴다. 프리팹을 저장하면 그때 값이 YAML 에 들어간다.
- 대시 아이콘은 `DashCooldownHUD.icon` 이 비면 슬롯 루트(`cooldownFill` 의 부모) 아래 `Icon_mask` 첫 자식을 쓴다.
