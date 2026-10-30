# handoff — 어쌔신 R 변신 VFX 정식 교체 (Blade Vortex)

> 받는 사람: **은희**(플레이어 영역 소유). 작성: 민경(Claude), 2026-10-14, 브랜치 `feature/VFX`. 계획 = [PLAN.md](../../PLAN.md) 맨 위.
> 요지: `AssassinSkillView` 의 **변신 루프만** 임시 Instantiate 경로에서 EffectManager 정식 VFX 로 바꿨다.
> 평타·Q 궤적·E·간파·백어택 임시 연출은 그대로다. 게임플레이 코드(AssassinState·스킬 판정)는 손대지 않았다.

## 1. 바뀐 동작
| 시점 | 전 | 후 |
|---|---|---|
| R 시전(`OnClientPlay`) | `Poof_electric` 1회 | **없음** — 루프의 차오름(0.5초)이 시전 연출을 겸한다 |
| 변신 중(`TransformChanged(true)`) | `CharacterCirclePurple` 를 `transformSocket` 에 Instantiate | `EffectCatalog.Assassin_R_BladeVortex` 를 **플레이어 루트**에 `PlayLooping` |
| 변신 종료(`TransformChanged(false)`) | StopEmitting + 2초 뒤 Destroy | `Release` — End 원샷 + 남은 입자 ×8 배속으로 **0.5초에** 흩어짐 |

- 종료는 시간 만료·2초 뒤 수동 해제·쓰러짐/사망 **모두 같은 이벤트**(`ResetForDownServer` → 상태 쓰기 → `TransformChanged(false)`)라 같은 연출이 나간다.
- 늦은 접속은 지금처럼 `SyncLoops`(Start·OnNetworkSpawn)가 현재 상태로 맞춘다. 이미 켜져 있으면 다시 켜지 않는다(핸들 확인).
- 디스폰·씬 언로드(`OnDestroy`)는 아웃트로 없이 `ReleaseImmediate`.

## 2. 코드
- `Assets/1.Scripts/Player/Assassin/AssassinSkillView.cs`
  - 삭제: `transformLoopPrefab` · `transformCastPrefab` · `transformSocket` 필드, `PlayTransformCast()`, `SetLoop()`.
  - 추가: `transformEffectOffset`(Vector3, 기본 0 — 프리팹 피벗이 발밑) · `transformEffectScale`(기본 1), `EffectHandle transformLoop`.
  - 엔트리가 비거나 씬에 EffectManager 가 없으면 **경고 후 연출만 빠진다**(게임플레이 영향 없음).
- `Assets/1.Scripts/Player/Assassin/AssassinTransformSkill.cs` — `OnClientPlay` 본문을 비웠다(추상 메서드라 빈 override 유지).
- `Assets/1.Scripts/Player/Editor/AssassinShellAuthoring.cs` — 지운 세 필드를 채우던 줄·경로 상수 제거. 다시 돌려도 변신 루프는 건드리지 않는다.
- `Assets/2.Prefabs/Player/Assassin/Player_Assassin.prefab` — 위 필드 직렬화 줄 정리(세 줄 삭제, 두 줄 추가). `Armature/VFX/Transformation` 소켓 오브젝트는 이제 참조가 없지만 **남겨 뒀다**(지워도 된다).

## 3. VFX 쪽(민경 영역, 참고)
- `EffectEntry.outroPlayRate`(기본 1) 신설 — `EffectManager.Release` 가 기존 루프 파트에 StopEmitting 과 함께 이 배속을 건다. 반납 시 1 로 복귀. 기본 1 이라 다른 엔트리 동작 불변.
- 에셋: `Assets/50.Art/VFX/Common/Players/Player3/R_Transform/` — `FX_Assassin_BladeVortex(.prefab|_End.prefab|_Entry.asset)`. Piloto Studio 의존 23개는 `50.Art/VFX/{ShaderGraph,_Materials,Textures,Models}/PilotoStudio/`. **전부 SVN**(민경 svn add).

## 3-1. 변신 E 원형 5타 루프 (2026-10-15 추가)
- `AssassinCircleStrikeSkill.OnClientPlay` → `view.BeginCircleStrike(원 중심, AreaRadius, MaxActiveDuration)`, `OnEnd`(완료·중단 공통) → `view.EndCircleStrike()`.
  원 중심은 기존과 같은 근사(플레이어 + 승인 방향 × CenterDistance). 임시 `Burst_rings` 1회(`circleStrikePrefab`)는 처음엔 유지했다가 **2026-10-30 필드·코드째 삭제**(민경) — 이제 루프만.
