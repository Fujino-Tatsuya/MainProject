# handoff — 23호 송전기(차징) 연출 교체 (전류·심지 발광 · 타격 스파크 · 방전 · 전기줄)

> 2026-10-22 · 민경(Claude) · 브랜치 `feature/VFX` · 계획 [PLAN.md](../../PLAN.md) 「23호 송전기(차징) 연출 교체」
> **경석 님 영역은 프리팹 2개에 컴포넌트만 더했다 — 스크립트 diff 0.** 송전기 HP·상승·판정·Rage/레이드 방전 진입 타이밍 그대로. §1 만 확인하면 된다.

## 1. 경석 영역 변경 (프리팹만, 추가만)
- `2.Prefabs/Environment/Layouts/Zones/bossroom.prefab` — `TwentyThreeCharger/Env_Mv_bosscharger_upper` 4개에 **`PylonChargeView`(NetworkBehaviour)** 추가.
  - `BossChargingPylon` 의 서버 전용 상태(`State`·`WasDestroyed`)를 **읽기만** 해서 `NetworkVariable<byte>` 하나(숨김·상승·Live·부서짐·내려감)로 복제한다 — 클라도 "부서져서 내려감"과 "시간 초과로 내려감"을 가를 수 있게.
  - 같은 NetworkObject 에 NetworkBehaviour 가 하나 늘었다(호스트·클라 같은 빌드면 문제없음).
  - **노랑 아웃라인 끔(10-22 민경 요청)** — 4개 모두 `BossChargingPylon.outlineMaterial` 을 비웠다. 아웃라인 전용 렌더러(`PylonOutline`)가 몸 연출(심지·코일 겹침)을 덮어 안 보였다. "Live = 때릴 수 있음" 표시는 이제 몸 연출(Live 때 밝아짐·전기줄 연결)이 맡는다.
    코드는 그대로라 Awake 때 `outlineMaterial 이 비어 있어…` 경고가 송전기마다 한 번 찍힌다 — 끄려면 경고를 지우거나 "의도적으로 끔" 옵션을 두는 건 경석 님 판단. 클라 콜라이더 토글(`ApplyOutline`)은 아웃라인 렌더러와 무관하게 그대로 돈다.
- `2.Prefabs/Monster/Boss/TwentyThree.prefab` — `VFX` 에 **`BossChargeTetherView`**(MonoBehaviour, 네트워크 없음) 추가, `chargeBall` = 기존 `VFX/ChargeBall`(EffectStagePlayer). 레이드(`TwentyThree_Raid`)는 Variant 라 같이 적용.
  - 가슴 점: 뼈 `spine_02.x` 를 이름으로 찾아 보스 기준 앞(+Z) 0.4m. `chest` 에 Transform 을 넣으면 그게 우선(프리팹에 소켓을 만들고 싶으면 여기).
- 시간 초과 신호는 보스가 이미 보내는 `EndChargeBallClientRpc(broken: false)` → `chargeBall.Stop()` 을 듣는다(시간 초과일 때만 Stop, 그 밖은 Abort). **이 규약(시간 초과 = Stop)을 바꾸면 과충전 연출이 엉뚱하게 나온다** — 바꿀 일이 있으면 알려 주세요.

## 2. 연출(민경 영역, 참고)
- 전 피어:
  - 송전기 몸 `FX_PylonCharge`(`PylonChargeVisual`) — 스폰 때 송전기 메시 자식으로 만든다(`NoHitFlash` — 피격 플래시·호버 아웃라인 제외). 상승 = 약하게 → Live = 1.8배 번쩍 후 유지 → 시간 초과·끊김 = 0.3초에 꺼짐 / 부서짐 = 즉시 꺼짐 + 방전 원샷.
    게임 카메라(위에서 60°)에서도 읽히게 송전기 전체 림 겹침(`Pylon_Shell`)·캡 위 후광·캡까지 전기 줄기를 더했다(10-22).
    심지·코일·전체 겹침 메시(`Models/Pylon_Core`·`Pylon_Coil`·`Pylon_Shell`)는 원본 메시에서 그 UV 칸 삼각형만 뽑은 것 — 원본 메시·공용 아틀라스 재질(`Generic_01_A`)은 안 건드린다.
  - 타격 — `Unit.ClientDamagedAttributed`(전 피어) 때마다 송전기마다 0.08초 간격으로: 송전기 전체 림(`Shell`)이 2.5배로 번쩍했다가 0.12초에 돌아옴(`PylonChargeVisual.Pulse`) + 심지 가운데서 스파크가 위쪽 반구(수평 위주)로 퍼짐. 맞은 방향은 안 본다(10-23 — 이전의 공격자 기준 표면 위치 계산은 지웠다). 드론 피해도 같다.
  - 전기줄 `FX_PylonTether`(`ElectricTether`) — Live 순간 송전기 꼭대기 → 가슴으로 뻗음(늦은 접속도 매 프레임 맞춤) · 부서짐 = 가운데서 끊겨 말려 들어감 · 시간 초과 = 과충전(빛 덩어리가 몰려 들어감) + 첫 줄이 닿을 때 가슴 큰 섬광 `FX_PylonOvercharge` 한 번 · 그 밖으로 내려감 = 0.2초 유예 뒤 조용히 사라짐(RPC·NetworkVariable 도착 순서 차이 흡수).
- 코드: 신규 `1.Scripts/Effects/PylonChargeView.cs`·`BossChargeTetherView.cs`·`PylonChargeVisual.cs`·`ElectricTether.cs`(뒤 둘은 MyProject 와 같은 guid) · `EffectStagePlayer` 에 `event Action<bool> Finished`(추가만 — 조용한 정리에서는 안 옴) · 카탈로그 `Pylon_HitSpark`·`Pylon_Discharge`·`Pylon_Overcharge`.
- 에셋(🔴 SVN add): `50.Art/VFX/Common/Boss/Pylon/`(폴더 · 프리팹 5 · 엔트리 3) · `_Materials/Boss/`(폴더 · 재질 18 — `M_Pylon_*` 17 + 팩 `Effect_44_Particle`) · `Textures/` `T_PylonCoreElectric`·`T_PylonFlowDash`·`T_PylonTetherGlow`·`T_PylonShellRim`·`spark_2`·`Electro_02` · `Textures/Noise/` `PerlinNoise_9`·`PerlinNoise_10` · `Textures/Normal/Normal_15` · `Models/Pylon_Core.asset`·`Pylon_Coil.asset`·`Pylon_Shell.asset`.
- 원본 제작: MyProject `Tools/VFX/Pylon Charge (23호 송전기) 빌드` → `Export/Boss/Charge/Pylon/` (미리보기 씬 `Scenes/PylonChargePreview`).

## 3. 확인 부탁
- Play/MPPM(호스트·클라): 송전기 차징 → 올라오며 약하게 → 다 올라오면 밝아지고 가슴으로 전기줄 · 타격마다 림 번쩍 + 심지에서 사방 스파크(맞은 방향 무관) · 하나 부수면 방전 + 그 줄 끊김 · 전부 부수면 구슬 깨짐 · 시간 초과면 과충전 섬광 → Rage(일반) / 바닥 방전(레이드) · 늦게 들어온 클라도 지금 상태대로.
