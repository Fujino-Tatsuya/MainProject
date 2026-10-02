# PLAN — 원거리 캐릭터 "거너" 구현 (2026-09-30 승인 · 2026-10-01 G0~G9 완료)

작성: 은희(Claude) / 브랜치: `feature/SecondCharacter`(= `development` `883fb34a` 에서 분기, 커밋 0)
기획 원본: [character_gunner.md](Docs/design/character/character_gunner.md) — **§0 확정 변경(D1~D15)이 원본 본문보다 우선**
관련: [player-prefabs.md](Docs/tech/player-prefabs.md) · [PLAN-player-variants.md](Docs/history/PLANS/PLAN-player-variants.md) ·
[PLAN-passive-onhit.md](Docs/history/PLANS/PLAN-passive-onhit.md) · [PLAN-boss-counter-vulnerable.md](PLAN-boss-counter-vulnerable.md)

> ✅ **담당 = 은희**(Unit·Player 본인 영역). 보스·몬스터 코드(경석)는 **수정하지 않는다**.
> VFX 는 이벤트 훅까지만 만들고 연출은 민경.

---

## 1. Goal

`Player_Gunner`(Player.prefab Variant)를 스폰해 **기획서의 기본 공격·과열·Q·E·우클릭·R** 이 MPPM 2~3인에서
기획서 §15 테스트 항목대로 동작하게 한다. **가붕이(`Player_Paladin`) 동작은 바뀌지 않아야 한다.**

범위 밖: 로비 캐릭터 선택 UI(player-prefabs.md §8.3), 정식 HUD 아트, 거너 VFX 연출, R 잠금(빌드), 밸런스 수치 확정.

---

## 2. 현재 코드 사실 (2026-09-30 조사)

| 영역 | 사실 | 계획 영향 |
|------|------|-----------|
| 기본 공격 | `DefaultAttackController` = 가붕이식 콤보. 공격마다 오너가 조준을 RPC 인자로 보냄, 서버 판정. `Player`·`PlayerStateController`·`PlayableCharacterVisual` 이 **`[RequireComponent(typeof(DefaultAttackController))]`**, 그 외 `PlayerEncounterLock`·`PlayerAnimationEventRelay`·`PlayerRootMotionRelay` 가 구체 타입을 직접 참조 | 공통 창구가 없어 **G2 에서 `IPlayerBasicAttack` 신설** |
| 스킬 | `PlayerSkillData` SO + `PlayerInstantSkill`/`PlayerHoldSkill`/`PlayerChannelingSkill`. 오너 요청 → 서버 승인. **쿨은 스킬 시작 시 시작**(`PlayerSkillController.cs:276`). 타겟팅은 ClickToConfirm 만 구현 | Q(발사 시)·R(생성 시) 쿨 시점 옵션 필요. Q 의 "Q 로 시작 → 좌클릭 발사"는 기존 Hold 와 달라 새 흐름 |
| 보호막 | `Health._currentShield` **int 1개**, `NetworkVariable<int>` 복제. `SetShield` = 교체. 사용처: `FirstMeleeSubSkill`(설정·만료 코루틴), `Unit.cs:237`, 흡수 `Unit.cs:136`, `PlayerHealthHUD:89`, `Player.cs:1647` | **G1 코어 변경** — 인스턴스 목록화 |
| 간파 | 공격은 `AttackInfo.isInterruptAttack` 만 싣고 보스가 판정. 23호 = 전방 ±60°·취약 넉백·벽/벤트(경석, 09-29 Play 확인). 동시 적중 = 같은 프레임 첫 1회만 넉백 | 거너 우클릭은 표시만 — 보스 수정 0 |
| 대시 | `PlayerStateController.BeginDash(dir, speed, duration)` public. 모터 스윕이 벽·적에 막힘, **아군은 통과**(`blockOtherPlayers: 0`) | E 는 아군도 막는 옵션 필요 |
| 이동 감속 | `StatusEffects.GetStatMultiplier(MoveSpeedModifier)` → 이동 시뮬레이션 | Q 집중 감속에 재사용 |
| 상태 아이콘 | **상태이상 아이콘 UI 없음** | D5(정신 집중 아이콘)용 최소 표시 신설 |
| 수명 | 플레이어는 **씬마다 재스폰**(`SpawnAsPlayerObject(…, destroyWithScene:true)`). 쓰러짐·부활은 같은 오브젝트 | 과열 "씬 전환 시 0" 자동 충족 |
| 플레이어 소유 네트워크 오브젝트 | **없음**(몬스터·`AreaZone`·몬스터 투사체만) | R 레이저가 첫 사례 |
| 어그로 | 거리 기반(`BossAggroPolicy`), 피해 무관 | 처리 불필요(D13) |
| 아트 | `gunner.fbx`(09-28, 테이크 16개, **`clipAnimations: []`**), `laser_gun.fbx`. 프리팹·애니메이터 없음 | G0 |

