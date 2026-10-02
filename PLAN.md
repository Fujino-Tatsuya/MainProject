> 📁 09-22 이전 계획(23호 공격 재작업 G1~G7 · Dev Boot · 벽 투명화 1단계 · 보호막 VFX 등)과 끝난 `PLAN-*.md` 는
> [Docs/history/PLANS/](Docs/history/PLANS/) 로 옮겼다. 옛 문서의 "PLAN.md §…" 참조는 [PLAN-archive-2026-06_09-22.md](Docs/history/PLANS/PLAN-archive-2026-06_09-22.md) 를 본다.

> 🆕 2026-10-02 은희: **캐릭터 선택 화면** → [PLAN-character-select.md](PLAN-character-select.md) (승인 대기)

# ▶▶▶ 진행 중 = **차징 구슬 외부 전기 유입** (2026-09-30 · 그릴 15문항 완료 · **구현 완료 ✅ · Play 검증 대기**)

## 별도 수정: 몬스터 메쉬·텍스처 연결 검증 (2026-10-01 · 완료)

- 사용자 승인 범위: ChompBot 표시·텍스처 문제를 포함해 기존 제작 몬스터 8종 전체 점검·수정.
- 확인한 문제: 현재 게임 프리팹이 원본 메쉬를 사용하여 새 텍스처의 UV와 맞지 않음. 눈·안테나·CRT의 제작 UV도 원본 Unity 재질과 불일치.
- 처리: 승인된 `.asset` 메쉬와 대응 맵 연결, 기존 본체 재질을 복제한 새 재질 11개, 표시부 UV 복구, 원본 클립 전체 프레임을 사용한 rootBone 기준 표시 범위 계산.
- 보존: Animator·Avatar·본·소켓·판정·게임 컴포넌트·기존 표시부/인터럽트 재질. 게임 프리팹 저장 때 생긴 다른 컴포넌트의 직렬화 변경은 제거함.
- 검증: Unity 6000.3.16f1 실제 GPU 렌더링, 저장된 게임 프리팹 8개, 원본 클립 51개·255시점. 전 시점 표시 확인, 변형 정점의 표시 범위 이탈 0. PNG 55개 해시·임포트 설정, 패킹 맵 11묶음 확인.
- 검토: Unity `Tools > Monster Surface Textures > Open Animation And Texture Review`, `Generated/monster-remodel-20260926/surface-v1/material-review/index.html`.
- 범위 한계: 전투 FSM·동작 합성·NGO·VFX 수명·성능 검사는 포함하지 않음. 메쉬·재질과 meta는 SVN, 게임 프리팹·에디터 도구는 Git 관리.

- 2026-10-01 사용자 요청으로 이전 에셋·복제 프리팹·반복 수정본·백업·임시 도구 등 5,674개(약 5GB)를 Windows 휴지통으로 이동. 최종 에셋·meta 해시 및 게임 프리팹 의존 관계 유지, Unity 51클립·255시점 재검증 통과. 정리 목록은 Docs/tech/monster-surface-cleanup-20261001.json, 전달 안내는 SurfaceV1/README.md.

---

> 작업 세션: **민경(Claude)**. VFX 단독 — 공유 코드 변경 없음.
> 🔴 `50.Art` = **SVN**. 특히 **텍스처 바이너리 교체가 포함**되므로 그 단계만 Unity 를 닫아야 한다.

보스 차징 구슬 바깥에서 전기가 날아와 구 표면에 꽂히며 사라진다 — "에너지를 끌어모아 충전한다".

## 지금 구조 (조사 결과)

```
FX_Buff_Lightningball_Loop      빈 컨테이너 PS(렌더러 꺼짐) ← 여기에 새 시스템을 붙인다
 └ FX_Buff_Lightningball        🔴 Grow · Loop · FadeOut · Break 네 프리팹이 공유
    └ ball                      Mesh(Sphere) · startSize 10.7
       ├ electric · flare_blue · ring · glow_blue ×2
       └ lightning_center       Stretched Billboard · m_LengthScale 4.5 · UV 시트 1×4
                                mat lightning_blue → Electro03.png (256×512)
```

**스케일 체인** — 중첩 인스턴스가 내부 루트 스케일을 `0.5 → 0.4673` 으로 **덮어쓴다**.
`0.4673 × 0.4(ball) = 0.18692`, 메시 반지름 `0.5 × 10.7 = 5.35` → **구 월드 반지름 1.0 m (지름 2 m)**.
구 중심은 `ChargeBallSocket`(보스 발밑 y 0) + 오프셋 2.5 → **지면 위 2.5 m**.
🔴 `_Loop` 루트는 스케일 1 이라 **거기 붙는 새 시스템은 월드 단위로 직접 잡는다.**

**텍스처 실측** `Electro03.png` 256×512 = 256×128 프레임 4장:

| | 중앙값 | 커버리지 |
|---|---|---|
| 코어 (α>200) | **6 px** | 8.1% |
| 본체 (α>120) | 7 px | 9.2% |
| 글로우 포함 (α>40) | 11 px | 13.2% |

프레임 높이 128 기준 코어가 **4.7%**. 화면상 획 두께 = `월드 폭 × (코어px / 128)` 이므로
현재 `lightning_center`(폭 0.26~0.37 m)는 **1.2~1.7 cm** — 이게 "얇다"의 실제 숫자다.

🔴 `lightning_blue.mat` 과 `Electro03.png` 은 **각각 참조가 한 곳뿐**이다(이 이펙트 전용).

## 그릴에서 확정된 것

| # | 항목 | 결정 |
|---|---|---|
| 1 | 어디에 | **`_Loop` 에만.** 알맹이에 넣으면 Break·FadeOut 까지 나와 "아직 충전 중"이라 거짓말한다 |
| 2 | 움직임 | **날아와 꽂힌다.** "충전"은 방향을 담은 말이다. 제자리 점멸은 기존 `electric` 과 역할이 겹친다 |
| 3 | 두께 해결 | **원본 `Electro03.png` 를 팽창시켜 다시 굽는다.** 참조 1곳이라 남에게 안 번진다 |
| 4 | 팽창량 | **+3 px → 코어 6→12 px (2배).** +2 는 티가 안 나고 +5 는 번개가 아니라 리본이 된다. **글로우도 같은 반경으로** 민다 — 코어만 굵히면 납작해진다 |
| 5 | 출발 반경 | **5.0 m** (구 반지름의 5배). 차징 예고로 멀리서도 읽혀야 한다 |
| 6 | 밀도 | **rate 12/s · 수명 0.3** → 동시 3.6개 |
| 7 | 끝 처리 | **그냥 사라짐.** 크기 1→0 이면 "흡수됐다"로 읽힌다. 표면 섬광은 기존 `electric` 이 이미 준다 |
| 8 | 머티리얼 | **`lightning_blue.mat` 재사용.** 같은 텍스처라야 안팎이 한 현상으로 읽힌다. SVN 추가 0 |
| 9 | 줄기 길이 | **약 1.8 m** (이동 거리 4 m 의 45%). 🔴 길이는 `m_LengthScale` 로 잡는다 — `startSize` 로 벌면 굵기가 같이 늘어 Q4 가 무의미해진다 |
| 10 | 색 | **진한 파랑.** 구 안쪽이 이미 가장 밝다(`flare_blue`·`glow_blue ×2`·`lightning_center`). 바깥이 진해야 "모여서 가운데가 뜨겁다"는 위계가 선다 |
| 11 | 속도 | **안으로 갈수록 가속.** 등속이면 그냥 날아오는 것이고, 충전은 구가 당기는 것이다 |
| 12 | 도착 산포 | **수명 ±10%(0.27~0.33), 착지 오차 ±0.4 m.** 🔴 정확한 착지와 산포는 **동시에 안 된다** — `Velocity over Lifetime` 은 m/s 라 이동거리 = 수명 × 평균속도이고, 두 랜덤값을 파티클 단위로 역비례시키는 기능이 없다. ±20% 는 허공에서 끊기는 게 보이므로 **±10% 가 상한** |
| 14 | 방향 | **위쪽 반구만.** 🔴 구 중심이 지면 위 2.5 m 인데 반경이 5 m 라, 완전한 구로 뿌리면 **최저 y = −2.5 m = 지면 아래**에서 솟는다 |
| 15 | 줄기 폭 | **0.50 m** (획 4.7 cm). 안쪽보다 굵어야 주인공이 된다. 폭을 키우면 `m_LengthScale` 이 5 → **3.6** 으로 내려가 **텍스처 늘어남이 2.5배 → 1.8배로 줄어든다** — 획이 같이 또렷해지는 이중 이득 |

## 만드는 것

### `Electro03.png` 다시 굽기 (🔴 SVN 바이너리 교체 · **Unity 닫아야 함**)

프리멀티플라이 RGBA 에 **반경 3 다이아몬드 팽창**(3×3 십자 3회). 알파와 색을 같은 반경으로 민다.
원본은 `scratchpad/Electro03.png.bak` 에 백업하고, **배율을 바꿀 때는 항상 원본에서 다시 굽는다**(팽창은 누적되면 안 된다).

```
코어 6 px → 12 px      커버리지 8.1% → 약 18%
```

⚠️ 같은 텍스처를 쓰는 **`lightning_center` 도 같이 굵어진다** — 의도한 바다(Q8: 안팎을 한 현상으로).

### `ChargeInflow` (신규 파티클 시스템 · `_Loop` 루트 직속 · 월드 단위)

