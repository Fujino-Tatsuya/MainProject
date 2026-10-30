# 보스 제압 칸 HUD (할 일 113 → 151, 2026-10-27 / 10-29 / 10-30)

> BossHealthHUD 는 경석 영역이라 PR 대신 이 문서로 공유한다. 제압 **규칙**(BossCounterProgress·TwentyThreeBoss)은 손대지 않았다.

## 151 후속 2(사용자 결정 10-30) — frame/fill 이중 구조 제거, 칸 = **fill 한 장**
- 사용자가 프리팹에서 직접 바꾼 것을 반영: 칸 원본(`SuppressSegmentTemplate`) 스프라이트 = `gauge_detection_cell_fill`
  (guid `849f20422f8eb6f41a9835581824eb93`), 자식 `Fill` 삭제, `BossHealthHUD.suppressSegmentFill` 필드 삭제.
  frame(`gauge_detection_cell_frame`)은 더 이상 쓰지 않는다 — 칸 테두리 역할은 슬롯 이미지 아래칸이 한다.
- 칸 줄 영역·N등분·간격·정수 픽셀은 아래 10-30 그대로. **남은 칸 = 그 칸 복제본 켬, 빈 칸 = 복제본 끔**
  (슬롯 아래칸 어두운 바탕이 보인다). 채움 방향(왼쪽부터 찬 칸, 오른쪽부터 빔)도 그대로.
- 아래 10-30·10-29 절의 "frame 바깥 테두리를 아래칸 바깥 테두리에 맞춘다"·"frame / fill 두 겹" 설명은 이 변경 전 기록이다.
  줄 영역 값(57,45 / 812×16)은 바꾸지 않았다.

## 151 후속(사용자 결정 10-30) — 칸 줄 = 슬롯 이미지 **아래칸** 자리
- 사용자 지시: "칸으로 바꾼 체간 게이지를 `Assets/50.Art/UI/HUD/slot_boss_info.png` 아래칸의 길이와 두께에 맞춘다."
  10-29 의 "줄 폭 = HP 바 폭(916)" 은 **폐기**했다(`suppressRowWidth`·HP 바 폭 읽기 제거 — 메커니즘은 이것 하나).
- **줄 영역 = 슬롯 이미지 원본 픽셀 영역**(`BossHealthHUD` **Suppress Row Slot Pixels**, 왼쪽 위 원점) → 슬롯 사각형 크기 ÷ 이미지 픽셀
  배율로 HUD 좌표로 바꿔 칸 줄의 가운데·폭·높이로 쓴다. 슬롯(`BossInfoSlot`) 크기를 바꿔도 칸 줄이 아래칸에 붙어 따라간다.
  - 계산 = 순수 함수 `BossSuppressCellLayout.SlotPixelsToRow`(EditMode 테스트 있음). 전제: 슬롯 Image Type **Simple**, **Preserve Aspect 끔**
    (이미지를 사각형에 꽉 늘려 그림 — 가로·세로 배율이 따로다). 바꾸면 영역이 어긋난다.
  - 칸 원본(`SuppressSegmentTemplate`)은 슬롯의 **직속 자식**이어야 한다(아니면 Awake 경고). 원본의 크기·자리는 런타임에 덮어쓰고
    스프라이트·색·모서리 배율만 쓴다. 프리팹의 원본 사각형(0,−38 / 845×27)은 에디터에서 보기 좋게 맞춰 둔 미리보기 값이다.
- **잰 값**(slot_boss_info.png 926×61, 알파 > 0 기준):
  | | 이미지 px | HUD(BossInfoSlot 964×103 가운데 기준) |
  |---|---|---|
  | 배율 | — | 가로 964/926 = 1.0410, 세로 103/61 = 1.6885 |
  | 아래칸 바깥 테두리 | x 57~868(폭 812), y 45~60(두께 16, 이미지 아래 끝까지) | 왼쪽 −422.66 ~ 오른쪽 +422.66(폭 845.32), 위 −24.48 ~ 아래 −51.5(두께 27.02) |
  | 가운데 | (463, 53) — 가로 정중앙 | (0, −37.99) |
  | 칸 줄(정수 px) | — | 줄 폭 845(왼쪽 끝 −422 ~ 오른쪽 끝 423), 칸 높이 27, 가운데 y −37.99 |
  - **바깥 테두리 기준인 이유**: 아래칸은 검은 테두리 3px(위 45~47·아래 58~60 행, 양끝 둥근 캡) + 안쪽 회색 10행(48~57)인 어두운 바다.
    칸 frame 스프라이트도 같은 모양(검은 테두리 3px + 회색 안쪽)이라 바깥 테두리끼리 겹쳐야 칸 테두리가 아래칸 테두리 자리에 온다.
    안쪽(채워질 자리)에 맞추면 칸 테두리가 하나 더 생겨 두 겹으로 보인다.
