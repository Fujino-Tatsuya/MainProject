# handoff — 23호 전기 장판 연출 (예고 전기 튐 · 발동 천둥 번개 · 가장자리 전류 · 피격 감전)

> 2026-10-23 · 민경(Claude) · 브랜치 `feature/VFX` · 계획 [PLAN.md](../../PLAN.md) 「23호 전기 장판 VFX」
> **경석 님 영역은 스크립트 2곳에 "알리기"만 추가했다.** 장판 판정·타이밍·피해·표시 판(붉은 예고 판·발동 "16" 판)은 그대로다. §1 만 확인하면 된다.

## 1. 경석 영역 변경 (추가만)
- `1.Scripts/Monster/Boss/BossElectricFloor.cs`
  - 클라(전 피어) 이벤트 3개: `ClientWarnShown(mask, warnTime)` · `ClientFired(mask, vfxTime, lethal)` · `ClientCleared()`. 각각 `ClientShowWarn` · `ClientFire` · `ClientClear` 에서 부른다(표시 판을 그리기 직전·직후). 구독자는 연출 컴포넌트 하나뿐.
  - `ClientFire(mask, vfxTime, bool lethal = false)` — 인자 하나 추가(기본값이 있어 다른 호출부 영향 없음).
  - 서버 `Fire()` 가 `_fireLethal` 을 함께 넘긴다: `_boss.SendFloorFire(mask, _d.vfxDuration, _fireLethal)`.
- `1.Scripts/Monster/Boss/TwentyThreeBoss.cs`
  - `SendFloorFire(mask, vfxTime, bool lethal)` · `FloorFireClientRpc(mask, vfxTime, bool lethal)` — **RPC 인자 1개 추가**(즉사 방전인지). 판정과 무관, 연출용.
  - 🔴 RPC 모양이 바뀌었으니 호스트·클라는 같은 빌드여야 한다(지금 운영 방식 그대로).
- `2.Prefabs/Monster/Boss/TwentyThree.prefab` — `VFX` 에 **`BossElectricFloorVfx`**(MonoBehaviour, 네트워크 없음) 추가. 레이드(`TwentyThree_Raid`)는 Variant 라 같이 적용.
  - `TwentyThree_Solo`(단독 중간보스 복제본)에는 안 붙였다 — 송전기 전기줄(`BossChargeTetherView`)도 없는 복제본이고 씬에서 쓰이지 않는다. 필요하면 같은 컴포넌트를 붙이면 된다.
- 데이터 `electricVfxPrefab` 칸은 **비운 채** 둔다(연출은 새 컴포넌트가 전담).

## 2. 연출(민경 영역, 참고)
- 흐름(전 피어, `BossElectricFloorVfx`):
  - 예고 → 칸마다 바닥 전기 튐(`Electro_02` 4×4 조각) + 불티. 예고 시간에 맞춰 잦아지고 마지막 0.25초 몰아치기 + 바닥 살짝 밝아짐.
  - 발동 → 바닥 번쩍 + **갈라지는 번개**(`ElectricFloorBolts` — 코드로 짠 메시): 칸 가운데 근처에서 0.1초에 칸 끝까지 뻗고 0.07초마다 모양을 바꿔 다시 침. 칸 경계 위 끝점은 이웃 칸과 같은 자리 → 판 전체가 한 그물. + **가장자리 전류**(`ElectricFloorEdges` — 테두리 위 떨리는 선 + 빛 줄기 약 8m/s, 공유 변은 한 칸만 그림). 0.4초 뒤 0.25초 잔광.
  - 즉사 방전(④ 대폭발 · 북진 압박 시간 초과) → **번개 모양 수치만 다르다**(줄기·곁가지 많고 더 꺾임 — `Bolts` 의 `Lethal` 묶음). 추가 연출 없음.
  - 지우기(모드 전환·제압·기믹 종료·사망) → 즉시 끔.
  - 피격 → 발동 직후 0.3초 안에 **실제 피해 신호**(`Unit.ClientDamagedAttributed`, 공격자 = 플레이어 아님)가 온 플레이어 중 발이 터진 칸 안인 사람에게 `FX_ElectricFloorHit` 을 몸 가운데에 0.5초 붙인다. 상태이상 없음(기획 §6.3).
- 칸 연출은 씬 루트 `BossElectricFloorFx` 밑에 49개까지 만들어 재사용(장판 표시 `BossElectricFloorView` 와 같은 방식). 보스가 사라지면 같이 정리.
- 코드: 신규 `1.Scripts/Effects/BossElectricFloorVfx.cs` · `ElectricFloorTileFx.cs` · `ElectricFloorBolts.cs` · `ElectricFloorEdges.cs` · `ElectricFloorFxPool.cs`(뒤 넷은 MyProject 와 같은 guid).
- 에셋(🔴 SVN add): `50.Art/VFX/Common/Boss/ElectricFloor/`(폴더 · `FX_ElectricFloorTile` · `FX_ElectricFloorHit` · `FX_ElectricFloorHit_Entry`) · `_Materials/Boss/M_Floor_Arc`·`BoltCore`·`BoltGlow`·`EdgeFlow`·`EdgeGlow`·`Flash`·`Glow`·`Spark` · `Textures/T_FloorBoltCore`·`T_FloorBoltGlow`·`T_FloorEdgeFlow`·`T_FloorGlow`.
- 원본 제작: MyProject `Tools/VFX/Electric Floor (23호 전기 장판) 빌드` → `Export/Boss/ElectricFloor/` (미리보기 씬 `Scenes/ElectricFloorPreview` — 1 일반 · 2 기믹 A · 3 기믹 B · 4 레이드 방전 · 5 즉사 · H 피격).

## 3. 확인 부탁
- Play/MPPM(호스트·클라): 일반 교차·기믹 A/B·레이드 방전 → 예고 중 전기 튐 → 발동 순간 번개·가장자리 전류(붉은/발동 표시 판은 예전처럼) → 맞은 사람만 몸에 감전 · 즉사 방전은 번개가 빽빽 · 제압·기믹 종료·사망 때 바로 꺼짐 · 늦게 들어온 클라도 다음 발동부터 정상.
