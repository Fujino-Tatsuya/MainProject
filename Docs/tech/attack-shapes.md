# 공격 판정 도형 (할 일 153)

> 판정 형상을 **프리팹 콜라이더 → 코드 계산 도형 클래스**로 옮긴다. 수치는 공격 데이터 SO 의 `AttackShapeSet` 필드(도형 `[SerializeReference]` 목록)에 두고 xlsx 로 튜닝한다.
> 코드: `Assets/1.Scripts/Unit/Weapon/AttackShapes/` · 테스트: 같은 폴더 `Editor/AttackShapeTests.cs`(`Tools/Tests/플레이어 EditMode 테스트 실행`).
> 🔴 **간파 판정 상자(`InterruptAttack` — 예측기·대쉬 간파가 씀)는 이 작업 전체에서 예외**다. 도형으로 옮기지 않는다.

## 1. 구조

| 타입 | 역할 |
|---|---|
| `AttackShape` (추상) | 도형 1개. 공통 칸 + 판정 `Contains`·최근접 점 `ClosestPoint`(도형 영역 3D, 도형별 `ClosestLocal`)·넓은 질의 상자 `GetBroadBox`·배율 `Scaled`·외곽선 `AppendOutlines`·검증 `Validate`. 순수 C#(Physics·MonoBehaviour 없음) |
| `AttackSectorShape` / `AttackCircleShape` / `AttackRectShape` / `AttackTriangleShape` | 도형 4종(`AttackShapes.cs`). 캡슐은 없다 |
| `AttackShapePose` | 기준 포즈 = 바닥 위치 + 바닥 평면 정면(정면 0 이면 +z) |
| `AttackShapeOrigin` | 기준점 종류 enum — `CasterAim`(시전자 발밑 + 조준 방향) / `Point`(스킬이 정한 지점 + 방향) |
| `AttackShapeSet` | 공격 하나 = 기준점 종류 + 도형 목록(**합집합**, 빼기 없음). SO 에 이 타입 필드를 둔다 |
| `AttackShapeQuery` | [서버] 질의 — 넓은 상자 Overlap → 몸 겹침(`Overlaps`) → 합집합 → 대상 1회 |
| `AttackShapeDrawing` | [에디터 전용, `#if UNITY_EDITOR`] 기즈모·Handles 그리기. 외곽선은 `AppendOutlines`(판정 수식) |
| `AttackShapeSetDrawer` | 인스펙터 '도형 추가' 메뉴(특성 정의의 '효과 추가'와 같은 이유 — `[SerializeReference]` 칸 종류 고르기) |

## 2. 도형 칸

로컬 바닥 평면: **x = 오른쪽, 정면 = 앞**, 원점 = 도형 포즈(기준점 + 오프셋). 단위 m·도. 경계는 포함(허용 오차 1e-4m).

**공통 칸**(모든 도형)

| 칸 | 의미 | 기본 |
|---|---|---|
| `forwardOffset` | 기준점에서 정면으로 옮김(음수 = 뒤) | 0 |
| `sideOffset` | 오른쪽으로 옮김(음수 = 왼쪽) | 0 |
| `yawOffset` | 옮긴 자리에서 도형을 돌림(+ = 위에서 볼 때 시계 방향 = 오른쪽) | 0 |
| `minHeight` / `maxHeight` | 판정 높이 범위(기준점 높이 기준) | -1 / 3 |

**도형별 칸**

| 도형 | 칸 | 안 판정 |
|---|---|---|
| 부채꼴 `AttackSectorShape` | `range`(바깥 반경), `angle`(전체 각, 360 = 원), `minOffset`(안쪽 빈 반경 — 도넛 부채꼴) | `minOffset ≤ 거리 ≤ range` 이고 정면과의 각 ≤ angle/2. 꼭짓점(거리 0)은 `minOffset = 0` 이면 안 |
| 원 `AttackCircleShape` | `radius`, `minRadius`(> 0 이면 도넛) | `minRadius ≤ 거리 ≤ radius` |
| 직사각형 `AttackRectShape` | `length`(앞으로), `width`(좌우 반씩), `backOffset`(기준점 뒤로 들어오는 길이) | `-backOffset ≤ 앞 ≤ length`, `|옆| ≤ width/2` |
| 삼각형 `AttackTriangleShape` | `length`(꼭짓점 → 밑변), `width`(끝 밑변 폭). 꼭짓점 = 도형 포즈 | `0 ≤ 앞 ≤ length`, `|옆| ≤ width/2 × 앞/length` |