---

## 3. 접근 — 단계별 (각 단계 = 커밋 1개 이상, `dotnet build` 통과 후)

### G0. 아트 준비 — SVN (은희 직접)
- ✅ 결정(2026-09-30): Rig = **Generic**(Humanoid 는 엄지·forearm.r 인비트윈 회전 불일치 에러), Avatar = Create From This Model. 전 클립 Root Transform Bake Into Pose.
- ✅ 애니 구성(은희): 기본 공격 **준비 동작 = `gunner_skill_Q_charge_loop`**, 연사 중 **하체 = `Q_charge_loop` 유지 + 상체 = `gunner_attack` 매 발 재생**
  → 애니메이터 2 레이어(Base 전신 / UpperBody = 척추 이상 AvatarMask, Generic 이라 본 경로 기반). `gunner_attack` 은 Loop 끔(매 발 처음부터).
  Loop 켬 = idle · walk · Q_charge_loop · Q_charge_move_loop.
- `gunner.fbx.meta` 클립 분할: idle / walk / attack(준비·발사 루프) / Q_start·Q_charge_loop·Q_charge_move_loop·Q_fire·Q_recover / E_cool_backstep / RMB_interrupt / ULT_cast. 루프 플래그(idle·walk·attack 발사·Q charge loop).
- **SVN 커밋 후 guid 불변 확인**(`.meta` guid). git 쪽 `art-svn.json` 핀 갱신.
- 이후(git): `Gunner.controller` 애니메이터 — 가붕이 컨트롤러의 파라미터·상태 이름 규약을 따른다.

### G1. 보호막 인스턴스화 (코어, D8)
- `ShieldType` enum(`HolyShield`=가붕이 E, `GunnerCharge`=거너 Q, …) + 종류별 `ShieldStackPolicy`(Replace/Stack) 데이터.
- `Health`/`Unit`: 인스턴스 목록 `{type, sourceId, amount, expireServerTime}` — 서버 보관, **`NetworkList` 로 복제**. `CurrentShield` = 합계(기존 API 유지).
- 흡수: **만료가 가장 빠른 것부터** 차감, 0 이 된 인스턴스 제거. 만료는 서버 틱에서 제거.
- API: `AddShield(type, sourceId, amount, duration)` / `RemoveShield(type, sourceId)` / `ClearShields()`. 구 `SetShield`·`IncreaseShield` 는 호출처 이관 후 제거.
- 이관: `FirstMeleeSubSkill` = Replace 정책 + 자기 인스턴스만 제거(다른 보호막 보존). `Unit.cs:237`·`Player.cs:1647`·`PlayerHealthHUD`(총합) 정리.
- **EditMode 테스트**: 흡수 순서, 부분 만료, Replace/Stack, 합계.

### G2. 기본 공격 공통 창구 `IPlayerBasicAttack`
- 상태기계·Player·EncounterLock·AnimationEvent/RootMotion 릴레이가 쓰는 메서드만 인터페이스로 뽑는다(`TryStart`·`CanStartApprovedAttack`·`BeginFromState`·`Tick`·`FixedTickMovement`·`CancelCurrentAttack`·`EndCurrentAttack`·`HitCurrentAttack`·`HandleAnimationEvent`·`HandleAnimatorMove`·`SetAnimator`).
- `RequireComponent(DefaultAttackController)` 3곳 제거 → `GetComponent<IPlayerBasicAttack>()`.
- **`DefaultAttackController` 를 base `Player.prefab` 에서 `Player_Paladin` Variant 로 옮긴다**(D-결정 필요 §6-1). `PlayableCharacterVisual.ApplyData` 의 `DefaultAttackData` 는 가붕이 구현 전용 분기로 남긴다.
- **완료 조건: 가붕이 동작 무변화**(은희 Play·MPPM: 콤보·대시 캔슬·연출 잠금).

