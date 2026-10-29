# 보스 제압 칸 HUD (할 일 113 → 151, 2026-10-27 / 10-29)

> BossHealthHUD 는 경석 영역이라 PR 대신 이 문서로 공유한다. 제압 **규칙**(BossCounterProgress·TwentyThreeBoss)은 손대지 않았다.

## 151 변경(사용자 결정 10-29) — 113 의 "이미지 비율 유지·64px 고정"을 바꿨다
- **줄 폭 고정 N등분 + 9-슬라이스.** 칸 수가 바뀌어도 칸 줄 전체 폭은 그대로고 칸 폭만 바뀐다.
  - 줄 폭 = **HP 바(`hpFill`) 폭을 읽는다**(지금 916px) → 바로 위 HP 바와 양끝이 맞는다. 예전 게이지 폭(806px)은 HP 바보다
    좁아 양끝이 어긋나서 쓰지 않았다. HP 바 크기를 바꾸면 칸 줄도 따라간다. 따로 정하려면 `BossHealthHUD` 의 **Suppress Row Width**(0 = HP 바 폭).
  - 칸 폭 = (줄 폭 − 간격×(N−1)) / N, 간격 = Suppress Segment Gap(8). 칸 높이 = 원본 높이(16) 그대로.
  - 칸 경계는 정수 픽셀로 반올림하고, 반올림 오차는 마지막 칸 오른쪽 끝(줄 끝)에서 흡수한다. 예: 3칸 = 300px×3, 5칸 = 177/177/176/177/177.
  - 계산은 순수 함수 `BossSuppressCellLayout.CellWidth`·`PixelCell`, HUD 는 복제본 `sizeDelta.x`·위치만 넣는다.
- **늘어나도 안 찌그러지게** 칸 frame·fill 을 `RawImage` → **`Image`(Type Sliced)** 로 바꿨다(필드 타입이 `Graphic` 이라 연결은 그대로).
  - 텍스처 `gauge_detection_cell_frame.png`·`gauge_detection_cell_fill.png` 의 .meta(SVN): Default → **Sprite (2D and UI)**, Single,
    **Border 6px 사방**(둥근 모서리 + fill 의 사방 2px 여백이 테두리 안에 들어간다), 밉맵 끔, Alpha Is Transparency, Clamp, Non-Power-of-2 = None
    → 원본 **76×20** 그대로 쓴다(예전 Default 는 64×16 으로 줄여 들어왔다). guid 는 그대로다.
  - `Pixels Per Unit Multiplier` 1.25 = 원본 높이 20px 을 칸 16px 에 맞춘 모서리 크기(예전 64×16 축소와 같은 0.8배).
  - fill 은 frame 과 같은 캔버스 크기라 같은 사각형·같은 border 로 겹치면 여백이 그대로 맞는다.
  - 이 두 텍스처를 쓰는 곳은 BossHealthHUD.prefab 뿐이다(AssetDatabase 의존성 검색).

## 구조
- 칸 하나 = **frame(바닥) + fill(위)** 두 겹. 남은 칸 = fill 켬, 빈 칸 = fill 끔(frame 만 보임).
- 칸 수 변화·갱신 경로: `Update` → `SetSuppressSegments(total, remaining)` → 칸 수가 바뀔 때만 `BuildSuppressSegments`(폭 재계산).
- 배치·표시 규칙은 순수 함수 `VeyTrace.UI.BossSuppressCellLayout`(`Assets/1.Scripts/UI/Combat/SuppressCells/`, 별도 asmdef),
  EditMode 테스트 `Assets/Tests/EditMode/BossSuppressCells/`.

## 프리팹에서 조절하는 곳 — `Assets/2.Prefabs/UI/BossHealthHUD.prefab`
| 조절 | 위치 |
|---|---|
| 칸 줄 폭 | 루트 `BossHealthHUD` **Suppress Row Width**(0 = HP 바 폭 916) |
| 칸 높이 | `BossInfoSlot/SuppressSegmentTemplate` RectTransform **Height**(16). Width 는 런타임에 덮어쓴다 |
| 칸 줄 위치 | 같은 오브젝트 **Pos X·Pos Y**(줄 가운데. 지금 0, -22.5 — Pos X 는 HP 바와 같게 둔다) |
| 칸 사이 간격 | 루트 `BossHealthHUD` **Suppress Segment Gap**(px, 지금 8) |
| frame / fill 이미지 | `SuppressSegmentTemplate` / `SuppressSegmentTemplate/Fill` 의 Image **Source Image**(Sliced). 모서리 크기는 Pixels Per Unit Multiplier |

- 원본은 꺼 둔 채 둔다(런타임 복제본 `SuppressSegment_0..n` 만 보인다). Fill 을 원본 직속 자식이 아닌 곳으로 옮기면 경고 로그 후 frame 만 보인다.

## Play 확인 항목
- 23호 전투: 칸 3개가 HP 바 양끝에 맞춰 300px 씩 깔리고, 3/3 → 2/3 → 0/3 이 오른쪽부터 fill 이 꺼지는지. 둥근 모서리가 늘어나 보이지 않는지.
- 다른 칸 수: No23 BossDataSO 의 `suppressSegmentCount` 를 바꿔 Play 하면 칸 폭이 자동으로 바뀐다(줄 끝은 그대로).
