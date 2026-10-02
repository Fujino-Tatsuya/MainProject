# PLAN-wall-modules.md — 벽 모듈 메시 정렬 (4m 그리드 스냅)

> 상태: **반영 완료** (2026-10-02 · Claude, 사용자 단계별 검수) — git `2ddf2838`(development `578569f6` 에 병합) · SVN r363. 남은 것: Play/빌드 확인. 사용자 승인: 방식 1(메시 재익스포트) · 코너 B(끝단 연장) · 코너 ①(그리드 정렬).

## 문제
`Assets/50.Art/Environment/Prefabs/Layouts/wall` 벽들이 서로 안 맞아 틈이 보인다. 실측(`bossroom.prefab`):
- 지상층: 1.4cm 틈 3곳 · 안쪽 면 단차 최대 2cm · 안쪽 27.370 × 27.355 (1.5cm 비정사각).
- 지하층: 틈 없음 · 27.579 정사각.
- 원인: 메시 피벗이 중앙이 아님(창문벽 +1.75/+1.9cm, 기본벽 −1.05cm) · 지상 코너벽 3.799m · 배치를 손으로 보정.

## 결정
1. **모든 벽 메시(필로티·복도벽 제외)**: 피벗 = 메시 XZ 중앙, 길이 정확히 4.000m. 두께는 유지(지상 1.268 / 지하 1.065).
   높이 기준은 원본 유지(지상 = 바닥 y0, 지하 = 윗면 y0).
2. **지상 코너벽**: 3.799 → 4.000m 는 **B(끝단 연장)** — 필로티에 묻히는 로컬 −Z 끝 정점 44개만 20.07cm 이동.
   벽돌·UV 원본 동일, 벽 전체에 걸친 긴 띠 면 30개만 6% 늘어남(사용자 승인).
3. **복도벽(`Wall_brick_basic_hallway_…`)**: 5.14m 반쪽 모듈을 미러로 맞대는 구조, 피벗 끝 = 복도 중앙. 방 쪽 끝 1.261m 는 **두께 절반짜리 맞물림 블록**(나머지 절반은 방 벽이 채움).
   방이 줄어 복도가 끝마다 0.345m 길어져, 최종적으로 **메시 몸통만 연속 스케일(×1.0896)·끝 블록은 강체 이동** — 5.1132 → 5.4582m. 벽돌은 몸통만 ~9% 길어짐.
   상단 빔은 문 H빔(벽보다 20cm 얇고 방 안쪽 면에 붙음)까지 닿도록 방 쪽 캡을 6.64cm 연장.
   (인스턴스 Z 스케일 1.067 은 끝 블록까지 늘려 방 벽 바깥면 앞 7.7cm 틈을 만들어 기각, 끝단 정점 연장은 끝 블록 33% 늘어나 기각)
4. **필로티 메시는 수정 안 함**(1.554 × 1.563) — 위치만 맞춘다.
5. **코너 조립(①)**: COM 피벗 = 두 벽 중심선 교차점 = 4m 그리드 교차점. 두 날개는 교차점에서 0~4m.
   필로티 바깥면 = 지상 벽 바깥면(교차점에서 −0.6341). 지하 필로티도 같은 바깥면(위아래 정렬).
   → bossroom 안쪽 27.37 → **26.73m**(변당 30cm 축소). NavMesh 여백·`InvisibleBoundaries` 재확인 필요.

