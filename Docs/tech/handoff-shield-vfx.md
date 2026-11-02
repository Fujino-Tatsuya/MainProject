# 핸드오프 — 보호막 VFX 공용화(금색·흰색) · 수호의 잔광/불굴의 가호 금색

> 2026-11-02 · 민경(Claude) · 브랜치 `feature/VFX` · 계획 [PLAN.md](../../PLAN.md) 맨 위(이 작업이 최신일 때).

## 바뀐 것 한눈에
| 전 | 후 |
|---|---|
| 팔라딘 `VFX/HolyShield`·`HolyShield_Break`·`AllyShield`·`AllyShield_Break`, 거너 `VFX/AllyShield`·`AllyShield_Break` 소켓(EffectSocketPlayer) | **삭제.** 공용 SO `9.ScriptableObject/Effects/PlayerShieldVfxSet.asset` 가 엔트리 4개·자리(몸)·배율 0.5 를 든다 |
| 금색 = `ShieldType.HolyShield`(옛 기본 E — 지금은 아무도 안 검) | 금색 = `ShieldType.HolyShield` + **특성 보호막 중 SO 목록**(`PAL_3_E_SHIELD` 수호의 잔광 · `PAL_4_HEAL_SHIELD` 불굴의 가호). 나머지(거너 Q 등) 흰색 |
| 수호의 잔광·불굴의 가호 보호막이 흰색으로 떴음 | 금색(본인·아군 모두) |
| 어쌔신 = `PlayerShieldVfx` 없음(받은 보호막 안 보임) | 어쌔신 루트에 `PlayerShieldVfx` 추가 |
| `StatusEffectVfxPresenter.body` 3캐릭터 비어 있음 | 팔라딘 = `Armature/VFXSocket/Middle` · 거너·어쌔신 = 새 `VFX/Body`(y 1) |

에셋(SVN, 민경 이동 완료): `50.Art/VFX/Common/Players/Shared/Shield/` — `FX_HolyShield(_Break)(_Entry)` · `FX_AllyShield(_Break)(_Entry)`·`_Barrier`·`_Motes`.

## `PlayerShieldVfx` (민경 작성 · 보호막 전용)
- 받는 쪽이 주인 — 복제된 보호막 목록(`Unit.ShieldInstance` type·sourceId)을 매 프레임 보고 배리어 하나(금색 우선)를 켜고 끈다. 시작·종료 RPC 없음.
- 루프 핸들을 직접 든다(`EffectManager.PlayLooping` → 걷힐 때 `Release`, 파괴 시 `ReleaseImmediate`). 옛 소켓 safetyTimeout 15초 제약 없음.
- 깨짐 = 서버 `ServerShieldEnded`(Depleted, 마지막 하나) → `BreakBurstRpc(holy)` → 몸 자리에 원샷. 색 판정 = `PlayerShieldVfxSet.IsHoly`.
- 피격 파문 = 지금 배리어 핸들의 인스턴스에 `ShieldRippleEffect.AddHit`(기존과 같음).
- 단죄의 방패 충격파·반격 자세 RPC 는 아래 `PaladinGuardRelay` 로 분리.
- 삭제: `PlayLocal()`·`ServerEnd(bool)`(오프라인 전용, 호출하는 곳 없었음).
- **새 금색 보호막 특성 추가** = SO `holyTraitIds` 에 특성 ID 한 줄.

