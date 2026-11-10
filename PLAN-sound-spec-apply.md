# 계획 = **Sound_2 사운드 적용 명세 v0.1 반영** (2026-11-10 · 경석(Claude) · ✅ grill Q1~Q11 · ✅ Codex 대조·계획 검수 반영 · ✅ 경석 승인 11-10(플레이어 코드 목록 검토 · 거너 Q 안 A))

> 브랜치 `feature/SettingsDdol`. 기준 = 기획 명세 `GA7thFinal_VeyTrace/2_Documents/20_Design/ReC/사운드_적용명세.md`(2026-11-09, 이하 **명세**).
> 대상 = `Assets/51.Audio/Sound_2` **만**. `AI_Sound`·`Sound_1`·`SFX/`·`BGM/Legacy` 등은 전부 레거시 — 손대지 않는다(경석 11-10).
> 바탕 = [PLAN-sound-mix.md](PLAN-sound-mix.md)(11-09 구현). **명세와 다르면 명세가 이긴다**(예: 그 계획의 간파 시작음 = 플레이어 시전 · Start_01/02 랜덤 → 이번에 뒤집힘).
> 조사 = 서브에이전트 4회(플레이어·보스 각 2회, 결론 일치) + Codex 대조표 + Codex 계획 검수(세션 scratchpad `codex_sound_out.txt`·`codex_plan_review_out.txt`) + 경석(Claude) 직접 재확인.

## 사실 (11-10 코드·에셋 확인)
- 명세 "사용" 80개 파일은 모두 엔티티에 등록돼 있다. **문제는 연결 사건·재생 방식이다.**
- 클립 구성·`playMode`·`loop` 의 원본 = xlsx `Sound` 시트(`SoundSyncPlanner` 가 `clipPaths`·`playMode`·`loop`(0/1/2)를 엔티티에 쓴다). 엔티티를 직접 고치면 다음 동기화가 되돌린다. 카탈로그는 **키가 아니라 인덱스**로 갱신되고, 시트에서 키를 지워도 **엔티티 파일은 남는다**. BGM `TransitionTime` 은 시트 칸이 없어 엔티티 직접.
- 재생 경로 2개: 코드 `AudioManager.Play(key, pos, owner)` / VFX 엔트리 `parts[].sound`(`EffectManager.Fire`, `stopOnRelease` = 루프 해제 때 정지, `outroParts` = 해제 때 1회). 엔트리 소리는 저작 메뉴 `Sound1VfxLink`(표 `Links`)가 붙였다 — **다시 돌리면 재부착된다.**
- 🔴 **보스 BGM 이 지금 안 난다.** `BossEncounterDirector.HandlePhaseBgm` 이 `SceneBgmSwitcher.Instance` 를 쓰는데 그 컴포넌트가 어느 씬·프리팹에도 없다(참조 0).
- 🔴 **몬스터 피격음이 안 난다.** `S1_Monster_Hit` 는 `FX_Hit_dark_magic_Entry` 에 붙어 있으나 공용 피격 VFX(`EffectCatalog.HitEffect`)는 무음 `FX_Hit_electric_Entry`.
- 소리 없는 VFX: 중간보스 사망 `FX_Death_Explosion_02`(3종), 어쌔신 변신 평타 `FX_Assassin_LClick_Transformed`, 강화 좌클릭 `FX_Assassin_E_Slash`.
- 🔴 **BroAudio `Shuffle` 은 직전 제외를 보장하지 않는다**(`ShuffleClipStrategy` — 반환 경로에서 `_lastUsed` 미갱신, `Use` 가 직전을 만나면 `_lastUsed = null`). 전략 인스턴스는 엔티티마다 하나.
- 전역 억제: `GlobalPlaybackGroup.asset` 이 같은 ID 0.05초 간격 제한(같은 프레임 예외 꺼짐) — "호출했다 ≠ 들린다".
- 피해 RPC `Unit.ClientDamageDealtClientRpc`(`Unit.cs:151`→`:821`, 전 피어): HP·보호막이 깎였을 때만, 공격자 clientId·채널·`AttackType`(Q = `SkillQ`, 세 캐릭터 공통)을 싣는다. `hpDamage` 는 체력 하한 클램프 전 값. 보호막+HP 동시 타격 = `ClientDamagedAttributed` 2번 · `ClientHitAttributed` 1번. **23호 자폭 실드 초과 타격은 HP RPC 와 실드 RPC 가 따로 와서 `ClientHitAttributed` 도 2번**(`TwentyThreeBoss.cs:6132`·`:6146`).
- 간파 창: 23호 `OnCounterWindowChanged`(`:7295`) · 중간보스 `MonsterBase.OnCounterVisualChanged`(`:2623`) — 복제 bool 의 전 피어 콜백. 일반 몬스터는 창 없음.
- 드론: 폭발 시각이 미리 정해짐(`lockTime` 1초를 `DroneLockClientRpc` 로 받음). **정상 폭발도 `ClientImpact → ClientCancel` 을 지난다.** `_vStart` = 각 피어가 Lock 을 받은 시각.
- 주먹 스파크는 **적중 때만**. 휘두름 사건 = `EffectAnimEvents.Played` 의 `Wind_L/R`(23호 FBX `.meta` 130·187·244행, 건틀릿 Punch01~03 L/R) — 전 피어 로컬, 적중 무관.
- `PlayDashHitClientRpc` 는 일반 돌진과 **레이지 돌진 벽 충돌 둘 다** 부른다(`:5112`·`:5274`).
- 거너 Q: 충전 중 대시 = 실제 취소 경로. 원격 오너가 로컬 `OnEnd` 를 먼저 탈 수 있어 클라 단독 판정은 발사음·취소음이 둘 다 날 수 있다.
- 23호 `OnMonsterStateChanged` 는 7030행 이후 서버 전용. `HandleBossDefeated` 도 서버 전용. 보스 사망 후에도 `phase` 는 `Combat` 일 수 있다.
- 저체력 HUD 이미지(`HealthVignetteHUD`)는 저체력 빨강·피격 플래시·사망 암전을 합성한다 — `image.enabled` 는 경고 상태가 아니다.
- 어쌔신 R 시작음은 변신 루프 엔트리에 있고, 늦은 합류 `SyncLoops()` 도 그 엔트리를 재생한다 → 변신 시작이 아닌데 시작음이 난다.

