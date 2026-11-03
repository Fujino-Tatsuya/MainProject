# 핸드오프 — 부서지는 프롭(`Breakable`) · 컨테이너 6종 파괴 연출

> 2026-10-30 · 민경(Claude) · 브랜치 `feature/VFX` · 계획 [PLAN.md](../../PLAN.md) 맨 위(이 작업이 최신일 때).

## 무엇이 바뀌었나
- **이름 공용화** (guid 유지 — 프리팹 참조 그대로)
  | 옛 이름 | 새 이름 |
  |---|---|
  | `BreakableCrate` | `Breakable` |
  | `CrateRegistry` | `BreakableRegistry` |
  | `CrateBreakBroadcaster` (`MapNetworkSync.prefab`) | `BreakableBroadcaster` · RPC `BreakCrateRpc` → `BreakRpc` |
  | `CrateAuthoringTool` · 메뉴 `Tools/Crates/…` | `BreakableAuthoringTool` · `Tools/Breakables/…` |
  | `CrateId` · `MapContentSpawner.AssignCrateIds` · 로그 `[Crate]` | `BreakableId` · `AssignBreakableIds` · `[Breakable]` |
- **새로 부서지는 프롭 6종** — `2.Prefabs/Environment/Props/Containers/SM_Prop_{CardboardBox_Stack_01·02·03, Pallet_01, PalletStack_01·02}.prefab`. 존 프리팹(`ZoneL/M/S_type*`)에 놓인 것이 전부 자동으로 부서진다(부수면 차단 콜라이더도 사라져 길이 열린다). `Crate_01·02` 는 `PF_box2stack`·`PF_box_3stack` 의 부품이라 제외.
- **에셋 위치** — 전부 SVN `50.Art/VFX/Common/Breakable/`(옛 `VFX/Common/Box`). git `2.Prefabs/Box/` 의 `Frag_Box_01` 세트도 `Breakable/Box_01/` 로 옮겼다.

## 단일 프롭 구조 (6종 공통)
```
SM_Prop_X                 레이어 Default · 차단 BoxCollider(트리거 아님) — 그대로
├ Breakable               hp 1 · burstEntry = FX_Frag_X_Entry · pieces = [루트]
│                         onBrokenLocal → EffectSocketPlayer.PlayOnce
├ EffectSocketPlayer      effect = FX_Explosion_Basic_Entry
└ Hurtbox (자식)          레이어 EnemyHurtBox · 트리거 BoxCollider(메시 바운드) · Hurtbox → 루트 Breakable
```
- 루트의 차단 콜라이더를 트리거로 바꾸거나 레이어를 옮기면 플레이어가 통과한다 — 판정을 자식으로 내린 이유.
- 같은 구조의 프롭 추가: 프로젝트 창에서 프리팹 선택 → `Tools/Breakables/선택한 프리팹을 파괴 가능하게 만들기`(루트에 메시가 있으면 단일 프롭 모드). 그 뒤 `burstEntry`·`EffectSocketPlayer`·`onBrokenLocal` 은 손으로(위 표 참고).

## 파편
- 프롭마다 자기 FBX 로 `MeshFragmentSet` Bake — `Breakable/<프롭>/Frag_<프롭>.asset` · `FX_Frag_<프롭>_Burst.prefab` · `FX_Frag_<프롭>_Entry.asset`.
- 조각 수: `Pallet_01` 10 · `PalletStack_01·02` 16(두께 0.1) · 머티리얼 = 프롭 머티리얼. 버스트 값은 `FX_Frag_Box_01_Burst` 복사, `explosionRadius` 만 크기에 맞춤(1.5~1.75).
- **종이박스 더미 3종 = Blender 손작업 파편**(삼각기둥 방식은 박스가 나무 판자 조각처럼 보여 폐기). 박스마다 찢긴 골판지 판 — L자 모서리(벽 두 장, 90°보다 25~50° 벌어짐, 검은 테이프 띠·라벨 일부)·테이프 붙은 덮개·찢긴 판 + 팔레트 판자 3(하나는 부러뜨림). 두께 8mm, 원본 아틀라스 UV(골판지 띠 U≈0.0385·V 0.302~0.329 / 테이프 0.08 / 라벨 0.29~0.34) 그대로.
  | 프롭 | 박스 | 박스당 | 조각 |
  |---|---|---|---|
  | Stack_01 | 8 | 모서리·덮개 | 19 |
  | Stack_02 | 6 | 모서리·덮개·판 | 21 |
  | Stack_03 | 3 | 모서리 2·덮개 2·판 | 18 |
  - 원본: `D:\김민경\유니티\My project\Blender\Breakable_Cardboard.blend` — 텍스트 블록 `breakable_cardboard_lib`(판 만들기)·`breakable_cardboard_gen`(`generate(stack, plan, seed)`, 기울어진 박스는 큰 면 법선으로 축을 구해 맞춤). 컬렉션 `Frag_CardboardBox_Stack_0X` 를 FBX 로(축 -Z 앞·Y 위 · 변환 굽기 · 스케일 FBX_SCALE_ALL).
  - 반입: `Breakable/CardboardBox_Stack_0X/SM_Frag_CardboardBox_Stack_0X.fbx`(임포트 스케일 **0.98** = 원본 프롭 FBX 와 같음, 머티리얼 임포트 끔) → 프로젝트 창에서 선택 → `Assets/Effects/모델 파편으로 버스트 프리팹 굽기`(`FragmentBurstFromModel`) → 같은 폴더 `FX_Frag_…_Burst.prefab` 을 guid 유지로 덮어씀(엔트리 연결 그대로). 자식 하나 = 파편 하나, 원점 = 조각 중심.