## 3. 기준점

- `CasterAim` — 시전자 발밑 + **그 공격의 조준 방향**(몸 회전 아님 — 서버가 원격 오너 몸 회전을 늦게 받아도 같다. 어쌔신 평타 상자와 같은 원칙).
- `Point` — 스킬이 넘긴 지점 + 방향. 예: 진격의 충격 = Q 끝 지점 + 마지막 진행 방향.
- 코드는 `set.ResolvePose(casterPos, aimDir, point, pointDir)` 로 종류에 맞는 포즈를 얻는다. 쓰지 않는 쪽 인자는 아무 값이나 된다.

## 4. 피격 규칙(몸 겹침)·합집합·1회

규칙: **도형 영역(바닥 평면 도형 × 높이 범위의 3D 기둥)과 대상 몸이 조금이라도 겹치면 맞음** — 예전 프리팹 콜라이더 겹침과 같다.

`AttackShapeQuery.Collect(set, pose, layerMask, triggers, buffer, resolveTarget, seen, results)`:

1. 도형마다 `GetBroadBox`(도형 로컬 경계 사각형 × 높이 범위의 회전 상자)로 `Physics.OverlapBoxNonAlloc`. 이 상자 밖 점은 판정도 밖이다(테스트로 보장).
2. 걸린 콜라이더마다 `AttackShapeQuery.Overlaps(shape, pose, body)` — **GJK**(볼록 두 집합의 겹침 판정, 민코프스키 차가 원점을 품는가):
   - 도형 쪽 = 볼록 조각(`ConvexPieceCount` — 보통 1, 180° 넘는 부채꼴은 오른쪽·왼쪽 반 2개) × 높이 기둥의 지지점 `AttackShape.Support`(도형별 `SupportLocal`), 경계 포함이라 `Epsilon` 만큼 부풀린다.
   - 몸 쪽 = `AttackShapeColliderBody.Support` — 콜라이더 치수에서 직접 계산(Physics 호출 없음): 구·캡슐·상자·CharacterController(세운 캡슐)·볼록 MeshCollider(읽을 수 있는 메시면 정점, 메시마다 캐시). 그 밖(비볼록 MeshCollider·Terrain 등)은 경계 상자(AABB).
   - 빈 곳 있는 도형(`HasHole` — `minOffset`·`minRadius` > 0)은 조각이 빈 곳을 채운 껍질이라, 껍질이 겹치면 도형 최근접(`AttackShape.ClosestPoint`) ↔ 몸 최근접(`collider.ClosestPoint`) 교대 투영(최대 8회)으로 빈 곳 밖 겹침을 다시 찾는다.
   - 기준점·원 중심이 몸 안이어도 그냥 겹침이다 — 예전 '발밑 겹침 → 중심 x·z' 특례는 없앴다.
   - `AttackShapeHit.Point` = 겹친 점 근사(몸 중심에 가장 가까운 도형 점 → 그 점에 가장 가까운 몸 점, 그 점이 도형 밖이면 도형 점). 판정에는 쓰지 않는다.
3. 도형 **하나라도** 겹치면 피격.
4. 같은 대상은 한 공격에 1회 — `resolveTarget(collider)` 가 낸 키(보통 Unit, 없으면 Hurtbox — `PlayerSkillBase.ResolveHitUnit` 규칙)를 `seen` 에 넣는다. 자기 자신·대상 아님은 `null` 을 내면 빠진다.
   `seen` 은 공격 하나 동안 유지(여러 틱·여러 도형) → 새 공격에서 호출자가 비운다. 밖이었던 콜라이더는 기록하지 않으므로 같은 대상의 다른 콜라이더가 안이면 맞는다.
