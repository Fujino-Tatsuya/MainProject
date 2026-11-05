# 핸드오프 — 그림자 회귀(ASN_2_RETURN) VFX: 출발점 그림자 단검 표식

> 2026-11-04 · 민경(Claude) · 브랜치 `feature/VFX` · 계획 [PLAN.md](../../PLAN.md) 맨 위.
> 판정·수치·네트워크 무변경. 연출 컴포넌트 1개 + `Player_Assassin` 자식 1개 + 출발점 클래스에 읽기 전용 2줄(은희 님 영역 — 아래).

## 기획 vs 구현
- 기획·구현 일치: Q 후 3초 안에 다시 누르면 처음 위치로 복귀 돌진(원래 Q 피해 60%), Q 쿨 +20%. 일반·변신 Q 모두. 창 = 첫 돌진 끝부터 3초, R·사망·씬 이동이면 폐기.
- 원래 전용 연출이 없었다([trait-effect-events.md](trait-effect-events.md) §3.1 "모드 변경"). 이번에 **돌아갈 자리 표식**을 더했다.

## 동작 — `AssassinShadowReturnView`(`Player_Assassin/VFX/ShadowReturn`)
| 언제 | 무엇 |
|---|---|
| Q 칸이 후속(복귀)으로 열린 게 보임(`IsFollowUpOpen(Main)`, 복제) + 원래 Q 가 이 피어에서 출발점을 새로 저장함 | 출발점에 `FX_ShadowReturn_Marker_Shadow`(바닥에 꽂힌 보라 실루엣 단검 + 테두리 + 바닥 그림자 웅덩이) 0.15초 페이드 인. +Z = 출발점 → 지금 플레이어 |
| 창 마지막 1초(근사) | 1 → 0.5 로 흐려지며 초당 4번 깜빡 |
| 복귀 돌진 시작(그 피어의 복귀 단계가 출발점을 가져감) | 0.2초 페이드 아웃 |
| 칸이 그냥 닫힘(만료·R 전환·사망) | 0.4초 페이드 아웃 |
- 전 피어가 각자 본다. 출발점 = `AssassinDashStrikeSkill.LocalReturnOrigin`(각 피어가 돌진 시작 위치를 저장 — 원래 있던 것).
- 남은 시간은 복제되지 않아 근사한다 — 창 끝 ≈ 칸 열림 + 준비(0.1, 칸 `dashPrepare`) + 돌진 시간(거리/속도) + 창 길이(원래 Q 의 `FollowUpWindowSeconds`).
- 페이드 = 재질 `_BaseColor` 알파 × 배율(MaterialPropertyBlock). 재질 = URP Unlit 반투명(실루엣 채움 · 앞면 컬 바깥 껍질 = 테두리 · 웅덩이).

## 은희 님께 — 바뀐 규칙 코드(연출용 읽기만)
| 파일 | 내용 |
|---|---|
| `Traits/Assassin/AssassinTraitRules.cs` `AssassinShadowReturnOrigin` | `TryPeek`(가져가지 않고 보기) · `Version`(저장마다 +1 — 만료 뒤 남은 옛 출발점으로 표식을 다시 내지 않게). 판정·저장·가져가기 동작 무변경 |

## 바뀐 파일
| 파일 | 내용 |
|---|---|
| `Player/Assassin/AssassinShadowReturnView.cs` | 새 연출 컴포넌트(위 표) |
| `Player_Assassin.prefab` | `VFX/ShadowReturn` 자식 + 컴포넌트(표식 프리팹 연결) |
| SVN `50.Art/VFX/Common/Players/Player3/ShadowReturn/` | `FX_ShadowReturn_Marker_Shadow.prefab`(민경이 단검 크기·위치 손본 그대로) · 재질 3(`SilhouetteFill`·`SilhouetteEdge`·`Pool`) · 텍스처 1(`T_ShadowReturn_Pool`). 단검 메시 = 기존 `50.Art/Char/assassin/Dagger_Weapon_R.fbx` |

## 원본 제작(MyProject)
- `Tools/VFX/Assassin Shadow Return (어쌔신 그림자 회귀) 빌드`(`Assets/1.Scripts/Editor/ShadowReturnVfxBuilder.cs`) → `Export/Player3/ShadowReturn/` · 미리보기 `Scenes/ShadowReturnPreview.unity`(`ShadowReturnTester` — Q 출발/복귀, C 질감, R 자동).
- ⚠ 다시 빌드하면 민경이 손본 `FX_ShadowReturn_Marker_Shadow` 의 단검 크기·위치가 빌더 값으로 덮인다 — 다시 돌리기 전에 빌더 값을 맞출 것(`Dagger` = 위치 (0.118, 0.732, 0.139) · 배율 2 · 회전 쿼터니언 (0.969, -0.026, -0.215, -0.119)).
- 고른 질감 = 그림자 실루엣(원래 텍스처 반투명안 `FX_ShadowReturn_Marker` 는 가져오지 않음).

## 확인
- DebugTraitLab(호스트) 특성 넣기 → Q: 0.1초 출발점(0,0,0)에 표식(알파 페이드 중) · 0.7초 최대(웅덩이 0.75·단검 0.55·테두리 0.45) → 다시 Q: 0.1초 페이드 중 · 0.5초 없음.
  쿨 뒤 Q 후 그대로: 창 끝(≈3.3초)에 칸 닫힘 → 0.4초 페이드 · 3.9초 없음 — 로그.
- [ ] 민경 Play: 실제 키 입력 · 변신 Q · 마지막 1초 깜빡 체감 · MPPM 클라 화면.
- [ ] SVN add `Player3/ShadowReturn/`.
