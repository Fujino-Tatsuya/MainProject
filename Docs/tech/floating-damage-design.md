# 플로팅 데미지(피해 숫자) — 실제 동작 기준 설계

갱신 2026-10-29 (할 일 154). 코드: `Assets/1.Scripts/UI/Combat/FloatingDamage/`, 수치: `Assets/9.ScriptableObject/UI/FloatingDamageSettings.asset`.

**출처** — 기획 「Re:C 데미지 숫자 표기 v0.2」(§3 구간 · §4 위치 · §5 흐름, 수치는 임시값)
+ 은희 결정(2026-10-01: 누적 숫자는 대상을 따라감 · 누적 구간은 합계로 판정)
+ 할 일 154 결정 1~6(§11). 기획과 다른 곳은 그 줄에 적었다.

---

## 1. 한눈에

| 항목 | 동작 |
|---|---|
| 그리는 곳 | 스포너가 직접 만드는 **Screen Space Overlay** Canvas(`FloatingDamageCanvas`, 1920×1080 기준 · 높이 맞춤 · 정렬 50). Canvas 단위 = 기획 px |
| 글자 | TMP 아님 — **이미지 숫자**(`FloatingDamageDigitSet` 스프라이트, 외곽선 색도 DigitSet). 굵은 변형 스프라이트 없음 → 기획의 "높음 = 한 단계 굵게"는 표현 안 함 |
| 생성점 | 대상의 **몸 중심**(§2) 을 화면에 투영 + 픽셀 오프셋 합산 |
| 흐름 | 단발 = 뜨고 떨어지며 사라짐 / 누적(Multi) = 한 숫자에 합산하다 사라짐(§3) |
| 크기·색 | 피해 강도 3구간(낮음/중간/높음) — 대상 등급별 %(§4) |
| 줌 | 카메라 줌에 비례해 크기·픽셀 오프셋을 같은 배율로(§7, 154) |
| 데이터 | 서버가 피해마다 `ClientDamageDealtClientRpc` 로 실제 감소량 + 공격자 + 공격 종류를 보냄(§6) |
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
x = 투영x + (style.offset.x + scatter.x + shake) × 줌배율
y = 투영y + (spawnOffset + style.offset.y + scatter.y + rise/fall) × 줌배율
→ 화면 가장자리 여백(60, 30) 안으로 클램프(여백은 줌배율 미적용)
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
(`FloatingDamageAccumulationPolicy`). 보호막 숫자와 HP 숫자는 종류가 달라 따로 누적된다.

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

- 크기 배율의 100% = `popupStyles[kind].height`(Damage 36px · ShieldDamage 34px).
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

## 6. 보호막 숫자 · 데이터 경로

- **데이터 = 서버 RPC**(154 결정 2 — 그대로). 서버 `Unit` 이 HP/보호막 실제 감소량을 계산한 뒤 **피해마다**
  `ClientDamageDealtClientRpc(hp, shield, attackerClientId, attackType, hitPattern)` → 각 클라 `Unit.ClientDamagedAttributed`
  → 자동 부착된 `FloatingDamagePresenter`(Unit `OnNetworkSpawn` 이 붙임) → `FloatingDamageSpawner.Submit`.
  값은 체력 클램프 **전**(막타 초과분 포함). 결과 통계(`CombatStatsEvents.ServerDamageApplied`)는 클램프 후라 다르다.
- 한 타격이 보호막과 체력을 함께 깎으면 보호막 숫자 → HP 숫자 순으로 같은 프레임에 둘 뜬다.
- **보호막 숫자(141)**: `PopupKind.ShieldDamage`, 밝은 하늘색 **#7FD8FF**, 높이 34px, **+64px 위**(HP 숫자와 안 겹치게).
- HP 를 거치지 않는 흡수(23호 자폭 실드)는 파생 클래스가 `Unit.ReportAbsorbedShieldDamage` 로 같은 RPC 를 보낸다.
- 훈련 더미는 자체 `TrainingDummyDamagePresenter` 로 같은 스포너에 넣는다.

## 7. 카메라 줌 배율 (154)

**결정 1**: Overlay 는 유지하고 줌에 비례해 숫자를 키우고 줄인다. 평소 게임 카메라 = 1배, 줌인 → 커짐, 줌아웃 → 작아짐, 간파 슬로우 줌인도 따라간다.

- 이 게임의 줌 = Cinemachine `CinemachineFollow.FollowOffset` **거리**를 배율로 곱하는 것(`CameraTargetSwitcher.UpdateSlowMotionZoom` — 간파 슬로우 줌 × 로비 포드 강하 `cinematicDistanceMultiplier`). 원근(FOV 34) 카메라, FOV·orthographicSize 는 안 바꾼다.
- 카메라 코드는 안 고친다. 매 프레임 **투영으로 잰다**(`FloatingDamageZoomScalePolicy.MeasureUnitsPerMeter`):
  카메라 정면이 대상 발 높이 평면과 만나는 점(= 화면 중앙 바닥)에서 카메라 위쪽 1m 를 `WorldToScreenPoint` 두 점으로 재 Canvas 단위로 바꾼다.
  숫자 위치가 아니라 화면 중앙에서 재므로 화면 위·아래 원근 차이·앵커 높이와 무관하게 **줌만** 반영된다.
