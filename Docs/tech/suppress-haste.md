# 제압 가속 버프 (할 일 91)

> 2026-10-22 · 담당 은희(Player·스탯·상태이상·UI). 계획: `PLAN-91-suppress-haste.md`(레인 산출물 `_lane-artifacts/common/91/`).
> 이 문서는 버프 전체를 한 장으로 묶는 입구다. 세부는 링크한 원본 문서를 따른다.

## 1. 한눈에
| 항목 | 내용 | 원본 |
|---|---|---|
| 발동 | 최종보스(23호)가 **제압에 실제로 들어가는 순간** 서버 1회. 예약만 된 제압은 소비될 때 | [handoff-suppress-haste.md](handoff-suppress-haste.md) §1 |
| 대상 | 그 순간 **Alive** 플레이어 전원. 쓰러짐·Soul·사망 제외 | 같은 문서 §2 |
| 도중 변화 | 버프 중 Alive 를 벗어나면 그 출처 3개 제거. 부활해도 다시 안 건다 | 같은 문서 §2 |
| 지속 | 시트 `durationOverride` > 0 이면 그 초, 0 이면 이번 제압 길이. 만료는 상태이상 스윕 | 같은 문서 §2 |
| 효과 | 이동속도 ×2 · 공격속도 ×2(최종 배율 단계) · 쿨 줄어드는 속도 ×2 | [player-stats.md](player-stats.md) §2·§3·§3.1·§7 |
| 상태이상 | `CooldownRateModifier`(대표) · `MoveSpeedFinalMultiplier` · `AttackSpeedFinalMultiplier` — 출처 = 보스 `NetworkObjectId`, 스택 1 | player-stats.md §3 |
| HUD 아이콘 | 대표(`CooldownRateModifier`) 1개만, 나머지 둘 숨김(`SuppressHasteRules.IsHudHidden`) | — |
| 데이터 | xlsx `SuppressHaste` 시트 → `Assets/Resources/Player/SuppressHasteData.asset` | [data-table.md](data-table.md) 시트 표 |
| 다른 출처 | `SuppressHaste.GrantServer(sourceId, suppressDuration)` — 보스 종류 무관 서버 API | handoff §2 |

- 쿨 속도 배율의 소비처(쿨 시계 — 스킬·궁극기·대시 충전·패시브 내부 쿨)는 B 레인(PLAN T2~T4)이다. 그 전까진 상태이상만 걸리고 쿨은 그대로다.

## 2. 화면 연출 — 비네팅 (PLAN T7)
| 무엇 | 위치 |
|---|---|
| 코드 | `Assets/1.Scripts/UI/Combat/SuppressHasteVignetteHUD.cs` |
| 프리팹 | `Assets/2.Prefabs/UI/SuppressHasteVignette.prefab` — 풀스크린 `Image`(raycastTarget 끔) |
| 임시 스프라이트 | `Assets/2.Prefabs/UI/Textures/SuppressHasteVignette_Temp.png` — 흰색, 가장자리 불투명·중앙 투명(에셋 1장, 런타임 생성 없음) |
| 배치 | `CombatHUD.prefab` › `Layer_Persistent` 아래 `ShieldVignette` 바로 다음 형제 인스턴스 1개(다른 요소는 그대로) |

- **표시 조건**: 로컬 플레이어(`Player.LocalPlayerChanged` 로 직접 받음 — `CombatHUD.cs` 무변경)에게 `CooldownRateModifier` 가 있으면 페이드 인, 없어지면 페이드 아웃.
  `StatusEffectController.EffectsChanged` 때만 다시 읽는다(매 프레임 탐색 없음). 바인딩 순간 한 번 직접 읽는다(첫 동기화엔 이벤트가 안 난다).
- 쓰러지면 서버가 버프를 빼므로 비네팅도 같이 사라진다 — 생명 상태를 따로 보지 않는다.
- 다른 플레이어의 버프는 안 보인다(로컬 플레이어 것만).
- 페이드는 게임 시간(`Time.deltaTime`) — 실드 비네팅과 같이 인터럽트 슬로우 동안 느려진다. 계산은 `ShieldVignetteModel.StepAlpha` 재사용.

### 2.1 인스펙터 필드 (프리팹)
| 필드 | 기본 | 뜻 |
|---|---|---|
| `vignetteSprite` | 임시 흰색 PNG | **이미지 교체 = 이것만 바꾼다** |
| `vignetteColor` | 흰색 | 스프라이트에 곱하는 색. 컬러 이미지로 바꾸면 흰색 그대로 둔다 |
| `maxAlpha` | 0.6 | 버프 중 최대 알파 |
| `data` | `SuppressHasteData.asset` | 페이드 인/아웃 시간(`vignetteFadeIn` 0.2 / `vignetteFadeOut` 0.3)을 읽는 시트 에셋. 비우면 Resources 에서 찾는다 |