### G3. 과열 + 거너 기본 공격 (D7)
- `GunnerHeat`(NetworkBehaviour, Variant 루트): 서버가 `heat`·`overheated`·`lastFireServerTime` 보관 → NetworkVariable. 단계 계산·냉각(일반/과열 속도)·`ResetToZero()`(E). 저장 단계 스냅샷 API `CaptureStage()`(과열 = 3단계). 이벤트 `StageChanged`·`OverheatEntered`·`OverheatCleared`(VFX 훅). 오너 로컬 예측 → 서버 값 수렴.
- `GunnerBasicAttack : IPlayerBasicAttack`: 좌클릭 유지 → 준비 동작 1회 → 간격마다 **오너가 "발사 + 조준" RPC**, 서버가 간격·과열·행동 가능 검사 후 **SphereCast(판정 폭) 첫 유효 대상 1체**(지형 마스크에서 종료) 피해 → `ServerAttackLanded` 발행 → 과열 증가(허공 포함). 최대치 도달 발사는 피해 적용 후 과열 전환. 이동 잠금, 조준 즉시 회전.
- `GunnerHeatHUD`(임시, D14): CombatHUD 에 막대 1개, 단계별 색·과열 깜빡임, 오너만.
- 데이터: `GunnerBasicAttackData`·`GunnerHeatData` SO(기획 §12.1·12.2 항목 전부 인스펙터).

### G4. Q — 충전 레이저 / 정신 집중 (D3·D4·D5)
- `PlayerSkillData` 에 **쿨 시작 시점**(`OnStart`/`OnServerCommit`) 추가 — 기존 스킬 기본값 = OnStart(무변화).
- 스킬이 활성 중 **좌클릭을 가로채는 훅**(`PlayerSkillController` → 활성 스킬 `OnPrimaryPressed`) — Q 발사용.
- `GunnerChargeLaserSkill`: 시작 시 `CaptureStage()`, `StatusEffectType.Focus`(Buff, 신규 비트) 부여 + `MoveSpeedModifier` 감속, 집중 시간 → 곡선으로 사거리·피해 보간, 최대 도달 후 유지시간 끝나면 자동 발사. 발사 = 두꺼운 박스 오버랩(지형에서 길이 절단), 적 피해 / 아군 `AddShield(GunnerCharge, Stack)`(시전자 제외) / 오브젝트 공통 피격, 대상별 1회. 쿨 = 발사 시. 쓰러짐·사망·조작 불가 = 발사·쿨 없음. **대시 취소 = 발사 없음 + 쿨 적용**(D2).
- 상태 아이콘 최소 UI: 오너 HUD 에 활성 Buff 아이콘 줄(현재는 `Focus` 만 아이콘 지정).
- `StatusEffectCategories` 표에 `Focus` = Buff 추가.

### G5. E — 냉각 백스텝 (D9)
- `GunnerCoolBackstepSkill`: 입력 순간 조준 저장 → 정반대 방향 고정 이동. `BeginDash` 에 **아군 차단 옵션**(모터 `blockOtherPlayers` 를 이 이동에만) 추가. 서버에서 `GunnerHeat.ResetToZero()`(막혀도 적용). 무적 없음. 동작 중 모든 행동·대시 불가, 종료 후 기본 공격 가능.

### G6. 우클릭 — 근접 간파 (D10·D11)
- `GunnerInterruptSkill`: `FirstMeleeInterruptSkill` 과 같은 판정 흐름(HitDelay/애니 이벤트 중 먼저, 앵커 오버랩, `isInterruptAttack:true`). 쿨 = 사용 시. 적중·간파 무관 **후폭풍 뒤로 이동**(공격 반대, 벽·오브젝트만 차단, 유닛 통과, 무적 없음). 빗나가도 연출·이동·쿨.

### G7. R — 추적 레이저 (D12)
- `GunnerTrackingLaser`(**서버 소유 NetworkObject 프리팹**, NetworkTransform): 생성 시 저장 단계·시전자 clientId 보관. 고정 속도 추적 → 대상 사망/제거 시 재탐색 반경 내 최근접(동거리는 서버 순서) → 없으면 제자리 주기 재탐색. 피해 주기마다 원형 오버랩, 주기당 대상 1회. 지속시간 만료·씬 전환(destroyWithScene)·**시전자 연결 해제** 시 Despawn. 시전자 쓰러짐·사망은 유지.
- `GunnerTrackingLaserSkill`: 기존 SingleTarget ClickToConfirm 재사용, R 재입력 취소(쿨 없음), 빈 곳 무시. 쿨 = 레이저 생성 시(G4 옵션 재사용).
- `DefaultNetworkPrefabs` 등록.

