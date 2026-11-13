# 로비 상단 플레이어 슬롯 — 아트 A안 (할 일 276)

3.LobbyScene `UI/Canvas/Panel_PlayerSlots` 의 `PlayerSlot_0~2`. 접속한 플레이어마다 원형 초상화·번호 배지·이름·상태 캡슐을 보여 준다.

## 원본
- 아트 = SVN `Assets/50.Art/UI/Lobby/Unity/Lobby_A/`(r683). png 13장 + `assembly.json`(부품 좌표) + `README`.
  - 초상화 중심 (0,0) 기준 디자인 단위, 부품 피벗 가운데. 부품은 디자인 단위 그대로 놓고 부모만 한 번 줄인다.
- 초상화 = `CharacterRoster` 의 `portrait` 원본 png 그대로 + `faceCrop`(`PortraitFaceCrop`, character-select-panel.md 의 크롭 항목).

## 계층 (저작 메뉴가 만든다)
`Tools/UI/Authoring/Lobby Player Slots (A안)` — `LobbyPlayerSlotsAuthoring`.
- 3.LobbyScene 이 활성 씬이고 **저장 안 한 변경이 없어야** 실행된다(끝에 씬을 저장한다).
- `Panel_PlayerSlots`: 켬(158 C8 에서 꺼 두었던 것을 다시 켬), 앵커 위 가운데, 위치 (0, −`PortraitCenterFromTop`), 스케일 `RootScale` = 0.6788173913(1920×1080 기준).
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
`LobbyUIController` 가 접속 목록이 바뀔 때 슬롯마다 부른다. `StateOf(connected, ready)`:

| 상태 | 초상화 | 링 | 캡슐·아이콘 | 이름 줄 | 글자색(번호·이름 / 상태) |
|---|---|---|---|---|---|
| 접속·준비 완료 | 캐릭터 얼굴 | ReadyRing | ready · 체크 | 캐릭터 이름 | 219 / 181 |
| 접속·준비 중 | 캐릭터 얼굴 | — | waiting · 점 | 캐릭터 이름 | 219 / 181 |
| 미접속 | 끔(바탕색만) | 점선 + '+' | disconnected · 선 | `UI.Lobby.SlotEmptyName` | 117 / 115 |

- 배지 = `OverheadPlayerLabel`(P{clientId+1}), 빈 칸은 P{슬롯 순서+1}.
- 문구 키(Strings 시트 행 없음 → ko 폴백): `UI.Lobby.SlotReady` 준비 완료 · `UI.Lobby.SlotWaiting` 준비 중 · `UI.Lobby.SlotDisconnected` 미접속 · `UI.Lobby.SlotEmptyName` 플레이어 대기.

## 알려진 한계
- Canvas 가 ConstantPixelSize(배율 1)라 0.6788 은 1920×1080 에서만 목업 크기와 정확히 같다.
- 목업 이름 줄은 "플레이어 01" 같은 플레이어 이름이지만, IPv4 모드엔 플레이어 이름이 없어 캐릭터 이름을 쓴다.
- 이전 판(원형 `Mask` 106 + `ResultGradeCircle` 바탕 + `Frame`·`ReadyMark`·`Label`)은 메뉴 실행 시 지워진다.

## 테스트
`Assets/Tests/EditMode/PortraitFaceCrop/Editor/LobbyPlayerSlotViewTests.cs` — 상태 매핑·문구 키·아이콘 자리(assembly.json 대조).
