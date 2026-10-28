# handoff — 23호 송전기(차징) 연출 교체 (전류·심지 발광 · 타격 스파크 · 방전 · 전기줄)

> 2026-10-22 · 민경(Claude) · 브랜치 `feature/VFX` · 계획 [PLAN.md](../../PLAN.md) 「23호 송전기(차징) 연출 교체」
> **경석 님 영역은 프리팹 2개에 컴포넌트 추가 + 스크립트 1곳(경고 1줄 삭제)뿐이다.** 송전기 HP·상승·판정·Rage/레이드 방전 진입 타이밍 그대로. §1 만 확인하면 된다.

## 1. 경석 영역 변경 (프리팹만, 추가만)
- `2.Prefabs/Environment/Layouts/Zones/bossroom.prefab` — `TwentyThreeCharger/Env_Mv_bosscharger_upper` 4개에 **`PylonChargeView`(NetworkBehaviour)** 추가.
  - `BossChargingPylon` 의 서버 전용 상태(`State`·`WasDestroyed`)를 **읽기만** 해서 `NetworkVariable<byte>` 하나(숨김·상승·Live·부서짐·내려감)로 복제한다 — 클라도 "부서져서 내려감"과 "시간 초과로 내려감"을 가를 수 있게.
  - 같은 NetworkObject 에 NetworkBehaviour 가 하나 늘었다(호스트·클라 같은 빌드면 문제없음).
  - **노랑 아웃라인 끔(10-22 민경 요청)** — 4개 모두 `BossChargingPylon.outlineMaterial` 을 비웠다. 아웃라인 전용 렌더러(`PylonOutline`)가 몸 연출(심지·코일 겹침)을 덮어 안 보였다. "Live = 때릴 수 있음" 표시는 이제 몸 연출(Live 때 밝아짐·전기줄 연결)이 맡는다.
    🔴 **10-23: `BossChargingPylon.BuildOutlineRenderers` 의 `outlineMaterial 이 비어 있어…` 경고를 지웠다**(민경 요청 — 경석 영역 스크립트 변경은 이 한 건뿐이다). 비움은 원래 필드 Tooltip 에 "비우면 아웃라인 없이 동작" 이라고 적힌 지원되는 설정이라, 경고가 자기 툴팁과 모순이었다. 동작 변경 0 — 비었을 때 바로 반환하는 흐름은 그대로고, 재질을 다시 넣으면 아웃라인도 예전처럼 돌아온다. 클라 콜라이더 토글(`ApplyOutline`)은 아웃라인 렌더러와 무관하게 그대로 돈다.
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

## 4. 10-27 추가(민경) — 01B 덮개 머리만 · 전기선 조명 · 차징 가슴 조명
> 계획 [PLAN.md](../../PLAN.md) 「송전탑 01B 덮개 머리만 · 전기선 라인 광원 · 차징 가슴 조명」. **경석 영역 파일 수정 없음**(`TwentyThree.prefab`·`bossroom.prefab` 무변경).
- `FX_PylonCharge_01B.prefab` — `Shell` 렌더러·`ShellArcs` 입자 모양 메시를 새 **`Models/BossCharger_01B/Pylon_01B_ShellHead.asset`**(꼭대기 원판 삼각형 512개만)으로 교체. 원래 `Pylon_01B_Shell.asset` 은 그대로. 원판 아래는 실제 모델의 원래 재질(`Generic_01_A`)이 보인다. Coil·Core 덮개는 그대로.
- `FX_PylonTether.prefab` 에 Light 2개 **`TetherLight_L`·`TetherLight_R`**(시안 · Intensity 30 · Range 10 · 그림자 Soft — 값은 각 Light 에 저장). `ElectricTether` 가 줄 가운데에서 양옆 1m(줄과 수직인 수평)에 두고, 평소 15% 로 은은하다가 불규칙하게 한쪽씩 번갈아 번쩍(가끔 같은 쪽 두세 번 떨듯이) · 과충전 땐 빨라지고 밝아짐 · 줄 가운데가 안 그려지면(뻗기 전·끊김) 꺼짐. 깜빡임 값 = `ElectricTether` 인스펙터 「조명」.
- 줄 재질 밝기 2배: `_Materials/Boss/M_Pylon_TetherGlow.mat`·`M_Pylon_TetherCore.mat` 의 Color(HDR).
- `BossChargeTetherView` — 가슴 점에 포인트 라이트 하나를 실행 중에 만든다. 차징 구슬 재생 중 0.6초에 걸쳐 밝아짐(60 · 범위 6m · 은은한 깜빡임) → 과충전은 첫 줄이 가슴에 닿는 순간 180 으로 번쩍 후 0.35초에 꺼짐 · 끊김(Abort)이나 신호 없는 정지는 0.2초에 꺼짐.
- 가슴 조명도 **그림자 Soft** · 깜빡임 0(차징 중 60 고정). 값은 `TwentyThree` → `VFX` 의 `BossChargeTetherView` 인스펙터 「가슴 조명」.
- 조명 최대 9개(송전기 4 × 2 + 가슴 1) 모두 그림자 — 포인트 라이트 그림자는 6면이라 `PC_RPAsset` 의 추가 조명 그림자 아틀라스(2048)를 나눠 쓴다. 많이 켜지면 URP 가 그림자 해상도를 자동으로 낮춘다.
- 🔴 SVN add: `Models/BossCharger_01B/Pylon_01B_ShellHead.asset`(+ `.meta`) · 수정 커밋: `Common/Boss/Pylon/FX_PylonCharge_01B.prefab` · `FX_PylonTether.prefab` · `_Materials/Boss/M_Pylon_TetherGlow.mat` · `M_Pylon_TetherCore.mat`.

## 5. 10-28 추가(민경) — 차징 중 전기 장판 칸 흐리게
- 🔴 **경석 영역 `1.Scripts/Monster/Boss/BossElectricFloor.cs` 변경(연출 전용, 추가만에 가까움)**:
  - 새 `public float ClientTileBrightness`(0~1, 기본 1) — 예고·발동 칸을 칠할 때 `_Tint` 알파에 곱한다. 판정·칸 범위·타이밍·네트워크 무관.
  - `PaintWarnTile` — 배율 < 1 이면 `EnemyC3Visuals.PaintLayer` 로 알파를 곱해 다시 칠함. `ClientFire`·`ClientTick` 의 `PaintActive` 알파에 배율을 곱함. 배율이 바뀌면 `ClientTick` 이 예고 칸을 다시 칠한다(`_brightnessDirty`).
- 값은 `BossChargeTetherView`(민경)가 정한다 — 차징 구슬 재생 중엔 0.4, 끝나면 1(둘 다 0.3초에 걸쳐). 인스펙터 「전기 장판 칸 흐리게(차징 중)」.
- 칸 이펙트 프리팹(전기 VFX)은 칸 머티리얼이 아니라 그대로.
- 10-28 추가: 차징 구슬 재생 중엔 **맵의 Directional Light 를 0.3초에 걸쳐 끄고**, 끝나면 원래 밝기로(`BossChargeTetherView` 「맵 해 끄기」 · 씬 파일 무수정 — 실행 중 `RenderSettings.sun` 또는 첫 Directional Light 를 찾는다. 보스가 꺼지면 원래대로 되돌린다).
