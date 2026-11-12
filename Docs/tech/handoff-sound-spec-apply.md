# 핸드오프 — Sound_2 사운드 적용 명세 v0.1 반영 (2026-11-10 · 경석(Claude))

> 계획 [PLAN-sound-spec-apply.md](../../PLAN-sound-spec-apply.md) · 기준 = 기획 명세 `2_Documents/20_Design/ReC/사운드_적용명세.md`(2026-11-09).
> 앞 판 [PLAN-sound-mix.md](../../PLAN-sound-mix.md)(11-09) 위에 얹었다. **명세와 다르면 명세가 이긴다.**
> 대상은 `Assets/51.Audio/Sound_2` 만. `AI_Sound`·`Sound_1`·옛 `SFX/`·`BGM/Legacy` 는 레거시(손대지 않음).

## 한 줄
소리를 "VFX 엔트리에 붙여 두면 VFX 와 같이 난다"에서 **명세의 사건 지점에서 코드로 낸다**로 옮긴 것이 많다.
키 이름과 "언제·몇 번" 규칙은 [`Sound/GameSoundHooks.cs`](../../Assets/1.Scripts/Sound/GameSoundHooks.cs) 한 곳, 호출부는 한 줄.

## 영역별 바뀐 곳
### 은희 영역(플레이어 코드) — 경석 검토 11-10
| 파일 | 바뀐 것 |
|---|---|
| `Player/Skill/PlayerSkillController.cs` | 간파 시작음 2줄 **삭제만**(플레이어 시전 → 보스 간파 창 열림으로 이동) |
| `Player/DefaultAttackController.cs` `PlaySlashVfx` | 팔라딘 평타 타수별 `S1_Paladin_Attack_1~4`(빗맞힘 포함) |
| `Player/Skill/FirstMeleeMainSkill.cs` | 일반 Q 달리기 루프(`OnClientPlay`~`OnEnd`), 돌파의 방패는 루프 대신 `S1_Dash` 1회 |
| `Player/Skill/FirstMeleeUltimateSkill.cs` | R 소리 = 시전 시작에서 착탄 정렬 재생(아래 측정값), 완주 못 하면 정지 |
| `Player/Skill/FirstMeleeSubSkill.cs` · `PaladinSkillView.cs` | E 실패음 = 해제 애니 이벤트 `GuardianRelease` + 그 전 강제 종료(사망 제외) 를 한 함수에서 시전당 1번. 성공은 반격 자세 전환으로 각 피어가 안다 |
| `Player/Assassin/AssassinBasicAttack.cs` | 휘두름 소리(Hit 이벤트, **서버 가드 앞**): 일반 = 타수별 `_1~4`, 변신 = Hit 마다 `_Tf`(직전 제외), 강타 = `S1_Assassin_E_Start` 만 |
| `Player/Assassin/AssassinDashStrikeSkill.cs` | Q 출발음 = Custom0, 이동 권한 조건 **밖**, 시전당 1회(그림자 회귀 포함) |
| `Player/Assassin/AssassinEnhanceReadyView.cs` | E 준비 때 `E_Start` 삭제 · `E_Buff` 는 실제 전이 때만(복원 무음) |
| `Player/Assassin/AssassinSkillView.cs` | 변신 시작/해제음 = `TransformChanged` 이벤트에서만(늦은 접속 루프 복원 무음), **사망 해제는 무음**(그 피어가 본 생사 — 스냅샷 안 바꿈, 경석 11-10) |
| `Player/Gunner/GunnerBeamView.cs` | 🔴 **새 RPC 1개** `ChargeCancelledRpc`(ClientsAndHost, Unreliable) — 서버가 "발사 전 취소"를 확정해 전 피어 1번 |
| `Player/Gunner/GunnerChargeLaserSkill.cs` `OnEnd` 서버 분기 | `!fired && reason != CasterDied` 면 위 RPC |
| `Player/Gunner/GunnerTrackingLaserView.cs` `PlayStrikes` | R 광선 틱당 1번(대상 수 무관, 빈 틱 무음) |
| `Player/PlayerHitSound.cs`(경석) | `ClientDamagedAttributed` **HP 채널만** — 보호막만 깎이면 무음 |
| `Player/PlayerLowHealthAudio.cs`(경석) | **변경 없음(30% 유지)** — 11-10 에 HUD 빨간 경고 비율(0.8)을 따르게 바꿨다가 80% 에서 켜져 경석 지시로 되돌림. `HealthVignetteHUD.cs` 도 원본 그대로 |
| `Unit.cs`·`Player.cs`·`PlayerDashVfx.cs`·`PlayerFootstepSound.cs` | **안 바꿈**(피해 RPC 는 구독만) |

