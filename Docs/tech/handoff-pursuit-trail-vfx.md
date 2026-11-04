# 핸드오프 — 추격의 궤적(ASN_4_Q_TRAIL) VFX: 지나온 길을 다시 베는 줄 + 바닥 자국

> 2026-11-04 · 민경(Claude) · 브랜치 `feature/VFX` · 계획 [PLAN.md](../../PLAN.md) 맨 위(재제작 4까지).
> 판정·수치·네트워크 무변경 — 이미 있던 특성 연출 사건(`TraitSkillFx`)만 구독한다.

## 기획 vs 구현
- 기획·구현 일치: Q 가 지나간 경로에 0.15초 뒤(÷ 공격속도) 원래 Q 피해 25% 추가 피해, 그림자 회귀 왕복 각각. 일반·변신 Q 모두.
- 서버가 판정 순간 `TraitSkillFx` `AssassinPursuitTrail` `Activated`(빗나가도, Pos = 선분 시작 · End = 선분 끝)를 공용 RPC 로 전 피어에 보낸다.

## 동작 — `AssassinSkillView.HandleTraitFx`
- 이 어쌔신의 `Activated` 를 받으면 **선분 시작(= 원래 Q 일섬과 같은 출발점·방향)**에 `EffectCatalog.Assassin_Q_PursuitTrail`(`FX_PursuitTrail`)을
  **Z 배율 = 선분 길이(= 실제 이동 거리)**로 한 번 — 원래 Q 일섬(`AssassinSkillView.EndPierce` 의 `PierceLine`)과 같은 규칙(루트 = 출발점, +Z 0~1m).
  선분이 `AssassinTrailRules.MinLength` 미만이면 생략. 대상별(`Hit`) 연출 없음.
- 서버 판정과 같은 순간(Q 종료 + 지연 ÷ 공격속도) 나온다 — 연출 쪽 지연 없음.

## `FX_PursuitTrail` 구성(민경 수정본 — 레이어 전부 z 0.5 중심, 출발점 → 끝을 덮음)
| 레이어 | 높이 | 원본 | 비고 |
|---|---|---|---|
| `Decal_1`·`Decal_5` | 바닥(0.03) | Piloto `Sword of the Gods` 의 `Ground_decal1/5_pbr_alpha` | 사각형 메시 입자(Local)로 바꿔 **공중 줄과 같은 길이로 늘어남**, 폭 0.5 · 0.8~1.6초 |
| `Cut_Scar`·`Cut_Glow`·`Cut_Core` | 1.0 | 좌클릭 적중 `BM Assassin_SlashImpact` 구성을 변형 | 경로 길이 · 원본보다 가늘고 짧게(날카롭게) |
| `Cut_Flash` | 1.0 | 같은 원본 빌보드 섬광 | 경로 가운데 |
| `HotFlash`·`HotFlash_Wide` | 1.0 | 새로(섬광 줄 텍스처 `RainbowFlarenomipmap`) | **URP 파티클 가산 + HDR 세기(×8 / ×3)** — 0.05초에 경로 전체가 번쩍(블룸 필요) |
- 재질: `Shader_IntegratedEffect` 쓰지 않음(알파 제곱으로 투명·파랗게 보임). Piloto UberFXSG(원본 재질) + URP 파티클 Unlit(섬광).
- 엔트리 `FX_PursuitTrail_Entry` 수명 1.65초(바닥 자국 최장 1.6초).

## 바뀐 파일
| 파일 | 내용 |
|---|---|
| `Player/Assassin/AssassinSkillView.cs` | `OnEnable`/`OnDisable` 에서 `TraitSkillFx.Raised` 구독 · `HandleTraitFx` |
| `Effects/EffectCatalog.cs`·`.asset` | 칸 `Assassin_Q_PursuitTrail` |
| SVN `50.Art/VFX/Common/Players/Player3/PursuitTrail/` | `FX_PursuitTrail.prefab` · `FX_PursuitTrail_Entry.asset` · 재질 2(`M_PursuitTrail_HotFlash`·`_HotFlashWide`). 옛 버전 재질 5·텍스처 3·메시 2 는 지웠다(svn delete 필요) |
| SVN `50.Art/VFX/_Materials/PilotoStudio/` | 새로: `Ground_decal1_pbr_alpha.mat` |
| SVN `50.Art/VFX/Textures/PilotoStudio/` | 새로: `GroundDecal1_Dis/_Norm/_diff.png` · `GroundRadial_oc.png` · `RainbowFlarenomipmap.png` |

## 원본 제작(MyProject)
- 빌더 `Tools/VFX/Assassin Pursuit Trail (어쌔신 추격의 궤적) 빌드`(`Assets/1.Scripts/Editor/PursuitTrailVfxBuilder.cs`) · 미리보기 `Scenes/PursuitTrailPreview.unity`(Q 종료 0.3초 뒤 재생 — 임의 값).
- ⚠ 최종 프리팹은 **민경 수정본 + 손질**(공중 줄 z 0.5 · 바닥 자국 늘이기)이다. 빌더를 다시 돌리면 덮어쓰인다 — MyProject `Export/Player3/PursuitTrail/FX_PursuitTrail.prefab` 은 MainProject 와 같은 파일로 맞춰 두었다.

## 확인
- DebugTraitLab(호스트) 특성 넣기 → Q: `Activated` 시작 (0,0,0) → 끝 (3.67,0,3.39) · 같은 순간 `FX_PursuitTrail` 루트 = 시작점 · Z 배율 5.0 · 모든 레이어 중심 = 선분 가운데(1.84, ·, 1.70) · 바닥 자국 2 켜짐 — 로그.
- [ ] 민경 Play: 실제 Q·변신 Q·그림자 회귀 왕복 · 섬광(블룸) · MPPM 클라.
- [ ] SVN: 위 추가·삭제.