## 결정 (grill 11-10, 경석)
| # | 항목 | 결정 |
|---|---|---|
| Q1 | 직전 제외 기록 단위 | **키별 하나**(23호·건틀릿이 펀치 기록 공유). 각 PC 가 따로 고른다 |
| Q2 | Hit_Monster 다중 대상·다단히트 | **대상·타격마다 + 시트 상한**(동시 2 · 간격 0.06초 — 기존 값) |
| Q3 | 스테이지 BGM 종료 | **보스 연출 시작(`Preparing` 진입) 때 1초 아웃** → 연출 중 BGM 없음 → `Combat` 에 보스 곡 2초 인 |
| Q4 | 강제 중단 종료음(어쌔신 R 해제·팔라딘 E Fail) | **사망만 무음, 나머지(직접·자동·피격·쓰러짐 등)는 항상 낸다** |
| Q5 | 드론 미리 시작 후 실제 취소 | **0.1초 페이드로 끈다**(정상 폭발은 끝까지) |
| Q6 | 거너 R | **낙뢰가 보이는 틱당 1번**(대상 수 무관, 빈 틱 무음) |
| Q7 | 벤트 발소리 | **이번 범위에서 뺀다**(콘크리트만 직전 제외로) |
| Q8 | 몬스터 보호막만 깎인 타격 | **Hit_Monster 낸다** |
| Q9 | Player_Hit "HP 피해" | **HP 채널로 피해가 들어가면**(클램프 전, 피해 RPC 변경 없음) |
| Q10 | 팔라딘 Q 피격음 범위 | **팔라딘의 `SkillQ` 피해 전부**(돌파의 방패·진격의 충격 등 파생 포함) |
| Q11 | 완성음 정렬이 사건보다 일러야 할 때 | **파일 앞부분을 건너뛰고 재생**(충돌음을 실제 충돌에 맞춤) |
| — | 송전탑 기준점(명세 미확정) | 지금 그대로 = 상승 시작·파괴 하강 시작. 시간 초과 하강은 무음 유지 |
| — | 3D·감쇠·볼륨·동시 수 | **안 건드린다**(명세 원칙 8·9, 11-09 팀 합의 유지) |