### 민경 영역(VFX 엔트리, SVN) — 소리 파트만 뗌(프리팹·outro 연출 그대로)
`FX_Hit_dark_magic` · `FX_ShieldDash_Smash`(Q·Q루프) · `FX_Single/DoubleSlash_O/X` · `FX_Lightening_Strike` · `FX_GuardianWill_BlockFail` ·
`FX_Assassin_LClick_Slash` · `FX_Assassin_PierceLine` · `FX_Assassin_BladeVortex`(시작 parts·해제 outro) · `FX_GunnerUltCast` ·
`FX_Punch_Hit_Spark` · `FX_Mob_Spin` · `FX_Throw_Mini` · `FX_Drone_Explosion` · `FX_Death_Explosion_01` — 19파트.
저작 메뉴 `Sound1VfxLink.Links` 에서도 같은 매핑을 주석으로 바꿨다(**다시 돌려도 재부착 안 됨**).

### 경석 영역(몬스터·보스·사운드)
- `MonsterBase`: 피격음(`MonsterHitSoundTracker` — 피해 RPC 1개 = 1번, 23호 자폭 실드 분리 보고 흡수) · 팔라딘 Q 피격음(공격자 팔라딘 AND `SkillQ`, 파생 포함) · 간파 시작음(`_counterVisual` false→true) · 사망음(Dead 전이, 23호 제외) · 펀치 휘두름(`Wind_L/R` 클립 이벤트).
- `TwentyThreeBoss`: 간파 시작음(창 열림) · 잡기 시작 1회 · 내려치기 매 타 · 돌진 벽 충돌음(`PlayDashHitClientRpc(endsDash)` — 🔴 **기존 RPC 인자 추가**, 레이지 제외) · 보스 사망 BGM 2초 아웃(서버 가드 앞) · 이미 죽은 채 합류 보정.
- `SpinnerBot`: 회전음 = `PlaySpinLoopClientRpc` ~ `StopSpinEffect`·디스폰.
- `WellsDroneAttack`: 드론 소리 = Lock 경과로 폭발 0.5초 전 시작, 정상 폭발은 끝까지, **실제 취소(`ClientAbort`)만** 0.1초 정지, 로딩 예열 무음.
- `AudioManager` BGM: 한 곳에서 판단(아래).
- `BossEncounterDirector.HandlePhaseBgm`: `SceneBgmSwitcher`(배선 없어 보스 곡이 안 났음) → `AudioManager.SetBossBgmState`.

## BGM 규칙(명세 원칙 10) — `AudioManager`
- 상태 = 씬이 원하는 곡(`PlayBGM(key)`) + 보스 단계(`SetBossBgmState`). 지금 곡과 다르면 **아웃 → 끝난 뒤 2초 인**(곡끼리 안 겹침).
- 씬 전환 시작 1초 아웃 `BeginSceneTransition()`: 타이틀 `StartGame` · 로비→맵 `NetworkLoadingFlowController.HandleLoadStarted`(전 피어, 멱등) · 맵→로비 `MapSceneManager.PerformGoToResult`.
- 보스: `Preparing~Dialogue` = 무음(스테이지 곡 1초 아웃) · `Combat` = 보스 곡 2초 인 · 23호 Dead = 2초 아웃, **이후 씬 전환 전까지 고정** · `Idle`/`FailedSafe` = 스테이지 곡.
- 같은 곡 반복 = BGM 엔티티 4개 `TransitionTime 2`·`SeamlessTransitionType 1(Time)`(SVN, 엔티티 직접 — 시트 칸 없음).
- SoundID 단위로 끈다(Seamless 반복은 회차마다 재생기가 바뀜). 진행 중 아웃은 늘이거나 줄이지 않는다.
- `PlayBGM(SoundID)`·`SceneBgmSwitcher` 는 남아 있지만 쓰는 곳이 없다(정리 대상).

