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
| 연출 | 비네팅(§2) · 피해 숫자 합산 · N타 줌인(기본 4타) · 보스 체력바 흰 칸 멈춤(§2.A, 할 일 174) | — |
| 다른 출처 | `SuppressHaste.GrantServer(sourceId, suppressDuration)` — 보스 종류 무관 서버 API | handoff §2 |

- 쿨 속도 배율은 쿨 시계(§1.1)가 소비한다 — 스킬(우클릭·Q·E·궁극기)·대시 충전·가붕이 패시브 내부 쿨.

### 1.1 쿨 시계 (할 일 91 T2~T4·T8)
| 항목 | 내용 |
|---|---|
| 코드 | `Assets/1.Scripts/Player/Cooldown/` — `CooldownClock`(순수 적분 `Now += Δ원천 × 배율`) · `PlayerCooldownClock`(플레이어마다 1개, 쓰는 쪽이 `For` 로 공유) |
| 시계 2개 | **스킬** `SkillNow` — 원천 `Time.timeAsDouble` 증분(= `Time.deltaTime` 합, 슬로우 모션 반영). 스킬 쿨 장부·**가붕이 패시브 내부 쿨**이 쓴다<br>**대시** `DashNow` — 원천 `NetworkClock.GameNow` 증분(일시정지·슬로우 반영). 세션이 안 돌면 `Time` 증분, 원천이 바뀌는 프레임은 기준점만 옮긴다. 두 시각은 원점이 달라 서로 빼지 않는다 |
| 배율 연결 | `Player.Awake` 한 곳: `PlayerCooldownClock.SetRateSource(() => Player.CooldownRate)`. 두 시계가 같은 배율을 쓴다 |
| 배율 계산 | `Player.CooldownRate` = `StatusEffectController.GetStatMultiplier(CooldownRateModifier)` — 이동·공속 최종 배율(`PlayerStats.FinalMultiplier`)과 같은 집계. 같은 타입 여러 개 = 곱, 스택 = magnitude^스택, 없으면 1. 순수 식 `StatusEffectController.StatMultiplierOf` |
| 피어별 | 서버·오너·원격 모두 **자기 복제본**으로 계산해 자기 시계에 쌓는다 — **시계 동기화 없음**. 상태이상이 만료·제거되면 목록이 바뀐 다음 프레임 구간부터 1 |
| 서버↔오너 | "남은 초"만 주고받는다. 스킬 = `CommitCooldownClientRpc`(남은 초) · 대시 = 응답 `DashServerResponse.NextChargeRemaining`(남은 초 — 이전 절대시각 `NextChargeReadyServerTime` 폐기) · 패시브 = 복제 `PassiveCooldownStamp`(남은 초 + 서버 시각, 늦은 참가는 흐른 시간 × 배율을 뺀다) |
| 판정 | 서버 장부(기존 그대로). 오너 장부는 HUD·예측용 |

- 🔴 **같은 빌드 필수** — 대시 응답 형식(절대시각 → 남은 초)·패시브 복제 형식이 바뀌어 이전 빌드와 섞이면 대시 충전·패시브 쿨 표시가 깨진다(데모 부스 규칙과 같음).
- **알려진 오차**: 배율이 바뀌는 순간 서버↔오너 시계 차 = 상태이상 복제 지연만큼(버프 시작·만료 각각 RTT/2 안팎 × (배율 − 1)). HUD 표시용 허용 범위이고 판정은 서버라 결과는 서버를 따른다.
  패시브 늦은 참가 환산은 현재 배율 하나로 흐른 시간을 곱한다 — 도장 이후 배율이 바뀌었으면 그만큼 어긋난다.
- **결정(2026-10-22 메인)**: 가붕이 패시브 내부 쿨은 스킬 쿨과 같은 스킬 시계를 써 **슬로우 모션을 반영**한다(이전 = 복제된 절대 서버시각 `readyServerTime` 기준이라 슬로우와 무관했다).
- 테스트: EditMode `CooldownClockTests`·`DashCooldownClockSyncTests`·`PassiveCooldownSyncTests`·`CooldownRateWiringTests`(배율 2 스킬·대시·패시브 절반, 중간 해제 2→1, 곱 계산).