```
looping 1 · playOnAwake 1 · simulationSpace Local · lengthInSec 1 · maxNumParticles 32
startLifetime  0.27 ~ 0.33 랜덤
startSpeed     0                         ← 이동은 전부 Velocity over Lifetime 이 만든다
startSize      0.50                      ← 월드 폭
startColor     진한 파랑 {0.3, 0.7, 1.4}
Shape          Hemisphere(2) · radius 5.0 · radiusThickness 0(껍질만) · 회전 X −90(돔이 위로)
Emission       rateOverTime 12           → 동시 12 × 0.30 = 3.6개
Velocity over Lifetime  radial 커브 · scalar −13.33 · 키 0.5@0 → 1.5@1 (평균 1.0 = 가속)
                        이동거리 = 0.30 × 13.33 = 4.0 m = 5.0 − 1.0 ✔
Size over Lifetime      1 → 0.85@0.5 → 0
UV                      1×4 시트 (lightning_center 에서 복제)
Renderer       Stretched Billboard(1) · m_LengthScale 3.6 · lightning_blue.mat
                        길이 = 0.50 × 3.6 = 1.8 m
```

**구현 = `lightning_center` 의 네 문서를 `_Loop` 로 복제해 개조한다.** 필요한 것(Stretched Billboard ·
UV 시트 1×4 · 같은 머티리얼)을 이미 다 갖고 있어서, 되돌릴 것이 Shape·Emission·Velocity 뿐이다.
**신규 에셋 0 · SVN add 0** (수정 2파일: 텍스처 + `_Loop` 프리팹).

## 구현 결과 (2026-09-30)

### 🔴 텍스처 방식을 바꿨다 — max 팽창은 실패했다

계획은 "반경 3 다이아몬드 팽창"이었다. **해 봤더니 흰 심이 사라지고 균일한 청록 리본이 됐다** —
max 는 이웃의 최댓값으로 평탄화하므로 **falloff 를 통째로 지운다.** 채도 높은 파란 글로우가
넓게 퍼지면서 좁은 흰 코어를 덮어써서, "굵어진" 게 아니라 **Q4 에서 경고했던 바로 그 납작함**이 나왔다.

**블러 + 게인으로 바꿨다.** 프리멀티플라이 상태에서 박스 블러(반경 3)를 돌려 획을 번지게 한 뒤
네 채널에 같은 게인(2.5)을 곱해 클램프한다. 블러는 평균이라 **그라데이션이 살아 있고**,
같은 게인이라 언프리멀티플라이 후 **색은 그대로이며 알파만 자란다.**

| | 코어(α>200) 중앙값 | 커버리지 | 코어 중 흰 심 |
|---|---|---|---|
| 원본 | 6 px | 15.8% | 23.4% |
| max 팽창 (폐기) | 13 px | 23.4% | — 심이 뭉개짐 |
| **블러+게인 (채택)** | **12 px** | 22.8% | **31.1%** ← 심이 오히려 또렷해졌다 |

목표치(6→12 px, 2배)를 정확히 맞췄다. 🔴 **다시 구울 때는 항상 `scratchpad/Electro03.png.bak`
(원본)에서 굽는다** — 블러도 누적되면 안 된다. 인자는 `dilate.py <반경> <게인>`.

### 그 외

| | 계획 | 실제 | 왜 |
|---|---|---|---|
| `ChargeInflow` 트랜스폼 | (언급 없음) | 🔴 **회전을 항등으로 초기화** | 복제 원본 `lightning_center` 가 `ball` 밑에서 쓰던 회전을 물려받아 있었다. 그대로 두면 **반구 축이 통째로 틀어진다** — 반구 방향은 ShapeModule 의 `m_Rotation x −90` 이 단독으로 정해야 한다 |
| 프리팹 증가 | 약 9,800줄 | **9,829줄** | 예측대로 |

측정값:

```
Electro03.png               256x512 · guid 유지 확인 ✅ · 62 → 107 KB
FX_Buff_Lightningball_Loop  4,925 → 9,829줄 · 문서 10개 · 중복 0 · 탭 0
ChargeInflow (루트 직속, 월드 단위)
  수명 0.27~0.33 · 속도 0 · 크기 0.5 · 색 {0.3, 0.7, 1.4} · maxParticles 32
  Shape  Hemisphere(2) · radius 5 · radiusThickness 0 · m_Rotation x −90
  Emission  rateOverTime 12                    → 동시 3.6개
  Velocity  radial state 1 · scalar −13.333 · 커브 0.5→1.5
            이동 = 0.30 × 13.333 = 4.0 m = 5.0 − 1.0 ✔
  Size      1 → 0.85@0.5 → 0
  UV        1×4 시트 (복제로 따라옴) · prewarm 1
  Renderer  Stretched(1) · m_LengthScale 3.6 · lightning_blue.mat → 길이 1.8 m
신규 에셋 0 · SVN add 0 · SVN 수정 2파일(텍스처 + _Loop 프리팹)
```

## 검증 항목

- [ ] 🔴 **반구 축이 위를 향하나** — Unity Hemisphere 는 +Z 반구라 X −90 으로 돌린다. **정적으로 확정할 수 없다.** 틀리면 옆으로 누운 반구가 되어 **한쪽에서만** 날아온다
- [ ] 지면 아래에서 솟는 줄기가 **없다**
- [ ] 줄기가 구 표면에서 사라진다 — 허공에서 끊기거나 구를 뚫고 나오지 않는다(오차 ±0.4 m 안)
- [ ] 안으로 갈수록 빨라지는 게 보인다
- [ ] 굵기: 유입이 안쪽 `lightning_center` 보다 눈에 띄게 굵다
- [ ] 안쪽 `lightning_center` 도 같이 굵어졌는데 **뭉개지지 않았다**
- [ ] 동시 3~4개 — 구 주변이 파랗게 뭉치지 않는다
- [ ] `Grow`·`FadeOut`·`Break` 에는 **유입이 없다**(Loop 전용)
- [ ] 🔴 **MPPM 클라 창**에서 같게 보인다
- [ ] 콘솔 경고 없음

## 리스크

| | |
|---|---|
| 🔴 반구 회전 | 정적 검증 불가. **증상이 명확하다** — 한 방향에서만 오면 회전이 틀린 것이고, `m_Rotation` 의 x 를 ±90 로 뒤집으면 된다 |
| 🔴 SVN 바이너리 | 텍스처 교체는 **Unity 를 닫고** 해야 한다. 바꾼 뒤 `.meta` guid 가 그대로인지 확인한다 |
| 텍스처 번짐 | `lightning_center` 도 굵어진다(의도). 안쪽이 뭉개지면 팽창을 +2 로 낮춰 원본에서 다시 굽는다 |
| 프리팹 비대화 | `_Loop` 이 4,925 → 약 9,800줄(+100%). 복제 대가다 |
| 범위 밖 | `Grow`/`FadeOut`/`Break` 연출 · 표면 착지 섬광 · 유입 전용 머티리얼 |

# ⏸ Play 검증 중 = **그랩 홀드 중 전기 전이 (잡힌 대상으로)** (2026-09-30 · 구현 완료 ✅ · **MPPM 전파 확인됨** · 나머지 항목 대기)

> 작업 세션: **민경(Claude)**. 🔴 공유 파일 `TwentyThreeBoss.cs`(경석) 수정 포함.
> 🔴 신규 VFX 에셋은 `50.Art` = **SVN**.

23호가 플레이어를 붙잡고 있는 동안, 전기가 **보스 손 → 대상 가슴으로 건너가고**(다리) 동시에
**대상 몸을 뒤덮는다**(감싸기). 지금은 지짐이가 보스 손에서만 터져서 **누가 당하고 있는지 안 보인다** —
그게 이 작업이 고치는 것이다.

## 지금 상태 (조사 결과)

| | |
|---|---|
| 팔 전기 | `grabPulse` = `EffectPathPlayer`(id `ArmElectric`), 경로 **어깨→팔꿈치→손** 3점, `FX_Grab_ArmElectric_Entry`, travelTime 0.3 / interval 0.3 / pool 10 / scale 1.2 |
| 틱 지짐이 | `PlayGrabbedElectricClientRpc` 가 `grabTickInterval` 0.5초마다 `FX_Grabbed_Electricity_Entry` 를 **그랩 소켓(보스 손)** 에 원샷 |
| 홀드 길이 | `grabHoldDuration` 2초 → 지짐이 약 4회, 그 뒤 내려치기 3회 |
| 플레이어 치수 | 캡슐 반경 0.38 · 높이 1.79 · 중심 y 0.88 (발 y 0, 머리 y ≈ 1.78) |

🔴 **"잡혀 있다"는 상태는 복제되지 않는다.** `BeginRestrainedClientRpc` 가 `if (!IsOwner) return;` 로
막혀 있고 FSM 자체가 복제 대상이 아니다 — 남의 화면에서 그 플레이어는 영원히 Idle/Move 다.
`Player.IsGrabbed` 같은 프로퍼티도 없다. **각 피어가 스스로 판단할 수 없으므로 보스가 알려 줘야 한다.**

🔴 **대신 위치 추종은 공짜다.** 잡힌 플레이어는 리페어런팅되지 않는다 — `PlayerRestrainedState.FixedTick`
이 포즈만 복사하고, 루트는 여전히 씬 루트의 `NetworkTransform` 이라 **모든 피어에 위치가 복제된다.**

## 그릴에서 확정된 것

