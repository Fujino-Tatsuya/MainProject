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