## 공통 규칙
- **사건을 옮길 때 옛 연결 제거와 새 연결을 한 커밋에.** 엔트리는 **소리 파트만** 지운다(엔트리·outro 전체 삭제 금지). 같은 매핑을 `Sound1VfxLink.Links` 에서도 지운다.
- **소리는 VFX 배선과 독립** — VFX null 조기 반환 **앞**에서 재생/정지.
- **복제값 콜백은 `!previous && next` 전이에서만** 재생. 스폰·늦은 합류의 현재값 적용에서는 재생 안 함.
- **서버 가드 앞**(전 피어가 지나는 곳)에 둔다. 호스트는 서버+클라를 겸하므로 한 사건에 한 경로만.
- 소리만 바꾸고 게임 판정·애니 시각은 바꾸지 않는다(명세 원칙 6).
- 완성음 오프셋 = `확정 사건에서 실제 충돌까지 시간 − 파일 안 충돌음 위치`. 양수 = 그만큼 늦게 예약(취소 시 예약도 취소), 음수 = 파일 앞부분을 그만큼 건너뛰고 즉시 재생(Q11). 슬로우·애니 배속은 실제 시각 기준으로 확인.

## 단계

### 1. 데이터 — xlsx `Sound` 시트 → `Tools/Data/사운드 동기화` (**새 키 추가만, 옛 키는 유지**)
| 키 | 변경 |
|---|---|
| `S1_Interrupt_Start` | 클립 = `Interrupt_Start_01` 만 |
| `S1_Interrupt_Success` | 클립 = `Heavy_interrupt_hit_2` 만 |
| `S1_Monster_Hit` | 클립 = `Hit_Monster` 만(`floraphonic` 제거) |
| `S1_Gunner_Q_Fire` | 클립 = `End01` 만 · **새 키 `S1_Gunner_Q_Cancel`** = `End02` |
| 새 `S1_Paladin_Attack_1~4` | Single, 각 1클립 |
| 새 `S1_Assassin_Attack_1~4` | 일반 상태, Single |
| 새 `S1_Assassin_Attack_Tf` | 변신 상태, 4클립 `Shuffle` |
| `S1_Punch_Hit`·`S1_Spinner_Attack`·`S1_Footstep` | `playMode` = `Shuffle` |
| `S1_23_Rush`·`S1_Spinner_Spin` | `loop` = **1** |
- 새 키는 `key/library/entity/audioType/clipPaths/playMode/loop` 전 칸 + `SoundEntry` 수치(볼륨·상한·3D 등)를 **옛 키 값 그대로 복사**. 동기화 후 카탈로그 수치까지 대조.
- BGM 4개 엔티티: `SeamlessLoop` 1 유지 · `TransitionTime` **2**(엔티티 직접, SVN).
- **옛 키(`S1_Paladin_Attack`·`S1_Assassin_Attack`) 삭제는 3단계 연결 이전이 끝난 뒤** — 시트의 그 키 모든 필드 행을 빈틈없이 재인덱싱, 엔티티 파일은 Legacy 로 옮기거나 삭제, 문자열 + GUID 참조 0 확인.

### 2. 직전 제외 보장 — `ShuffleClipStrategy` 만 수정(BroAudio 패키지, git)
- 유효 클립 중 직전을 뺀 후보에서 고르고 **반환 클립·`out index`·`_lastUsed` 를 함께 확정**. 유효 후보 1개면 반복 허용. Random(가중치)·Chained·공통 `PickNewClip` 은 건드리지 않는다. 수정 지점에 `// MainProject:` 주석.
- EditMode 테스트: 빈 배열·1개·미설정 클립 혼합·2/3/12개 1000회 연속 중 같은 클립 연속 0·반환 인덱스 일치.

