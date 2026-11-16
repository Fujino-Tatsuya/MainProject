# 로비 상단 플레이어 슬롯 — 아트 A안 (할 일 276)

3.LobbyScene `UI/Canvas/Panel_PlayerSlots` 의 `PlayerSlot_0~2`. 접속한 플레이어마다 원형 초상화·번호 배지·이름·상태 캡슐을 보여 준다.

## 원본
- 아트 = SVN `Assets/50.Art/UI/Lobby/Unity/Lobby_A/`(r683). png 13장 + `assembly.json`(부품 좌표) + `README`.
  - 초상화 중심 (0,0) 기준 디자인 단위, 부품 피벗 가운데. 부품은 디자인 단위 그대로 놓고 부모만 한 번 줄인다.
- 초상화 = `CharacterRoster` 의 `portrait` 원본 png 그대로 + `faceCrop`(`PortraitFaceCrop`, character-select-panel.md 의 크롭 항목).

## 계층 (저작 메뉴가 만든다)
`Tools/UI/Authoring/Lobby Player Slots (A안)` — `LobbyPlayerSlotsAuthoring`.
- 3.LobbyScene 이 활성 씬이고 **저장 안 한 변경이 없어야** 실행된다(끝에 씬을 저장한다).
- `Panel_PlayerSlots`: 켬(158 C8 에서 꺼 두었던 것을 다시 켬), 앵커 위 가운데, 위치 (0, −`PortraitCenterFromTop`), 스케일 `RootScale` = 0.6788173913(1920×1080 기준) + `ScreenFitScale`(없으면 붙임, 아래 "해상도 대응").
- 슬롯 간격 245(초상화 중심). 각 슬롯 자식은 지우고 새로 만든다 — 슬롯 GameObject·`LobbyPlayerSlotView`·`LobbyUIController.slots` 연결은 유지.

| 자식(뒤 → 앞) | 위치 | 크기 | 비고 |
|---|---|---|---|
| FrameBase | (0.402, −2.4225) | 157 | 접속/미접속 프레임 교체 |
| PortraitMask / CharacterPortrait | (0.402, −0.4225) | 129 | `Mask`(Show Mask Graphic) — 바탕색 접속 (43,47,52)·미접속 (31,33,35). 초상화는 자식, faceCrop 으로 크기·위치 |
| ReadyRing | (−0.098, 0.0775) | 140 | 준비 완료만 |
| EmptyDashedRing · EmptyPlus | 같은 자리 | 128 · 32 | 미접속만 |
| PlayerNumberBadge / PlayerNumberTMP | (−54.598, 62.0775) | 47×32 | Heavy 15 |
| PlayerNameTMP | (0, −92) | 240×30 | Medium **Bold** 20 — Heavy 정적 에셋엔 '팔·딘·쌔' 가 없음 |
| StatusCapsule / StatusIcon · ReadyStatusTMP | (0.402, −131.4225) | 153×41 | 캡슐·아이콘 상태별 교체. 아이콘 자리 = `LobbyPlayerSlotView.StatusIconLayout`, 글자 Heavy 18 (9.6, 0.42) |

## 상태 (`LobbyPlayerSlotView.SetState`)
`LobbyUIController` 가 접속 목록이 바뀔 때(`LobbySession.StateChanged`)와 준비 결과가 바뀔 때 슬롯마다 부른다. `StateOf(connected, ready)`:

### "준비 완료" 기준 (사용자 결정 11-13) — `LobbySlotReadyRules.IsReady`
- **브리핑이 끝난 뒤 그 플레이어가 런치패드(출격 집결 구역) 안**일 때만 준비 완료. 브리핑 중·브리핑 끝났지만 구역 밖·쓰러짐 = 준비 중. 미접속은 그대로.
  - 브리핑 끝 = 튜토리얼 파사드 `LobbyTutorialState.ActiveGatherPointId == LobbyGatherPoint.Sortie`(단계 L40_SortieGather, 복제 스냅샷 `stepIndex` 에서 계산).
  - 구역 안 = `LobbyTutorialState.ActiveGatherZone.ContainsPoint(본체 위치)` + `!PartyGatherZone.IsIncapacitated(player)` — 서버 집결 판정과 **같은 함수**를 각 피어가 복제된 위치·체력·생명 상태로 돌린다.
  - 출격 확정 뒤(`LaunchCommitted`, L41 로딩) = 접속자 전원 준비 완료(확정 = 전원 집결이었음, 이후 접속 거절).
- **새 RPC·NetworkVariable 없음.** 서버 `PartyGatherZone` 은 인원 수(`gatheredCount`)만 복제하고 clientId 별 집결 여부는 복제하지 않는다 — 그래서 전 피어가 위 함수로 각자 계산한다.
  원격 플레이어 위치는 NetworkTransform 복제라 왕복 지연만큼 늦게 바뀔 수 있다(서버 집결 n/N 과 한 순간 다를 수 있음).
