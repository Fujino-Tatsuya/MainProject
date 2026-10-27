# handoff — 팔라딘 우클릭(단죄의 방패) 연출: 충격파는 간파 성공 때만 · 판정 전 방패 기 모으기

> 2026-10-26 · 민경(Claude) · 브랜치 `feature/VFX` · 계획 [PLAN.md](../../PLAN.md) 「팔라딘 우클릭(단죄의 방패) 연출」
> **은희 님 영역 변경 = 연출 배선만.** 판정·피해·카운터 창·슬로우·네트워크는 그대로다.

## 1. 은희 영역 변경
- `1.Scripts/Player/Skill/FirstMeleeInterruptSkill.cs`
  - **충격파 조건 변경**: 예전 `OnInterruptResolutionCompleted` 의 `resolvedCount > 0`(적중 1건 이상) 호출을 지우고,
    부모의 정적 이벤트 `PlayerInterruptSkillBase.ServerInterruptSucceeded` 를 `OnEnable`/`OnDisable` 에서 구독해
    **자기 시전(`skill == this`)이 간파 성공(카운터 창 소비)일 때만** `PlayerShieldVfx.ServerInterruptWave()`. 판정 1회에 한 번(여러 대상 성공해도 1회).
    → 간파 창 없는 대상·일반 몹·상자를 때리면 이제 충격파가 안 나온다(의도).
  - 새 칸 `chargeLoop`(EffectSocketPlayer) — `OnEnd` 에서 `Stop()`. 판정 전에 끊긴 경우(경직·사망·취소) 기 모으기 회수용. 켜고 끄기는 아래 클립 이벤트가 한다.
  - `PlayerInterruptSkillBase`(부모)는 무변경. ~~E 반격(`FirstMeleeSubSkill`)의 `ServerInterruptWave` 호출도 그대로.~~
    → 정정(2026-10-27, 할 일 118): E 반격의 임시 충격파 호출은 2026-10-26 에 이미 제거됐다(`FirstMeleeSubSkill` 21줄 주석) — 지금 `ServerInterruptWave` 를 부르는 곳은 간파 성공 relay 하나뿐.
    충격파 성공 신호도 118 에서 정적 이벤트 구독 → 디렉터 성공 단계 relay(`IInterruptSuccessRelay`)로 바뀌었다([handoff-interrupt-director-vfx.md](handoff-interrupt-director-vfx.md)).
- `4.Animations/Player/Garen/Garen_Parry.anim` — 이벤트 2개 추가 + **Hit 시각 0.25 → 0.333초**(모션 무변경):
  - 🔴 Hit 0.333초는 제가 옮긴 게 아니라 작업 전부터 로컬에 있던 변경이다(커밋본 `5ecc21df` = 0.25). 민경 판단으로 **0.333초 유지**해 같이 올린다 — 판정이 1배속 기준 약 0.08초 늦어진다. 의도와 다르면 알려 주세요(그때는 StopEffect 도 같은 시각으로).
  - 0초 `StartEffect("ShieldCharge")` · 0.333초(Hit 와 같은 시각) `StopEffect("ShieldCharge")` — `EffectAnimEvents` 계통이라 전 피어에서 각자 울린다(어쌔신 클립 선례).
  - 🔴 Hit 이벤트를 옮기면 StopEffect 도 같은 시각으로 옮겨 주세요(기 모으기가 판정 순간 꺼지는 게 의도).
  - 작업 전부터 로컬에 있던 `m_EditorCurves` 캐시(Animation 창이 채우는 약 5만 줄)는 걷어 냈다 — 클립 diff 는 `m_Events` 만.
- `2.Prefabs/Player/Paladin/Player_Paladin.prefab`
  - 새 `VFX/ShieldInterrupt/ShieldCharge`(EffectSocketPlayer, Id `ShieldCharge`, 소켓 = 방패 `SM_Wep_Shield_01`(ShieldGlow 와 같음), safetyTimeout 1초).
  - `FirstMeleeInterruptSkill.chargeLoop` = 위 소켓.
  - ⚠️ 이 프리팹도 작업 전부터 에디터 재직렬화로 문서 순서가 바뀐 로컬 변경(약 250줄, 내용 동일)이 있었다. 제 변경은 52줄.

## 2. 연출(민경 영역, 참고)
- `FX_ShieldCharge`(+`_Entry`, outroDuration 0.1 · outroPlayRate 5 — 꺼질 때 남은 입자를 5배속으로 빨리 지움): 방패 둘레 1.1m 에서 금색 빛 줄기가 중심으로 빨려 들고, 중심 빛·후광이 커진다. 재질은 깊이 검사 끔(소켓이 방패 메시 안이라 가려지지 않게).
- 에셋(🔴 SVN add): `50.Art/VFX/Common/Players/Player1/RightClickSkill/FX_ShieldCharge.prefab`·`FX_ShieldCharge_Entry.asset` · `50.Art/VFX/_Materials/M_ShieldCharge_Core`·`Gather`·`Halo`.
- 원본 제작: MyProject `Tools/VFX/Shield Charge (팔라딘 우클릭 기 모으기) 빌드` → `Export/Player1/Skill03/` (미리보기 `Scenes/ShieldChargePreview`).

## 3. 확인 부탁
- Play/MPPM(호스트·클라): 우클릭 시작 → 방패에 기 모임 → 판정 순간 꺼짐 · 허수아비/보스 간파 성공 = 충격파 · 간파 창 없을 때·일반 몹 = 충격파 없음 · 공격 속도 버프 때도 판정 순간에 꺼짐 · 판정 전 경직·사망 때 기 모으기가 남지 않음 · E 반격 충격파는 예전처럼.