- 칸 폭 = (줄 폭 − 간격 8×(N−1)) / N, 정수 픽셀·오차는 줄 끝 흡수(10-29 그대로). 3칸 = **276/277/276**, 5칸 = **163/162/163/162/163**.
- 9-슬라이스 모서리: `Pixels Per Unit Multiplier` 1.25 → **0.74**(= 원본 높이 20px ÷ 칸 높이 27px, 10-29 와 같은 "원본 높이 ÷ 칸 높이" 규칙).
  border 6텍셀 → 화면 8.1px, 검은 테두리 3텍셀 → 4.05px(슬롯 아래칸 테두리 가로 3.1·세로 5.1px 사이). 칸 높이를 바꾸면 이 값도 같이 바꾼다.
- 미리보기 캡처(Edit 모드 프리뷰 씬, 3칸 2개 참·5칸 3개 참): `C:\UnityProject\_lane-artifacts\MainProject-WorkTree\151-suppress-cells-slot\`.

## 151 변경(사용자 결정 10-29) — 113 의 "이미지 비율 유지·64px 고정"을 바꿨다
- **줄 폭 고정 N등분 + 9-슬라이스.** 칸 수가 바뀌어도 칸 줄 전체 폭은 그대로고 칸 폭만 바뀐다(줄 폭 출처는 위 10-30 으로 바뀌었다).
  - 칸 경계는 정수 픽셀로 반올림하고, 반올림 오차는 마지막 칸 오른쪽 끝(줄 끝)에서 흡수한다.
  - 계산은 순수 함수 `BossSuppressCellLayout.CellWidth`·`PixelCell`, HUD 는 복제본 크기·위치만 넣는다.
- **늘어나도 안 찌그러지게** 칸 frame·fill 을 `RawImage` → **`Image`(Type Sliced)** 로 바꿨다(필드 타입이 `Graphic` 이라 연결은 그대로).
  - 텍스처 `gauge_detection_cell_frame.png`·`gauge_detection_cell_fill.png` 의 .meta(SVN): Default → **Sprite (2D and UI)**, Single,
    **Border 6px 사방**(둥근 모서리 + fill 의 사방 2px 여백이 테두리 안에 들어간다), 밉맵 끔, Alpha Is Transparency, Clamp, Non-Power-of-2 = None
    → 원본 **76×20** 그대로 쓴다(예전 Default 는 64×16 으로 줄여 들어왔다). guid 는 그대로다.
  - fill 은 frame 과 같은 캔버스 크기라 같은 사각형·같은 border 로 겹치면 여백이 그대로 맞는다.
  - 이 두 텍스처를 쓰는 곳은 BossHealthHUD.prefab 뿐이다(AssetDatabase 의존성 검색).

## 구조
- 칸 하나 = **fill 한 장**(원본 복제본). 남은 칸 = 켬, 빈 칸 = 끔(슬롯 아래칸 바탕이 보임). frame 없음(151 후속 2).
- 칸 수 변화·갱신 경로: `Update` → `SetSuppressSegments(total, remaining)` → 칸 수가 바뀔 때만 `BuildSuppressSegments`(영역·폭 재계산).
- 배치·표시 규칙은 순수 함수 `VeyTrace.UI.BossSuppressCellLayout`(`Assets/1.Scripts/UI/Combat/SuppressCells/`, 별도 asmdef),
  EditMode 테스트 `Assets/Tests/EditMode/BossSuppressCells/`.

## 프리팹에서 조절하는 곳 — `Assets/2.Prefabs/UI/BossHealthHUD.prefab`
| 조절 | 위치 |
|---|---|
| 칸 줄 자리·폭·두께 | 루트 `BossHealthHUD` **Suppress Row Slot Pixels**(슬롯 이미지 원본 px, 왼쪽 위 원점 — 지금 57,45 / 812×16) |
| 기준 슬롯 이미지 | 루트 `BossHealthHUD` **Suppress Row Slot**(= `BossInfoSlot`) |
| 칸 사이 간격 | 루트 `BossHealthHUD` **Suppress Segment Gap**(px, 지금 8) |
| 칸(fill) 이미지 | `BossInfoSlot/SuppressSegmentTemplate` 의 Image **Source Image**(Sliced, 지금 `gauge_detection_cell_fill`). 모서리 크기는 Pixels Per Unit Multiplier(0.74) |

- 원본은 꺼 둔 채 둔다(런타임 복제본 `SuppressSegment_0..n` 만 보인다). 원본 아래 자식은 두지 않는다.

## Play 확인 항목
- 23호 전투: 칸 3개(276/277/276)가 슬롯 아래칸에 정확히 겹치는지 — 양끝 둥근 캡·위아래 테두리가 아래칸 테두리와 맞고, 칸 사이 8px 로 아래칸 바탕이 보인다.
  3/3 → 2/3 → 0/3 이 오른쪽부터 칸이 꺼지는지(꺼진 자리에 아래칸 바탕). 둥근 모서리가 늘어나 보이지 않는지.
- 다른 칸 수: No23 BossDataSO 의 `suppressSegmentCount` 를 바꿔 Play 하면 칸 폭이 자동으로 바뀐다(줄 끝은 그대로).
- 해상도·CanvasScaler 배율이 1 이 아닐 때 칸 경계가 번져 보이지 않는지(정수 px 는 캔버스 단위 기준이다).
