# 간파 성공 연출 — 슬로우·히트스톱·암순응·성공 이펙트 칸·오너 줌

> 🔹 할 일 104: 슬로우를 뺀 연출은 지금 **전부 꺼져 있다** — 칸마다 켜는 스위치는 [§6](#6-연출-스위치--interruptpresentationtoggles-할-일-104).
>
> 할 일 87(C1~C5)의 단일 사실 원본. 슬로우 모션 레이어 자체(시간 도메인·서버 권한·last-wins·컷신 리셋)는
> [PLAN-interrupt-slowmo.md](../history/PLANS/PLAN-interrupt-slowmo.md) 가 원본이고, 여기서는 **간파 성공에 얹은 연출**만 다룬다.
> 수치 원본은 `GameData.xlsx` `SlowMotion` 시트([data-table.md](data-table.md)).

## 1. 흐름

```
간파 입력(오너) ─▶ 서버 PlayerInterruptSkillBase
  ① 예측 적중이면 시도 슬로우   GlobalTimeScale.ServerStart(interruptProfile, 시전자, Attempt)
  ② 판정 — 대상 수신 전후 성공 수 비교 / CombatStatsEvents.ServerCounterSucceeded 수신
       카운터 창을 소비한 첫 대상의 transform.position 을 기록만 한다(C5 — 판정 불변)
  ③ 시도 슬로우 정리            빗나가면 ServerFail(시도 번호)
  ④ 성공(카운터 창 소비)이면     ServerStart(interruptSuccessProfile, 시전자, Success, 대상 위치 — 없으면 시전자 위치)
                                 → last-wins: 시도 슬로우의 지금 배율을 덮고 맨 앞 히트스톱부터 다시 시작
                                 → 예측이 빗나가 시도 슬로우가 없었어도 건다
  ⑤ named message 로 전 피어 배포(SlowMotionEvent — 목적 칸 Purpose·대상 위치 TargetPosition 포함, 54B)
        │
        ▼ 각 피어에서 GlobalTimeScale.Started(id, 발동자 clientId, purpose, 대상 위치)  — 같은 값 = CurrentTargetPosition
  ├─ 시간 배율: 히트스톱 → 진입 → 유지 → 복귀(전 피어 같은 타임라인)
  ├─ InterruptDarkAdaptation (NetworkManager)   purpose==Success → 암순응 곡선, 본인/타인 강도, 원형 중심 = revealCenter
  ├─ InterruptSuccessFx (각 Player)              purpose==Success && 발동자 == 이 플레이어 Owner →
  │                                               casterFx.PlayOnce()(시전자 소켓) + targetFx 를 대상 위치에서 원샷
  └─ CameraTargetSwitcher (오너 카메라)          발동자가 나면 줌, 깊이 기준 = 지금 재생 중인 프로필 배율(CurrentProfileScale)
```

- **공용 성공 신호 = 슬로우 이벤트의 목적 칸**(`SlowMotionPurpose.Success`). 연출용 RPC 를 따로 두지 않는다 —
  성공 연출을 더할 땐 `GlobalTimeScale.Started` 를 구독하고 `purpose` 를 본다. 위치 기준이 필요하면 넷째 인자(대상 위치)를 쓴다.
- **대상 위치** = 카운터 창을 소비한 첫 대상(수신 전후 성공 수가 처음 바뀐 Unit, 또는 `CombatStatsEvents` 로만 온 성공의 target).
  서버 판정 시점 위치를 고정 월드 좌표로 싣는다 — 대상을 따라가지 않는다. 시도 슬로우·Fail·Reset 은 0.
- 연출 훅일 뿐 판정·피해·카운터 창에는 관여하지 않는다.
- 컷신·씬 전환·세션 종료로 `ForcedReset` 이 오면 암순응은 즉시 0. 원샷 성공 이펙트는 끊지 않는다.

## 2. 수치 (기본값 = `NetworkManager.prefab`)

### 2.1 슬로우 프로필 — `GlobalTimeScale` (시트 `SlowMotion`, Order 0)

| 필드 | 시도 `interruptProfile` | 성공 `interruptSuccessProfile` | 의미 |
|---|---|---|---|
| `scale` | 0.3 | 0.25 | 유지 단계 시간 배율 [0.01, 1] |
| `hitStopDuration` | 0 | 0.05 | 맨 앞 히트스톱 길이(실시간 초). 0 = 없음 |
| `hitStopScale` | 0.02 | 0.02 | 히트스톱 배율 [0.01, 1]. 진입은 이 배율에서 `scale` 로 간다 |
| `enterDuration` | 0.05 | 0.05 | 진입(linear) 실시간 초 |
| `holdDuration` | 0.35 | 1.2 | 유지 실시간 초 |
| `exitDuration` | 0.25 | 0.4 | 복귀(ease-out) 실시간 초 |
| `failExitDuration` | 0.1 | 0.1 | 실패 복귀 실시간 초(성공 슬로우는 Fail 을 받지 않는다) |

곡선은 고정(닫힌 식) — 피어마다 같은 `LostTimeUntil` 을 내야 해서 AnimationCurve 를 쓰지 않는다(`SlowMotionProfile` 주석).

### 2.2 오너 줌 — `CameraTargetSwitcher` (시트 `SlowMotion`, Order 1)

| 필드 | 기본 | 의미 |
|---|---|---|
| `slowMotionZoomFactor` | 0.85 | 내가 일으킨 슬로우 유지 단계에서 카메라 거리 배율 [0.1, 1]. 줌 깊이는 지금 재생 중인 프로필(시도·성공) 배율 기준 |

### 2.3 암순응 — `InterruptDarkAdaptation` (시트 `SlowMotion`, Order 2)

곡선은 **실시간(unscaled)** — 슬로우 배율과 무관하게 같은 길이로 보인다.

| 필드 | 기본 | 의미 |
|---|---|---|
| `selfIntensity` | 1 | 발동자 본인 화면 최대 강도 [0, 1] |
| `othersIntensity` | 0.35 | 다른 사람 화면 최대 강도 [0, 1] |
| `fadeInDuration` | 0.08 | 최대 강도까지 실시간 초 |
| `holdDuration` | 0.6 | 최대 강도 유지 실시간 초 |
| `fadeOutDuration` | 0.5 | 0 으로 돌아오는 실시간 초 |
| `luminanceThreshold` | 0.8 | 이 HDR 휘도보다 어두운 픽셀만 낮춘다(빛 이펙트·발광은 남는다) |
| `thresholdSoftness` | 0.3 | 임계 아래로 부드럽게 섞는 휘도 폭 |
| `maxDarken` | 0.85 | 강도 1 에서 어두운 픽셀을 낮추는 최대 비율 [0, 1] |
| `revealCenter` | Target | 원 중심 기준(열거 — **데이터 테이블 열 아님**, 인스펙터·`NetworkManager.prefab` 에서 고른다). Target = 대상 위치 · Caster = 발동자 추적 · Midpoint = 둘의 중간 |
| `revealRadius` | 0.18 | 중심 주변을 살리는 원 반경(화면 높이 비율). 0 = 원형 끔 |
| `revealSoftness` | 0.1 | 원 가장자리 부드러움(화면 높이 비율) |

**본인/타인.** `Started` 의 발동자 clientId 가 이 피어의 `LocalClientId` 면 `selfIntensity`, 아니면 `othersIntensity`.

**원형 중심**(`InterruptDarkAdaptationModel.TrySelectRevealCenter`) — 매 프레임 메인 카메라 뷰포트로 바꾼다(카메라 뒤면 원형 끔).

| `revealCenter` | 중심 | 시전자를 못 찾으면 |
|---|---|---|
| `Target`(기본) | 신호의 대상 위치(고정 월드 좌표) | 상관없음 |
| `Caster` | 발동자 플레이어 오브젝트(매 프레임 추적, C3 동작) | 원형 끔 |
| `Midpoint` | 대상과 시전자의 중간 | 대상 |

> 데이터 테이블은 enum 을 템플릿에 넣지 않는다([data-table.md §8](data-table.md)) — `revealCenter` 는 xlsx 열이 없다.

### 2.4 성공 이펙트 칸 — `InterruptSuccessFx` (Player.prefab base 루트)

| 필드 | base 기본 | 의미 |
|---|---|---|
| `casterFx` | 비어 있음 | 간파 성공 시 **시전자 소켓 위치**에서 원샷(`EffectSocketPlayer.PlayOnce`). C4 의 `successEffect` 를 이름만 바꾼 칸(`FormerlySerializedAs`) |
| `targetFx` | 비어 있음 | 간파 성공 시 **신호의 대상 위치**에서 원샷 — 충격파는 여기. 소켓을 그 위치로 잠깐 옮겨 `PlayOnce` 하고 같은 호출 안에서 되돌린다(회전은 소켓 회전, `offset` 은 더해진다) |

- 칸마다 따로 — 비어 있는 칸은 아무것도 안 한다. 둘 다 비면 아무것도 안 함(경고 없음).
- 전 피어가 각자 재생한다 — 판단은 `ShouldPlay(purpose, 발동자, 스폰 여부, OwnerClientId)` → `SelectSlots(재생 여부, casterFx 있음, targetFx 있음)`.
- `GlobalTimeScale` 이 플레이어보다 늦게 생겨도 `Update` 에서 다시 찾아 구독한다.
- 데이터 테이블 대상이 아니다(오브젝트 참조만 있다).

## 3. 렌더러 기능 — 순서와 주입 시점

`DarkAdaptationFeature`(`Assets/1.Scripts/Rendering/DarkAdaptation/`)는 `PC_Renderer`·`Mobile_Renderer` 기능 목록 **맨 끝**에 있다.

- 주입 시점 = `RenderPassEvent.BeforeRenderingPostProcessing`(550). Fog(450) 뒤, 같은 550 의 PlayerSilhouette·EnemyOutline·MaskBlur 뒤 —
  같은 이벤트끼리는 기능 목록 순서라 맨 끝에 둔다. 후처리(Bloom·톤매핑) 전 HDR 색에서 돌아 남긴 밝은 픽셀에서 Bloom 이 그대로 나온다.
- 강도(`DarkAdaptationState.Intensity`)가 0 이면 `EnqueuePass` 를 하지 않는다 — 평소 비용 0.
- 셰이더는 기능의 `_shader` 칸에 직접 연결한다(`Shader.Find` 는 빌드에서 빠진다).
- `PC_Renderer` 목록 순서(현재): SSAO(꺼짐) · Decal · Fog · MaskBlur(꺼짐) · RetroCRT · PlayerSilhouette · Flat Kit Outline · EnemyOutline · **DarkAdaptation**.

## 4. 성공 이펙트 넣는 법 (이펙트 담당 — 민경)

1. 캐릭터 **Variant**(`Player_Paladin.prefab`·`Player_Gunner.prefab` 등)를 연다. **base `Player.prefab` 은 고치지 않는다**
   ([player-prefabs.md](player-prefabs.md) — 캐릭터 고유 = Variant).
2. Variant 안에 소켓 오브젝트를 만들고 `EffectSocketPlayer` 를 붙인다 — `effect` 에 EffectEntry, 필요하면 `socket`·`offset`·`scale`.
   `id` 는 애니메이션 이벤트로 부르지 않으니 비워도 된다.
3. Variant 루트의 `InterruptSuccessFx` 칸에 그 소켓을 끌어 넣는다(base 값에 대한 Variant 오버라이드로 저장된다).
   - **충격파처럼 맞은 대상에서 터져야 하는 것은 `targetFx`**, 시전자 몸에서 나는 것은 `casterFx`.
   - `targetFx` 소켓은 루트가 아닌 **빈 자식 오브젝트**에 둔다 — 재생 순간 그 트랜스폼을 대상 위치로 잠깐 옮겼다 되돌린다.
4. Play — 간파 성공 시 모든 화면에서 한 번 재생된다. 원샷이라 소켓·대상을 따라가지 않고 재생 순간 위치에 찍힌다.

캐릭터별 기존 성공 연출(가붕이 `PlayerShieldVfx` 충격파, 어쌔신 `AssassinSkillView` 성공 소켓, 거너 쪽)은 그대로다 — 이 칸은 그것과 별개로 추가되는 공용 칸이다.
겹쳐 보이면 둘 중 하나를 비운다.

## 5. 알려진 제약

- **`Mobile_Renderer` 재직렬화.** C3 에서 기능을 추가하며 URP 가 `Mobile_Renderer.asset` 을 업그레이드(`m_AssetVersion` 2→3, 줄 바꿈 정리)했다.
  다음 사람이 열어도 같은 diff 가 다시 나지 않지만, 그 커밋 diff 에 노이즈가 섞여 있다.
- **성공이 `CombatStatsEvents` 로만 온 경우.** 대상이 `IInterruptSlowMotionTarget` 의 성공 수를 올리지 않고
  `CombatStatsEvents.ServerCounterSucceeded` 로만 성공을 알리면(`FindSlowMotionTarget` 이 못 찾는 대상 등) `interruptSucceeded` 는 서지만
  `interruptLanded` 는 서지 않는다. 그래서 시도 슬로우가 먼저 Fail 로 정리되고, 같은 판정 안에서 성공 Start 가 바로 덮는다(last-wins).
  화면상 차이는 없지만 `Failed` 이벤트가 성공 직전에 한 번 난다 — `Failed` 구독자는 이를 감안할 것.
- **임계값은 톤매핑 전 HDR 기준.** `luminanceThreshold` 는 화면에 보이는 최종 밝기가 아니라 후처리 전 HDR 휘도다.
  노출·톤매핑 설정을 바꾸면 같은 값이 다르게 보인다.
- **대상 위치는 판정 순간 값.** 클라 화면의 대상은 보간 지연만큼 뒤에 있을 수 있어 `targetFx`·Target 원형이 살짝 어긋나 보일 수 있다.
- 사람이 MPPM 으로 확인할 것: 본인/타인 강도 차이, 원형 중심이 `revealCenter` 대로(기본 = 맞은 대상) 잡히는지,
  `casterFx`·`targetFx` 를 넣은 뒤 전 화면에서 각각 시전자·대상 위치에 재생되는지.

## 6. 연출 스위치 — `InterruptPresentationToggles` (할 일 104)

슬로우만 남기고 그 위의 연출을 칸마다 끄고 켠다. 연출 코드·에셋은 지우지 않았고 재생 지점에서 스위치를 확인만 한다.
`NetworkManager.prefab` 루트(`InterruptDarkAdaptation` 옆)에 붙어 있다. **기본 전부 꺼짐.**

| 칸 | 필드 | 무엇이 언제 | 막는 곳 |
|---|---|---|---|
| A1 | `hitStop` | 성공 슬로우 맨 앞 정지 | `GlobalTimeScale.ServerStart` — 이벤트의 `hitStopDuration` 을 0 으로 싣는다 |
| A2 | `darkAdaptation` | 성공 때 화면 암순응 | `InterruptDarkAdaptation.HandleStarted` 입구 |
| A3 | `ownerZoom` | 내가 일으킨 슬로우 동안 카메라 줌 | `CameraTargetSwitcher.UpdateSlowMotionZoom` — 꺼지면 줌 배율 1 |
| A4 | `successFxSlot` | 공용 성공 이펙트 칸(§2.4) | `InterruptSuccessFx.HandleStarted` 입구 |
| B1 | `paladinShieldGlow` | 팔라딘 간파 시도 방패 발광·루프 | `FirstMeleeInterruptSkill.OnClientPlay` — `OnEnd` 의 끄기·회수는 늘 돈다 |
| B2 | `paladinShieldCharge` | 팔라딘 방패 기 모으기(클립 `Garen_Parry` 의 `ShieldCharge`) | `EffectAnimEvents.PlayEffect`·`StartEffect` |
| B3 | `paladinWave` | 팔라딘 성공 충격파 | `PlayerShieldVfx` 의 `interruptWave` 재생(RPC·오프라인 둘 다) |
| C1 | `assassinInsightAttempt` | 어쌔신 기 모으기·튀기기·올려치기(클립 `Attack_Up_01`) | `EffectAnimEvents.PlayEffect`·`StartEffect` |
| C2 | `assassinInsightSuccess` | 어쌔신 간파 성공 이펙트 | `AssassinSkillView.TryPlayInsightSuccess` |
| E1 | `targetFlash` | 맞은 쪽 섬광 `Interrupt_Flash` | 23호·건틀릿봇·스피너봇·월봇·허수아비·스파링 봇 `PlayInterruptFlashRpc` |

- **피어별.** 각 피어가 자기 `NetworkManager` 인스턴스 값을 읽는다(동기화 없음). 인스턴스가 없으면(EditMode·NetworkManager 없는 씬) 전부 꺼짐.
- **히트스톱만 서버 값.** 서버가 Start 이벤트를 만들 때(`GlobalTimeScale.CreateStartEvent`) 스위치를 보고 히트스톱 길이만 0 으로 싣는다 —
  전 피어 같은 타임라인이 유지된다. 배율·진입·유지·복귀는 그대로. 클라 쪽 `hitStop` 값은 쓰이지 않는다.
- **클립 이벤트 이펙트(B2·C1)** 는 id 로 가린다 — 간파 id(`ShieldCharge`, `Assassin_InsightWindup`/`Pop`/`Slash`)는 `InterruptPresentationToggles` 상수 한 곳에만 있다.
  다른 id 는 영향 없음. `StopEffect` 는 막지 않는다. 재생을 막아도 `EffectAnimEvents.Played` 알림은 난다 —
  `AssassinSkillView` 가 올려치기 알림을 C2 기준 프레임으로 쓰므로 **C1 을 꺼도 C2 를 켜면 성공 이펙트가 같은 프레임에 나온다.**
- 섬광(E1)은 연출만 막는다 — 판정·그로기·통계, 보스의 '섬광 칸 비어 있음' 경고는 그대로.
- 대상 아님: 슬로우 자체, 거너 폭발선(`GunnerBeamView.InterruptBlastRpc`), `InterruptHighlightHUD`, `CameraFeedback` 공통 흔들림.
- bool 이라 데이터 테이블 열이 아니다([data-table.md §8](data-table.md)) — `NetworkManager.prefab` 인스펙터에서 켠다.
- 테스트: `Tools/Tests/슬로우 모션 EditMode 테스트 실행`(`InterruptPresentationTogglesTests`).

## 7. 이후 확장

- 암순응 셰이더에 흑백 실루엣·반전 모드를 얹을 수 있음(미구현, 이펙트 확정 후 결정).
- 레퍼런스 의도 — 주변 입자가 한 점으로 모이는 효과 + 피격 대상에서 날카로운 선 수십 개가 한쪽으로 쏠려 터지는 충격파(번개 제외), targetFx 칸에 넣을 예정.