- 다시 계산 = `LobbyUIController.Update` 0.1초마다(출격 집결·확정 뒤에만 스폰 목록을 훑음), 결과 비트가 바뀔 때만 슬롯 갱신.
- 옛 기준 `LobbyPlayerEntry.Ready`(LobbySession 준비 토글 — 씬에 Ready 오브젝트가 없어 qa/LobbyDemo 하네스만 켬)는 **슬롯 표시에 더 쓰지 않는다(대체).** 하네스의 카운트다운 동작은 그대로.
- 튜토리얼 진행기가 없는 씬(`IsAvailable == false`)에선 늘 준비 중.

| 상태 | 초상화 | 링 | 캡슐·아이콘 | 이름 줄 | 글자색(번호·이름 / 상태) |
|---|---|---|---|---|---|
| 접속·준비 완료 | 캐릭터 얼굴 | ReadyRing | ready · 체크 | 캐릭터 이름 | 219 / 181 |
| 접속·준비 중 | 캐릭터 얼굴 | — | waiting · 점 | 캐릭터 이름 | 219 / 181 |
| 미접속 | 끔(바탕색만) | 점선 + '+' | disconnected · 선 | `UI.Lobby.SlotEmptyName` | 117 / 115 |

- 배지 = `OverheadPlayerLabel`(P{clientId+1}), 빈 칸은 P{슬롯 순서+1}.
- 문구 키(Strings 시트 행 없음 → ko 폴백): `UI.Lobby.SlotReady` 준비 완료 · `UI.Lobby.SlotWaiting` 준비 중 · `UI.Lobby.SlotDisconnected` 미접속 · `UI.Lobby.SlotEmptyName` 플레이어 대기.

## 해상도 대응 (할 일 283 후속, 2026-11-13)
- 로비 `UI/Canvas` 는 ConstantPixelSize(배율 1) 그대로 두고 **슬롯 패널만** `ScreenFitScale`(`Assets/1.Scripts/UI/ScreenFitScale.cs`)이 Play 중 맞춘다 — 씬 값(스케일 0.6788·위치 y −78.98) × min(캔버스 가로/1920, 캔버스 세로/1080)(CanvasScaler Expand 와 같은 규칙). 1920×1080 은 이전과 픽셀 단위로 같고, 다른 해상도에서도 화면 가로·세로 대비 같은 비율(1920×1080 기준 가로 23%)·같은 자리(위 가운데)다. 21:9 는 세로 기준이라 가로 점유율이 줄고, 4:3·16:10 은 가로 기준이라 위아래 여백이 는다.
- 캔버스 스케일러를 바꾸지 않은 이유: 같은 캔버스의 `Button_ShowLastResult`·`Slot_*` 슬롯들도 ConstantPixelSize 로 저작돼 있어 스케일러를 바꾸면 그쪽 크기·위치가 같이 바뀐다. 슬롯 패널을 별도 루트 캔버스로 빼는 안은 계층 이동·정렬 순서가 바뀌어 제외. 캔버스 크기를 캔버스 단위로 읽으므로 나중에 캔버스가 Scale With Screen Size 로 바뀌어도 배율이 1 로 돌아와 이중으로 커지지 않는다.
- 편집 모드에선 아무것도 안 한다(Game 뷰 크기로 씬 값이 바뀌어 저장되는 것 방지). 창 크기가 바뀐 뒤 1프레임 늦게 따라간다(LateUpdate 에서 캔버스 크기 변화 감지).
- 빈 슬롯 이름 줄 겹침 확인: 씬 기본 상태 1920×1080 렌더에서 "플레이어 대기" 글자 윤곽(y 930.8~946.4, 화면 아래 원점)과 프레임(946.1~1052.7, 투명 여백 포함)·점선 링(957.6~) 은 겹치지 않는다 — 글자 중심 y −92 = assembly.json `glyphCenterRelativeToPortrait` 그대로.

## 알려진 한계
- 같은 `UI/Canvas` 의 `Button_ShowLastResult`(320×100 고정 픽셀)·`Slot_*` 안 내용은 여전히 ConstantPixelSize — 저해상도에서 크고 4K 에서 작다(283 후속에서 목록만 보고, 안 고침).
- 목업 이름 줄은 "플레이어 01" 같은 플레이어 이름이지만, IPv4 모드엔 플레이어 이름이 없어 캐릭터 이름을 쓴다.
- 이전 판(원형 `Mask` 106 + `ResultGradeCircle` 바탕 + `Frame`·`ReadyMark`·`Label`)은 메뉴 실행 시 지워진다.

## 테스트
`Assets/Tests/EditMode/PortraitFaceCrop/Editor/LobbyPlayerSlotViewTests.cs` — 상태 매핑·준비 기준(`SlotReadyRules` 6케이스)·문구 키·아이콘 자리(assembly.json 대조)·`ScreenFitScale.Factor`(9개 해상도 중 7)·씬 슬롯 패널 배선(283 후속).
