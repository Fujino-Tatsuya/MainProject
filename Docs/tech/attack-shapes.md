# 공격 판정 도형 (할 일 153)

> 판정 형상을 **프리팹 콜라이더 → 코드 계산 도형 클래스**로 옮긴다. 수치는 공격 데이터 SO 의 `AttackShapeSet` 필드(도형 `[SerializeReference]` 목록)에 두고 xlsx 로 튜닝한다.
> 코드: `Assets/1.Scripts/Unit/Weapon/AttackShapes/` · 테스트: 같은 폴더 `Editor/AttackShapeTests.cs`(`Tools/Tests/플레이어 EditMode 테스트 실행`).
> 🔴 **간파 판정 상자(`InterruptAttack` — 예측기·대쉬 간파가 씀)는 이 작업 전체에서 예외**다. 도형으로 옮기지 않는다.

## 1. 구조

| 타입 | 역할 |
|---|---|
| `AttackShape` (추상) | 도형 1개. 공통 칸 + 판정 `Contains`·넓은 질의 상자 `GetBroadBox`·배율 `Scaled`·외곽선 `AppendOutlines`·검증 `Validate`. 순수 C#(Physics·MonoBehaviour 없음) |
| `AttackSectorShape` / `AttackCircleShape` / `AttackRectShape` / `AttackTriangleShape` | 도형 4종(`AttackShapes.cs`). 캡슐은 없다 |
| `AttackShapePose` | 기준 포즈 = 바닥 위치 + 바닥 평면 정면(정면 0 이면 +z) |
| `AttackShapeOrigin` | 기준점 종류 enum — `CasterAim`(시전자 발밑 + 조준 방향) / `Point`(스킬이 정한 지점 + 방향) |
| `AttackShapeSet` | 공격 하나 = 기준점 종류 + 도형 목록(**합집합**, 빼기 없음). SO 에 이 타입 필드를 둔다 |
| `AttackShapeQuery` | [서버] 질의 — 넓은 상자 Overlap → 판정 점 → 합집합 → 대상 1회 |
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

## 4. 피격 판정 점·합집합·1회

`AttackShapeQuery.Collect(set, pose, layerMask, triggers, buffer, resolveTarget, seen, results)`:

1. 도형마다 `GetBroadBox`(도형 로컬 경계 사각형 × 높이 범위의 회전 상자)로 `Physics.OverlapBoxNonAlloc`. 이 상자 밖 점은 판정도 밖이다(테스트로 보장).
2. 걸린 콜라이더마다 판정 점 = `ClosestPoint(기준점)` — **몸 표면에서 기준점에 가장 가까운 점**(지금 어쌔신·진격의 충격과 같은 점). 비볼록 MeshCollider 는 `ClosestPointOnBounds`.
   **발밑 겹침**(그 점이 바닥 평면에서 기준점과 같음 = 기준점이 몸 안)이면 x·z 만 콜라이더 `bounds.center` 로 바꾼다(높이는 가장 가까운 점 그대로) — 예전 `QBlastInSector` 동작.
3. 판정 점이 도형 **하나라도** 안(바닥 평면 2D + 높이 범위)이면 피격.
4. 같은 대상은 한 공격에 1회 — `resolveTarget(collider)` 가 낸 키(보통 Unit, 없으면 Hurtbox — `PlayerSkillBase.ResolveHitUnit` 규칙)를 `seen` 에 넣는다. 자기 자신·대상 아님은 `null` 을 내면 빠진다.
   `seen` 은 공격 하나 동안 유지(여러 틱·여러 도형) → 새 공격에서 호출자가 비운다. 밖이었던 콜라이더는 기록하지 않으므로 같은 대상의 다른 콜라이더가 안이면 맞는다.
5. `buffer` 가 꽉 차면 넘친 콜라이더는 빠진다 — 크기는 호출자가 정한다.