| # | 항목 | 결정 |
|---|---|---|
| 1 | 무엇이 보이나 | **다리 + 감싸기 둘 다.** 다리가 주, 감싸기가 보조 — 다리만이면 시선이 손에 남고, 감싸기만이면 보스가 한 일로 안 읽힌다 |
| 2 | 리듬 | **2층.** 상시 루프를 새로 깔고, 기존 틱 원샷은 그 위에 그대로 남긴다(데미지 순간 피드백) |
| 3 | 구간 | **놓아줄 때까지 전부** — 홀드 + 내려치기 3회. 내려치기 순간의 추가 번쩍은 범위 밖(타격 연출이 이미 있다) |
| 4 | 회수 | **기존 `StopGrabPulseClientRpc` 5개 길목에 같이 태운다** → 새 누수 경로 0개 |
| 5 | 감싸기 기술 | **파티클**(셰이더 오버레이 아님) — `DissolveOverlay` 슬롯은 `FirstMeleeUltimateSkill.superArmorOverlay` 가 이미 쓴다. **궁극기 중 잡히면 싸운다.** 캐릭터마다 메시 작업이 드는 것도 이유 |
| 6 | 다리 구현 | **프록시 트랜스폼 + 두 번째 `EffectPathPlayer`**(경로 = 손 → 프록시). `EffectPathPlayer` 자체는 안 고친다 |
| 7 | 착점 | **대상 가슴 한 점**(발 기준 y 1.2). 여러 점이면 프록시·재생기·회수 길목이 배로 는다 |
| 8 | 세기 | **일정.** 램프는 `SetScale` 이 필요한데 그건 다음 펄스부터만 먹어서 굵기가 섞인다 |
| 9 | 프록시 주인 | **새 컴포넌트.** 보스 파일 증분을 "RPC 쌍 + 대상 참조"로 최소화한다 |
| 10 | 감싸기 앵커 | **프록시 하나 + `EffectSocketPlayer.offset`**(y −0.32). 추종 오프셋은 대상 회전을 탄다(`WorldPosition`) |
| 11 | 틱 원샷 | 🔴 **대상 가슴으로 옮긴다.** 타이밍·엔트리·빈도는 그대로, **위치만.** 도착점에서 터져야 인과가 완성된다 |
| 12 | 감싸기 볼륨 | **Box `0.8 × 1.9 × 0.8`, 중심 y 0.88** (캡슐 근사) |
| 13 | 감싸기 재료 | **`electronic.mat`**(`Electro.png` 번개 지그재그). 다리(리본)와 **다른 재료**여야 역할이 갈린다 |
| 14 | 에셋 | **신규 제작.** 미사용 `FX_Grabbed_Electricity.prefab`(참조 0곳) 개조는 하지 않는다 — 이름과 내용이 어긋난다 |
| 15 | 다리 리듬 | **travelTime 0.15 / interval 0.15 / pool 10.** 거리가 팔의 절반쯤이라 같은 시간이면 손에서 굼떠진다 |

## 만드는 것

### `Assets/1.Scripts/Effects/GrabbedEffectAnchor.cs` (신규 · git · ~90줄)

프록시를 대상 가슴으로 옮기고, 다리·감싸기 두 재생기를 켜고 끈다. **보스를 모른다** — 대상 트랜스폼만 받는다.

```
[SerializeField] Transform proxy;              // 따라다닐 빈 트랜스폼
[SerializeField] EffectPathPlayer bridge;      // 손 → 프록시
[SerializeField] EffectSocketPlayer envelope;  // 프록시 기준 몸 전체
[SerializeField] float chestHeight = 1.2f;     // 발 기준
[SerializeField] Transform fallback;           // 대상을 잃었을 때 떨어뜨릴 곳(보스)

public void Play(Transform target)   // 대상 물고 proxy 이동 시작 → bridge.Play() + envelope.Play()
public void Stop()                   // bridge.Stop() + envelope.Stop(), 대상 해제
public Vector3 ChestPoint            // 틱 원샷이 읽어 갈 현재 가슴 좌표
void LateUpdate()                    // proxy.position = target.position + up * chestHeight
                                     // proxy.rotation = target.rotation
```

- 🔴 **LateUpdate** 여야 한다. 잡힌 포즈는 `FixedTick`/`NetworkTransform` 이 쓰므로 그 뒤에 읽어야 한 프레임 안 밀린다.
- 🔴 **대상이 사라지면 `fallback` 위치로 떨어뜨리고 계속 재생한다.** 끄지 않는다 — 끄는 것은 보스의 종료 신호 몫이고, 여기서 임의로 끄면 종료 신호가 왔을 때 이미 없어서 상태가 갈린다. (기존 "소켓 없으면 보스 위치" 정책과 같은 판단)
- 🔴 **`IsServer` 가드를 넣지 않는다.** 모든 피어가 로컬로 돈다(`EffectPathPlayer`·`DissolveOverlay` 주석이 같은 경고를 단다).

### `FX_Grabbed_Envelope.prefab` + `_Entry.asset` (신규 · 🔴 SVN add 4파일)

`Assets/50.Art/VFX/Common/Boss/Grab/` 에 둔다.

```
looping 1 · playOnAwake 0 · simulationSpace Local
Shape   Box (0.8, 1.9, 0.8)        ← 캡슐 근사. SocketPlayer.offset 이 y 를 0.88 로 내린다
rate    25/s   수명 0.25   크기 0.25~0.6 랜덤   회전 0~2π 랜덤
색      차가운 청백 HDR (팔 전기와 같은 계열)
Size over Lifetime  0 → 1@0.3 → 0    (지지직 점멸)
Renderer  Billboard · electronic.mat
```

🔴 **속도 0 이므로 Stretched Billboard 를 쓰지 않는다.** 늘일 방향이 없어 통째로 안 보인다 —
`FX_Magnetic_Pull` 의 `Arcs` 가 정확히 그 이유로 안 보였다(2026-09-30 확인).

### `TwentyThreeBoss.cs` (수정 · git · 🔴 경석님 파일 · +~55줄)

```
[SerializeField] GrabbedEffectAnchor grabbedElectric;   + _warnedNoGrabbedElectricAnchor

[ClientRpc] StartGrabbedElectricClientRpc(NetworkObjectReference target)
              → TryGet 해서 grabbedElectric.Play(player.transform)
[ClientRpc] StopGrabbedElectricClientRpc()
              → grabbedElectric.Stop()          (Reliable — 유실되면 플레이어 몸에 영구히 붙는다)
```

호출 자리 = **기존 `grabPulse` 와 정확히 같은 6곳**:
시작 1곳(`AcquireGrab` 성공, 972행 옆) / 종료 5곳(1908 · 1986 · 2072 · 2180 · `AbortAttackChain`).

그리고 **틱 원샷의 좌표만** 바꾼다 — `PlayGrabbedElectricClientRpc` 가 `GrabSocket` 대신
`grabbedElectric.ChestPoint` 를 읽는다(앵커가 없으면 기존 소켓으로 폴백).

### `TwentyThree.prefab` (수정 · git · 문서 5개 · +~80줄)

`Dash` 옆에 `GrabbedElectric` 빈 오브젝트를 만들고 그 아래:
`GrabbedAnchor`(프록시 트랜스폼) · `GrabbedEffectAnchor` · `EffectPathPlayer`(다리) · `EffectSocketPlayer`(감싸기).
다리 경로 = `[손 본(564008308436468704), 프록시]`, 엔트리 `FX_Grab_ArmElectric_Entry`, scale 1.2.
감싸기 소켓 = 프록시, `offset (0, -0.32, 0)`, 엔트리 `FX_Grabbed_Envelope_Entry`.
보스 컴포넌트에 `grabbedElectric` 배선.

## 구현 결과 (2026-09-30)

계획대로 들어갔다. **계획에서 바뀐 것은 아래 두 줄뿐이다.**

| | 계획 | 실제 | 왜 |
|---|---|---|---|
| 감싸기 `safetyTimeout` | (미지정, 기본 5) | **12** | 🔴 그랩 체인 최장이 catch 1.1 + hold 1.13 + throw 0.65×3 + end 1.37 = **5.55초**다. 5 였으면 **정상 재생을 잘라낸다** |
| 종료 길목 | "5곳" | **4곳** | 실제로 `StopGrabPulseClientRpc` 가 불리는 자리는 넷이다(헛잡기 · 대상 소멸 · 놓아줌 · AbortAttackChain) |

측정값:

```
GrabbedEffectAnchor.cs      140줄 · 중괄호 9/9
TwentyThreeBoss.cs          4,772 → 4,841줄 (+69) · 중괄호 304/304
  시작 1곳(AcquireGrab 1966) / 종료 4곳(1955 · 2038 · 2125 · 2235)
  틱 원샷 좌표(2859) → grabbedElectric.ChestPoint, 폴백 소켓 → 보스
TwentyThree.prefab          문서 162개 · 중복 0 · 탭 0
  VFX/Grab/GrabbedElectric [Anchor][PathPlayer 다리][SocketPlayer 감싸기]
                           └ GrabbedAnchor (프록시)
FX_Grabbed_Envelope.prefab  4,890줄 — FX_Dissolve_Death 복제 후 개조
  Box(0.8,1.9,0.8) · rate 25/s · 수명 0.25 · 크기 0.25~0.6 · 회전 0~2π
  색 {1.2, 2, 3.2} · Size 0→1@0.3→0 · Billboard · electronic.mat
  Color·Velocity 모듈 OFF · looping 1 · simulationSpeed 1 · Local
FX_Grabbed_Envelope_Entry   duration 0 / outro 0.6 / prewarm 2
다리                        FX_Grab_ArmElectric_Entry 재사용 · travelTime·interval 0.15 · pool 10 · scale 1.2
```

🔴 **복제 원본을 `FX_Dissolve_Death` 로 골랐다.** 프로젝트에서 **단일 시스템 + Billboard + Size 모듈**을
모두 갖춘 프리팹이 그것뿐이다(`Grab` 폴더의 것들은 전부 트레일이 달린 다계층이다).
`DissolveDeath` 는 Shape 를 런타임에 스킨드 메시로 갈아끼우므로 프리팹 쪽 Shape 는 폴백이고,
복제본을 Box 로 바꾸는 데 걸림돌이 없었다.