## 단계 (각 단계 사용자 검수 후 다음)
- [x] 창문벽(지상) · 기본벽(지상) 변환 — fileID 유지, 정점·UV 일치 확인
- [x] 지상 코너벽 B 변환
- [x] 지하 기본벽 · 지하 코너벽 변환
- [x] COM 지상/지하 배치안 → 미리보기 프리팹으로 검수
- [x] 🔴 Unity 끄고 FBX 교체(SVN) → `.meta` guid 확인
- [x] COM 프리팹 갱신(SVN) — 자식 위치: 날개 (0,0,-2) r180 · (2,0,0) r270 미러Z · 필로티 지상 (3.3707,0,-1.8398) / 지하 (1.8718,0,-1.873), 루트 0
- [x] bossroom 재배치 — 벽 중심선 ±14, 직선 4m 스냅, COM (±14,±14), 문·서쪽 파이프 동반 이동, `InvisibleBoundaries` 안쪽 면 ±13.3659(사용자 승인). 실측: 틈 0 · 정사각 지상 26.7318 / 지하 26.9354
- [x] bossroom `NavMeshMargin` 재생성(`Build Boss Room NavMesh Margin`) — 띠 ±12.866, 길이 26.73 → NavMesh 가장자리 벽에서 1.5m 복원. 🔴 Play 에서 `[23호] NavMesh 여유` ≈1.5 확인 필요
- [x] COM 프리팹 자식 y 원복(지상 날개 +0.6mm · 지하 +5mm — 탑면 z-fighting·지하 코너 5mm 꺼짐 방지). SVN diff 는 x/z 만
- [x] `Level_wall_hallway_tutorial` 재배치(사용자 승인) — 방 5개 변당 0.32m 축소(중심 이동 ≤6cm, 복도 4개 길이 9.648 동일), 복도벽 16개는 처음 `scale.z ×1.06747` → 끝 틈(7.7cm) 발견 후 **메시 몸통 연장 FBX 로 교체하고 스케일 ±1 복원**, 복도 지하 벽 16개 길이 스케일, 직선 179·COM 40·H빔 8·복도 바닥 4·창 캡·투명화 존 9 이동 → `Tutorial/1. Add Wall Colliders` 로 콜라이더 재생성. 실측: 틈 0 · 면 단차 ≤0.1mm · 콜라이더=메시
- [x] **복도–스테이지 연결부 정렬(사용자 요청)** — 존 5개를 "존 바닥 끝 = 벽 중심선"(바닥 타일 중심 = 새 방 중심)으로, 복도 바닥 4개를 벽 중심선~중심선(×1.005)·슬롯 중심으로. `StageTutorial.prefab` 슬롯+저장 위치 · `TutorialStageAuthoring.Slots` · `all_mesh.unity` 존 5개 · hallway 복도 바닥. Unity 종료 상태에서 텍스트 수정 — 🔴 Unity 켠 뒤 실측 필요
- [x] 복도벽 FBX 몸통 연장 교체(SVN, Unity 종료 상태) — fileID 유지, 16개 모두 방 안쪽 면까지·끝 블록이 방 벽 두께와 7mm 겹침(원 설계와 같음), 콜라이더 재생성·일치 확인, 바깥 모서리 틈 없음(장면 확인)
- [x] 복도벽 상단 빔 연장(사용자 요청) — 방 쪽 끝 캡(볼트 12개 포함)과 빔 끝을 +6.64cm, 빔·캡 +2mm 올림(방 벽 윗면과 같은 높이로 겹쳐 깜빡이는 것 방지). 문 H빔(`Wall_Hbeam_1stack`)의 복도 쪽 면과 16곳 모두 2.8~3.1mm 겹침, 콜라이더 재생성·일치
- [x] 지하 벽 이음새 실금(사용자 발견) — 지하 기본벽·지하 코너벽의 기둥 반쪽이 메시 경계보다 0.7cm 짧아(원래 4.0069m 로 겹쳐 가려졌음) 4m 맞춤 후 이음새마다 세로 실금. 큰 부품(기둥 판·가로 띠) 끝 정점만 끝면 ±2.0005m 로 이동(최대 0.8cm, 이음새 1mm 겹침), 볼트·브래킷은 그대로. 메시 fileID 유지, 튜토리얼 콜라이더 재생성. 지상 벽 끝면은 1mm 이내라 해당 없음
- [x] 지상 벽 이음새 실금(사용자 발견) — 끝면이 경계와 딱 맞닿아(겹침 0) 이음새에 점선 실금. 지상 기본벽·창문벽·코너벽의 큰 부품 끝 정점(끝에서 0.2cm 이내)을 ±2.001m 로(최대 0.29cm 이동, 이음새 2mm 겹침). 볼트 등 작은 부품·면 모양 변화 없음. fileID 유지, 튜토리얼 콜라이더 재생성
- [x] 방 벽–복도벽 연결부 실금(사용자 발견) — 복도벽 끝을 방 벽 안쪽 면 경계에 정확히 맞췄더니 끝 블록 앞면이 방 벽 앞면보다 7.4mm(벽돌 판 구간 12.3mm) 앞으로 나와, 두 메시 모두 끝이 열린 껍데기라 틈 사이로 빛이 보임. 원래 설계(복도벽 끝이 경계보다 7.4mm 짧음)대로 **끝 블록에만 있는 부품 10개를 통째로 7.4mm 뒤로**(본체·상단 빔은 그대로). 띠 구간 단차 0, 벽돌 판 구간 4.9mm(원래 설계와 같음). 막음 면(cap) 시도는 두꺼운 어두운 띠로 보여 기각
- [x] 커밋·병합 — git `2ddf2838`(fix/stage261002) → development `578569f6`, SVN r363(벽 FBX 6 · 조립 코너 2). 문서 갱신은 fix/stage261002V2
- [ ] `all_mesh` — 별도 재배치 불필요(hallway 중첩만). 카탈로그 낱개 조각은 피벗 변화만큼 이동(선택 정리)
- [ ] Play 확인 — 보스전 `[23호] NavMesh 여유` ≈1.5(4방향) · 튜토리얼 문 넘어 몬스터 추적 여부 · 개발 빌드 문턱 콜라이더(비균일 스케일 convex) 에러 없음

