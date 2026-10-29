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
| 5b | 다른 캐릭터 Q·E 스킬 |
| 6 | DebugTraitLab 기즈모 통합(할 일 152 기즈모 → `AttackShapeDrawing`) |

간파 상자(`InterruptAttack`)는 어느 단계에도 들어가지 않는다.

## 9. 알려진 제약

- 판정 점은 **기준점 쪽** 가장 가까운 점 하나다. 도형이 기준점에서 멀리 떨어진 경우(큰 `forwardOffset`·`sideOffset`) 큰 몸의 반대편 일부만 도형에 걸치면 놓칠 수 있다 — 기존 방식과 같은 한계.
- 외곽선은 바닥(기준점 높이) 한 겹만 그린다. 높이 범위는 그리지 않는다.
- 발밑 겹침(기준점이 대상 몸 안)이면 판정 점이 중심 x·z 라 거리도 중심으로 잰다. 예전 가붕이 E 반격은 이때 거리를 표면(0)으로 쟀으므로, 시전자가 중심이 반경 밖인 큰 몸 안에 들어간 드문 경우만 달라진다.
