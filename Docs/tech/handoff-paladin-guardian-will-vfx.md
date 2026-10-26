# handoff — 팔라딘 E(수호자의 의지) 연출: 상태 클립별 VFX(점화 · 결계 · 해제 · 막아냄 · 오라 · 기 모으기 · 반격)

> 2026-10-26 · 민경(Claude) · 브랜치 `feature/VFX` · 계획 [PLAN.md](../../PLAN.md) 「팔라딘 E(수호자의 의지) 연출」
> **은희 님 영역 변경 = 연출 배선만.** 방어 판정·반격 판정·피해·넉백 무시·종료·네트워크는 그대로다.

## 1. 은희 영역 변경
- `1.Scripts/Player/Skill/FirstMeleeSubSkill.cs`
  - **임시 반격 충격파 제거**: `ResolveCounterHit` 끝의 `shieldVfx.ServerInterruptWave()` 한 줄 삭제. `shieldVfx` 칸은 반격 자세 전환(`ServerPlayGuardCounterPose`)에 계속 쓴다. 우클릭 충격파(`FirstMeleeInterruptSkill`)는 그대로.
  - 새 칸 4개: `shieldGlow`(MaterialFadeEffect — 우클릭과 같은 방패 문양 발광) · `barrierLoop` · `auraLoop` · `chargeLoop`(EffectSocketPlayer).
  - `OnClientPlay` → `shieldGlow.FadeIn()` (E 내내 방패 발광).
  - `OnEnd`(전 피어, 종료 사유 무관) → `shieldGlow.FadeOut()` + 루프 3개 `Stop()`. 끊김(경직·사망·안전망)엔 클립의 Stop 이벤트가 안 오므로 여기서 회수한다. 이미 꺼져 있으면 no-op.
- `4.Animations/Player/Garen/` — 클립 3개에 `EffectAnimEvents` 이벤트 추가(전 피어에서 각자 울림, 기존 `HandleSkillEvent` 무변경). 시각 = 같은 클립의 스킬 이벤트와 같은 시각.

  | 클립 | 시각 | 이벤트 |
  |---|---|---|
  | `Garen_E_GuardStart` | 0 | `PlayEffect` GuardianIgnite |
  | | 0.9(Custom0 방어 창 열림) | `StartEffect` GuardianBarrier |
  | `Garen_E_GuardEnd` | 0.1(Custom1 창 닫힘) | `StopEffect` GuardianBarrier · `PlayEffect` GuardianRelease |
  | `Garen_E_GuardCounter` | 0 | `StopEffect` GuardianBarrier · `PlayEffect` GuardianBlock · `StartEffect` GuardianAura · `StartEffect` GuardianCharge |
  | | 0.433(Hit 반격) | `StopEffect` GuardianCharge · `PlayEffect` GuardianCounter |
  | | 1.1(End) | `StopEffect` GuardianAura |

  - 🔴 Custom0·Custom1·Hit·End 를 옮기면 같은 시각의 연출 이벤트도 같이 옮겨 주세요(결계 = 실제 방어 창, 기 모으기 = 반격 판정 전까지가 의도).
  - `Garen_E_GuardIdle` 은 무변경(결계가 이어짐).
- `2.Prefabs/Player/Paladin/Player_Paladin.prefab`
  - 새 `VFX/GuardianWill` 밑 EffectSocketPlayer 7개 — Id `GuardianIgnite`·`GuardianBarrier`·`GuardianRelease`·`GuardianBlock`·`GuardianAura`·`GuardianCharge`·`GuardianCounter`. 소켓은 자기 위치(= 플레이어 루트 · 정면 +Z), `GuardianCharge` 만 검 `SM_Wep_Sword_03`. safetyTimeout Barrier 3초 · Aura 2초 · Charge 1초.
  - `FirstMeleeSubSkill` 새 칸 4개 연결(`shieldGlow` = 방패 `SM_Wep_Shield_01` 의 MaterialFadeEffect).
  - 새 문서는 파일 끝에 덧붙였다 — 다음에 에디터가 저장하면 순서가 한 번 정리될 수 있다(내용 동일).

## 2. 연출(민경 영역, 참고)
- 색: 방어 단계 흰빛·옅은 금빛 / 반격 단계 진한 금빛 · 반격(Counter)만 둘의 중간색(10-26 — 반격 불티는 전용 재질 `M_GuardianWill_CounterSpark`).
- Ignite(원샷) 방패 점화 섬광 + 발밑 빛 고리 · Barrier(루프) A 반원 빛 벽(정면 180° · 반지름 1.2m · 높이 1.6m) + B 휜 방패 투영 + C 바닥 반원 문양 · Release(원샷) 결계가 빛 조각으로 흩어짐 · Block(원샷) 방패 앞 섬광 + 결계 금색 번쩍·깨짐 · Aura(루프) 몸 금빛 오라(반격 중 피해 무시 표시) · Charge(루프) 검에 금빛 모임 · Counter(원샷) 반격 클립 실제 검 궤적 금빛 베기 호 + 바닥 120° · 3m 부채꼴 충격파(데이터 counterAngle·counterRange 와 같은 범위).
- 엔트리 outro: Barrier 0.05초(끄는 순간의 사라짐은 Release·Block 원샷이 맡음) · Aura 0.25초 ×2 · Charge 0.1초 ×5.
- 에셋(🔴 SVN add):
  - `50.Art/VFX/Common/Players/Player1/ESkill/FX_GuardianWill_{Ignite,Barrier,Release,Block,Aura,Charge,Counter}.prefab` + `_Entry.asset` 7개
  - `50.Art/VFX/_Materials/Player1/`(새 폴더) `M_GuardianWill_*` 23
  - `50.Art/VFX/Textures/Player1/`(새 폴더) `T_GuardianWill_*` 11
  - `50.Art/VFX/Models/Player1/`(새 폴더) `SM_GuardianWill_*` 6(벽·바닥·부채꼴·방패 판·베기 호 2)
- 원본 제작: MyProject `Tools/VFX/Guardian Will (팔라딘 E 수호자의 의지) 빌드` → `Export/Player1/ESkill/` (미리보기 `Scenes/GuardianWillPreview` — 실제 팔라딘 모델·E 클립 재생).

## 3. 확인 부탁
- Play/MPPM(호스트·클라): E 시전 → 방패 점화 · 방어 창 동안 결계 → 방어 실패 = 결계 흩어짐 / 정면 피격 방어 성공 = 막아냄 섬광·결계 깨짐 → 반격 중 금빛 오라·검 기 모으기 → 반격 순간 베기 호·부채꼴 충격파 → 끝나면 오라 꺼짐 · 반격 충격파(우클릭 것)가 E 에서 더는 안 나옴 · 공격 속도 버프 때도 결계·기 모으기가 이벤트 시각에 맞춰 꺼짐 · 방어·반격 중 경직·사망 때 결계·오라·기 모으기가 남지 않음.
- 알려진 가능성(MPPM 에서 확인): 방어 창이 막 닫히는 순간 막으면 해제(Release)와 막아냄(Block)이 겹쳐 보일 수 있다 · 원격 오너 화면은 반격 상태가 동기로 다시 들어가 반격 클립 이벤트가 두 번 울릴 수 있다(원샷 두 번).
