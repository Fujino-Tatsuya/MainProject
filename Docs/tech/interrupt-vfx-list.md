# 간파 스킬 캐릭터 VFX 목록 + 랩 개별 토글 (할 일 238)

> 플레이어 캐릭터(팔라딘·거너·어쌔신) **간파 스킬 자체**의 VFX 목록과, DebugInterruptLab 에서 하나씩 켜고 끄는 방법.
> 공용 간파 연출(`InterruptProductionDirector` 의 V3 적중 라이트·암순응·블룸·화이트아웃·불꽃·빛줄기·슬로우 등)은 **이 문서와 토글 범위 밖**이다
> — 그쪽은 [interrupt-presentation.md](interrupt-presentation.md) 와 랩의 '단계·버전 선택'·'화면 효과' 섹션.
> 예외 하나(할 일 238, 11-06): **거너가 간파 성공하면** 공용 V3 적중 라이트의 빛줄기(`Rays`)·불꽃(`Sparks`)을 빼고 포인트 라이트를 거너 이펙트 파랑(`FX_Arrow_hail` LightFlash)으로 — 일반·대쉬·제압 모두.
> 칸은 디렉터 `gunnerHitLight`(랩 토글 아님), 상세 [interrupt-presentation.md §9.10](interrupt-presentation.md).

## 1. 목록

- **언제**: 일반·대쉬·제압 간파 모두 같은 VFX 가 나온다. 대쉬 간파는 대시 중 상체 레이어(`DashInterruptUpper`)에서 같은 클립을 재생하므로
  클립 이벤트 VFX 가 그 레이어에서 나고, 대시 끝에 0층으로 넘겨받을 때 상체 레이어를 즉시 비워 두 번 나지 않는다(`PlayerDashController.HandOffDashInterruptToBase`).
  제압 간파 전용 캐릭터 VFX 는 없다.
- **누가 보나**: 아래 VFX 는 전부 **전 피어**(각 피어가 자기 화면에 재생). 오너 전용은 없다.
- **사건**: 시도 = 시전 시작(`OnClientPlay`), 판정 = 클립 Hit 이벤트(서버), 성공 = 디렉터 성공 단계가 시전자 relay 를 부름(서버) → RPC, 실패 전용 VFX 는 없다.

### 팔라딘 — 단죄의 방패 (`Player_Paladin.prefab`, 클립 `Assets/4.Animations/Player/Garen/Garen_Parry.anim`)

| 토글 키 | 이름 | 프리팹·에셋 | 언제 | 스폰 코드 | 디렉터 칸 |
|---|---|---|---|---|---|
| `PaladinShieldMaterialGlow` | 방패 문양 발광(머티리얼 페이드) | 방패 메쉬 `SM_Wep_Shield_01` 의 `MaterialFadeEffect` (E 수호자의 의지와 공용) | 시도 — 시전 시작 ~ 스킬 끝(FadeOut) | `PaladinSkillView.BeginInterruptGlow` ← `FirstMeleeInterruptSkill.PlayCue`(디렉터 시도 cue) | B1 |
| `PaladinShieldGlowLoop` | 방패 발광 루프 | 소켓 `VFX/ShieldInterrupt/ShieldGlow` · `50.Art/VFX/Common/Players/Player1/RightClickSkill/FX_ShieldGlow(_Entry)` | 시도 — 시전 시작 ~ 스킬 끝(Stop) | 같은 곳 | B1 |
| `PaladinShieldCharge` | 방패 기 모으기 | 소켓 `VFX/ShieldInterrupt/ShieldCharge`(id `ShieldCharge`) · `…/RightClickSkill/FX_ShieldCharge(_Entry)` | 클립 0초 `StartEffect` ~ Hit(0.333초) `StopEffect`, 판정 전에 끊기면 `EndInterruptVfx` 가 회수 | `EffectAnimEvents.StartEffect` | B2 |
| `PaladinShieldWave` | 성공 충격파 | 소켓 `ShieldWave` · `…/RightClickSkill/FX_ShieldWave(_Entry)` | 성공 — 판정 1회에 한 번(서버 relay → `InterruptWaveRpc` 전 피어) | `PaladinSkillView.PlayCue`(PaladinWave) | B3 |

### 거너 — 근접 간파 (`Player_Gunner.prefab`, 클립 `gunner.fbx` 의 `gunner_skill_RMB_interrupt` — 이펙트 이벤트 없음)

| 토글 키 | 이름 | 프리팹·에셋 | 언제 | 스폰 코드 | 디렉터 칸 |
|---|---|---|---|---|---|
| `GunnerMuzzleBlast` | 총구 폭발 | 소켓 `InterruptFx`(`GunnerBeamView.interruptPlayer`) · `50.Art/VFX/Common/Players/Player2/RightClick_Interrupt/FX_Arrow_hail(_Entry)` · 소켓이 비면 임시 LineRenderer | 판정 직후 — 적중·간파 성공과 무관(빗나가도), 서버 `ServerInterruptBlast` → `InterruptBlastRpc` 전 피어 | `GunnerBeamView.ShowInterrupt` ← `GunnerInterruptSkill.OnInterruptResolutionCompleted` | 없음 |

### 어쌔신 — 우클릭 간파 (`Player_Assassin.prefab` `VFX/RightClick/*`, 클립 `Assets/4.Animations/Player/Assassin/Attack_Up_01.anim`)

