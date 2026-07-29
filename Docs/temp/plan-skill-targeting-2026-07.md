# PLAN — 스킬 타겟팅 시스템

> 브랜치: feature/PlayerSkill
> 상태: **코드 구현 + 프리팹/SO 배선 완료(2026-07-21, aimPoint 포함) — dotnet build 오류 0. Unity 재열기 검증 + 값 튜닝 + MPPM 남음**
> 사거리 인디케이터는 사용자가 URP DecalProjector로 제작 → SkillRangeIndicator.cs를 DecalProjector.size 구동식으로 맞춤(메시 스케일 아님).
> 기획 근거: 이전 PlayerSkill 플랜에서 비범위로 미뤄둔 "R의 타겟팅 UI/선택 방식"([plan-playerskill-2026-07.md](plan-playerskill-2026-07.md) 2절·6번 항목)

## 1. 목표

스킬키를 눌러도 즉시 시전하지 않고 **조준 모드**로 진입해 사거리를 표시하고, 마우스로 대상을 지정한 뒤 좌클릭으로 확정하는 범용 타겟팅 인프라. 첫 소비처로 **R 최후의 심판(Ultimate)** 을 타겟형으로 구현한다.

## 2. 그릴 확정 사항

| 항목 | 결정 |
|---|---|
| 타겟팅 방식 | **둘 다** — 스킬별 SO로 `SingleTarget`(적 지정) / `GroundPoint`(지면 지점) 선택. 기존 즉발 스킬은 `None`(무영향) |
| 코드 범위 | 범용 인프라 + R(Ultimate)을 첫 타겟형 스킬로 구현 |
| 확정 흐름 | **조준 모드 진입 → 좌클릭 확정**(기본). 홀드-릴리즈·즉시확정은 enum으로 예약만, 이번엔 미구현 |
| 커서 | 마우스 상태머신 + **아이콘 교체 훅만**(에셋 없음). 이벤트만 발행, 실제 `Cursor.SetCursor`는 stub |
| R 효과 | **최소** — 채널 완주 후 지정 대상에게 단일 대미지 1회. 수치는 SO |
| 사거리 비주얼 | **바닥 링/쿼드 메시 프리팹** 1개(시전자 중심, 반경 = castRange) |
| 타겟 레이어 | **Enemy** 레이어 레이캐스트 → `GetComponentInParent<Unit>()`(Hurtbox fallback) |
| 아웃라인(타겟 강조 색) | **보류** (사용자 지시) — 훅 `SetHighlightedTarget(Unit)`만 남기고 실제 색 변경 미구현 |

## 3. 비범위

- 타겟 아웃라인/색 변경 실제 구현 (훅만)
- 커서 아이콘 텍스처 교체 실제 구현 (상태 이벤트 + 직렬화 필드만)
- 확정 흐름 2·3안(홀드-릴리즈, 즉시확정) 실제 구현 (enum 예약)
- 기존 스킬(Q/E/우클릭) 동작 변경 — `targetingMode = None`으로 현행 유지
- R의 상세 메커니즘/연출 (최소 대미지만)

## 4. 아키텍처

### 4.1 흐름

```
[오너 로컬]
스킬키 press
  └ skill.Data.TargetingMode == None → 기존 즉발 경로 (TryUse, 변화 없음)
  └ != None → PlayerSkillTargeting.Begin(slot)  // 조준 모드 진입, FSM은 아직 Idle/Move 유지
       ├ 매 프레임: 사거리 링 표시 + 카메라 레이캐스트 → 마우스 상태 갱신
       │   ├ SingleTarget: Enemy 레이캐스트로 후보 Unit 탐색 + 사거리 내 판정
       │   └ GroundPoint : Ground 레이캐스트로 지점 + 사거리 내 클램프
       ├ 좌클릭 → 확정: 클라 유효성(사거리/대상 생존) 통과 시
       │        PlayerSkillController.TryUse(slot, target[, aimPoint]) 호출
       └ Esc / 스킬키 재입력 → 취소 (조준 모드 종료, 시전 안 함)

[서버] 기존 경로 그대로 — StartSkillServer → CanApproveSkill → skill.CanUse(direction, target)
       에서 사거리/대상을 **권위 재검증**. 클라 판정은 UX 게이트일 뿐.
```

조준 모드는 **FSM에 진입하지 않는다**(실제 시전 승인 시에만 Skill 상태 진입). 조준 중 이동은 허용, 좌클릭/다른 스킬 입력은 억제(좌클릭은 확정에 소비).

### 4.2 신규 파일 (`Assets/1.Scripts/Player/Skill/Targeting/`)

| 파일 | 내용 |
|---|---|
| `SkillTargetingMode.cs` | `enum { None, SingleTarget, GroundPoint }` |
| `SkillConfirmMode.cs` | `enum { ClickToConfirm, HoldRelease, InstantAtCursor }` — 이번엔 ClickToConfirm만 구현 |
| `SkillCursorState.cs` | `enum { Default, Targeting, ValidTarget, InvalidTarget, OutOfRange }` |
| `PlayerSkillTargeting.cs` | 오너 전용 MonoBehaviour. 상태머신·레이캐스트·프리뷰·확정/취소. Player 루트 부착 |
| `SkillRangeIndicator.cs` | 사거리 링 + (GroundPoint용) 지점 마커 표시/숨김/스케일 |
| `SkillCursorView.cs` | 상태 이벤트 구독 stub. 텍스처 직렬화 필드 + `Cursor.SetCursor` 자리(현재 no-op) |
| `FirstMeleeUltimateSkill.cs` | R 최후의 심판 — Channeling + SingleTarget. 완주 시 대상 단일 대미지 |
| `FirstMeleeUltimateSkillData.cs` | R 파생 SO (팀 관례상 스킬별 SO 유지 — 궁극 전용 튜닝 수용처) |

