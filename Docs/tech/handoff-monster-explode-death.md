# handoff — 잡몹 8종 폭발 사망 (CollapseDeath → ExplodeDeath)

> 2026-10-21 · 민경(Claude) · 브랜치 `feature/VFX` · 계획 [PLAN.md](../../PLAN.md) 「잡몹 5종 폭발 사망」
> **경석 님 영역(몬스터 프리팹)에 컴포넌트 교체가 있다.** `MonsterBase`·FSM·데이터는 안 건드렸다.

## 1. 몬스터 프리팹 변경 — 경석 님 확인용
- `ChompBot`·`HumanoidBot`·`MortarBot`·`PeekABot`·`TeslaBot` 의 사망 연출 컴포넌트 `CollapseDeath` → **`ExplodeDeath`**(같은 오브젝트·같은 자리, 공통 칸 값 그대로).
  - 둘 다 `NetworkBehaviour` + `IDeathEffect` 라 개수·순서가 그대로다. `MonsterBase` 는 지금처럼 `GetComponent<IDeathEffect>()` 로 집는다.
  - 타이밍도 같다 — 사망 클립 끝 0.1초 전(`leadBeforeClipEnd`).
- **2차(같은 날): `GauntletBot`·`SpinnerBot`·`WallBot` 도 `ExplodeDeath`** — 엘리트라 더 세게(바깥 3~5 · 위로 3~5m/s · 회전 360~720도/초) + 다른 폭발 `FX_Death_Explosion_02`. 이제 **잡몹 8종 전부 폭발**이고 `CollapseDeath` 를 쓰는 프리팹은 없다(코드·도구는 남아 있음).
- ⚠️ 한 몹에 `CollapseDeath` 와 `ExplodeDeath` 를 같이 두지 말 것(둘 중 아무거나 걸린다). `Tools/Monster/부위 붕괴 — …` 도구는 `ExplodeDeath` 가 있으면 붕괴를 붙이지 않고 조각 에셋만 다시 굽는다.

## 2. 연출(민경 영역, 참고)
- 순서: 터짐(조각이 바깥 1.5~2.5m/s · 위로 2~3m/s · 회전 180~360도/초) + 폭발 이펙트 1회 → 1.2초 굴러다님 → 0.6초 디졸브(파티클 없음 — 붕괴 때 쓰던 `FX_Dissolve_Death` 는 폭발에선 뺐다).
- 코드: 신규 `1.Scripts/Monster/Collapse/ExplodeDeath.cs` · `MonsterCollapseDebris.Config` 에 칸 2개 **추가**(바깥 속도 범위 · 위로 속도 범위 — 비우면 붕괴와 똑같이 돈다) · `MonsterCollapseSetup` 가드.
- 이펙트: 엘리트 3종은 `ExplodeDeath.explosionEffect` 에 `FX_Death_Explosion_02_Entry`(카탈로그 `Mob_Death_Explosion_Elite` 로 prewarm), 비운 몹(잡몹 5종)은 카탈로그 `Mob_Death_Explosion`(`50.Art/VFX/Common/Monsters/Death/FX_Death_Explosion_01` + `_Entry`). `EffectManager` 로 내서 몬스터가 디스폰돼도 잘리지 않는다.
- 폭발 위치: `burstHeight` 0 = 터지는 순간 몸 렌더러의 경계 중심(몹마다 자동). 배율 `explosionScale` 1 — Play 에서 보고 몹별로 조정.
- 🔴 SVN add: `Common/Monsters/Death/`(폴더·프리팹·엔트리) · `_Materials/Mat_fx_HCFX_Dust_02.mat` · `Textures/Tex_fx_HCFX_Dust_02.tga` · (2차) `Death/FX_Death_Explosion_02.prefab`·`_Entry` · `_Materials/Mat_fx_HCFX_Glow_Long_01_add.mat`·`Mat_fx_HCFX_Impact_01_add.mat` · `Textures/Tex_fx_HCFX_Glow_Long_01.tga` (+ 각 `.meta`).

## 3. 확인 부탁
- Play/MPPM: 8종 처치(엘리트 3종은 더 크게 튀고 02 폭발) → 휘청 → 조각이 위·바깥으로 튀며 폭발 이펙트 → 디졸브(파티클 없음), 호스트·클라 둘 다.
