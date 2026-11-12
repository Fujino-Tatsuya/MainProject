# 플로팅 데미지(피해 숫자) — 실제 동작 기준 설계

갱신 2026-11-12 (할 일 268 — 판정 크기 배율·맞은 반대쪽·외곽선 알파·힐 숫자 §13), 2026-11-06 (할 일 242 — 디버그 씬 §12), 2026-11-02 (할 일 174 — 제압 가속 합산 §3.1), 2026-10-29 (할 일 154). 코드: `Assets/1.Scripts/UI/Combat/FloatingDamage/`, 수치: `Assets/9.ScriptableObject/UI/FloatingDamageSettings.asset`.

**출처** — 기획 「Re:C 데미지 숫자 표기 v0.2」(§3 구간 · §4 위치 · §5 흐름, 수치는 임시값)
+ 은희 결정(2026-10-01: 누적 숫자는 대상을 따라감 · 누적 구간은 합계로 판정)
+ 할 일 154 결정 1~6(§11). 기획과 다른 곳은 그 줄에 적었다.

---

## 1. 한눈에

| 항목 | 동작 |
|---|---|
| 그리는 곳 | 스포너가 직접 만드는 **Screen Space Overlay** Canvas(`FloatingDamageCanvas`, 1920×1080 기준 · 높이 맞춤 · 정렬 50). Canvas 단위 = 기획 px |
| 글자 | TMP 아님 — **이미지 숫자**(`FloatingDamageDigitSet` 스프라이트, 외곽선 색도 DigitSet, 외곽선 알파 = DigitSet × `outlineAlpha` 0.8 × 숫자 알파 — §13.3). 굵은 변형 스프라이트 없음 → 기획의 "높음 = 한 단계 굵게"는 표현 안 함 |
| 생성점 | 대상의 **몸 중심**(§2) 을 화면에 투영 + 픽셀 오프셋 합산. 공격자(힐이면 힐한 사람)가 있으면 **화면 x 로 반대쪽**에 밀어 뜨고 그쪽으로 흐른다(§13.2) |
| 흐름 | 단발 = 뜨고 떨어지며 사라짐 / 누적(Multi) = 한 숫자에 합산하다 사라짐(§3) / 제압 가속 중 = 공격자·대상별 하나로 전부 합산(§3.1) |
| 크기·색 | 피해 강도 3구간(낮음/중간/높음) — 대상 등급별 %(§4) × **판정(Hurtbox) 크기 배율** 0.8~1.6(§13.1) |
| 줌 | **카메라 줌과 무관** — 크기·픽셀 오프셋 고정(§7: 넣었다가 뺐음, 154) |
| 해상도 | 높이 맞춤 + 실제 px 하한 16(§4, 154) |
| 간파 중 | 내 강 시도 ~ 성공 섬광 끝 동안 내 화면만 숨김(§5, 154) |
| 데이터 | 서버가 피해마다 `ClientDamageDealtClientRpc` 로 실제 감소량 + 공격자(clientId·몬스터면 NetworkObjectId) + 공격 종류를 보냄(§6). 회복은 `ClientHealedClientRpc`(§13.4) |
| 수치 원본 | SO 에셋. **xlsx(GameData) 미연결** |

## 2. 생성 위치 — 앵커 + 픽셀 오프셋

### 2-1. 앵커(월드 점) 우선순위 — `FloatingDamageSpawner.ResolveAnchor(Unit)`
1. 대상 **Unit 과 같은 GameObject(루트)** 에 `FloatingDamageAnchor` 가 있으면 그 `WorldPosition`
   = (`anchorTransform` 위치, 비었으면 `transform.TransformPoint(localOffset)`) + `worldOffset`.
2. 없으면 루트의 **Collider**(켜져 있을 때) `bounds.center` — 몸 중심.
3. 그것도 없으면 `transform.position + (0, 1, 0)`.

- 단발은 **첫 타격 때 월드 점에 고정**(넉백·이동을 따라가지 않음 — 기획 §4).
- 누적은 매 프레임 앵커를 다시 읽어 **대상을 따라간다**(은희 2026-10-01 — 기획 §4 와 다름). 대상이 꺼지면 마지막 점에 남는다.
- 카메라 뒤(투영 z ≤ 0)이거나 `Camera.main` 이 없으면 투명.

### 2-2. 픽셀 오프셋 합산 (Canvas 단위 = 1080p px, +y = 위)

```
x = 투영x + style.offset.x + scatter.x + shake + side × (sidePush + sideDrift × 흐름)   ← side 는 §13.2
y = 투영y + spawnOffset + style.offset.y + scatter.y + rise/fall
→ 화면 가장자리 여백(60, 30) 안으로 클램프
```

