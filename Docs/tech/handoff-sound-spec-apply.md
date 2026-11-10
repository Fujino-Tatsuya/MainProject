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