## 2. 화면 연출 — 비네팅 (PLAN T7)
| 무엇 | 위치 |
|---|---|
| 코드 | `Assets/1.Scripts/UI/Combat/SuppressHasteVignetteHUD.cs` |
| 프리팹 | `Assets/2.Prefabs/UI/SuppressHasteVignette.prefab` — 풀스크린 `Image`(raycastTarget 끔) |
| 임시 스프라이트 | `Assets/2.Prefabs/UI/Textures/SuppressHasteVignette_Temp.png` — 흰색, 가장자리 불투명·중앙 투명(에셋 1장, 런타임 생성 없음) |
| 배치 | `CombatHUD.prefab` › `Layer_Persistent` 아래 인스턴스 1개(다른 요소는 그대로. 옛 `ShieldVignette` 형제는 실드 비네팅 삭제로 없어짐) |

- **표시 조건**: 로컬 플레이어(`Player.LocalPlayerChanged` 로 직접 받음 — `CombatHUD.cs` 무변경)에게 `CooldownRateModifier` 가 있으면 페이드 인, 없어지면 페이드 아웃.
  `StatusEffectController.EffectsChanged` 때만 다시 읽는다(매 프레임 탐색 없음). 바인딩 순간 한 번 직접 읽는다(첫 동기화엔 이벤트가 안 난다).
- 쓰러지면 서버가 버프를 빼므로 비네팅도 같이 사라진다 — 생명 상태를 따로 보지 않는다.
- 다른 플레이어의 버프는 안 보인다(로컬 플레이어 것만).
- 페이드는 게임 시간(`Time.deltaTime`) — 인터럽트 슬로우 동안 느려진다. 계산은 `SuppressHasteVignetteModel.StepAlpha`(옛 실드 비네팅 모델에서 옮김).

### 2.1 인스펙터 필드 (프리팹)
| 필드 | 기본 | 뜻 |
|---|---|---|
| `vignetteSprite` | 임시 흰색 PNG | **이미지 교체 = 이것만 바꾼다** |
| `vignetteColor` | 흰색 | 스프라이트에 곱하는 색. 컬러 이미지로 바꾸면 흰색 그대로 둔다 |
| `fillAlpha`(옛 `maxAlpha`) | 0.25 | 버프 중 가장자리 채움 알파(0 = 스피드 라인만) |
| 스피드 라인(249) | `MA_SpeedLines`(셰이더 `UI/SpeedLines`) | `speedLineMaterial`·`lineColor`·`lineAlpha` 0.55·`lineCount` 90·`lineDensity` 0.5·`lineThickness` 0.35·`lineInnerMin/Max` 0.75/1.05·`lineFps` 24·`centerClearRadius` 0.6 — 런타임 자식 RawImage `SpeedLines` 에 절차 생성(텍스처 없음) |
| 켜짐 버스트(249) | 0.2초 × 1.8 | `burstDuration`·`burstMultiplier` — 켜지는 순간 선 밀도·알파를 배율만큼 올렸다 버스트 시간 동안 1 로. 시트 대상 아님(HUD 인스펙터) |
| 에디터 미리보기 | 꺼짐 | Play 없이 Prefab Mode 에서 강도·버스트 시점·선 배치 번호로 보기(저장 안 되는 자식, Play·빌드 무시) |
| `data` | `SuppressHasteData.asset` | 페이드 인/아웃 시간(`vignetteFadeIn` 0.2 / `vignetteFadeOut` 0.3)을 읽는 시트 에셋. 비우면 Resources 에서 찾는다 |