### G8. 공용 대시 우선 (D1·D2)
- 스킬별 `CanBeCanceledByDash`(기본 false = 기존 무변화) + 취소 콜백. 기본 공격 창구에도 동일. 거너: 기본 공격·Q·R = true, E·우클릭 = false.
- `PlayerStateController`: Attack/Skill 상태에서 대시 입력 → 취소 가능하면 취소 후 Dash.

### G9. `Player_Gunner` Variant + 테스트 스폰
- `Assets/2.Prefabs/Player/Gunner/Player_Gunner.prefab`, `Gunner_Armature.prefab`(gunner.fbx + laser_gun 소켓·HurtBox·앵커). **GlobalObjectIdHash 실기록 확인**(player-prefabs.md 절차). `CharacterDefinition_Gunner.asset`.
- 선택 UI 가 없으므로 **개발용 스폰 override**(Dev_Boot/테스트 씬에서 기본 프리팹을 거너로) — 정식 선택은 범위 밖.
- G2 직후 껍데기부터 만들어 G3~G8 을 거너로 바로 확인한다.

### 순서
`G0`(병행) · `G1` · `G2 → G9(껍데기) → G3 → G4 → G5 → G6 → G7 → G8` → 최종 MPPM.
G1 은 G4 전까지 어느 때든. 문서 갱신(player-prefabs.md 거너 항목, CONTEXT 인수인계)은 마지막.

---

## 4. 리스크

| # | 리스크 | 대응 |
|---|--------|------|
| R1 | G2 에서 base 의 NetworkBehaviour(`DefaultAttackController`)를 Variant 로 옮기면 NetworkBehaviour 순서가 바뀐다 | 동일 빌드 피어 간이라 호환 문제는 없지만 **Paladin MPPM 재검증**을 G2 완료 조건으로 |
| R2 | 보호막 목록 복제(`NetworkList`) 로 기존 단일 int 소비처가 깨짐 | `CurrentShield` 합계 API 유지, 호출처 전수 이관(§2 목록), EditMode 테스트 |
| R3 | 첫 플레이어 소유 네트워크 오브젝트 — 등록 누락·연결 해제 처리 | `DefaultNetworkPrefabs` 확인, `OnClientDisconnect` 에서 소유 레이저 Despawn, 2인 이탈 테스트 |
| R4 | G0 은 SVN `.meta` 수정 — Unity 켜진 채면 재임포트 충돌 | **Unity 종료 후 작업**, guid·`cmp` 확인 (CLAUDE.md §6) |
| R5 | 오너 예측 과열과 서버 값 불일치(핑) | 서버가 최종 판정, 오너는 표시만. 과열 직전 연사에서 서버 거절 시 로컬 롤백 |
| R6 | Unity Auto Refresh 꺼짐 — 새 스크립트 임포트 불가 | 컴파일 확인은 `dotnet build`, 임포트는 은희가 Unity 창 클릭 |
| R7 | 기획서와 다른 결정(D3 Q 피해 보간, D1 대시 우선)이 기획자 의도와 어긋날 수 있음 | §0 표로 명시해 기획 공유 |

---

## 5. 검증

- 단계마다 `dotnet build Assembly-CSharp-Editor.csproj --no-restore -m:1` 오류 0.
- EditMode: 보호막(G1), 과열 단계·냉각 계산(G3 순수 로직 분리), Q 보간(G4).
- **Play·MPPM 은 은희가 직접**(MCP 로 Play 금지). 기획서 §15 체크리스트 전 항목 + 가붕이 회귀(콤보·E 보호막·우클릭 간파·궁) + 거너 궁 중 시전자 이탈.

---

## 6. 승인 결정 (2026-09-30 은희 ✅ 승인)

1. **G2 `DefaultAttackController` 위치** — ✅ **Paladin Variant 로 이동**(base 는 캐릭터 무관)
2. **작업 분담** — ✅ G1·G2 는 Claude 직접, G3 이후 스크립트는 Codex 위임 가능, 프리팹·에디터 조작은 Claude

## 7. 진행