| 칸 | 기본 |
|---|---|
| `spawnOffset` | 20 (몸 중심에서 위로) |
| `spawnScatter` | ±12 / ±6 무작위(생성 때 1회) |
| `popupStyles[kind].offset` | ShieldDamage (0, 64), 나머지 0 |
| 상승/하강 | `riseDistance` 24, `fallDistance` 12 |
| 흔들림 | `shakeDistance` 3, `shakeDuration` 0.08 (좌우 sin 1주기, 높음 구간만) |

### 2-3. 대상별 위치 오프셋 설정법 (154 — 사용자 작업)
코드 수정 없이 **프리팹 루트에 `FloatingDamageAnchor` 를 붙이면 된다.** 지금은 23호(`TwentyThree.prefab`·`TwentyThree_Solo.prefab`) 에만 있다.

- **반드시 Unit 이 붙은 루트에.** `ResolveAnchor` 가 `target.TryGetComponent` 로 루트만 보므로 **자식에 붙이면 무시된다.**
  (2026-10-29 확인: `2.Prefabs` 의 Unit 프리팹 24개 중 캐릭터·몬스터는 전부 루트에 Unit. 예외 1개는 `bossroom.prefab` 의 환경 장치.)
- 붙이면 **몸 중심(콜라이더) 대신 앵커 점을 쓴다** — 오프셋이 아니라 대체다. 그래서 값을 줄 때:
  - 본·소켓을 따라가게: `anchorTransform` 에 본 지정(+ 필요하면 `worldOffset` 으로 m 단위 미세 조정).
  - 고정 높이: `anchorTransform` 비우고 `localOffset` = 루트(발) 기준 위치. 예) 지금 몸 중심이 콜라이더 중심 y≈1 이면 `(0, 1, 0)` 에서 시작해 조정.
- 대상별:
  - **플레이어**: 역할 공통이면 `Player.prefab` 루트, 캐릭터마다 다르면 그 Variant(`Player_Paladin.prefab` 등) 루트에서 추가/덮어쓰기.
    (참고 — 로컬 플레이어 자기 피격은 숫자를 안 띄우므로 팀원 화면에서만 보인다.)
  - **일반 몬스터·중간보스·보스**: 각 몬스터 프리팹 루트.
- 앵커가 없는 프리팹은 위치가 전과 똑같다(콜라이더 중심).
- 떠 있는 누적 숫자는 매 프레임 앵커를 다시 읽어 Play 중 값 수정이 바로 보인다. 씬 뷰에서 선택하면 노란 구로 점이 보인다.

## 3. 단발 / 누적 흐름 (기획 §4·§5)

`AttackInfo` 의 `AttackHitPattern` 으로 갈린다.

| | 단발 `Single` | 누적 `Multi` |
|---|---|---|
| 생성 | 타격마다 **따로** 뜬다(단발 연타도 따로 — 의도, 154 결정 3) | 같은 누적 키의 살아 있는 숫자가 있으면 값만 더함 |
| 상승 | `riseDuration` 0.2s 동안 24px (OutCubic) | 같음 |
| 이후 | 최고점에서 끝까지 12px 완만히 하강(InOutSine), 끝 `fadeDuration` 0.25s 흐려짐 | 마지막 타격 후 `accumulateHold` 0.5s 유지 → 0.25s 동안 하강하며 흐려짐. **흐려지는 중에 맞아도 누적**(유지로 복귀) |
| 표시 시간 | max(구간 `singleDuration`, rise+fade) — 낮음 0.65 / 중간 0.75 / 높음 0.85s | 타격이 이어지는 동안 계속 |
| 값 갱신 연출 | — | 펀치 ×1.05 / 0.06s(OutQuad), 간격 0.1s 안이면 값만 갱신 |

**누적 키** = 공격자 clientId + 공격 종류(`AttackType`) + 대상(InstanceID) + 숫자 종류(`PopupKind`). **Multi 만** 키를 만든다
(`FloatingDamageAccumulationPolicy`). 보호막 숫자와 HP 숫자는 종류가 달라 따로 누적된다. 예외 — 제압 가속 중(§3.1).

### 3.1 제압 가속 합산 (할 일 174)
공격자 플레이어에게 **제압 가속**이 켜져 있으면(`SuppressHastePresence.IsActiveForClient` — 상태이상 복제값이라 전 피어 같다)
그 공격자의 피해는 **단발·Multi·공격 종류 무관** 대상별 숫자 하나로 합친다(`FloatingDamageAccumulation.SuppressHaste`).