## 존 소품 — 방이 좁아져 벽에 더 묻히는 것 (사용자 결정: 목록만, 레벨 담당이 판단)
교체 전에도 벽에 붙어 일부 묻힌 렌더러/콜라이더가 223개였다. 아래는 이번에 **새로** 묻히는 27개(존 루트 기준 최상위 소품).
부수 사항: 벽 붙임 조명 `LGT_WarmKey_02` 4~5개가 새로 벽 안쪽 면 뒤로 들어감. 문 바닥 구멍(존 바닥이 복도 문턱까지 안 닿음 — S01–M01 0.2m, M01–L01 0.5m, L01–BossEnter 0.47+0.16m)은 **기존부터** 있던 것(크기 ±5cm).

| 존 | 소품 | 새로 묻히는 깊이(최대) | 닿는 벽 |
|---|---|---|---|
| ZoneL_typeB | `PF_Prop_SM_Gen_Prop_Barrel_Metal_02_002` | 0.30 m | `walll_brick_cornerCOM_ground floor (14)` |
| ZoneL_typeB | `PF_Prop_SM_Prop_Pallet_01` | 0.28 m | `wall_brick_window_4stack_Ground floor (21)` |
| ZoneL_typeB | `PF_Prop_SM_Prop_CardboardBox_Stack_03` | 0.27 m | `walll_brick_cornerCOM_ground floor (14)` |
| ZoneL_typeB | `PF_Prop_PF_box2stack_002` | 0.27 m | `walll_brick_cornerCOM_ground floor (15)` |
| ZoneL_typeB | `PF_Prop_SM_Gen_Prop_Barrel_Metal_01_002` | 0.25 m | `walll_brick_cornerCOM_ground floor (14)` |
| ZoneL_typeB | `PF_Prop_SM_Gen_Prop_Barrel_Metal_02_006` | 0.22 m | `walll_brick_cornerCOM_ground floor (13)` |
| ZoneL_typeB | `PF_Prop_SM_Gen_Prop_Barrel_Metal_03_002` | 0.11 m | `walll_brick_cornerCOM_ground floor (15)` |
| ZoneL_typeB | `PF_Prop_SM_Prop_BarrelStack_01_001` | 0.01 m | `walll_brick_cornerCOM_ground floor (15)` |
| ZoneM_typeA | `PF_Prop_PF_manufacture01_003` | 0.25 m | `walll_brick_cornerCOM_ground floor (10)` |
| ZoneM_typeA | `PF_Prop_PF_manufacture01_002` | 0.17 m | `walll_brick_cornerCOM_ground floor (9)` |
| ZoneM_typeA | `wall_basic_082` | 0.10 m | `wall_brick_basic_4stack_basement floor (51)` |
| ZoneM_typeA | `wall_basic_086` | 0.10 m | `wall_brick_basic_4stack_basement floor (51)` |
| ZoneM_typeA | `wall_basic_086 (1)` | 0.10 m | `wall_brick_basic_4stack_basement floor (51)` |
| ZoneM_typeA | `wall_basic_086 (2)` | 0.10 m | `wall_brick_basic_4stack_basement floor (51)` |
| ZoneM_typeA | `wall_basic_077` | 0.09 m | `wall_brick_basic_4stack_basement floor (34)` |
| ZoneM_typeA | `wall_basic_079` | 0.09 m | `wall_brick_basic_4stack_basement floor (34)` |
| ZoneM_typeA | `wall_basic_079 (1)` | 0.09 m | `wall_brick_basic_4stack_basement floor (34)` |
| ZoneM_typeA | `wall_basic_079 (2)` | 0.09 m | `wall_brick_basic_4stack_basement floor (34)` |
| ZoneS_typeA | `PF_Prop_PF_box2stack_001` | 0.30 m | `walll_brick_cornerCOM_ground floor (6)` |
| ZoneS_typeA | `PF_Prop_PF_box_3stack_001` | 0.23 m | `walll_brick_cornerCOM_ground floor (6)` |
| ZoneS_typeA | `PF_Prop_SM_Prop_Pallet_01_001` | 0.09 m | `walll_brick_cornerCOM_ground floor (7)` |
| ZoneS_typeA | `PF_Prop_SM_Prop_Pallet_01_002` | 0.09 m | `walll_brick_cornerCOM_ground floor (7)` |
| ZoneS_typeStart | `PF_Prop_SM_Prop_Pallet_01_001` | 0.37 m | `walll_brick_cornerCOM_ground floor (1)` |
| ZoneS_typeStart | `PF_Prop_SM_Prop_Shipping_Container_01_001` | 0.33 m | `wall_brick_basic_4stack_Ground floor (25)` |
| ZoneS_typeStart | `PF_Prop_SM_Prop_CardboardBox_Stack_02` | 0.28 m | `walll_brick_cornerCOM_ground floor (3)` |
| ZoneS_typeStart | `PF_Prop_PF_box2stack` | 0.01 m | `walll_brick_cornerCOM_ground floor (1)` |
| ZoneS_typeStart | `PF_Prop_PF_box2stack_001` | 0.01 m | `wall_brick_basic_4stack_Ground floor (24)` |