- `MeshFragmentSet` 에 `areaWeighted`·`heightRange`·`outsideRangeFragments` 옵션이 있다(기본 꺼짐 — 작은 삼각형이 많은 메시에서 큰 면을 뽑을 때). 종이박스엔 시도했다가 위 Blender 방식으로 대체.
- 버스트 프리팹을 다시 구우면 기존 `FragmentBurstEffect` 튜닝(폭발력·반경)·머티리얼·Rigidbody 값을 이어받는다(`MeshFragmentSetEditor.SaveBurstPrefab`).
- 엔트리 6개 `EffectCatalog` 등록(`Frag_<프롭>`), `prewarmCount` 4.
- 다시 굽기: 원본 FBX Read/Write 켬 → 세트 인스펙터 `다시 Bake` → `버스트 프리팹` → Read/Write 원복. **FBX `.meta` 는 Unity 가 새 형식으로 재직렬화하므로 끝나면 SVN 원본으로 되돌릴 것**(이번엔 되돌렸다 — 6개 바이트 동일).

## Play 체크리스트 (민경)
- [ ] 솔로: 존에서 6종 각각 평타 한 대 → 파편 + 폭발 먼지, 프롭 사라짐
- [ ] 솔로: 아직 안 부순 프롭은 통과 불가(차단 유지)
- [ ] 솔로: 기존 `PF_box2stack`·`PF_box_3stack` 여전히 부서짐(이름 변경 회귀)
- [ ] MPPM 2인: 호스트가 부수면 클라에도 부서짐 · 클라가 부수면 호스트에도
- [ ] 콘솔에 `[Breakable]` 오류 없음(ID 0·중복·못 찾음)
- [ ] 파편 양·크기 체감 — 스택이 휑하거나 과하면 조각 수 조정

## 알려진 한계
- NavMesh 는 다시 굽지 않는다 — 부순 자리로 몬스터가 못 지나갈 수 있다(관찰 후 판단).
- 삼각형 샘플링 파편이라 스택 메시(삼각형 580~1176개)에서는 조각이 작게 보일 수 있다.
- 한 번 휘두르기에 Hurtbox 하나라 hp 는 한 번만 깎인다.

## SVN 업로드 목록 (민경)
- `Assets/50.Art/VFX/Common/Breakable/` — TortoiseSVN Rename(완료) 이후 **추가**: `Box_01/`(+`.meta`, git 에서 옮긴 `Frag_Box_01.asset`·`FX_Frag_Box_01_Burst.prefab` 포함, `FX_Frag_Box_01_Entry` 는 같은 폴더 안 이동), `CardboardBox_Stack_01~03/`, `Pallet_01/`, `PalletStack_01·02/` 각 폴더(+`.meta`) 전부.
- FBX `.meta` 변경 없음.

## git 커밋 대상
- `1.Scripts/Map/Breakable/*`(이름 변경) · `MapContentSpawner.cs` · `AssassinHitTargets.cs` · `EffectCatalog.cs` · `9.ScriptableObject/Effects/EffectCatalog.asset`
- `SM_Prop_*` 6종 · `PF_box2stack`·`PF_box_3stack`·`MapNetworkSync.prefab`·`0.Scenes/Debug/4.MapScene-TV3.unity`(클래스 식별자 한 줄)
- `2.Prefabs/Box/` 삭제 · 문서(`PLAN.md`·`CONTEXT.md`·이 파일)