### 4.3 수정 파일

| 파일 | 변경 |
|---|---|
| `PlayerSkillData.cs` | `+ TargetingMode targetingMode(기본 None)`, `+ float castRange`, `+ ConfirmMode confirmMode(기본 ClickToConfirm)`, `+ LayerMask targetableLayers(SingleTarget용, 기본 Enemy)` |
| `PlayerSkillController.cs` | `PlayerSkillTargeting`가 슬롯별 스킬/데이터를 조회할 수 있게 이미 있는 `GetSkill`/`IsCooldownReady` 재사용. GroundPoint 확정 지점을 서버로 넘기기 위한 **`aimPoint` 1개**를 요청 경로·`RequestUseSkillRpc`에 추가(기존 direction/targetRef 유지, 신규 인자만). SingleTarget은 기존 targetRef 경로로 충분 |
| `PlayerStateController.cs` | `TryStartSkillInput`에서 조준 모드 분기: `targetingMode != None`이면 `TryUse` 대신 `PlayerSkillTargeting.Begin(slot)`. 조준 활성 중엔 `AttackPressed`/다른 스킬 시작 억제 |
| `PlayerSkillBase.cs` | (선택) `CanUse` 오버라이드로 R에서 사거리/대상 검증. 시그니처 변경 없음 |

**네트워크 표면 변경 최소화**: SingleTarget(R)은 기존 RPC로 충분. GroundPoint 확정 지점 전달용 `aimPoint`(Vector3) 1개만 `RequestUseSkillRpc`에 추가한다. 기존 Q/E/우클릭 호출은 `aimPoint = default`로 무해. 이 한 곳이 유일한 RPC 확장 — 승인 시 이 범위 확인 요망.

### 4.4 마우스 상태머신

조준 중 매 프레임 레이캐스트 결과로 전이하고, 변할 때만 `OnCursorStateChanged(SkillCursorState)` 이벤트 발행:

- `Default` — 조준 아님
- `Targeting` — 조준 중, 유효 대상/지점 아직 아님
- `ValidTarget` — SingleTarget: 사거리 내 유효 적 위 / GroundPoint: 사거리 내 유효 지면
- `InvalidTarget` — SingleTarget에서 적이 아닌 것 위
- `OutOfRange` — 대상/지점이 사거리 밖

`SkillCursorView`가 이 이벤트를 구독해 (미래) 아이콘 교체. 현재는 no-op + 직렬화 필드만.

### 4.5 R 최후의 심판 (첫 소비처)

- 타입: `PlayerChannelingSkill` 상속, `Slot => Ultimate`, `TargetingMode = SingleTarget`
- `CanMoveWhileActive = false` (채널 중 이동/회전 잠금)
- `OnServerStart(direction, target)`: `lockedTarget = target` 저장
- `CanUse`: target이 살아있는 Enemy Unit이고 사거리(castRange) 내인지 **서버 권위** 검증
- `OnChannelCompleted`(서버): `lockedTarget`에 `AttackInfo(damageSnapshot, AttackType.R)` 1회 적용(Hurtbox 우선, Unit fallback — FirstMeleeMainSkill.ResolveUnit 패턴 재사용)
- 채널 중 대상 사망/디스폰: `EndSelf(Cancelled)` (최소 처리)

## 5. Unity 에디터 작업 (코드 후속, 사용자/AI 합의 배선)

1. `SkillRangeIndicator` 프리팹 제작(바닥 링 메시 + 머티리얼) — Player.prefab 하위 배치, 기본 비활성
2. Player.prefab에 `PlayerSkillTargeting` + `SkillCursorView` 부착, 인디케이터·PlayerSkillController 참조 배선
3. `FirstMeleeUltimateSkill` 컴포넌트 + `FirstMeleeUltimateSkillData` SO 생성 후 UltimateSkill 앵커/슬롯에 배선
4. R SO에 castRange/채널시간(MaxActiveDuration)/대미지 계수/targetingMode=SingleTarget/targetableLayers=Enemy 설정
5. InputActions에 `SkillUltimate(R)` 존재 확인(플랜상 이미 추가됨)

## 6. 완료 조건

1. 컴파일 통과, 전 파일 UTF-8(BOM)
2. 기존 스킬(Q/E/우클릭) 동작 무변화(`targetingMode = None`)
3. R 입력 → 조준 모드(사거리 링) → 적 호버 시 커서 상태 `ValidTarget` → 좌클릭 → 채널 → 완주 시 대상 단일 대미지 (MPPM 2인 검증)
4. Esc/재입력 취소, 사거리 밖 좌클릭 시 미시전
5. 커서 아이콘/아웃라인은 훅만 존재(no-op)임을 코드 주석으로 명시

## 7. 리스크 / 확인 필요

- **좌클릭 확정 vs 기본 공격 충돌**: 조준 중 좌클릭은 확정에 소비하고 DefaultAttack을 억제. 조준 아님일 땐 현행 유지.
- **취소 키**: 우클릭은 이미 단죄의 방패(Interrupt 슬롯)라 취소에 못 씀 → **Esc + 스킬키 재입력**으로 취소.
- **aimPoint RPC 확장**(4.3) 승인 여부 — GroundPoint를 진짜 동작시키려면 필요, R만이면 불필요.
