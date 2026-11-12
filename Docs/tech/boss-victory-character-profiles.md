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

## 4.4 배경 글자 RECTIFY(267 후속)

트랙 `Rectify`('배경 글자 RECTIFY', 세기 키 = 알파). 기본 클립 = 0.3초부터 0.1초 동안 들어와 4.5초(TV 꺼짐 시작 — 캡처에 남음)까지. 옛 에셋은 한 번 시드(`rectifySeeded`).
- 모양(공용 `BossVictoryGlitchSettings`): 폰트 `rectifyFont` = SVN `Assets/50.Art/UI/Fonts/JalnanGothicTTF.ttf`(guid ec558e14…) · 글자 `RECTIFY` · 폭 = 화면 너비 × 1.06(양끝 조금 잘림) · 대문자 높이 가운데 = 뷰포트 0.55(위에서 45%) · 기울기 0° · 흰 외곽선(대문자 높이 4%), 안은 비움.
- 렌더: 켤 때 TMP 동적 SDF 아틀라스(512² Alpha8, 글자 7개)를 한 번 만들고 끌 때 버린다(`BossVictoryRectifyText`). 글리치 합성 셰이더가 글자마다 화면 사각형·아틀라스 UV 로 SDF 외곽선만 그리고 **플레이어·보스 마스크가 0 인 곳에만** 섞는다 → 인물·보스 뒤, 벽·바닥 앞(별도 패스·카메라 없음). 찢긴 UV 라 같이 찢기고, 들어오는 동안(알파 < 1) 가로줄이 알파 비율만큼 무작위로 켜져 찢기듯 나타난다(0.32초 찢김 펄스와 겹침).
- 시각 t 만으로 정해져 스크럽·모든 피어 같음(새 RPC 없음). 복사 = 클립 추가.
- 🔴 폰트 `.meta` 가 SVN 에 아직 안 올라가 있으면 PC 마다 guid 가 달라 참조가 끊긴다(글자만 안 보이고 경고) — `.meta` SVN 커밋 필요.

## 4.5 RECTIFY 안쪽 색 — Ring Wipe Fill(267 후속)