## 데이터(SVN)
- xlsx `Sound` 시트: 미사용 클립 제거(`Interrupt_Start_02`·`Heavy_interrupt_hit_1`·`floraphonic`) · `S1_Gunner_Q_Fire` = End01 · 새 키 `S1_Gunner_Q_Cancel`(End02)·`S1_Paladin_Attack_1~4`·`S1_Assassin_Attack_1~4`·`S1_Assassin_Attack_Tf`(Shuffle) · Shuffle = Punch·Spinner_Attack·Footstep · loop 1 = 23_Rush·Spinner_Spin.
  **Unity 에서 `Tools/Data/사운드 동기화` 가 필요하다**(Play 자동 동기화도 돈다) — 새 엔티티·카탈로그가 이때 생긴다.
- 옛 키 `S1_Paladin_Attack`·`S1_Assassin_Attack` 는 시트에 "미사용" 으로 남겨 뒀다 — 정리는 다음(시트 재인덱싱 필요).
- 클립별 볼륨은 전부 1 이었다(보정은 wav 에 들어 있음) — 키를 나눠도 음량 그대로.

## BroAudio 패키지 수정(git) — 🔴 패키지 갱신하면 사라진다, 다시 적용
1. `Runtime/Utility/ClipSelection/ShuffleClipStrategy.cs` `SelectClip` — **직전 클립 절대 제외**(원본은 다시 고를 수 있었다). 테스트 `ShuffleClipStrategyTests`.
2. `ISchedulable.SetStartOffset(float)` + `AudioPlayer`(`_startOffset`, `SetPlaybackData` 에서 0, 재생 시작 `timeSamples` 에 더함) + `AudioPlayerInstanceWrapper` + `EmptyInstance` + `BroAudioChainingMethod.SetStartOffset` — 파일 앞부분 건너뛰기(완성음 정렬, Q11).
모든 수정 지점에 `MainProject` 주석.

## 완성음 측정값(11-10, 10ms RMS — scratchpad `wavpeaks.py`)
| 파일 | 길이 | 충돌·강조 위치 | 처리 |
|---|---:|---|---|
| `Paladin_R_Skill.wav` | 3.00s | 0.95~1.10s(최대 1.08) → **1.0s** | 피해 Hit = 클립 1.367s ÷ (1.2 × 1.43) ≈ 0.80s → 시작에서 **앞 0.2초 건너뜀** |
| `23_Drone.wav` | 1.652s | 뚜렷한 단일 피크 없음(0.18s 최대, 0.92s 상승) | 명세값 0.5s 전 시작(`DroneLeadSeconds`) — **청취로 조정** |
| `Paladin_E_Skill_Success.wav` | 1.50s | 막기 0.10s · 반격 0.56s(간격 0.46s) | 게임 반격 = 막기 0.433s 뒤 → 어긋남 0.03s, **그대로** |
| `Gunner_E_Skill.wav` | 0.80s | 강조 0.48~0.54s | 연출 시각 미측정 — 청취 대기 |
상수는 `GameSoundHooks`(`PaladinRFileImpact`·`PaladinRHitClipTime`·`DroneLeadSeconds`).

## Play 확인 순서(경석)
1. Unity 켜기 → 컴파일 0 · `Tools/Data/사운드 동기화` 오류 0(새 키 9개 엔티티 생성).
2. EditMode `ShuffleClipStrategyTests` · 기존 `SoundSyncPlannerTests`.
3. 솔로: 팔라딘 평타 1→4 · Q(일반 루프/돌파의 방패 대시음)·Q 피격음 · E 실패(만료·키 놓음·대시)·성공 · R 착탄 / 어쌔신 평타 1→4·변신 평타·강타·Q 출발·E·R 시작·해제 / 거너 Q 발사·대시 취소·R 틱 / 몬스터 피격·사망 · 스피너 회전·평타 · 건틀릿 빗맞힘 펀치 / 23호 간파 창·잡기·내려치기 3타·돌진 벽(일반만)·드론 정상·취소·송전탑 / 저체력(80% 기준 확인) / BGM 타이틀→로비→맵→보스 연출→전투→사망.
4. MPPM 2~3인: 호스트·클라 각각 1번씩(중복 0) · 원격 Q 발사 직전 대시 · 변신 중 늦은 합류.

