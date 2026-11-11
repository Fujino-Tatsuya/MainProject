# 보스 처치 연출 — 캐릭터별 프로필 (할 일 267)

> 계획: [PLAN-boss-victory-character-profiles.md](../history/PLANS/PLAN-boss-victory-character-profiles.md) · 연출 전체: [PLAN-boss-victory-glitch.md](../../PLAN-boss-victory-glitch.md)

## 1. 구조

| 무엇 | 어디 | 캐릭터별? |
|------|------|-----------|
| 타임라인 전체(카메라 컷·처치자 평타·마커·끝 자르기·느려짐·보스 노드·화면 효과 세기 클립·TV 꺼짐·로비 전환) | `BossVictoryProfile.timeline` | ✅ |
| 백대쉬(거리·시간·곡선·보정 각·후 대기) | `BossVictoryProfile.backDash*` | ✅ |
| 대상별 시간 배율 4개(하던 동작·처치자 End 후·팀원·보스 End 후) | `BossVictoryProfile.*Scale` | ✅ |
| 처치 연출 스킬 칸 | `BossVictoryProfile.finisherSlot` | ✅ |
| 화면 효과 모양(호박색·주사선·찢김·보스 노이즈·CRT 색·셰이더)·벽 숨김 기준·보스 피격 애니 상태·마커 옵션(포커스 블렌드·숨김 대상) | `BossVictoryGlitchSettings` | ❌ 공용 |

- 에셋: 공용 `Assets/Resources/BossVictory/BossVictoryGlitchSettings.asset` · 프로필 `Assets/Resources/BossVictory/Profiles/BossVictoryProfile_{Default,Paladin,Gunner,Assassin}.asset`.
- **고르기**: 공용 설정의 `characterProfiles` 표(캐릭터 → 프로필). 캐릭터 = 기존 캐릭터 판별 `TutorialCharacter`(로스터 칸, `TutorialLocalPlayerInfo.ResolveCharacter`). 같은 캐릭터 두 줄이면 앞 줄. **표에 없거나 프로필이 비면 `defaultProfile`**, 그것도 비면 코드 기본값(경고 1회). 순수 함수 `BossVictoryProfileSelect.Select`.
- 처음 내용: 267 이전 설정 에셋 값을 그대로 복제(4개 모두 같은 내용). 기본 프로필 = 이전 연출.
- 프리팹 수정 없음. `PlayerVictoryFinisher`(어느 프리팹에도 안 붙어 있던 Variant 스킬 칸 컴포넌트)는 삭제 — 스킬 칸은 프로필.

## 2. 네트워크

새 RPC·NetworkVariable 없음. 각 피어가 `BossVictoryGlitchPresentation.Begin` 에서 복제된 `VictoryState.KillerClientId` 로 처치자 `Player` 를 찾고 → 그 캐릭터 → 프로필을 회차 시작 때 고정한다(늦은 참가자도 같은 길). 호스트의 로비 전환(타임라인 '로비 전환' 마커 → `PresentationFinished`)도 같은 프로필 시계. 처치자를 못 찾으면(나감 등) 기본 프로필.

## 3. 편집법 — 타임라인 창 `Tools/Boss Victory/처치 연출 타임라인`

- **도구 막대 캐릭터 드롭다운**(기본·팔라딘·거너·어쌔신) = 그 프로필을 편집. 옛 '미리보기 캐릭터'(스킬 클립 길이 표시)와 통합 — 기본은 팔라딘 몸으로 보여 준다.
- Play 중 연출이 돌면 **실제 처치자 캐릭터를 따라간다**(드롭다운 잠김, '실제 처치자').
- 표에 프로필이 없는 캐릭터를 고르면 기본 프로필이 보이고(고치면 기본이 바뀐다고 안내) '이 캐릭터 프로필 만들기(기본 복제)' 버튼.
- **노드 복사**: 클립·마커 우클릭 → '다른 캐릭터에 복사…' → 캐릭터 체크 → 복사. Ctrl/⌘+클릭으로 여러 노드를 골라 한 번에. 같은 시각으로:
  - 마커(백대쉬 시작·스킬 연출 시작·느려짐·보스 노드·로비 전환 등 하나뿐인 것) = **덮어쓰기**. 애니 클립 노드(스킬 연출 시작·보스 피격 애니)는 끝 자르기도 같이.
  - 클립(카메라 컷·화면 효과 구간·HUD 숨김) = **새로 추가**(카메라 컷 값·키 포함). 1개뿐인 트랙(TV 꺼짐·처치자 평타)은 덮어쓰기.
  - Undo 한 번(Ctrl+Z)에 전부 되돌림. 순수 함수 `BossVictoryTimeline.CopyMarkerFrom`·`CopyClipFrom`.
- 노드 인스펙터의 백대쉬·배율·스킬 칸은 지금 프로필 칸, 트랙 모양 값(색·노이즈 등)은 공용 설정 칸.
- 메뉴 `Tools/Boss Victory/캐릭터별 프로필 만들기 (267)` — 없는 프로필만 기본 복제로 만들어 표에 넣는다(덮어쓰지 않음).

## 4. 디버그 랩 (DebugBossVictory)

**F1 = 처치자(내 플레이어) 캐릭터 순환** 팔라딘 → 어쌔신 → 거너 — 간파·특성 랩의 `DebugCharacterCycle` 재사용. 이 씬은 빌드 목록에 있어(디버그 컴포넌트 격리 테스트 `DebugInterruptLabIsolationTests`) 씬에 두지 않고 랩이 실행 중에 붙인다(로스터 = NetworkManager 의 `NetworkLoadingFlowController`). 내 플레이어가 바뀌면 랩이 처음부터 다시 — 새 캐릭터 프로필로 재생.