- `AssassinSkillView`: `PlayCircleStrike` → `BeginCircleStrike`/`EndCircleStrike` + `circleStrikeScaleFactor`(기본 1, 배율 = 원 반경 × 이 값).
  클라에서 종료 RPC 가 막혀 `OnEnd` 가 안 오면 최대 지속 + 1초 뒤 스스로 해제(안전망). 재시작 시 남은 루프를 먼저 해제.
- 엔트리 `Players/Player3/E_CircleStrike/FX_Assassin_MultiSlashes_Entry` · 카탈로그 `Assassin_E_MultiSlashes`. `outroDuration` 1.1 — Release 가 방출만 멈추고 남은 입자가 제 곡선대로 사라진다(0 이면 즉시 지워져 뚝 끊김).

## 3-2. 변신 Q 돌진 루프 (2026-10-15 추가)
- `AssassinDashStrikeSkill.OnClientPlay` → 데이터가 `AssassinTransformedDashStrikeSkillData` 일 때만 `view.BeginStormSlash(MaxActiveDuration)`, `OnEnd`(완료·벽 조기 종료·중단 공통) → `view.EndStormSlash()`. 일반 Q 는 변화 없음.
- 임시 돌진 궤적(`dashTrailPrefab`)은 **삭제**(10-16) — 변신 Q 는 Storm Slash, 일반 Q 는 아래 §3-3.
- `AssassinSkillView`: `BeginStormSlash`/`EndStormSlash` + `stormSlashOffset`(0)·`stormSlashScale`(1). 플레이어 루트 추종, 안전망(최대 지속 + 1초) 동일.
- 엔트리 `Players/Player3/Q_TransformedDash/FX_Assassin_StormSlash_Entry` · 카탈로그 `Assassin_Q_StormSlash` · `outroDuration` 1.1. 입자는 World + Rate over Distance — 움직인 만큼 지나간 자리에 남는다.

## 3-3. 일반 Q 관통 일섬 (2026-10-16 추가)
- `AssassinDashStrikeSkill.OnClientPlay`(일반 데이터) → `view.BeginPierce(시작 위치, 돌진 방향)` — 기억만 한다. `OnEnd`(사유 무관) → `view.EndPierce()`.
- `EndPierce`: 길이 = 돌진 방향으로 실제 나아간 거리. `Assassin_Q_PierceLine` 을 시작점에 축별 배율 `(s, s, 길이)`로, `Assassin_Q_PierceBurst` 를 끝점에 배율 `s` 로 원샷. 길이 < `pierceMinLength`(0.3m)면 일섬 생략.
- `AssassinSkillView`: `dashTrailPrefab`·`BeginDashTrail`/`EndDashTrail`·`Release(ref)` **삭제**, `pierceScale`(1)·`pierceMinLength`(0.3) 추가. `AssassinShellAuthoring` 의 해당 줄도 삭제.
- `EffectManager.Play(entry, pos, rot, Vector3 axisScale)` 오버로드 신설(VFX 코드) — localScale 에만 축별로 곱한다. 기존 float 배율 경로는 동작 불변.
- 에셋 `Players/Player3/Q_Dash/FX_Assassin_PierceLine`·`_PierceBurst`(+Entry, duration 0.6·1.0).

## 3-4. 일반 E 단검 강화 — 날 붉은 발광 + 오라 + 차지 (2026-10-16 추가)
- ✅ **공용 셰이더 `ToonLit`·`MaterialFadeEffect` 는 수정 없음**(10-16 2차 — 처음 넣었던 발광·날 마스크·Color 모드는 되돌렸다). 칼날 발광 = **팔라딘 패시브와 같은 방식**:
  양손 단검 렌더러 `m_Materials` = [0] 원래 `Assassin_Toon.mat` + [1] 오버레이 `50.Art/VFX/_Materials/Dagger_Blade_Glow.mat`(`Shader_IntegratedEffect`, 마스크 `50.Art/VFX/Textures/Dagger_BladeMask.png` = 단검 UV0 의 칼날 폴리곤만).
