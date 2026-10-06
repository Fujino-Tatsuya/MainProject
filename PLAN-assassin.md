# PLAN — 근접 캐릭터 "어쌔신" 구현 (2026-10-06 작성 · ✅ 은희 승인)

작성: 은희(Claude) / 브랜치: `feature/Assassin`(= `development` `e63d0f3b` 에서 분기)
기획 원본: [character_assassin.md](Docs/design/character/character_assassin.md) — **맨 아래 「구현 결정」 절이 본문보다 우선**
관련: [player-prefabs.md](Docs/tech/player-prefabs.md) · [PLAN-gunner.md](PLAN-gunner.md)(선례) · [PLAN-flatkit.md](PLAN-flatkit.md) · [data-table.md](Docs/tech/data-table.md)

> ✅ **담당 = 은희**(Unit·Player·UI 본인 영역). 분담 = **Codex: C# 로직** / **Claude: 아트 임포트·프리팹·SO·Animator·씬 배선·문서·EditMode 실행**.
> 🔴 **경석 영역(`MonsterBase`·보스) 은 §6 공유 후에만** 건드린다(백어택 A11). VFX 는 임시 파티클까지만 — 정식 연출은 민경.

---

## 1. Goal

`Player_Assassin`(Player.prefab Variant)을 스폰해 기획서 §2~§12 의 **일반/변신 평타·Q·E·R·간파·백어택·R 스택**이
MPPM 2~3인에서 기획서 §17 확인 목록대로 동작한다. **가붕이·거너는 SuperArmor 의미 통일(§3 A1) 외 동작이 바뀌지 않는다.**

범위 밖: 머리카락 2차 모션(고정), 캐릭터 선택 노출(`available = false`), 정식 VFX·SFX·아이콘 아트, 밸런스 확정,
CC 면역 이상의 새 상태이상 체계, 간파 수치 공통 SO 통합.

---

## 2. 현재 코드 사실 (2026-10-06 조사)

| 영역 | 사실 | 계획 영향 |
|------|------|-----------|
| 스킬 슬롯 | `PlayerSkillController` 직렬화 4필드, Awake 고정 바인딩. 교체 API 없음. 쿨 장부 `float[] nextReadyTime` = **슬롯 단위**, 서버 권위 + 오너 미러(`PlaySkillClientRpc`·`CommitCooldownClientRpc`). 감소·초기화 API 없음 | **A2** 스킬 단위 장부 + 대체 세트 |
| HUD | `SkillCooldownHUD` 아이콘·툴팁을 `Bind()` 때 1회. `PassiveHUD.cs:37` `GetComponent<GunnerHeat>()` 하드코딩. 스택 표시 없음 | **A2** 재바인딩 · **A5** 일반화 |
| 거너 고유 HUD | `GunnerHeatGauge.prefab` = 자체 Canvas, 오너만, **`Player_Gunner` 루트**에 중첩 | **A5** `Gunner_Armature/HUD` 로 이동 |
| Armature | 유령 상태에서 `PlayerSoulController` 가 `Armature`(aliveVisual) 를 끈다(`:176`) | 캐릭터 HUD 도 유령 중 숨김 — 의도 |
| 입력 | 범용 예약 없음(기획 §3.1 과 일치). 가붕이 평타만 콤보 창 래치(`DefaultAttackController.cs:719`). 콤보 리셋 유예 없음 | 어쌔신 평타 = 자체 `IPlayerBasicAttack` 구현 |
| 조준 | `GroundPoint` = 커서 지점, 사거리 밖일 때만 클램프(`PlayerSkillTargeting.cs:532`). 효과 범위 원 표시 없음 | **A4** 고정 거리 + 효과 반경 |
| SuperArmor | 몬스터 = SuperArmor 외 **모든 CC 적용 무시**(`MonsterStatusEffect.cs:74`). 플레이어 = 넉백(`Unit.cs:723`)·Push(`PlayerStateController.cs:188`) 만. **Carry 는 일부러 제외**(:187 주석 — 보스 Grab 체인) · Stun 적용 검사 없음 | **A1** 의미 통일 |
| 백어택 | 판정·배율 없음. `BossDataSO.backAttackAngle=60` + `BossDirectionIndicator` 표시만. 각도 판정 선례 `TwentyThreeBoss.IsCounterFromFront`(:4937) | **A11**(경석 공유 후) |
| 대상 분류 | 보스/몹 = `MonsterBase`(`Unit.Rank`: Normal/MidBoss/Boss) · 송전기 = `BossChargingPylon : Unit` · 상자 = `BreakableCrate`(Unit 아님, `IAttackReceiver`) | 공용 헬퍼 `AssassinHitTargets` |
| 무적 | `PlayerInvulnerability` 원인 토큰(Dash/LandingProtection/FallRecovery/Revive/Cinematic) | `SkillAction` 원인 추가 |
| 이동 스킬 | 오너 권위 모터. `motor.AddDisplacement`, 벽 스윕 자동 정지 + `WasBlockedThisTick`, `PassThroughEnemiesOverride`(거너 우클릭 선례) | Q 돌진 재사용 |
| 간파 | `FirstMeleeInterruptSkill`·`GunnerInterruptSkill` 복사 구현, 계약 = `AttackInfo.isInterruptAttack` | **A3** 베이스 추출 |
| 애니 | 가붕이 `.anim` = git(`4.Animations/Player/Garen`, 8MB급, LFS 없음). 거너 Animator = 독립 컨트롤러, 저작 메뉴가 생성 | 어쌔신 클립 git 이전 + 저작 메뉴 |
| 사망 훅 | `PlayerLifeCycleController.LifeStateChanged`(전 피어), `OnEnd(SkillEndReason.CasterDied)` | 스택·변신·강화 정리 |