5. `buffer` 가 꽉 차면 넘친 콜라이더는 빠진다 — 크기는 호출자가 정한다.

지형 차단(벽 뒤)은 보지 않는다 — 지금 판정들과 같다.

같은 몸이 여러 도형과 겹치는지 보는 호출부(수호의 파동 동승)는 `AttackShapeQuery.Overlaps(set, pose, collider)`.

**정확도·비용**(2026-10-29 측정, 경계 근처 무작위 상자·캡슐·구 — 기울기 ±40°·회전 무작위. 정답 = 직사각형은 PhysX `ComputePenetration`(±1 mm 띠 제외), 나머지는 몸 부피 표본 점(19³)이 도형 안 / 교대 투영 수렴 틈 > 1 cm):

| 도형 | 판정 표본 | 놓침 | 거짓 맞음 | 몸 Support(평균) | 몸 ClosestPoint(평균) |
|---|---|---|---|---|---|
| 직사각형 | 2996 | 0 | 0 | 4.0 | 0.4 (맞을 때만 — 겹친 점 근사) |
| 부채꼴 120° | 585 | 0 | 0 | 3.9 | 0.4 |
| 원 | 579 | 0 | 0 | 3.5 | 0.4 |
| 삼각형 | 586 | 0 | 0 | 3.6 | 0.3 |
| 부채꼴 250°(조각 2) | 586 | 0 | 0 | 5.5 | 0.4 |
| 도넛 부채꼴(minOffset 1.5) | 581 | **10**(맞아야 할 195 중) | 0 | 3.8 | 0.7 |
| 도넛 원(minRadius 1.5) | 582 | **8**(232 중) | 0 | 3.6 | 0.7 |

- 볼록 도형은 GJK 라 반복 상한이 정확도를 정하지 않는다(상한 32, 실측 평균 4~6 지지점). 지지점은 순수 계산이라 Physics 호출은 넓은 질의 1회 + 맞을 때 ClosestPoint 1회뿐.
- 처음 시도한 순수 교대 투영(도형 최근접 ↔ `collider.ClosestPoint`)은 기울어진 면이 도형 면과 쐐기를 이루면 경계로 천천히만 다가가 **겹침 깊이 0.55 m 까지도 놓쳤다**(직사각형 3000 표본 중 놓침: 상한 1회 119 · 2회 119 · 4회 114 · 8회 95, 평균 ClosestPoint 1.0~3.2 회). 그래서 볼록 판정은 GJK, 교대 투영은 도넛 다듬기에만 남겼다.
- 측정 결과 사본(레포 밖): `C:\UnityProject\_lane-artifacts\MainProject\153-core-accuracy.txt`(GJK)·`153-core-accuracy-projection.txt`(교대 투영).


## 5. 특성 배율

`AttackShapeSet.Scaled(lengthMul, widthMul)` / `AttackShape.Scaled` = **복사본**(SO 원본 불변). 음수 배율은 0.

| 도형 | 길이 배율 | 폭 배율 |
|---|---|---|
| 부채꼴 | `range` | `angle`(상한 360) |
| 원 | `radius` | — |
| 직사각형 | `length` | `width` |
| 삼각형 | `length` | `width` |

오프셋·`minOffset`·`minRadius`·`backOffset`·높이는 배율을 받지 않는다.

## 6. xlsx 행