- 페이드 시간은 프리팹이 아니라 **시트**(xlsx `SuppressHaste`)에서 고친다 — 빌드는 항상 xlsx 값([data-table.md](data-table.md)).

### 2.2 이미지로 바꾸는 법
1. 가장자리만 불투명한 스프라이트(Texture Type = Sprite, Clamp 권장)를 `Assets/2.Prefabs/UI/Textures/` 또는 아트 경로에 둔다.
2. `SuppressHasteVignette.prefab` 의 `vignetteSprite` 에 넣는다. 컬러 이미지면 `vignetteColor` 를 흰색으로 둔다.
3. 임시 PNG 는 참조가 없어진 뒤 지워도 된다.

## 3. 팔라딘 E — 공속 적용 범위 (할 일 91 A5, 2026-10-22 사용자 결정)

A2(T5) 에서는 E 전체가 `Animator.speed` 로 공속을 타 **방어 창도 짧아졌다**. E 를 네 상태로 나누고 방어중만 공속에서 뺀다.

| 상태(`GarenAnimatorController`) | 클립 | 상태 Speed | 공속 | 이벤트 |
|---|---|---|---|---|
| 준비 `SubSkillGuard` | `Garen_E_GuardStart` 1.0s | 2 × `AttackSpeedRate` | ✅ | `Custom0` = 방어 창 열림 |
| 방어중 `SubSkillGuardIdle` | `Garen_E_GuardIdle` 3.5s 루프 | 1 | ❌ | 없음. 길이 = 해제로 가는 전이 exitTime(0.143 ≈ 0.5s) |
| 해제 `SubSkillGuardEnd` | `Garen_E_GuardEnd` 1.0s | 2 × `AttackSpeedRate` | ✅ | `Custom1` = 창 닫힘(E 계속) · `End` = E 종료 |
| 반격 `SubSkillCounter` | `Garen_E_GuardCounter` 1.167s | 1 × `AttackSpeedRate` | ✅ | `Hit` 반격 판정 · `End` 종료(기존 그대로) |

- 준비 → 방어중(exitTime 1 · 0.1s), 방어중 → 해제(exitTime 0.143 · 0.1s) 전환은 **초 단위 고정**이라 공속과 무관하다. 방어 창 실제 길이 ≈ 준비 클립 `Custom0` 이후 + 방어중 + 해제 클립 `Custom1` 이전.
- 파라미터 `AttackSpeedRate`(기본 1)는 `PlayerAttackSpeedAnimator` 가 전 피어에서 `Player.AttackSpeedRate` 로 쓴다 — 호스트·클라 같은 값. 팔라딘 E 는 `Animator.speed` 배율 대상이 아니다(`ScalesWithAttackSpeed = false`). 상세: [player-stats.md](player-stats.md) §3.1.
- 모델(`FirstMeleeGuardCounterModel`): Guarding →(`Custom1`) **Releasing** →(해제 클립 `End`) Finished. 방어 성공이면 Countering →(반격 클립 `End`) Finished. 단계가 안 맞는 이벤트(반격으로 CrossFade 중 늦게 온 해제 클립 `Custom1`, Guarding 중 `End`)는 무시한다.
- 서버 임시 안전망 = `maxActiveDuration`(1.5) ÷ min(1, 배율) + padding(`ScalesSafetyNetWithAttackSpeed = true`) — 감속만 늘리고 가속엔 줄이지 않는다.
  방어중 길이는 애니메이터 전이에만 있어 런타임에 따로 뺄 수 없으므로 **전체**를 늘린다(정밀 분할보다 길 뿐 먼저 끊지 않는다). SO 필드는 늘리지 않았다.

### 3.1 이벤트 위치 — 🟨 사용자 튜닝 대상 임시값
| 클립 | 이벤트 | 클립 시각 | 실제 시각(배율 1, Speed 2) |
|---|---|---|---|
| `Garen_E_GuardStart` | `Custom0` 창 열림 | **0.9** (이전 0) | 0.45s — 준비 모션 동안은 비방어 |
| `Garen_E_GuardEnd` | `Custom1` 창 닫힘 | **0.1** (이전 0.933) | 해제 시작 0.05s |
| `Garen_E_GuardEnd` | `End` E 종료(신규) | **0.967** | 0.483s — 해제 모션 끝까지 재생 후 종료 |

- 튜닝은 Animation 창에서 이벤트를 옮기면 된다(코드·SO 변경 없음). EditMode `FirstMeleeGuardAttackSpeedTests` 는 값이 아니라 순서(`Custom1` < `End` < 클립 길이, `Custom0` < 준비 클립 길이)만 지킨다.