- ⏳ **과열 게이지 → 거너 고유 UI 프리팹**(2026-10-01, 은희 요청). OnGUI `GunnerHeatHUD` 삭제 →
  `Gunner/GunnerHeatGauge.prefab`(Screen Space Overlay Canvas, 하단 중앙 막대 + 한글 TMP 라벨, `GunnerHeatGauge` 가 오너만 표시) 를 `Player_Gunner` 에 중첩.
  공용 CombatHUD 와 분리(캐릭터 고유 자원 UI). 메뉴 `Tools/Player/Gunner/과열 게이지 프리팹 (임시 UI)` — 루트의 구 컴포넌트 Missing Script 도 제거.

- ✅ **G8 완료 — Play 확인**(2026-10-01). 대시 우선은 **행동이 허락할 때만**(가붕이 무변화):
  `IPlayerBasicAttack.CanBeCanceledByDash`(가붕이 false·거너 true), `PlayerSkillBase.CanBeCanceledByDash(phase)`(거너 Q = Focus 만), `CanCancelAimByDash`(거너 R).
  `PlayerStateController.Tick` 이 허락된 행동 중 대시 입력을 `TryBeginPredictedDash(cancelsAction: true)` 로 넘긴다(이동 조건 = `CanDashFromAction`: CC·연출·사망만 막음).
  `BeginDash` 가 상태를 덮어 공격은 `CancelCurrentAttack`, 스킬은 `SkillEndReason.DashCancelled`(신규) — 거너 Q 는 이때 **쿨 커밋**(D2). 조준 중엔 `CancelTargeting`(쿨 없음) 후 대시.
  서버 검증(`DashValidationPolicy`)은 행동 상태를 보지 않으므로 변경 없음.

- ✅ **G7 완료 — Play 확인(🔸 대상 사망 후 재탐색은 미검증)**(2026-10-01). **첫 플레이어 생성 네트워크 오브젝트.**
  - `GunnerTrackingLaserSkill`(Ultimate, SingleTarget ClickToConfirm 재사용 — R 재입력 취소·빈 곳 무시·사거리 밖은 기존 자동 접근):
    승인 = 확정 순간 단계 저장, 대상 위치에 `GunnerTrackingLaser` 서버 `Spawn(destroyWithScene)`, 쿨 시작. `castDuration`(0.3s) 뒤 자유.
  - `GunnerTrackingLaser`(NetworkObject + 서버 NetworkTransform): 고정 속도 추적, 대상 무효 시 `retargetRadius` 최근접(동거리 = 먼저 확인), 없으면 주기 재탐색,
    `damageInterval` 마다 원형 범위 적 1회(플레이어 제외, 시전자 명의), 지속시간 만료·**시전자 접속 종료** 시 Despawn. 반경은 NetworkVariable 로 임시 원기둥에 반영.
  - 메뉴 `Tools/Player/Gunner/R 추적 레이저 부착 (G7)` — 프리팹(+해시 실기록)·임시 머티리얼·SO·R 슬롯·애니 `Gunner_ULT_Cast`.
  - 🔸 기획 9.3 "생성 즉시 자유" vs 시전 모션: `castDuration` 0 이면 즉시 자유(모션은 잘림).

- ✅ **G6 완료 — Play 확인**(2026-10-01). `GunnerInterruptSkill`/`Data`(Interrupt 슬롯, `PlayerInstantSkill`):
  HitDelay 또는 Hit 이벤트 1회 → 앵커(`Gunner_Armature/InterruptAttack` 박스) Overlap 에 `isInterruptAttack` 피해(보스가 판정 — 가붕이와 같은 계약).
  같은 시각부터 각 시뮬레이션 피어가 후폭풍(공격 반대, `recoilDistance/Duration`) — **`PlayerMotor.PassThroughEnemiesOverride`** 로 적 통과·벽만 막힘. 빗나가도 이동·쿨·연출.
  임시 연출 = `GunnerBeamView.ServerInterruptBlast`(주황선). 메뉴 `Tools/Player/Gunner/우클릭 근접 간파 부착 (G6)` + 애니 `Gunner_RMB_Interrupt`.

- ✅ **G5 완료 — Play 확인**(2026-10-01). `GunnerCoolBackstepSkill`/`Data`(Sub 슬롯, Skill 상태):
  서버 시작 시 `GunnerHeat.ServerResetToZero`, 승인 조준의 정반대로 `distance/moveDuration` 속도 이동 — 시뮬레이션 피어가 `OnFixedTick` 에서
  `AddGroundedDisplacement`(모터 스윕이 막힘 처리), **`PlayerMotor.BlockOtherPlayersOverride`** 로 동작 중만 아군 차단. 무적·피해 없음.
  스킬 시스템: `PlayerSkillBase.OnFixedTick` + `PlayerSkillState.FixedTick → PlayerSkillController.FixedTick`(기본 no-op).
  메뉴 `Tools/Player/Gunner/E 냉각 백스텝 부착 (G5)` + 애니 `Gunner_E_Backstep`.