| 토글 키 | 이름 | 프리팹·에셋 | 언제 | 스폰 코드 | 디렉터 칸 |
|---|---|---|---|---|---|
| `AssassinWindup` | 기 모으기 | 소켓 `WindUp`(id `Assassin_InsightWindup`) · `50.Art/VFX/Common/Players/Player3/RClick/FX_Assassin_RClick_Windup(_Entry)` | 클립 0초 `StartEffect` ~ 0.217초 `StopEffect`, 끊기면 `AssassinSkillView.EndInterrupt` 가 회수 | `EffectAnimEvents.StartEffect` | C1 |
| `AssassinPop` | 튀기기 | id `Assassin_InsightPop` · `…/RClick/FX_Assassin_RClick_Pop(_Entry)` | 클립 0.217초 `PlayEffect` | `EffectAnimEvents.PlayEffect` | C1 |
| `AssassinSlash` | 올려치기 | 소켓 `Slash`(id `Assassin_InsightSlash`) · `…/RClick/FX_Assassin_RClick_Slash(_Entry)` | 클립 0.35초 `PlayEffect`(= Hit) | `EffectAnimEvents.PlayEffect` | C1 |
| `AssassinSuccess` | 간파 성공 | 소켓 `Slash_Success` · `…/RClick/FX_Assassin_RClick_Success(_Entry)` | 성공 — 서버 relay → `InterruptSucceededRpc` 전 피어, 올려치기 프레임과 성공 알림이 둘 다 온 순간 | `AssassinSkillView.PlayCue`(AssassinInsightSuccess) | C2 |

### 목록에만 있고 토글 없는 것

- **공용 성공 이펙트 칸(A4, `InterruptSuccessFx` — `Player.prefab` 루트)**: 캐스터·대상 칸이 base 와 세 Variant 모두 비어 있어 지금은 아무것도 안 나온다. 칸을 채우면 이 문서와 토글에 추가한다.
- **피격 쪽 일반 타격 이펙트**(맞은 몬스터·보스의 히트 이펙트)는 간파 스킬 VFX 가 아니라 공용 전투 연출이라 범위 밖.

## 2. 랩 개별 토글 (`InterruptVfxToggles`)

- 위치: DebugInterruptLab 튜닝 패널 → 섹션 **'캐릭터 VFX 개별 켜고 끄기 (238)'** — 전체 켬/끔, 캐릭터별 켬/끔, VFX 하나당 토글 하나.
- 공용 게이트 한 곳: `Assets/1.Scripts/Player/InterruptVfxToggles.cs`(정적, 키 = `InterruptVfx` enum). 스폰 지점이 `IsOn(키)` 만 묻는다.
  - 코드에서 직접 내는 것: `PaladinSkillView.BeginInterruptGlow`·`PlayCue`, `GunnerBeamView.ShowInterrupt`, `AssassinSkillView.PlayCue`.
  - 클립 이벤트 VFX: `EffectAnimEvents.PlayEffect`·`StartEffect` 가 id → 키(`InterruptVfxToggles.TryGetAnimEffect`)로 묻는다. 꺼도 `Played` 이벤트는 그대로 난다 — 어쌔신 성공 VFX 가 올려치기 프레임을 기준으로 쓰기 때문.
- 규칙
  - 기본 전부 켬. 끄면 그 VFX **재생만** 건너뛴다 — 판정·슬로우·공용 간파 연출·다른 VFX 는 그대로. 끄는 쪽(Stop·FadeOut·회수)은 막지 않는다.
  - 디렉터 칸(B1·B2·B3·C1·C2)과 **둘 다 켜져 있어야** 나온다. 그 칸들은 11-06(할 일 238 사용자 결정)부터 `NetworkManager.prefab` **기본 켬** —
    팔라딘·어쌔신 간파 VFX 가 실전에 나온다(할 일 104~238 사이엔 꺼져 있어 안 나왔음, 거너는 칸이 없어 늘 나옴). 그래서 랩 토글 기본(전부 켬)이 실전과 같다.
    랩 토글은 '실제로 나오는가'(`IsEffective` = 토글 ∧ 칸)를 보여 주고, 켤 때 그 칸도 런타임으로 켠다(`SetEffective`, 저장 안 함). 끌 때는 토글만 끈다(칸은 같은 칸의 다른 VFX 도 막으므로).
  - 로컬(복제 안 함) — 이 피어 화면만. 저장 안 함(PlayerPrefs 없음) — Play 를 시작할 때마다 전부 켬(`RuntimeInitializeOnLoadMethod(SubsystemRegistration)`).
  - 에디터·개발 빌드에서만 동작(`GateActive`). 릴리스 빌드는 늘 켬.
  - 루프(방패 발광·기 모으기)를 재생 중에 꺼도 이번 재생은 끝까지 간다 — 다음 시전부터.
- 새 간파 VFX 를 추가할 때: `InterruptVfx` 에 키 + `Label`·`CharacterOf`(클립 이벤트면 `TryGetAnimEffect`) → 스폰 지점에 `IsOn` 한 줄 → 이 문서 표.
- 테스트: `Assets/1.Scripts/Dev/InterruptLab/Editor/InterruptVfxTogglesTests.cs`(메뉴 `Tools/Tests/간파 랩 EditMode 테스트 실행` 에 포함).

## 3. 랩 캐릭터 변경 버튼

- 튜닝 패널 섹션 **'캐릭터 변경'** — 팔라딘/거너/어쌔신 버튼. 같은 오브젝트의 `DebugCharacterCycle.TrySwapToServer` 를 부른다
  (F1 순환·특성 랩 버튼과 같은 길: 서버가 지금 PlayerObject 를 디스폰하고 같은 자리에 새 Variant 를 `PlayerSpawnService.SpawnCharacter` 로 세움).
- **호스트 화면에서만** 된다(새 RPC 없음 — 클라 창은 '호스트 화면에서만' 안내). 바꾸면 HP·쿨·상태가 새로 시작된다. VFX 토글 값은 정적이라 그대로 남는다.
