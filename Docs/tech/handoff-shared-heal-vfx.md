# 핸드오프 — 플레이어 공용 원샷 이펙트 표(힐·레벨업) · 나누는 불굴 전이 VFX

> 2026-11-02 · 민경(Claude) · 브랜치 `feature/VFX` · 계획 [PLAN.md](../../PLAN.md) 맨 위(이 작업이 최신일 때).

## 은희 영역 변경점 (먼저 읽기)
| 파일 | 바뀐 것 | 동작 영향 |
|---|---|---|
| `Traits/Effects/TraitSkillFx.cs` | `TraitFxTrait.PaladinShareHeal = 9`(끝에 추가) · `IdOf` → `"PAL_2_SHARE_HEAL"` | 없음(연출 사건 종류 하나 추가) |
| `Player/Stats/PlayerTraitEffects.cs` | `ShareOverflow` — 아군 실제 회복 > 0 이면 `TraitSkillFxNetwork.ServerSend(PaladinShareHeal, Hit, target: 아군)` 한 줄 | 판정·수치 무변경. 공용 RPC 1회 늘어남(초과 전달 1회당) |
| `Player/Stats/PlayerTraitEffects.cs`(2) | 후보 목록 타입 `Player` → `Unit` · **개발 빌드 전용** `DevShareRecipients`(DebugTraitLab 아군 허수아비가 등록, 플레이어 뒤 순번) | 릴리스 빌드 무변경(목록이 컴파일되지 않음) |
| `Player/Skill/FirstMeleePassive.cs` | `healBurst`(EffectSocketPlayer 칸) 삭제 · `ProcVfxRpc` 에 `selfHealed` 칸 추가 · 회복 연출 = `PlayerOneShotVfxPresenter.Play(Heal)` | 팔라딘 본인 힐 이펙트가 **실제로 회복했을 때만** 나온다(만피면 안 나옴 — 전부 나누는 불굴로 넘어가므로) |

## 공용 원샷 이펙트 표
```
9.ScriptableObject/Effects/PlayerOneShotVfxSet.asset   (SO PlayerOneShotVfxSet)
  Heal    → Shared/Heal/FX_Heal_Entry      자리 발밑 · 따라감
  LevelUp → Shared/States/Level_up_Entry   자리 발밑 · 따라감

각 Variant(Paladin/Gunner/Assassin) VFX/States
  ├ StatusEffectVfxPresenter   (상태이상 루프 — 그대로)
  └ PlayerOneShotVfxPresenter  set = 위 SO · anchors = 옆 StatusEffectVfxPresenter(자리 공유)
```
- **표 = 무엇을 어디에** / **계기(부르는 쪽 코드) = 언제·누구 화면에**. 표에는 '본인만' 칸이 없다 — 레벨업은 계기(`TraitAcquiredVfxPresenter`)가 이미 오너만 거른다.
- 재생: `PlayerOneShotVfxPresenter.For(유닛).Play(PlayerOneShotVfx.X)`. 반드시 전 피어 경로(RPC 수신부·복제 상태 감시)에서 부른다.
- 따라가기: outroDuration 이 있는 엔트리 = 곧바로 해제해 그 동안 따라감(레벨업 — 옛 방식과 같은 길이) / 없으면 엔트리 길이만큼 따라간 뒤 해제(힐).
- `TraitAcquiredVfxPresenter` 는 이제 획득 감시만 한다(엔트리·배율 칸 삭제).
- 팔라딘 `VFX/PassiveSkill/Heal` 소켓 삭제(빈 `PassiveSkill` 오브젝트만 남음).
- **새 원샷 추가법**: `PlayerOneShotVfx` enum 끝에 한 줄 → SO 에 줄 추가 → 계기 코드에서 `Play`.

## 나누는 불굴 전이
```
서버 ShareOverflow → TraitSkillFxRpc → TraitSkillFx.Raised(PaladinShareHeal, Hit)
  → Player_Paladin VFX 의 PaladinShareHealVfx (시전자 = 이 팔라딘인 것만)
     ① FX_ShareHeal_Swirl  팔라딘 몸 따라가는 소용돌이(0.95초)
     ② FX_ShareHeal_Ribbon 리본 3가닥(HealTransferRibbon) → 움직이는 아군 추적
     ③ 도착 순간 아군 Play(Heal)
```
- 에셋(SVN): `50.Art/VFX/Common/Players/Player1/PassiveSkill/ShareHeal/` — 프리팹 2 · 엔트리 2(리본 duration 1.2 · 프리웜 2). `EffectCatalog` 에 `ShareHeal_Swirl`·`ShareHeal_Ribbon` 등록. 머티리얼 = `_Materials/Glow Fade`, 색 = `FX_Heal` 계열 라임.
- 리본 튜닝 칸(`FX_ShareHeal_Ribbon` 루트 `HealTransferRibbon`): `travelSeconds`(도착 시간, 기본 0.4 — 거리 무관) · 출발/도착 높이 · 옆/위 휨 범위 · 물결 폭·수 · 진행 곡선. 엔트리 duration 은 `travelSeconds` + 트레일 Time(0.3)보다 길어야 꼬리까지 보인다.
- 날아가는 중 아군이 쓰러짐·끊김·연출 숨김이면 리본이 그 자리에서 흐려지고 아군 힐은 생략(체력은 서버에서 이미 들어감).

## 1인 확인 — DebugTraitLab
Dev Boot → `DebugTraitLab` → Play → 팔라딘 · `PAL_2_SHARE_HEAL`(+ 원하면 불굴의 회복) 넣기 → 패널 `플레이어 체력`에서 나 = `만피`, 아군 허수아비 = `30%` → 허수아비 쳐서 불굴의 의지 발동 → 리본이 아군 허수아비(-4, 0, 2)로. 나를 `50%` 로 두면 본인 힐 + (초과분 있으면) 리본.

## Play 체크리스트 (민경)
- [ ] MPPM 2인: 팔라딘 **만피**로 불굴의 의지 발동 → 소용돌이 + 리본 + 아군 힐, 팔라딘 힐 없음
- [ ] 팔라딘 체력 깎인 상태 → 팔라딘 힐 + (초과분 있으면) 리본·아군 힐
- [ ] 호스트·클라 화면 둘 다 같은 연출
- [ ] 레벨업(증강 선택) 회귀: 고른 본인만, 몸 따라감
- [ ] 자연 회복으로 힐 이펙트 안 남
- [ ] 거너·어쌔신이 받는 쪽일 때도 힐 이펙트 나옴

## SVN 업로드 목록 (민경)
- `Assets/50.Art/VFX/Common/Players/Player1/PassiveSkill/ShareHeal/`(+`.meta`) 전부 — 새 폴더.
- (이미 민경이 옮김) `Players/Shared/Heal/`·`Shared/States/` 이동분.

## git 커밋 대상
- 새 스크립트 `Effects/PlayerOneShotVfxSet.cs`·`PlayerOneShotVfxPresenter.cs`·`HealTransferRibbon.cs`·`PaladinShareHealVfx.cs`(+meta)
- `Effects/StatusEffectVfxPresenter.cs`(`GetAnchor` 공개)·`TraitAcquiredVfxPresenter.cs`·`EffectCatalog.cs` · 위 은희 영역 3파일
- `Player_Paladin/Gunner/Assassin.prefab` · `9.ScriptableObject/Effects/PlayerOneShotVfxSet.asset`(+meta)·`EffectCatalog.asset`
- 문서: 이 파일 · `trait-effect-events.md` · `PLAN.md` · `CONTEXT.md`