## 알려진 한계·남은 것
- 벤트 발소리(Q7 범위 밖), 송전탑 시간 초과 하강 무음, `23_Jump_End` 기준점 미확인.
- 스피너 회전음·팔라딘 Q 루프는 시작 위치 고정(몸을 따라가지 않음) — 3D 최소 거리 10m 라 체감 작을 것, 거슬리면 추종 재생 추가.
- 23호 내려치기 소리 = 매 타 시작(서버 피해 시각). 애니 충돌 프레임과 다르면 지연 칸 추가.
- 어쌔신 R 사망 무음은 복제 순서로 드물게 어긋날 수 있다(허용, 경석 11-10).
- (Codex 구현 검수 11-10, 남긴 것) 간파 슬로우 중 드론·팔라딘 R 정렬은 게임 시간 vs 오디오 실시간이라 어긋난다 ·
  23호 자폭 실드 분리 보고가 **다른 프레임**에 도착하면 피격음 2번 · 원격 오너가 E 방어 성공 자세 RPC 를 받기 전에 대시하면 그 화면만 실패음 ·
  1초 BGM 아웃 진행 중 보스 사망이 오면 2초로 늘지 않음(BroAudio 는 진행 중 Stop 을 다시 받지 않는다).
- 카탈로그 에셋의 3D·거리·동시 수·피치 칸은 **모든 키가 기본값**(원래부터) — 시트 값은 툴바 `데이터: 테이블` Play·빌드 때 xlsx 에서 들어간다. 인스펙터 모드 Play 로 들으면 2D·제한 없음.
- 몬스터 피격음 동시 재생 2 → **4**(간격 0.06 그대로, 경석 11-10 Play) — 0.77초 클립이 동시 2개에 막혀 연타·다중 타격 때 자주 빠졌다(진단 로그로 확인: 재생 자체는 정상, 제한 차단이 원인).
- EditMode 전체(11-10): 통과 5955 / 실패 12 — 실패는 전부 사운드 무관 기존 건(데칼 렌더 4·로비 포드 바닥 4·피해 숫자 간격·간파 디렉터 스위치 2·몬스터 인스펙터 목록).

## 11-12 교차검증 후속 (Codex `-s read-only` + 코드 재확인) — 미커밋
결과 원문: 사이드 세션 노트 `codex-review-sound-monster-boss-result.md`. 고친 것:

| # | 증상 | 원인 | 수정 |
|---|---|---|---|
| 1 | 판 도중 재합류(C14)한 사람만 보스전 중 스테이지 곡 / 보스 사망 후 재합류하면 스테이지 곡 | 동기화(보스 단계·23호 Dead 적용) **뒤에** `MainGameReady` → `PlayBGM("BGM_InGame")` 이 `_bossBgm = None` 으로 덮음 | `SetBossBgmState(state, owner)` 가 owner 의 씬을 기억, `PlayBGM` 은 **그 씬이 아직 떠 있으면** 보스 상태를 지우지 않는다(ESC→타이틀·로비는 맵이 내려가 예전처럼 지움). 이번 커밋에서 생긴 빈틈(이전엔 보스 곡 자체가 안 났음) |
| 2 | 호스트 끊김 → 재접속 대기 씬에서 이전 곡(보스 곡 포함) 계속 | `DemoSessionDirector.CleanupAfterDrop` 에 BGM 처리 없음(원래부터) | `BeginSceneTransition()` 1초 아웃 — **은희 영역 한 줄** |
| 3 | 23호 디스폰(전멸 리스폰·연출 중단·씬 내림) 때 잡기 시작·잡기 전기·점프 시작음이 1.5~2초 남음(사망은 정상) | NGO 가 `IsSpawned=false` 뒤 `OnNetworkDespawn` → `if (IsSpawned) StopAttackSoundsClientRpc()` 가 늘 빠짐, 클라는 서버 전용 경로 | `OnNetworkDespawn` 에서 각 피어가 `StopAttackSoundsLocal()` |
| 4 | 취약 중 팔라딘 Q·간파 넉백이 **경직만 들어가고 안 밀림**(간파 직후 수 초) | 23호 SO `startsWithSuperArmor: 1` 이라 공격 끝 슈퍼아머 해제가 안 돌고, 끊긴 공격의 슈퍼아머가 공격 예산(돌진 ≈4.4초)까지 남아 `Unit.Knockback` 이 조용히 거부 | `StartVulnerable` 에서 `RemoveStatus(SuperArmor)`(취약 밖은 상태별 슈퍼아머가 그대로 막음) + 거부 시 경고 `"[23호] 취약 중 팔라딘 Q — 밀림 거부(…)"` |
| 5 | 취약 CC 로 훅·어퍼가 끊기면 바람 루프(`Wind_L/R`)가 가끔 5초 남음 | 공격 이탈 때 루프를 끈 **직후** 0.1초 CrossFade 동안 나가는 클립의 `StartEffect`(훅 L 0.193 — 예고 재개 0.15 바로 뒤)가 다시 켬, 짝 `StopEffect` 는 영영 안 옴 | `MonsterBase.RestopExitLoopsAfterTransition` — 전환이 끝난 뒤 같은 id 를 한 번 더 끔(그새 Attack 이면 건너뜀, 1초 상한). 23호·건틀릿·스피너 공통 |

