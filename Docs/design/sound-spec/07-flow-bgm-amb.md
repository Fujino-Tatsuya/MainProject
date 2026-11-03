# 07. 씬·맵 흐름 · BGM · 환경음

> 공통 열 뜻은 [README.md §2](README.md#2-표-읽는-법). 흐름 이벤트는 대부분 **전원**(서버가 복제한 상태를 각 PC 가 받아 각자 재생).
> ⚠️ 구역 첫 진입·존 클리어·보스방 도착은 **서버 전용 이벤트**다 — 클라에서 소리를 내려면 `StepChanged`·`GateOpened`·`StateChanged` 로 받아야 한다([tutorial-director.md](../../tech/tutorial-director.md)).

## 7.1 포드 출격(로비) · 포드 착지(맵)

| 키 | 언제 | 듣는 사람 | 2D·3D | 길이·반복 | 느낌 · 레퍼런스 | 녹화 | 프롬프트(EN) | 상태 |
|---|---|---|---|---|---|---|---|---|
| `Pod_Hatch_Open` | 해치 열림 → 탑승(`LobbyPodLaunch.cs:286-292`) | 전원 | 3D 3~20m | 1.5s | 유압 해치 · Helldivers 2 헬포드 | 해치 열림부터 탑승까지 8초 | `heavy drop pod hatch opening, hydraulic hiss, metal latches, 1.5s` | ❌ |
| `Pod_Seat` | 좌석 도착·고정(`:296-302`) | 전원 | 3D 2~10m | 0.5s | 안전바 "철컥" | (위 클립에 포함) | `seat harness locking clunk, metal, 500ms` | ❌ |
| `Pod_Launch` | 발사 클립(해치 닫힘 → 갱도 문 → 하강, `:505-526`) | 전원 | 2D | 클립 길이 맞춤(약 4~6s) | 해치 닫힘 → 엔진 점화 → 낙하 | **발사 클립 전체**(영상→효과음 1순위) | `drop pod launch sequence: hatch slams shut, engine ignition roar, rapid descent rumble` | ❌ |
| `Pod_Launch_Boost1` / `_Boost2` | 가속 1·2단계 애니 이벤트(`:350-365`) | 전원 | 2D | 1.0s | 추진 폭발 레이어 | (위 클립) | `rocket thruster burst, deep boom, 1s` | ❌ |
| `Pod_Fall_Loop` | 맵 포드 낙하 시작 ~ 급감속(`MapPodArrival.cs:245-257`) | 전원 | 2D | 루프 | 고속 낙하 바람 + 진동 | 맵 진입 낙하 구간 | `high speed atmospheric fall wind roar with metal rattle, loop` | ❌ |
| `Pod_Retro` | 급감속·날개 펴기(`:307-310`) | 전원 | 3D 5~40m | 1.2s | 역추진 분사 | (낙하 클립에 포함) | `retro thrusters firing for landing, powerful jet blast, 1.2s` | ❌ |
| `Pod_Impact` | 착지 충돌(`:615-622`) | 전원 | 3D 5~50m | 1.5s | 땅이 울리는 착지 | 착지 순간 앞뒤 3초 | `massive metal drop pod impact on concrete, ground shake boom, debris, 1.5s` | ❌ |
| `Pod_Open_Fold` | 해치 열기·날개 접기(`:315-318`) | 전원 | 3D 3~20m | 1.5s | `Pod_Hatch_Open` 변형 | (착지 클립) | (공용 변형) | ❌ |
| `Pod_Dissolve` | 디졸브로 포드 제거(`:551-566`) | 전원 | 3D 3~20m | 1.0s | 에너지로 흩어짐 | (착지 클립 끝) | `sci-fi object dissolving into particles, shimmering fade, 1s` | ❌ |

## 7.2 존 흐름 (게이트·배리어·다리·타이머)

| 키 | 언제 | 듣는 사람 | 2D·3D | 길이·반복 | 느낌 · 레퍼런스 | 녹화 | 프롬프트(EN) | 상태 |
|---|---|---|---|---|---|---|---|---|
| `Gate_Open` | 게이트 열림 G1~G10(`MapSceneDirector.cs:1031-1040` `GateOpened`) | 전원 | 3D 3~25m | 1.2s | 공장 셔터 · `Trait_Gate_Open` 과 같은 계열 | 게이트 열림 5초 | `industrial shutter gate rolling open, metal clatter and motor, 1.2s` | ❌ |
| `Barrier_Off` | 에너지 배리어 해제(`MapSceneBarriers.cs:193-203`) | 전원 | 3D 2~15m | 0.8s | 전기장 꺼짐 "윙-" | 배리어 해제 3초 | `energy barrier shutting down, electric hum fading with zap, 800ms` | ❌ |
| `Barrier_On` | 등 뒤 개인 배리어 닫힘(`:119-143`) | 나만 | 3D 2~10m | 0.6s | 반대 | — | `energy barrier activating, electric hum rising, 600ms` | ❌ |
| `Barrier_Loop` | 배리어 켜져 있는 동안 | 전원 | 3D 1~8m | 루프 | 낮은 전기 험 | — | `low electric force field hum, seamless loop` | ❌ |
| `Zone_EnemyWake` | 존2 진입 4초 뒤 기상(`MapSceneZoneFlow.cs:104-116`) · 존3 스피너봇 기상(`:152-160`) | 전원 | 2D | 1.5s | 경보 스팅어 — 적이 깨어남 | — | `enemy alert stinger, robotic power-up siren, short, 1.5s` | ❌ |
| `Zone_Clear` | 존 클리어(서버 `MapSceneDirector.cs:579-591`, 클라 = 다음 게이트 열림) | 전원 | 2D | 1.5s | 밝은 클리어 스팅어 | — | `area cleared stinger, positive synth hit, 1.5s` | ❌ |
| `Timer_Start` | G8 타이머 시작(`MapSceneDirector.cs:697`) | 전원 | 2D | 0.8s | 카운트 시작 "띠-" | — | `countdown timer start, alarm clock style digital beep, 800ms` | ❌ |
| `Bridge_Panel` | 다리 패널 활성화 F(`ZoneBridgeGateManager.cs:377-433`) | 전원 | 3D 2~15m | 0.5s | 단말기 켜짐 + 진행음 | 패널 활성화 5초 | `control panel activation, console power-up beep, 500ms` | ❌ |
| `Bridge_Extend_Loop` | 다리 개통 이동(`:471-485`) | 전원 | 3D 5~30m | 루프 + 정지 | 거대한 기계 다리 펼침 | 다리 펼침 전체 | `huge mechanical bridge extending, heavy gears and hydraulics, loop` | ❌ |
| `Elevator_Door` | G10 엘리베이터 문 열림(`MapSceneElevatorDoor.cs:68`) | 전원 | 3D 3~20m | 1.2s | 화물 엘리베이터 문 | 문 열림 3초 | `freight elevator doors opening, heavy metal slide, 1.2s` | ❌ |
| `Timer_Expire_Alarm` | 타이머 만료 → 3초 경고 → 강제 이동(`BossTimerManager.cs:352-355`) | 전원 | 2D | 3.0s | 경보 사이렌 | — | `facility alarm siren, urgent, 3 seconds` | ❌ |

## 7.3 엘리베이터 → 보스방

| 키 | 언제 | 듣는 사람 | 2D·3D | 길이·반복 | 느낌 · 레퍼런스 | 녹화 | 프롬프트(EN) | 상태 |
|---|---|---|---|---|---|---|---|---|
| `Pad_Countdown_Tick` | 패드 집결 카운트다운 초마다(`BossTeleportManager.cs:1206-1234`) | 전원 | 2D | 0.05s | `UI_CountdownTick` 공용 | — | (공용) | ❌ |
| `Elevator_Descend_Loop` | 하강(`BossElevatorTimeline.cs:11-22`) | 전원 | 2D | 루프 | 거대 리프트 하강 · Doom 엘리베이터 | 엘리베이터 시퀀스 전체 | `large industrial lift descending, deep rumble, cable whine, loop` | ❌ |
| `Elevator_Stop` | 정지 | 전원 | 2D | 1.0s | "쿵" 정지 | (위 클립) | `heavy elevator stopping clunk, metal thud, 1s` | ❌ |
| `Elevator_DoorOpen` / `_DoorClose` | 문 열림·닫힘 | 전원 | 2D | 1.2s | `Elevator_Door` 공용 | (위 클립) | (공용) | ❌ |
| `Elevator_Ascend_Loop` | 상승 | 전원 | 2D | 루프 | 하강 반대 | (위 클립) | (하강 변형) | ❌ |
| `BossRoom_Arrive` | 보스방 도착 페이드 인(`BossTeleportManager.cs:929`) | 전원 | 2D | 2.0s | 넓은 공간의 낮은 드론 | — | `ominous low drone swell, large empty industrial hall, 2s` | ❌ |

## 7.4 사망 · 관전 · 전멸

| 키 | 언제 | 듣는 사람 | 2D·3D | 길이·반복 | 느낌 · 레퍼런스 | 녹화 | 프롬프트(EN) | 상태 |
|---|---|---|---|---|---|---|---|---|
| `Ally_Down` | 동료 사망 알림(`PlayerLifeCycleController.cs:46`) | 다른 사람 | 2D | 0.8s | 무겁고 짧은 알림 | — | `teammate down alert, low somber tone, 800ms` | ❌ (본인 사망음은 [01-player](01-player.md)) |
| `Revive_Tutorial` | 튜토리얼 자동 부활 3초(`MapSceneDeathRules.cs`) | 전원 | 3D 2~15m | 1.0s | 재기동 | 부활 순간 | `robotic reboot revive, power surge rising, 1s` | ❌ |
| `Spectate_Enter` | 관전 진입(`CameraTargetSwitcher.cs:452`) | 나만 | 2D | 1.0s | 귀가 먹먹해짐(전체에 저역 필터) | — | `muffled underwater transition whoosh, 1s` | ❌ · 저역 필터 코드 필요 |
| `Spectate_Switch` | 관전 대상 전환 | 나만 | 2D | 0.15s | `UI_TabSwitch` 공용 | — | (공용) | ❌ |
| `Party_Wipe` | 전멸(`PartyWipeWatcher.cs:75-89`) | 전원 | 2D | 2.0s | 패배 스팅어 | — | `mission failed stinger, deep descending synth, 2s` | ❌ |
| `Party_Retry` | 전멸 리스폰(`PartyWipeRespawner.cs:128`) | 전원 | 2D | 1.5s | 재도전 | — | `retry stinger, determined rising synth, 1.5s` | ❌ |

## 7.5 BGM

> 현재 키 3개(`BGM_Title`·`BGM_Lobby`·`BGM_InGame`)뿐이고 씬 매니저 3곳에 하드코딩. `SceneBgmSwitcher` 는 어디에도 안 붙어 있다.
> **씬 안 구간(존·보스·승리)을 나누려면 이벤트 기반 전환기가 새로 필요** — 이 표는 "곡이 필요한 구간" 명세이고, 전환 코드는 별도 작업.
> BGM 은 텍스트→음악 생성이 맞다. 루프 곡은 **이음매 없는 루프**로 요청하고, 길이는 1~2분 루프 기준.

| 키 | 구간 | 전환 근거 | 길이·반복 | 느낌 · 레퍼런스 | 프롬프트(EN) | 상태 |
|---|---|---|---|---|---|---|
| `BGM_Title` | 타이틀 | `TitleSceneManager.cs:27` | 루프 | 정적인 레트로 신스, 고장 난 모니터실 | — | ✅ 있음 |
| `BGM_Lobby` | 로비 평상시 | `LobbySceneManager.cs:38` | 루프 | 기지 대기실, 느긋한 신스 | — | ✅ 있음 |
| `BGM_Lobby_Training` | 로비 훈련 Train01~03 | `LobbyTrainingDirector.cs:627` | 루프 1:30 | 가벼운 전투 연습, 템포 중간 | `light training combat music, synthwave with punchy drums, medium tempo 110bpm, seamless loop` | ❌ |
| `BGM_Briefing` | 브리핑 약 23초 | `LobbyBriefing.cs:172` | 25s 1회 | 작전 브리핑, 긴장 깔린 패드 | `military sci-fi mission briefing underscore, tense pads, ticking pulse, 25 seconds` | ❌ (또는 로비 BGM 덕킹) |
| `BGM_InGame` | 맵 존1~4 공용(현재) | `MapSceneManager.cs:52` | 루프 | 공장 스테이지 | — | ✅ 있음(`BGM_Factory_Stage_01`) |
| `BGM_Zone_Explore` | 존 탐색(게이트 열림 ~ 적 기상 전) | `MapSceneDirector.cs:135` | 루프 2:00 | 공장 탐색, 낮은 긴장 | `industrial factory exploration music, low tension, mechanical rhythms, dark synth, 90bpm, seamless loop` | ❌ |
| `BGM_Zone_Combat` | 존 전투(적 기상 ~ 클리어) | `MapSceneZoneFlow.cs:114,156` | 루프 1:30 | 탐색 곡과 같은 키·템포의 전투 레이어 · Ravenswatch 필드 전투 | `intense industrial combat music, driving drums, distorted synth bass, 128bpm, seamless loop, same key as exploration track` | ❌ |
| `BGM_Zone4_Timer` | 존4 타이머 진행 | `MapSceneDirector.cs:697` | 루프 1:30 | 시간 압박, 템포 업 | `time pressure chase music, ticking percussion, urgent synth arpeggios, 140bpm, seamless loop` | ❌ |
| `BGM_MidBoss` | 중간보스전(다리 개통 ~ 존4 클리어) | `MapSceneBossApproachGuide.cs:198-207` | 루프 1:30 | 거대 기계와 전투 | `mini boss battle music, heavy industrial metal, aggressive synths, 135bpm, seamless loop` | ❌ |
| `BGM_Elevator` | 엘리베이터 하강 | `BossTeleportManager.cs:420~929` | 시퀀스 길이 1회 | 고조되는 긴장, 끝에서 끊김 | `ominous building tension, low drones and heartbeat percussion, ends abruptly` | ❌ (정지로 대체 가능) |
| `BGM_BossIntro` | 보스 등장 컷신 | `BossEncounterDirector.cs:447~1126` | 컷신 길이 1회 | 거대한 등장, 착지에 맞춘 히트 | `boss reveal cinematic music, massive orchestral hit with industrial synths, rising, 20 seconds` | ❌ |
| `BGM_Boss23_P1` / `_P2` / `_P3` | 23호 페이즈별 | `TwentyThreeBoss.cs:7300-7337` (⚠️ 페이즈가 클라로 복제 안 됨 — NetworkVariable 필요) | 루프 2:00 | 같은 테마로 점점 격해짐 | `epic final boss battle music, industrial robot theme, phase N intensity, 150bpm, seamless loop` | ❌ |
| `BGM_Raid_Countdown` | 레이드 ④ 폐기 처분(자폭 카운트다운) 등 긴박 구간 | `BossDataSO.cs:187-199`, `TwentyThreeBoss.cs:1035-1075` | 루프 레이어 | 기존 보스 곡 위에 얹는 경보 레이어 | `self-destruct countdown tension layer, alarm pulses, rising, seamless loop, to layer over boss music` | ❌ |
| `BGM_Victory` | 승리 | `BossVictoryPresentation.cs:237` | 5~8s 1회 | BGM 끊고 승리 징글 | `victory fanfare, triumphant sci-fi brass and synth, 6 seconds` | ❌ |
| `BGM_Defeat` | 전멸 → 결과 | `PartyWipeWatcher.cs:89` | 5s 1회 | 무거운 패배 | `defeat theme, somber low strings and synth, 5 seconds` | ❌ |

## 7.6 환경음(AMB) — 루프는 전부 3D(위치 고정 오브젝트) 또는 2D 베드

| 키 | 위치 | 듣는 사람 | 2D·3D | 길이·반복 | 느낌 | 녹화 | 프롬프트(EN) | 상태 |
|---|---|---|---|---|---|---|---|---|
| `AMB_Factory_Bed` | 맵 전체 베드 | 전원(로컬 루프) | 2D | 루프 2:00 | 먼 기계음·환풍기·금속 울림 | — | `large industrial factory ambience, distant machinery, ventilation hum, occasional metal clanks, seamless loop` | ❌ (`Assets/51.Audio/AMB/` 비어 있음) |
| `AMB_SteamVent` | 증기 벤트 6초 주기(`Map/SteamVent.cs`) | 전원 | 3D 2~15m | 분사 1.5s | 증기 "쉬익" | 분사 2회 포함 15초 | `industrial steam vent burst, pressurized hiss, 1.5s` | ❌ |
| `AMB_FloorVent_Warn` / `_Active` | 바닥 벤트 경고·활성(`Map/Vent.cs`) | 전원 | 3D 2~12m | 경고 1s / 활성 루프 | 경고 = 압력 차오름(핵심) / 활성 = 분출 | 경고~활성 한 주기 | `pressure building rumble warning, 1s` / `floor vent hazard blasting hot air, loop` | ❌ |
| `AMB_Conveyor_Loop` | 컨베이어(`ConveyorTile.cs`) | 전원 | 3D 1~10m | 루프 | 벨트·롤러 | 컨베이어 옆 10초 | `conveyor belt rollers running, mechanical loop` | ❌ |
| `AMB_Platform_Move` / `_Stop` | 이동 플랫폼(`MovingPlatform.cs`) | 전원 | 3D 2~15m | 루프 / 0.5s | 모터 / 철컥 | 이동~정지 | `moving platform motor hum loop` / `platform stop clunk, 500ms` | ❌ |
| `AMB_LaserWall_Loop` | 레이저 벽(`Wall_Hallway_laser`) | 전원 | 3D 1~6m | 루프 | 레이저 험 | — | `laser barrier buzzing hum, seamless loop` | ❌ (추정 지점) |
| `AMB_BridgeMonitor_On` | 다리 패널 모니터 켜짐(`ZoneBridgeGate.cs:127`) | 전원 | 3D 1~8m | 0.4s | `Bridge_Panel` 공용 | — | (공용) | ❌ |
| `AMB_Elevator_Idle` | 엘리베이터 칸 대기(`BossElevatorRig.cs`) | 전원 | 3D 1~10m | 루프 | 대기 험 | — | `elevator idle machine hum, loop` | ❌ |
| `AMB_Lobby_Room` | 로비 실내(사무실·관제실) | 전원(로컬 루프) | 2D | 루프 | 실내 톤 + 먼 무전 잡음 | — | `sci-fi military base control room ambience, computer hum, distant radio chatter, seamless loop` | ❌ |
| `AMB_Lobby_CRT` / `_Projector` | 로비 CRT 모니터·슬라이드 프로젝터 | 전원 | 3D 1~5m | 루프 | 고주파 / 팬 | — | `CRT monitor whine loop` / `slide projector fan loop` | ❌ |
| `AMB_PodBay_Idle` | 로비 포드 발사대(`PF_DropPod_LaunchStation_Combined_v001`) | 전원 | 3D 3~20m | 루프 | 대기 험 | — | `launch bay idle hum, large machinery standby, loop` | ❌ |

> 송전탑·전기 장판 등 **보스 기믹 소리**는 [05-boss23.md](05-boss23.md) 에 있다.

## 대조
- 조사 메모 §3(흐름) · §4(BGM 17구간) · §5(AMB 14곳) 각 행을 이 문서 행 또는 "(공용)" 으로 대응. 웰즈 독백·보스 착지 등 보스 컷신 개별음은 05 로 넘김.
