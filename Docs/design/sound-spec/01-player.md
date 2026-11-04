# 01. 플레이어 — 팔라딘 · 어쌔신 · 거너

> 공통 열 뜻은 [README.md §2](README.md#2-표-읽는-법). 전투음은 기본 **3D · 전원**(감쇠 3~25m — 탑다운 카메라 높이 기준 제안값), 다르면 행에 적는다.
> "듣는 사람" 근거: 스킬은 서버 승인 후 `PlaySkillClientRpc` 로 전 피어가 같은 클립을 틀고 각자 애니 이벤트가 난다 → 소리도 각 피어 로컬 재생이면 된다(새 RPC 불필요).
> **소리를 붙이기 가장 쉬운 곳 = 이미 있는 VFX 엔트리의 `EffectPart.sound` 칸**(VFX 가 재생되는 피어에서 같이 울림, `EffectManager.cs:612`). 아래 "짝 VFX" 열이 그 자리다. 루프 VFX 에 넣으면 시작할 때 1회만 울린다 → 루프음은 별도 키.
> 조사 근거: 2026-10-30 코드·애니 전수. 길이는 클립 초(재생 속도 반영 전) 또는 데이터 값.

## 녹화 공통 지시 (영상→효과음용)
- 연습장(로비 훈련 또는 빈 존)에서 **캐릭터 1명, 동작 1종만**, 같은 동작 **3회 반복**, 앞뒤 1초 여유. 게임 소리 **끔**.
- 탑다운 기본 카메라, 1080p 60fps, 클립당 5~10초. 적중음이 필요하면 허수아비/몬스터 1마리를 세워 둔다.
- 결과물에서 1타씩 잘라 쓴다. 같은 키에 변형 3개를 원하면 3회 반복분을 각각 자른다.

---

## 1.1 팔라딘 (Player_Paladin) — 무거운 대검 + 방패, 성광(금·흰) 테마

> 레퍼런스 톤: 묵직한 금속, 방패 쿵, 성스러운 공명. Ravenswatch 베오울프 + Diablo IV 성기사.
> SVN r473 클립: `Player_Character_A/Attack/floraphonic-swing-whoosh-6·7`(휘두름) · `Q_Skill/Action-game_shield_c_*`·`SFX_Q_Skill`(방패) · `Passive/Sword_impact_1`.

| 키 | 언제 | 듣는 사람 | 2D·3D | 길이·반복 | 느낌 · 레퍼런스 | 짝 VFX | 녹화 | 프롬프트(EN) | 상태 |
|---|---|---|---|---|---|---|---|---|---|
| `Paladin_Atk1_Swing` | 평타 1타 휘두름(`PlayDefaultAttackClientRpc`, 휘두름 이벤트 **없음** — 클립에 이벤트 추가 필요) | 전원 | 3D | 0.3s · 변형 3 | 대검 가로베기 "후웅" | — | 평타 1타만 3회 | `heavy greatsword horizontal swing whoosh, armored knight, 300ms` | ⚠️ SVN `swing-whoosh-6` |
| `Paladin_Atk2_Swing` | 2타 | 전원 | 3D | 0.3s · 변형 3 | 역방향 베기, 1타보다 살짝 높게 | — | 1→2타 | `heavy greatsword backhand swing whoosh, 300ms` | ⚠️ SVN `swing-whoosh-7` |
| `Paladin_Atk3_Swing` | 3타 | 전원 | 3D | 0.3s · 변형 3 | 내려찍기 준비 | — | 1→3타 | `heavy overhead greatsword swing, rising whoosh, 300ms` | ❌ |
| `Paladin_Atk4_Swing` | 4타(피해 ×1.2, 마무리) | 전원 | 3D | 0.4s | 가장 크게, 갑옷 덜컥 포함 | — | 1→4타 전체 | `powerful finishing greatsword slam swing, armor rattle, 400ms` | ❌ |
| `Paladin_Atk_Hit` | 평타 적중(1~3타, Hit 이벤트 → `PlaySlashVfxRpc`) | 전원 | 3D | 0.25s · 변형 4 | 금속이 로봇 장갑을 가름 "깡-" | `SingleSlash_Hit_01~03` | 몬스터 상대 1~3타 | `greatsword hitting metal robot armor, heavy clang with impact, 250ms` | ⚠️ SVN `Hit/` 1개(공용) |
| `Paladin_Atk4_Hit` | 4타 적중 | 전원 | 3D | 0.35s | 위 + 저음 쿵 | `DoubleSlash_Hit` | 4타 적중 | `heavy sword finishing blow on metal, deep boom and clang, 350ms` | ❌ |
| `Paladin_Atk_Miss` | 평타 헛스윙(1~4) | 전원 | 3D | — | 휘두름 키로 충분 → **무음 권장** | `*_Miss` | — | — | — |
| `Paladin_RMB_Charge` | 우클릭 간파 기 모으기 0~0.333s(`ShieldCharge`) | 전원 | 3D | 0.33s | 방패에 빛이 모이는 짧은 흡입 | `ShieldCharge`(B2, 현재 꺼짐) | 우클릭 3회(적 공격 없이) | `holy energy gathering into shield, short reverse swell, 330ms` | ❌ |
| `Paladin_RMB_Swing` | 간파 판정 순간 0.333s(Hit) | 전원 | 3D | 0.25s | 방패 밀쳐내기 "훅" | — | 위와 같음 | `shield bash thrust whoosh, heavy, 250ms` | ❌ |
| `Paladin_RMB_Success` | 간파 성공(`ServerRelaySuccess` → `InterruptWaveRpc`) | 전원 | 3D 5~35m | 0.6s | 방패가 공격을 튕겨냄 + 성스러운 충격파 · **간파 공용음 위에 얹는 캐릭터 레이어** | `ShieldWave`(B3, 꺼짐) | 적 공격에 간파 성공 3회 | `shield parry impact, metallic deflect clang with holy shockwave burst, 600ms` | ⚠️ SVN `Q_Skill/shield_c_*` 후보 |
| `Paladin_Q_Start` | Q 진격의 방패 시작(`FirstMeleeMainSkill.OnClientPlay`) | 전원 | 3D | 0.4s | 방패 들고 돌진 시작 "쿵-" | `Smash` | Q 홀드 3초 2회 | `armored knight raising shield and charging forward, heavy step and metal, 400ms` | ⚠️ SVN `Q_Skill/SFX_Q_Skill` |
| `Paladin_Q_Loop` | Q 유지(최대 3초, 6m/s 전진) | 전원 | 3D | 루프 0.667s 주기 | 방패로 밀어붙이는 무거운 발걸음 + 마찰 | `Smash`·`Trail` 루프 | 위와 같음 | `heavy shield charge loop, armored footsteps and grinding metal, seamless loop` | ❌ |
| `Paladin_Q_Tick` | 0.25초 틱 적중(서버만 — **연출 없음**, 피격 측 소리로 대체) | 전원 | 3D | 0.15s | 방패 부딪힘 "쿵" | — | 몬스터 밀며 Q | `shield bashing into metal robot, short heavy thud, 150ms` | ❌ · 틱 연출 RPC 없음 |
| `Paladin_Q_End` | 놓음·3초 만료(`OnReleased`/`OnEnd`) | 전원 | 3D | 0.3s | 방패 내림, 숨 고르기 | — | (위 클립 끝) | `shield lowering, armor settle, 300ms` | ❌ |
| `Paladin_E_Ignite` | E 점화 0s(`GuardianIgnite`) | 전원 | 3D | 0.3s | 방패 문양 점등 "징-" | GuardianWill/Ignite | E 탭(공격 안 받음) 3회 | `holy sigil igniting on shield, bright resonant ring, 300ms` | ❌ |
| `Paladin_E_Barrier_Loop` | 방어 창 0.225s~ 유지(최대 3초) | 전원 | 3D 2~15m | 루프 | 빛 결계 험 | `Barrier` 루프 | E 홀드 3초 | `holy energy barrier hum, warm shimmering drone, seamless loop` | ❌ |
| `Paladin_E_Release` | 해제(아무것도 못 막음, Custom1) | 전원 | 3D | 0.3s | 결계 꺼짐 | `Release` | E 탭 후 자연 해제 | `holy barrier dissipating, soft descending shimmer, 300ms` | ❌ |
| `Paladin_E_Block` | 방어 성공 — 공격을 막음(`ServerTryAbsorbAttack`) | 전원 | 3D 5~30m | 0.5s | 크게 막아내는 "카앙!" + 충전 시작 | `GuardianBlock` | 적 공격을 E 로 막기 3회 | `heavy shield block, loud metallic impact with holy resonance, 500ms` | ❌ |
| `Paladin_E_Charge_Loop` | 막은 뒤 반격 전 Aura·Charge(0~0.433s) | 전원 | 3D | 0.43s(짧은 루프) | 힘을 모음 | `GuardianCharge`·`Aura` | (위 클립) | `power charging, rising holy energy, 450ms` | ❌ |
| `Paladin_E_Counter` | 반격 적중 0.433s(`GuardianCounter`) | 전원 | 3D 5~35m | 0.6s | 반격 일격 + 빛 폭발 | `GuardianCounter` | (위 클립) | `counterattack sword strike with holy explosion, powerful, 600ms` | ❌ |
| `Paladin_R_Aim_Loop` | R 조준 중(`OwnerPlayUltimateVfx`) | 전원 | 3D | 루프 | 칼에 전기가 감김 "지지직" | `SwordLightening`·`SwordElectric` | R 조준만 4초 | `crackling electricity charging on a sword, seamless loop` | ❌ |
| `Paladin_R_Cast` | 대상 확정·바닥 표식(`PlayTargetFloorRpc`) | 전원 | 3D 5~40m | 0.8s | 하늘이 열리는 듯한 상승 | `FX_Target_Floor` | R 대상 지정 전체 2회 | `divine judgement marker appears, ominous rising choir-like synth, 800ms` | ❌ |
| `Paladin_R_Strike` | 마무리 낙뢰 ~0.97s(`PlayTargetStrikeRpc`) | 전원 | 3D 10~50m | 1.2s | **궁극기 핵심** — 거대한 낙뢰 + 지면 폭발 | `FX_Lightening_Strike`·`SwordStick` | (위 클립) | `massive lightning strike from the sky, thunder crack and ground explosion, 1.2s` | ❌ |
| `Paladin_R_Cancel` | 대상 소실 취소 | 전원 | 3D | 0.3s | 전기가 꺼짐 | — | — | `electric charge fizzling out, 300ms` | ❌ |
| `Paladin_Passive_Charged` | 패시브 충전 — 칼날 발광 켜짐(`PassiveCharge` 부여) | 전원 | 3D 2~10m | 0.4s | 칼에 빛이 들어옴 "칭-" | `bladeGlow` | 패시브 충전 순간 | `sword blade glowing with holy light, bright metallic ring, 400ms` | ❌ |
| `Paladin_Passive_Proc` | 발동 — 추가 피해 + 회복(`ProcVfxRpc`) | 전원 | 3D 5~30m | 0.7s | 성광 낙인 + 회복 흡수 | `FX_Paladin_Smite` + `Heal` | 충전 상태로 몬스터 타격 | `holy smite impact on robot with electric short circuit sparks, then soft healing chime, 700ms` | ⚠️ SVN `Passive/Sword_impact_1` |
| `Paladin_Dash` | 대시(잔상 `FX_PaladinDashAfterimage`) | 전원 | 3D | 0.25s · 변형 2 | 무거운 갑옷 대시 | `FX_PaladinDashAfterimage` | 대시 3회 | `heavy armored dash, quick whoosh with armor clank, 250ms` | ⚠️ SVN `Dash/` 2개(공용) |
| `Paladin_Footstep` | 걷기 `Garen_Walk` 0.5s 루프(발 이벤트 **없음**) | 전원 | 3D 1~12m | 0.1s · 변형 4 | 무거운 철갑 부츠 | — | 걷기 10초 | `heavy armored boot footstep on concrete, metal plate clank, 100ms` | ⚠️ SVN `Footstep/` 공용 · 이벤트 추가 필요 |

## 1.2 어쌔신 (Player_Assassin) — 쌍단검, 변신(분노) 시 칼날 폭풍

> 레퍼런스 톤: 날카롭고 빠름, 가벼운 금속, 변신 시 공기를 찢는 바람. Hades 쌍검 + Ravenswatch 피리 부는 사나이 속도감.
> 평타 3모드(일반 4단 / 강타 / 변신 묶음 4타). 베기 VFX 가 애니 이벤트(`PlayEffect`)라 VFX 칸에 넣으면 프레임이 정확히 맞는다.

| 키 | 언제 | 듣는 사람 | 2D·3D | 길이·반복 | 느낌 · 레퍼런스 | 짝 VFX | 녹화 | 프롬프트(EN) | 상태 |
|---|---|---|---|---|---|---|---|---|---|
| `Assassin_Atk1_Slash` ~ `Atk4_Slash` | 일반 1~4타 베기(0.133/0.133/0.117/0.117s) | 전원 | 3D | 0.15s · 각 변형 2 | 단검 "쉭" — 4타가 가장 길게 | `Assassin_NormalSlash_1~4` | 일반 4단 콤보 3회 | `fast dagger slash whoosh, sharp and light, 150ms`(4타: `double dagger finishing slash, 250ms`) | ❌ |
| `Assassin_Atk_Hit` | 일반 적중(`NormalSlashHitRpc`) | 전원 | 3D | 0.15s · 변형 4 | 얇은 칼이 장갑을 긁음 "칭" | `NormalSlash_Success_1~4` | 몬스터 상대 4단 | `dagger hitting metal armor, sharp scrape and ting, 150ms` | ❌ |
| `Assassin_BackAttack` | 백어택 적중(`BackAttackHitRpc`) | 전원 | 3D 3~30m | 0.35s | 치명타 "찰칵-쾅" — 일반 적중보다 확실히 크게 | `backAttackHitPrefab` | 몬스터 뒤에서 타격 | `critical backstab hit, piercing metal crunch with sharp accent, 350ms` | ❌ |
| `Assassin_Heavy_Slash` | 강타(일반 E 뒤 1회, `Assassin_EnhancedSlash` ~0.11s) | 전원 | 3D | 0.3s | 힘 실린 큰 베기 | `Assassin_EnhancedSlash` | E → 강타 | `empowered heavy dagger slash, deep whoosh with energy, 300ms` | ❌ |
| `Assassin_Heavy_Hit` | 강타 적중(피해 ×2.5) | 전원 | 3D 5~30m | 0.4s | 육중한 관통 | — | 위 + 몬스터 | `heavy dagger strike piercing metal, impact boom, 400ms` | ❌ |
| `Assassin_Trans_Slash` | 변신 묶음 4타 Hit 4회(약 0.1s 간격) | 전원 | 3D | 0.08s · 변형 4 · 재생 제한 | 빠른 연속 "샤샤샥" | `Assassin_TransformedSlash` 루프 | 변신 중 평타 | `rapid flurry dagger slashes, very fast, 80ms each` | ❌ |
| `Assassin_RMB_Windup` | 간파 기 모으기 0s | 전원 | 3D | 0.22s | 칼을 거꾸로 쥐는 금속음 | `Assassin_InsightWindup`(C1, 꺼짐) | 우클릭 3회 | `dagger flip and grip, quick metal shing, 220ms` | ❌ |
| `Assassin_RMB_Slash` | 튀기기·올려치기 0.217~0.267s(판정) | 전원 | 3D | 0.25s | 위로 올려베기 | `Assassin_InsightPop`·`InsightSlash` | 위와 같음 | `upward dagger slash, rising whoosh, 250ms` | ❌ |
| `Assassin_RMB_Success` | 간파 성공(`InterruptSucceededRpc`) | 전원 | 3D 5~35m | 0.5s | 공격을 흘려 베어냄 "치잉-" · 간파 공용음 위 레이어 | `Slash_Success` | 적 공격에 간파 성공 | `dagger parry deflect, sharp metallic ring with slicing follow-through, 500ms` | ❌ |
| `Assassin_Q_Crouch` | Q 준비(웅크림) 0s | 전원 | 3D | 0.1s | 순간 정적, 짧은 흡입 | (변신: `Assassin_Q_StormSlash` 루프) | Q 3회(일반) + 3회(변신) | `short inhale tension tick before dash, 100ms` | ❌ |
| `Assassin_Q_Dash` | 돌진 출발 0.1s(5m 를 60m/s) | 전원 | 3D 3~25m | 0.2s | 순간이동급 "슝!" | — | 위와 같음 | `ultra fast piercing dash, sharp air tear whoosh, 200ms` | ❌ |
| `Assassin_Q_Pierce` | 종료 — 관통선·폭발(일반) | 전원 | 3D 5~30m | 0.4s | 지나간 자리 베임 폭발 | `Assassin_Q_PierceLine`·`PierceBurst` | 위와 같음 | `delayed slash line burst after dash, sharp explosion, 400ms` | ❌ |
| `Assassin_Q_Storm_Loop` | 변신 Q 칼날 폭풍 유지 | 전원 | 3D | 루프 | 회전하는 칼날 바람 | `Assassin_Q_StormSlash` | (위 클립) | `spinning blade storm whirlwind, metallic wind loop` | ❌ |
| `Assassin_E_Enhance` | 일반 E 강화 획득(0.5s) | 전원 | 3D | 0.4s | 칼에 힘이 맺힘 | `Assassin_E_Charge` | E 3회 | `daggers charging with energy, sharp rising shing, 400ms` | ❌ |
| `Assassin_E_Aura_Loop` | 강화 유지(강타 쓸 때까지) | 전원 | 3D 1~8m | 루프 | 작게 떨리는 칼날 험 | `Assassin_E_Aura` | E 후 5초 대기 | `subtle energized blade hum, quiet seamless loop` | ❌ |
| `Assassin_E_Circle_Start` | 변신 E 원형 난격 시작(무적) | 전원 | 3D 5~30m | 0.3s | 뛰어드는 소리 | `circleStrikePrefab` | 변신 E 3회 | `assassin leaping into circular strike, whoosh, 300ms` | ❌ |
| `Assassin_E_Circle_Hit` | 난격 Hit 5회(0.22~0.90s, 0.17s 간격) | 전원 | 3D | 0.12s · 변형 5 | 사방에서 베는 소리 | `Assassin_E_MultiSlashes` 루프 | 위와 같음 | `multiple rapid blade slashes around an area, 120ms each` | ❌ |
| `Assassin_R_Transform` | 변신 시작(`TransformChanged(true)`) | 전원 | 3D 10~40m | 1.0s | **변신 핵심** — 칼날 소용돌이가 일어남 · 분노 폭발 | `Assassin_R_BladeVortex` | R 변신 2회 | `transformation burst, swirling blade vortex rising with aggressive energy roar, 1s` | ❌ |
| `Assassin_R_Vortex_Loop` | 변신 유지 | 전원 | 3D 1~10m | 루프 | 몸 주변 칼날 회전 바람(작게) | 위 루프 | 변신 상태 10초 | `quiet spinning blades whoosh around body, seamless loop` | ❌ |
| `Assassin_R_Revert` | 변신 종료(만료·해제·쓰러짐) | 전원 | 3D | 0.5s | 소용돌이가 흩어짐 | (Vortex 해제 0.5s) | 변신 끝까지 대기 | `blade vortex dispersing, descending wind, 500ms` | ❌ |
| `Assassin_Dash` | 대시 `Dodge`(0.25s) | 전원 | 3D | 0.2s · 변형 2 | 가벼운 구르기 + 천 소리 | `FX_AssassinDashAfterimage` | 대시 3회 | `light agile dodge roll, cloth swish, 200ms` | ⚠️ SVN `Dash/` 공용 |
| `Assassin_Footstep` | `Run_Loop` 0.583s 주기(발 이벤트 **없음**) | 전원 | 3D 1~10m | 0.08s · 변형 4 | 가벼운 부츠 | — | 달리기 10초 | `light quick boot footstep on concrete, soft, 80ms` | ⚠️ SVN `Footstep/` 공용 · 이벤트 추가 필요 |

## 1.3 거너 (Player_Gunner) — 레이저 소총, 과열·냉각 기믹

> 레퍼런스 톤: 에너지 무기, 날카로운 레이저 + 냉각 증기. Destiny 펄스 라이플 + Returnal 무기음.
> 평타는 0.35s 간격 연사 → **재생 제한 필수**, 과열 단계(25/50/75/100)에 따라 음정을 올리면 좋다(변형 4개로 대체 가능).

| 키 | 언제 | 듣는 사람 | 2D·3D | 길이·반복 | 느낌 · 레퍼런스 | 짝 VFX | 녹화 | 프롬프트(EN) | 상태 |
|---|---|---|---|---|---|---|---|---|---|
| `Gunner_Atk_Ready` | 평타 준비 동작 0.25s(`StartRpc`) | 전원 | 3D | 0.25s | 총을 겨누는 "철컥-윙" | — | 평타 10발(과열까지) 2회 | `energy rifle raise and power-up, click and whine, 250ms` | ❌ |
| `Gunner_Atk_Fire` | 매 발(`ShotRpc`, 0.35s 간격) | 전원 | 3D 3~30m | 0.2s · 변형 4 | 짧고 날카로운 레이저 "피웅" | `FX_GunnerBeam` | (위 클립) | `sci-fi laser rifle shot, sharp zap, short, 200ms` | ❌ |
| `Gunner_Atk_Impact` | 착탄(벽·유닛 공통, 각 피어 로컬) | 전원 | 3D | 0.15s · 변형 3 | 지지직 탄착 | `FX_LaserHit` | 벽·몬스터에 사격 | `laser impact sizzle on metal, short burn hiss, 150ms` | ❌ |
| `Gunner_Heat_Stage` | 과열 단계 상승 25/50/75(`StageChanged`, 각 피어 로컬) | 오너 | 2D | 0.15s | 경고 비프 단계별로 높게 | — | — | `weapon heat warning beep, rising pitch per stage, 150ms` | ❌ · VFX 없음 |
| `Gunner_Overheat` | 과열 진입 100(`OverheatChanged(true)`) | 전원 | 3D 3~20m | 0.8s | 증기 분출 + 경보 · **핵심** | — | 10발 연사로 과열 | `weapon overheating, steam burst with alarm beep, 800ms` | ❌ · VFX 없음 |
| `Gunner_Overheat_Loop` | 과열 중(초당 40 냉각) | 전원 | 3D 1~8m | 루프 | 지글거리는 냉각 | — | 과열 후 대기 | `hot metal sizzling and cooling, seamless loop` | ❌ |
| `Gunner_Cooled` | 과열 해제 | 오너 | 2D | 0.2s | 냉각 완료 "핑" | — | — | `weapon cooled ready ping, 200ms` | ❌ |
| `Gunner_Q_Charge_Loop` | Q 집중 0~1.2s(`FX_GunnerCharge`) | 전원 | 3D | 1.2s 상승 → 루프 | 에너지 충전 상승음 | `FX_GunnerCharge` | Q 최대 충전 후 발사 3회 | `energy weapon charging up, rising whine, 1.2s then sustained` | ❌ |
| `Gunner_Q_Full` | 최대 충전 1.2s | 전원 | 3D | 0.3s | "띵!" 완충 신호 | `FX_GunnerChargeFull` | (위 클립) | `fully charged ping, bright electric ding, 300ms` | ❌ |
| `Gunner_Q_Overcharge_Loop` | 완충 대기(자동 발사 2.0s 까지) | 전원 | 3D | 루프 | 불안정하게 떨림 | `FX_GunnerOvercharge` | (위 클립) | `unstable overcharged energy crackle, seamless loop` | ❌ |
| `Gunner_Q_Fire` | 발사(`ChargeLaserFiredRpc`) | 전원 | 3D 10~50m | 1.0s | **핵심** — 굵은 관통 레이저 "뷔이잉-쾅" | `FX_GunnerQBeam`·`QVent` | (위 클립) | `massive charged laser beam blast, deep powerful zap with bass boom, 1s` | ❌ |
| `Gunner_Q_Hit` | 맞은 적마다(최대 8) | 전원 | 3D | 0.2s · 재생 제한 | 관통 탄착 | `FX_GunnerQHit` | 몬스터 여럿 일렬 | `heavy laser piercing through metal, burn impact, 200ms` | ❌ |
| `Gunner_Q_AllyShield` | 아군 보호막 부여 | 받은 아군 근처 전원 | 3D | 0.4s | 보호막 켜짐 | `AllyShield` | 아군 옆에서 Q | (공용 `Shield_Gain` — [02](02-combat-common.md)) | ❌ |
| `Gunner_E_Backstep` | E 냉각 백스텝(과열 0) | 전원 | 3D 3~20m | 0.5s | 냉기 분사 "쉬익" + 뒤로 점프 | `FX_GunnerFrostBlast`·`CoolVent` | E 3회(과열 상태에서) | `cryo blast backstep, cold gas burst hiss with quick jump, 500ms` | ❌ |
| `Gunner_RMB_Swing` | 근접 간파 판정 0.2s(반동 1.5m) | 전원 | 3D | 0.2s | 총 개머리 밀치기 | — | 우클릭 3회 | `rifle butt bash, quick heavy thump, 200ms` | ❌ |
| `Gunner_RMB_Blast` | 판정 직후 폭발(성공·실패 공통 `InterruptBlastRpc`) | 전원 | 3D 5~30m | 0.4s | 근거리 에너지 폭발 | `InterruptFx`(`FX_Arrow_hail`) | (위 클립) | `close range energy blast, punchy electric burst, 400ms` | ❌ |
| `Gunner_RMB_Success` | 간파 성공 레이어 | 전원 | 3D | 0.4s | 튕겨낸 금속음 | — | 적 공격에 성공 | `metallic deflect clang with energy crackle, 400ms` | ❌ |
| `Gunner_R_Cast` | R 시전(`FX_GunnerUltCast`) | 전원 | 3D 5~40m | 0.6s | 위성 호출 신호 | `FX_GunnerUltCast` | R 2회(몬스터 다수) | `orbital laser call-in, radio beep and charging hum, 600ms` | ❌ |
| `Gunner_R_Zone_Loop` | 추적 레이저 존재 6s(`FX_GunnerUlt_Zone`) | 전원 | 3D 3~30m | 루프 6s | 하늘에서 내려오는 레이저 험 | `FX_GunnerUlt_Zone` | (위 클립) | `continuous orbital laser beam hum, burning sizzle, seamless loop` | ❌ |
| `Gunner_R_Lock` | 대상 고정(대상마다) | 전원 | 3D | 0.2s | 록온 "삐빅" | `FX_GunnerUlt_Lock` | (위 클립) | `target lock-on beep, 200ms` | ❌ |
| `Gunner_R_Strike` | 0.5s 틱 피해(`StrikeRpc`) | 전원 | 3D | 0.25s · 재생 제한 | 레이저 지짐 | `FX_GunnerUlt_Strike` | (위 클립) | `laser burn tick on metal, 250ms` | ❌ |
| `Gunner_R_End` | 소멸(Zone 0.3s 축소) | 전원 | 3D | 0.4s | 꺼지는 험 | — | (위 클립 끝) | `laser beam powering down, 400ms` | ❌ |
| `Gunner_Dash` | 대시(잔상) | 전원 | 3D | 0.2s | 장비 덜컥 + 바람 | `FX_GunnerDashAfterimage` | 대시 3회 | `tactical dash with gear rattle, 200ms` | ⚠️ SVN `Dash/` 공용 |
| `Gunner_Footstep` | `gunner_walk` ~0.67s 주기(발 이벤트 **없음**) | 전원 | 3D 1~10m | 0.09s · 변형 4 | 전술 부츠 | — | 걷기 10초 | `tactical boot footstep on concrete, 90ms` | ⚠️ SVN `Footstep/` 공용 · 이벤트 추가 필요 |

## 대조
- 팔라딘: 평타 4단(휘두름·적중·헛스윙) · 우클릭 5단계 · Q 4단계 · E 7단계 · R 6단계 · 패시브 3순간 · 이동·대시 → 표 28행(헛스윙은 무음 결정, 패시브 "피격 시 쿨 감소"는 서버 판정만이라 소리 없음).
- 어쌔신: 평타 3모드 · 우클릭 4단계 · Q 5단계 · E(일반 3·변신 3) · R 4단계 · 이동·대시 → 표 22행(일반 1~4타 베기는 한 행에 4키).
- 거너: 평타 5단계 · 과열 4순간 · Q 7단계 · E 1 · 우클릭 3 · R 6단계 · 이동·대시 → 표 24행.
- 공용(대시 간파·피격·보호막·사망·부활·낙사·버프 등)은 [02-combat-common.md](02-combat-common.md).