### 3. 코드 — 사건 이동·추가 (새 RPC = 3.15 거너 전용 1개 · 기존 RPC 인자 추가 = 3.21 23호 1곳 · 그 외 네트워크 변경 없음 — 경석 11-10)
| # | 소리 | 새 지점 | 옛 연결 제거 |
|---|---|---|---|
| 3.1 | `Interrupt_Start` | 23호 `OnCounterWindowChanged` · `MonsterBase.OnCounterVisualChanged` 본문에서 `!previous && next` 1회(텔레그래프 반복문 밖) | `PlayerSkillController.cs:1823` |
| 3.2 | `Hit_Monster` | 몬스터 피해 RPC 수신 본문에서 **원래 타격 1건당 1번**(보호막+HP 합침 · 23호 자폭 실드 분리 보고는 두 번째 보고에서 소리 제외), 공격자 = 플레이어 귀속. 시간 기반 중복 제거 금지 | `FX_Hit_dark_magic_Entry` 소리 |
| 3.3 | `Player_Hit` | `ClientDamagedAttributed` 의 **HP 채널만**(Q9) — 보호막만 깎이면 무음, 보호막+HP 는 1번 | `ClientHitAttributed` 구독 교체 |
| 3.4 | `Paladin_Q` | 피해 RPC 중 **공격자 캐릭터 = 팔라딘 AND `SkillQ`**(Q10, 파생 포함). "Q 사용 중" 검사로 대체 금지 | Smash 엔트리의 Q 소리 |
| 3.5 | `Paladin_Q_Loop` | 일반 Q 만 — `BeginSmashHold`/`EndSmashHold` 안에서 **소리만** 돌파의 방패 분기 제외(VFX 는 그대로) | Smash 엔트리의 Loop 파트 |
| 3.6 | `Character_Dash` | `TraitFxEvent(PaladinShieldDash, Activated)` 1회(시전자 필터) | — |
| 3.7 | `Paladin_Attack_1~4` | `PlaySlashVfx(attackIndex, hit)` — 0기반 → 1~4 키, VFX null 반환 앞 | Single/DoubleSlash O/X 4엔트리 소리 |
| 3.8 | `Paladin_R` | **승인된 시전 시작** 기준(조준 시작 아님) 오프셋 재생, 취소 시 예약 취소. 선딜 배율·애니 속도 반영 | `FX_Lightening_Strike_Entry` 소리 |
| 3.9 | `Paladin_E_Fail` | 시전별 **단일 재생 함수**(성공 여부·이미 재생 여부·사망 여부) — 해제 애니 `GuardianRelease` 와 강제 종료(피격·쓰러짐 등) 둘 다 이 함수로. 클라에서 서버 전용 `model.Blocked` 사용 금지 | 애니 경로를 함수 호출로 교체 |
| 3.10 | `Assassin_Attack_1~4` / `_Tf` | 일반 = 애니 이벤트 `Assassin_NormalSlash_n` / 변신 = `AssassinBasicAttack.HandleAnimationEvent` **서버 가드 앞** Hit 4회, 현재 모드 확인(강화·일반엔 변신음 없음) | `FX_Assassin_LClick_Slash_Entry` 소리 |
| 3.11 | `Assassin_Q` | `OnLocalAnimationEvent(Custom0)` 의 **이동 권한 조건 밖**에서 시전별 1회 — 일반·변신·특성 복귀 공통 | `FX_Assassin_PierceLine_Entry` 소리 |
| 3.12 | `Assassin_E_Start` | 애니 이벤트 `Assassin_EnhancedSlash` | `AssassinEnhanceReadyView` 준비 때 재생 |
| 3.13 | `Assassin_E_Buff` | 유지, `Apply(on, false)` 상태 복원 때 무음 | — |
| 3.14 | `Assassin_R_Start`/`_End` | 시작음 = 실제 변신 시작 사건 1회(루프 복원 `SyncLoops` 와 분리). 종료음 = outro 유지, **사망으로 풀릴 때는 해제음 없음**(경석 11-10 — 죽으면 사망 쪽 소리만). 네트워크 계약은 안 바꾸고, 해제 순간 그 플레이어가 죽었는지 각 피어가 조회해 outro 의 **소리 파트만** 생략(VFX 유지). 복제 순서로 드물게 어긋나도 허용 | BladeVortex 엔트리 parts 의 시작음 |
| 3.15 | `Gunner_Q_Cancel` | **서버가 확정한 "발사 전 취소"를 기존 종료 RPC 인자로** 전 피어에 — 사망 제외. 로컬 조기 `OnEnd` 는 루프 정리만, 시전별 중복 방지 | — |
| 3.16 | `Gunner_R` | `GunnerTrackingLaserView.StrikeRpc` 위치 반복문 밖 1번 | `FX_GunnerUltCast_Entry` 소리 |
| 3.17 | `Punch` | 23호·건틀릿 `EffectAnimEvents.Played` 의 `Wind_L/R` **한 경로만** | `FX_Punch_Hit_Spark_Entry` 소리 |
| 3.18 | `Spinner_Spin` | `PlaySpinLoopClientRpc` 시작 ~ 멱등 정리 함수(정상·그로기·사망·디스폰·비활성화). 정지는 `_effects` null 반환 **앞** | `FX_Mob_Spin_Entry` 소리 |
| 3.19 | `23_Grab_Start` | `StartGrabPulseClientRpc` 1회, `grabPulse == null` 반환 앞 | — |
| 3.20 | `23_Grab_Floor` | 1타 `CrossFadeGrabCycleStateClientRpc(Slam)` · 2·3타 `ReplayGrabSlamClientRpc` | `FX_Throw_Mini_Entry` 소리 |
| 3.21 | `Interrupt_Wall` | `PlayDashHitClientRpc` 에 **충돌 종류 인자** 추가 → 일반 돌진 벽 충돌만 | — |
| 3.22 | `23_Drone` | `ClientTick` Lock 경과 ≥ `lockTime − 파일 내부 폭발 위치` 첫 프레임 1회(음수면 앞부분 생략). Impact 가 먼저 오면 정렬 위치부터 재생. **정상 Impact 는 소리 유지, 실제 취소(`DroneCancelClientRpc`)만 0.1초 정지** — 드론 세대별 핸들 | `FX_Drone_Explosion_Entry` 소리 |
| 3.23 | `Dead_Monster` | `MonsterBase.OnStateChanged` Dead 전이 1회(23호 제외) | `FX_Death_Explosion_01_Entry` 소리 |
| 3.24 | `Vignetting` | HUD 합성 **전의 저체력 경고 상태**를 HUD·사운드가 공유(피격 플래시·사망 암전 제외) | `PlayerLowHealthAudio` 30% 판정 교체 |
- **완성음 정렬 작업**(측정 → 현재 연출과 대조 → 시작점 조정 → 취소·배속 검증): `23_Drone`(3.22) · `Paladin_R`(3.8) · `Paladin_E_Success`(반격 0.433초) · `Gunner_E`. 측정값·합격 기준은 핸드오프에.