---

## 3. 접근 — 단계별 (각 단계 = 커밋 1개 이상, `dotnet build` 오류 0 + Refresh 컴파일 확인 후)

### A0. 아트 준비 (Claude · MCP)
- `Assassin_v16_20261003.zip` → `Assassin_ActionHair_Review.unitypackage` 를 MCP 로 임포트 → **모델·텍스처만** `Assets/50.Art/Char/assassin/` 로 정리(검수 장면·`ActionHairTests` 브리지·머리 스크립트 제외).
  Built-in 재질은 쓰지 않는다 → `PlayableCharacter_Toon` Material Variant(Base Map 만) `3.Materials/Toon/Assassin_Toon.mat`.
- Rig = **Humanoid**(패키지 `.meta` 그대로 — 클립이 Humanoid 머슬 커브라 Generic 불가), Avatar = Create From This Model.
  패키지 내 `SelectedAnimations` 는 `Assassin_Selected_Animations.zip` 과 **guid·바이트 동일**(10-06 확인) → 출처 불일치 리스크 없음.
- 🔸 팔 비틀림 보정 `SDArmTwist.cs`(패키지 런타임 스크립트, forearm twist 헬퍼 본 분배)는 git `1.Scripts/Player/Assassin/` 로, guid 유지. Armature 모델에 붙인다.
- 텍스처는 툰 Base Map 용 `Assassin_BaseColor.png` 만(Metallic/Roughness 는 zip 에 남김). 패키지 Built-in 재질·임포트 헬퍼 스크립트 제외.
- `FlatKitSmoothNormalBaker.Targets` 에 FBX 추가 → Reimport.
- 클립(`Assassin_Selected_Animations.zip`) → **git** `Assets/4.Animations/Player/Assassin/`, `.meta` guid 유지. 대상 =
  Idle · Idle_Combat · Run_Combat_Fast_Start/Loop/Stop · Dodge_Combat_F_0 · Combo_Attack_Wave_01~04 · Speed_Attack_Loop ·
  Combo_Attack_02_04 · Combo_Attack_03_04 · Buff · Combo_Attack_02_01 · Skill_02_Move_000pct · Attack_Up_01 · Parry_R (18개, 87MB).
  제외 = `Combo_Attack_Wave_All`(참고용) · `Skill_02` 원본 · `Skill_02_Move_010~090pct`.