### 벽 **바깥면 밖으로** 나오는 존 소품 27개 (2차 검증 — 사용자 결정: 목록만)
방이 안쪽으로 줄면서, 벽 두께 안에 걸쳐 있던 펜스·배관이 건물 밖 허공에 드러난다. 대부분 카메라 쪽(N/W) 페이드 벽이라 눈에 띈다.
교체 전에는 4개(최대 0.23m)만 밖으로 나와 있었다. 수치 = 벽 바깥면에서 튀어나온 거리.

| 방 · 벽 | 소품 (존 프리팹 기준 경로) | 밖으로 |
|---|---|---|
| M01 W (바깥면 x −10.813) | `Fence_MetalSheet_01_007/_008/_009` | 0.37 (전체 두께가 밖) |
| M01 W | `PF_manufacture01_003`: `object_pipe_tank (1)` 0.52 · `object_pipe_compressor` 0.32 · `pipe_L_curve (2)` 0.26 · `(1)` 0.10 · `(3)` 0.09 · `pipe_L_st (3)` 0.07 · `pipe_S_curve02 (1)/(2)` 0.001 | ≤0.52 |
| M01 W | `floor_bar_metal_008` (높이 0.5 바) | 0.27 |
| M01 N (z 20.789) | `Fence_MetalSheet_01_005/_006` | 0.26 |
| L01 W (x −30.813) | `Fence_MetalSheet_01_034/_035/_036/_038` | 0.19 |
| L01 N (z −28.859) | `_021/_024` 0.16 · `_022/_023` 0.10 · `_020` 0.02 | ≤0.16 |
| S01 N (z 50.437) | `_018` 0.08 · `_019` 0.05 | ≤0.08 |
| start E (x −19.193) | `_002/_003/_004` (페이드 안 하는 벽, `_002` 는 창 캡 안) | 0.10 |

