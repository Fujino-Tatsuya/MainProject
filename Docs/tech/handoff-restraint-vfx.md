# 핸드오프 — 절제된 분노(ASN_2_RESTRAINT) VFX: 봉인 순간 + 단검 봉인 띠

> 2026-11-04 · 민경(Claude) · 브랜치 `feature/VFX` · 계획 [PLAN.md](../../PLAN.md) 맨 위(3차 결정).
> 판정·수치·네트워크 무변경 — 새 RPC·복제 칸 없음. 연출 컴포넌트 1개 + 카탈로그 칸 1개 + `Player_Assassin` 자식 1개.

## 기획 vs 구현
- 기획·구현 일치: 특성을 고르면 R(변신)·분노가 사라지고 일반 Q·E 가 강해진다. `AssassinState.IsRestrained` = `HasTrait(ASN_2_RESTRAINT)` — 특성 획득 행 복제라 **전 피어 같은 값**, 고르면 그 판 내내 켜짐.
- 원래 전용 연출이 없었다([trait-effect-events.md](trait-effect-events.md) §3.1 "모드 변경"). 이번에 봉인 순간·상시 띠를 더했다. Q·E 레이어(MyProject 에서 만든 관통 폭발·강타 은색 복제)는 **연결하지 않았다**(민경 결정).

## 동작 — `AssassinRestraintView`(`Player_Assassin/VFX/Restraint`)
| 언제 | 무엇 |
|---|---|
| `IsRestrained` false → true 를 본 순간(변신 중이면 변신 끝난 순간) | 발밑 `EffectCatalog.Assassin_Restraint_Seal`(사슬 3줄이 0.3~0.42초 훅 조여듦 → 버팀 → 0.58초 오라 폭발) + R 소용돌이 `Assassin_R_BladeVortex` 루프 |
| +0.42초(`sealLockTime`) | 소용돌이 해제 → 엔트리 outro `_End`(사슬에 갇혀 R 이 꺼짐) |
| +0.58초(`bandShowDelay`) | 양손 단검 띠 알파 페이드 인 0.4초(`bandFadeIn`) |
| 특성 해제 | 띠 페이드 아웃 0.4초(`bandFadeOut`), 진행 중 봉인 취소 |
| 처음 본 값(늦은 합류·재접속·오브젝트 재활성) | 봉인 없이 띠만 바로(페이드 없음) |
| 사망·다운 | 그대로(특성 유지) |

- **띠** = `FX_Restraint_DaggerBand_R/_L`(MeshRenderer 사슬 하나, 파티클 아님). 소켓 `Dagger_Weapon_R/L/VFX_EnhanceAura`(E 오라와 같은 곳)의 **자식**으로 처음 보일 때 만든다 → 단검 배율 1.25 를 그대로 받는다(메시는 배율 없는 소켓 공간).
  손마다 메시가 따로다 — 왼손 소켓은 Z 180° 돌아 있어 오른손 메시를 쓰면 위아래가 뒤집힌다.
- **페이드** = 재질 `_BaseColor` 알파를 MaterialPropertyBlock 으로 바꾼다(색은 재질 값 그대로), 0 이면 렌더러를 끈다.
- **재질** `M_Restraint_ChainBand` = URP `Unlit` 반투명(알파 블렌드 + 깊이 씀). 알파 1 이면 불투명처럼 보인다.
  ⚠ 이펙트 셰이더 `Shader_IntegratedEffect` 로 그리면 투명한 파랑으로 보였다(원인 미확정 — 알파를 제곱하는 반투명 셰이더). 띠에는 쓰지 말 것.

## 바뀐 파일
| 파일 | 내용 |
|---|---|
| `Player/Assassin/AssassinRestraintView.cs` | 새 연출 컴포넌트(위 표) |
| `Effects/EffectCatalog.cs`·`9.ScriptableObject/Effects/EffectCatalog.asset` | 칸 `Assassin_Restraint_Seal` |
| `Player_Assassin.prefab` | `VFX/Restraint` 자식 + 컴포넌트(소켓 2·띠 프리팹 2 연결) |
| SVN `50.Art/VFX/Common/Players/Player3/Restraint/` | 프리팹 3(`FX_Restraint_Seal`·`_DaggerBand_R/_L`) · 엔트리 `FX_Restraint_Seal_Entry` · 재질 6 · 텍스처 5 · 메시 3(`SM_Restraint_ChainRing.fbx`·`SM_Restraint_ChainHelix_R/_L`) |

## 원본 제작(MyProject)
- `Tools/VFX/Assassin Restraint (어쌔신 절제된 분노) 빌드`(`Assets/1.Scripts/Editor/RestraintVfxBuilder.cs`) → `Export/Player3/Restraint/{Crimson,Violet}/` · 미리보기 `Scenes/RestraintPreview.unity`(`RestraintTester` — 실제 어쌔신 모델·애니메이션, S 봉인 · T 켬/끔 페이드).
- 고른 색 = **Crimson**. 봉인 박자 상수 `SnapStart 0.3`·`SealLockTime 0.42`·`SealBurstTime 0.58`(본편 칸 기본값과 같다 — 바꾸면 둘 다).
- 나선 메시는 빌드 때 단검 FBX 를 재서 만든다(주성분 축 = 칼날 · 가장 넓은 칸에서 긴 쪽 = 칼날 · 단면 폭·두께로 타원 밀착 · 한 바퀴 17cm).

## 확인
- DebugTraitLab(호스트) 특성 넣기 → 0.25초 봉인·소용돌이 · 0.75초 `_End`·띠 알파 0.37(페이드 중) · 1.35초 알파 1 · 빼기 0.2초 알파 0.51 → 0.7초 0·렌더러 꺼짐. 띠 부모 = 양손 `VFX_EnhanceAura`, 월드 배율 1.25 — 로그.
- [ ] 민경 Play: 실제 특성 선택 화면에서 고르기 · 변신 중에 고르기(변신 끝난 뒤 봉인) · MPPM 클라 화면 · 탑다운에서 띠 읽힘.
- [ ] SVN add `Player3/Restraint/`.