- 신규 `AssassinEnhanceReadyView`(Assassin_Armature `VFX/EnhancedReady` 에 부착, 같은 오브젝트에 MaterialFadeEffect `_ColorFactor` 0 ↔ 1, 0.5초 인 / 0.3초 아웃): `AssassinState.IsEnhancedReady` 로 켜고 끈다. 차지는 `EnhancedReadyChanged(true)` 순간만, 늦은 접속·Armature 재활성은 Update 가 차지 없이 맞춘다. **추가 RPC 없음.**
- 단검 소켓 `Dagger_Weapon_R/L/VFX_EnhanceAura`(날 중간 로컬 Y 0.33, Assassin_Armature 에서 추가) — 오라·차지가 붙는다.
- `Player_Assassin` 의 `enhanceCastPrefab`(임시 Flash_star) 비움 — 차지가 대신한다. `AssassinSkillView.PlayEnhanceCast` 호출은 그대로(빈 필드 = 연출 없음).
- 강화 일격 슬래쉬는 `enhancedSlashPrefab` 교체만(코드 무변경) — 반입 때.

## 3-5. 좌클릭 — 일반 4타 · 적중 성공 · 변신 묶음 (2026-10-19 추가)
- 소켓 = `Player_Assassin/VFX/NormalSlash_1~4 · NormalSlash_Success_1~4 · TransformedSlash`(민경이 넣은 `EffectSocketPlayer`, 각자 Armature `BasicAttack01~04`·`TransformedAttack` 추종). 엔트리 `EffectCatalog.Assassin_LClick_Slash`(일반 4타 공용 원샷)·`_Success`(원샷)·`_Transformed`(루프, outro 0.5초).
- 🔸 **재생 구조(민경 결정)** = `EffectAnimEventRelay` → `EffectAnimEvents`. `Player_Assassin` 루트에 `EffectAnimEvents` 추가(Armature 의 기존 `EffectAnimEventRelay` 가 찾는다). 소켓 id: `Assassin_NormalSlash_1~4`·`Assassin_TransformedSlash`(성공 4개는 id 없음 — 코드 재생).
  클립 이벤트: `Combo_Attack_Wave_01~04` = `PlayEffect("Assassin_NormalSlash_N")`(0.133/0.05/0.117/0.117초, 민경 배치), `Speed_Attack_Loop` = `StartEffect("Assassin_TransformedSlash")` 0.05초 · `StopEffect` 0.65초. 판정 Hit 은 그대로.
- `AssassinSkillView`: `normalSlashPrefabs`·`transformedSlashPrefab`·`normalSlashSockets`·`transformedSlashSocket` **삭제**. 남은 것: `EffectSocketPlayer[] normalSlashSuccess` + 신규 `ServerNormalSlashHit(int)` → `[Rpc(ClientsAndHost, Unreliable)] NormalSlashHitRpc` → `normalSlashSuccess[타].PlayOnce()`(서버 판정이라 애니 이벤트로 못 낸다, 팔라딘 `PlaySlashVfxRpc` 선례), `EffectSocketPlayer transformedSlash` + `EndTransformedSlash()`(안전 정지). `PlayAttackSlash`(Hit) 는 강타 임시 프리팹만.
- `AssassinBasicAttack`: `FireCurrentHit` 가 `coneAttack.Fire()` 반환값(지금까지 버림)을 받아 **일반 모드 + 적중**이면 `view.ServerNormalSlashHit(currentStepIndex)`. 변신 루프 안전 정지 — `PlayAttackClip`(일반·강타 클립 시작)·`ResetRuntime`(끝·취소·시작 실패)에서 `EndTransformedSlash`(피격·대시·변신 해제로 StopEffect 프레임 전에 끊겨도 루프가 남지 않게).
- ⚠️ 묶음을 이어 누르면 다음 묶음의 `StartEffect` 가 루프를 다시 켠다(`EffectSocketPlayer.Play` 는 Stop 후 재생 — 남은 입자는 outro 로 흩어지고 새 루프 시작).
- `AssassinShellAuthoring`(에디터 저작 메뉴): 지운 필드를 채우던 줄·상수·`SetArrayIfEmpty` 삭제. `Speed_Attack_Loop` ReplaceEvents 에 Start/StopEffect 를 같이 찍게(`AnimationEventSpec` 에 string 인자 추가) — 메뉴를 다시 돌려도 남는다. 일반 4타 클립은 이벤트가 있으면 유지.
- 🔸 이펙트 공용: `ShurikenEffectSystem.SetPlayRate` 가 `저작 Simulation Speed × 배율` 로 쓴다(`ShurikenPartCache.baseSpeeds`, 첫 캐시 = 첫 재생 전 프리팹 값). 전에는 배율을 그대로 덮어써 풀 반납(1) 뒤 빠르게 저작한 이펙트가 1배속이 됐다. 저작 속도 1 인 기존 이펙트는 동작 불변.