### 3-부록. 플레이어 코드(은희 영역) 수정 목록 — 경석 검토용 (11-10)
> `Unit.cs`·`Player.cs`·`PlayerDashVfx.cs`·`PlayerFootstepSound.cs` 는 **안 고친다**(피해 RPC 는 구독만).
| 등급 | 파일 | 위치 | 바꾸는 것 |
|---|---|---|---|
| 🟢 경석이 만든 사운드 파일 | `Player/PlayerHitSound.cs` | `:18` 구독 | `ClientHitAttributed` → `ClientDamagedAttributed` HP 채널 |
| 🟢 | `Player/PlayerLowHealthAudio.cs` | `:19`·`:58` | 30% 자체 판정 → HUD 저체력 경고 상태 구독 |
| 🟢 | `Player/Assassin/AssassinEnhanceReadyView.cs` | `:74` | 준비 때 `E_Start` 재생 삭제(Buff 는 유지 + 복원 가드) |
| 🟡 한 줄 훅(로직 불변) | `Player/Skill/PlayerSkillController.cs` | `:1821–1823` | 간파 시작음 2줄 **삭제만** |
| 🟡 | `Player/DefaultAttackController.cs` | `:509 PlaySlashVfx` 맨 앞 | 타수별 `Paladin_Attack_n` 재생 |
| 🟡 | `Player/Skill/FirstMeleeMainSkill.cs` | `:165 OnClientPlay`·`:293 OnEnd` | 일반 Q 일 때만 Q 루프 소리 시작/정지(VFX 호출은 그대로) |
| 🟡 | `Player/Skill/FirstMeleeUltimateSkill.cs` | `OnClientPlay`(전 피어 시전 시작) | R 완성음 오프셋 재생, 취소 때 정지 |
| 🟡 | `Player/Assassin/AssassinBasicAttack.cs` | `:301 HandleAnimationEvent`, `:320` 서버 가드 **앞** | 변신 상태 Hit 이면 `Assassin_Attack_Tf` |
| 🟡 | `Player/Assassin/AssassinDashStrikeSkill.cs` | `:250 OnLocalAnimationEvent` Custom0, `isLocallyDashing` 조건 **밖** | Q 출발음 시전당 1회 |
| 🟡 | `Player/Gunner/GunnerTrackingLaserView.cs` | `:209 PlayStrikes` 반복문 밖 | 거너 R 틱당 1회 |
| 🟠 상태 추가(네트워크 불변) | `Player/Skill/FirstMeleeSubSkill.cs` + `PaladinSkillView.cs` | 반격 자세 RPC 수신·해제 애니·`OnEnd` | 시전별 "성공함 / Fail 냄" 기억 → Fail 은 한 함수에서 1번(사망 제외) |
| 🟠 | `Player/Assassin/AssassinSkillView.cs` | `:115·:121 SyncLoops`, 변신 시작 | 시작음을 루프 엔트리에서 떼어 실제 변신 시작에만(늦은 합류 복원 무음) |
| 🟠 | `UI/Combat/HealthVignetteHUD.cs` | 저체력 판정부 | 합성 전 "저체력 경고 켜짐" 상태·이벤트 공개(표시 동작 불변) |
| 🔴 네트워크 계약 | 거너 Q 취소음 | `GunnerChargeLaserSkill.cs:456 OnEnd` 서버 `!fired` 분기(사망 제외) | ✅ **안 A 확정(경석 11-10)**: `GunnerBeamView` 에 거너 전용 RPC 1개(서버 확정 → 전 피어 1회). 공용 `EndSkillClientRpc` 는 안 건드림 |
| 🟠 | 어쌔신 R 사망 해제 무음 | `AssassinSkillView` 해제 처리 | ✅ 스냅샷 안 바꿈(경석 11-10) — 해제 순간 사망 여부 로컬 조회로 소리 파트만 생략 |
- 새 파일(경석): 돌파의 방패 출발 `Character_Dash`(`TraitSkillFx.Raised` 구독)·팔라딘 Q 피격음(피해 RPC 구독, 공격자 팔라딘 AND `SkillQ`)은 사운드 전용 컴포넌트에서 구독 — 플레이어 스킬 코드는 안 건드린다.

