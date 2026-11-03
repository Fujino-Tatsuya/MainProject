# 04. 중간보스 — SpinnerBot(존3) · GauntletBot(존4)

> 공통 열 뜻은 [README.md §2](README.md#2-표-읽는-법). 전부 **전원 · 3D**(감쇠 5~40m — 일반 몬스터보다 크게). 간파 성공 공용 층은 [02 §2.1](02-combat-common.md#21-간파-전-캐릭터-공용-층).
> 일반 몬스터 공용(기상·피격·기절·사망 폭발 등)은 [03 §3.0](03-monsters.md#30-일반-몬스터-공용-4종이-같이-씀) 을 쓰되, 사망은 엘리트용(`FX_Death_Explosion_02`) 큰 폭발.
> 레퍼런스 톤: 거대한 공장 기계 — Furi 보스, Hades 엘리트 등장감.

## 4.0 중간보스 공용

| 키 | 언제 | 듣는 사람 | 2D·3D | 길이·반복 | 느낌 · 레퍼런스 | 짝 VFX | 녹화 | 프롬프트(EN) | 상태 |
|---|---|---|---|---|---|---|---|---|---|
| `MidBoss_Wake` | 기상(존3 시작 / 존4 다리 개통, `SetZoneDormantServer`) | 전원 | 3D 10~50m | 1.5s | 큰 기계가 깨어남 — 엔진 점화 + 저음 | — | 존3 진입·다리 개통 장면 | `large war machine powering up, heavy engine ignition, deep mechanical roar, 1.5s` | ❌ · 훅 없음 |
| `MidBoss_Groggy` | 간파당함 그로기 1s(Dizzy / 자세 정지) | 전원 | 3D 5~30m | 1.0s | 기계가 휘청 — 모터 꺼졌다 켜짐 | `FX_Faint` | 간파 성공 장면 | `large robot stunned, motors sputtering and sparking, 1s` | ❌ |
| `MidBoss_Vulnerable_Loop` | 취약 8s(하늘색 틴트) | 전원 | 3D 2~15m | 루프 8s | 지직거리는 불안정 험 | 틴트 | (위) | `damaged machine unstable electric buzz, seamless loop` | ❌ |
| `MidBoss_Vulnerable_End` | 취약 끝 | 전원 | 3D | 0.5s | 재가동 | — | — | `machine systems restored, power surge, 500ms` | ❌ |
| `MidBoss_Hurt` | 피격(평소엔 경직 없음) | 전원 | 3D | 0.2s · 변형 4 | 두꺼운 장갑 "텅" | `FX_Hit_dark_magic` | 중간보스 타격 | `hitting thick metal armor plating, dull heavy clang, 200ms` | ❌ |
| `MidBoss_Death_Explode` | 사망 폭발 | 전원 | 3D 10~60m | 2.0s | 큰 폭발 + 연쇄 소폭발 + 파편 | `FX_Death_Explosion_02` | 중간보스 처치 | `large robot destruction, chain explosions, heavy debris, 2 seconds` | ❌ |

## 4.1 SpinnerBot — 회전 칼날 팔의 팽이형 로봇 (HP 260)

| 키 | 언제 | 듣는 사람 | 2D·3D | 길이·반복 | 느낌 · 레퍼런스 | 짝 VFX | 녹화 | 프롬프트(EN) | 상태 |
|---|---|---|---|---|---|---|---|---|---|
| `Spinner_Move_Loop` | 이동 중 | 전원 | 3D 2~15m | 루프 | 회전 팽이 베이스 험 | — | SpinnerBot 이동 10초 | `spinning top robot moving, rotating base hum, loop` | ❌ |
| `Spinner_Whip` | 채찍 L/R 0.14s(`PlayEffect(Whip_L/R)`) | 전원 | 3D 3~25m | 0.3s · 변형 2(L/R) | 칼날 팔 휘두름 "휘익-쇅" | `FX_Mob_Slash_180` | 채찍 공격 5회 | `spinning blade arm whip slash, sharp metallic whoosh, 300ms` | ❌ |
| `Spinner_Spin_Windup` | 스핀 준비 1.5s(간파 창, 파랑 띠 차오름) | 전원 | 3D 5~30m | 1.5s | 점점 빨라지는 회전 — 간파 타이밍 신호 | 파랑 띠 데칼 | 스핀 공격 3회 | `blades spinning up faster and faster, rising mechanical whine, 1.5 seconds` | ❌ |
| `Spinner_Spin_Loop` | 스핀 돌진 1.2s(×15 이동, 0.5s 반복 타격) | 전원 | 3D 5~35m | 루프 | 칼날 회오리 "위이이잉" · 체인톱 | `FX_Mob_Spin` 루프 | (위) | `buzzsaw whirlwind dash, high speed spinning blades, loop` | ❌ |
| `Spinner_Spin_Hit` | 스핀이 플레이어 타격 | 전원 | 3D | 0.15s · 재생 제한 | 칼날 긁힘 | — | (위) | `spinning blade grinding hit, metal scrape, 150ms` | ❌ |
| `Spinner_Spin_End` | 스핀 종료 | 전원 | 3D | 0.6s | 회전이 감속 | — | (위) | `spinning blades winding down, descending whine, 600ms` | ❌ |

## 4.2 GauntletBot — 거대 건틀릿 권투형 로봇 (HP 300)

| 키 | 언제 | 듣는 사람 | 2D·3D | 길이·반복 | 느낌 · 레퍼런스 | 짝 VFX | 녹화 | 프롬프트(EN) | 상태 |
|---|---|---|---|---|---|---|---|---|---|
| `Gauntlet_Footstep` | 걷기(발 이벤트 없음) | 전원 | 3D 2~20m | 0.15s · 변형 3 | 무거운 "쿵" | — | 이동 10초 | `heavy robot footstep, ground thud, 150ms` | ❌ · 훅 없음 |
| `Gauntlet_Punch_Wind` | 펀치 01·02·03 × 좌우 감김(`StartEffect(Wind_L/R)` 0.16~0.31s) | 전원 | 3D 3~25m | 0.35s · 변형 3 | 거대 주먹이 공기를 가름 "부웅" | `FX_Punch_Wind` | 펀치 각 종류 3회 | `huge mechanical fist punch, heavy air whoosh with hydraulic hiss, 350ms` | ❌ |
| `Gauntlet_Punch_Hit` | 펀치 적중(`PlaySparkRpc`) | 전원 | 3D 5~30m | 0.3s · 변형 3 | 강철 주먹 "콰직" | `FX_Punch_Hit_Spark` | 펀치에 맞기 | `heavy metal fist impact, crunching punch with sparks, 300ms` | ❌ |
| `Gauntlet_Smash_Warn` | 스매시 예고 2.0s(간파 창, 바닥 경고 반경 3m) | 전원 | 3D 5~30m | 2.0s | 양주먹을 들어 올리며 모터가 차오름 | `FX_Mob_Smah_Warning` | 스매시 3회 | `giant robot raising fists, hydraulic pressure building, rising tension, 2 seconds` | ❌ |
| `Gauntlet_Smash` | 내려찍기 0.15s(`PlayEffect(Dust)`) | 전원 | 3D 10~50m | 1.0s | 바닥을 부수는 "쾅!!" + 먼지 | `FX_Mob_Smash_Dust` | (위) | `massive ground smash, concrete cracking boom with debris and dust, 1s` | ❌ |
| `Gauntlet_Defeat` | `Defeat` 클립 약 2s → 폭발(4.0 `MidBoss_Death_Explode`) | 전원 | 3D 5~40m | 2.0s | 무너지며 모터가 꺼짐 | — | 처치 장면 | `giant robot collapsing, motors failing, heavy metal crash, 2 seconds` | ❌ |

## 대조
- SpinnerBot: 인지·채찍·스핀 준비·돌진·종료·간파당함·취약·피격·사망 → 공용 6 + 고유 6.
- GauntletBot: 기상·펀치(6종 공용 1키+변형)·적중·스매시 예고·스매시·간파당함·취약·피격·사망 → 공용 6 + 고유 6.