- 페이드 시간은 프리팹이 아니라 **시트**(xlsx `SuppressHaste`)에서 고친다 — 빌드는 항상 xlsx 값([data-table.md](data-table.md)).
- 주의(249 되살리기, 11-09): `CombatHUD.prefab` 인스턴스에 옛 이름 오버라이드 `maxAlpha` 0.981·`visibleAlphaThreshold` 0 이 있다. `fillAlpha` 로 이어지면 채움이 거의 불투명해 선이 묻힐 수 있으니 Play 에서 확인하고 값은 사용자가 정한다.

### 2.2 이미지로 바꾸는 법
1. 가장자리만 불투명한 스프라이트(Texture Type = Sprite, Clamp 권장)를 `Assets/2.Prefabs/UI/Textures/` 또는 아트 경로에 둔다.
2. `SuppressHasteVignette.prefab` 의 `vignetteSprite` 에 넣는다. 컬러 이미지면 `vignetteColor` 를 흰색으로 둔다.
3. 임시 PNG 는 참조가 없어진 뒤 지워도 된다.

## 2.A 연출 3가지 (할 일 174, 2026-11-02)

판정·피해·버프 수치는 안 바꾼다. "제압 가속 중" 판정은 한 곳 — `SuppressHastePresence`(`Assets/1.Scripts/Player/Stats/SuppressHastePresence.cs`):
대표 상태이상 `CooldownRateModifier` 보유(비네팅과 같은 기준). 상태이상 목록은 NetworkList 라 **전 피어 같은 답**이다 — 새 RPC·NetworkVariable 없음.