- 배율 = 현재 ÷ `zoomReferenceUnitsPerMeter`, [`zoomScaleMin`, `zoomScaleMax`] 클램프, 끔·잘못된 값(카메라가 바닥을 안 내려다봄 등)이면 1 (`FloatingDamageZoomScalePolicy.Evaluate`, EditMode 테스트 `FloatingDamageZoomScaleTests`).
- **픽셀 오프셋도 같은 배율**(spawnOffset·scatter·style offset·rise/fall·shake) — 숫자와 몸 사이 거리 비율 유지(사용자 추천안). 화면 가장자리 여백은 그대로.
- 팀원 배율·구간 크기·펀치와 곱해진다.

| 칸 | 기본 | 뜻 |
|---|---|---|
| `zoomScaleEnabled` | 켬 | 끄면 항상 1배(이전 동작) |
| `zoomReferenceUnitsPerMeter` | 89.8 | 배율 1 기준. `PlayerFollowCamera.prefab`(FOV 34 · 오프셋 (7,17,-7) → 거리 19.67m) 실측 89.78. 카메라 프리팹을 바꾸면 같이 맞춘다 |
| `zoomScaleMin` / `zoomScaleMax` | 0.5 / 2 | 배율 클램프 |

예) 간파 성공 줌 0.85 → 1/0.85 ≈ **1.18배**.

## 8. 풀 · 갱신 시점

- `ObjectPool`, 동시 최대 `maxConcurrentPopups` **32** — 넘치면 가장 오래된 숫자부터 즉시 반납.
- 위치·크기·알파 반영은 스포너가 **`Canvas.willRenderCanvases`** 에서 `ApplyLayout` — 카메라가 LateUpdate 에서 움직인 뒤라 한 프레임 밀리지 않는다. 시간 진행은 각 숫자의 `Update`(`Time.deltaTime`).
- 스포너는 씬 상주 1개(`2.Prefabs/Managers/FloatingDamageSpawner.prefab` — MapScene·디버그 씬 배치).

## 9. 코드 지도

| 파일 | 역할 |
|---|---|
| `FloatingDamageSpawner` | Overlay Canvas·풀·누적 키 표·앵커 결정·렌더 직전 배치 |
| `FloatingDamagePopup` | 숫자 1개의 시간 함수(상승·유지·하강·페이드·펀치·흔들림)·줌 배율 적용 |
| `FloatingDamagePresenter` | Unit 이벤트 → 필터 → Submit |
| `FloatingDamageSettings` | 수치 SO + 표시 필터·스타일·구간·모양 |
| `FloatingDamageZoomScale` | 줌 배율 정적 규칙 |
| `FloatingDamageTier` / `FloatingDamageAccumulationPolicy` | 구간 판정·강조 간격 / 누적 키 |
| `FloatingDamageDigitSet` / `FloatingDamageNumberView` | 이미지 글꼴 / 글자 Image |
| `FloatingDamageAnchor` | 대상별 생성점(§2-3) |

## 10. xlsx
`FloatingDamageSettings` 는 `GameData.xlsx` 에 **연결하지 않는다**(시트 없음, 154 결정 7). 튜닝은 에셋 인스펙터에서.

## 11. 할 일 154 결정 기록 (2026-10-29)

1. 줌에 맞춰 숫자 크기 변경 — Overlay 유지, 크기·픽셀 오프셋 같은 배율(§7). **구현**.
2. 데이터는 RPC 방식 그대로(§6).
3. 단발 연타는 타격마다 따로 뜨는 것이 의도(§3).
4. 생성점 = 몸 중심 확정(§2).
5. 슬로우·히트스톱 때 숫자 느려짐 그대로(§3).
6. 굵은 스프라이트 없음 — 높음 구간 "굵게"는 표현하지 않는다(§1).
7. 대상별 위치 오프셋 = 프리팹 루트 `FloatingDamageAnchor`, xlsx 미연결(§2-3·§10). 코드 수정 없음 — 프리팹 부착은 사용자 몫.

## 역사
2026-08-05 초안은 World-space TMP · NetworkVariable 복제 기반 · "체력바 옆 Active 상태머신"을 검토했으나, 구현은
Overlay Canvas + 이미지 숫자 + 서버 RPC + 기획 v0.2 흐름으로 바뀌었다. 옛 비교안은 git 기록에 있다.