| | 동작 |
|---|---|
| 키 | 공격자 + 대상 + 숫자 종류 + 제압 표지(공격 종류 안 봄). 같은 공격자·대상의 Multi 키와 겹치지 않는다 |
| 갱신 | Multi 와 같다 — 값 즉시 갱신 + 펀치(`updatePunchScale`·`updatePunchDuration`·간격 `updatePunchMinInterval`), 합계로 구간 판정 |
| 유지 | 공격자의 가속이 켜진 동안은 타격이 끊겨도 **사라지지 않는다**(`HeldLastHitAt` 이 마지막 타격 시각을 지금으로 붙잡음) |
| 끝 | 가속이 끝난 순간부터 평소처럼 `accumulateHold` 0.5s 유지 → 0.25s 하강·흐려짐 = 마지막 합계를 보여 주고 사라진다 |
| 끝 직후 타격 | 가속이 꺼진 뒤 타격은 평소 규칙(단발/Multi)으로 새 숫자 |

- 보호막 숫자는 HP 숫자와 색·위치가 달라 **따로** 합산한다(숫자 종류는 키에 남김).
- 판정은 스포너 `Submit` 한 곳(훈련 더미 경로도 같이 탄다). 몬스터가 공격자(`ulong.MaxValue`)면 해당 없음.
- 가속 중 쓰러지면 서버가 버프를 빼므로 그 공격자 숫자도 그때부터 사라지기 시작한다.

시간은 `Time.deltaTime` — **슬로우·히트스톱 때 숫자도 느려진다**(그대로 유지 — 154 결정 5).

## 4. 피해 강도 구간 · 색 · 크기 · 강조

피해 비율(%) = 피해 ÷ 대상 최대 체력 × 100. 경계값은 위 구간에 속한다(`FloatingDamageTierPolicy`).
누적 숫자는 **누적 합계로** 판정(은희 2026-10-01 — 기획 §5 "개별 타격 기준"과 다름). 합계는 줄지 않으므로 구간은 오르기만 하고, 오를 때 그 구간 등장 확대를 다시 보여 준다.

| 등급 | 중간 ≥ | 높음 ≥ |
|---|---|---|
| 일반(Normal) | 15% | 50% |
| 중간보스(MidBoss) | 5% | 20% |
| 보스(Boss) | 0.75% | 3% |

| 구간 | HP 숫자 색 | 유지 크기 | 등장 확대 | 흔들림 |
|---|---|---|---|---|
| 낮음 | 노랑 (1, 0.85, 0.2) | ×0.5 | 없음 | 없음 |
| 중간 | 주황 (1, 0.55, 0.1) | ×1.0 | ×1.1 → 0.08s | 없음 |
| 높음 | 빨강 (1, 0.2, 0.15) | ×1.4 | ×1.63 → 0.12s (OutBack) | 3px 0.08s |

- 크기 배율의 100% = `popupStyles[kind].height`(Damage 36px · ShieldDamage 34px). 여기에 판정 크기 배율(§13.1)을 곱한다 — 아래 표는 배율 1(플레이어 크기) 기준.
- **해상도 하한(154)** — Canvas 가 높이 맞춤(1080 기준)이라 숫자 실제 px = 높이 × 유지 크기 × 팀원 배율 × (화면 높이 ÷ 1080).
  이 값이 `minScreenPixelHeight`(기본 **16 실제 px**)보다 작으면 그 높이까지 같은 배율로 키운다(`FloatingDamageMinPixelPolicy`, 등장 확대·펀치 비율은 그대로, 0 = 끔).
  16 근거: 1080p 내 낮음(18px)은 그대로 두고, 그보다 작은 팀원 낮음(13.5px)·720p 낮음(12px)부터 올린다(16px = 흔한 본문 글자 최소 크기).

  | 화면 (화면 높이 ÷ 1080) | 낮음 내/팀원 | 중간 내/팀원 | 높음 내/팀원 |
  |---|---|---|---|
  | 1920×1080 · 2560×1080 (1.0) | 18 / 13.5→**16** | 36 / 27 | 50.4 / 37.8 |
  | 1680×1050 (0.972) | 17.5 / 13.1→**16** | 35 / 26.3 | 49 / 36.8 |
  | 1920×1200 (1.111) | 20 / 15→**16** | 40 / 30 | 56 / 42 |
  | 1280×720 (0.667) | 12→**16** / 9→**16** | 24 / 18 | 33.6 / 25.2 |
  | 960×540 (0.5) | 9→**16** / 6.8→**16** | 18 / 13.5→**16** | 25.2 / 18.9 |
  | 3440×1440 (1.333) | 24 / 18 | 48 / 36 | 67.2 / 50.4 |
  | 3840×2160 (2.0) | 36 / 27 | 72 / 54 | 100.8 / 75.6 |

  가로 길이(울트라와이드·16:10)는 크기에 영향 없다 — 높이 맞춤이 원근 카메라(세로 FOV)의 월드 크기와 같이 늘고 줄어서다. 다른 HUD 처럼 가로·반반 맞춤으로 바꾸지 않는다.
  **MPPM 가상 플레이어·작은 Game 뷰**: Game 뷰가 고정 1920×1080 이면 렌더는 1080 이고 창에 축소해 보여 준다(가상 플레이어 레이아웃 실측 0.39·0.25배) —
  화면 높이가 1080 이라 하한이 안 걸리고 모든 UI 가 같이 작아진다. 크기 확인은 창을 키우거나 Free Aspect 로.