트랙 `RectifyFill`('RECTIFY 안쪽 색', 세기 = 채움 불투명도), 기본 클립 0.4~4.5초(옛 에셋 1회 시드 `rectifyFillSeeded`). 사양 = `_lane-artifacts/Work2/rectify-fill/SPEC-ring-wipe-fill.md` + 사용자 지시(중심·간격).
- 링 i(0 마젠타 #FF2DB4 · 1 블루 #3A6BFF · 2 그레이 #8A8A8A)는 클립 시작 뒤 i × 4/60초에 공통 중심(단어 사각형 UV (0.46, 0.5))에서 시작 반지름(단어 높이 0.15, `rectifyFillStartRadius`)으로 생겨, 크기 그대로 페이드 0.15초(알파 0→1) → 불투명해진 뒤 시작 반지름 → Rmax 로 easeOutCubic 확장(0.6초). 간격은 생성 시각 기준. 나중 링이 위(painter's order) — 약 0.88초 뒤 그레이로 고정, 반복 없음. 첫 링 전은 비어 있음(흰 외곽선만).
- 좌표 = 단어 사각형(글자 사각형 합집합) UV, 거리 단위 = 단어 높이(가로에 단어 종횡비 보정). Rmax = 중심에서 네 모서리까지(+경계, 다 덮음). 경계 0.01(픽셀보다 작으면 fwidth AA).
- 링 안 방사형 그라데이션: 색 = lerp(색, 색 × (1 − 0.35), d / r), r = 현재 반지름 — 중심 원래 색 → 가장자리 어둡게.
- **4번째 흰 원**(`rectifyWhite*`, 링 3개와 값 분리): 클립 시작 뒤 0.2초(`rectifyWhiteStart`)에 생겨 같은 순서(시작 반지름 → 페이드 → easeOutCubic 확장)로 퍼진다. 중심·시작 반지름·페이드·확장·그라데이션 따로.
- **주황 채움**(`rectifyOrange*`): 흰 원이 다 퍼진 뒤 `rectifyOrangeHold`(0.05초) 머물고, 레퍼런스(raw 12~23 프레임)처럼 가로줄(4px)마다 들쭉날쭉한 경계가 오른쪽 → 왼쪽으로 지나가며(0.1초, 들쭉날쭉 단어 폭 0.2) 흰색을 불투명한 주황으로 바꾼다. 색 = 왼쪽 #FFA53A → 오른쪽 #F8E862 보간(세기 `rectifyOrangeGradient`). 좌→우 보간은 주황 채움에만 — 링(마젠타·블루·그레이)은 사양 단색 + 방사형 그대로.
- 블룸: 글리치 합성은 `AfterRenderingTransparents`(후처리 전)라 RECTIFY 도 볼륨 블룸을 받지만, 값이 0~1(LDR)이라 맵 볼륨 threshold(0.9~1.25)를 거의 못 넘어 번짐이 없다.
- 순수 함수 `BossVictoryRectifyFill`(링 상태·흰 원·주황 진행·와이프·색·단어 사각형·Rmax) → 셰이더가 글자 SDF 마스크에 그린다 — `rectifyFillCoversOutline`(기본 켬, 11-12 사용자 요청)이면 마스크를 외곽선 바깥 경계(sd = −외곽선 반두께, 바깥 AA 유지)까지 넓혀 색이 지나간 자리는 외곽선도 같은 색(그라데이션 포함), 흰 외곽선은 색이 아직 닿지 않은 만큼(1 − 덮인 정도)만 남는다(첫 링 전 = 흰 외곽선만). 끄면 예전처럼 안쪽만·외곽선 흰색. 시각 t 만으로 정해지고 새 RPC 없음, C# 순수 함수 변경 없음. 안쪽 가는 가로 주사선 칸(간격 화면 높이 0.3%, `rectifyScanlineDark`) — 11-12 사용자 요청으로 기본 0(끔), 칸은 다시 켤 여지로 남김. 덮기 끔일 때만 흰 외곽선이 그 위.
- 모양 값은 공용 설정 `rectifyFill*`·`rectifyScanline*`. 시각 t 로 결정 — 스크럽·피어 동일, 새 RPC 없음. 복사 = 클립 추가.

## 4.6 CRT 주사선 = 벽·하늘에만(267 후속)

글리치 합성의 CRT 주사선(트랙 '주사선')을 바닥에서 뺀다. 픽셀마다 깊이(`_CameraDepthTexture`, 찢긴 UV 그대로)로 월드 위치와 이웃 4픽셀(깊이 차 작은 쪽)로 표면 법선을 복원 → 위쪽 성분 normal.y 가 `scanlineFloorNormalMin`(0.55) 이하 = 벽(주사선 그대로), `scanlineFloorNormalMax`(0.8) 이상 = 바닥(`scanlineFloorKeep` 0 = 없음), 사이는 smoothstep. 하늘 = 먼 평면 깊이 → 주사선. 플레이어·보스 마스크 위는 전부 주사선. **RECTIFY 글자 마스크(외곽선 포함 — SDF 안쪽 ∪ 외곽선 띠, 보이는 만큼) 위는 주사선을 뺀다**(11-12 사용자 요청, 이전엔 일부러 남겼던 부분). 깊이만 쓰므로 새 RPC 없이 피어마다 같다. RECTIFY 안쪽 주사선(§4.5)은 별개.
- 하늘 호박색: 호박색 = 흑백 명도 × 색(곱하기)이라 검은 하늘은 칙칙한 겨자색이 됐다. 하늘(먼 평면 깊이)만 명도를 `skyAmberMinLuma`(0.6) 이상으로 올려 벽·바닥 톤의 밝은 노랑으로. 플레이어·보스·RECTIFY 는 그대로(그 뒤에 덮음). 하늘이 깊이를 쓰는 메시(돔)면 하늘로 안 잡힌다.
- 한계: 바닥 위 소품의 윗면(위를 향함)은 바닥으로, 옆면은 벽으로 본다. 깊이를 쓰지 않는(ZWrite 꺼진) 반투명 물체는 뒤 표면 기준.

## 5. 알려진 한계

- 프로필은 회차 시작 때 고정 — 연출 중 처치자 캐릭터가 바뀌어도(랩 F1) 그 회차는 옛 프로필, 랩이 바로 처음부터 다시 튼다.
- 끝 자르기 값은 클립 시간(초) 하나 — 기본 프로필을 여러 캐릭터가 쓰면 캐릭터마다 클립 길이가 달라 같은 값이 다른 프레임이다(캐릭터 프로필을 만들면 따로).
- 클립 노드 실측 표본(`BossVictoryClipTiming`)은 클립 이름 기준이라 프로필과 무관(같은 클립이면 공유).
- 클립 복사는 같은 자리에 이미 같은 클립이 있어도 하나 더 생긴다(중복 확인 안 함 — Undo 로 되돌림).