- SVN 커밋 → guid 불변 확인 → `art-svn.json` 핀 갱신.

### A1. SuperArmor 의미 통일 (코어 · Codex)
- 플레이어 `StatusEffectController.Apply`: SuperArmor 활성 중 **차단류 디버프(Airborne·Stunned·Slowed·Rooted·Silenced·Debilitated) 적용 무시**(둔화 포함 — 은희 확인 10-06) — `MonsterStatusEffect` 와 같은 규칙. 스탯 modifier 디버프는 대상 아님.
- `PlayerStateController.TryReceiveRestraint`: SuperArmor 면 **Carry 도 거부**(Push 와 동일).
- 영향: 가붕이 Q/R 시전 중 스턴·잡기 면역이 된다(§6 공유). Grab 체인 회귀 여부는 Play 확인 항목.
- EditMode: SuperArmor 중 차단류 Apply 무시 / 해제 후 정상 적용.

### A2. 슬롯 교체 + 스킬 단위 쿨 장부 (코어 · Codex)
- 장부를 **스킬 인스턴스 단위**로(기본 4 + 대체 세트). 비활성 스킬 쿨도 계속 감소.
- `PlayerSkillController.alternateSkills`(슬롯별, 비어 있으면 무시) + 서버 전용 `SetSlotOverride(slot, bool)`.
  현재 활성 세트 = **`NetworkVariable<byte>` 비트마스크**(늦은 접속·비오너 아이콘 일치).
  활성 스킬 실행 중에는 교체하지 않는다(종료 후 적용 — 호출자가 보장, 컨트롤러는 실행 중 요청을 보류).
- `ReduceCooldownServer(skill, seconds)`(하한 0) · `StartCooldownServer(skill)` → 오너 미러는 기존 `CommitCooldownClientRpc` 확장.
- 이벤트 `SlotBindingChanged` → `SkillCooldownHUD` 가 아이콘·툴팁·쿨 출처 재바인딩.
- 완료 조건: **가붕이·거너 무변화**(대체 세트 비어 있음). EditMode: 장부 키·감소 하한·교체 보류.

### A3. 간파 공용 베이스 (코어 · Codex)
- `PlayerInterruptSkillBase : PlayerInstantSkill` 추출 — Hit 이벤트/HitDelay 먼저 오는 쪽 1회, `isInterruptAttack`, 앵커 오버랩, 상자 포함, `RaiseServerAttackLanded`.
  가붕이·거너가 상속(거너 후폭풍만 오버라이드). 데이터는 지금처럼 캐릭터별 SO.
- 완료 조건: 가붕이·거너 간파 무변화(Play).

### A4. 고정 거리 조준 + 효과 반경 (코어 · Codex)
- `PlayerSkillData` 조준 설정에 `fixedDistance`(bool) · `aoeRadius`(float). `fixedDistance` 면 조준점 = 플레이어 + 수평 마우스 방향 × `range`(기본 사거리 원 숨김).
- `aoeRadius > 0` 이면 조준점에 효과 범위 원 데칼(`SkillRangeIndicator` 재사용).
- 서버: 확정 조준점을 플레이어 위치 기준으로 **재투영**(거리 오차 허용 밖이면 고정 거리로 보정).
- 기존 사용처(가붕이 R·거너 R) 무변화.

### A5. 캐릭터별 HUD 분리 (Claude 프리팹 + Codex 코드)
- 규약: 캐릭터 고유 HUD = `<Char>_Armature/HUD` 자식에 중첩, 자체 Canvas, **오너만 표시**. 유령 중 Armature 와 함께 숨김.
- 거너: `GunnerHeatGauge` 를 `Player_Gunner` 루트 → `Gunner_Armature/HUD` 로 이동(참조 `GetComponentInParent` 유지 확인).
- `PassiveHUD`: GunnerHeat 하드코딩 제거 → 플레이어의 `IPlayerPassive` / `ISkillTooltipSource` 구현에서 툴팁 출처를 찾는다.
- `SkillTooltipAuthoring` 에 어쌔신 경로 추가. `player-prefabs.md` 에 HUD 규약 추가.

