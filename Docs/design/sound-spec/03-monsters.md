# 03. 일반 몬스터 — 실제 스폰 4종

> 공통 열 뜻은 [README.md §2](README.md#2-표-읽는-법). 적은 **서버 권한**이지만 소리는 각 피어가 상태 복제(`MonsterBase._state`)·애니 이벤트(`PlayEffect`)·RPC 를 보고 **각자 재생** → 전부 "전원 · 3D".
> 소리를 붙일 자리: 애니 이벤트 `PlayEffect` 로 나가는 **VFX 엔트리의 `EffectPart.sound`**(프레임 정확). `OnAttackHit` 은 서버 판정 전용이라 소리 자리로 쓰지 않는다.
> 범위(2026-10-30 확인): `4.MapScene` 의 `StageTutorial` 실제 스폰 = 존2(ChompBot·HumanoidBot·MortarBot) · 존4(위 + PeekABot). 존1 은 0마리.
> 레퍼런스 톤: 공장 경비 로봇 — 서보 모터·유압·금속, 사망 = 폭발 + 파편. 크기가 작을수록 음정 높게.

## 3.0 일반 몬스터 공용 (4종이 같이 씀)

| 키 | 언제 | 듣는 사람 | 2D·3D | 길이·반복 | 느낌 · 레퍼런스 | 짝 VFX | 녹화 | 프롬프트(EN) | 상태 |
|---|---|---|---|---|---|---|---|---|---|
| `Mob_Wake` | 휴면 해제(존 진입·게이트, `SetDormantServer`) — **화면 신호 없음**, 상태 변화 감지 필요 | 전원 | 3D 3~25m | 0.6s · 변형 3 | 로봇 기동 "위잉-삑" | — | 존2 진입 장면 | `robot powering on, servo whir and beep boot-up, 600ms` | ❌ · 훅 없음 |
| `Mob_Aggro` | 인지 → 추격(Idle→Chase) | 전원 | 3D 3~20m | 0.3s · 변형 3 | 감지 "삐빅!" · MGS 발견음보다 작게 | — | 몬스터 시야에 들어가기 | `robot detecting target, short electronic alert chirp, 300ms` | ❌ · Chase 가 귀환에도 쓰여 구분 필요 |
| `Mob_Hurt` | 몬스터 피격(`PlayHitVFXRpc`) | 전원 | 3D | 0.2s · 변형 4 | 플레이어 적중음 아래 깔리는 로봇 쪽 반응 "찌직" | `FX_Hit_dark_magic` | 몬스터 타격 | `robot taking damage, electric crackle and metal dent, 200ms` | ❌ |
| `Mob_Stun` | 넉백 후 기절(`FX_Faint`) | 전원 | 3D | 0.4s | [02 `Status_Stun_Start`](02-combat-common.md) 공용 | `FX_Faint` | — | (공용) | ❌ |
| `Mob_WallImpact` | 넉백 중 벽 충돌 — **효과 없음**(서버 이벤트만) | 전원 | 3D | 0.3s | 금속 몸이 벽에 "쾅" | — | 팔라딘 Q 로 벽에 밀기 | `small robot slammed into wall, metal crash, 300ms` | ❌ · 훅 없음 |
| `Mob_Return` | 추적 해제·리쉬 귀환(5배속, 풀피) | 전원 | 3D | 0.4s | 포기하고 돌아감 "삐-우" 하강 | — | — | `robot disengaging, descending beep, 400ms` | ❌ |
| `Mob_Death_Explode` | 사망 즉시 폭발(`PlayExplodeRpc`, Reliable) | 전원 | 3D 5~35m | 0.8s · 변형 3 | 작은 로봇 폭발 + 파편 튐 | `FX_Death_Explosion_01` | 몬스터 처치 5회 | `small robot explosion, metal debris scattering, electrical pop, 800ms` | ❌ |
| `Mob_Debris_Settle` | 파편 1.2s 굴러다님 → 0.6s 디졸브 | 전원 | 3D 2~12m | 1.2s | 금속 조각 덜그럭 | 파편 디졸브 | (위) | `small metal debris clattering on floor, 1.2s` | ❌ |
| `Mob_Footstep_Light` / `_Heavy` | 걷기·추격(발 이벤트 없음) | 전원 | 3D 1~10m | 0.08s · 변형 4 · 재생 제한 | 작은 금속 발 / 무거운 금속 발 | — | 걷는 몬스터 10초 | `small robot metal footstep, light clank, 80ms` / `heavy robot footstep, metal thud, 120ms` | ❌ · 훅 없음 |
| `Mob_Tell_Glow` | 공격 예고 발광 시작(`AttackAnticipation`, 선딜 0.35s) — 근접형 공용 | 전원 | 3D 3~20m | 0.35s | 날카로운 충전 "치잉" — 피하라는 신호 | 발광 재질 | 근접 몬스터 공격 직전 | `quick warning charge-up, sharp rising zing, 350ms` | ❌ |

## 3.1 ChompBot — 턱 달린 작은 깨무는 로봇 (근접)

| 키 | 언제 | 듣는 사람 | 2D·3D | 길이·반복 | 느낌 · 레퍼런스 | 짝 VFX | 녹화 | 프롬프트(EN) | 상태 |
|---|---|---|---|---|---|---|---|---|---|
| `ChompBot_Idle_Loop` | 대기·이동 중 | 전원 | 3D 1~8m | 루프 | 턱이 딱딱거리는 작은 모터 | — | ChompBot 대기 10초 | `small robot idle, jaw clicking and tiny servo hum, quiet loop` | ❌ |
| `ChompBot_Bite` | 깨물기 0.17s(`PlayEffect(Bite)`) | 전원 | 3D 3~20m | 0.25s · 변형 3 | 강철 턱 "철컥!" · 덫이 닫히는 소리 | `Fx_Bite` | ChompBot 공격 5회 | `metal robot jaws snapping shut, bear trap clang, 250ms` | ❌ |

## 3.2 HumanoidBot — 도끼 든 인간형 로봇 (근접)

| 키 | 언제 | 듣는 사람 | 2D·3D | 길이·반복 | 느낌 · 레퍼런스 | 짝 VFX | 녹화 | 프롬프트(EN) | 상태 |
|---|---|---|---|---|---|---|---|---|---|
| `HumanoidBot_Idle_Loop` | 대기 중 | 전원 | 3D 1~8m | 루프 | 관절 서보 | — | 대기 10초 | `humanoid robot idle, joint servos whirring softly, loop` | ❌ |
| `HumanoidBot_Swing` | 도끼 내려치기 0.10s(`PlayEffect(Slash)`) | 전원 | 3D 3~20m | 0.3s · 변형 3 | 무거운 도끼 "후웅" | `FX_Mob_Slash_220` | HumanoidBot 공격 5회 | `robot swinging heavy axe, whoosh with servo motor, 300ms` | ❌ |
| `HumanoidBot_AxeHit` | 내려치기가 플레이어에 맞음(판정 후 — 피격음과 공용 가능) | 전원 | 3D | 0.25s | 도끼 쿵 | — | (위) | (`Player_Hurt` 공용) | ❌ |

## 3.3 MortarBot — 박격포 이동형 (원거리, 수직 낙하 포격)

| 키 | 언제 | 듣는 사람 | 2D·3D | 길이·반복 | 느낌 · 레퍼런스 | 짝 VFX | 녹화 | 프롬프트(EN) | 상태 |
|---|---|---|---|---|---|---|---|---|---|
| `MortarBot_Aim_Loop` | 조준 선딜 3.5s(`AttackLoop`) | 전원 | 3D 3~20m | 루프 | 포신이 들리며 장전 "끼익-철컥" 후 대기 험 | — | MortarBot 공격 전체 3회 | `mortar cannon adjusting angle, mechanical creak and loading clunk, then low hum loop` | ❌ |
| `MortarBot_Fire` | 발사 확정 `OnAttackCommit@0.9` → 발사 — **발사 VFX 없음, 발사음 핵심 자리** | 전원 | 3D 5~40m | 0.5s | 박격포 "퐁!" (높고 짧게) | — | (위) | `mortar shell launch, hollow thump pop, 500ms` | ❌ · VFX 없음 → 클립 이벤트 필요 |
| `MortarBot_Telegraph` | 대상 발밑 예고 원 1.5s(`SkyStrikeShowRpc`) | 전원 | 3D 2~15m | 1.5s | 떨어지는 휘파람 — 점점 높아짐 · 2차대전 포탄 낙하음 | 원형 데칼 | (위) | `incoming artillery shell whistle, descending pitch, 1.5 seconds` | ❌ |
| `MortarBot_Impact` | 착탄 폭발(반경 1.5m) | 전원 | 3D 5~40m | 0.7s · 변형 2 | 중간 크기 폭발 | `FX_Mob_Bomb_Explode` | (위) | `mortar shell explosion, medium blast with debris, 700ms` | ❌ |

## 3.4 PeekABot — 3단 신축 기둥 고정 포탑 (원거리)

| 키 | 언제 | 듣는 사람 | 2D·3D | 길이·반복 | 느낌 · 레퍼런스 | 짝 VFX | 녹화 | 프롬프트(EN) | 상태 |
|---|---|---|---|---|---|---|---|---|---|
| `PeekABot_Rise` | 인지 시 기둥이 올라옴(추정 — 상태 HeadIdle→Idle) | 전원 | 3D 3~20m | 0.6s | 신축 기둥 "슈욱-철컥" | — | 포탑 앞으로 다가가기 | `telescopic turret extending up, pneumatic hiss and lock clunk, 600ms` | ❌ (추정 지점) |
| `PeekABot_Turn_Loop` | 머리 회전(초당 360°) | 전원 | 3D 1~10m | 루프 · 회전 중만 | 작은 회전 서보 | — | (위) | `turret head rotating servo whine, loop` | ❌ |
| `PeekABot_Lock_Track` | 레이저 추적(초록→주황) 0.7s | 전원 | 3D 2~15m | 0.7s | 조준 "삐-삐-" 빨라짐 | `TrackingLaser` | (위) | `targeting laser tracking beeps, accelerating, 700ms` | ❌ |
| `PeekABot_Lock_Fixed` | 빨강 고정 0.5s — **피하라는 신호** | 전원 | 3D 2~15m | 0.5s | 연속음 "삐이이-" | `TrackingLaser`(빨강) | (위) | `target locked continuous tone, high pitched, 500ms` | ❌ |
| `PeekABot_Fire` | 발사(`PlayEffect(Muzzle)@0.41`) | 전원 | 3D 5~35m | 0.3s · 변형 2 | 에너지탄 "퓽" | `FX_Mob_Muzzle` | (위) | `turret energy bolt shot, punchy zap, 300ms` | ❌ |
| `PeekABot_Projectile_Loop` | 투사체 비행(최대 4s, 12m/s) | 전원 | 3D 1~8m | 루프 | 지나가는 "윙-"(도플러) | `FX_Mob_Bomb_Trail` | (위) | `energy projectile flyby hum, short loop` | ❌ |
| `PeekABot_Projectile_Impact` | 투사체 소멸(명중·벽·수명, Despawn) | 전원 | 3D 3~25m | 0.4s | 작은 폭발 | `FX_Mob_Bomb_Explode` | (위) | `small energy projectile explosion, 400ms` | ❌ |

## 부록 — 프리팹만 있고 현재 맵에 안 나오는 적 (이번 명세 제외)
TeslaBot(꺼진 Stage1) · WallBot · SpinnerBot_Tutorial(Legacy) · TrainingSparringBot(로비 훈련 — 필요 시 HumanoidBot 소리 재사용) · TrainingDummy_Boss · Boss/Bomb·FireFloor(웰즈 폭탄 제거됨) · Stone_Golem 등 TestAssets.

## 대조
- 4종 × (스폰·기상·인지·이동·예고·공격·피격·넉백·벽·복귀·사망) → 공용 10행 + 고유 16행. 기상·인지·벽 충돌·발소리·MortarBot 발사는 **화면 신호가 없어 코드/클립 이벤트 추가 필요** 표기.