🔴 **신규 에셋 4파일 = SVN add 필요** (`FX_Grabbed_Envelope.prefab`·`.meta`, `_Entry.asset`·`.meta`).
`GrabbedEffectAnchor.cs`·`.meta` 는 `1.Scripts` = git 이다.

## 검증 항목

- [ ] 붙잡는 순간 다리가 생기고, 놓는 순간 사라진다
- [ ] 다리가 **손에서 가슴까지** 이어진다 — 팔 구간과 속도가 이어져 보인다
- [ ] 감싸기가 몸을 덮는다 — 발밑·머리 위로 새지 않는다
- [ ] 틱 번쩍이 **대상에서** 터진다(0.5초 간격 유지)
- [ ] 내려치기 3회 동안에도 유지되고, 놓는 순간 함께 걷힌다
- [ ] 🔴 돌진 중 **카운터·그로기·보스 사망**으로 끊었을 때 플레이어 몸에 안 남는다
- [ ] 🔴 잡힌 **플레이어가 사망**해도 안 남는다
- [ ] 🔴 **MPPM 클라 창**에서 같게 보인다 — 특히 *잡히지 않은* 쪽 화면에서 보이는지(복제 안 되는 상태라 여기가 핵심)
- [ ] 궁극기(슈퍼아머 오버레이) 중에 잡혀도 둘이 안 싸운다
- [ ] 콘솔 경고 없음

## 리스크

| | |
|---|---|
| 🔴 공유 파일 | `TwentyThreeBoss.cs` 를 이 세션에서만 **네 번째** 수정(궤적·보호막·벽충돌·이번). 경석님께 **한 덩어리로** 정리해 넘긴다 |
| 대상 참조 | 클라에서 `NetworkObjectReference.TryGet` 이 실패할 수 있다(디스폰 타이밍) → 폴백 위치로 떨어뜨리고 종료 신호를 기다린다 |
| 한 프레임 밀림 | `LateUpdate` 로 읽지만 `NetworkTransform` 보간과 어긋나면 다리 끝이 살짝 뜰 수 있다 — Play 에서 확인 |
| SVN | 🔴 **신규 4파일**(`FX_Grabbed_Envelope` 프리팹·엔트리 + meta). 기존 백로그와 함께 add 필요 |
| 범위 밖 | 미사용 `FX_Grabbed_Electricity.prefab`(참조 0곳) 정리 · 내려치기 순간 추가 번쩍 · 세기 램프 · 징크스 대응 |

# ⏸ Play 검증 대기 = **성검 메쉬 (R스킬 최후의 심판 · 낙하 착지)** (2026-09-29 · 그릴 18문항 완료 · **2026-09-29 기성 에셋으로 선회**)

> 작업 세션: **민경(Claude)**. VFX 단독 영역이라 공유 대상 없음 —
> 🔴 단, `FX_Lightening_Strike.prefab` 은 **`50.Art` = SVN** 이다.

## 목표

`FX_Lightening_Strike` 의 **빛 기둥(`LightPillar`)을 3D 성검 메쉬로 교체**한다.
칼은 하늘에서 떨어져 지목된 좌표에 꽂히고, 포인트가 빛난다.

## 🔴 2026-09-29 방향 전환 — 직접 모델링을 버리고 기성 에셋을 쓴다

Blender 로 직접 뽑던 메쉬(실루엣 6회 반복)를 접고, 사용자가 임포트한
**`Assets/JC_StylizedWeapons_Lite/`** (JC Stylized Weapons Lite) 의 `SM_Sword_01` 로 간다.

**없어진 단계 = 1(모델링) · 2(UV) · 3(문양 베이크) · 4(FBX 내보내기).**
에셋이 메쉬와 2048² 텍스처 4장(Base / Normal / Metallic / Roughness)을 이미 들고 온다.

**따라서 아래 그릴 결정은 에셋이 대신한다 — 다시 논의하지 않는다:**
`1`(가드 형태) `2`(음각 구현) `3`(음각/양각) `4`(칼날 단면) `6`(음/양 배분)
`9`(폴리·뒷면) `11`(텍스처 구성) `12`(해상도·UV).
**살아 있는 결정 = `5`(크기·피벗) `7`(발광 포인트) `8`(커스텀 셰이더) `10`(요 회전)
`13`(가짜 광원) `14`(발광 연출) `15`~`18`(낙하 연출).**

### 에셋 실측치 (FBX 직접 파싱 · Blender 안 거침)

| | |
|---|---|
| 메쉬 | `SM_Sword_01.fbx` guid `a3f745712a8a6d34ba2d5c78c073ce8d`, 서브메쉬 fileID `7905712616854619025` |
| 삼각형 | **2,984 tri / 1,560 vert** — 그릴 9의 "3~5k" 안에 정확히 들어온다 |
| 단위 | `UnitScaleFactor 1` = **cm 파일**, `useFileUnits: 1` → Unity 에서 **1.200 m** |
| 🔴 축 | 칼날이 **+Y**(칼끝 위), 폭 X, 두께 Z. 길이 `-0.152 .. +1.048`, 폭 `±0.200`, 두께 `±0.043` |
| 🔴 피벗 | **칼끝이 아니라 그립 속**(손잡이 끝에서 +0.152 m). 그릴 5의 "피벗=칼끝"이 안 맞는다 |
| 머티리얼 | `M_StylizedWeaponsLite_Sword_01` = URP **AutodeskInteractive**(PBR). HDRP 프로퍼티는 잔재 |
| 룩 | 금 장식 + 강철 날 + **보라 가죽 그립** + **청록 보석** 인레이 → 그릴 7의 "발광 3곳"에 그대로 맞는다 |
| 위치 | `Assets/JC_StylizedWeapons_Lite/` = **git 추적 중**(6.3 MB). `50.Art`(SVN)가 아니다 ← 아래 리스크 |

### 피벗·회전·크기를 어떻게 맞출 것인가

에셋 피벗이 칼끝이 아니므로 **메쉬를 다시 굽지 않고 파티클에서 해결한다**
(그릴 10 에서 회전을 파티클에 두기로 한 것과 같은 이유 — 인스펙터에서 보이고 싸다).

| 필요한 것 | 쓰는 노브 |
|---|---|
| 칼끝을 아래로 | `startRotationX = 180°` (에셋은 칼끝이 +Y) |
| 60° 카메라 정면 각 | `startRotationY` **시작 135°, ±20° 튜닝** (그릴 10 그대로) |
| 6 m 로 키우기 | `startSize = 5` (1.200 m × 5 = **6.0 m**) |
| 🔴 칼끝을 바닥에 정확히 | `ParticleSystemRenderer.m_Pivot` — **파티클 크기 단위**의 오프셋이다. 칼끝은 원점에서 +1.048/1.200 = **+0.873** 지점 → 뒤집혔으므로 `pivot.y` 로 상쇄한다. 실측으로 확정할 것 |

## 조사로 확정된 사실

| | |
|---|---|
| 현재 `LightPillar` | 유니티 내장 Cylinder, 크기 `2 × 15 × 2`, 수명 1초, 길이 0.05초, 이미터 local y=30 |
| `Renderer.m_RenderAlignment` | **2 (Local)** — 메쉬가 카메라를 안 따라간다. 월드 고정 ✅ |
| `PS.startRotation3D` | 미설정 → 켜고 X/Y 를 준다 |
| `PS.VelocityModule` | 이미 `enabled: 1` — 낙하에 그대로 쓴다 |
| 엔트리 | `FX_Lightening_Strike_Entry`, `computedDuration` **2.15초**, prewarm 3 |
| 재생 배율 | `targetStrikeScale: 1` (코드가 안 키움) |
| 카메라 시선 | `(-0.36, -0.86, 0.36)` → 피치 60°, **요 -45°** |
| 씬 라이트 | 조사 방향 `(-0.17,-0.94,-0.29)`, 고도 **70°**(거의 수직) |
| 🔴 맵 조명 | 디렉셔널이 **Baked 모드**인데 라이트맵도 프로브도 없다 → **동적 오브젝트는 직접광을 못 받는다.** URP Lit 도 AutodeskInteractive 도 여기서 죽는다 → **에셋 머티리얼을 그대로 쓸 수 없는 이유** |
| 🔴 제약 | `ShurikenEffectSystem.CanDrive` 가 `ParticleSystem` 을 요구한다 → 평범한 MeshRenderer 는 EffectManager 가 못 켠다. **반드시 파티클의 Mesh 렌더 모드** |
| 머티리얼 1장 | 파티클 Mesh 모드는 머티리얼을 하나만 받는다 → 에셋이 1장만 쓰므로 문제 없다 ✅ |

## 남은 설계 결정 (그릴 합의 중 유효한 것)

| # | 항목 | 결정 |
|---|---|---|
| 5 | 크기 | **6 m** (`startSize 5`). 피벗은 위 표대로 렌더러에서 보정 |
| 7 | 발광 포인트 | **3곳** — 가드 보석 · 폼멜 보석 · 날 중앙. 에셋의 **청록 보석**이 그대로 후보다 |
| 8 | 셰이더 | **커스텀 이미시브.** 씬 조명을 안 쓴다 |
| 10 | 정면 각도 | 파티클 `startRotationY`(135° ±20°). 메쉬에 굽지 않는다 |
| 13 | 가짜 광원 | **히어로 라이팅** — 키 ≈ `(0.5, 0.7, -0.5)` 월드, 뒤에 림 하나 |
| 14 | 발광 연출 | **등장 플래시 후 안정**(`_Time` 기반) |
| 15 | 등장 | **하늘에서 낙하** |
| 16 | 시간 배분 | 낙하 **0.3초(약 12m)** → 체류 1.45초 → 소멸 0.4초 |
| 17 | 낙하 곡선 | **가속 후 급정지** |
| 18 | 궤적 | **새 스트레치 빌보드** 추가. `LightPillar` 은 삭제 |

