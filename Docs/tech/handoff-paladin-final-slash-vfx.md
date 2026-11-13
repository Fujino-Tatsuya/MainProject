# 팔라딘 평타 4타(막타) VFX 교체 — 단일 검기 + 궤적 연쇄 성광 (2026-11-13 민경)

## 무엇이 바뀌었나
- 기획 요청: 막타가 "여러 검기 동시"(`FX_DoubleSlash`, 검기 4줄)였던 것을 **검기 하나**로 줄이되 막타의 큰 데미지 느낌은 유지.
  색으로 차별화는 불가(명중 O / 빗맞음 X 를 이미 색으로 가른다) → **모양·리듬**으로.
- 결과: `FX_FinalSlash_Chain_O/X` — 검기 한 줄(Double 의 첫 줄 `Slash`) + 궤적을 처음 → 끝 순서로 훑으며 빛씨가 생기고,
  빛씨마다 짧게 뒤 4갈래 성광·작은 빛 고리·빛 알갱이가 터진다(성기사 — 무채색 HDR). 명중·빗맞음 둘 다 같은 연쇄(X 는 검기 색만 다름).
- 참고: Matthew Guz "Explosion Slash"(궤적을 따라 폭발이 연달아 터지는 구조) — 폭발 대신 섬광.

## 코드·데이터 변경 (git)
| 파일 | 변경 |
|---|---|
| `Assets/2.Prefabs/Player/Paladin/Player_Paladin.prefab` | `slashHit[3]` 소켓 effect → `FX_FinalSlash_Chain_O_Entry`, `slashMiss[3]` → `FX_FinalSlash_Chain_X_Entry`(소켓 위치·각도 그대로) |
| `Assets/1.Scripts/Effects/EffectCatalog.cs` + `9.ScriptableObject/Effects/EffectCatalog.asset` | 프리워밍 등록 `FinalSlash_Hit`·`FinalSlash_Miss` 추가(DoubleSlash 칸은 비교·되돌리기용으로 유지) |
| `Assets/1.Scripts/Player/DefaultAttackController.cs` | `slashHit`/`slashMiss` 툴팁 문구만(동작 변경 없음 — 은희 영역이라 기록) |

판정·네트워크 경로는 그대로다 — 재생 경로는 여전히 `DefaultAttackController.PlaySlashVfx`(Hit 이벤트, 전 피어 RPC).

### 평타 Hit 이벤트 시각 당김 (11-13 민경 — 은희 확인 필요)
`Garen_Default_Attack_1~4.anim` 애니 이벤트를 VFX 타이밍에 맞춰 앞당겼다. **Hit = 데미지 판정 시점도 겸하므로 데미지도 같이 빨라진다.**

| 클립 | 변경 |
|---|---|
| 1타 | Hit 0.333 → 0.233 |
| 2타 | ComboWindowOpen 0.146 → 0.133, Hit 0.3 → 0.146 |
| 3타 | Hit 0.3 → 0.233 |
| 4타 | Hit 0.267 → 0.15 |

`BasicAttackVerify` 기대 간격·`DefaultAttackChainRulesTests` 가 클립 시각을 읽으므로 테스트·서버 2차 검증 경고를 확인할 것.

## 아트 (SVN) — `Assets/50.Art/VFX/Common/Players/Player1/LeftClickSkill/FinalSlash/`
- `FX_FinalSlash_Chain_O/X.prefab` + `_Entry.asset`
- `Materials/` `M_FinalSlash_Star`·`M_FinalSlash_ChainRing`·`M_FinalSlash_ChainMote`
- `Textures/` `T_FinalSlash_Star`·`T_FinalSlash_Dot`(빌더가 구운 PNG)
- `Meshes/` `MS_FinalSlash_ChainPath`(검기 호 가운데선 — 처음 → 끝 한 방향 선분, 형태 Edge Loop 용)
- 기존 `FX_DoubleSlash_O/X` 는 남겨 둔다(되돌리기 = 위 두 소켓 effect 를 Double 엔트리로).

## 저작 원본
MyProject(`D:\김민경\유니티\My project`) — `Tools/FinalSlash/비교 버전 만들기`(`FinalSlashVariantsBuilder`)·값 `Assets/FinalSlash/FinalSlashSettings.asset`·
미리보기 씬 `Scenes/FinalSlashPreview`(키 4 = 연쇄 섬광). 최종 프리팹은 그 결과에 민경이 직접 손본 값(성광 0.5·고리 0.6·형태 위치/회전/크기)을 반영.

## 타이밍 값 (11-13 민경 Play 후 조정)
- `Chain_Seeds`: 0.22초 동안(Duration) 빛씨 6개(Rate 27.8, 0.036초 간격) — 형태 Edge·Loop, 속도 4.51.
  경로 `MS_FinalSlash_ChainPath` = 검기 호(120°) 시작 그대로, 끝을 호 길이의 0.224 만큼 늘린 147°.
  빛씨 k 는 호 시작에서 호 길이 × 0.194 × k — 첫 빛씨는 한 간격 안쪽(시작에 붙이면 궤적보다 튀어나와 보였다), 끝 빛씨는 호 너머.
  **궤적 따라 차례로 생기고, 검기가 사라지는 0.3초(Slash 수명)에 다 함께 터진다** — 수명 = 곡선(방출 시점 0 → 1 에 0.28 → 0.06초).
  검기 수명을 바꾸면 이 곡선도 같이 맞출 것.
- 터짐: `Pop_Star` 0.16초 · `Pop_Ring` 0.19초 · `Pop_Motes` 0.28~0.45초

## 확인할 것(Play)
- [ ] 4타 명중/빗맞음에서 검기 한 줄 + 궤적 따라 순서대로 섬광(끝에서 처음으로 되돌아가지 않음)
- [ ] MPPM 클라 화면에서도 같게
- [ ] 1~3타는 그대로(`FX_SingleSlash`)