### 4. BGM — `AudioManager` 한 곳에서 판단 (`SceneBgmSwitcher` 는 Legacy)
- 상태: 현재 곡 핸들·요청 세대·종료 사유·대기 곡. 씬 준비·보스 단계·보스 사망을 함께 평가, 같은 키 재요청은 멱등.
- 같은 곡 반복 = 엔티티 Seamless 2초(1단계).
- 씬 전환 시작 → 현재 곡 `Stop(1초, onFinished)`: 타이틀 = 로컬 `StartGame` / 로비→맵 = 서버 시작 경로 + 클라 상태 메시지 수신(flowId 별 1회) / 맵→결과 = 공통 `PerformGoToResult`.
- 새 곡 = 씬 재생 조건(Title·Lobby `Start`, Map `MainGameReady`) **그리고** 이전 곡 종료 콜백 후 `fadeIn 2초`(`Transition.OnlyFadeIn`). 로딩은 기다리지 않음.
- 보스: `Preparing` 진입 → 스테이지 곡 1초 아웃 / `Combat` → 보스 곡 2초 인 / **23호 Dead 전이(서버 가드 앞, `previous != Dead && next == Dead`) → 2초 아웃이 이긴다**(사유를 먼저 기록해 이후 일반 Stop 차단, 예약 반복 취소) / `Idle` 복귀(전멸) → 스테이지 곡 2초 인 / `FailedSafe` → 스테이지 곡 복귀.
- 늦은 합류·초기 동기화: 이미 Dead 면 보스 곡을 새로 시작하지 않음. `MainGameReady` 가 보스 진행 중 상태를 덮지 않음.