지형 차단(벽 뒤)은 보지 않는다 — 지금 판정들과 같다.

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
4. 기즈모: `AttackShapeDrawing.DrawGizmos(area, pose, color)`(OnDrawGizmos) 또는 `DrawHandles`(씬 GUI).
5. 테스트: 그 공격의 기대 형상을 `Contains` 로 확인한다(Physics 없이).

## 8. 이관 단계

| 단계 | 내용 |
|---|---|
| 1 | 코어(이 문서·도형 4종·질의·그리기·테스트) — 기존 판정 코드는 아직 안 바뀜 |
| 2 | 어쌔신 평타·강타·변신 묶음 — ✅ `AssassinBasicAttackStepData.hitArea`(부채꼴 1개, 높이 ±range), `AssassinConeAttack.Fire·FireDerived` = `Collect`, 칼날의 영역 = `AssassinTraitAreaRules.ScaleArea`. 144 프리팹 상자·`AssassinStrikeBox` 삭제 |
| 3 | 팔라딘 평타 — ✅ `DefaultAttackStep.hitArea`(직사각형 1개, 4타 같은 값 = 예전 `Armature/DefaultAttack/AAC1` 상자: 앞 1.55·뒤 0.55·폭 2.6·yaw −0.03°·높이 0~2.039), `PlayerDefaultAttack.HitOverlap` = `Collect`(기준점 발밑 + 그 타 조준 방향). 프리팹 `AAC1~4`·`defaultHitbox`·단계 `hitbox` 삭제, `DefaultAttackController.attackSteps` 는 직렬화 안 함(데이터에서만) |
| 4 | 특성 추가 공격 5종 — ✅ `TraitDerivedAttackEffect.hitArea`(런타임 `TraitDerivedAttack.HitArea`). **비우면 원래 공격 치수 재사용**. 진격의 충격(`PAL_4_Q_BLAST`)만 도형: 기준점 `Point` = Q 끝 지점 + 마지막 진행 방향(`PaladinTraitRules.QBlastPose`), 부채꼴 range 2·angle 90·minOffset 0·forwardOffset 0·높이 ±2(예전 반경 2 구 질의 체감), `FirstMeleeMainSkill.RunTraitBlastServer` = `Collect`. `QBlastApex`·`QBlastInSector` 삭제. 나머지 4종(이중 출력·잔류 포격 = 원래 레이저, 추격의 궤적 = Q 경로 상자, 끝없는 칼날 = 마지막 한 타 도형 — 2단계)은 비움. 효과 칸 `angle` 삭제(시트에 행 없음), `radius`·`forwardOffset` 은 시트 `Traits_*` 행 때문에 미사용 칸으로 유지 |
| 5a | 가붕이 Q·E — ✅ Q `FirstMeleeMainSkillData.hitArea`(직사각형 1개 = 예전 앵커 `Armature/MainSkill` 상자 center 0,0,0.8 · size 2,1,2: 앞 1.8·뒤 0.2·폭 2·yaw −0.03°·높이 −0.461~0.539), 기준점 = 발밑 + 진행 방향(`FirstMeleeMainSkill.ResolvePose`), 틱마다 `Collect`(틱당 대상 1회). 돌파의 방패(`PAL_2_DASH`)는 같은 도형을 경로 표본마다 표본 지점(바닥 평면) + 시전자 높이 + 돌진 방향으로 옮겨(`ShieldDashSamplePose`) 훑고 시전 1회는 경로 장부. E `FirstMeleeSubSkillData.hitArea`(부채꼴 range 3·angle 120·높이 ±3 = 예전 counterRange·counterAngle·3D 구), 기준점 = 반격 순간 발밑 + 고정 방어 방향(`FirstMeleeSubSkill.CounterPose`). 수호의 파동(`PAL_4_E_PUSH`) = `PaladinTraitRules.EPushArea(반경)` 원(높이 ±반경, 특성 없으면 null) — 반격 맞은 대상은 같은 판정 점이 원 안이면 밀치기 동승, 나머지는 원으로 따로 `Collect`(살아 있는 몬스터만). 삭제: 프리팹 `Armature/MainSkill`·Q `hitboxAnchor`, `FirstMeleeGuardRules.IsInsideCounterSector`, `PaladinTraitRules.InsidePushRadius`. `counterAngle`·`counterRange` 는 xlsx 행 때문에 미사용 칸으로 유지 |
| 5b | 거너 Q — ✅ `GunnerChargeLaserData.hitArea`(직사각형 1개 = **최대 충전** 기준, 예전 발사 상자 `SkillHitVolume.Beam`: length 16(예전 `maxRange`)·width 1.2(예전 `beamWidth`)·뒤 0·높이 −1~1(예전 `beamHeight` 2 를 원점 높이 중심)). 기준점 `Point` = 발사 원점 `FireOrigin`(발밑 + 위로 `muzzleHeight`) + 조준 방향(`GunnerChargeLaserData.FirePose`). 발마다 `FireArea(길이, 폭)` = `Scaled(길이 / length, 폭 / width)` — 길이 = 충전 사거리(`minRange` → 도형 length 보간)를 지형 차단(SphereCast, 반경 = 데이터 폭/2 — 광폭 제외)으로 자른 값, 폭 = 광폭 레이저(`GUN_1_WIDTH`) ×1.3. `GunnerBeamAttack.FirePiercing`·`CollectAllies`(보호막) = `Collect`(대상 키 Unit, 없으면 Hurtbox · 자기 몸·시체 제외). 잔류 포격(`GUN_4_ECHO_Q`) = 첫 발 포즈(원점·방향)·충전 사거리·폭으로 같은 도형, 지형 차단은 그때 다시. `MaxRange`·`BeamWidth` 는 도형에서 읽는다(지형 차단 반경·발사 연출 폭·미리보기 공용). 오너 미리보기 `FireVolume` = 첫 도형 넓은 질의 상자(`SkillHitVolume.Box`). `maxRange`·`beamWidth`·`beamHeight` 는 xlsx 행 때문에 미사용 칸으로 유지. 거너 E(냉각 백스텝)는 피해 판정이 없다. 평타(SphereCast)·R 추적 레이저(구)는 범위 밖 |
| 5c | 어쌔신 Q·E — ✅ Q `AssassinDashStrikeSkillData.hitArea`(일반·변신 Q 각 SO, 직사각형 1개 = 예전 구간 상자 `PathBox`: length 5(**기준 길이** = SO 돌진 거리 — 구간마다 구간 길이로 맞추므로 판정엔 영향 없음, 0 이면 판정 없음)·width 1.2(예전 `pathWidth`)·뒤 0·높이 0~2(예전 `pathHeight` 2 를 발부터 위로)). 기준점 `Point` = 경로 구간 시작(서버 재구성 출발점 + 방향 × from) + 돌진 방향(`AssassinDashStrikeSkill.PathSegment` → `PathArea(구간 길이)` = `Scaled(구간 / length, 1)`·`PathPose`). 매 틱 구간 `Collect`(질의 안 1회) → 사용당 1회는 기존 적중 장부(`AssassinDashHitLedger`, 그림자 회귀 복귀는 원래 Q 장부·단계별). 그림자 회귀(`AssassinShadowReturnSkill`)는 같은 단계 코드라 같은 도형(원래 Q 데이터). 추격의 궤적(`ASN_4_Q_TRAIL`, 도형 비움) = 같은 도형을 지나온 선분 길이로(기준점 = 선분 시작). `PathWidth` 는 도형에서 읽고 `pathWidth` 는 xlsx 행 때문에 미사용 칸으로 유지, `pathHeight`(DataTableIgnore)·`AssassinTrailRules.Box`·`PathBox` 삭제. 변신 E `AssassinCircleStrikeSkillData.hitArea`(원 1개 = 예전 원기둥: radius 2(예전 `aoeRadius`)·높이 0~3(예전 `areaHeight`)), 기준점 `Point` = 원 중심(지면 조준점, 높이 = 시전자 발) + 시전자 → 중심 방향(`StrikePose`). 칼날의 영역(`ASN_3_E_AREA`) = `StrikeArea(반경 배율)` = `Scaled(배율, 1)`, 5타 모두 확정 순간 영역·포즈. `AoeRadius` 를 덮어써(`PlayerSkillData.AoeRadius` virtual) 조준 원·VFX·미리보기가 도형 radius 를 읽는다 — 이 SO 의 베이스 `aoeRadius` 칸은 미사용. 오너 미리보기 `PreviewVolume` = 같은 도형의 원기둥(`SkillHitVolume.CircleColumn`). `areaHeight` 삭제. 일반 E(단검 강화)는 판정 없음(강타 = 2단계 평타), R 변신은 판정 없음, 간파 `InterruptAttack` 그대로 |
| 5d | 남은 캐릭터 Q·E 스킬(있으면) |
| 6 | DebugTraitLab 기즈모 통합(할 일 152 기즈모 → `AttackShapeDrawing`) |

