# handoff — 23호 점프 착지 균열 VFX 교체(FX_GroundBreak → FX_GroundBreak_Shock)

> 2026-10-27 · 민경(Claude) · 브랜치 `feature/VFX` · 계획 [PLAN.md](../../PLAN.md) 「23호 착지 균열 VFX 재제작」
> **경석 님 영역 변경 = 착지 균열 소켓의 배율 한 칸만.** 점프 판정·`jumpAoeRadius`·FSM·클립 이벤트·네트워크는 그대로다.

## 1. 경석 영역 변경
- `2.Prefabs/Monster/Boss/TwentyThree.prefab` — `GroundBreak` 소켓(EffectSocketPlayer, Id `GroundBreak`)의 **scale 3 → 1**.
  - 새 연출은 실제 크기(판정 원 5.25m)로 만들었다. 배율 3 이면 3배로 커지고, 바닥 타일 무늬 맞춤도 어긋난다.
  - `TwentyThree_Raid` 등 변형 프리팹은 이 칸을 덮어쓰지 않아서 함께 바뀐다.
  - effect(`FX_GroundBreak_Entry`)·socket·safetyTimeout(5)은 그대로.
- 🔴 `jumpAoeRadius`(5.25)를 바꾸면 알려 주세요 — 연출 반경이 판정 원에 맞춰져 있다.

## 2. 배선(민경 영역, 참고)
- `50.Art/VFX/Common/Boss/Jump/FX_GroundBreak_Entry.asset` — parts[0] 프리팹만 `FX_GroundBreak.prefab` → **`FX_GroundBreak_Shock.prefab`**. computedDuration 재계산 = 2.9초(옛 4.6초 — 풀 반납이 빨라진다).
- **옛 `FX_GroundBreak.prefab` 은 지우지 않고 그대로 둔다**(되돌릴 때 Entry 프리팹 칸만 다시 바꾸면 된다).
- 재생 시점은 그대로 — 착지 클립 `Boss_23_landingattack` 의 `PlayEffect("GroundBreak")`.

## 3. 새 파일(SVN add 필요 — 아트)
- `50.Art/VFX/Common/Boss/Jump/FX_GroundBreak_Shock.prefab`
- `50.Art/VFX/_Materials/GroundBreak/`(새 폴더) `M_GroundBreak_*` 18(판 위 금 `Root{Fill,Halo,Dark}` 포함)
- `50.Art/VFX/Textures/GroundBreak/`(새 폴더) `T_GroundBreak_*` 12(판 위 금 `Root{Fill,Halo,Dark}` 포함)
- `50.Art/VFX/Models/GroundBreak/`(새 폴더) `SM_GroundBreak_V0~V2`(깨진 바닥 판 패턴 3벌) · `_Rocks` · `_Field`(잔번개)
- 코드(git): `1.Scripts/Effects/GroundBreakFx.cs`
- 원본 제작: MyProject `Tools/VFX/Ground Break (23호 착지 균열) 빌드` → `Export/Boss/GroundBreak/` (미리보기 `Scenes/GroundBreakPreview`, 보스방 바닥 타일 위).

## 4. 연출 구성
- **깨진 바닥**: 착지점 둘레를 불규칙한 판 21~24장으로 나누고 판끼리 벌린다. 바깥 테두리는 각도마다 4.45~5.25m 로 삐뚤빼뚤. 틈 폭은 가운데 0.85m → 바깥 0.06m. 판 아래 깔린 시안·흰빛이 틈으로만 새어 나온다. 판은 1.5~3° 기울었다가 1.1~1.4초에 다시 평평해지고 숨는다.
- **판 위 금**: 정가운데에서 나무뿌리처럼 갈라지며 테두리까지 뻗는 빛나는 금. 판마다 잘려 각 판 자식 `Cracks` 로 붙어 판과 함께 기운다. 밝기는 `GroundBreakFx` 가 MaterialPropertyBlock 으로 움직인다(빛 0.75초 → 어두운 홈 → 1.1~1.4초 사라짐).
- **바닥 재질 = 보스방 바닥(`Generic_01_A.mat`)**. `GroundBreakFx` 가 재생 때 씬의 4m 돌 타일(`floor_stone_EXPORT`)을 찾아 판 윗면을 그 자리 타일 무늬로 다시 쓴다. 타일이 없는 곳의 판은 프리팹에 구운 비슷한 무늬를 쓰고 끝에 가라앉는다.
- 그 외: 섬광 · 빛 기둥 · 충격파 고리 · 틈 따라 전류 · 원 전체 잔번개(0~0.35초) · 돌 튐(착지 후 멈춰 있다가 균열과 함께 사라짐) · 먼지·바람 · 점광원.
- **재생마다 패턴 3벌 중 하나 + 무작위 방향.** 피어마다 따로 고른다(네트워크 동기화 없음 — 순수 연출).

## 5. 확인할 것
- 보스방에서 23호 점프 착지 — 판 무늬가 바닥 타일과 이어지는지, 판이 사라질 때 튀는 곳이 없는지.
- MPPM 2인 — 양쪽 다 재생되는지(패턴은 서로 달라도 정상).
- 보스방 밖(타일 없는 바닥)에서 착지해도 깨지지 않고 재생되는지(판이 가라앉는 대체 동작).
