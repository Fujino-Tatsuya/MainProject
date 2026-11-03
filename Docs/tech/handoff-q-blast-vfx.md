# 핸드오프 — 진격의 충격(PAL_4_Q_BLAST) 부채꼴 충격 VFX

> 2026-11-03 · 민경(Claude) · 브랜치 `feature/VFX` · 계획 [PLAN.md](../../PLAN.md) 맨 위(이 작업이 최신일 때).
> 판정·피해 코드는 무변경 — 이미 나가던 특성 연출 사건을 받아 재생만 한다.

## ⚠️ 은희 님께 — 기획서와 판정 데이터가 다르다
- 기획서(`Re_C_특성_기획서_20261016`): "종료 순간 **전방 1m 지점 중심 반경 2m**"(원).
- 데이터 `9.ScriptableObject/Traits/Paladin/PAL_4_Q_BLAST.asset` 파생 hitArea: **Q 끝 지점에서 앞으로 90° 부채꼴, range 2m, forwardOffset 0**.
- 연출은 **실제 판정(부채꼴)** 에 맞췄다. 데이터를 기획대로 원으로 바꾸면 연출 모양도 다시 맞춰야 한다(민경에게 알려 주세요).

## 동작
- Q 가 정상 종료·홀드 해제·최대 시간 도달로 끝나면 0.05초 뒤 서버 판정 순간 `TraitFxTrait.PaladinAdvanceImpact` **Activated** 가 전 피어에 난다(`FirstMeleeMainSkill.RunTraitBlastServer`, 맞은 적 없어도 1번). 돌파의 방패 종료도 같다. 강제 중단·사망이면 없다.
- `PaladinSkillView` 가 받아 사건 도형의 부채꼴(`AttackSectorShape`) 포즈(`ShapePose` — forward/side/yaw 오프셋 반영)에 `FX_QBlast_Entry` 를 재생. **배율 = range**(부채꼴 길이 1 저작), 방향 = 마지막 진행 방향. 각도 90° 는 저작 고정.
- 맞은 적마다의 **Hit** 사건은 연출 없음.
- 구성: 앞쪽 금빛 섬광·별·발밑 빛 고임·불티 / 바닥을 앞으로 쓸고 나가는 금빛 충격파(약 0.15초에 끝)·지나간 자리·낮은 벽 / 흙먼지·돌 파편 / 공기 일렁임. 전체 약 0.65초. (바닥 균열 레이어는 민경 확인 후 뺐다.)

## 바뀐 파일
| 파일 | 내용 |
|---|---|
| `1.Scripts/Player/Skill/PaladinSkillView.cs` | 칸 `qBlast` · `HandleTraitFx` 에 `PaladinAdvanceImpact` Activated 분기 → `PlayQBlast`(부채꼴 없으면 생략) |
| `1.Scripts/Effects/EffectCatalog.cs` · `9.ScriptableObject/Effects/EffectCatalog.asset` | `QBlast` 등록(프리웜) |
| `2.Prefabs/Player/Paladin/Player_Paladin.prefab` | `PaladinSkillView.qBlast` 연결(1줄) |
| SVN `50.Art/VFX/Common/Players/Player1/QSkill/QBlast/` | `FX_QBlast`(+`_Entry`) · 재질 10 · 텍스처 9 · 메시 6 |

## 원본 제작(MyProject)
- 빌드 메뉴 `Tools/VFX/Q Blast (팔라딘 진격의 충격) 빌드` → `Export/Player1/QSkill/QBlast/` · 미리보기 `Scenes/QBlastPreview.unity`(`QBlastTester` — 정면 팔라딘 + 2m·90° 부채꼴 가이드 + 허수아비 안 2·밖 1).
- 균열 레이어는 빌더에서도 뺐다. 🔴 빌더를 다시 돌리면 프리팹의 손 수정은 덮어써진다.

## 확인
- DebugTraitLab(Play): `PAL_4_Q_BLAST` 넣고 Q → 최대 시간 종료 → 끝 지점·진행 방향에 `FX_QBlast` 1개(배율 2) — 로그로 확인.
- [ ] 민경 Play: 화면에서 크기·방향·타이밍 · 돌파의 방패(PAL_2_DASH) 종료 · MPPM 클라 화면.

## SVN / git
- SVN: `50.Art/VFX/Common/Players/Player1/QSkill/QBlast/` 폴더 add.
- git: 위 코드·SO·프리팹 + 문서.