### 설계에서 특별히 짚은 것

**① 회전을 코드가 아니라 파티클에 둔 이유.** 필요한 각도는 "카메라가 요 -45°일 때만 맞는 값"이다.
코드(`Quaternion.identity` 자리)에 숨기면 **왜 그 숫자인지 아무도 모르게** 되고, 은희 님 파일을 건드리게 된다.
메쉬에 구우면 리익스포트 없이는 못 고친다. 파티클 인스펙터가 셋 중 가장 눈에 보이고 가장 싸다.

**② 낙하를 넣으면 타격감이 밀릴 수 있다 — 그래서 0.3초다.** 이 이펙트는 `OnChannelCompleted()`,
즉 채널링이 **이미 끝난** 시점에 재생된다. 낙하 시간만큼 "심판"과 "도착" 사이에 빈 틈이 생긴다.
0.3초는 눈이 낙하를 인지하는 하한이면서 틈이 안 느껴지는 상한이다. 늘리려면 이 트레이드오프를 다시 봐야 한다.

**③ 🔴 낙하 정지는 수동으로 맞춘 커브다.** Velocity over Lifetime 은 "바닥에서 정확히 0"을 모른다.
높이·시간·커브가 서로 물려 있어 **하나를 바꾸면 셋을 다시 맞춰야 한다.** 프리팹에 주석을 남길 수 없으니
이 문단이 그 기록이다.

**④ 🔴 에셋 머티리얼을 왜 버리는가.** `AutodeskInteractive` 는 PBR 이라 씬 직접광이 필요한데
이 맵은 디렉셔널이 Baked 이고 프로브가 없다. 그대로 쓰면 **앰비언트만 받아 납작한 회색 덩어리**가 된다.
커스텀 셰이더는 에셋의 Base·Normal 을 그대로 샘플링하되 **광원을 상수로 들고 있어서**
맵 조명이 고쳐지든 말든 똑같이 나온다. 색 튜닝도 인스펙터 한 번이다.

## 구현 단계

1. ~~Blender 모델링~~ · ~~UV~~ · ~~문양 베이크~~ · ~~FBX 내보내기~~ → **에셋으로 대체 ✅**
2. **에셋 정리 ✅** — `Assets/50.Art/VFX/Models/JC_StylizedWeapons_Lite/` 로 이동(guid 보존, git → SVN).
   데모 씬·배경 머티리얼·컨버터 `.unitypackage` 2개는 사용자가 삭제
3. **셰이더 ✅** — `Assets/50.Art/VFX/Shaders/HolySword.shader` (`VFX/HolySword`)
4. **텍스처 ✅** — 둘 다 `Assets/50.Art/VFX/Textures/`
   - `HolySword_Base.png` (2048²) — **에셋 베이스컬러를 대체**. 아래 「베이스맵을 갈아끼운 이유」
   - `HolySword_Emissive.png` (1024², R=보석 G=금)
5. **머티리얼 ✅** — `Assets/50.Art/VFX/_Materials/HolySword.mat`
6. **프리팹 ✅** — 아래 「적용 수치」
7. **검증 — 미완**(Play 필요)

### 베이스맵을 갈아끼운 이유 (2026-09-29)

에셋 원본 `T_StylizedWeapons_Sword_Base`(금·보라 가죽·청록 보석으로 칠해진 것)는 **프롭 룩**이다.
바닥에 꽂히는 심판의 검으로는 안 읽힌다. 그래서 초기 요구였던
**"백색 외곽선 + 안쪽으로 갈수록 희미한 그라데이션"** 을 **에셋의 실제 UV 위에** 다시 구웠다.

`scratchpad/make_basemap.py` — 순수 파이썬(zlib+struct), PIL/numpy 없음.

| | |
|---|---|
| 외곽선 씨앗 | ① **UV 섬 경계**(FBX `LayerElementUV` 를 직접 파싱해 1,509 폴리곤을 2048² 로 래스터화) ② **금 장식 영역 경계** ③ **보석 영역 경계** |
| 왜 셋인가 | 섬 경계만 쓰면 조각 실루엣만 나온다. 금·보석 경계를 넣어야 **덩굴 문양이 선으로 드러난다** |
| 거리장 | 챔퍼 3-4, 섬 안쪽으로만 번진다 |
| 램프 | `RIM 3px` 순백 → `FALLOFF 22px` smoothstep → `FLOOR 0.10` |
| 패딩 | 섬 밖 `10px` 번지게 — 밉맵에서 검은 테두리가 생기는 걸 막는다 |
| 색 | **없다.** RGB 는 회색조뿐이고 색은 전부 머티리얼이 칠한다 |

🔴 **첫 시도는 실패했다** — `RIM 5 / FALLOFF 58` 로 구웠더니 섬 폭보다 폴오프가 넓어
**덮인 면적의 70%가 거의 흰색**이 됐다. 그라데이션이 안 읽히고 통짜 백색 덩어리가 된다.
섬(특히 금 리본)이 2048² 에서 80~200px 밖에 안 된다는 걸 계산에 안 넣은 탓이다.

**셰이더도 같이 바꿨다 — 색을 전부 머티리얼로 뺐다:**

| 전 | 후 |
|---|---|
| `_GoldBoost` (float, 밝기만) | **`_GoldTint`** (HDR Color) |
| `_GemColor` 로 **덮어쓰기** | **곱하기** — 덮으면 외곽선 그라데이션이 평평해진다 |
| (없음) | **`_EdgeEmission`** — 베이스맵 밝기에 곱하므로 **선만 빛나고 안쪽은 안 빛난다** |

머티리얼 기본값: 칼날 `(0.78,0.86,1.0)` 차가운 백 / 금 `(1.0,0.82,0.45)` / 보석 `(0.35,0.95,1.1)`.
🔴 에셋 **노멀맵은 그대로 쓴다.** 면이 흰 라인아트로 평평해진 만큼 오히려 더 중요해졌다.

### 적용 수치 — `FX_Lightening_Strike.prefab`

🔴 `LightPillar` 을 **지우지 않고 개조**했다. 그 슬롯이 이미 `m_RenderMode: 4`(Mesh) +
`m_RenderAlignment: 2`(Local) 였고, 5천 줄짜리 ParticleSystem 문서를 손으로 쓰는 것보다
비교할 수 없이 안전하다. 그래서 그릴 Q18의 "새 스트레치 빌보드 추가"도
**같은 파티클의 TrailModule** 로 대체했다(GameObject 증가 0).

| | 전 | 후 | 근거 |
|---|---|---|---|
| 이름 | `LightPillar` | `HolySword` | |
| 이미터 Y | 30 | **15** | 낙하 12.0 + 칼끝 오프셋(사용자 튜닝) |
| `startLifetime` | 1 | **2.15** | 0.3 낙하 + 1.45 체류 + 0.4 소멸 (그릴 Q16) |
| `startSize` / `size3D` | 2 / 1 (2×15×2 원기둥) | **5 / 0** | 1.200 m × 5 = **6.0 m** (그릴 Q5) |
| `rotation3D` | 0 | **1** | X/Y 회전을 쓰려면 필수 |
| `startRotationX` | 0 | **3.1415927** (=180°) | 에셋은 칼날이 **+Y** → 뒤집어 꽂는다 |
| `startRotationY` | 0 | **2.3561945** (=135°) | 그릴 Q10 시작값. ±20° 굴려 튜닝 |
| ShapeModule | on | **off** | 칼이 정확히 원점 위에 뜨게 |
| SizeModule | on | **off** | 칼은 커지거나 작아지지 않는다 |
| `maxNumParticles` | 1000 | **4** | 칼은 하나 |

**🔴 낙하 산술 — 하나 바꾸면 셋 다 다시 맞춰야 한다** (위 ③의 실제 숫자)

```
칼끝 오프셋 = 1.048 m (메쉬 원점→칼끝) × startSize 5           = 5.24 m
낙하 거리   = 12.0 m                                           (고정)
이미터 Y    = 15        ← 🔴 사용자가 눈으로 튜닝한 값. 여기서 12 내려가 3.0 에 선다
렌더러 pivot.y = -0.16  ← 🔴 사용자 튜닝. 칼끝을 바닥 아래로 묻어 "꽂힌" 느낌을 낸다
속도 y 커브 = 0 → -1 선형(t 0→0.0682) → 0 급정지(t 0.0698)
속도 scalar = 160  (최고속 160 m/s)
이동거리    = 160 × 0.5 × 0.0698 × 2.15s                       = 12.0 m 정확히
착지 시각   = 0.0698 × 2.15s                                   = 0.150 s
```

**낙하 시간을 다시 바꿀 때의 공식** (`LIFE` = startLifetime 2.15):

```
t2(급정지 시각, 정규화) = 원하는 낙하시간 / LIFE
속도 scalar            = 낙하거리 / (0.5 × t2 × LIFE)
t1(램프 끝)            = t2 - 0.0016
키 기울기              = -1 / t1
→ 낙하거리를 유지하면 **착지 지점이 안 움직이므로** 이미터 Y 와 pivot 은 건드릴 필요가 없다.
→ 대신 **충돌 자식 6개의 startDelay 를 착지 시각에 맞춰 다시 밀어야 한다.**
```

🔴 **`m_Pivot` 과 이미터 높이는 사용자가 Play 로 맞춘 값이다.** 에셋 원점이 칼끝이 아니라
그립 속이라 둘을 같이 써야 칼끝 위치가 잡힌다. `m_Pivot` 은 "파티클 크기 단위"라
부호·배율을 정적으로 확정할 수 없어 눈으로 맞췄다 — **숫자를 근거 없이 되돌리지 말 것.**