## 분리 — `PaladinGuardRelay` (2026-11-02, 별도 커밋 · 동작 무변경)
`PlayerShieldVfx` 에 붙어 있던 **팔라딘 전용** 두 가지를 새 NetworkBehaviour `Player/Skill/PaladinGuardRelay.cs`(`Player_Paladin` 루트에만)로 옮겼다 — 보호막 연출이 모든 캐릭터에 붙으면서 팔라딘 코드가 거너·어쌔신에 딸려 가지 않게.
| 옮긴 것 | 쓰는 곳(칸 이름 `shieldVfx` → `guardRelay`) |
|---|---|
| `ServerPlayGuardCounterPose` + `GuardCounterPoseRpc`(반격·해제 자세 전 피어 CrossFade) | `FirstMeleeSubSkill.guardRelay` |
| `ServerInterruptWave` + `InterruptWaveRpc` + `IInterruptCueSink`(B3 충격파, `interruptWave` = `ShieldWave` 소켓) | `FirstMeleeInterruptSkill.guardRelay` · 간파 연출 디렉터는 sink 를 인자로 받으므로 변경 없음 |
- **은희 영역**(반격 자세·간파 충격파) — 코드는 줄 단위로 그대로 옮겼다. 확인: E 방어 → 반격 자세, E 재입력 해제, 대시로 E 끊기, 간파 성공 충격파(B3 켰을 때).
- 덤: `PlayerShieldVfx.OnDestroy` 가 `NetworkBehaviour.OnDestroy` 를 가리던 경고(CS0114)를 `override` + `base.OnDestroy()` 로 고침.
- 레거시 보관 프리팹(`Player/Legacy/Paladin`·`Player/Paladin/Paladin_VFX`)은 스폰되지 않아 손대지 않았다(그쪽 스킬 칸은 비어 있게 됨).

## 통합 — `PaladinSkillView` · `PlayerDashVfx` (2026-11-02, 별도 커밋 · 동작 무변경)
거너 `GunnerBeamView`·어쌔신 `AssassinSkillView` 처럼 팔라딘 전용 연출 창구를 하나로 모았다.
| 전 | 후 |
|---|---|
| `Player/Skill/PaladinGuardRelay.cs`(반격 자세·충격파) | **`Player/Skill/PaladinSkillView.cs`** — 파일 이름만 바꿔 guid 유지(스킬 칸 참조 그대로). 아래 둘도 흡수 |
| `PlayerSkillVfx` 의 최후의 심판 연출(칼 낙뢰·칼날 전기·대상 표식·마무리 낙뢰, RPC 4쌍) | `PaladinSkillView` 로 코드 그대로 이동. `FirstMeleeUltimateSkill.skillVfx` → `skillView` |
| `Effects/PaladinShareHealVfx.cs`(`Player_Paladin/VFX`, 나누는 불굴 전이) | `PaladinSkillView` 로 이동(칸 `shareHealSwirl`·`shareHealRibbon` …). 스크립트·컴포넌트 삭제 |
| `PlayerSkillVfx`(3 캐릭터 루트 — 남은 일은 대시 잔상뿐) | **`PlayerDashVfx`** 로 이름 변경(guid 유지 → 3 프리팹 참조 그대로). 대시 잔상 코드만 남김 |
- 칸 이름: `FirstMeleeSubSkill`·`FirstMeleeInterruptSkill` 의 `guardRelay` → `skillView`(같은 컴포넌트).
- **은희 영역**(`FirstMelee*Skill` 칸 이름·궁극기 RPC 위치) — 동작은 줄 단위로 그대로.
- 그대로 둔 것: `FirstMeleePassive` 의 발동 연출 RPC(자체가 NetworkBehaviour 라 창구 불필요) · 스킬 소켓 루프·클립 이벤트 소켓.
- SVN 에디터 도구 `50.Art/VFX/Scripts/Editor/GunnerDashSetup.cs`·`GunnerBackstepSetup.cs` 도 `PlayerDashVfx` 로 바꿨다(SVN 커밋 필요).
- 레거시 `Paladin_VFX.prefab` 의 `PlayerSkillVfx` 는 `PlayerDashVfx` 가 되고 옛 R 칸 값은 무시된다(스폰 안 됨).
- 확인: 팔라딘 R(칼 낙뢰·전기 → 대상 표식 → 마무리 낙뢰, 조준 취소 시 걷힘) · 3캐릭터 대시 잔상(호스트·클라 화면) · E 반격 자세 · 간파 충격파(B3) · 나누는 불굴 리본.