- ✅ **상태 분리 — Play 확인**(2026-10-01, 은희 결정: 목적 = 대시 규칙(G8)·외부 조회·구조 명확화).
  `PlayerActionState` 끝에 **`AttackReady`**(거너 평타 준비 자세)·**`Focus`**(Q 정신 집중) 추가.
  - `AttackReady` → 준비 시간이 차면 각 피어가 `Update` 에서 `Attack` 으로(상태 비복제, 서버는 시각으로 발사 검증). 준비 중 놓으면 `AttackReady → Idle`.
    `PlayerAttackState` 가 두 상태를 공유(서로 전이 시 공격 취소 없음). `Player.BeginAttackReadyState`, `EndAttackState` 는 둘 다 받음.
  - `Focus` = 스킬 상태 계열(`IsSkillState`). `PlayerSkillBase.EntryActionState`(기본 Skill, 거너 Q = Focus) 로 진입,
    발사 RPC(`GunnerBeamView`, Reliable)에서 `ChangeSkillPhase(Skill)` — 같은 스킬 유지(Exit 가 스킬 상태 계열끼리면 취소 안 함).
    `PlayerSkillController` 의 Skill 비교 3곳 → `IsInSkillState`. 가붕이는 두 상태에 들어가지 않는다.

- ✅ **G4 완료 — Q Play 확인**(2026-09-30).
  - 스킬 시스템 확장(기본값 = 기존 동작): `PlayerSkillData.commitCooldownManually` + `PlayerSkillController.CommitCooldownServer`(오너 HUD 미러 RPC),
    `PlayerSkillBase.ConsumesPrimaryInput`/`OnPrimaryPressed`(실행 중 좌클릭 → 서버), `WantsAimUpdates`(기본 = Hold), `OnOwnerTick`.
  - `StatusEffectType.Focus = 1<<13`(Buff) — 기존 `StatusEffectHUD` 가 이름·아이콘으로 표시(상태 아이콘 UI 는 이미 있었다 — §2 표 정정).
  - `GunnerChargeLaserSkill`/`Data`: 시작 시 단계 저장·Focus·감속, 좌클릭/자동 발사, 집중 곡선 → 사거리·피해 보간, 지형 절단 관통 박스(적·오브젝트 피해 + 아군 보호막 합산, 시전자 제외), 발사 시 쿨.
  - `GunnerBeamView`(NetworkBehaviour) — 임시 발사선 공용 + Q 발사 RPC(발사선·`Gunner_Q_Fire` 애니). 기본 공격 발사선도 여기로 이관.
  - 메뉴 `Tools/Player/Gunner/Q 충전 레이저 부착 (G4)` — SO 생성(수동 쿨·마스크·애니 상태), 컴포넌트 부착, Q 슬롯 배선, Q 애니 상태(시작→집중/이동집중→발사→회복).

- ✅ **G3 완료 — 연사·과열·명중 Play 확인**(2026-09-30). 🔸 초기 버그: SphereCast 가 자기 콜라이더·맵 트리거(Default)에 시작점부터 막혀 길이 0 → 자기 계층 무시 + 지형은 비트리거만. `Player/Gunner/`:
  - `GunnerHeatModel`(순수 계산 + `GunnerHeatState` 기준점), `GunnerHeatData` SO, `GunnerHeat`(NetworkVariable 기준점 1개 — 서버는 발사·E 때만 쓰고,
    전 피어가 같은 함수로 현재값 계산 → 오너 HUD 즉시·매 프레임 복제 없음. 단계/과열 변화 이벤트 = VFX 훅).
  - `GunnerBasicAttack : IPlayerBasicAttack` — 오너 시작 요청 → 서버 승인(준비 1회) → 오너가 간격마다 "한 발+조준" RPC → 서버가 간격(×0.75 허용)·과열·상태 검사 후 판정.
    놓음/과열 → 마지막 발 후속 동작 뒤 종료. 오너 무응답(간격×3) 시 서버가 끝냄. **피해 단계 = 이번 발의 증가 전 단계.**
  - `GunnerBeamAttack : BaseAttack` — SphereCast, 거리순 첫 유효 대상 1체, 지형(blocking) 먼저면 종료, 시체 건너뜀, `ServerAttackLanded` 발행.
  - `GunnerHeatHUD`(임시 OnGUI 게이지), 임시 발사선(LineRenderer) — VFX 전까지.
  - 부착 메뉴 `Tools/Player/Gunner/기본 공격·과열 부착 (G3)`(SO 2개 생성 + Variant 루트에 4 컴포넌트). EditMode `GunnerHeatModelTests` 5건.

