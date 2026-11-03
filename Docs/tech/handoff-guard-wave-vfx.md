# 핸드오프 — 수호의 파동(PAL_4_E_PUSH) 바람 링 VFX

> 2026-11-02 · 민경(Claude) · 브랜치 `feature/VFX` · 계획 [PLAN.md](../../PLAN.md) 맨 위(이 작업이 최신일 때).
> 판정·밀치기 코드는 무변경 — 이미 나가던 특성 연출 사건을 받아 재생만 한다.

## 동작
- E 방어 성공 → 반격 판정 순간 서버가 `TraitFxTrait.PaladinGuardWave` **Activated** 를 전 피어에 보낸다(`FirstMeleeSubSkill.ResolveCounterHit`, 밀린 적이 없어도 1번).
- `PaladinSkillView`(Player_Paladin 루트)가 받아 사건 위치(발밑)에 `FX_GuardWave_Ring_Entry` 를 재생한다. **배율 = 사건 도형의 원 반경**(받는 피어가 같은 특성 수치로 다시 만든 판정 원) — 링은 반경 1m 저작이라 링 끝 = 판정 끝, 수치가 바뀌어도 맞는다. 약 0.25초에 끝에 닿는다(밀치기 시간과 같다).
- 밀린 적마다의 **Hit** 사건은 연출 없음(적별 연출 `FX_GuardWave_Push` 는 민경 확인 후 뺐다).
- 같은 순간 반격 클립 Hit 에 기존 `GuardianCounter`(금빛 반격 베기)가 함께 나온다. 링 = 흰 바람 + 옅은 금빛 테(바닥 고리 + 무릎 높이 벽).

## 바뀐 파일
| 파일 | 내용 |
|---|---|
| `1.Scripts/Player/Skill/PaladinSkillView.cs` | 칸 `guardWaveRing` · `HandleTraitFx` 에 `PaladinGuardWave` Activated 분기 → `PlayGuardWave`(반경 = `AttackCircleShape.Radius`, 도형 없으면 생략) |
| `1.Scripts/Effects/EffectCatalog.cs` · `9.ScriptableObject/Effects/EffectCatalog.asset` | `GuardWave_Ring` 등록(프리웜) |
| `2.Prefabs/Player/Paladin/Player_Paladin.prefab` | `PaladinSkillView.guardWaveRing` 연결(1줄) |
| SVN `50.Art/VFX/Common/Players/Player1/ESkill/GuardWave/` | `FX_GuardWave_Ring`(+`_Entry`) · 재질 8 · 텍스처 9 · 메시 5 + `SlashMesh_ring.fbx`(NamuFX, 금빛 테 메시 — 민경 수정분) |

## 원본 제작(MyProject)
- 빌드 메뉴 `Tools/VFX/Guard Wave (팔라딘 수호의 파동) 빌드` → `Export/Player1/ESkill/GuardWave/` · 미리보기 `Scenes/GuardWavePreview.unity`(`GuardWaveTester` — 실제 팔라딘 반격 클립 + 허수아비 3개 + 3m 경계).
- 🔴 `Ring_GoldEdge` 메시를 손으로 NamuFX `SlashMesh_ring` 으로 바꿨다 — 빌더를 다시 돌리면 이 수정이 덮어써진다(다시 돌릴 땐 손으로 다시 바꾸거나 빌더에 반영).

## 확인
- DebugTraitLab(Play): `PAL_4_E_PUSH` 넣고 E → 허수아비 공격으로 방어 성공 → 반격 순간 링 1개 생성(발밑, 배율 3) — 로그로 확인.
- [ ] 민경 Play: 화면에서 링 크기·타이밍(랩 `플레이어 피격` 패널 — 마우스를 허수아비 쪽에 두고 E) · MPPM 클라 화면.

## SVN / git
- SVN: `50.Art/VFX/Common/Players/Player1/ESkill/GuardWave/` 폴더 add.
- git: 위 코드·SO·프리팹 + 문서.
