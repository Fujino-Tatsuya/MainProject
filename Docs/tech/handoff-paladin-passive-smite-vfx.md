# 핸드오프 — 팔라딘 패시브(불굴의 의지) 추가 피해 VFX `FX_Paladin_Smite` (2026-10-28 · 민경(Claude) → 은희)

> 계획·결정 원본: [PLAN.md](../../PLAN.md) 맨 위 "성광 낙인". 판정·피해·회복·쿨·RPC 는 **안 바꿨다** — 연출 배선만.

## 1. 은희 영역에서 바뀐 것
| 파일 | 변경 |
|---|---|
| `1.Scripts/Player/Skill/FirstMeleePassive.cs` | `PlayAdditionalHitLocal` — 회전 `identity` → **팔라딘 → 대상 수평 방향**(+Z = 때린 방향, 관통 빛줄기·등 뒤 불티가 씀). `Play` → `PlayRecallable` + `GetInstances` 로 인스턴스를 받아 `SiphonMotes.SetTarget(transform)` (빛 알갱이가 팔라딘에게 흘러듦). 정적 버퍼 `InstanceBuffer` 1개 추가 · **대상 크기 배율**(10-28): 몸통 바운즈 높이 ÷ `additionalHitReferenceHeight`(2m), 1~`additionalHitMaxSizeScale`(3) 로 묶어 `additionalHitScale` 에 곱함 — 칸 2개 추가(`[DataTableIgnore]`) · `using System.Collections.Generic` · `additionalHit` 툴팁 엔트리 이름 |
| `2.Prefabs/Player/Paladin/Player_Paladin.prefab` | `FirstMeleePassive.additionalHit` = `FX_Paladin_Smite_Entry` (빈 칸이던 1줄) |

## 2. 새 파일
- `1.Scripts/Effects/SiphonMotes.cs` — 파티클 입자를 `pullDelay`(0.12초) 뒤 지정한 대상(+`targetOffset` 가슴 높이)으로 끌어당긴다. World 시뮬레이션 전용. 대상이 없으면 제자리에서 흐려짐. 풀 반납(`OnDisable`) 때 대상을 지운다.
- `50.Art/VFX/Common/Players/Player1/PassiveSkill/FX_Paladin_Smite.prefab` + `FX_Paladin_Smite_Entry.asset`(computedDuration 0.75)
- `50.Art/VFX/_Materials/Player1/M_Smite_*.mat` 9개 · `50.Art/VFX/Textures/Player1/T_Smite_*.png` 4개 — **SVN add 필요**(민경)

## 3. 동작
- 각 피어가 `ProcVfxRpc` 를 받고 자기 화면에서 재생한다. 알갱이 대상 = 그 피어가 보는 팔라딘(원격이면 보간 위치).
- 기본 몬스터 피격 이펙트(`Hit_dark_magic`)는 그대로 같이 나온다. 이 연출은 시작 섬광 없이 십자 베기(0.03초)부터 나온다.
- 옛 `FX_Additional_Hit`·`Hit2`·`Crack`(+Entry)은 연결 없이 남아 있다.

## 4. Play 체크(민경)
- [ ] 1인: 패시브 충전 → 평타 첫 타 → 로봇 몸통에 금빛 십자 베기 → 등 뒤 섬광·불티 → 빛 알갱이가 팔라딘 가슴으로
- [ ] 엘리트(건틀릿봇)·23호에게는 몸통에 맞춰 커짐(잡몹은 그대로)
- [ ] 때린 방향이 바뀌어도 등 뒤 불티가 반대편으로 나감
- [ ] MPPM: 클라 화면에서도 같은 연출 · 알갱이가 (클라가 보는) 팔라딘에게 감