### A6. `Player_Assassin` Variant + Animator + 평타 (Claude 저작 + Codex 코드)
- 저작 메뉴 `Tools/Player/Assassin/*`(거너 `GunnerShellAuthoring` 구조 복제): `Assassin_Armature.prefab`(모델·Animator·히트박스 앵커·VFX 소켓·`HUD`) ·
  `AssassinAnimatorController`(공통 파라미터 규약 + 어쌔신 상태) · **클립 애니 이벤트 심기**(`HandleDefaultAttackEvent`/`HandleSkillEvent`, 시점 값은 메뉴 코드 — 재실행 시 같은 결과) ·
  `Player_Assassin.prefab`(NetworkObject `SetDirty` → `GlobalObjectIdHash` 기록) · `CharacterRoster` Entry(`available = false`).
- 대기: `AssassinCombatIdle`(로컬) — 최근 `combatIdleSeconds`(5) 안 공격·피격이면 `Idle_Combat`, 아니면 `Idle`.
- `AssassinBasicAttack : IPlayerBasicAttack` — Wave_01~04, 유지 시 1→2→3→4→1, 클릭 예약 없음, 타 시작 시 방향 고정, 이동 불가, 종료 후 0.8초 내 재입력 = 다음 타,
  Q·E·R 전환 시 순서 초기화. 판정 = 서버 부채꼴(타별 사거리·각도·계수). **강화 상태면 강타로 분기**(A7).
  변신 상태면 Speed_Attack_Loop 4타 묶음(입력 1회 = 묶음 1회, 놓아도 완료).

### A7. 어쌔신 상태 · R 변신 · E 강화/강타 (Codex)
- `AssassinState`(NetworkBehaviour, Variant 루트): 서버 쓰기 NetworkVariable = `stacks`(0~4) · `transformEndServerTime`(0 = 일반) · `enhancedReady` · `releaseRequested`.
  순수 모델 `AssassinStateModel`(스택 상한, 지속시간 표 4/6/8/10, 해제 가능 2초, 종료 대기 규칙, 사망 정리) — EditMode 테스트.
- R(`Parry_R`): 1스택 이상·쿨 종료·행동 가능 → 스택 전부 소모, 변신 시작(**슬롯 Q/E 교체 = A2**), 강화 준비 중이면 제거 + 일반 E 쿨 6초(`StartCooldownServer`).
  Parry_R 중 이동·대시·공격·스킬 불가, 일반 피격·경직으로 끊기지 않음. 2초 이후 R 재입력 = 해제 요청.
  만료·해제 = 현재 공격 완료 후 종료(§10.3 표) → 슬롯 원복 → R 쿨 8초(실제 종료 시점부터 = 수동 커밋).
- 일반 E(`Buff`): 강화 준비(쿨 시작 안 함, 중복 불가) → 다음 평타 = `Combo_Attack_02_01` 강타 1회, **시작 시** 강화 소모 + E 쿨 6초.
  유효 대상(보스·몹·송전기) 적중 시 스택 +1(타격당 1, 상한 4). 변신 중 스택 획득 없음.
- 사망·쓰러짐(`LifeStateChanged`/`CasterDied`): 스택 0, 강화 제거 + E 쿨, 변신 즉시 종료 + R 쿨, 남은 타격·무적 제거.

### A8. Q 관통 돌진 — 일반/변신 (Codex)
- 입력 시 방향 고정, 4m / 20m/s, 모터 이동(루트모션 끔), `PassThroughEnemiesOverride`, 벽 스윕 정지 = 조기 종료.
- 경로 판정: 매 틱 폭 1.2m 스윕, 대상당 사용 1회 피해. 돌진 중 SuperArmor(A1 의미). 실제 시작 못 하면 쿨 없음.
- 변신 Q = 별도 스킬(대체 세트), 쿨 3초, 사용당 최초 유효 적중에서 `ReduceCooldownServer(1.5)` 1회.