## 4.1 노이즈 UV 수직 이동 노드(267 후속)

트랙 `NoiseScrollV`('노이즈 UV 수직 이동', 클립 = 구간). 클립마다 시작·길이·**시작 오프셋·끝 오프셋·이징**(`BossVictoryKeyCurve` 재사용 — 선형/ease in·out/in/out/계단).
- 움직이는 값 = 보스 노이즈 텍스처 V 오프셋 `_NoiseST.w`(= 기존 카메라 이동 누적 + 이 값). 새 셰이더 속성 없음.
- 시작 전 0 · 구간 안 시작 → 끝(이징) · 끝 뒤 끝 값 유지 · 클립끼리 더함. 노이즈 텍스처가 Repeat 라 1 = 한 장, 넘어가면 감긴다(따로 감기 옵션 없음).
- 시각 t 만으로 정해지는 값이라 스크럽·되감기 OK, 모든 피어 같은 화면(새 복제 없음). 노이즈가 보이려면 '보스 노이즈' 세기 클립이 켜져 있어야 한다.
- 프로필 노드 복사 = 클립이라 **추가**. 순수 함수 `BossVictoryClip.UvOffset`·`BossVictoryTimeline.NoiseScrollOffset`(EditMode 테스트 `BossVictoryTimelineTests` UV_*).

## 4.2 발밑 회색 원(246 복원, 267 후속)

ZZZ 글리치 교체(188597bd)로 옛 연출과 함께 빠졌던 원을 새 글리치 경로에 다시 구현(Legacy 재연결 아님).
- 트랙 `GroundCircle`('발밑 회색 원', 세기 키 클립) — **반경 = 설정 SO `groundCircleRadius` × 클립 세기**. 키 0 → 1 = 원이 커짐. 시각은 캐릭터별 프로필 타임라인, 모양(반경 6m·가장자리 1m·흑백 밝기 1)은 `BossVictoryGlitchSettings` 공용.
- 기본 클립 = 0.3초(노란 틴트 시작)부터 0.6초 smoothstep 으로 커져 5.6초까지 유지(옛 246: 주황 들어오는 순간부터 0.6초, 씬 끝까지).
- 옛 에셋(이 트랙 전 프로필)은 읽을 때 기본 클립을 한 번 넣는다(`SeedGroundCircle` — 숨은 표시 `groundCircleSeeded`, 지운 뒤엔 다시 안 넣음).
- 중심 = **카메라 바로 아래 바닥 점**(카메라 x·z, 높이 = 연출 시작 순간 처치자 발 높이) — 매 프레임 카메라를 따라간다(`BossVictoryTimeline.GroundCircleCenter`). '카메라 처치자로' 마커 전은 피어마다 게임 카메라가 달라 처치자 발밑(연출 시작 위치, 없으면 타격점)을 쓴다. 마커 뒤 카메라 = 타임라인 컷·처치자 포커스라 피어마다 같다. 원기둥(수평 거리만) — 셰이더가 `_CameraDepthTexture` 로 픽셀 월드 위치를 되살려 원 안 배경을 호박색 대신 흑백(원래 명도 × 밝기). 플레이어는 원래도 흑백, 보스 노이즈는 그 위에 그대로. 찢긴 UV 로 계산해 원도 같이 찢긴다.
- 네트워크: 복제된 처치자 위치·처치 시각과 타임라인으로 정해지는 카메라만 — 새 RPC 없음. 노드 복사 = 클립 추가.

## 4.3 카메라 두둥실(267 후속)

트랙 `CameraBob`('카메라 두둥실', 세기 키 클립) — 화면 세로 오프셋(화면 높이 %) = 세기 × 진폭 × e^(−감쇠·경과) × sin(2π(경과/주기 + 위상)), 클립끼리 합.
- 칸: 진폭(%)·주기(초)·시작 위상(0~1)·감쇠(1/초). 새 노드 기본 = 레퍼런스 측정값 0.22%·1.6초·위상 0.25·감쇠 0(분석: `_lane-artifacts/Work2/camera-bob/`).
- 적용 = 카메라 투영 행렬 세로 이동(`ApplyCameraBob`) — 카메라 위치·회전·컷·추적은 안 건드려 누적 없음, 끝나면 원래 투영. 시각 t 로 정해져 스크럽·모든 피어 같음(새 RPC 없음). 복사 = 클립 추가.
- 시차(원근 이동) 없음 — 화면 전체가 같이 오르내린다.
- 4개 프로필 기본 클립 = 0.3~5.6초, 들어올 때 0.2초·나갈 때 0.3초, 0.22%·1.6초·위상 0.25. 옛 에셋은 읽을 때 한 번 넣는다(`SeedCameraBob`, 숨은 표시 `cameraBobSeeded` — 지우면 다시 안 넣음).

## 5. 알려진 한계

- 프로필은 회차 시작 때 고정 — 연출 중 처치자 캐릭터가 바뀌어도(랩 F1) 그 회차는 옛 프로필, 랩이 바로 처음부터 다시 튼다.
- 끝 자르기 값은 클립 시간(초) 하나 — 기본 프로필을 여러 캐릭터가 쓰면 캐릭터마다 클립 길이가 달라 같은 값이 다른 프레임이다(캐릭터 프로필을 만들면 따로).
- 클립 노드 실측 표본(`BossVictoryClipTiming`)은 클립 이름 기준이라 프로필과 무관(같은 클립이면 공유).
- 클립 복사는 같은 자리에 이미 같은 클립이 있어도 하나 더 생긴다(중복 확인 안 함 — Undo 로 되돌림).
