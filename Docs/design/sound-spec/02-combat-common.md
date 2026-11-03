# 02. 공용 전투 — 간파 · 타격/피격 · 발소리 · 상태 · 생사

> 공통 열 뜻은 [README.md §2](README.md#2-표-읽는-법). 캐릭터 3종이 같이 쓰는 소리. 캐릭터 고유 레이어는 [01-player.md](01-player.md).
> 간파 연출은 `InterruptProductionDirector`(`NetworkManager.prefab`)가 단일 진입점 — 새 간파 소리는 `GlobalTimeScale.Started` 직접 구독이 아니라 **디렉터 cue** 로 붙인다([interrupt-presentation.md §6](../../tech/interrupt-presentation.md)).

## 2.1 간파 (전 캐릭터 공용 층)

> 순서: 시도(입력) → 슬로우(강 0.15배·0.5s / 약 0.5배·0.2s) → 판정 → 성공(히트스톱 0.05s → 성공 슬로우 0.25배·1.2s → 퇴장 0.4s) 또는 실패(0.1s 복귀).
> 슬로우 중엔 모든 소리가 같이 느려지면 어색하다 — **간파 소리는 타임스케일 무시**(pitch 고정) 권장.

| 키 | 언제 | 듣는 사람 | 2D·3D | 길이·반복 | 느낌 · 레퍼런스 | 짝 VFX | 녹화 | 프롬프트(EN) | 상태 |
|---|---|---|---|---|---|---|---|---|---|
| `Interrupt_Predict_Strong` | 입력 순간 강 예측 — 오너 암전(`LocalOwnerAttempt`) | 오너 | 2D | 0.4s | 세상이 멎는 "흡" — 저음 서브 드롭 + 고음 링 · Sekiro 위험 신호 반대 버전 | 오너 암전 | 간파 성공 장면 10회 모음 | `time slows down effect, deep sub drop with high pitched ring, 400ms` | ❌ |
| `Interrupt_Slow_In` | 시도 슬로우 진입(`GlobalTimeScale` Started, 목적 Attempt) | 전원 | 2D | 0.3s | 공기가 늘어지는 리버스 | — | (위) | `slow motion whoosh in, reversed air swell, 300ms` | ❌ |
| `Interrupt_Success` | 성공 히트스톱 순간(A1·A4) | 전원 | 2D | 0.8s | **간파 핵심** — 날카로운 금속 파열 + 낮은 충격 + 긴 링 · Sekiro 튕겨내기 + 레이븐스워치 | `InterruptSuccessFx` | (위) | `perfect parry impact, sharp metallic clash with heavy bass hit and long ringing tail, 800ms` | ⚠️ SVN `Detection/Heavy_interrupt_hit` |
| `Interrupt_Success_Slow_Loop` | 성공 슬로우 1.2s 동안 | 전원 | 2D | 1.2s | 먹먹한 저음 지속 + 심장 박동 1회 | — | (위) | `slow motion ambience, muffled low drone with single heartbeat, 1.2s` | ❌ |
| `Interrupt_Slow_Out` | 슬로우 퇴장 0.4s | 전원 | 2D | 0.4s | 시간이 돌아오는 정방향 휘익 | — | (위) | `time resuming whoosh, fast forward air rush, 400ms` | ❌ |
| `Interrupt_Fail` | 실패(빗나감·판정 전 종료) | 오너 | 2D | 0.3s | 김빠짐, 짧고 작게 | — | 헛간파 5회 | `failed parry, dull whiff with short descending tone, 300ms` | ❌ |
| `Interrupt_Teammate_Light` | 팀원 성공 — 암순응(꺼짐 칸) | 다른 사람 | 2D | 0.5s | 멀리서 들리는 성공 반향 | — | — | `distant parry echo, reverberant metallic ring, 500ms` | ❌ (연출 꺼짐 상태) |
| `DashInterrupt_Window` | 대시 간파 성립(대시 0.1s 안 우클릭) | 오너 | 2D | 0.15s | 짧은 "칭" — 입력이 먹혔다는 확인 | — | 대시→우클릭 5회 | `quick confirm ting, sharp, 150ms` | ❌ |

## 2.2 타격 · 피격

| 키 | 언제 | 듣는 사람 | 2D·3D | 길이·반복 | 느낌 · 레퍼런스 | 짝 VFX | 녹화 | 프롬프트(EN) | 상태 |
|---|---|---|---|---|---|---|---|---|---|
| `Hit_Generic_Metal` | 플레이어 공격이 로봇 몬스터에 맞음 — 캐릭터별 적중음이 없을 때의 기본 | 전원 | 3D | 0.2s · 변형 4 | 금속 "깡" | — | 아무 캐릭터로 몬스터 타격 | `generic hit on metal robot, clang impact, 200ms` | ⚠️ SVN `Hit/` 1개 |
| `Player_Hurt` | 플레이어 피격 HP 감소(`ClientDamaged`) | 전원 | 3D 2~15m | 0.25s · 변형 4 | 둔탁한 충격 + 짧은 신음(캐릭터별 보이스는 선택) | `HitFlash` | 몬스터에게 맞기 5회 | `player taking damage, dull body impact thud with short grunt, 250ms` | ❌ |
| `Player_Hurt_Self` | 내가 맞음 — 위와 동시에, 나만 크게 | 오너 | 2D | 0.2s | 화면 흔들림 같은 저음 "둥" | — | (위) | `low punchy damage feedback thump, 200ms` | ❌ |
| `Player_Hurt_Heavy` | 큰 피해(보스 패턴 등, 기준값은 코드에서 정함) | 전원 | 3D | 0.4s | 크게 날아가는 충격 | — | 보스에게 맞기 | `heavy impact knockback hit, body slam, 400ms` | ❌ |
| `Knockback_WallImpact` | 넉백 벽 충돌(`WallImpactPresentRpc`) | 전원 | 3D 3~20m | 0.3s | 벽에 처박힘 "쾅" | 벽 충돌 연출 | 보스 넉백으로 벽 | `body slamming into metal wall, heavy thud, 300ms` | ❌ |
| `Shield_Gain` | 보호막 생김(목록 복제) | 전원 | 3D 2~12m | 0.4s | 막이 둘러쳐짐 "우웅" | `AllyShield`·`HolyShield` | 거너 Q 로 보호막 | `energy shield activating around body, soft whoosh hum, 400ms` | ❌ |
| `Shield_Hit` | 보호막이 맞음(`HitShieldVfxRpc`) | 전원 | 3D | 0.2s · 변형 3 | 에너지막 파문 | 배리어 파문 | 보호막 상태로 맞기 | `energy shield absorbing hit, shimmering ripple zap, 200ms` | ❌ |
| `Shield_Break` | 보호막 깨짐(`BreakBurstRpc`) | 전원 | 3D 3~20m | 0.5s | 유리처럼 깨지는 에너지 | `AllyShield_Break`·`HolyShield_Break` | 보호막 깨질 때까지 맞기 | `energy shield shattering, glassy electric break, 500ms` | ❌ |
| `Shield_Expire` | 보호막 만료(조용히 사라짐) | 전원 | 3D 1~8m | 0.3s | 작게 꺼짐 | — | — | `shield fading out, soft descending hum, 300ms` | ❌ |

## 2.3 발소리 (전 캐릭터 공용 재료 · 캐릭터별 키는 01)

> ⚠️ 세 캐릭터 모두 걷기/달리기 클립에 **발 이벤트가 없다** → 클립에 `Footstep` 이벤트를 심거나, 이동 속도 기준 주기 재생 코드가 필요(이번 범위 밖).
> 바닥 재질 2종은 SVN 에 있다(`Footstep/Concrete` 5개 · `Footstep/Vent` 3개). 재질 판정도 코드 추가 필요.

| 키 | 언제 | 듣는 사람 | 2D·3D | 길이·반복 | 느낌 | 녹화 | 프롬프트(EN) | 상태 |
|---|---|---|---|---|---|---|---|---|
| `Footstep_Concrete` | 콘크리트 바닥 걸음 | 전원 | 3D 1~12m | 0.1s · 변형 5 | 단단한 바닥 | — | (01 의 캐릭터별 프롬프트) | ✅ SVN `Foot_Conc_ShoesRun01~05` |
| `Footstep_Vent` | 금속 격자·환풍구 위 | 전원 | 3D 1~12m | 0.1s · 변형 3 | 속 빈 금속 "통통" | — | `footstep on hollow metal vent grate, 100ms` | ✅ SVN `Foot_HollowVent_Run02~04` |
| `Footstep_Metal` | 금속 판·다리·엘리베이터 바닥 | 전원 | 3D 1~12m | 0.1s · 변형 4 | 철판 "깡" | — | `footstep on solid steel plate floor, 100ms` | ❌ |
| `Footstep_Land` | 착지(낙사 복귀·점프 패드 등) | 전원 | 3D 1~12m | 0.2s | 두 발 착지 | — | `two feet landing on concrete, 200ms` | ❌ |

## 2.4 상태 · 생사

| 키 | 언제 | 듣는 사람 | 2D·3D | 길이·반복 | 느낌 · 레퍼런스 | 짝 VFX | 녹화 | 프롬프트(EN) | 상태 |
|---|---|---|---|---|---|---|---|---|---|
| `Status_Stun_Start` | 기절 시작(상태이상 복제) | 전원 | 3D 2~12m | 0.4s | 머리 위 별 도는 "띠링" | `FX_Faint` | 기절 패턴 맞기 | `stunned dizzy sound, cartoonish ringing birds-like electronic, 400ms` | ❌ |
| `Status_Stun_Loop` | 기절 중 | 전원 | 3D 1~6m | 루프 | 작은 지직 링 | `FX_Faint` | (위) | `dizzy electronic ringing, quiet seamless loop` | ❌ |
| `Status_Restrained` | 잡힘·밀림 시작(`BeginRestrainedClientRpc`) | 전원 | 3D | 0.4s | 기계 집게에 잡힘 | — | 보스 잡기 | `grabbed by mechanical claw, metal clamp, 400ms` | ❌ |
| `Status_SuperArmor` | 슈퍼아머 부여(팔라딘 Q·R 등) | 전원 | 3D 2~10m | 0.3s | 몸이 단단해짐 "징" | `DissolveOverlay` | — | `armor hardening, metallic resonance, 300ms` | ❌ |
| `Buff_Gain` | 일반 버프 획득(이속·Focus 등) | 오너 | 2D | 0.3s | 밝은 상승 | — | — | `buff gained, positive ascending chime, 300ms` | ❌ |
| `Buff_SuppressHaste` | 제압 가속(23호 제압 시 전원 버프) | 전원 | 2D | 0.6s | 아드레날린 — 심장 + 상승 | — | 23호 제압 성공 | `adrenaline rush power up, heartbeat with rising synth, 600ms` | ❌ · VFX 없음 |
| `Buff_Expire` | 버프 만료 | 오너 | 2D | 0.2s | 작게 내려감 | — | — | `buff expired, soft descending tone, 200ms` | ❌ |
| `Player_Death` | 사망 연출 진입 1.5s(`TryBeginDeathPresentation`) | 전원 | 3D 3~25m | 1.0s | 로봇 슈트가 꺼지는 "위잉-툭" · 사망 애니 없음 | — | 사망 장면 | `player death, power suit shutting down with electric fizzle and collapse, 1s` | ❌ |
| `Player_Soul_Enter` | 다운 → 영혼 상태 | 전원 | 3D | 0.8s | 유령처럼 빠져나옴 | Soul 외형 교체 | (위 이어서) | `ghostly spirit leaving body, ethereal whoosh, 800ms` | ❌ |
| `Player_Soul_Loop` | 영혼 이동 중 | 오너 | 2D | 루프 | 먹먹한 공허 | — | 영혼 상태 10초 | `ethereal hollow ambience, muffled, seamless loop` | ❌ |
| `Player_Revive` | 부활(Soul → Alive) | 전원 | 3D 3~25m | 1.0s | 재기동 — [07](07-flow-bgm-amb.md) `Revive_Tutorial` 과 공용 가능 | 외형 복귀 | 부활 장면 | `robotic reboot revive, power surge rising, 1s` | ❌ |
| `Player_Respawn_Wipe` | 전멸 리스폰(무적 2s 깜빡임) | 전원 | 3D | 0.8s | 소환 | 깜빡임 | 전멸 후 리스폰 | `respawn materialize, energy shimmer, 800ms` | ❌ |
| `Player_Fall` | 낙사 감지 | 오너 | 2D | 0.8s | 떨어지는 "으아-" 대신 바람 하강 | — | 낭떠러지 낙사 | `falling into abyss, descending wind whoosh, 800ms` | ❌ |
| `Player_Fall_Recover` | 낙사 생존 복귀(`BeginRecoveryRpc`) | 전원 | 3D | 0.5s | 순간 복귀 | — | (위) | `teleport back to safety, quick shimmer, 500ms` | ❌ |
| `Heal_Generic` | 회복(패시브 외 회복원) | 전원 | 3D 2~12m | 0.5s | 따뜻한 상승 | `Heal` | — | `healing sound, warm ascending shimmer, 500ms` | ❌ |
| `Dash_Generic` | 대시 공용 재료 | 전원 | 3D | 0.2s · 변형 2 | 바람 | — | — | (캐릭터별 01) | ✅ SVN `Dash/universfield-swoosh-*` 2개 |

## 대조
- 조사 메모 "공용 동작" 표 26행 → 간파 공통 3 + 대시 간파 2 → 2.1(8행), 피격·보호막·넉백 7 → 2.2(9행), 이동 → 2.3(4행), 상태·사망·부활·낙사·회복·버프 → 2.4(16행).
- 소리 없음으로 둔 것: 자연 재생 회복(HUD 만, 잦아서 소음), 착지 보호 깜빡임, 대시 충전 없음 거부(이벤트 없음 — 06 `UI_Deny` 를 쓰려면 코드 추가).