남긴 것(⚠️, 트리거 미발견): 씬 전환 뒤 Combat 재요청 시 보스 곡 재예약 · BroAudio 시작 전 Stop 재생기 재사용 시 새 소리 즉시 꺼짐(패키지 기존 결함) · 드론 `Despawn(false)`(호출부 없음). 취약 중 Q 가 **밀리다 멈춤**은 미수정 — 취약 시간 만료(`"취약 종료 — 시간 종료"`) 또는 NavMesh 가장자리(`"NavMesh 여유 …"` 한 축 > 1.53)를 로그로 가린다.

### Play 체크리스트 (11-12 후속)
**솔로**
1. 23호 돌진·잡기 **간파 직후** 그로기 풀리자마자 팔라딘 Q → 보스가 매 틱 밀린다 · 로그 `"취약 넉백 — 3m"` 동반 · `"밀림 거부"` 경고 0. 취약이 끝나면 다시 안 밀리는 것은 정상.
2. 같은 상황에서 간파 스킬 넉백도 밀린다(예전엔 같은 구간에 안 밀림).
3. 취약이 **아닐 때** Q → 밀리지도 경직도 없다(평상시 슈퍼아머 유지 — 회귀 확인).
4. 취약 중 훅L·훅R·어퍼 **예고가 끝나 휘두르기 시작한 직후** Q·간파로 끊기 반복 → 바람 이펙트가 바로 사라짐 · Editor.log 에 `'Wind_…'가 5.0초 동안 종료되지 않아 강제 회수` 0. 건틀릿·스피너 공격 끊김도 이펙트 잔존 없음.
5. 끊긴 직후 바로 다음 훅이 나올 때 바람 이펙트가 정상으로 켜진다(재정지가 새 공격을 끄지 않음).
6. 잡기·점프 시작음이 나는 중 **전멸** → 리스폰 화면에서 그 소리가 0.1초 안에 끊긴다. 23호 사망 때 공격음도 끊긴다.
7. BGM 회귀: 타이틀 → 로비 → 맵 곡 → 보스 연출 무음 → 전투 보스 곡 → 사망 2초 아웃 → 결과/로비 곡. 보스전 중 ESC → 타이틀이면 타이틀 곡(보스 곡 안 남음). 전멸 리스폰 후 스테이지 곡.

**MPPM / 빌드 2대 (데모 흐름)**
8. 보스 **전투 중** 클라 하나 강제 종료 → 대기 씬에서 곡이 1초 안에 꺼짐 → 재합류 → 그 클라에 **보스 곡**(스테이지 곡 아님).
9. 보스 **사망 후** 재합류 → 그 클라는 무음(스테이지 곡 안 남).
10. 보스 연출 전(맵 진행 중) 재합류 → 스테이지 곡.
11. 호스트·클라 각각 1~6 의 소리·이펙트가 한 번씩만(중복 0).

**11-12 경석 Play(솔로) 결과:** 1~5·7 확인 — 취약 Q 가 벽까지 연속 밀림(로그 `취약 넉백` 최대 4회 → 외곽 벽 충돌), 바람 잔존 0, BGM 흐름 정상(로딩씬 진입 약 1초 뒤 아웃 · 보스 연출 무음 → 전투 보스 곡 · 사망 2초 아웃 → 로비). 남은 것: 8~11 MPPM 재합류, **로딩씬 BGM 유무(기획 문의 중)**, 연출 구간 효과음(보스 등장·사망 연출). ⚠️ 이 PC 에디터 PlayerPrefs 음악 볼륨이 0으로 저장돼 BGM 이 안 들렸던 적 있음 — 연결 문제로 오인하지 말 것(`Settings.Audio.Volume.1`).