➡ **적용됨(위 단계 "복도–스테이지 연결부 정렬")** — 존 바닥 중심 기준 정렬로 밖으로 나오는 것 28 → 1개(`PF_manufacture01_003`, 0.07m), 문 바닥 간격 4곳 모두 0. 그 대신 존이 최대 0.46m 움직여 반대편 벽 쪽이 더 묻힌다: 가장자리 펜스(`Fence_MetalSheet_*`)는 벽 두께 속에 완전히 숨고(의도에 가까움), `ZoneS_typeStart` 컨테이너 0.75m · `ZoneL_typeB` 팔레트 0.73m 가 원래보다 깊이 묻힌다.
**적용 좌표**(존 바닥 타일 중심 = 새 방 중심):
`StageTutorial.prefab` 슬롯 위치 = `Rotations[0].Position` — start (29.825725,−200.400275) · S01 (0.177525,−200.400275) · M01 (0.176525,−160.553075) · L01 (10.174525,−110.904875) · BossEnter (−29.470675,−101.103875).
`TutorialStageAuthoring.Slots`·`all_mesh.unity` 존 (회전 전) — start (−29.8257,−120.7997) · S01 (−0.1775,−120.7997) · M01 (−0.1765,−160.6469) · L01 (−10.1745,−210.2951) · BossEnter (29.4707,−220.0961).
복도 바닥 4개 `m_LocalScale.x` 1.0050208, 벽 중심선~중심선 · 슬롯 중심. 3자 일치(≤0.03mm)·문 바닥 구멍 0(렌더러·콜라이더) 교차 검증 완료.
(기각된 대안: 존 **원점** 기준 재정렬 — 문 구멍이 0.27m 남아서 바닥 기준으로 바꿈)

**존 정렬로 새로 생긴 것**
- 🔴 부서지는 상자(`BreakableCrate`) 3개가 벽 콜라이더에 크게 묻힘: `ZoneS_typeStart` `PF_box2stack`·`_001`(서쪽 벽, 노출 100%→47%), `ZoneL_typeB` `PF_box2stack_002`(남동 모서리, 74%→18%). 근접 판정은 벽 가림이 없어 여전히 부서지긴 함(시각 문제). `Stage1.prefab` 도 같은 존을 쓰므로 존 프리팹에서 옮길 땐 Stage1 배치도 확인.
- 문 바닥이 이어져 **방끼리 NavMesh 가 연결됨**(전엔 0.2~0.5m 구멍이 복셀 0.1667 보다 커서 끊겼을 가능성) → 몬스터가 문을 지나 따라올 수 있음(`leashRadius` 한도). Play 확인 필요.
- 빌드 전용 위험(낮음): 복도 바닥 루트의 비균일 스케일(1.005) 아래 90° 회전된 문턱 convex MeshCollider, 메시 Read/Write 꺼짐. 90° 라 전단 없음 → 아마 정상. 개발 빌드에서 문턱 위 걷기 + 로그 확인.

### 그 밖의 검증 메모
- H빔 6개가 옛 방 사각형 기준으로 옮겨져 최대 8.5cm 튀어나왔던 것 → 벽 안쪽 면에 맞춤(±0.25mm) ✅.
- BossEnter 문 아래 지하 벽 1장 없음(기존) — 존 정렬 후 문 바닥 틈은 0.1mm 라 문제 아님(보강은 선택).
- `TutorialStageAuthoring` 의 ㄱ자 코너 분기(`AddCornerColliders`)는 이제 안 쓰인다(코너가 곧은 4m 팔 + 정사각 필로티). 주석이 낡음.
- 콜라이더 도구 재실행은 333개 fileID 를 매번 새로 만든다(외부 참조 없음 — 무해, diff 만 큼).

## 리스크
- FBX 교체는 바이너리 + SVN → **Unity 종료 필수**. 메시 fileID(`fileIdsGeneration: 2`)는 오브젝트 이름 기반 — 이름 유지로 참조 보존(검증함).
- 배치가 손보정돼 있어, 교체 직후 기존 인스턴스가 1~30cm 움직인다 → 재배치 단계 전까지 커밋 금지.
- 작업 파일: 원본 백업·변환 스크립트(Blender 5.2 `-b -P`)는 세션 scratchpad, 검수본은 `Assets/_TmpWallCheck/`(작업 후 삭제).