| # | 무엇 | 누가 보나 | 기준 | 코드 |
|---|---|---|---|---|
| 1 | 피해 숫자 합산 | 전 피어 | **공격자**에게 가속이 있으면 그 공격자 → 대상 피해를 숫자 하나로(공격 종류·Multi 무관) | [floating-damage-design.md §3.1](floating-damage-design.md#31-제압-가속-합산-할-일-174) |
| 2 | N타 줌인(기본 4타) | 내 화면만 | **로컬 플레이어**에게 가속이 있을 때 내가 보스에게 맞힌 타격 N번마다 | §2.2 |
| 3 | 보스 체력바 흰 칸 멈춤 | 전 피어 | 그 **보스가 건**(출처 = 보스 NetworkObjectId) 가속이 플레이어 누구에게라도 남아 있는 동안 | §2.3 |

### 2.2 N타 줌인
- 셈: 보스 `Unit` 의 피해 RPC(`ClientDamageDealtClientRpc`) 1회 = 1타. `Unit.ClientHitAttributed`(RPC 당 1회 — 보호막+체력을 함께 깎아도 1타) →
  `UnitCameraFeedbackReporter`(대상 `Rank == Boss` · 공격자 = 나) → `CameraTargetSwitcher.ReportLocalBossHit`.
  빗나감·0 피해(무적 잠김 등)는 RPC 가 없어 안 센다. 가속이 아니면 안 세고 0 으로 리셋, 낙하 뷰·관전 중엔 안 센다.
- 줌: 플레이어 vcam `FollowOffset` 배율(슬로우 줌과 같은 길). 실시간(`Time.unscaledDeltaTime`) — 히트스톱·슬로우와 무관하게 같은 길이.
  가속이 끝나도 진행 중인 펄스는 끝까지(튀지 않게), 셈만 리셋.
- 🟨 **슬로우 줌과 겹치면 건너뛴다(곱하지 않는다).** 이 프레임 슬로우 줌 배율이 1 이 아니면 펄스를 버리고 1 — 끝난 뒤 다시 나오지 않는다.
  이유: 제압 가속은 간파 성공으로 시작하므로 첫 타들이 내 간파 슬로우 줌(0.7~0.85배)과 바로 겹친다. 곱하면 0.63배까지 들어가 슬로우 줌의 의도된 깊이를 넘고,
  슬로우 중 미세한 앞뒤 흔들림은 연출이 아니라 잡음으로 보인다. 간파 연출이 우선이라 그동안은 N타 줌을 쉰다. 셈은 계속한다.
- 순수 로직 `SuppressHasteHitZoom`(`Assets/1.Scripts/Player/Stats/SuppressHasteHitZoom.cs`). 수치는 시트 칸(아래 표).

| 칸(xlsx `SuppressHaste`) | 기본 | 뜻 |
|---|---|---|
| `hitZoomEveryHits` | 4 | 몇 타마다(할 일 233 — 3 → 4, xlsx SVN r607) |
| `hitZoomFactor` | 0.9 | 카메라 거리 배율(1 = 줌 없음, 하한 0.1) |
| `hitZoomInSeconds` | 0.02 | 1 → 배율 (실시간 초) |
| `hitZoomHoldSeconds` | 0.11 | 배율 유지 — 들어감 + 유지 = 0.13초 뒤 복귀 시작 |
| `hitZoomReturnSeconds` | 0.06 | 배율 → 1 |

- 🟨 0.02/0.11/0.06 은 임시값 — 사용자 튜닝(Play 피드백으로 들어감·복귀를 0.04/0.12 에서 절반으로). 새 칸이라 병합 Export 전까지는 SO 값으로 돈다([data-table.md](data-table.md) §3).

### 2.3 보스 체력바 흰 칸(잔상) 멈춤
- `BossHealthHUD` 가 매 프레임 `DelayedHealthBar.SetFrozen(제압 중, 체력)` → 멈춘 동안 잔상은 내려가지 않아 제압 중 깎인 양이 흰 칸으로 쌓인다.
  회복이 와도 흰 칸을 지우지 않는다(잔상은 체력 아래로만 안 내려가게 맞춘다).
- 풀리면 쌓인 양(잔상 − 체력) 전체를 조각 하나로 붙잡아 평소 규칙(`holdSeconds` 0.4 머문 뒤 `drainRatePerSecond` 로 내려감).
- **제압 여부를 가속 버프로 읽는 이유**: 23호의 제압 플래그(`IsSuppressed`)는 서버 전용이고, 복제되는 값(그로기 상태 + 제압 칸 0)은 기믹 보상 제압(칸을 안 건드림)을 못 잡는다.
  제압 가속은 모든 제압 진입(칸·기믹 보상)에서 같은 길이(`durationOverride` 0)로 걸리고 이미 전 피어에 복제된다.
- **한계**: 시트 `durationOverride` > 0 이면 흰 칸 멈춤 길이 = 그 초(제압 길이와 달라진다). 살아 있는 플레이어가 아무도 없으면(전원 쓰러짐) 버프가 빠져 멈춤도 풀린다.
- 규칙 `DelayedHealthFreezeRule`(같은 파일). 플레이어·머리 위 바는 `SetFrozen` 을 안 불러 그대로다.
- 테스트: EditMode `SuppressHastePresentationTests`(N타 셈·리셋·펄스·슬로우 줌 건너뜀·잔상 멈춤/해제/회복) · `FloatingDamageAccumulationPolicyTests`(합산 키).

## 3. 팔라딘 E — 공속 적용 범위 (할 일 91 A5, 2026-10-22 사용자 결정)

A2(T5) 에서는 E 전체가 `Animator.speed` 로 공속을 타 **방어 창도 짧아졌다**. E 를 네 상태로 나누고 방어중만 공속에서 뺀다.

| 상태(`GarenAnimatorController`) | 클립 | 상태 Speed | 공속 | 이벤트 |
|---|---|---|---|---|
| 준비 `SubSkillGuard` | `Garen_E_GuardStart` 1.0s | 4 × `AttackSpeedRate` (≈ 0.25s) | ✅ | `Custom0` = 방어 창 열림 |
| 방어중 `SubSkillGuardIdle` | `Garen_E_GuardIdle` 3.5s 루프 | 1 | ❌ | 없음. 길이 = 해제로 가는 전이 exitTime(0.4286 × 3.5 = 1.5s) |
| 해제 `SubSkillGuardEnd` | `Garen_E_GuardEnd` 1.0s | 4 × `AttackSpeedRate` (≈ 0.25s) | ✅ | `Custom1` = 창 닫힘(E 계속) · `End` = E 종료 |
| 반격 `SubSkillCounter` | `Garen_E_GuardCounter` 1.167s | 1 × `AttackSpeedRate` | ✅ | `Hit` 반격 판정 · `End` 종료(기존 그대로) |

- 🟨 Speed 4·방어중 1.5s·안전망 4.0 은 할 일 91 A6(2026-10-22) **임시값** — 사용자가 튜닝한다(이전 A5: Speed 2·방어중 0.5s·안전망 1.5).
  배율 1 기준 E 전체 ≈ 0.25 + 1.5 + 0.25 = 2.0s. 창 닫히기 직전 방어 성공 시 최장 ≈ 1.78 + 반격 `End` 1.1 ≈ 2.9s.
- 준비 → 방어중(exitTime 1 · 0.1s), 방어중 → 해제(exitTime 0.4286 · 0.1s) 전환은 **초 단위 고정**이라 공속과 무관하다. 방어 창 실제 길이 ≈ 준비 클립 `Custom0` 이후 + 방어중 — 방어중 → 해제로 넘어가는 순간 서버가 창을 바로 닫는다(할 일 95, `Custom1` 은 늦은 보조 — 이미 닫혔으면 무시).
- 파라미터 `AttackSpeedRate`(기본 1)는 `PlayerAttackSpeedAnimator` 가 전 피어에서 `Player.AttackSpeedRate` 로 쓴다 — 호스트·클라 같은 값. 팔라딘 E 는 `Animator.speed` 배율 대상이 아니다(`ScalesWithAttackSpeed = false`). 상세: [player-stats.md](player-stats.md) §3.1.
- 모델(`FirstMeleeGuardCounterModel`): Guarding →(해제 진입 — E 재입력·홀드 뗌·최대 유지 시간·탭 exitTime 전이, 또는 `Custom1`) **Releasing** →(해제 클립 `End`) Finished. 방어 성공이면 Countering →(반격 클립 `End`) Finished. 단계가 안 맞는 이벤트(반격으로 CrossFade 중 늦게 온 해제 클립 `Custom1`, Guarding 중 `End`)는 무시한다.
- 서버 임시 안전망 = `maxActiveDuration`(SO 4.0 — xlsx 반영은 메인) ÷ min(1, 배율) + padding(`ScalesSafetyNetWithAttackSpeed = true`) — 감속만 늘리고 가속엔 줄이지 않는다.
  방어중 길이는 애니메이터 전이에만 있어 런타임에 따로 뺄 수 없으므로 **전체**를 늘린다(정밀 분할보다 길 뿐 먼저 끊지 않는다). SO 필드는 늘리지 않았다.

### 3.1 이벤트 위치 — 🟨 사용자 튜닝 대상 임시값
| 클립 | 이벤트 | 클립 시각 | 실제 시각(배율 1, Speed 4) |
|---|---|---|---|
| `Garen_E_GuardStart` | `Custom0` 창 열림 | **0.9** (이전 0) | 0.225s — 준비 모션 동안은 비방어 |
| `Garen_E_GuardEnd` | `Custom1` 창 닫힘 | **0.1** (이전 0.933) | 해제 시작 0.025s |
| `Garen_E_GuardEnd` | `End` E 종료(신규) | **0.967** | 0.242s — 해제 모션 끝까지 재생 후 종료 |

- 튜닝은 Animation 창에서 이벤트를 옮기면 된다(코드·SO 변경 없음). EditMode `FirstMeleeGuardAttackSpeedTests` 는 이벤트 위치는 값이 아니라 순서(`Custom1` < `End` < 클립 길이, `Custom0` < 준비 클립 길이)만 지킨다.
  상태 Speed(4)·방어중 실시간(1.5s)·안전망(4.0)은 값으로 검사하고, 안전망 > 준비 + 방어중 + 해제 `Custom1` + 반격 `End` 도 검사한다 — 튜닝하면 테스트 기대값도 같이 고친다.