**🔴 충돌 자식 6개는 착지 시각을 따라다닌다.** 자식들의 `startDelay` 는 "칼이 땅에 닿는 순간"에
맞춰져 있다. 낙하 시간을 바꾸면 **반드시 같이 밀어야 한다** — 안 밀면 칼이 닿기 전에 바닥이 터진다.

| | FlashCore | FlashGold | ShockRing | GroundDisc | RockChunks | ImpactDust |
|---|---|---|---|---|---|---|
| 착지 0.148초(원래 빛기둥) | 0.15 | 0.15 | 0.15 | 0.17 | 0.16 | 0.19 |
| 착지 0.30초 | 0.30 | 0.30 | 0.30 | 0.32 | 0.31 | 0.34 |
| **착지 0.15초 (현재)** | **0.15** | **0.15** | **0.15** | **0.17** | **0.16** | **0.19** |

`MotesSpiral` / `MotesRise` 는 **안 민다** — 낙하 전부터 바닥이 기운다는 읽힘이 오히려 좋다.

`FX_Lightening_Strike_Entry.computedDuration` = **2.2**
(최장은 `HolySword` 자신 = `lengthInSec 0.05 + startDelay 0 + startLifetime 2.15`).
`EffectLifetime.SystemLifetime` 의 식(`duration + startDelay + startLifetime`)과 동일하게 계산했다.
🔴 낙하 시간을 줄여도 **엔트리 수명은 거의 안 변한다** — 칼의 `startLifetime` 이 지배하기 때문이다.

**🔴 트레일 Y 위치 — Unity 에 오프셋 필드가 없다** (2026-09-30)

Trails 모듈에는 위치 항목이 **아예 없다**(mode/ratio/lifetime/minVertexDistance/textureMode/
textureScale/ribbonCount/shadowBias/worldSpace/dieWithParticles/sizeAffects*/inheritParticleColor/
generateLightingData/split*/attach*/colorOverLifetime/widthOverTrail/colorOverTrail 가 전부).
**트레일은 파티클 중심에 고정**이다.

그런데 이 에셋의 메쉬 원점은 **그립 속**(손잡이 끝에서 +0.152 m)이고 `startRotationX 180°` 로
뒤집혀 있다. 그래서 파티클 중심은 **폼멜 꼭대기에서 0.76 m 아래**, 칼날은 거기서 5.24 m 더
아래로 뻗는다 — 즉 트레일이 **손잡이 근처에서** 뿜어져 나온다.

내리려면 **두 노브를 같이** 움직여야 한다:

```
D = 트레일을 칼끝 쪽으로 내릴 거리(m)
이미터 Y        -= D          ← 파티클(=트레일)이 D 내려간다
렌더러 pivot.y   = D / startSize   ← 메쉬만 D 다시 올려 칼은 제자리

현재(사용자 튜닝 후): 이미터 Y **15**,  pivot.y **-0.16**   (startSize 5)
```

⚠️ 제안값은 이미터 13.8 / pivot +0.44 였으나 **사용자가 Play 로 보고 위 값으로 맞췄다.**
pivot 부호는 180° 뒤집힘과 pivot 규약이 겹쳐 정적으로 확정할 수 없었던 부분이다 —
**지금 값이 정답이다. 산술로 되돌리지 말 것.**

**셰이더 구동 입력** — ColorModule 이 셰이더를 먹인다.
`color.a` = 존재량(1 온전 → 0 완전히 흩어짐), `color.rgb` = 등장 플래시 틴트.
알파 키 3개: 1@0 → 1@0.814 → 0@1.0 (= 마지막 0.4초에 디졸브).

**트레일** — TrailModule on, `lifetime` 0.12(×2.15s = 0.26초), `sizeAffectsWidth` off,
`widthOverTrail` 0.9 m(= 날 폭), `colorOverTrail` 꼬리로 갈수록 투명.
머티리얼은 `m_Materials[1]` 에 **`HolySword_Trail.mat`**
(`Eric/URP Particles/Additive` + `t_trail01.png`, 틴트 `(0.62,0.78,1)`, Emission 3).

🔴 **처음엔 `Mat_fx_HCFX_Twinkle_01_add` 를 물렸다가 "트레일이 아예 안 보인다"로 반려됐다 (2026-09-30).**
"같은 프리팹의 Motes 가 이미 쓰니 안전하다"는 이유로 골랐는데, **텍스처를 안 봤다.**
그 머티리얼의 그림은 `Tex_fx_HCFX_Twinkle_01.tga` = **256×128 에 네 갈래 반짝이 별 2개**,
알파>8 인 픽셀이 **18.9%** 뿐이다. `textureMode: 0`(Stretch)은 이 아틀라스를 12 m 트레일에
**딱 한 번** 늘려 입히므로 길이 방향엔 별 두 개의 흐릿한 자국만 남고, 폭 방향은 별의
투명한 위아래에 걸린다. 애디티브라 검정은 그대로 안 보임이다.
**교훈: 트레일 머티리얼은 "가로로 긴 스트릭" 텍스처여야 한다.** 빌보드에서 예쁜 스프라이트는
트레일에서 정확히 반대로 동작한다. `m_Materials[1]` 이 트레일 슬롯인 것은 프로젝트 내
트레일 켜진 프리팹 8개로 확인했다.

### 착지 후 연출 2종 — `SwordGlow` · `SwordTwinkle` (2026-09-30 · 그릴 13문항 완료 · **구현 완료 ✅ · Play 검증 대기**)

칼이 꽂힌 뒤 「칼 중심의 은은한 원형 glow」 + 「칼 주위에서 제자리 반짝임」을 얹는다.
둘 다 **루트 직속**이다 — `Impact` 밑은 전부 착지 *순간* 연출이고 이 둘은 *그 뒤로 지속*이라 성격이 다르다.

**기준 타임라인** (칼 착지 0.150초 · 칼 수명 2.15초 · 디졸브 1.75~2.15초 · 엔트리 2.2초)

| | |
|---|---|
| 칼 지상 높이 | **4.56 m** (폼멜 꼭대기). ⚠️ pivot 부호에 따라 2.96 m 일 수도 있다 — 치수는 이 값 기준이며 어긋나면 배율만 줄이면 된다 |
| 가드 스팬 / 날 폭 | 2.0 m / 0.75 m |

#### 그릴에서 확정된 것

| # | 항목 | 결정 |
|---|---|---|
| 1 | glow 형태 | **빌보드 헤일로**(카메라를 보는 정원). 바닥 판은 `GroundDisc`·`ShockRing` 이 이미 1.7초까지 깔린다 |
| 2 | glow 리듬 | **등장 플래시 → 안정**. 셰이더 발광(그릴 Q14)과 같은 리듬이라 따로 놀지 않는다 |
| 3 | twinkle 볼륨 | **세로 실린더**. 구로 잡으면 6 m 칼의 위아래가 빈다 |
| 4 | twinkle 밀도 | **보통** — 14/s × 수명 0.3초 = 항상 4개 남짓 |
| 5 | 색 | glow = **차가운 백**(칼날과 통일) / twinkle = **금빛**(금 장식과 호응, 백색 위에서 점으로 뜬다) |
| 6 | glow 치수 | **지름 5 m, 중심 높이 2.3 m** — 칼 높이와 엇비슷해야 "칼이 머금은 빛"으로 읽힌다 |
| 7 | twinkle 볼륨 치수 | **반경 1.5 m × 높이 5.0 m**, 바닥에서 0.2 m 띄움 |
| 8 | twinkle 개당 | **0.15~0.4 m 랜덤**, **회전 없음(축 정렬)** — 렌즈 반짝임은 화면 축에 정렬된다 |
| 9 | twinkle 곡선 | **크기만** 0→최대→0. 애디티브라 크기가 곧 밝기다. 알파는 안 얹는다(뭉근해진다) |
| 10 | 시작 | glow **0.15초**(착지) / twinkle **0.4초** — 0.15~0.35초는 Flash·RockChunks·ImpactDust 가 동시에 터져 0.15 m 반짝임이 묻힌다 |
| 11 | 머티리얼 | **신규 0개.** glow = `Common_Glow3.mat`, twinkle = `twink_01.mat` 재사용 + **파티클 Start Color 로 틴트** |
| 12 | 밝기 기준 | glow 는 **칼보다 확실히 어둡게**. 형태는 칼이 책임지고 glow 는 "거기 있다"만 알린다 |
| 13 | 가려짐 | **보정 안 한다.** 15~20% 가려지는데, 가려졌다 나타나는 것이 칼의 두께를 알려 준다 |

#### 적용 수치

**`SwordGlow`** — 루트 직속, local `(0, 2.3, 0)`

```
lengthInSec 0.05 · looping 0 · playOnAwake 0
startDelay 0.15   startLifetime 2.0   startSpeed 0   startSize 5.0
startColor  차가운 백 HDR — 칼날 색조(0.62, 0.78, 1.0) × 2
Emission    rate 0, 버스트 1발 @ t=0        Shape  끔(원점에 1개)
Color over Lifetime 알파 = 플래시 후 안정 후 페이드
   1.00 @ 0      (착지 0.15초, 플래시)
   0.45 @ 0.10   (0.35초, 안정 수준으로 내려앉음)
   0.45 @ 0.80   (1.75초, 칼 디졸브 시작)
   0.00 @ 1.00   (2.15초)
Renderer  Billboard(0) · View(0) · Common_Glow3.mat
```

**`SwordTwinkle`** — 루트 직속, local `(0, 2.7, 0)` (= 0.2 + 5.0/2)