## 3-6. 우클릭 간파 — 기 모으기 · 튀기기 · 올려치기 · 간파 성공 (2026-10-19 추가)
- 소켓 = `Player_Assassin/VFX/RightClick/WindUp · Pop · Slash · Slash_Success`(민경이 넣은 `EffectSocketPlayer`). 엔트리 `EffectCatalog.Assassin_RClick_Windup`(루프, outro 0 = 끄는 즉시 반납)·`_Pop`(0.4초)·`_Slash`(1.0초)·`_Success`(1.0초).
- 소켓 id: `Assassin_InsightWindup`·`Assassin_InsightPop`·`Assassin_InsightSlash`(성공은 id 없음 — 코드 재생). 클립 `Attack_Up_01` 이벤트(민경 배치): 0초 `StartEffect(Windup)` · 13프레임(0.2167초) `StopEffect(Windup)` + `PlayEffect(Pop)` · 16프레임(0.2667초) `PlayEffect(Slash)`. 판정 Hit(0.6초)·End(1.275초)는 그대로.
- 🔸 **간파 성공 = 올려치기와 같이.** 기준 = 서버 `PlayerInterruptSkillBase.ServerInterruptSucceeded`(**베이스 무변경**). `AssassinSkillView` 가 `OnEnable` 에서 구독 → 시전자 == 자기 `Player` 면 `[Rpc(ClientsAndHost)] InterruptSucceededRpc`(Reliable) → 전 피어 "성공 대기". `EffectAnimEvents` 에 새로 연 `Played(string id)` 이벤트로 올려치기 소켓 id 가 재생되는 순간을 받아, 대기 중이면 `insightSuccess.PlayOnce()`. 알림이 올려치기보다 늦으면(지연, 판정을 뒤로 옮길 때) 받는 즉시 재생 — 순서 무관. 대기 표시는 다음 시전(`BeginInterrupt`)에서 지운다(스킬 종료 0.6초가 올려치기보다 먼저 와도 안 잃게).
- `AssassinSkillView`: `interruptCastPrefab`·`PlayInterruptCast` **삭제**(임시 `FX_Interrupt_Flash` — 기 모으기가 대신). 신규 필드 `insightWindup`·`insightSlash`·`insightSuccess`, `BeginInterrupt()`(대기 초기화)·`EndInterrupt()`(기 모으기 안전 정지).
- `AssassinInterruptSkill`: `OnClientPlay` → `BeginInterrupt`, `OnEnd` 오버라이드 신설 → `EndInterrupt` 후 `base.OnEnd`(시전 중 피격·사망으로 13프레임 전에 끊겨도 기 모으기가 남지 않게).
- `AssassinShellAuthoring`: `interruptCastPrefab` 채우던 줄·상수 삭제, `Attack_Up_01` ReplaceEvents 에 위 효과 이벤트 3종을 같이 찍게(메뉴 3번을 다시 돌려도 남는다).
- `EffectAnimEvents`(VFX 코드): `PlayEffect`·`StartEffect` 가 재생 성공 뒤 `Played(id)` 를 낸다. 구독자 없으면 동작 불변.
- ⚠️ 판정(0.15초 타이머)과 올려치기 프레임(0.2667초)은 여전히 따로다 — 정렬은 은희 결정. 판정을 옮겨도 성공 연출은 위 구조로 따라온다.