### 5. 마무리
- 옛 키 삭제(1단계 말미 조건) · `Sound1VfxLink.Links` 정리 · 키 문자열 + GUID 참조 전수 검색.
- 핸드오프 `Docs/tech/handoff-sound-spec-apply.md`(은희·민경 영역 변경, 패키지 수정 재적용, 측정값) · `CONTEXT.md` 갱신.

### 6. 검증
- 컴파일 0 · EditMode(2단계 테스트 + 기존) · 사운드 동기화 오류 0 · 카탈로그 수치 대조.
- 솔로 Play: 캐릭터 3종 평타 타수·빗맞힘·전 스킬, 몬스터 피격·사망, 스피너·건틀릿 펀치 빗맞힘, 23호 전 패턴(돌진 벽·레이지·잡기 3타·드론 정상/취소·송전탑·취약·벽), 저체력 진입·회복, BGM 4곡 전환·반복 이음매·보스 사망 아웃.
- MPPM 2~3인: 호스트·클라 각각 **재생 1회**(중복 0) · 원격 Q 발사 직전 대시 · 동시 간파 · 변신 중 늦은 합류 · 해제음 뒤 강제 중단 · 보스 BGM.
- 추가 경계: 23호 자폭 실드 초과 타격 · 로비 체력 하한 · BGM 반복 경계 중 전환·사망.
- 명세 개발 검수 체크리스트 7개. 전역 0.05초 억제가 누락을 가리지 않는지 — 개발 중 임시 호출 로그와 실제 출력을 함께 본다.

## 범위 밖
- 벤트 발소리(Q7) · 3D/감쇠/볼륨/동시 수 · Sound_2 밖 모든 오디오 · UI 클릭(명세 보류) · 송전탑 시간 초과 하강 · `23_Jump_End`(애니 이벤트 0.042초 vs 피해 0.206초 — Play 에서 착지와 어긋나면 별건) · 간파 창이 복제 전에 열렸다 닫히는 순간 창(bool 복제 한계).

## 리스크
- **영역**: 플레이어 코드(은희)·VFX 엔트리(민경) 다수. 기존 RPC 인자 추가 3곳(거너 Q 종료·23호 돌진 충돌·어쌔신 변신 종료 사유)은 코어 인터페이스 변경 → 핸드오프 필수.
- **SVN**: 엔트리·엔티티·`SoundCatalog`·`GameData.xlsx`. 여러 에셋을 한 번에 바꾸므로 **Unity 끄고** 작업, 커밋 전 `svn update`(충돌 0) → 커밋 → 핀 갱신.
- 패키지 수정(2단계)은 BroAudio 갱신 때 사라진다.
- Unreliable RPC(잡기 지지기·팔라딘 평타 VFX)는 유실 때 소리도 빠진다 — 경로 유지.

## 커밋 나누기
1(데이터 — 새 키·클립 정리·모드·루프, SVN) → 2(Shuffle + 테스트) → 3 묶음별(공용 피격·간파 / 팔라딘 / 어쌔신 / 거너 / 몬스터·중간보스 / 23호) → 4(BGM) → 5(옛 키 삭제·저작 메뉴·문서).