간파 상자(`InterruptAttack`)는 어느 단계에도 들어가지 않는다.

## 9. 알려진 제약

- 판정 점은 **기준점 쪽** 가장 가까운 점 하나다. 도형이 기준점에서 멀리 떨어진 경우(큰 `forwardOffset`·`sideOffset`) 큰 몸의 반대편 일부만 도형에 걸치면 놓칠 수 있다 — 기존 방식과 같은 한계.
- 외곽선은 바닥(기준점 높이) 한 겹만 그린다. 높이 범위는 그리지 않는다.
- 거너 Q 레이저(5b)는 예전엔 상자 **겹침**이라 몸 옆면만 폭에 걸친 큰 대상도 맞았다. 이제 판정 점 = 발사 원점 쪽 가장 가까운 점이라 먼 대상일수록 **몸 중심 쪽이 폭 안**이어야 맞는다(예: 10 m 앞 반경 1 m 몸 — 예전은 중심 옆 거리 1.6 m 까지, 지금은 약 0.7 m 까지). 끝 모양은 같다(예전도 상자 — 캡슐 아님).
- 어쌔신 Q 경로(5c)도 같은 차이 — 판정 점 = **구간 시작** 쪽 가장 가까운 점. 매 틱 구간(60 m/s · 60fps ≈ 1 m)은 몸 옆을 지날 때 구간 시작이 가까워 예전과 거의 같다(반경 1 m 몸: 예전 중심 옆 1.6 m 까지, 지금 구간 위상에 따라 약 1.42~1.6 m). 한 구간으로 몰아 훑으면(클립 End 몰아치기·추격의 궤적 = 지나온 선분 전체) 빠진다(5 m 선분, 3 m 앞 반경 1 m 몸: 예전 1.6 m → 지금 약 0.88 m). 또 뒤 0 이라 **구간 시작보다 뒤에 중심이 있는 몸**(돌진 시작 때 겹쳐 서 있던 뒤쪽 몸)은 판정 점이 뒤라 빠진다.
- 어쌔신 변신 E 원(5c)은 예전엔 콜라이더 **AABB** 가 원에 걸리면 맞았다. 이제 실제 몸 표면 가장 가까운 점이라 대각선 모서리만 걸친 둥근 몸은 빠진다(예: 중심 (1.8, 1.8) 반경 0.5 구 — 예전 맞음, 지금 표면 2.05 m 라 밖). 원 중심이 큰 몸 안(발밑 겹침)이면 몸 **중심**까지 거리로 잰다 — 몸 반경 > 원 반경(2, 칼날의 영역 2.5)인 몸만 해당(예: 반경 3 구 중심 2.5 m — 예전 맞음, 지금 밖). 23호 프리팹의 반경 1.7 콜라이더는 해당 없음(4.42 상자 콜라이더가 피격 레이어인지는 미확인).
- 발밑 겹침(기준점이 대상 몸 안)이면 판정 점이 중심 x·z 라 거리도 중심으로 잰다. 예전 가붕이 E 반격은 이때 거리를 표면(0)으로 쟀으므로, 시전자가 중심이 반경 밖인 큰 몸 안에 들어간 드문 경우만 달라진다.