- **높음 강조 간격** `highEmphasisInterval` 0.3s — 같은 공격자·같은 대상 기준. 간격 안이면 크기·값만 반영하고 등장 확대·흔들림은 생략(`FloatingDamageEmphasisGate`).
- HP 피해(`PopupKind.Damage`)만 구간 색, 다른 종류는 `popupStyles` 색.

## 5. 표시 필터 · 자기 피격 · 팀원 스타일

| 값 | 동작 |
|---|---|
| `AllDamage` | 전부 같은 모양 |
| `OwnDealtOnly` | 내가 준 피해만 |
| `AllWithOwnEmphasis` (**에셋 값**) | 전부 표시, 팀원·몬스터 쪽 공격은 크기 ×0.75 · 최대 알파 200/255 |
| `Off` | 숨김(설정창 "끄기") |

- 실제 값 = **설정창 저장값(PlayerPrefs `Settings.Gameplay.DamageNumbers`) 우선**, 없으면 에셋 값(`GameplayConfig.DamageNumbers`). 에셋은 고치지 않는다.
- enum 은 SO·PlayerPrefs 에 정수로 저장 — **끝에만 추가**.
- **로컬 플레이어가 받은 피해는 띄우지 않는다**(Presenter·Spawner 양쪽에서 거름). 자기 피격은 HUD·HitFlash 담당.
- 공격자가 플레이어가 아닌 피해도 "로컬 공격 아님" → 팀원 스타일.
- **간파 중 숨김(154 → 179 후속 2·4)** — 간파 연출 디렉터 A5 `hideCombatUi`(옛 이름 `hideDamageNumbers`, 기본 켬)가 켜져 있으면, **내 강 시도 암전 시작 ~ 성공 섬광 ⑥ 가라앉음 끝**
  (실패·판정 전 종료·예측 빗나감이면 오너 암전 걷기 끝) `FloatingDamageCanvas` 의 Canvas 를 끈다(`enabled = false`, 보스 숫자 포함 전부).
  (11-04 할 일 211: 제압 간파 섬광 시퀀스 동안 숨김은 시퀀스가 Legacy 가 되며 삭제 — 제압 간파 = V3.) 숫자 생성·수명·누적은 그대로 돌아 끝나면 살아 있는 숫자가 보인다. 약 시도·대상 없음·팀원 화면의 일반 간파는 숨기지 않는다.
  스포너 `LateUpdate` 가 `InterruptProductionDirector.CombatUiHidden` 을 읽는다(디렉터는 스포너를 모름). 규칙 = `InterruptProductionRules.ShouldHideCombatUi`
  — 같은 값으로 머리 위 체력바(플레이어·몬스터·보스 — 179 후속 4, 화면 고정 보스 HP 바는 그대로)·오너 조준 표시도 숨는다. [interrupt-presentation.md §6.4](interrupt-presentation.md#64-스위치프로필-위치) A5.

## 6. 보호막 숫자 · 데이터 경로

- **데이터 = 서버 RPC**(154 결정 2 — 그대로). 서버 `Unit` 이 HP/보호막 실제 감소량을 계산한 뒤 **피해마다**
  `ClientDamageDealtClientRpc(hp, shield, attackerClientId, attackType, hitPattern)` → 각 클라 `Unit.ClientDamagedAttributed`
  → 자동 부착된 `FloatingDamagePresenter`(Unit `OnNetworkSpawn` 이 붙임) → `FloatingDamageSpawner.Submit`.
  값은 체력 클램프 **전**(막타 초과분 포함). 결과 통계(`CombatStatsEvents.ServerDamageApplied`)는 클램프 후라 다르다.
- 한 타격이 보호막과 체력을 함께 깎으면 보호막 숫자 → HP 숫자 순으로 같은 프레임에 둘 뜬다.
- **보호막 숫자(141)**: `PopupKind.ShieldDamage`, 밝은 하늘색 **#7FD8FF**, 높이 34px, **+64px 위**(HP 숫자와 안 겹치게).
- HP 를 거치지 않는 흡수(23호 자폭 실드)는 파생 클래스가 `Unit.ReportAbsorbedShieldDamage` 로 같은 RPC 를 보낸다.
- 훈련 더미는 자체 `TrainingDummyDamagePresenter` 로 같은 스포너에 넣는다.
- RPC 인자(268): `ClientDamageDealtClientRpc(hp, shield, attackerClientId, attackerObjectId, attackType, hitPattern)` — `attackerObjectId` = 플레이어가 아닌 공격자 Unit 의 NetworkObjectId(반대쪽 연출 기준점, §13.2). 회복 RPC 는 §13.4. RPC 목록 = [networking.md](networking.md) '표시용 전투 알림 RPC'.

## 7. 카메라 줌 배율 — 넣었다가 뺐음 (154)

숫자 크기·픽셀 오프셋은 **카메라 줌과 무관**하다(Overlay Canvas 단위 고정 — 간파 슬로우 줌인에도 그대로).
92376865 에서 줌 비례 배율(`FloatingDamageZoomScalePolicy`, Settings `zoomScale*` 칸)을 넣었으나 사용자 결정으로 **필요 없어 뺐다**.
다시 필요하면 그 커밋을 참고한다.

## 8. 풀 · 갱신 시점

- `ObjectPool`, 동시 최대 `maxConcurrentPopups` **32** — 넘치면 가장 오래된 숫자부터 즉시 반납.
- 위치·크기·알파 반영은 스포너가 **`Canvas.willRenderCanvases`** 에서 `ApplyLayout` — 카메라가 LateUpdate 에서 움직인 뒤라 한 프레임 밀리지 않는다. 시간 진행은 각 숫자의 `Update`(`Time.deltaTime`).
- 스포너는 씬 상주 1개(`2.Prefabs/Managers/FloatingDamageSpawner.prefab` — MapScene·디버그 씬 배치 · 숫자 확인 씬 `DebugFloatingDamage`(§12)).

## 9. 코드 지도

| 파일 | 역할 |
|---|---|
| `FloatingDamageSpawner` | Overlay Canvas·풀·누적 키 표·앵커 결정·렌더 직전 배치 |
| `FloatingDamagePopup` | 숫자 1개의 시간 함수(상승·유지·하강·페이드·펀치·흔들림) |
| `FloatingDamagePresenter` | Unit 이벤트 → 필터 → Submit |
| `FloatingDamageSettings` | 수치 SO + 표시 필터·스타일·구간·모양 · 해상도 하한 규칙 `FloatingDamageMinPixelPolicy` |
| `FloatingDamageTier` / `FloatingDamageAccumulationPolicy` | 구간 판정·강조 간격 / 누적 방식(`FloatingDamageAccumulation`)·누적 키·제압 가속 유지 |
| `FloatingDamageDigitSet` / `FloatingDamageNumberView` | 이미지 글꼴 / 글자 Image |
| `FloatingDamageAnchor` | 대상별 생성점(§2-3) |
| `FloatingDamageHurtboxScale` | 판정 크기 측정 `FloatingDamageHurtboxMeasure` · 배율 규칙 `FloatingDamageHurtboxScalePolicy`(§13.1) |
| `FloatingDamageSource` | 반대쪽 기준점 — clientId → 플레이어 / NetworkObjectId → 몬스터 위치(§13.2). 규칙 `FloatingDamageSidePolicy`·외곽선 `FloatingDamageOutlinePolicy` 는 `FloatingDamageSettings.cs` |

## 10. xlsx
`FloatingDamageSettings` 는 `GameData.xlsx` 에 **연결하지 않는다**(시트 없음, 154 결정 7). 튜닝은 에셋 인스펙터에서.

## 11. 할 일 154 결정 기록 (2026-10-29)

1. 줌에 맞춰 숫자 크기 변경 — 92376865 에 구현했다가 사용자 결정으로 **뺐다**(필요 없음, §7).
2. 데이터는 RPC 방식 그대로(§6).
3. 단발 연타는 타격마다 따로 뜨는 것이 의도(§3).
4. 생성점 = 몸 중심 확정(§2).
5. 슬로우·히트스톱 때 숫자 느려짐 그대로(§3).
6. 굵은 스프라이트 없음 — 높음 구간 "굵게"는 표현하지 않는다(§1).
7. 대상별 위치 오프셋 = 프리팹 루트 `FloatingDamageAnchor`, xlsx 미연결(§2-3·§10). 코드 수정 없음 — 프리팹 부착은 사용자 몫.
8. 후속 — 간파 중 숨김(§5, 디렉터 A5) · 해상도 하한 `minScreenPixelHeight` 16(§4).

## 12. 디버그 씬 — `Assets/0.Scenes/Debug/DebugFloatingDamage.unity` (할 일 242, 2026-11-06)

숫자 크기·보스 위치를 **보면서** 고치는 씬. 씬을 열고 그냥 ▶(네트워크 없음 — 호스트·Dev Boot 불필요). 🔴 빌드 목록(EditorBuildSettings)에 넣지 않는다.

- **배치**: 실제 프리팹 인스턴스 — 보스 `TwentyThree_Raid`(웰즈 포함, 가운데) · 비교용 `Player_Paladin`(왼쪽) · 일반 몬스터 `HumanoidBot`(오른쪽), 바닥 평면, `FloatingDamageSpawner.prefab`.
  ▶ 하면 구동기 `DebugFloatingDamageLab`(`Assets/1.Scripts/Dev/FloatingDamageLab/`)이 대상 아래 스크립트를 전부 끈다(애니메이터·조명·앵커만 남김, 물리 정지) → AI·공격 없음.
- **카메라** = 실제 플레이 카메라 값(`PlayerFollowCamera` FollowOffset (7,17,-7) · 각도 (59.787, -45, 0) · FOV 34). 기본은 가운데(보스) 기준 전체 보기, `F` = 선택 대상 중심(플레이어 시점처럼).
- **띄우기** — 화면 왼쪽 패널(F1 숨김) 또는 단축키. 경로는 게임과 같은 `FloatingDamageSpawner.Submit`.

  | 키 | 동작 |
  |---|---|
  | `1`~`9` | 대상 선택(23호·웰즈·팔라딘·휴머노이드 순) |
  | `Space` | 띄우기 |
  | `R` | 자동 반복 켜기/끄기(구동기 `autoInterval` 0.6초) |
  | `M` | 단발 ↔ 누적(×`multiHits` 6번, 0.1초 간격 — 합계가 고른 구간이 되게 나눔) |
  | `Z` `X` `C` | 구간 낮음·중간·높음(패널에서 '직접' = 값 입력) |
  | `H` `S` `B` | HP · 보호막 · 둘 다(보호막 → HP 순, 게임과 같음). 회복·상태·텍스트는 패널 버튼 |
  | `T` | 내 공격 ↔ 팀원 공격(표시 필터 '내 데미지 강조'면 팀원 = ×0.75·흐리게) |
  | `A` | 공격자 자리 없음 → 왼쪽 → 오른쪽 → 정면(268 — 숫자는 반대쪽, 없음·정면 = 위로만). 자리 = 대상 생성점에서 카메라 좌우(바닥 평면) `sourceDistance` 3m, 정면 = 카메라 쪽. 기즈모 빨간 구 = 마지막 공격자 자리 |
  | `F` | 카메라 = 선택 대상 중심 |
- **구간 기준 최대 체력**: 네트워크 없이는 Unit 최대 체력이 0 이라 구동기 칸 `groups[i].baseMaxHp` 를 쓴다(요청 `baseMaxHpOverride` — 게임 경로는 안 씀). 기본 = 데이터 값 23호 레이드 2880(1인) · 팔라딘 220 · 휴머노이드 90.
- **숫자 크기 고치기** = 설정 SO `Assets/9.ScriptableObject/UI/FloatingDamageSettings.asset` — `popupStyles[kind].height`(HP 36·보호막 34)·`tierLooks[*].holdScale`·`spawnOffset` 등.
  패널의 `-4 -1 +1 +4` 버튼이 HP·보호막 높이를 SO 에 바로 쓰고 저장한다(Play 중이어도 남음). 인스펙터에서 고쳐도 같다 — **다음 숫자부터 반영**. 이 SO 는 **xlsx 미연결**(§10) — 확정 값은 에셋 커밋만.
  **어느 칸이 크기를 바꾸나** (숫자마다 생성 때 읽음, 캐시 없음): 화면 높이(px) = `popupStyles[kind].height` × `tierLooks[구간].holdScale` × 팀원 배율(`teammateScaleMultiplier`, 내 공격이면 1) × (화면 높이 ÷ 1080),
  그 값이 `minScreenPixelHeight`(16) 보다 작으면 16 으로 올림. `tierLooks` 는 **그 숫자가 들어간 구간 줄만** 쓰인다 — 패널 기본 구간은 **중간**(X)이라 `[0]` 낮음 줄을 고치면 안 보인다(Z 로 바꿀 것).
  `spawnScale`(등장 확대)·`shake`(흔들림)는 그 줄의 `spawnSettleDuration > 0` **이고** `spawnScale > holdScale` 일 때만 돈다(`HasSpawnPop`) — 아니면 둘 다 무시.
  패널이 방금 숫자의 구간(▶)·읽은 높이·유지 크기·화면 px·하한 걸림과, 등장 확대가 꺼진 줄을 보여 준다.
- **보스 위치 고치기** = 대상 프리팹 루트 `FloatingDamageAnchor`(23호 = `TwentyThree.prefab` 루트, 본 `anchorTransform` + `worldOffset`). 패널 `Y ±0.1/±0.5` 또는 인스펙터에서 Play 중 조정 → 떠 있는 누적 숫자도 바로 따라온다.
  Play 중 값은 끝나면 날아가므로 **'현재 앵커 값을 프리팹에 저장'** 버튼 — 컴포넌트가 정의된 원본 프리팹(`TwentyThree.prefab`, 레이드 변형이 물려받음)에 `localOffset`·`worldOffset` 을 쓴다(본 지정은 그대로). 중간 변형이 같은 칸을 덮어쓰면 패널·콘솔에 경고.
- **기즈모**: 노란 구 = 앵커 점, 초록 구 = 앵커 없음(콜라이더 중심), 선 = 대상 발 → 생성점. 에디트 모드 씬 뷰에서도 보이고, Game 뷰는 Gizmos 켬일 때.
- 🔴 Dev Boot(호스트)로 열지 말 것 — 씬에 놓인 `Player_Paladin` 이 호스트 소유로 스폰되면 "내 플레이어"로 걸러져 숫자가 안 뜰 수 있고, 호스트 플레이어·카메라가 따로 생긴다(11-06 로그 — 오디오 리스너 3개). 씬을 열고 그냥 ▶.
- 알려진 한계: 꺼진 스크립트도 `Awake` 는 돈다(네트워크 없는 경고가 콘솔에 날 수 있음 — 숫자와 무관) · 플레이어·몬스터에는 앵커가 없어 콜라이더 중심 · 앵커 '저장'은 에디터에서만.
- 패널(268): 방금 숫자의 판정 크기(m)·배율·반대쪽 방향, 설정 칸 외곽선 알파·반대쪽 밀기/흐름/문턱을 보여 준다. 회복 숫자는 '회복' 버튼(흐름 무관 늘 합산).
- 테스트: `Assets/Tests/EditMode/FloatingDamage/Editor/FloatingDamageDebugLabTests.cs`(메뉴 `Tools/Tests/데미지 숫자 EditMode 테스트 실행`).

## 13. 판정 크기 배율 · 맞은 반대쪽 · 외곽선 알파 · 힐 숫자 (할 일 268, 2026-11-12)

계획서 [PLAN-floating-damage-hurtbox-side-heal.md](../history/PLANS/PLAN-floating-damage-hurtbox-side-heal.md). 수치 칸은 전부 `FloatingDamageSettings`(xlsx 미연결, §10).

### 13.1 판정(Hurtbox) 크기 배율
- 배율 = clamp(sqrt(대상 판정 크기 ÷ `hurtboxReferenceSize`), `hurtboxScaleMin` 0.8, `hurtboxScaleMax` 1.6). 구간 크기 × 팀원 배율 × **이 배율** → 그다음 해상도 하한(§4). 카메라 줌과 무관.
- **판정 크기** = 대상이 주인(`Hurtbox.OwnerUnit`)인 Hurtbox 의 **켜진 콜라이더** 월드 경계 상자를 합친 것의 **대각선 길이(m)** — 높이와 수평 폭을 함께 본다.
  자식 Unit(23호 안의 웰즈)의 Hurtbox 는 뺀다. Hurtbox 가 없거나 전부 꺼져 있으면 루트 콜라이더, 그것도 없으면 배율 1.
- 기준 `hurtboxReferenceSize` **1.7** = `Player.prefab` 캡슐(반경 0.2·높이 1.6 → 0.4×1.6×0.4 대각선 1.697). 0 = 끔.
- 프리팹 값 계산: 플레이어 1.0 · 일반 몬스터(반경 0.5·높이 2, 대각선 2.45) ≈ 1.2 · 23호(반경 1.7·높이 5.78, 7.52) = 1.6(최대에 걸림).
- 스포너가 대상별 **마지막 측정값을 기억**해 판정이 잠깐 꺼진 순간(무적 등)에도 크기가 튀지 않는다. 숫자 생성 때 1회 — 누적 숫자는 첫 값 유지.

### 13.2 맞은 반대쪽에 뜨기
- 생성 순간 대상 생성점과 **공격자 본인 위치**(투사체도 공격자 본인)를 화면에 투영해 x 를 비교(Canvas 단위 = 1080p px) — 공격자가 왼쪽이면 숫자는 오른쪽.
  차이가 `sideFrontThreshold` **24px** 이하(거의 정면)거나 공격자가 없으면 지금처럼 위로만.
- 위치: 생성 때 `sidePush` **40px** 밀고, `sideDriftDuration` **0.6초** 동안 `sideDriftEase`(OutCubic)로 `sideDrift` **30px** 더 흐른 뒤 머문다. 상승·하강(§3)은 그대로 같이 돈다.
- **누적 숫자는 처음 정한 방향을 유지**(방향은 생성 때 1회).
- 공격자 위치 찾기(`FloatingDamageSource`, 각 피어) — 새 RPC 없음, 기존 피해 RPC 인자만 1개 추가:
  - 플레이어 공격자 = RPC 의 `attackerClientId` → `PlayerLifeCycleController.Active` 에서 그 OwnerClientId 의 플레이어.
  - 몬스터·보스 공격자 = RPC 의 `attackerObjectId`(서버가 `hitContext.sourceUnit` → 없으면 `sourceTransform` 부모 Unit, 플레이어 아님) → `SpawnManager.SpawnedObjects`. **이미 디스폰됐으면 위로만.**
  - 장판·도트·추락·비율 피해(`ReceiveAttack` 을 안 타는 경로)는 둘 다 없음 → 위로만.
- 힐은 기준 = 힐한 사람(§13.4), 자기 회복은 위로만.

### 13.3 외곽선 알파
- 외곽선 알파 = DigitSet `outlineColor.a` × `outlineAlpha`(**0.8**) × 숫자 알파(팀원 최대 알파 200/255·페이드 포함) — `FloatingDamageOutlinePolicy.Alpha`.

### 13.4 힐 숫자 — 새 RPC `Unit.ClientHealedClientRpc(int healed, ulong healerClientId)`
- 서버 `Unit.HealHp(amount, countInStats, healerClientId)` 의 **실제 회복량 &gt; 0** 이고 `countInStats` 가 true 일 때만 전 피어에 보낸다 → 이벤트 `Unit.ClientHealedAttributed(HealReceivedInfo)` → `FloatingDamagePresenter` 가 `PopupKind.Heal`(초록 `popupStyles` 색 (0.28, 1, 0.38))로 띄운다. 회복량 0(풀피) = 안 보냄.
- **대상 회복**: 회복 스킬·특성 회복(`PlayerTraitEffects.HealServer`)·초과 회복 아군 전달(회복 주체 = 시전자)·소생.
  소생 = `PlayerLifeCycleController.TryCompleteRevive` 가 `Unit.Revive(reportHeal: true)` — 회복량 = 최대 체력 − 직전 체력, 회복 주체 = 자기 자신(위로만).
- **제외**: 플레이어 기본 체력 재생·로비 훈련 보정(`countInStats: false`) · 재합류 체력 복원(`SetCurrentHealthOnServer`) · 전멸 리스폰·몬스터 재사용 등 `Revive()`(= `reportHeal` false) · 훈련 더미 리필(`TrainingDummy.ReportsHealNumbers` false).
- 표시 필터는 피해와 같다(내 힐 = 크게, 팀원끼리 = ×0.75·흐리게, 끄기·내 것만 동일). 로컬 플레이어가 받은 회복은 띄우지 않는다(피해와 같음 — 팀원 화면에서만 보인다).
- 누적: **늘 힐한 사람·대상별 합산**(Multi 키 = 힐한 사람 + `AttackType.Default` + 대상 + Heal) — 피해 숫자와 같은 값 갱신·펀치·유지·흐려짐. 제압 가속 합산(§3.1)은 타지 않는다(`FloatingDamageAccumulationPolicy.Resolve(kind, …)`).
  구간(크기)은 피해처럼 합계 ÷ 최대 체력으로 판정하고, 색은 늘 Heal 스타일 색. 판정 크기 배율·반대쪽도 그대로 적용.

### 13.5 알려진 한계
- 보스 숫자가 1.6배가 되면 보호막·HP 숫자 세로 간격(`popupStyles[ShieldDamage].offset` 64px 고정)이 상대적으로 좁아진다. 기존 EditMode `ShieldAndHpNumbers_DoNotOverlapVertically` 는 268 전부터 실패 중 — 에셋 튜닝값(가장 큰 유지 크기·`spawnScatter.y`) 기준 필요 간격 65.8px > 64px. 268 범위에서는 안 고친다.
- 반대쪽 방향은 생성 순간 위치로 한 번 정한다 — 누적 중 공격자가 반대편으로 돌아가도 방향은 그대로(사양).
- 몬스터 투사체가 공격자 Unit 을 `sourceUnit`·부모로 안 들고 오면 위로만 뜬다.
- 소생의 회복 주체는 따로 기록되지 않아 자기 회복(위로만)으로 본다.

## 역사
2026-08-05 초안은 World-space TMP · NetworkVariable 복제 기반 · "체력바 옆 Active 상태머신"을 검토했으나, 구현은
Overlay Canvas + 이미지 숫자 + 서버 RPC + 기획 v0.2 흐름으로 바뀌었다. 옛 비교안은 git 기록에 있다.