## 11-12 BGM 흐름 규칙 변경 (경석 지시) — 미커밋
**연출 동안은 연출 사운드만(BGM 없음), 로딩씬은 로비 곡.** `AudioManager` 에 두 가지를 더했다.
- `EnterLoadingBgm()` — 로딩 씬 = `BGM_Lobby`(`LoadingBgmKey`). `NetworkLoadingFlowController` 의 예전 `BeginSceneTransition` 4곳(서버 로딩 시작·재시작, 클라 LoadingScene 상태, 맵 로드 시작)을 이걸로 바꿨다. 같은 곡이면 아무 일도 없다.
- `BeginCinematicBgm(owner)` / `EndCinematicBgm(owner)` — 연출 owner 가 하나라도 있으면 목표 곡 없음, 지금 곡 **0.1초 아웃**(뚝 끊으면 틱 잡음). 끝나면 그 씬 곡(보스 단계면 보스 곡) 2초 인. 파괴된 owner 는 자동으로 빠진다. `BeginSceneTransition`·`EnterLoadingBgm` 이 전부 지운다.

| 구간 | 지금 동작 | 거는 곳 |
|---|---|---|
| 타이틀 | Press·접근 동안 타이틀 곡 → **모니터 꺼짐 시작에 0.1초 아웃** | `TitleFlowDirector.PowerOffThenLobby` → `BeginSceneTransition(0.1f)` |
| 로비 끝 포드 발사 | 시작 순간 로비 곡 0.1초 아웃(연출 사운드만) | `LobbyPodLaunch.Update` 시작 시각 처음 본 순간 1번 · 끝/취소 = `ClearLocal` |
| 로딩씬 | 로비 곡 2초 인 | `EnterLoadingBgm` |
| 맵 포드 착륙 | 로딩이 걷히는 순간 로비 곡 0.1초 아웃 → 착륙 끝나면 **인게임 곡 2초 인** | `MapSceneManager.PlayInGameBgm`(맵 곡보다 먼저 무음) · `MapPodArrival.LateUpdate`(시작 본 순간 1번, 끝·중단 = `ClearLocal`/미무장) |
| 존4 소개 연출 | **BGM 안 끊김**(인게임 곡 그대로 — 경석 11-12 정정, 무음 처리 되돌림) | — |
| 보스 엘리베이터(문 앞 이동·탑승·상승·암전) | 시작 순간 인게임 곡 0.1초 아웃 → 도착 후 보스 등장 연출 무음으로 이어짐. 도착 무효·재도전 리셋이면 인게임 곡 2초 인 | `BossTeleportManager.UpdateElevatorLocal`(복제 시작 시각, 0 = 끝) |
| 보스 등장·사망 | 그대로(등장 무음 → 전투 보스 곡 / 사망 2초 아웃) | 보스 단계 |
| 맵→로비(결과·전멸) | 그대로 1초 아웃 → 로비 곡 | `PerformGoToResult` |
| 재접속 대기 씬 | 1초 아웃 | `CleanupAfterDrop` |

### Play 체크리스트 (BGM 흐름)
1. 타이틀: Press 문구·카메라 접근 동안 타이틀 곡 유지 → 모니터가 꺼지기 시작하는 순간 곡이 바로 꺼짐.
2. 로비: 로비 곡 → 출격 집결 확정 후 **포드 발사가 시작되는 순간** 곡이 바로 꺼지고 발사 효과음만.
3. 로딩씬이 뜨면 로비 곡이 2초에 걸쳐 다시 나옴(처음부터).
4. 맵: 로딩 화면이 걷히고 포드 착륙이 시작되는 순간 로비 곡이 바로 꺼짐 → 착륙 연출(효과음만) → 연출이 끝나 UI 가 돌아오는 순간 인게임 곡 2초 인. (예전처럼 로딩 진입 1초 뒤에 꺼지지 않아야 한다.)
5. 존4 진입 소개 연출: 인게임 곡이 **끊기지 않고** 그대로 이어진다.
5-1. 보스 엘리베이터: 집결 확정 후 연출(암전→문 앞→탑승→상승)이 시작되는 순간 인게임 곡이 바로 꺼지고, 보스방 도착·등장 연출 내내 무음 → 전투 시작에 보스 곡. (11-12 Play 지적: 예전엔 등장 연출 시작까지 인게임 곡이 남았다)
6. 보스 연출 무음 → 전투 보스 곡 → 사망 2초 아웃 → 로비 곡(변화 없음 확인).
7. 포드 발사가 취소되는 경우(가능하면)엔 로비 곡이 다시 나온다.
8. MPPM: 클라도 같은 순간에 꺼지고 켜진다(로딩씬 = 로비 곡, 착륙 끝 = 인게임 곡).


