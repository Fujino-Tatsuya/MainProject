# CURRENT PLAN — 카메라 쉐이크 + HP 비네트 (2026-08-06)

> 상태: **승인 대기**. 브랜치 `feature/CameraFeedback` (base `development`), 레인 `MainProject`.
> 구현 위임: Codex(`.cs`만) / 프리팹 부착: 은희 또는 Claude(Codex 종료 후).
> grill 완료 — 아래는 확정된 결정만 담는다.

## 목표

전투 타격감을 **로컬 표현**으로 올린다.

1. **카메라 쉐이크** — 내가 맞을 때(강) / 내가 때렸을 때(약·짧게).
2. **HP 비네트** — `현재HP / 최대HP`가 낮아질수록 화면 테두리가 진해지고, 회복되면 연해진다.
   피격 순간의 번쩍임이 아니라 **HP 수위를 상시 반영하는 연속 표현**이다.

## 스코프

- **In**: 로컬 플레이어 기준 쉐이크 2종 + HP 비율 비네트.
- **Out**: 카메라 플레어(원 요청 3번) — 렌즈 플레어인지 화면 플래시인지 미확정이라 분리.
- **Out**: 보스 착지·광범위 기믹 연출 지진 — `BossEncounterDirector`를 건드려야 해서 별건.
- **Out**: 피격 순간 비네트 펄스 — HP 연속 표현으로 요구가 충족된다. 나중에 얹을 수 있게 진폭 소스를 하나로 둔다.

## 🔴 불변식 (이걸 어기면 3인 플레이가 망가진다)

**세 효과 전부 로컬 전용이다.** 서버 RPC로 브로드캐스트하면 한 명이 맞을 때 3명 화면이 다 흔들린다.
피해 이벤트(`ClientDamagedAmount` 등)는 **모든 피어에서 발동**하므로 "로컬 플레이어인가" 게이트가
반드시 있어야 한다. 판정 기준은 기존 코드와 동일하게 쓴다:

- 내가 맞았나 = `unit is Player p && (p == Player.LocalPlayer || p.IsOwner)`
- 내가 때렸나 = `attackerClientId == NetworkManager.Singleton.LocalClientId`

두 판정 모두 [FloatingDamagePresenter.cs:73-82](Assets/1.Scripts/UI/Combat/FloatingDamage/FloatingDamagePresenter.cs:73)에
이미 있다. **똑같이 쓸 것**(새로 만들지 말 것).

## 현재 이해 (조사 완료 — 전제로 삼아도 된다)

| 사실 | 근거 |
|---|---|
| 필요한 이벤트가 이미 다 있다. 새 이벤트·새 RPC **불필요** | `Unit.cs`: `ClientHpChanged(prev,next)` / `ClientDamagedAmount(amount,channel)` / `ClientDamagedAttributed(amount,channel,attackerClientId)` |
| **`Unit.OnNetworkSpawn`이 모든 Unit에 컴포넌트를 자동 부착하는 관례가 있다** | [Unit.cs:505-509](Assets/1.Scripts/Unit/Unit.cs:505) — `HitFlash`, `FloatingDamagePresenter`. 덕분에 적 프리팹 작업이 0이다 |
| 카메라 리그는 씬에 없다. **런타임 생성**이다 | `CameraTargetSwitcher`가 `MainCamera.prefab` + `PlayerFollowCamera.prefab`을 인스턴스화. MapScene에 Cinemachine 참조 0건 |
| Cinemachine **3.1.6** (`CinemachineCamera`/`CinemachineBrain`/`CinemachineFollow`) | `CameraTargetSwitcher.cs` |
| 추락·관전 카메라 전환이 있다 | `CameraTargetSwitcher.IsInFallView` / `IsSpectatorMode` — vcam 2개를 우선순위로 스왑 |
| MapScene에 `Global Volume` + 프로필 에셋 존재 | `4.MapScene.unity:4002`, 프로필 GUID `746a3f69856cf614d8e782652e51b262` |

## 접근