### A9. 변신 E — 원형 5타 (Codex)
- 조준 = A4(`fixedDistance` 1.5m, `aoeRadius` 2m). 조준 중 이동 가능. 조준 중 변신 종료 → 조준 취소, 쿨 없음.
- 확정 시 플레이어 위치·원 중심 고정, `Skill_02_Move_000pct`(모델만 이동), 5타 각 Hit 이벤트마다 원 안 대상 재판정(지형 차단 무시).
- 공격 중 이동·대시·스킬 불가, **무적**(`InvulnerabilityCause.SkillAction`), 사망·강제 중단 시 해제. 쿨 12초(공격 시작).

### A10. 간파 (Codex)
- `AssassinInterruptSkill : PlayerInterruptSkillBase`, `Attack_Up_01`, 수치 = 가붕이 간파 SO 복사. 일반·변신 공통(교체 없음).

### A11. 백어택 (코어 + 보스 · Codex) — 🔴 **경석 OK 후**
- `AttackInfo` 에 `backAttackMultiplier`(기본 1) · `forceBackAttack`(bool).
- 보스(`MonsterBase` 피해 진입점, Rank=Boss): 공격자 위치가 후방 ±`backAttackAngle` 이면 배율 적용. `forceBackAttack` 이면 **보스·일반 몹** 모두 위치 무관 적용.
  송전기·상자 = `MonsterBase` 아님 → 자연 제외.
- 어쌔신: 모든 타격에 배율 1.2 + 변신 중 `forceBackAttack`. 패시브 툴팁 출처 = `AssassinPassive`(백어택 + R 스택 설명).

### A12. 어쌔신 HUD · 임시 VFX · 툴팁 (Claude + Codex)
- `AssassinHUD.prefab`(→ `Assassin_Armature/HUD`): R 스택 구슬 4 · 변신 남은 시간 바(2초 해제 눈금) · E 강화 준비 강조 · 변신 중 Q/E 아이콘은 A2 재바인딩.
- 임시 VFX: 변신 중 · 강화 준비 · 백어택 적중 · 변신 E 원 — 기존 공용 파티클을 `AssassinSkillView`(연출 RPC 창구) 프리팹 필드에 꽂음. 민경 교체용 목록을 문서에.
- 툴팁: 스킬 SO·패시브 `displayName`·`description` + `tooltip.icon`(아트 대기).

### A13. 데이터 테이블 Export (Claude)
- 모든 어쌔신 SO·컴포넌트에 `[DataTableSheet("Assassin", Order = n)]`, 기술 값 `[DataTableIgnore]`.
- `GameData.xlsx` SVN 잠금 → 병합 Export(`Assassin` 시트 + `tooltip.*` TODO) → Verify 0 → SVN 커밋 + 핀 갱신. 🔴 xlsx 와 코드는 같이 나간다.

### 순서
A0 → A1 → A2 → A3 → A4 → A5 → A6 → A7 → A8 → A9 → A10 → A12 → A13, **A11 은 경석 OK 시점에 끼운다**(A7 이후 어디든).
A1~A4 는 코어라 각 단계 끝에서 가붕이·거너 회귀 Play 를 은희에게 요청한다.

---

## 4. 리스크

| 리스크 | 대응 |
|---|---|
| A1 SuperArmor 통일로 보스 Grab 체인·가붕이 밸런스 변화 | §6 공유. Play 항목에 23호 잡기 + 가붕이 Q/R 중 잡기·스턴 |
| A2 장부 키 변경 → 기존 쿨·HUD 회귀 | 대체 세트 빈 상태에서 무변화 EditMode + Play |
| 클립 git 이전 ≈110MB(Idle 2개만 42MB) | 사용 클립만. 추가 이동 비율은 필요 시 |
| 클립이 Generic 리그와 안 맞음(별도 패키지 출처) | A0 에서 임포트 직후 재생 확인, 안 맞으면 v16 패키지 클립으로 대체 |
| 애니 이벤트 = git `.anim` 수정 → 저작 메뉴 재실행 시 중복 | 메뉴가 이벤트를 **교체**(누적 아님) |
| 변신 종료 대기 중 사망·연출 잠금 경합 | 순수 모델에서 우선순위(사망 > 완료 대기) 테스트 |
| 오너 권위 이동 + 서버 판정 — Q 경로 피해가 오너 위치와 어긋남 | 서버는 시작 위치·방향·거리로 경로를 재구성해 판정(벽 조기 종료는 오너 보고 거리를 상한 검증) |
| 백어택 보류가 길어짐 | A11 외 단계는 독립. 배율 필드 없이도 키트 동작 |