## 11-12 연출 사운드 5종 (SVN r670 `Sound_2/SFX/Direct`) — 미커밋
키 = xlsx `Sound` 시트 entries[66~70](라이브러리 `Sound1Library`, 엔티티 = 키). 각 피어가 자기 화면 시각에 맞춰 로컬 재생(새 RPC 없음), 늦게 본 피어는 지난 만큼 앞을 건너뛰고 다 지났으면 안 튼다(`GameSoundHooks.PlayCinematic`).

| 키 | 파일(길이) | 언제 | 기준(측정 50ms RMS) | 2D/3D | 거는 곳 |
|---|---|---|---|---|---|
| `Direct_Pod_Start` | Pod_Start(8.3s) | 로비 **해치 열림 − 0.75초**(`PodStartLeadSeconds`, 청취 3회째, 해치 클립 이벤트로 미리 계산 · 11-12 Play 정정 2회 — 발사 시작 기준이면 끝 2초가 로딩씬에 샘, 해치 기준이면 문 열림 소리 0.5초 늦음) | 2.0s = 발사(+3.83) · 5.3~6s = 가속·하강(+7.4~7.6) · 끝 = 로딩(+10.13) | 2D | `LobbyPodLaunch.UpdateLauncher` · 취소·재시작만 끊음 |
| `Direct_Pod_End` | Pod_End(5.03s) | 맵 착지 시각에 파일 0.85초 충돌음 | 착지 = 시작 + √(2·5/9.81) ≈ 1.01s(앞 구간 건너뛰기) | 2D | `MapPodArrival.LateUpdate`(`TouchdownAt`) |
| `Direct_Elevator_Start` | Elevator_Start(5.9s) | 파일 2.1초(둘째 소리) = 문 열림(`DoorOpenAt` 2.9s) → 0.8s 에 시작 | 앞 0.15~1.6s = 하강 | **3D** | `BossTeleportManager.UpdateElevatorSounds` |
| `Direct_Elevator_End` | Elevator_End(4.47s) | 문 닫힘 시작(`DoorCloseAt`) | 0~1.7s 닫힘(클립 1.68s) · 2.55s 상승 | **3D** | 같은 곳 · 도착 전 취소면 끊음 |
| `Direct_Boss_Start` | Boss_Start(7.6s) | 등장 시네마틱 Preparing 시작 | 2.75~3s 최대 ≈ 착지 충격 2.8s(워크인 1.6 + 강하 1.2) | 2D | `BossEncounterDirector.HandlePhaseSound` · 스킵·중단이면 0.3초 아웃 |

**엘리베이터 3D 거리감(기획 "너무 가깝다")**: 리스너는 플레이어에 붙어 칸에 같이 타므로 칸 위치에 틀면 거리 0 이다. 그래서 발원을 **리스너 + (칸 − 카메라)** 가상 자리(`ElevatorSoundAnchor`, 매 프레임 이동)에 둔다 = 쿼터뷰 카메라가 칸을 보는 거리·방향에서 들린다. 감쇠는 시트 `minDistance 8 / maxDistance 60` — 시작음 재생 때 `[Sound] 엘리베이터 시작음 — 가상 거리 Nm` 로그가 찍히니 그 값을 보고 시트에서 조정한다(멀게 = min 을 줄인다). `AudioManager.Play(key, Transform follow)` 를 새로 둠(BroAudio followTarget).

### Play 체크리스트 (연출 사운드)
0. Unity `Tools/Data/사운드 동기화` → 엔티티 5개 생성(동기화 창에서 '동기화').
1. 로비 출격: 발사 시작에 출발음 → 가장 큰 구간이 포드가 빨라질 때, 페이드 무렵 잦아드는지.
2. 맵 착륙: 충돌음이 포드가 땅에 닿는 순간인지(어긋나면 `PodEndFileImpact`).
3. 엘리베이터: 하강음 → 문 열릴 때 둘째 소리 / 문 닫힘 → 상승음. 거리감(로그의 가상 거리)·좌우가 이상하지 않은지.
4. 보스 등장: 착지 충격과 최대 음량이 맞는지 · 스킵 시 0.3초에 꺼지는지.
5. 존4 소개 연출: 인게임 곡이 끊기지 않는지.
6. MPPM: 클라도 같은 순간 1번씩(중복 없음).