### A. 부착 지점 — `MainCamera.prefab`에 컴포넌트 1개

신규 `CameraFeedback`을 **`MainCamera.prefab`(= Brain을 든 렌더 카메라)에 부착**한다. 이유:

- 리그가 런타임 생성이라 씬 배선으로는 못 잡는다. 프리팹에 있으면 리그와 함께 생성된다.
- 튜닝값을 `SerializeField`로 프리팹에서 조절할 수 있다(런타임 `AddComponent`면 불가능).
- ⚠️ **Impulse 리스너는 vcam이 아니라 Brain 쪽에 둔다.** vcam에 붙이면 추락·관전 전환 때 끊긴다.

`Volume`과 `CinemachineIndependentImpulseListener`는 **이 컴포넌트가 런타임에 `AddComponent`로 만든다**
→ 프리팹 작업은 "컴포넌트 1개 부착"뿐이고 참조 배선이 없다.

### B. 비네트 — 런타임 생성 Volume (공유 에셋 절대 건드리지 않음)

🔴 **씬의 `Global Volume` 프로필을 런타임에 수정하면 안 된다.** `sharedProfile`은 에셋이라
값이 에디터에서 디스크에 남고, 모든 피어·모든 세션에 새어나간다.

대신 `CameraFeedback`이 자기 GameObject에:

1. `Volume` 추가 — `isGlobal = true`, `priority`를 Global Volume보다 높게(예: 100)
2. `VolumeProfile`을 **런타임 인스턴스로** 생성(`ScriptableObject.CreateInstance<VolumeProfile>()`)
3. 그 프로필에 `Vignette` 오버라이드만 추가하고 `intensity`를 매 프레임 갱신
4. `OnDestroy`에서 프로필 인스턴스 `Destroy` (누수 방지)

```
매 프레임:
  Player p = Player.LocalPlayer
  if (p == null || p.FinalMaxHp <= 0) → 목표 강도 0
  else ratio = clamp01(p.CurrentHealth / (float)p.FinalMaxHp)
       목표 강도 = Lerp(intensityAtFullHp, intensityAtZeroHp, 1 - ratio)
  현재 강도 = MoveTowards(현재, 목표, smoothingPerSecond * dt)   // 고정 속도
  vignette.intensity.value = 현재 강도
```

- **고정 속도로 따라가는 이유**: `Lerp` 감쇠는 끝이 안 닿아 미세하게 남고 프레임레이트에 의존한다
  (지연 체력바에서 같은 결론을 냈다 — `DelayedHealthBar` 참조).
- HP를 **이벤트가 아니라 폴링**으로 읽는다. 목표가 "수위 반영"이라 델타가 필요 없고,
  늦은 바인딩·부활·최대HP 변동이 전부 자동으로 맞는다.
- Soul/사망 표현은 건드리지 않는다 — `PlayerCombatUiLifecyclePolicy`가 HUD를 따로 처리한다.

### C. 쉐이크 — Cinemachine Impulse, 방향은 무작위

`CameraFeedback`에 공개 메서드 2개를 두고 per-Unit 리포터가 호출한다.

| 계기 | 세기 | 지속 |
|---|---|---|
| 내가 맞음 | 강 | 길게 |
| 내가 때림 | 약 | 짧게 |

- 두 값 전부 `SerializeField`(진폭·지속). 기본값은 Codex 재량, 튜닝은 플레이 후 프리팹에서.
- 피해량에 비례시키지 **않는다** — v1은 고정 2단계. (비례는 튜닝 축이 늘어나 나중에.)
- 연타 시 임펄스가 누적돼 화면이 멀미나면 안 된다 → **최소 재발동 간격**(`SerializeField`, 예 0.05s)을
  계기별로 따로 둔다.

### D. 피해 계기 수집 — `Unit` 자동 부착 리포터 1개

신규 `UnitCameraFeedbackReporter`를 `FloatingDamagePresenter`와 **똑같은 방식**으로 만든다:

- `Unit.OnNetworkSpawn`에 2줄 추가해 자동 부착([Unit.cs:508](Assets/1.Scripts/Unit/Unit.cs:508) 바로 아래).
  **적/보스/플레이어 프리팹 수정 0.**
- `OnEnable`에서 자기 `Unit`의 `ClientDamagedAmount` + `ClientDamagedAttributed` 구독, `OnDisable`에서 해제.
- 분기:
  - 이 Unit이 **로컬 플레이어**이고 피해가 들어왔다 → `CameraFeedback.Instance?.ReportLocalPlayerHit()`
  - 이 Unit이 로컬 플레이어가 **아니고** `attackerClientId == LocalClientId` → `ReportLocalPlayerDealtDamage()`
- `CameraFeedback.Instance`는 `FloatingDamageSpawner.Instance`와 같은 형태의 정적 접근자.
  **없으면 조용히 아무것도 안 한다**(카메라 리그 없는 테스트 씬에서 예외 금지).

⚠️ **중복 발동 주의**: `ClientDamagedAmount`와 `ClientDamagedAttributed`가 같은 피해에 둘 다 나온다.
맞음 쉐이크는 한쪽만 구독하거나 같은 프레임 중복을 눌러야 한다 — 아니면 진폭이 2배가 된다.

## 변경 파일 (정확히 4개)

| 파일 | 변경 |
|---|---|
| `Assets/1.Scripts/Camera/Feedback/CameraFeedback.cs` | **신규** — Impulse 소스/리스너 + 런타임 Volume·Vignette + 정적 `Instance` |
| `Assets/1.Scripts/Camera/Feedback/UnitCameraFeedbackReporter.cs` | **신규** — Unit 피해 이벤트 → 로컬 판정 → `CameraFeedback` 호출 |
| `Assets/1.Scripts/Unit/Unit.cs` | 자동 부착 2줄 (`FloatingDamagePresenter` 블록 바로 아래) |
| `Assets/1.Scripts/Camera/CameraTargetSwitcher.cs` | **필요할 때만** — Brain 카메라를 리포터가 못 찾으면 접근자 1개 추가. 불필요하면 손대지 말 것 |

## 완료 조건

1. 변경 파일 최대 4개. **프리팹·씬·`.meta`·SO 무수정** (부착은 사용자 몫)
2. 신규 `.cs`는 **UTF-8(BOM)** — `Docs/tech/conventions.md` 규칙
3. 씬의 `Global Volume` 프로필 에셋이 런타임에 **수정되지 않음**(별도 Volume + 인스턴스 프로필)
4. 카메라 리그·`CameraFeedback`이 없는 씬에서 예외 0건 (전부 null 안전)
5. 3인 기준: 한 명이 맞을 때 **그 사람 화면만** 흔들린다
6. C# 컴파일 0 에러 / 0 경고
7. 단일 커밋 + `work_completed`에 커밋 해시 + 프리팹 부착 안내

## 검증 계획

- (사용자) `MainCamera.prefab`에 `CameraFeedback` 부착 → MapScene Play
- 솔로: 맞으면 강하게 흔들림 / 때리면 약하게 / HP 낮추면 비네트 진해지고 회복하면 연해짐
- **MPPM 2~3인: 한 명만 맞았을 때 다른 화면이 흔들리지 않는지** (불변식 검증 — 이게 핵심)
- 추락·관전 카메라 전환 중에도 쉐이크가 살아있는지

## 리스크

- Codex가 컴파일 검증을 못 한다(Unity 미실행) → 에러 시 Claude가 수정.
- Cinemachine 3.x Impulse API 이름이 2.x와 다르다(`CinemachineImpulseSource`,
  `CinemachineIndependentImpulseListener`). 3.1.6 기준으로 확인하며 쓸 것.
- `Unit.cs`는 코어(은희 담당)지만 자동 부착 2줄이라 기존 경로 무영향.
- 비네트 강도 기본값이 과하면 저HP에서 시야를 가린다 → 기본값은 보수적으로.