---

## 5. 검증

- **EditMode(Claude 직접 실행)**: `AssassinStateModel`(스택·지속·해제 2초·종료 대기·사망 정리) · 평타 순서/0.8초 · 쿨 장부(키·감소 하한 0·교체 보류) · SuperArmor 차단 · 고정 거리 조준 투영.
- **Play·MPPM(은희)**: 기획서 §17 전 항목 + 회귀(가붕이·거너 쿨·간파·HUD, 거너 과열 게이지 이동, SuperArmor 중 잡기·스턴, 23호 Grab 체인) + 비오너·늦은 접속 HUD·아이콘.
- 데이터 테이블 Verify 0, `dotnet build` 경고 신규 0.

---

## 6. 경석 공유

은희가 직접 전달(10-06). 공유 항목 = ① 백어택 판정(`AttackInfo` 배율·강제 플래그 + `MonsterBase` 피해 진입점에서 `backAttackAngle` 판정)
② 플레이어 SuperArmor 의미 통일(CC·잡기 거부 — `PlayerStateController.cs:187` Grab 체인 회귀 내용 확인 요청).
답 오면 여기에 결과를 적고 A11 착수.

---

## 7. 승인 결정 (2026-10-06 은희 grill)

Q1 전체 키트 단계 진행 · Q3 머리카락 고정 · Q4 MCP 임포트 · Q5 Codex/Claude 분담 · Q7 `available=false` · Q8 모델=v16, 클립=Selected ·
Q9 기획 사본 + 구현 결정 절 · Q10 SO 먼저, xlsx 마지막 · Q11 UI 전부 · Q12 Skill_02_Move_000pct · Q13 한 브랜치 ·
Q14·Q25 백어택 = 보스 공통 판정, 경석 공유 후 · Q15·Q21 슬롯 교체 API · Q16·Q28 SuperArmor 몬스터와 통일(전역, 진행) ·
Q17 고정 거리 조준 · Q18 간파 베이스 추출 · Q19·Q24·Q27 캐릭터 HUD = Armature/HUD, 거너 게이지도 이동 · Q20 임시 VFX ·
Q22 `AssassinState` NetworkVariable · Q23·Q26 클립 git + 애니 이벤트(사용 클립만) · Q29 Idle/Idle_Combat 전환 · Q30 EditMode + Play 혼합.

## 8. 진행

- ✅ A0(10-06): SVN **r383**(모델·BaseColor, 핀 383) + git 클립 18·`SDArmTwist`·`Assassin_Toon.mat`·SmoothNormal 목록. 메타·클립 바이트 원본 일치 확인.
  🔸 모델에 단검 본 없음(클립의 `Dagger_*` 커브는 바인딩 대상 없음) — 단검이 몸 메시에 스킨됐는지 A6 에서 확인, 없으면 아트 요청.
  🔸 FBX 재임포트 시 "Importer generated inconsistent result" 경고 1건 — A6 에서 재확인.
- ✅ A1(10-06): Codex `05055211` — `StatusEffectImmunityPolicy`(차단류 6종 무시) · Carry 거부. EditMode `Tools/Tests/플레이어 EditMode 테스트 실행` 27/0.
  보스 쪽 거부 처리 확인: 23호 `AttachGrabbed`·`GrabController.CallPlayerBeginGrab` 모두 거부 시 해제 → Recovery. ⏳ Play(가붕이 Q/R 중 23호 잡기·스턴).
