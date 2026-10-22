# handoff — 거너 R 추적 레이저 연출 재제작 (장판 · 대상별 조준 · 위성 레이저)

> 2026-10-21 · 민경(Claude) · 브랜치 `feature/VFX` · 계획 [PLAN.md](../../PLAN.md) 「거너 R 추적 레이저 연출 재제작」
> **은희 님 영역(`GunnerTrackingLaser`)에 추가만 한 변경이 있다** — 판정·이동·수명·피해는 그대로다. §1 만 확인하면 된다.

## 1. 코어 변경 — `Player/Gunner/GunnerTrackingLaser.cs` (추가만)
- 필드 `GunnerTrackingLaserView view` 1개.
- `ServerInitialize` 끝: `view = GetComponent<GunnerTrackingLaserView>()` → `view.ServerConfigure(radius, damageInterval, enemyLayers)` (연출에 반경·간격·적 레이어를 넘김).
- `DamageTick` 끝: `landed` 가 있으면 `view.ServerStrike(landed)` (실제로 맞은 적 위치마다 레이저 연출).
- 둘 다 `view == null` 이면 아무 일도 안 한다. 피해 판정(`OverlapSphere` · `tickHits` · `RaiseServerAttackLanded`)은 손대지 않았다.
- 프리팹 `2.Prefabs/Player/Gunner/GunnerTrackingLaser.prefab`: 루트에 `GunnerTrackingLaserView`(**NetworkBehaviour** — NetworkVariable 3 + Reliable RPC 1) 추가, `Visual` 아래 중첩 `FX_GunnerUltLaser` 제거(`Visual` 은 빈 채로 남김 — `visual` 배율 코드는 그대로 돈다).

## 2. 연출(민경 영역, 참고)
- `GunnerTrackingLaserView` — 전 피어:
  - 장판 `FX_GunnerUlt_Zone2`(10-22 RipplePortal 기반으로 교체 — 같은 guid·엔트리 `FX_GunnerUlt_Zone_Entry` 그대로, 루프, 배율 반경×2) — 위치 고정으로 띄우고 매 프레임 옮긴다(추종으로 걸면 디스폰 즉시 회수돼 퇴장 연출이 없다). 디스폰 때 0.3초 줄어들며 퇴장.
  - 대상별 조준 `FX_GunnerUlt_Lock`(루프) — 각 피어가 매 프레임 서버와 같은 규칙(반경·적 레이어·살아 있는 비플레이어)으로 범위 안 적을 찾아 붙이고 뗀다. 원 진행도 = (지금 − 마지막 레이저) mod 간격 ÷ 간격 → `TelegraphRing.SetProgress`(0 = 막 쏜 직후로 원이 가장 큼, 1 = 발사 순간으로 가장 작음). 크기 = 몸 반경(NavMeshAgent → CapsuleCollider → 0.5)×2, 0.8~3.
  - 위성 레이저 `FX_GunnerUlt_Strike`(원샷) — `ServerStrike` RPC 받은 위치마다.
- 신규 `1.Scripts/Effects/TelegraphRing.cs`(MyProject 와 같은 guid `f4c85f3d…`). 카탈로그 `Gunner_R_Zone`·`_Lock`·`_Strike`(prewarm 등록).
- 에셋(🔴 SVN add): `50.Art/VFX/Common/Players/Player2/R_TrackingLaser/FX_GunnerUlt_*`(프리팹 3·엔트리 3) · `_Materials/Player2/`(폴더·재질 11 — 10-22 옛 장판 재질 4 빼고 `M_GUlt_ZoneRipple`·`ZoneCircle`·`ZoneLine` 추가) · `Textures/` `Mark_1`·`Mark_7`·`Ring_1`·`Ring_2`·`GausianNoise_3`·`Normal_8` · `Models/Cylinder_01.fbx` · 수정 `Scripts/Editor/GunnerUltSetup.cs`(View 가 있으면 옛 VFX 를 다시 넣지 않음). 옛 `FX_GunnerUltLaser.prefab` 은 파일만 남김.

## 3. 확인 부탁
- Play/MPPM(호스트·클라): R → 장판이 레이저 따라 이동 · 범위 안 적에 조준 표식 붙고 나가면 떨어짐 · 원이 가장 작아지는 순간 레이저 + 피해 숫자 · 6초 뒤 장판 줄어들며 퇴장 · 늦게 들어온 클라.