`[SerializeReference]` 원소 숫자 칸은 특성 효과와 같은 경로로 나간다([data-table.md §8](data-table.md#8-프로그래머--새-수치를-테이블에-내보내기) — 필드·`[Min]`·`[Range]` 는 원소의 실제 타입에서 찾는다).
SO 필드 이름이 `hitArea` 면 행 이름:

```
hitArea.shapes[0].forwardOffset     (이름표: hitArea.shapes[0] · AttackSectorShape)
hitArea.shapes[0].range
hitArea.shapes[0].angle
hitArea.shapes[0].minOffset
hitArea.shapes[0].minHeight / maxHeight / sideOffset / yawOffset
hitArea.shapes[1].length            (hitArea.shapes[1] · AttackRectShape)
```

- `origin`(enum)은 수치 시트에 나가지 않는다 — 인스펙터에서 고른다.
- 도형 **종류·개수**는 xlsx 로 바꿀 수 없다(원소 칸 번호가 없으면 오류). 종류·개수는 프로그래머가 SO 에서 정하고, 숫자만 기획이 튜닝한다.
- 도형을 추가·삭제·순서 변경하면 행 이름(`shapes[i]`)이 바뀐다 → 병합 Export 로 시트를 다시 맞춘다.

## 7. 새 공격에 도형 붙이는 법

1. 공격 데이터 SO 에 `[SerializeField] private AttackShapeSet hitArea = new AttackShapeSet();`(카테고리 시트 `[DataTableSheet]` 는 그 SO 가 이미 가진 것).
2. 인스펙터 '도형 추가' 로 도형을 넣고 기준점 종류를 고른다. 오류는 `hitArea.Validate(errors)`.
3. [서버] 판정 시점에: `pose = hitArea.ResolvePose(...)` → 특성 배율이 있으면 `area = hitArea.Scaled(l, w)` → `AttackShapeQuery.Collect(area, pose, data.HittableLayers, QueryTriggerInteraction.Collide, buffer, ResolveTarget, seen, hits)` → `hits` 마다 피해.
4. 기즈모: `AttackShapeDrawing.DrawGizmos(area, pose, color)`(OnDrawGizmos) 또는 `DrawHandles`(씬 GUI). (도형, 포즈) 는 판정이 쓰는 그 함수에서 받는다 — 판정 안에서 인라인으로 만들고 있으면 공용 정적 함수로 꺼내 판정과 기즈모가 같이 부른다.
   특성 랩 공격 범위 기즈모(`Dev/TraitLab/TraitLabAttackGizmos.cs`)에 새 공격을 넣으려면 `TraitLabAttackShapes` 에 그 공용 함수를 부르는 `Add…` 한 줄 + `TraitLabAttackShapeTests` 에 동일성 테스트 하나.

**특성 랩 기즈모 사용법**: DebugTraitLab 씬 Play → 패널 `공격 범위 기즈모` 체크(기본 꺼짐) → 분류 3개(■ 기본공격 하늘 · 스킬 주황 · 특성 추가 공격 분홍) 켜고 끄기. 씬의 모든 플레이어를 지금 위치·바라보는 방향 기준으로 매 프레임 다시 모아 특성 넣기·빼기가 바로 반영된다. Game 뷰는 상단 Gizmos 버튼을 켜야 보인다. 특성 추가 공격(파생 명세)·거너 R 은 서버 전용이라 호스트 창에서만 나온다. 라벨 '(도형 아님)' = 판정 도형이 아닌 콜라이더 판정.
5. 테스트: 그 공격의 기대 형상을 `Contains` 로 확인한다(Physics 없이).

## 8. 이관 단계

| 단계 | 내용 |
|---|---|
| 1 | 코어(이 문서·도형 4종·질의·그리기·테스트) — 기존 판정 코드는 아직 안 바뀜 |
| 2 | 어쌔신 평타·강타·변신 묶음 — ✅ `AssassinBasicAttackStepData.hitArea`(부채꼴 1개, 높이 ±range), `AssassinConeAttack.Fire·FireDerived` = `Collect`, 칼날의 영역 = `AssassinTraitAreaRules.ScaleArea`. 144 프리팹 상자·`AssassinStrikeBox` 삭제 |
| 3 | 팔라딘 평타 — ✅ `DefaultAttackStep.hitArea`(직사각형 1개, 4타 같은 값 = 예전 `Armature/DefaultAttack/AAC1` 상자: 앞 1.55·뒤 0.55·폭 2.6·yaw −0.03°·높이 0~2.039), `PlayerDefaultAttack.HitOverlap` = `Collect`(기준점 발밑 + 그 타 조준 방향). 프리팹 `AAC1~4`·`defaultHitbox`·단계 `hitbox` 삭제, `DefaultAttackController.attackSteps` 는 직렬화 안 함(데이터에서만) |
| 4 | 특성 추가 공격 5종 — ✅ `TraitDerivedAttackEffect.hitArea`(런타임 `TraitDerivedAttack.HitArea`). **비우면 원래 공격 치수 재사용**. 진격의 충격(`PAL_4_Q_BLAST`)만 도형: 기준점 `Point` = Q 끝 지점 + 마지막 진행 방향(`PaladinTraitRules.QBlastPose`), 부채꼴 range 2·angle 90·minOffset 0·forwardOffset 0·높이 ±2(예전 반경 2 구 질의 체감), `FirstMeleeMainSkill.RunTraitBlastServer` = `Collect`. `QBlastApex`·`QBlastInSector` 삭제. 나머지 4종(이중 출력·잔류 포격 = 원래 레이저, 추격의 궤적 = Q 경로 상자, 끝없는 칼날 = 마지막 한 타 도형 — 2단계)은 비움. 효과 칸 `angle` 삭제(시트에 행 없음), `radius`·`forwardOffset` 은 시트 `Traits_*` 행 때문에 미사용 칸으로 유지 |
| 5a | 가붕이 Q·E — ✅ Q `FirstMeleeMainSkillData.hitArea`(직사각형 1개 = 예전 앵커 `Armature/MainSkill` 상자 center 0,0,0.8 · size 2,1,2: 앞 1.8·뒤 0.2·폭 2·yaw −0.03°·높이 −0.461~0.539), 기준점 = 발밑 + 진행 방향(`FirstMeleeMainSkill.ResolvePose`), 틱마다 `Collect`(틱당 대상 1회). 돌파의 방패(`PAL_2_DASH`)는 같은 도형을 경로 표본마다 표본 지점(바닥 평면) + 시전자 높이 + 돌진 방향으로 옮겨(`ShieldDashSamplePose`) 훑고 시전 1회는 경로 장부. E `FirstMeleeSubSkillData.hitArea`(부채꼴 range 3·angle 120·높이 ±3 = 예전 counterRange·counterAngle·3D 구), 기준점 = 반격 순간 발밑 + 고정 방어 방향(`FirstMeleeSubSkill.CounterPose`). 수호의 파동(`PAL_4_E_PUSH`) = `PaladinTraitRules.EPushArea(반경)` 원(높이 ±반경, 특성 없으면 null) — 반격 맞은 대상은 같은 콜라이더가 원과도 겹치면(`AttackShapeQuery.Overlaps(set, pose, collider)`) 밀치기 동승, 나머지는 원으로 따로 `Collect`(살아 있는 몬스터만). 삭제: 프리팹 `Armature/MainSkill`·Q `hitboxAnchor`, `FirstMeleeGuardRules.IsInsideCounterSector`, `PaladinTraitRules.InsidePushRadius`. `counterAngle`·`counterRange` 는 xlsx 행 때문에 미사용 칸으로 유지 |
| 5b | 거너 Q — ✅ `GunnerChargeLaserData.hitArea`(직사각형 1개 = **최대 충전** 기준, 예전 발사 상자 `SkillHitVolume.Beam`: length 16(예전 `maxRange`)·width 1.2(예전 `beamWidth`)·뒤 0·높이 −1~1(예전 `beamHeight` 2 를 원점 높이 중심)). 기준점 `Point` = 발사 원점 `FireOrigin`(발밑 + 위로 `muzzleHeight`) + 조준 방향(`GunnerChargeLaserData.FirePose`). 발마다 `FireArea(길이, 폭)` = `Scaled(길이 / length, 폭 / width)` — 길이 = 충전 사거리(`minRange` → 도형 length 보간)를 지형 차단(SphereCast, 반경 = 데이터 폭/2 — 광폭 제외)으로 자른 값, 폭 = 광폭 레이저(`GUN_1_WIDTH`) ×1.3. `GunnerBeamAttack.FirePiercing`·`CollectAllies`(보호막) = `Collect`(대상 키 Unit, 없으면 Hurtbox · 자기 몸·시체 제외). 잔류 포격(`GUN_4_ECHO_Q`)은 할 일 149 로 도형 재판정이 아니라 맞은 대상에 붙는 폭탄(단일 대상, [tutorial-traits.md §7.1](tutorial-traits.md)). `MaxRange`·`BeamWidth` 는 도형에서 읽는다(지형 차단 반경·발사 연출 폭·미리보기 공용). 오너 미리보기 `FireVolume` = 첫 도형 넓은 질의 상자(`SkillHitVolume.Box`). `maxRange`·`beamWidth`·`beamHeight` 는 xlsx 행 때문에 미사용 칸으로 유지. 거너 E(냉각 백스텝)는 피해 판정이 없다. 평타(SphereCast)·R 추적 레이저(구)는 범위 밖 |
| 5c | 어쌔신 Q·E — ✅ Q `AssassinDashStrikeSkillData.hitArea`(일반·변신 Q 각 SO, 직사각형 1개 = 예전 구간 상자 `PathBox`: length 5(**기준 길이** = SO 돌진 거리 — 구간마다 구간 길이로 맞추므로 판정엔 영향 없음, 0 이면 판정 없음)·width 1.2(예전 `pathWidth`)·뒤 0·높이 0~2(예전 `pathHeight` 2 를 발부터 위로)). 기준점 `Point` = 경로 구간 시작(서버 재구성 출발점 + 방향 × from) + 돌진 방향(`AssassinDashStrikeSkill.PathSegment` → `PathArea(구간 길이)` = `Scaled(구간 / length, 1)`·`PathPose`). 매 틱 구간 `Collect`(질의 안 1회) → 사용당 1회는 기존 적중 장부(`AssassinDashHitLedger`, 그림자 회귀 복귀는 원래 Q 장부·단계별). 그림자 회귀(`AssassinShadowReturnSkill`)는 같은 단계 코드라 같은 도형(원래 Q 데이터). 추격의 궤적(`ASN_4_Q_TRAIL`, 도형 비움) = 같은 도형을 지나온 선분 길이로(기준점 = 선분 시작). `PathWidth` 는 도형에서 읽고 `pathWidth` 는 xlsx 행 때문에 미사용 칸으로 유지, `pathHeight`(DataTableIgnore)·`AssassinTrailRules.Box`·`PathBox` 삭제. 변신 E `AssassinCircleStrikeSkillData.hitArea`(원 1개 = 예전 원기둥: radius 2(예전 `aoeRadius`)·높이 0~3(예전 `areaHeight`)), 기준점 `Point` = 원 중심(지면 조준점, 높이 = 시전자 발) + 시전자 → 중심 방향(`StrikePose`). 칼날의 영역(`ASN_3_E_AREA`) = `StrikeArea(반경 배율)` = `Scaled(배율, 1)`, 5타 모두 확정 순간 영역·포즈. `AoeRadius` 를 덮어써(`PlayerSkillData.AoeRadius` virtual) 조준 원·VFX·미리보기가 도형 radius 를 읽는다 — 이 SO 의 베이스 `aoeRadius` 칸은 미사용. 오너 미리보기 `PreviewVolume` = 같은 도형의 원기둥(`SkillHitVolume.CircleColumn`). `areaHeight` 삭제. 일반 E(단검 강화)는 판정 없음(강타 = 2단계 평타), R 변신은 판정 없음, 간파 `InterruptAttack` 그대로 |
| 5d | 남은 캐릭터 Q·E 스킬(있으면) |
| 6 | DebugTraitLab 기즈모 통합 — ✅ `TraitLabAttackGizmos` 는 각 공격이 서버 판정에 넘기는 그 (도형, 포즈) 를 판정과 같은 공용 함수로 받아 `AttackShapeDrawing.DrawGizmos` 로만 그린다(기즈모 쪽 수치·형상 계산 없음). 꺼낸 공용 함수(판정 동작 불변): `PlayerDefaultAttack.StepArea`·`FirstMeleeMainSkill.HoldArea`·`ShieldDashSampleArea`·`FirstMeleeSubSkill.CounterArea`·`TraitPushArea`·`AssassinCircleStrikeSkill.ResolveStrikeArea`(+ `RadiusMultiplier` 공개)·`GunnerChargeLaserSkill.ShotArea`(지형 차단 길이 포함). 기존 공용: `AssassinBasicAttack.TryGetHitArea`·`AssassinDashStrikeSkill.PathSegment`·`PaladinTraitRules.QBlastPose`. 152 임시 그리기(넓은 질의 상자·임시 부채꼴·원기둥, `AddStrikeBox`·`AddBroadBoxes`)는 삭제. 도형 아닌 판정(거너 평타 캡슐·R 추적 구·간파 앵커 상자)만 콜라이더 형상 + 라벨 '(도형 아님)'. `AttackShapeDrawing` 은 높이 범위(minHeight·maxHeight 고리 + 세로 모서리, 옅게)도 그린다. 테스트 `TraitLabAttackShapeTests`(기즈모 = 판정 동일성·외곽선 = Contains 경계) |

간파 상자(`InterruptAttack`)는 어느 단계에도 들어가지 않는다.

## 9. 알려진 제약

- 외곽선은 바닥(기준점 높이 + lift) 고리를 진하게, 높이 범위(`minHeight`·`maxHeight`) 고리와 세로 모서리(고리당 최대 8개)를 옅게 그린다(`heightRange: false` 면 바닥만). 도넛은 안쪽 고리까지.
- 몸 겹침(§4)은 볼록 도형(180° 넘는 부채꼴은 두 조각)·볼록 몸에서 정확하다. 남는 근사:
  - 비볼록 MeshCollider·Terrain 등은 경계 상자(AABB)로 본다 — 실제 몸보다 넓게 맞는다. 볼록 MeshCollider 도 메시를 읽을 수 없으면(빌드 Read/Write 꺼짐) AABB.
  - 도넛(부채꼴 `minOffset`·원 `minRadius` > 0)은 빈 곳 밖 겹침을 교대 투영으로 찾아 기울어진 상자·캡슐이 빈 곳 쪽으로 걸친 경우를 약 4% 놓친다(구 몸은 정확). 지금 데이터에 도넛은 없다(모두 0).
  - 아주 먼 월드 좌표(수천 m)에선 float 해상도(≈0.5 mm)가 경계 허용 오차(0.1 mm)보다 커서 딱 닿는 경계가 어느 쪽으로든 읽힐 수 있다.
- 어쌔신 변신 E 원(5c)은 예전엔 콜라이더 **AABB** 가 원에 걸리면 맞았다. 이제 실제 몸 겹침이라 몸은 원에 안 닿고 AABB 모서리만 원 안인 둥근 몸은 빠진다(예: 중심 (1.8, 1.8) 반경 0.5 구 — 몸 표면이 2.05 m 로 원 반경 2 밖. 반경 0.6 이면 1.95 m 라 맞음). 원 중심이 큰 몸 안이면 이제 맞는다(예전과 같음).
- 판정 규칙이 몸 겹침으로 바뀌어(§4) 예전 '발밑 겹침 = 중심 방향' 특례도 없다 — 기준점(부채꼴 꼭짓점)이 몸 안이면 몸 중심이 등 뒤여도 맞는다(진격의 충격·가붕이 E 반격). 예전 가붕이 E 반격(표면 거리 0)과 같고, 예전 진격의 충격(`QBlastInSector` — 중심이 뒤면 빠짐)과는 이 경우만 다르다.