- ✅ **G9 껍데기 생성 — 스폰 확인**(2026-09-30). 메뉴 `Tools/Player/Gunner/껍데기 생성 (G9)`(`Player/Editor/GunnerShellAuthoring.cs`, 재실행 시 있는 건 건너뜀)로
  `4.Animations/Player/Gunner/GunnerAnimatorController.controller`(파라미터 5 + 빈 Idle/Walk), `2.Prefabs/Player/Gunner/Gunner_Armature.prefab`
  (gunner.fbx + Animator·NetworkTransform 회전만·NetworkAnimator 오너·릴레이 2, `hand.r` 에 laser_gun), `Player_Gunner.prefab`(Variant, **GlobalObjectIdHash 816596077**, `DefaultNetworkPrefabs` 자동 등록) 생성.
  Dev Boot 캐릭터 선택 = 메뉴 `Dev/Dev Boot/캐릭터/`(EditorPrefs, 씬 필드 우선). 기본 공격 컴포넌트는 아직 없음(G3).

- ⏳ **G1 코드 완료 — Unity 임포트·EditMode·Play 확인 대기**(2026-09-30). `dotnet build` 오류 0.
  - 신규 `Unit/ShieldType.cs`(`ShieldType`·`ShieldStackPolicy`·`ShieldEndReason`·`ShieldTypePolicies`·`ShieldInstance`), `Unit/Editor/HealthShieldTests.cs`(5건).
  - `Health`: 보호막 = 인스턴스 목록(만료 빠른 것부터 소모, 만료 없음은 마지막). `Unit`: `AddShield`/`RemoveShield`/`ContainsShield`/`BreakShield`,
    `ServerShieldEnded(instance, reason)` 서버 이벤트, 만료는 코루틴(파생의 `Update` 가림 회피), `NetworkList<ShieldInstance>` 복제 + 합계 `_currentShield` 유지.
    **사망 시 모든 보호막 제거**(Cleared). `SetShield`·`IncreaseShield`·두 RPC 삭제(오너가 보호막을 직접 쓰던 창구).
  - `FirstMeleeSubSkill`: 만료 코루틴 삭제 → `AddShield(HolyShield, 자기 NetworkObjectId)` + 종료 통지로 연출(깨짐 = Depleted).
  - 🔸 동작 차이: 추락(`BreakShield`) 시 E 연출이 "깨짐" → **"걷힘"** 으로 바뀐다(피해 소진이 아니므로).
  - HUD 보호막 바 = 남은 합 / 부여 합(`ShieldInstance.grantedAmount`). ✅ 은희 Play 확인 → `f1e2fe1c`.
- ✅ **G2 완료 — 가붕이 기본 공격 Play 확인**(2026-09-30). `dotnet build` 오류 0.
  - 신규 `Player/IPlayerBasicAttack.cs`. `DefaultAttackController` 가 구현. `RequireComponent(DefaultAttackController)` 3곳 제거,
    `Player`·`PlayerStateController`(컨텍스트)·`PlayableCharacterVisual`·`PlayerEncounterLock`(직렬화 필드 → 런타임 조회)·애니/루트모션 릴레이가 인터페이스 사용, 전부 null 허용.
    `ApplyData(DefaultAttackData)` 는 콤보 구현일 때만. `PlayerEncounterLockAuthoring` 의 defaultAttack 배선 제거.
  - 프리팹(YAML): base `Player.prefab` 에서 `PlayerDefaultAttack`·`DefaultAttackController` 제거 →
    `Player_Paladin` 에 추가 컴포넌트로 이관(fileID `7301928374650192837`·`…838`). 기존 오버라이드 13건(공격 데이터·히트박스·레이어·slash 소켓 8)은 새 컴포넌트 값으로 옮김. 참조 fileID 전수 존재 확인.