```
lengthInSec 1.35 (0.4 → 1.75초 방출) · looping 0 · playOnAwake 0
startDelay 0.4    startLifetime 0.3   startSpeed 0
startSize  0.15 ~ 0.4 랜덤            startRotation 0 (축 정렬)
startColor 금빛 HDR — 금 장식 색조(1.0, 0.82, 0.45) × 3
Emission   rate 14/s   →  라이브 14 × 0.3 = 4.2개
Shape      Box, scale (3.0, 5.0, 3.0)
Size over Lifetime  0 @ 0  →  1.0 @ 0.30  →  0 @ 1.0   (빠른 어택, 느린 감쇠)
Renderer   Billboard(0) · View(0) · twink_01.mat
```

🔴 **Shape 를 실린더가 아니라 Box 로 쓰는 이유.** 유니티 ShapeModule 에 **실린더가 없다.**
`ConeVolume` 의 angle 을 0 으로 두면 실린더가 되지만 원뿔 축을 +Y 로 돌리는 회전을
정적으로 검증할 수 없다(잘못되면 옆으로 누운 실린더가 된다). Box 3×5×3 은 회전이 필요 없고,
모서리가 반경 1.5 대신 2.12 까지 나가지만 **초당 14개 랜덤 배치에서는 사각형으로 안 읽힌다.**

#### 엔트리 수명 — 안 고쳤다 ✅

```
SwordGlow     0.15 + 0.05 + 2.00 = 2.200   ← HolySword 와 동률로 최장
HolySword     0.00 + 0.05 + 2.15 = 2.200
ShockRing     0.15 + 1.50 + 0.50 = 2.150
SwordTwinkle  0.40 + 1.35 + 0.30 = 2.050
```
`FX_Lightening_Strike_Entry.computedDuration` **2.2 유지** — 실측으로 확인했다.

#### 구현 후 실측값

| | SwordGlow | SwordTwinkle |
|---|---|---|
| Transform(local) | `(0, 2.3, 0)` | `(0, 2.7, 0)` |
| lengthInSec / startDelay | 0.05 / **0.15** | **1.35** / **0.4** |
| startLifetime / startSize | 2.0 / 5 | 0.3 / **0.15~0.4 랜덤** |
| startColor (HDR) | `(1.24, 1.56, 2)` 차가운 백 | `(3, 2.46, 1.35)` 금빛 |
| Emission | rate 0 + **버스트 1발** | **rate 14/s** + 버스트 0발 |
| 켜진 모듈 | Initial · Emission · **Color** | Initial · **Shape** · Emission · **Size** |
| Shape | 꺼짐 | **Box (3, 5, 3)** |
| 렌더러 | Billboard · View · `Common_Glow3` | Billboard · View · `twink_01` |
| maxNumParticles | 4 | 64 |

알파 곡선(glow): `1.0 @0.15초 → 0.45 @0.35초 → 0.45 @1.75초 → 0 @2.15초`
크기 곡선(twinkle): `0 → 1.0 @0.3 → 0`

🔴 버스트를 지우는 대신 **개수를 0으로** 뒀다(`countCurve.scalar: 0`).
`m_Bursts` 시퀀스를 통째로 들어내면 YAML 구조를 건드려야 하는데, 0발 버스트는 완전한 no-op 이다.

#### 구현 방식

🔴 **복제 원본을 `MotesSpiral` 에서 `HolySword` 로 바꿨다**(구현 중 판단).
`MotesSpiral` 은 **UVModule(스프라이트 시트)과 CustomDataModule 이 켜져 있어** 그대로 복제하면
텍스처가 잘려 나오고, 렌더러 버텍스 스트림에도 커스텀 데이터(`00010304052226`)가 물려 있었다.
반면 `HolySword` 는 **버스트 1발 · Shape 꺼짐 · UV 꺼짐 · ColorModule 켜짐** 이라 두 목표에 훨씬 가깝다.
되돌려야 했던 것은 `VelocityModule` / `TrailModule` / `rotation3D` 뿐이다.

실제 결과: 프리팹 **49,904 → 59,838줄**(+20%), 문서 44 → **52개**, fileID 중복 0.

#### 검증 항목

- [ ] glow 가 칼보다 **어둡다** — 칼 라인아트가 흰 원에 안 먹힌다
- [ ] 착지 순간 한 번 밝았다가 은은한 수준으로 내려앉는다
- [ ] twinkle 이 0.4초부터 보이고, **이동하지 않는다**(제자리에서 반짝였다 사라짐)
- [ ] twinkle 별 모양이 기울지 않는다(축 정렬)
- [ ] 동시에 4개 남짓 — 성기지도 촘촘하지도 않다
- [ ] 1.75초부터 둘 다 걷히고 2.2초에 깔끔히 끝난다(잔상·풀 고갈 없음)
- [ ] 🔴 **MPPM 클라 창**에서 같게 보인다
- [ ] 콘솔 경고 없음

#### 리스크

| | |
|---|---|
| 칼 지상 높이 | pivot 부호 미확정이라 4.56 m 가정. 2.96 m 였다면 glow 지름·실린더 높이를 약 0.65배로 줄여야 한다 |
| 프리팹 비대화 | +20%. 머지 충돌 시 손으로 풀기 어려워진다 |
| SVN | **신규 에셋 0개** ✅ — 프리팹 하나만 수정된다 |
| 재사용 머티리얼 | `Common_Glow3` · `twink_01` 은 다른 이펙트도 쓴다. **머티리얼 자체는 절대 수정하지 않는다** — 색·밝기는 전부 파티클 Start Color 로만 조절 |


## 검증 항목

- [ ] 지목한 좌표에 **칼끝이 정확히** 꽂힌다 (🔴 에셋 피벗이 그립 속이라 `pivot` 보정이 맞는지)
- [ ] 60° 카메라에서 **가드 윗면과 날 두께**가 보인다 — 이게 이 작업의 목적이다
- [ ] `startRotationY` 를 굴려 정면 각도를 맞출 수 있다
- [ ] 낙하 0.3초 뒤 **바닥에서 정확히 멈춘다**(뚫고 내려가거나 공중에 서지 않는다)
- [ ] 트레일이 낙하 구간에만 보이고 착지와 함께 걷힌다
- [ ] 발광 포인트가 멀리서도 읽힌다
- [ ] 2.15초에 깔끔히 사라진다(엔트리 수명과 어긋나지 않는다)
- [ ] 🔴 **MPPM 클라 창**에서 같게 보인다
- [ ] 콘솔 경고 없음 · 풀 고갈 없음

## 리스크

| | |
|---|---|
| 🔴 배치 | 에셋이 **git 에 6.3 MB** 로 들어간다. 팀 규칙은 "대용량 아트 = SVN"(`50.Art` 는 gitignore). **결정 필요** |
| 🔴 SVN | 셰이더·머티리얼·마스크 = **신규 3파일**이 `50.Art` 밑. 미등록 에셋이 머지로 날아간 전례 있음 → `svn add` 필수 |
| 피벗 | 에셋 피벗이 칼끝이 아니다. 렌더러 `pivot` 은 **파티클 크기 단위**라 `startSize` 를 바꾸면 같이 흔들린다 |
| 낙하 커브 | 위 ③. 높이·시간·커브가 물려 있다 |
| 데모 씬 | 에셋 데모 씬이 **Baked 라이팅 데이터**를 들고 온다. 빌드 씬 목록에 안 들어가게 할 것 |
| 맵 조명 | 커스텀 셰이더로 우회했지만 **근본 원인(프로브 없음·Baked 라이트)은 그대로**다. 경석 님 영역 |

---

# ⏸ 보류 = **23호 팔다리 전기 (돌진·레이지)** (2026-09-28 · 1~6단계 구현 완료 · **7단계(도구 실행)와 Play 검증 대기**)

> 작업 세션: **민경(Claude)**. 🔴 `TwentyThreeBoss.cs` 를 건드리므로 **경석 님 공유 필요**(AGENTS.md §3).
> 선례 = `GrabArmVFX`(잡기 팔 전기). 같은 `EffectPathPlayer` 를 팔다리 네 갈래로 늘린 것이다.

## 목표

보스 **몸통에서 손·발까지 이어지는 전기 흐름**을 붙이고, **일반 대쉬 공격**과 **레이지 돌진**에서 재생한다.

## 조사로 확정된 사실

| | |
|---|---|
| 도구 | `EffectPathPlayer` — "트랜스폼 배열을 따라 흐르는 펄스". 🔴 **경로는 하나만 받는다. 분기 불가** → 네 갈래 = 컴포넌트 4개 |
| 선례 | `GrabArmVFX` = `EffectPathPlayer`(`id: ArmElectric`, 3점, 0.3/0.3/pool 10). **애니 이벤트가 아니라 코드**가 켠다(`grabPulse` 필드) |
| 애니메이터 상태 | 일반 대쉬 = `DashAttack` / 레이지 = `Rage` (별개 클립) |
| 기존 훅 | 레이지는 `BeginRageDash` → `StartRageSmashClientRpc()` / `StopRageDash` → `Stop…` 쌍이 있다. **일반 대쉬는 연출 RPC 가 없다** |
| 리그 | 팔 `c_shoulder → forearm → hand` · 다리 `c_thigh_b → c_thigh_fk → c_leg_fk → leg_fk → foot` · 몸통 `c_spine_02.x` / `c_root_master.x` |
| 🔴 본 참조 | 중첩 FBX 인스턴스라 `--- !u!4 &<id> stripped` 스텁으로 들어간다. fileID 가 **이름 해시**라 텍스트로 지어낼 수 없다 |
| `EffectPathPlayer` API | `Id · IsEmitting · Play() · PlayOnce() · Stop()`. 🔴 **`SetScale` 이 없다**(`EffectSocketPlayer` 에는 있다) |
| `Stop()` | `!_emitting` 이면 즉시 반환 — 길목에서 무조건 불러도 안전 |
| `Play()` | `!_ready` 면 조용히 반환 — **경로가 2점 미만이면 아무 일도 안 일어난다** |