## 3-7. 일반 E — 단검 날 발광 · 오라 · 차지 · 강타 슬래쉬 (2026-10-20 추가)
- 🔸 3-4(10-16) 의 단검 발광·소켓·뷰 배선은 커밋되지 않았고 v18 비주얼(`c5c80d5f`) 뒤 단검 = FK 1슬롯이라 **다시 했다**(방식 동일 — 팔라딘 패시브 오버레이).
- `Assassin_Armature`:
  - 양손 단검 렌더러 슬롯 [1] = `50.Art/VFX/_Materials/Dagger_Blade_Glow.mat`(인스턴스 수정 `m_Materials.Array.size 2`). [0] FK 그대로.
  - 소켓 `Dagger_Weapon_R/L`(FBX Null) `/VFX_EnhanceAura`(로컬 Y 0.33 = 칼날 중간) — 자식 `Aura`·`Charge` 에 `EffectSocketPlayer`(엔트리 `Assassin_E_Aura` 루프 · `Assassin_E_Charge` 원샷, Scale 1.25 = 단검 루트 배율, 오라 Safety Timeout 0). ⚠️ 왼손 FBX Null 배율이 (-1,-1,-1)(거울)이라 왼손 소켓은 Z 180° — 회전만 따르는 소켓 플레이어가 칼날 쪽을 보게.
  - `VFX/EnhancedReady` 에 `MaterialFadeEffect`(대상 = 두 단검 렌더러, `_ColorFactor` 인 0.5·아웃 0.3초 — 몸 FK 재질은 속성이 없어 건너뜀) + `AssassinEnhanceReadyView`.
- `AssassinEnhanceReadyView`: 프리팹 Instantiate/Destroy → 소켓 플레이어 `Play`/`Stop`/`PlayOnce` 로 교체(`auras[]`·`charges[]`, `chargePrefab`·`auraPrefab`·`bladeSockets`·`releaseLifetime` 삭제). 켜짐 = `AssassinState.IsEnhancedReady`(차지는 `EnhancedReadyChanged(true)` 순간만). **추가 네트워크 없음.**
- 강타 슬래쉬: `Player_Assassin/Armature/VFX/EnhancedSlash`(민경 배치)에 `EffectSocketPlayer`(id `Assassin_EnhancedSlash`, 엔트리 `Assassin_E_Slash`) · `Combo_Attack_02_01` 0.2167초(= Hit, 민경이 넣어 둔 빈 이벤트) `PlayEffect("Assassin_EnhancedSlash")`.
  - `AssassinSkillView`: `enhancedSlashPrefab`·`enhancedSlashSocket`·`PlayAttackSlash` **삭제**(임시 팔라딘 `FX_DoubleSlash_O`). `AssassinBasicAttack.HandleAnimationEvent` 의 Hit → `PlayAttackSlash` 호출 삭제(판정 경로 무변경). `AssassinShellAuthoring` 해당 상수·줄 삭제. 강타 클립은 `StampEventsIfEmpty` 라 메뉴 재실행에도 이벤트 유지.
- 🔸 테스트 `AssassinVisualPrefabTests.BodyAndDaggerSlotsUseFlatKitBody`: 단검은 [0] = FK, 그 뒤 슬롯은 `Dagger_Blade_Glow` 만 허용(`AssertDaggerSlots`), 기대 슬롯 5 = FK 슬롯만 센다. `FlatKitPlayerVariants` 는 `Assassin_Toon` 이름만 바꾸므로 오버레이는 건드리지 않는다.
- 에셋 `Players/Player3/NormalE/FX_Assassin_E_Aura`(루프, outro 0.7)·`_Charge`(0.5)·`_Slash`(1.0) + `Models/Dagger_Blade.fbx`(오라 Shape 메시).
- 🔸 10-20 2차(민경): 오라·차지 소켓 플레이어를 `Player_Assassin/VFX/NormalE/Aura_R·Aura_L/{Aura,Charge}`(Socket = 단검 `VFX_EnhanceAura`), 강타 플레이어를 `VFX/NormalE/EnhancedSlash`(Socket = `Armature/VFX/EnhancedSlash`)로 옮김 → `MaterialFadeEffect`(대상 = Armature 안 양손 단검 렌더러)·`AssassinEnhanceReadyView` 도 **`VFX/NormalE` 로 이동**. `Assassin_Armature` 의 `EnhancedReady` 컴포넌트 2·원래 `Aura`/`Charge` 4 삭제(소켓 `VFX_EnhanceAura` 는 유지), `Player_Assassin` 의 낡은 오버라이드·삭제 기록 정리. 손으로 만든 stripped fileID 4개를 Unity 규칙(인스턴스 ^ 원본)으로 바로잡음.
- 시전 임시 연출 삭제: `Player_Assassin` 의 `enhanceCastPrefab`(공용 `Burst/Flash_star`)가 아직 채워져 E 시전마다 나오고 있었다 → `AssassinSkillView.enhanceCastPrefab`·`PlayEnhanceCast`·`PlayAttached`(호출자 없어짐), `AssassinEnhanceSkill.OnClientPlay` 의 호출(상체 레이어 처리는 그대로), `AssassinShellAuthoring` 의 `EnhanceCastVfxPath`·`SetIfEmpty` 삭제(메뉴 재실행으로 되살아나지 않게).