### 2단계 — 스킬 5종의 연출 칸도 `PaladinSkillView` 로 (2026-11-02, 커밋 1개 · 동작 무변경)
스킬은 "언제"(콜백·판정)만, `PaladinSkillView` 는 "무엇"(소켓·엔트리)만. 스킬에는 `skillView` 칸 하나만 남는다(Main·Passive 에 새로 추가).
| 스킬 | 옮긴 칸 → View 칸 | 스킬이 부르는 것 |
|---|---|---|
| Main | `smashLoop`·`trailLoop` → 같은 이름 | `OnClientPlay` `BeginSmashHold` / `OnEnd` `EndSmashHold` |
| Sub | `shieldGlow` → `shieldGlow` · `barrierLoop`·`auraLoop`·`chargeLoop` → `guardBarrierLoop`·`guardAuraLoop`·`guardChargeLoop` | `OnClientPlay` `BeginGuardVfx` / `OnEnd` `EndGuardVfx` |
| Interrupt | `shieldGlow`(Sub 와 같은 컴포넌트 → 한 칸) · `glowLoop`·`chargeLoop` → `interruptGlowLoop`·`interruptChargeLoop` | B1 `PlayCue`(디렉터가 스킬을 sink 로 되부름 — 메서드는 스킬에 남김) → `BeginInterruptGlow` / `OnEnd` `EndInterruptVfx` |
| Ultimate | `superArmorOverlay`·`stickVfx` → 같은 이름 | `ShowSuperArmor`·`HideSuperArmor`·`SetStickPlayRate` |
| Passive | `bladeGlow` → 같은 이름 · `additionalHit*` 5칸 → `passiveHit*` · `ProcVfxRpc` → `PassiveProcRpc` | 버프 엣지 `SetBladeGlow(bool)` · 발동 `ServerPlayPassiveProc(target, healed > 0)` |
- **은희 영역**: `FirstMelee*` 5개는 연출 호출만 바뀌었다. 판정·타이밍·데이터 무변경. 패시브의 데이터 테이블 열(`[DataTableIgnore]` 연출 배율)은 원래 시트에 없던 칸이라 시트 영향 없음.
- 프리팹: 값을 그대로 옮겼다(점검 스크립트로 27칸·스킬 5개 `skillView` 확인). 레거시 프리팹(`Paladin_VFX`·`Legacy/Paladin`)은 칸이 비게 된다(스폰 안 됨).
- 확인할 것(Play): 방패 스매시 루프·궤적 / E 방패 발광·결계·오라·기 모으기 + E 해제·대시로 끊을 때 전부 꺼짐 / 우클릭 방패 발광·루프·기 모으기·충격파 / R 슈퍼아머 오버레이·칼 꽂기 속도 / 패시브 칼날 발광·발동 추가 피해 연출·자기 힐.

## DebugTraitLab
- 아군 허수아비 `AllyDummy` 에도 `PlayerShieldVfx` + `VFX/States/StatusEffectVfxPresenter`(몸 = `VFX/Body` y 1) — 불굴의 가호가 아군에게 걸리면 금색이 보인다.
- 확인한 것(Play): 수호의 잔광 채널 → 팔라딘 금색, 불굴의 가호 채널 → 아군 허수아비 금색, 거너 Q 채널 → 흰색 판정 · 두 배리어 재생 핸들 정상.

## Play 체크리스트 (민경)
- [ ] 수호의 잔광: E 로 막은 뒤 피해 무시가 끝나면 팔라딘 금색 4초 · 허수아비 공격으로 깨면 금색 깨짐
- [ ] 불굴의 가호: 패시브 회복 → 팔라딘 금색 · 나누는 불굴 받은 아군(허수아비)도 금색
- [ ] 거너 Q 맞은 아군 = 흰색 · 흰색 위에 금색이 걸리면 금색으로 바뀜
- [ ] 크기·높이 — 팔라딘 Middle / 거너·어쌔신 `VFX/Body` y 1 (어쌔신 높이는 보고 조정)
- [ ] MPPM: 클라 화면에서도 같은 색

## SVN / git
- SVN: `Shared/Shield/`(이동분 — 옛 `Players/Shield/` 삭제 + 금색 4종 `Player1/ESkill/` → 이동) 커밋.
- git: `Effects/PlayerShieldVfxSet.cs`(+meta) · `Player/PlayerShieldVfx.cs` · `9.ScriptableObject/Effects/PlayerShieldVfxSet.asset`(+meta) · `Player_Paladin/Gunner/Assassin.prefab` · `0.Scenes/Debug/DebugTraitLab.unity` · 문서.