## 확정된 설계 (그릴 12문항 전부 합의)

| 항목 | 결정 |
|---|---|
| **경로** | 4갈래. 팔 `c_spine_02.x → c_shoulder.{l,r} → forearm.{l,r} → hand.{l,r}` / 다리 `c_root_master.x → c_thigh_b.{l,r} → c_leg_fk.{l,r} → foot.{l,r}` |
| **왜 시작점이 다른가** | 가슴에서 다리로 내려가면 몸통을 가로지르는 긴 구간이 생겨 관절 보간이 튄다 |
| **컴포넌트** | `TwentyThree/Effects/LimbElectricVFX_{ArmL,ArmR,LegL,LegR}`, `id` 비움(코드 전용) |
| **수치** | travelTime `0.25` / interval `0.1` / pulsePoolSize `6` / scale `1` |
| **왜 interval < travelTime** | 펄스를 **겹치게** 해야 "띄엄띄엄 지나간다"가 아니라 "계속 흐른다"로 읽힌다 |
| **엔트리** | `FX_Limb_Electric_Entry` 신규. 파트 프리팹은 그랩과 동일(`b0551a23…f02`), outroDuration `0.6`, **prewarmCount `24`**(4갈래×6), maxActiveWarn `40` |
| **왜 엔트리를 나누나** | 보기엔 같아도 그랩은 1갈래·이번은 4갈래라 prewarm 수요가 다르다. 공유하면 튜닝이 서로를 흔든다 |
| **카탈로그** | 새 헤더 `보스 — 돌진 전기` 밑에 `Dash_LimbElectric` |
| **보스 필드** | `[SerializeField] EffectPathPlayer[] limbPulses` **1개** (경석 님 파일의 표면 최소화) |
| **켜기** | `StartAttack()` switch — `case Dash:` · `case RageDash:` 각 한 줄. **선딜부터** |
| **끄기** | `FinishChain()` + `AbortAttackChain()` **두 길목** |
| **레이지 배율** | `EffectPathPlayer.SetScale(float)` 을 추가하고 레이지만 `1.6` |
| **본 배선** | `Effects/Editor/EffectSystemSetup.cs` 에 "팔다리 경로 채우기" 메뉴 추가 |

### 설계에서 특별히 짚은 것

**① 끄는 자리를 열거하지 않는다.** 그랩은 `StopGrabPulseClientRpc()` 를 네 군데에 흩뿌려 놓고
주석이 "끄는 곳은 넷이다 — 헛잡기 경로가 특히 중요하다"고 경고한다. 돌진은 그럴 필요가 없다:
정상 종료 `FinishChain()` · 비정상 `AbortAttackChain()`(카운터·그로기·사망·타임아웃) 둘뿐이다.
**덤으로 일반 대쉬와 레이지가 같은 두 곳으로 끝나므로 Stop 한 쌍이 둘 다 커버한다.**

**② 레이지 진입점이 둘인데 저절로 풀린다.** 일반 선택과 차징 완료(`StartRageAfterCharge`) 두 경로가
있지만, 후자도 결국 `StartAttack()` 을 다시 부른다. **switch 한 곳이면 둘 다 커버된다.**

**③ 선딜부터 켜는 근거는 그랩 주석에 이미 있다.**
> 팔 전기는 **잡기 판정보다 먼저** 흐른다 — 판정(AcquireGrab)은 히트 프레임이라 거기서 켜면
> 팔을 뻗는 동안 아무 예고가 없다.

돌진도 같다. `BeginDash()` 는 클립 0.57(실제 돌진 시작)에 불리므로 거기서 켜면 선딜이 비어 있다.
⚠️ 다만 **선딜 정보가 하나 늘어나는 건 난이도 변경**이다 — 경석 님 확인 대상.

**④ 애니메이션 이벤트를 쓰지 않는 이유.** 갈래가 4개면 id 도 4개라 클립마다 이벤트가 8개가 된다.
게다가 이 클립들은 SVN 아트 FBX 이고, 같은 세션에서 `JumpLanding` 이벤트가 `time: 0` 에 박혀
**조용히 안 뜨던** 사고를 이미 겪었다.

**⑤ 본 참조를 손으로 끌지 않는 이유.** 4갈래 × 4점 = 16개다. 레포에 이미 같은 패턴이 있고
(`"Tools 의 잡기소켓 저작 도구가 채워 준다"`), 이름 기반이라 **리깅이 바뀌어도 다시 돌리면 그만**이다.

## 구현 단계

1. **`EffectPathPlayer.SetScale(float)`** 추가 — `EffectSocketPlayer` 와 같은 3줄.
   ⚠️ 주석에 "재생 중에 부르면 이번 재생에 반영되지 않는다(배율은 대출 시점 확정)"를 남긴다.
2. **`FX_Limb_Electric_Entry.asset`** 신규 + `.meta`(guid 고정).
3. **`EffectCatalog`** — `Dash_LimbElectric` 프로퍼티 + 에셋 등록.
4. **`EffectSystemSetup.cs`** — "23호 팔다리 전기 경로 채우기" 메뉴. 본 이름으로 4갈래 자동 배선.
5. **`TwentyThree.prefab`** — `LimbElectricVFX_*` 4개 생성, `EffectPathPlayer` 붙이고 수치 저작.
   🔴 Unity 닫고. 경로는 비워 두고 4단계 도구로 채운다.
6. **`TwentyThreeBoss.cs`** 🔴 경석 — 필드 1 + switch 2줄 + RPC 2개 + 길목 2줄 (총 7줄쯤).
7. 검증.

## 구현 결과 (2026-09-28)

모두 완료. 계획에서 **한 가지 벗어났다**: 4단계를 `EffectSystemSetup.cs` 에 얹지 않고
**`Effects/Editor/LimbElectricPathAuthoring.cs` 새 파일**로 뺐다 — `EffectSystemSetup` 은 자기 주석에
"Effect System v1의 에디터 진입점 **두 가지**(기본 에셋 생성 · 스모크 테스트)"라고 적혀 있어서
보스 본 배선을 섞으면 그 계약이 깨진다. 레포의 `*Authoring.cs` 관례(`CrateAuthoringTool`,
`BossRoomAuthoring`, `PlayerInterruptSkillAuthoring`)와도 이쪽이 맞는다.

| 파일 | 내용 |
|---|---|
| `Effects/EffectPathPlayer.cs` | `SetScale(float)` 추가 (Play 전 호출 경고 주석 포함) |
| `50.Art/VFX/Common/Boss/FX_Limb_Electric_Entry.asset` 🆕 | guid `450273ae…`, prewarm 24 / maxActiveWarn 40 / outro 0.6 |
| `Effects/EffectCatalog.cs` + `.asset` | `보스 — 돌진 전기` 헤더 + `Dash_LimbElectric` |
| `Effects/Editor/LimbElectricPathAuthoring.cs` 🆕 | `Tools/Effects/23호 팔다리 전기 경로 채우기` |
| `2.Prefabs/Monster/Boss/TwentyThree.prefab` | `LimbElectricVFX_{ArmL,ArmR,LegL,LegR}` 4개 (0.25/0.1/pool 6), `limbPulses` 배선 |
| `Monster/Boss/TwentyThreeBoss.cs` 🔴경석 | 필드 1 + switch 2곳 + RPC 2개 + 길목 2곳 |

검증: 앵커 105 / dangling 0 · 카탈로그 프로퍼티 43 = 필드 43 · 줄바꿈·인코딩 유지.

🔴 **경로는 아직 비어 있다(`path: []`).** `Play()` 는 `_ready` 가 아니면 조용히 반환하므로
7단계(도구 1클릭) 전에는 **아무 일도 일어나지 않는다** — "안 나온다"로 오진하지 말 것.

## 검증 항목

- [ ] 일반 대쉬: **선딜부터** 네 갈래가 흐르고, 돌진이 끝나면 걷힌다
- [ ] 레이지: 차징 완료 경로로 들어가도 켜지고, **연타 사이에 끊기지 않는다**
- [ ] 레이지가 일반 대쉬보다 **굵다**(scale 1.6)
- [ ] 돌진 중 카운터/그로기/사망 → `AbortAttackChain` 으로 전기가 남지 않는다
- [ ] 체인 타임아웃(안전망) 경로에서도 안 남는다
- [ ] 🔴 **MPPM 클라 창**에서 보스가 이동 중일 때 펄스가 몸에 붙어 있다
- [ ] 그랩 팔 전기(`GrabArmVFX`)가 **회귀하지 않았다** — 엔트리를 나눴으므로 영향 없어야 한다
- [ ] 콘솔에 `EffectPathPlayer` 경고 없음 · 풀 고갈 경고 없음

## 리스크

| | |
|---|---|
| 🔴 경석 님 파일 | `TwentyThreeBoss.cs` 7줄. 선딜 노출은 **난이도 변경**이라 별도 확인 |
| 본 참조 유실 | 리깅에서 본 이름이 바뀌면 끊긴다 → 4단계 도구를 다시 돌리면 복구 |
| 펄스 과다 | 4갈래 × pool 6 = 동시 24. 프레임 문제가 보이면 interval 을 먼저 올린다 |
| SVN | 새 엔트리 2파일(`50.Art`)은 `svn add` 필요 — 이번 세션에 미등록 에셋이 머지로 날아간 전례 있음 |

---