## 3-8. 대쉬 잔상 (2026-10-20 추가)
- `Player_Assassin` 루트(Player.prefab 인스턴스 루트에 추가 컴포넌트)에 **`PlayerSkillVfx`**(최후의 심판 칸은 전부 비움 → 대쉬 잔상 RPC 경로만 동작, 거너와 같은 구성) + `DashAfterimage` + `DashSpeedLines`. **코드 무변경.**
  - `PlayerSkillVfx` 는 NetworkBehaviour 라 어쌔신 NetworkObject 의 behaviour 목록이 하나 늘어난다(모든 피어가 같은 프리팹이라 문제없음 — 팔라딘·거너 선례).
  - `DashAfterimage`: modelRoot = `Armature`(몸·머리카락·양손 단검, VFX 제외), 재질 `50.Art/VFX/Common/Players/Dash/FX_AssassinDashAfterimage.mat`, spawnInterval 0 · 수명 0.2 · 최대 80 · safetyTimeout 2(팔라딘·거너 값).
  - `DashSpeedLines`: searchRoot = `Armature`, 이름 힌트 head·hand·foot, 재질 `FX_AssassinDashSpeedLine.mat`.
- 대상은 스페이스바 공용 대쉬(Dash 상태)뿐 — Q 관통 돌진은 Skill 상태라 잔상이 안 난다.

## 4. 확인 부탁
- Play: R → 0.5초에 소용돌이가 차고 따라다님 / 만료·수동 해제·쓰러짐 → 0.5초에 흩어짐.
- MPPM: 다른 피어·변신 중 늦게 들어온 클라에서도 보이는지.
- 변신 E: 원 표시 중심에서 루프가 나오고, 5타 끝(또는 중단) 뒤 1초쯤에 걸쳐 흩어지는지.
- 변신 Q: 돌진 경로에 자국이 남고, 출발 순간 엉뚱한 곳에서 한 줄로 쏟아지지 않는지(풀 인스턴스 위치 이동).
- 일반 Q: 돌진이 끝나고 한 박자 뒤 경로 위로 일섬 + 끝점 버스트, 벽에 막히면 일섬이 그만큼 짧은지. 다른 피어에서도 경로와 맞는지.
- 일반 E: 누르면 양손 단검 날만 붉게 + 검붉은 오라, 강화 일격·변신·쓰러짐에 꺼짐. 다른 피어·늦은 접속에서도 같은지. 다른 캐릭터(팔라딘·거너·어쌔신 몸)가 그대로인지.
- 좌클릭: 일반 4타마다 소켓 방향대로 베기, 맞았을 때만 성공 연출이 겹쳐 나옴(빗나가면 베기만). 변신 묶음 동안 루프가 돌고 묶음 끝·피격 취소·변신 해제에 꺼지는지. 다른 피어에서도 같은지.
- 우클릭: 기 모으기 → 13프레임에 튀기며 사라지고 16프레임 올려치기. 보스·중간보스 간파 **성공 때만** 성공 연출이 올려치기와 함께(빗나감·일반 적중은 올려치기만). 시전 중 피격되면 기 모으기가 남지 않는지. 다른 피어에서도 같은지.
- 일반 E(10-20): E → 양손 단검 날만 붉게 + 차지 + 오라(무기를 따라감, 왼손도 칼날 쪽), 강타 시작에 꺼지고 0.2167초에 강타 슬래쉬, 변신·쓰러짐에 꺼짐. 다른 피어·늦은 접속 동일. EditMode `AssassinVisualPrefabTests` 통과.
- 대쉬 잔상(10-20): 스페이스바 대쉬마다 검붉은 잔상 + 붉은 속도선, 호스트·클라 누가 대쉬해도 양쪽에 보이는지, Q 돌진엔 안 나오는지.
