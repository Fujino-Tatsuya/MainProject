# 보스 처치 연출 — 캐릭터별 프로필 (할 일 267)

> 계획: [PLAN-boss-victory-character-profiles.md](../history/PLANS/PLAN-boss-victory-character-profiles.md) · 연출 전체: [PLAN-boss-victory-glitch.md](../../PLAN-boss-victory-glitch.md)

## 1. 구조

| 무엇 | 어디 | 캐릭터별? |
|------|------|-----------|
| 타임라인 전체(카메라 컷·처치자 평타·마커·끝 자르기·느려짐·보스 노드·화면 효과 세기 클립·TV 꺼짐·로비 전환) | `BossVictoryProfile.timeline` | ✅ |
| 백대쉬(거리·시간·곡선·보정 각·후 대기) | `BossVictoryProfile.backDash*` | ✅ |
| 대상별 시간 배율 4개(하던 동작·처치자 End 후·팀원·보스 End 후) | `BossVictoryProfile.*Scale` | ✅ |
| 처치 연출 스킬 칸 | `BossVictoryProfile.finisherSlot` | ✅ |
| 팔레트 17칸 — 칸마다 독립 색(271, 11-16 관계 끊음, §4.11) | `BossVictoryProfile.paletteColors` | ✅ |
| **RECTIFY 주황 채움 = 캐릭터별(11-12, §4.12)** — 머무름·와이프·방향·들쭉날쭉·줄 굵기·좌우 색·보간 세기·안쪽 주사선 간격·어둡기 | `BossVictoryProfile.rectifyOrange*`·`rectifyScanline*` | ✅
| 화면 효과 모양(호박색·주사선·찢김·보스 노이즈·CRT 색·셰이더)·벽 숨김 기준·보스 피격 애니 상태·마커 옵션(포커스 블렌드·숨김 대상) | `BossVictoryGlitchSettings` | ❌ 공용 |

- 에셋: 공용 `Assets/Resources/BossVictory/BossVictoryGlitchSettings.asset` · 프로필 `Assets/Resources/BossVictory/Profiles/BossVictoryProfile_{Paladin,Gunner,Assassin}.asset`. 기본 프로필 에셋 `BossVictoryProfile_Default` 는 **11-12 삭제**(사용자 지시) — 공용 `defaultProfile` 칸은 비어 있다.
- **고르기**: 공용 설정의 `characterProfiles` 표(캐릭터 → 프로필). 캐릭터 = 기존 캐릭터 판별 `TutorialCharacter`(로스터 칸, `TutorialLocalPlayerInfo.ResolveCharacter`). 같은 캐릭터 두 줄이면 앞 줄. **표에 없거나 프로필이 비면 `defaultProfile`, 그것도 비면(정본) 코드 기본값** `BossVictoryProfile.CreateDefault()`(저장 안 되는 인스턴스 하나, 처음 쓸 때 로그 1회). 순수 함수 `BossVictoryProfileSelect.Select`.
- 처음 내용: 267 이전 설정 에셋 값을 그대로 복제(기본·세 캐릭터 같은 내용) → 그 뒤 캐릭터별 튜닝. 기본 프로필 에셋은 11-12 삭제 — 지금은 표에 없는 캐릭터·처치자를 못 찾은 회차만 코드 기본값(코드 초기값 타임라인 = 옛 기본 곡선, 처치자 검·백대쉬 동시 끔, 팔레트 = 코드 기본 색)을 쓴다.
- 프리팹 수정 없음. `PlayerVictoryFinisher`(어느 프리팹에도 안 붙어 있던 Variant 스킬 칸 컴포넌트)는 삭제 — 스킬 칸은 프로필.

## 2. 네트워크

새 RPC·NetworkVariable 없음. 각 피어가 `BossVictoryGlitchPresentation.Begin` 에서 복제된 `VictoryState.KillerClientId` 로 처치자 `Player` 를 찾고 → 그 캐릭터 → 프로필을 회차 시작 때 고정한다(늦은 참가자도 같은 길). 호스트의 로비 전환(타임라인 '로비 전환' 마커 → `PresentationFinished`)도 같은 프로필 시계. 처치자를 못 찾으면(나감 등) 기본 프로필 — 칸이 비어 코드 기본값(모든 피어 같은 코드 값이라 결과 같음).

## 3. 편집법 — 타임라인 창 `Tools/Boss Victory/처치 연출 타임라인`

- **도구 막대 캐릭터 드롭다운**(팔라딘·거너·어쌔신) = 그 프로필을 편집. 옛 '미리보기 캐릭터'(스킬 클립 길이 표시)와 통합. ('기본' 줄은 기본 프로필 에셋 삭제와 함께 뺐다, 11-12.)
- Play 중 연출이 돌면 **실제 처치자 캐릭터를 따라간다**(드롭다운 잠김, '실제 처치자'). 처치자를 못 찾은 회차면 '코드 기본값'으로 보이고 고쳐도 저장되지 않는다.
- 표에 프로필이 없는 캐릭터를 고르면 코드 기본값이 보이고(고쳐도 저장 안 됨 안내) '이 캐릭터 프로필 만들기(코드 기본값)' 버튼. 메뉴 `Tools/Boss Victory/캐릭터별 프로필 만들기 (267)` 도 같은 길 — **기본 프로필 에셋은 다시 만들지 않는다**. 새 프로필 원본을 다른 캐릭터 복제가 아니라 코드 기본값으로 둔 이유: 팔라딘 등을 복제하면 그 캐릭터 전용 칸(손 검 번개·낙하 검 배율·백대쉬 동시)까지 딸려 온다. 비슷하게 시작하려면 노드 복사로 옮긴다.
- **노드 복사**: 클립·마커 우클릭 → '다른 캐릭터에 복사…' → 캐릭터 체크 → 복사. Shift(또는 Ctrl/⌘)+클릭·사각형으로 여러 노드를 골라 한 번에(키는 복사 대상 아님). 같은 시각으로:
  - 마커(백대쉬 시작·스킬 연출 시작·느려짐·보스 노드·로비 전환 등 하나뿐인 것) = **덮어쓰기**. 애니 클립 노드(스킬 연출 시작·보스 피격 애니)는 끝 자르기도 같이.
  - 클립(카메라 컷·화면 효과 구간·HUD 숨김) = **새로 추가**(카메라 컷 값·키 포함). 1개뿐인 트랙(TV 꺼짐·처치자 평타)은 덮어쓰기.
  - Undo 한 번(Ctrl+Z)에 전부 되돌림. 순수 함수 `BossVictoryTimeline.CopyMarkerFrom`·`CopyClipFrom`.
- 노드 속성 칸(296부터 창 **왼쪽 세로 패널**)의 백대쉬·배율·스킬 칸은 지금 프로필 칸, 트랙 모양 값(색·노이즈 등)은 공용 설정 칸.
- **왼쪽 속성 패널(296)**: 옛 아래 칸(255 후속 9)을 창 왼쪽으로 — 타임라인은 오른쪽 나머지. 패널·타임라인 사이 막대를 좌우로 끌어 폭(최소 220px · 타임라인 최소 260px, EditorPrefs `BossVictoryTimelineWindow.PanelWidth` 기억). 내용이 길면 세로 스크롤. 맨 위 '조작' 접기 = 단축키 안내.
- **여러 개 선택(296)** — 순수 함수 `BossVictoryMultiEdit`(`Editor/BossVictoryMultiEdit.cs`), EditMode `BossVictoryMultiEditTests`:
  - **Shift(또는 Ctrl/⌘)+클릭** = 넣기, 다시 누르면 빼기. 클립·마커(클립 노드 포함)·키를 섞어 고를 수 있다. **빈 곳 클릭** = 선택 풀기. **빈 곳 끌기** = 사각형 선택(클립·마커·클립 노드 — 키는 Shift+클릭), Shift 누르고 끌면 지금 선택에 더하기.
  - 선택한 것 하나를 끌면 **전부 같은 Δ초** 이동. 스냅(0.01초)은 잡은 노드 기준, 그 Δ 를 나머지에 그대로. 가장 앞 노드가 0초 아래로 못 가게 Δ 를 한 번에 막는다(간격 유지). 처치자 평타(시작 0 고정)는 끝이 움직인다.
  - 선택 클립의 **끝(또는 시작)을 끌면** 선택 클립 전부 같은 Δ 로 길이(클립마다 최소 0.01초·0초). 카메라 컷(길이 자동)·마커·키는 이때 그대로.
  - 못 움직이는 노드는 건너뜀: 꺼진 마커(0 = 끔인 마커가 0일 때)·시작·끝 키·클립과 같이 선택된 키(클립 비율이라 따라감). 처치 연출 스킬·보스 피격 애니 **끝 자르기**는 그 노드 하나만.
  - Undo 한 번(끌기 묶음 — 손을 뗄 때 합침).
  - 왼쪽 패널 = "N개 선택" + 목록. **같은 종류**(같은 트랙 클립 / 마커 / 같은 트랙 키)면 공통 칸 — 값이 다르면 '—', 고친 칸만 전부에: 클립 = 시작·길이(처치자 평타 = 끝)·노이즈 UV·두둥실·순간이동 두 점, 마커 = 시각(전부 같은 시각으로), 거너 Q 1~3단계 마커만이면 단계 칸 전부, 키 = 세기(컷 키 = FOV)·다음 키까지 곡선. 종류가 섞이면 개수만.
  - 기존 단축키를 여러 개에도: **Del** = 선택 클립 전부 + 중간 키(마커는 못 지움) · **Ctrl+C/V/D** = 선택 클립(트랙·간격 유지 — 붙여넣기는 각자 트랙, 가장 앞 클립이 재생 헤드·우클릭 시각, 복제 = 가장 늦은 끝 뒤). 새 단축키는 없다.
- 메뉴 `Tools/Boss Victory/캐릭터별 프로필 만들기 (267)` — 없는 캐릭터 프로필(팔라딘·거너·어쌔신)만 코드 기본값으로 만들어 표에 넣는다(덮어쓰지 않음, 기본 프로필 에셋은 안 만듦).
- **스크롤**(11-12): 위 시간 눈금 = 세로 고정·가로(시간 스크롤) 따라감, 왼쪽 트랙 이름 열 = 세로 따라감·가로 고정. 트랙이 넘치면 세로 막대(맨 밑에 트랙 1칸 여유), 확대해서 시간이 넘치면 맨 아래 가로 막대. 휠(트랙·눈금 위) = 줌, 휠(이름 열 위) = 세로 스크롤, 가운데 끌기 = 시간 이동. 왼쪽 속성 패널은 따로 세로 스크롤(296).
- **카메라 컷 → Scene 뷰**(11-12): 컷 인스펙터 '이 카메라 뷰로 Scene 카메라 세팅하기'(컷 시작) · '재생 헤드 N초 화면으로'(이동 컷 그 순간, 재생 헤드가 컷 구간 안일 때만). 자세 = 런타임과 같은 `BossVictoryCameraCuts.Pose`, 기준 = 'Scene 뷰로' 와 같은 처치자·보스 위치(Play 중 지금 회차 / 아니면 이 에디터 세션 마지막 회차). 기준이 없거나 Scene 뷰가 없으면 비활성 + 이유. 앞 컷과 섞는 블렌드 구간은 반영하지 않는다(이 컷 자신의 화면). 기준 '처치자 무기'(매 프레임 추종·부드러움)는 §4.15.

## 4. 디버그 랩 (DebugBossVictory)

**F1 = 처치자(내 플레이어) 캐릭터 순환** 팔라딘 → 어쌔신 → 거너 — 간파·특성 랩의 `DebugCharacterCycle` 재사용. 이 씬은 빌드 목록에 넣지 않는다(사용자 결정 — `BossVictoryLabRulesTests` 가 확인). 단 툴바 Dev Boot 로 ▶하면 `DevBootLauncher` 가 Play 동안만 빌드 목록에 임시로 넣고 Play 가 끝나면 되돌린다 — Play 중 커밋하거나 Play 중 에디터가 비정상 종료되면 `ProjectSettings/EditorBuildSettings.asset` 에 남으니 커밋하지 말고 `Dev/Dev Boot/빌드 목록 강제 정리`. 캐릭터 순환 컴포넌트는 씬에 두지 않고 랩이 실행 중에 붙인다(로스터 = NetworkManager 의 `NetworkLoadingFlowController`). 내 플레이어가 바뀌면 랩이 처음부터 다시 — 새 캐릭터 프로필로 재생.

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
- 프로필 기본 클립(267 당시 4개 프로필) = 0.3~5.6초, 들어올 때 0.2초·나갈 때 0.3초, 0.22%·1.6초·위상 0.25. 옛 에셋은 읽을 때 한 번 넣는다(`SeedCameraBob`, 숨은 표시 `cameraBobSeeded` — 지우면 다시 안 넣음).

## 4.4 배경 글자 RECTIFY(267 후속)

트랙 `Rectify`('배경 글자 RECTIFY', 세기 키 = 알파). 기본 클립 = 0.3초부터 0.1초 동안 들어와 4.5초(TV 꺼짐 시작 — 캡처에 남음)까지. 옛 에셋은 한 번 시드(`rectifySeeded`).
- 모양(공용 `BossVictoryGlitchSettings`): 폰트 `rectifyFont` = SVN `Assets/50.Art/UI/Fonts/JalnanGothicTTF.ttf`(guid ec558e14…) · 글자 `RECTIFY` · 폭 = 화면 너비 × 1.06(양끝 조금 잘림) · 대문자 높이 가운데 = 뷰포트 0.55(위에서 45%) · 기울기 0° · 흰 외곽선(대문자 높이 4%), 안은 비움.
- 렌더: 켤 때 TMP 동적 SDF 아틀라스(512² Alpha8, 글자 7개)를 한 번 만들고 끌 때 버린다(`BossVictoryRectifyText`). 글리치 합성 셰이더가 글자마다 화면 사각형·아틀라스 UV 로 SDF 외곽선만 그리고 **플레이어·보스 마스크가 0 인 곳에만** 섞는다 → 인물·보스 뒤, 벽·바닥 앞(별도 패스·카메라 없음). 찢긴 UV 라 같이 찢기고, 들어오는 동안(알파 < 1) 가로줄이 알파 비율만큼 무작위로 켜져 찢기듯 나타난다(0.32초 찢김 펄스와 겹침).
- 시각 t 만으로 정해져 스크럽·모든 피어 같음(새 RPC 없음). 복사 = 클립 추가.
- 🔴 폰트 `.meta` 가 SVN 에 아직 안 올라가 있으면 PC 마다 guid 가 달라 참조가 끊긴다(글자만 안 보이고 경고) — `.meta` SVN 커밋 필요.

## 4.5 RECTIFY 안쪽 색 — 원 3개(267 후속)

트랙 `RectifyFill`('RECTIFY 안쪽 색', 세기 = 채움 불투명도), 기본 클립 0.4~4.5초(옛 에셋 1회 시드 `rectifyFillSeeded`). 처음 사양 = `_lane-artifacts/Work2/rectify-fill/SPEC-ring-wipe-fill.md`, 11-12 사용자 지시로 **원 3개 통합**(옛 링 3개 + 따로 있던 흰 원 → ① 마젠타 · ② 블루 · ③ 흰색).
- 원마다 칸(`rectifyCircles[i]`): 생성 시각(클립 시작 기준)·시작 반지름(단어 높이)·페이드·확장·중심(단어 UV)·왼쪽 색·오른쪽 색. 생성 시각에 자기 중심에서 시작 반지름으로 생겨 크기 그대로 페이드(알파 0→1) → 불투명해진 뒤 시작 반지름 → Rmax 로 easeOutCubic 확장. 나중 원이 위(painter's order), 마지막 원으로 고정, 반복 없음. 첫 원 전은 비어 있음(흰 외곽선만).
- **③ 흰 원 = 회색 단계**: 페이드 동안 불투명한 회색(`rectifyCircleGrayColor` #8A8A8A) → 페이드가 끝나면 `rectifyCircleGrayToWhite`(0.1초) 동안 회색 → 흰색(좌우 색) → **흰색이 된 순간부터 확장**. 옛 그레이 링은 이 회색 단계로 흡수.
- 원 안 색 = 단어 UV x 기준 왼쪽 색 → 오른쪽 색(옛 방사형 그라데이션은 없앰). 코드 기본값: ① #FF2DB4 → #A040FF · ② #5AC8FF(하늘) → #2438A8(남색) · ③ #FFFFFF → #FFF6E0. 생성 0 / 4/60 / 0.2초, 반지름 0.15, 페이드 0.15, 확장 0.6, 중심 (0.46, 0.5).
- 좌표 = 단어 사각형(글자 사각형 합집합) UV, 거리 단위 = 단어 높이(가로에 단어 종횡비 보정). Rmax = 원 중심에서 네 모서리까지(+경계, 다 덮음). 경계 0.01(`rectifyFillSoftness`, 픽셀보다 작으면 fwidth AA).
- **옛 값 자동 이전**(`MigrateRectifyCircles`, `OnEnable` 1회 — `rectifyCirclesVersion` 1): ① = 옛 링 0(생성 0·옛 반지름·페이드·확장·중심, 왼쪽 = 옛 링 0 색) · ② = 옛 링 1(생성 = 옛 간격 `rectifyFillDelay`, 색 = 새 블루 그라데이션) · ③ = 옛 흰 원(생성·반지름·페이드·확장·중심, 왼쪽 = 옛 흰색) · 회색 = 옛 링 2 색(있으면). 옛 칸(`rectifyFillColors`·`rectifyFillCenter/StartRadius/Delay/Fade/Expand`·`rectifyWhite*`)은 숨김, 그라데이션 칸은 삭제. 에셋(`BossVictoryGlitchSettings.asset`)은 11-12 튜닝값(링 색 2개·간격 0.1·확장 0.5·흰 원 0.3초/확장 0.1·그라데이션 0)을 이 규칙으로 옮겨 저장했다 — ② 옛 #3A6BFF 는 하늘 → 남색으로 바뀜.
- **주황 채움**(`rectifyOrange*` — 11-12부터 캐릭터별 프로필, §4.12): 마지막(흰) 원이 다 퍼진 뒤(생성 + 페이드 + 회색→흰 + 확장) `rectifyOrangeHold`(0.05초) 머물고, 레퍼런스(raw 12~23 프레임)처럼 가로줄(4px)마다 들쭉날쭉한 경계가 한쪽에서(`rectifyOrangeRightToLeft`) 지나가며(0.1초, 들쭉날쭉 단어 폭 0.2) 흰색을 불투명한 주황으로 바꾼다. 색 = 왼쪽 → 오른쪽 보간(세기 `rectifyOrangeGradient`, 옮길 때 값: 왼쪽→오른쪽 방향·#FF4500 근처 → 분홍 #FF0074 근처 — 사용자 튜닝).
- 블룸: 글리치 합성은 `AfterRenderingTransparents`(후처리 전)라 RECTIFY 도 볼륨 블룸을 받지만, 값이 0~1(LDR)이라 맵 볼륨 threshold(0.9~1.25)를 거의 못 넘어 번짐이 없다 — 대신 전용 글로우(§4.8).
- 순수 함수 `BossVictoryRectifyFill`(원 상태 `Circle`·원 색·주황 시작/진행·와이프·색·단어 사각형·Rmax) → 셰이더가 글자 SDF 마스크에 그린다 — `rectifyFillCoversOutline`(기본 켬, 11-12 사용자 요청)이면 마스크를 외곽선 바깥 경계(sd = −외곽선 반두께, 바깥 AA 유지)까지 넓혀 색이 지나간 자리는 외곽선도 같은 색, 흰 외곽선은 색이 아직 닿지 않은 만큼(1 − 덮인 정도)만 남는다(첫 원 전 = 흰 외곽선만). 끄면 예전처럼 안쪽만·외곽선 흰색. 안쪽 가는 가로 주사선 칸(간격 화면 높이 0.3%, `rectifyScanlineDark`) — 11-12 사용자 요청으로 기본 0(끔), 칸은 다시 켤 여지로 남김. 덮기 끔일 때만 흰 외곽선이 그 위.
- 모양 값은 공용 설정 `rectifyCircles`·`rectifyCircleGray*`·`rectifyFillSoftness/CoversOutline`(주황 채움·`rectifyScanline*` 는 캐릭터별 프로필, §4.12). 시각 t 로 결정 — 스크럽·피어 동일, 새 RPC 없음. 복사 = 클립 추가.

## 4.6 CRT 주사선 = 벽·하늘에만(267 후속)

글리치 합성의 CRT 주사선(트랙 '주사선')을 바닥에서 뺀다. 픽셀마다 깊이(`_CameraDepthTexture`, 찢긴 UV 그대로)로 월드 위치와 이웃 4픽셀(깊이 차 작은 쪽)로 표면 법선을 복원 → 위쪽 성분 normal.y 가 `scanlineFloorNormalMin`(0.55) 이하 = 벽(주사선 그대로), `scanlineFloorNormalMax`(0.8) 이상 = 바닥(`scanlineFloorKeep` 0 = 없음), 사이는 smoothstep. 하늘 = 먼 평면 깊이 → 주사선. 플레이어·보스 마스크 위는 전부 주사선. **RECTIFY 글자 마스크(외곽선 포함 — SDF 안쪽 ∪ 외곽선 띠, 보이는 만큼) 위는 주사선을 뺀다**(11-12 사용자 요청, 이전엔 일부러 남겼던 부분). 깊이만 쓰므로 새 RPC 없이 피어마다 같다. RECTIFY 안쪽 주사선(§4.5)은 별개.
- 하늘 호박색: 호박색 = 흑백 명도 × 색(곱하기)이라 검은 하늘은 칙칙한 겨자색이 됐다. 하늘(먼 평면 깊이)만 명도를 `skyAmberMinLuma`(0.6) 이상으로 올려 벽·바닥 톤의 밝은 노랑으로. 플레이어·보스·RECTIFY 는 그대로(그 뒤에 덮음). 하늘이 깊이를 쓰는 메시(돔)면 하늘로 안 잡힌다.
- 한계: 바닥 위 소품의 윗면(위를 향함)은 바닥으로, 옆면은 벽으로 본다. 깊이를 쓰지 않는(ZWrite 꺼진) 반투명 물체는 뒤 표면 기준.

## 4.7 화면 밝기 — 하이키(267 후속, 11-12)

레퍼런스(배경 노랑이 밝고 쨍함·바닥 밝은 회색)보다 어두침침하던 것. 원인 1순위 = 호박색 = 씬 명도 × 색(곱하기)이라 어두운 벽이 어두운 겨자색으로 남음.
- `amberLumaLift`(0.45): 호박색 명도 = 리프트 + (1 − 리프트) × 원래 명도. 벽 명도 0.1~0.3 → 후처리 뒤 약 (194,162,91)~(205,174,101) ≈ 레퍼런스 벽 (200,161,97). 하늘 하한(`skyAmberMinLuma`)은 그 위.
- `scanlineDarkness`(0.15): CRT 주사선 어두운 줄 = 1 − 값(예전 코드 고정 0.28).
- `groundCircleBrightness` 1 → 1.8: 발밑 회색 원(바닥) 밝기.
- 처치 연출 전용 볼륨(`victoryVolume`·`victoryVignetteIntensity` 0·`victoryOverrideExposure`/`victoryPostExposure`): `BossVictoryGlitchRender.Enable` 때 런타임 전역 볼륨(우선순위 120, 카메라 레이어)을 만들고 `Disable`(연출 끝·취소·Teardown) 때 지운다. 맵 볼륨 `VP_CombatLevel_Toon` 의 비네트 0.22·노출 0.25 만 덮는다. 각 피어 로컬, 새 RPC 없음.
- 참고(바꾸지 않음): 맵 볼륨 Neutral 톤매핑은 흰 1.0 → sRGB 224 로 깎는다. 디버그 씬과 맵은 같은 볼륨·방향광(0.75)·환경광.

## 4.8 RECTIFY 전용 글로우(267 후속, 11-12)

맵 블룸(문턱 1.1)과 상관없이 글자 채움만 빛나게 한다. 합성 셰이더 패스 1~4(`BossVictoryGlitchRender.CompositePass.RecordGlow`):
1. 소스(절반 해상도): 고른 채움(`rectifyGlowTarget` — 흰 원 / 주황 채움 / 둘 다) 색 × 글자 마스크(외곽선 바깥 경계까지) × 채움 불투명도 × 글자 알파 × (1 − 플레이어·보스). 찢김 UV·등장 가로줄은 따르지 않는다.
2·3. 9탭 가우스 가로·세로 블러(절반 해상도, 한 칸 = `rectifyGlowRadius` ÷ 4 화면 px).
4. 합성 결과에 더하기(전체 해상도, × `rectifyGlowColor` × `rectifyGlowIntensity`). 그 뒤 맵 후처리(톤매핑)는 그대로 받는다.
- 켜지는 프레임: 세기 > 0 · 글자 알파 > 0 · 채움 불투명도 > 0 · 고른 채움이 있을 때만(아니면 패스 자체를 안 넣는다).
- 텍스처: 절반 해상도 HDR 2장(렌더 그래프 임시 텍스처 — 글로우 프레임에만 잡히고 풀로 반환, 1920×1080 이면 960×540 × 2). 비용 = 전체 화면 패스 4개(소스 절반·블러 절반 ×2·더하기 전체), 블러 탭 9 × 2.
- 블러 빛이 앞 인물 가장자리에도 살짝 번진다(소스만 인물을 빼고, 더하기는 안 뺀다).

## 4.9 스킬과 백대쉬 동시(267 후속, 11-12 — 팔라딘만)

프로필 칸 `finisherWithBackDash`(기본 끔). 켜면 '스킬 연출 시작' 때 백대쉬를 끊지 않는다 — 스킬 애니가 몸을 쥐고 백대쉬는 이동만(`BossEncounterDirector.StepSkid(animate: false)` — 밀림 리액션 애니·끝 Idle 복귀 없음). 스킬이 백대쉬 마커보다 먼저 시작해도 백대쉬는 자기 시각에 시작(순수 함수 `BossVictoryKillerSequence.StartDashDuringFinisher`).
- 팔라딘 프로필: 켬 · 백대쉬 시작 0 · 스킬 연출 시작 0 · 처치자 평타 클립 없음 → 처치 판정 순간 하던 평타를 스킬 재생(0층 CrossFade 0.05초)이 끊고, 같은 순간 궁극기 애니 + 백대쉬 이동.
- 기본·거너·어쌔신: 끔(옛 흐름 — 평타 클립 0.2 → 백대쉬 0.2 → 스킬 0.65 에서 백대쉬 끊음).
- 끊기·재생은 각 피어 표시만(서버 판정·새 RPC 없음). 이동은 오너 모터, 다른 피어는 위치 복제.

## 4.10 팔라딘 손 검 번개·낙하 검 1.25배·느려짐 제외(269)

프로필 칸 `handSwordLightning`(손 검 번개)·`fallingSwordScale`(낙하 검 배율) — 팔라딘 프로필만 켬·1.25(처음 2 → 사용자 결정으로 1.25), 기본·거너·어쌔신은 끔·1. 타임라인 창 '스킬 연출 시작' 노드 인스펙터에도 보인다. 노드가 아니라 칸인 이유: 손 검 번개 구간은 연출 전체로 고정이고, 낙하 검은 R 클립 Custom0 이 시각을 정한다.
- **손 검 번개**: 연출 시작(`Begin`, 이펙트 소거 직후) ~ 정리(`Teardown` — 끝·취소·랩 원상복구·파괴). 팔라딘 R 칼날 전기 엔트리(`FX_Sword_Electric_Entry`)·소켓(손 검 메쉬 `SM_Wep_Sword_03`)을 `PaladinSkillView.swordElectric` 에서 읽어 **따로 든 루프 핸들**로 재생(`BeginVictorySword`/`EndVictorySword`). 같은 소켓 플레이어를 쓰면 처치 연출 R 의 `OnEnd` → `StopUltimateVfxLocal` 때 같이 꺼진다. 엔트리 파트 delay 0.45초만큼 늦게 보인다. 손 검 크기는 그대로.
- **낙하 검 배율(팔라딘 1.25)**: 처치 연출 R(표시 전용)의 마무리 낙뢰 `FX_Lightening_Strike`(HolySword — 하늘 15m 에서 떨어지는 검)를 `PlayTargetStrikeAtLocal`(표시 전용 경로만 — 평소 R 은 배율 1) 에서 배율 × `fallingSwordScale` 로 재생.
  - 🔴 매니저 배율은 인스턴스 루트 localScale 만 키우는데 칼 파트(HolySword·SwordGlow·SwordTwinkle)는 `scalingMode: Local` 이라 부모 배율을 무시한다 → 루트만 키우면 칼 크기는 그대로, 낙하 시작점만 30m 로 올라가 칼이 15m 공중에서 멈춘다. 그래서 루트 아래 Local 파트의 자기 localScale 도 같은 배율로 키우고(크기·낙하 거리 모두 같은 배율 → 바닥에 닿음), 이펙트가 풀로 돌아가면(원샷 수명 2.2초 끝·걷힘·플레이어 비활성) `PaladinSkillView.Update` 가 원래 값으로 되돌린다.
  - 낙하 검은 원래대로 수명 2.2초(Custom0 부터) 뒤 사라진다 — 연출 끝까지 남기지 않는다(그 위에 번개를 붙이지 않으므로 남길 이유 없음).
  - **낙하 자리·수명(11-12)**: 프로필 칸 `fallingSwordOffset`(보스 기준 오프셋, +Z = 보스 정면, 높이 무시 → 그 아래 바닥, 0 = 보스 아래)·`fallingSwordLife`(초 = **꽂힌 채 화면에 남는 시간** — 엔트리 길이 × 0.75 시점에 재생 배율 0 으로 멈췄다가 이 시간 뒤 풀어 원래 페이드로 사라짐, 처치 연출 정리 때 남은 검은 걷음, 0 = 엔트리 2.2초. 반납 시각만 늦추면 칼 입자 수명 2.15초에 그림이 사라져 안 보였다 — `VictoryStrikeHold`) — 처치 연출 R 에서만(평소 R 그대로). 생기는 시각은 그대로 클립 Custom0 이벤트라 **타임라인 클립이 아니라 숫자 칸**(클립을 두면 시작이 이벤트와 둘이 된다). 타임라인 창 '스킬 연출 시작' 노드 칸 + 창이 열려 있으면 Scene 뷰 노란 원 핸들로 끈다. 보스 위치·회전은 복제라 모든 피어 같다, 새 RPC 없음.
- **느려짐 제외**(사용자 결정): 손 검 번개와 어쌔신 처치 오라(267 후속)는 '느려짐 시작' 뒤에도 원래 속도. `BossVictoryGlitchPresentation.KeepRealtime(handle)` 로 등록 → 처치자 이펙트 배율을 걸 때마다(`SetEffectRate`) 등록 핸들은 `EffectManager.SetPlayRate(handle, 1)`. 회차 시작·정리 때 비운다. (손 검 번개는 손 검을 따라가 원래도 `SetPlayRateForTarget(처치자 루트)` 에 안 걸리지만 같은 규칙으로 등록.)
  - 낙하 검은 월드 고정 원샷이라 처치자 배율에 원래 안 걸린다 — 느려짐 뒤에도 원래 속도로 떨어진다(맞추지 않음: 공중에서 거의 멈춘 칼이 되고, 마무리 낙뢰 타이밍이 클립 Custom0 과 어긋난다).
- 프리팹 수정 없음(런타임). 다른 캐릭터는 `PaladinSkillView` 가 없어 칸을 켜도 무시. 새 RPC 없음 — 복제 처치 상태로 각 피어가 `Begin`(늦은 참가자 같음). 순수 판단 `BossVictoryGlitchPresentation.UsesKillerSword`(EditMode `BossVictoryProfileTests`).

## 4.11 팔레트 = 칸마다 독립 색(271 · 11-16 관계 끊음)

처치 연출 화면 효과 색 17칸 = 프로필 `paletteColors`(칸 순서 = `BossVictoryPaletteSlot`). `BossVictoryPalette.From(profile)` 이 칸을 그대로 읽는다 — **계산 없음, 칸끼리 관계 없음**(한 칸을 바꿔도 다른 칸은 안 바뀐다). EditMode `BossVictoryPaletteTests`.

- **11-16 관계 끊음(사용자 요청 — "다른 색에 의해 내 색이 정해지는 모든 컬러를 화면 색으로 역산해 넣고 관계를 끊어줘")**: 그 전까지는 프로필 기준 색(`baseColor`) 하나에서 공용 설정 색(= 팔라딘 기준 팔레트)을 색조 회전·채도/밝기 비율로 옮겨 계산했다(대비 그룹 = 대비 기준 색 자동/직접 + 하한, 글자 그룹 = `textBaseColor`(11-13), 칸별 직접 지정 `paletteOverrides`, 주황 채움 원본 = 프로필 `rectifyOrangeLeft/Right`). 세 프로필마다 그 계산 결과(그때 화면 색)를 배치 모드에서 옛 계산식 그대로 구워 `paletteColors` 에 넣고, 계산식(`BossVictoryPaletteCalc`)·입력 칸(`baseColor`·`separateTextColor`·`textBaseColor`·`contrast*`·`paletteOverrides`·`rectifyOrangeLeft/Right`)을 지웠다.
- **공용 설정 색 칸은 화면에 안 쓴다**: `amberColor`·`bossNoiseBright/DarkColor`·`rectifyColor`·`rectifyCircles[].left/rightColor`·`rectifyCircleGrayColor`·`rectifyGlowColor`·`crtOffGlowColor` 는 옛 값 보관용 숨은 칸(`[HideInInspector]`) — 창 트랙 칸에서도 뺐다. 세기·모양 값(`bossNoiseBrightIntensity` 등)은 그대로 공용.
- **코드 기본 색**: `BossVictoryPalette.CreateDefaultColors()` = 굽기 때 팔라딘 색. 프로필이 없는 회차(코드 기본 프로필)·칸이 모자란 프로필은 이 색으로 그린다.
- **런타임**: `BossVictoryGlitchPresentation.Begin` 이 처치자 프로필을 고르는 순간 `_palette = BossVictoryPalette.From(profile)` → `BossVictoryGlitchRender.Enable/ApplyLook`·TV 꺼짐 `BossVictoryCrtOff`. 새 RPC 없음 — 복제된 `KillerClientId` → 처치자 캐릭터 → 프로필 → 같은 색(늦은 참가자도).
- **편집**: 타임라인 창 인스펙터 위 **'팔레트'** 버튼(또는 노란 틴트·보스 노이즈·TV 꺼짐·RECTIFY 트랙 칸의 '팔레트') — 17칸 색을 바로 편집(HDR). Play 중 연출이 돌면 바로 화면에(`RefreshLook`, TV 꺼짐은 다음 꺼짐부터). 프로필 에셋 인스펙터에서도 `paletteColors` 로 고칠 수 있다(다음 회차부터).

## 4.12 RECTIFY 주황 채움 = 캐릭터별(11-12)

공용 설정의 'RECTIFY 주황 채움' 10칸(`rectifyOrangeHold/Wipe/RightToLeft/Jitter/RowPixels/Left/Right/Gradient`·`rectifyScanlineSpacing/Dark`)을 `BossVictoryProfile`(인스펙터 'RECTIFY 주황 채움(캐릭터별)')로 옮기고 공용에서는 지웠다.
- **이전**: 세 프로필에 옮기기 전 공용 값(사용자 튜닝 포함 — 왼쪽→오른쪽·좌 (1, 0.27, 0)·우 (1, 0, 0.455))을 Unity 에서 그대로 복사 → 화면은 옮기기 전과 같다. `CreateDefault()`(표에 없는 캐릭터) 기본값도 같은 값(`BossVictoryOrangeLook.Default*`).
- **런타임**: 처치자 프로필 → `BossVictoryOrangeLook.From(profile)`(하한·0~1 자르기) → `BossVictoryGlitchRender.Enable/ApplyLook(settings, palette, orange)`. 처치자 = 복제된 `KillerClientId`(새 RPC·복제 값 없음).
- **팔레트(271)**: 주황 채움 색 = 팔레트 칸 `OrangeLeft/Right`(11-16부터 칸에 직접 — 프로필 `rectifyOrangeLeft/Right` 원본 칸은 지움, §4.11). 이 절의 10칸 중 색 2칸이 빠져 지금은 모양 8칸.
- **편집**: 타임라인 창 RECTIFY 안쪽 색 트랙 칸 — '주황 채움' 칸이 지금 고른 캐릭터 프로필 값. 테스트 `BossVictoryOrangeProfileTests`(세 프로필 값 = 옛 공용 값 · 렌더 값 = 처치자 프로필).

## 4.13 처치자 이동 = 캐릭터별(272)

프로필 칸 `killerMove`(`BossVictoryKillerMove`)가 처치자 움직임을 고른다 — 캐릭터 분기 대신 데이터. 순수 함수 `BossVictoryKillerMoves`(`Map/BossVictory/BossVictoryKillerMove.cs`), EditMode `BossVictoryKillerMoveTests`. 정본 값 넣기 = 메뉴 `Tools/Boss Victory/처치자 이동 프로필 적용 (272)`(지정 칸만 바꾸고 Unity 직렬화 — 다른 칸 그대로).

| 이동 | 프로필 | '백대쉬 시작' 단계 | '처치 연출 스킬' 단계 |
|---|---|---|---|
| `BackDash`(기본) | 팔라딘 | 백대쉬(지금 그대로) | 스킬 연출 시작 마커에 처치 연출 스킬 |
| `TeleportDash` | 어쌔신 | 없음 | 트랙 '어쌔신 순간이동·Q 이동' 클립마다 순간이동 → Q 이동(첫 클립 시작부터) |
| `RetreatCharge` | 거너 | 가까울 때만: 후퇴 스킬(E) 표시 + 백대쉬(이동만) | 가까움·멂 모두 '거너 Q 차징 시작' 마커(294) |

- **공통**: 이동은 처치자 **오너만 실제로**(PlayerMotor), 다른 피어는 복제 위치. 애니·이펙트는 각 피어 로컬. **새 RPC·복제 값 없음**. 처치 연출에 특성 연출 없음(어쌔신 일섬 `DashTint` 안 씀, 거너 Q 는 아래 294 단계 색 — 특성 색 아님).
- **어쌔신 순간이동 Q — 타임라인 트랙 '어쌔신 순간이동·Q 이동'(272 후속, `BossVictoryTrack.AssassinDash` · 목록 `timeline.assassinDash`)**:
  - **클립 하나 = 한 번**: 클립 시작에 그 클립의 **시작 지점**으로 순간이동 → 클립 길이 동안 **도착 지점**까지 Q 이동(곡선 `teleportMoveEase`) → 끝에 일섬 1번.
  - **여러 개 = 시작 순서대로 #1 → #2 …**(시작이 같으면 목록 순). 겹치면 앞 클립은 다음 클립 시작에서 끝난다(그 자리까지만 가고 일섬 끝점도 거기). 순수 함수 `BossVictoryAssassinDash`(`Order`·`Number`·`SegmentEnd`·`Current`·`AllDone`·`Progress`).
  - 두 점 = 클립마다 보스 기준 오프셋 `dashFrom`·`dashTo`(보스 위치 + 보스 **수평** 회전 × 오프셋, +Z = 보스 정면, 높이 = 처치자 발). 보스가 없으면 처치자 기준. 창에서 클립을 고르면 왼쪽 칸(296)에 숫자로 고친다.
  - 순간이동 = `LobbyPodLaunch.TeleportOwner` 순서(백대쉬·대시 끊기 → 넉백 지움 → `TeleportAuthoritative` → 방향 = 도착 지점 쪽 → `NetworkTransform.Teleport`). 이동 = 오너가 FixedUpdate 에 진행 차이만큼 `AddGroundedDisplacement`(유닛 통과, 벽이면 멈춤). 진행 = 글리치 시각 기준(클립 길이 = 실제 초).
  - 애니 = Q 대신 `teleportMoveState`(기본 `Assassin_Q_DashStrike_Transformed` = Combo_Attack_03_04, 60fps 1.917초, 상태 속도 1.5, Custom0 0.15·End 0.425). 첫 클립에 `PlayerSkillController.PlayPresentationStateOnly` 로 스킬 없이 0층을 쥐고(클립 이벤트는 어느 스킬에도 안 감 — **End 는 '스킬 끝'이 아니다**), **클립마다 Custom0 프레임부터 다시** 튼다. 이동 중 처치자 애니 배율 = `MoveAnimScale` = (End − Custom0) ÷ 클립 길이 ÷ 상태 속도 — 도착하는 순간 End 자세(기본 0.275초 클립 = 배율 0.667). 클립 사이 빈 시간은 제자리에서 애니가 그대로 이어진다.
  - 일섬 = 일반 Q 그대로 — 클립 시작에 `AssassinSkillView.BeginPierce(시작 지점, 방향)`, 이동 끝에 `EndPresentationPierce(진행 점)`(PierceLine + PierceBurst, 특성 색 없음). 끝점은 계산값이라 모든 피어 같다.
  - **마지막 클립 뒤** 애니가 정지 프레임까지 가서 로비 전환까지 멈춘다 — 기존 '처치 연출 스킬' 클립 끝 자르기(255 후속 7) `finisherTrimSeconds` 재사용(기본 71/60 = 1.1833초, 트랙 이름 누르면 '정지 프레임 = 71f' 버튼). 정지는 `AllDone` 뒤에만 본다.
  - 단계 시각: '백대쉬 시작' 단계 없음(무한대), '처치 연출 스킬' 단계 = 첫 클립 시작(클립 없으면 안 함). **'백대쉬 시작' 마커·'스킬 연출 시작' 마커는 이 이동에서 안 쓴다.** 옛 칸 `teleportOffset`·`arrivalOffset`·`teleportMoveInterval`(이동 구간 A/B)은 지웠다.
  - 이관: 메뉴 '처치자 이동 프로필 적용 (272)' 가 트랙이 비었을 때만 클립 1개를 만든다(옛 값 = 시작 0.2·길이 0.275·옛 두 점은 11-12 이관 끝, 이후엔 기본 두 점). 이미 있으면 그대로.
  - 원래 처치 연출(R 변신)을 대신한다 — `finisherSlot` 은 이 이동에서 안 쓴다(칸은 남김).
  - **매번 같게(297)** — 옛 원인: 카메라 컷이 컷 시작 프레임의 **실제 처치자 위치**(처치자→보스 축)로 구도를 잡는데 처치자는 보스 기준으로 순간이동해, 컷 1(0.09초)이 이동 #1(0.06초~) 도중·또는 순간이동 전(첫 프레임 끊김 — 순간이동은 시각이 지난 **다음** 프레임) 위치를 잡았다. 다른 피어는 복제 지연이라 늘 처치 위치. 지금:
    - 카메라 = 새 컷 기준 **보스 정면**(§4.17) — 어쌔신 컷 2개를 이 기준으로 옮겼다(구도 유지 이관, 아래).
    - 애니 속도 = 순간이동 Q 동안(`_dashOn`) 처치자 Animator.speed **절대값**(이동 중 `MoveAnimScale` · 클립 사이·뒤 1 · 느려짐 `killerAfterFinisherScale` · 정지 0). 옛: 원래 값(공속 배율 — 서버가 처치자 동작을 안 끊어 평타·스킬이 남으면 공속을 탄다)에 곱해 처치 순간 하던 동작·공속 버프마다 도착 자세·71f 정지 시점이 달랐다. 상태 속도는 실제로 읽은 값만 기억(Play 직후 못 읽는 프레임에 1 로 튀지 않게).
    - 클립 시작 프레임 = `SegmentStartClipTime` = Custom0 + (t − 클립 시작) ÷ 클립 길이 × (End − Custom0) — 한 프레임(끊기면 더) 늦게 들어가도 자세가 타임라인 시각에 맞는다.
    - 몸 방향 = 모든 피어 `PlayerMovement.RotateImmediately`(Armature). 옛 2번째 클립부터 루트를 돌리던 줄은 지웠다(루트 회전은 복제 안 됨 → 피어·회차마다 남음). 디버그 랩 `PlaceKiller` 가 루트 회전을 0 으로 되돌린다.
    - 벽 진단: 랩 `timelineClockLog` 켜면 이동마다 '도착 확인 — 실제 · 목표 · 수평 거리'(다음 순간이동 직전 · 마지막은 끝 0.1초 뒤, 오너 = 모터 결과 / 다른 피어 = 복제 위치). 벽 회피 자체는 아직 없다(§5).
  - Scene 뷰: 타임라인 창이 열려 있고 이동이 순간이동이면 **모든 클립**의 두 점·경로를 `#n 시작`·`#n 도착` 라벨로 그리고, 고른 클립만 밝게 + 위치 핸들(높이 0 으로 저장). 기준 보스 = 재생 중 보스 → 이 세션 마지막 회차 보스 → 열린 씬의 23호. 트랙 클립에도 `#n` 이 보인다.
- **거너 후퇴 차징**(제자리 — 순간이동 없음):
  - 처치 순간 보스와 수평 거리 < `retreatDistance`(기본 5m)면 '백대쉬 시작'에 `retreatSkillSlot`(E 냉각 백스텝) 표시 전용 재생(냉기·궤적 — 그 스킬 이동은 안 돎) + 백대쉬(기존 백대쉬 칸, 보스 반대쪽, 이동만). '거너 Q 차징 시작' 마커(`chargeStartTime`, 기본 -1 = 백대쉬 시작 + 0.2초 — 끌면 고정)에 E 표시를 접고 처치 연출 스킬(Q) — 백대쉬는 끊지 않고 이어 감.
  - 멀면 E·백대쉬 없이 **같은 '거너 Q 차징 시작' 마커**에 Q(294 — 옛 '백대쉬 시작 시각에 바로 Q' 폐기).
  - **3단계 차징·큰 레이저(294)** — 타임라인 트랙 '◆ 거너 Q' 한 줄에 마커 5개: 차징 시작 → 1단계 → 2단계 → 3단계 → 발사(`BossVictoryMarker.ChargeStart`·`GunnerStage1~3`·`GunnerFire`, 칸 `chargeStartTime`·`gunnerStage1/2/3Time`·`gunnerFireTime`). 기본 = 차징 시작 + 0.3 / 0.6 / 0.9 / 1.2초(옛 1.2초 충전과 같은 길이). 순서가 뒤집히면 앞 마커 시각으로 붙인다(`BossVictoryGunnerCharge.Times.Ordered`).
    - 진행은 프리젠테이션이 글리치 시각(타임라인 초)으로 계산(`BossVictoryGunnerCharge.Evaluate`)해 매 프레임 `GunnerBeamView.SetVictoryFrame` 에 넘긴다 — 뷰는 그리기만. 연출 시간 배율과 무관하게 마커에 맞는다. 옛 1.2초 코루틴(`GunnerChargeLaserSkill.OnPresentationOnlyStart`)은 지웠다. Gunner_Q_Fire 의 End = 처치 연출 스킬 End 는 그대로.
    - 수치 = 프로필 `gunnerQ`(`BossVictoryGunnerQ`, `Map/BossVictory/BossVictoryGunnerQ.cs`). 창에서 거너 Q 마커를 고르면 왼쪽 칸(296)에 그 마커 값:
      - 차징 시작: 시작 크기 `startScale`·구체 등장 지연 `sphereDelay`(타임라인 초)·차징 오프셋 `chargeOffset`(총구 기준 옆 x·위 y·앞 z — 구체·경고·과충전).
      - 1~3단계: 도달 크기 `stageN.sphereScale`·이전 마커 → 이 마커 보간 곡선 `ease`·도달 순간 경고 폭발(`chargeFullPlayer`) 켜기·크기. 3단계 = 과충전(`overchargePlayer`) 시작·크기 `overchargeScale`. 단계 색 = `OverdriveTint` 1 → 2 → 3(1단계 전 = 0).
      - 발사: 배출(`chargeVent`) 크기·레이저 폭 배율(표시만)·길이 `beamLength`(m)·유지 `beamHoldSeconds`(타임라인 초)·발사 오프셋 `fireOffset`(배출·레이저 시작).
    - 레이저 = 보스 방향 수평, 벽 무시·보스 관통·길이 칸 그대로, 피격 이펙트 없음. 오프셋 0 이면 원래 소켓 자리. 소켓·크기·폭은 실제 Q 와 공유하는 풀 인스턴스라 레이저 끝·정리 때 원래 값으로 되돌린다.
    - 이관: 옛 `chargeScaleFrom/To/Curve` 는 숨긴 칸으로 남아 프로필 `OnEnable` 에서 한 번 옮긴다(단계 크기 = Lerp(from, to, curve(k/3)), 1~3단계·발사 마커 = 차징 시작 + 0.3/0.6/0.9/1.2) — 끝나면 `chargeScaleFrom = -1`. 저장은 에셋을 다시 저장할 때.
    - 새 RPC·복제 값 없음 — 각 피어 로컬 표시. 테스트 `BossVictoryGunnerQTests`(마커 시각·단계 크기 보간·이관).
  - **차징 이펙트 세부 조절(295)** — 같은 프로필 `gunnerQ` 를 넓혔다. 이 피어 표시 전용이고 새 RPC 는 없다. 프리팹 `50.Art/VFX/Common/Players/Player2/Q_ChargeLaser/FX_GunnerCharge.prefab`(SVN)은 고치지 않는다. 대신 재생 중인 풀 인스턴스의 ParticleSystem 값을 배율로 덮고, 정리(`EndVictoryCharge`)·파괴 때 처음 값으로 되돌린다(`BossVictoryChargeFxApplier`, `Map/BossVictory/`).
    - **294 구체 크기가 안 보이던 원인** — 파트 셋(Charge 와 그 아래 ChargeParticle·ChargeLine)의 `scalingMode` 가 모두 Local 이다. 그래서 루트 `localScale` 을 받지 않았다. 지금은 구체 크기를 파트 크기·모양 반경·시작 속도에 곱한다(균일 스케일과 같은 모양). 그래서 294 단계 크기가 **이제 실제로 보인다** — 기존 프로필 값이 보이는 크기를 바꾼다.
    - **파트 = 렌더 모드로 가른다**(`BossVictoryChargeFxApplier.PartOf`):
      - Stretch = 빛줄기 `line` — 지금 프리팹에서는 오브젝트 이름 ChargeParticle.
      - Mesh = 입자 `particle` — 이름 ChargeLine.
      - 그 밖 = 코어 `core` — 지금 프리팹의 Charge(Billboard).
      - 오브젝트 이름이 화면에 보이는 모양과 반대라 이름으로 가르지 않는다.
    - **마커 값** `fxStart`(차징 시작)·`fxStage1~3`(`BossVictoryChargeFx`). 앞 마커 → 이 마커 사이를 구체 크기와 같은 구간·곡선으로 보간한다(`BossVictoryGunnerCharge.Segment`·`EvaluateFx`).
      - 파트마다(`BossVictoryChargePartFx`) 배율 칸: 크기 · 양(rate·버스트 수) · 반복 간격(초, 0 = 프리팹 값) · 수명 · 재생 속도(simulationSpeed) · HDR 밝기(재질 색 칸 MaterialPropertyBlock) · 길이(lengthScale, 빛줄기만).
      - 반복 간격은 한쪽이 0 이면 섞지 않는다. 도착 마커에서 계단식으로 바뀐다.
      - 총구 포인트 라이트 칸: 세기·범위·색. 차징 소켓 위치에 놓인다. 세기 0 = 없음.
    - **단계 도착 카메라 흔들림** — 칸 `stageN.shakeIntensity`·`shakeDuration`. 세기 0 = 없음, 길이 0 = 0.2초.
    - **발사**
      - 카메라 흔들림 `fireShakeIntensity`·`fireShakeDuration`. 흔들림은 단계 도착과 같은 기존 `CameraFeedback.PlayImpactShake`(설정창 배율 포함)를 쓴다.
      - 번쩍 라이트 `fireFlashIntensity`·`fireFlashDuration`·`fireFlashRange`·`fireFlashColor`. 발사 소켓에서 세기 → 0 으로 줄어든다.
      - 레이저 `beamBrightness` · `beamColorOverride`·`beamColor`(끄면 한계 돌파 색) · `beamFadeSeconds`. 페이드아웃은 유지 시간 뒤 알파 1 → 0 이다. 페이드가 있으면 끝에 `StopImmediate` 로 걷는다.
    - **최대 충전 알림** `burstAmount`·`burstBrightness`, **과충전** `overchargeAmount`·`overchargeBrightness`.
      - 원샷(`PlayOnce`)은 인스턴스에 닿을 수 없다. 그래서 알림 배율이 1 이 아니면 루프 핸들(`Play`)로 틀고 바로 덮는다(첫 시뮬레이션 전). 배율 1 이면 예전처럼 원샷으로 튼다.
    - **라이트** — 런타임 `Light` 1개("BossVictoryChargeLight", Point, 그림자 없음). 세기가 0 이면 꺼 두고, 정리 때 파괴한다.
    - **기본값 = 지금 모습**: 배율 1, 라이트·흔들림·번쩍·페이드 0, 색 덮어쓰기 끔. 밝기 배율이 1 이면 MaterialPropertyBlock 을 걸지 않는다 — 같은 값이라도 블록을 걸면 이 파티클 셰이더가 다르게 그려진다(EffectRecolor 11-03).
    - **같은 칸을 쓰는 다른 코드**
      - 재생 속도 = `ShurikenEffectSystem.SetPlayRate`, 색 = `EffectRecolor`.
      - 그 칸은 "마지막에 내가 쓴 값"과 지금 값이 다르면 밖에서 바뀐 것으로 본다. 그 값을 새 기준으로 삼고 그 위에 배율을 곱한다.
    - **창**
      - 차징 시작 마커 = `fxStart`.
      - 1~3단계 마커 = 흔들림 · `fxStageN` · 알림 배율. 3단계는 과충전 배율도 함께 보인다.
      - 발사 마커 = 발사 연출 칸.
      - 차징 시작·1~3단계 마커만 여러 개 고르면 '차징 이펙트(295) 공통 칸'이 보인다.
    - **안 하는 것**: 히트스톱, Scene 뷰 미리보기. 평소 Q 충전의 `ScaleChargeLoop`(루트 스케일)도 같은 이유로 안 먹지만 이번 범위가 아니다.
    - **한계** — 덮은 풀 인스턴스는 정리 때까지 덮인 채다. 그 사이 같은 풀을 다른 거너 Q 가 다시 쓰면 배율이 따라간다(처치 연출 중이라 드묾).
    - 테스트 `BossVictoryChargeFxTests`: 기본값 = 지금 모습 · 구간·파트 보간 · 반복 간격 계단 · 페이드 · 번쩍 · 기준 추적 · 곡선 배율 · 렌더 모드 → 파트.
  - **레이저·차징이 도중에 사라지던 버그(298)** — 소켓 안전 회수(`EffectSocketPlayer.safetyTimeout`, 프리팹 저작 레이저 `QBeamFx` 1초·차징 루프·과충전 3초·알림 1초)가 프로필 유지 시간(예: `beamHoldSeconds` 31.6)보다 짧았다. 그래서 "1.0초 동안 종료되지 않아 강제 회수" 경고와 함께 레이저가 꺼졌다. 처치 연출이 트는 차징·알림(루프로 틀 때)·과충전·레이저 소켓은 재생 직전에 안전 회수를 끈다(`EffectSocketSafetyOverride`, `EffectSocketPlayer.SetSafetyTimeout`). 정리(`EndVictoryCharge`) 때 저작 값으로 되돌린다. 테스트 `BossVictoryGunnerLaserTests`.
  - **레이저 정지 화면(299)** — 레이저 이펙트 `FX_GunnerQBeam`(SVN)은 파트 16개가 전부 원샷이다(대부분 0.05초, Beam 1초). 그래서 소켓을 붙잡고 있어도 1초 남짓이면 입자가 다 죽고, `beamHoldSeconds` 는 끄는 시각만 늦췄다.
    - 프로필 `beamFreezeAt`(발사 뒤 타임라인 초, 기본 0.12 = 빔이 다 뻗은 순간)에 그 레이저 인스턴스 입자를 전부 `Pause` 한다(정지 화면).
    - 유지 시간 끝에 다시 재생해 원래 소멸 꼬리가 나온다. 페이드아웃(295)은 유지 끝부터 기존대로다.
    - 유지 시간 ≤ 멈춤 시점이면 멈추지 않는다(예전 그대로).
    - 판정은 타임라인 초 기준 순수 함수 `BossVictoryGunnerCharge.BeamFrozen` → `Frame.BeamFrozen` 이라 시간 배율·글리치와 무관하다.
    - 멈춘 동안에도 밝기·페이드(색 블록)·폭은 그대로 먹는다. 레이저 끝·정리(`EndVictoryLaser`)에서 먼저 재개하므로 멈춘 채 풀에 돌아가지 않는다. 평소 Q 는 불변.
  - **레이저 3D 원통(300)** — 몸통 `LaserCore`·`LaserCoreDark` 는 Stretched Billboard(카메라를 향해 도는 평면)라 낮은 앵글·레이저 축 방향 컷에서 종이처럼 얇게 보였다.
    - 프로필 `beamMesh3D`(기본 켬)면 발사 순간 몸통(size3D·Stretch 인 시스템만 — 번개·불꽃·섬광·이동 빔은 그대로)을 Mesh 렌더 + 런타임 원통(`GunnerBeamMeshSwap` — 축 +Y·y∈[0,1]·반지름 0.5)으로 바꾼다.
    - 맞춤: 몸통 속도가 로컬 −Z 라 Stretched 꼬리는 +Z(= 레이저 끝)로 뻗는다 → 정렬 Local + 입자 회전 X +90°(메시 +Y → 로컬 +Z), 길이 = startSizeY(규약 "값 1 = 1m"), 지름 = startSizeX, 단면 원이 되게 Z = X.
    - UV: U = 길이(0 총구 → 1 끝, 텍스처가 가로로 긴 Trail), V = 둘레 0→1→0 삼각파(이음매 없음). U 방향이 Stretched 와 반대면 Play 에서 그라데이션이 뒤집혀 보인다 — 그때 `BuildCylinder` 의 U 를 1−y 로.
    - 되돌리기: 레이저 끝·정리 때 예약하고, 남은 소멸 꼬리 입자가 다 사라지면(LateUpdate) 빌보드로(도중에 바뀌지 않게). 파괴 때는 바로. 프리팹(SVN)은 그대로.
    - 창 발사 마커 칸 '레이저 멈춤 시점'. 테스트 `BossVictoryGunnerLaserTests`.
  - **레이저 흐름·진동·통과 지점·광원·반동(301)** — 프로필 `gunnerQ` 칸, 필드 기본 0/꺼짐(거너 프로필에만 시작값). 창 발사 마커 아래 네 묶음. 전부 각 피어 표시(반동 이동만 오너), 새 RPC 없음.
    - 흐름: `beamFlowSpeed`·`beamNoiseFlowSpeed`(UV/초) → 레이저 렌더러 블록으로 셰이더 `HS_Blend_CG` 의 `_SpeedMainTexUVNoiseZW.x`·`_DistortionSpeedXYPowerZ.x` 를 덮는다(+ = 원통 U 끝 쪽, 음수 = 반대, 0 = 재질 값). 색 지정이 블록을 비워도 매 프레임 다시 쓰고, 레이저 끝에 재질 값으로. 재질(SVN)은 그대로.
    - 진동: 발사부터 `vibrationSeconds`(레이저 유지와 별개) 동안 올림·유지·내림 비율(`BossVictoryGunnerCharge.Envelope`). ① 땅울림 = 기존 연출 흔들림(`PlayImpactShake`)을 `vibrationFrequency` 번/초 반복 ② 몸·총 떨림 = Armature 로컬 위치 + 펄린 노이즈(m) — 총구 소켓이 따라온다. 발사 한 번 흔들림(295)은 따로.
    - 통과 지점: `useBeamPassPoint` 켜면 발사 순간 총구 → `beamPassPoint`(보스 발밑 + 보스 수평 회전 기준 x 옆·y 위·z 앞) 방향으로 길이만큼(위아래로 기울 수 있음, `BeamDirection`). 방향은 레이저 끝까지 고정, 끝점은 매 프레임 지금 총구 + 방향 × 길이(반동·떨림 중에도 평행). 창이 열려 있으면 Scene 뷰 하늘색 구 + 핸들로 끈다(후퇴 차징 프로필·켰을 때만).
    - 광원(`BossVictoryBeamLight`): 비율 = 발사부터 `lightInSeconds` 차오름 → 레이저 유지 끝까지 → `lightRecoverSeconds` 돌아옴. ① 씬 조명 `SceneLightDim.Shared` 태양·환경광 × (1 − `lightSceneDim`×비율)(간파 암순응과 곱) ② 런타임 전역 Volume(우선순위 125) ColorAdjustments.contrast = `lightContrast`, 가중치 = 비율 ③ 레이저를 `beamLightCount` 등분한 칸 가운데 포인트 라이트(레이저 색 = 덮어쓰기 색 또는 한계 돌파 색). 정리 때 배율 반납·Volume·라이트 파괴.
    - 반동: 발사부터 `recoilSeconds` 동안 등속으로 `recoilDistance` m, 방향 = 발사 순간 레이저 반대쪽(수평). 오너만 `PlayerMotor.AddGroundedDisplacement`(FixedUpdate, 벽에서 멈춤 — 백대쉬와 같은 길, 겹치면 더해짐), 다른 피어는 위치 복제.
    - 테스트 `BossVictoryGunnerBeamFxTests`.
  - E 의 End 는 처치 연출 스킬 End 로 세지 않는다(`OnFinisherEnded` 가 스킬로 거름).
  - 카메라 = 기존 카메라 컷 트랙 `Anchor.Killer`(새 기능 없음).

## 4.14 연출 중 피해 숫자 제거(11-12)

처치 연출 동안(`Begin` ~ 끝·취소·파괴, 디버그 랩 F2/F7 리셋 포함) 피해·힐 숫자를 띄우지 않고, 시작 순간 떠 있던 숫자(막타 포함)는 지운다 — `BossVictoryGlitchPresentation.HidesDamageNumbers` 를 `FloatingDamageSpawner` 가 매 프레임 읽는다(연출 상태에서 파생이라 해제 누수 없음, 각 피어 로컬, 새 RPC 없음). [floating-damage-design.md §5](floating-damage-design.md).


## 4.15 카메라 컷 기준 '처치자 무기'(11-12)

컷 기준점(`BossVictoryCameraCuts.Anchor`)에 4번째 선택지 **처치자 무기**(`KillerWeapon`) — 숄더캠처럼 "총 오른쪽 얼마나"로 저작. 어느 캐릭터 컷에서나 고를 수 있다.

- **무기 찾기**(`BossVictoryWeaponMarker.Find`): ① 처치자 아래 표식 컴포넌트 `BossVictoryWeaponMarker`(첫 것) ② `WeaponTransformRelay` 첫 줄 무기(팔라딘 = 방패 줄) ③ 없으면 처치자 발밑 + 경고 1회. 거너는 총(`LaserGun`)이 본 자식이라 relay 가 없어 `Player_Gunner.prefab` 의 LaserGun 에 표식을 달았다(메뉴 `Tools/Boss Victory/거너 총에 처치자 무기 표식`, 있으면 그대로).
- **좌표계**: 원점 = 무기 위치, 축 = 처치자→보스 수평(다른 기준과 같은 축 정의 — +X 오른쪽·+Y 위·+Z 보스 쪽). 무기 회전·처치자 forward 를 쓰지 않은 이유: 둘 다 애니(조준·백대쉬)로 흔들려 숄더캠이 같이 돈다. 처치자→보스 축은 처치자가 늘 보스를 보는 처치 연출에서 '보는 방향'과 같고 안정적.
- **매 프레임 추종**: 이 기준만 컷 동안 처치자·보스·무기 위치를 매 프레임 다시 읽는다(나머지 기준은 컷 시작 1회 고정, 동작 불변). 무기 위치는 컷 칸 `weaponFollowSmoothing`(초, 지수 감쇠 시간 상수 — 프레임 속도 무관, 0 = 딱 붙음, unscaled)으로 부드럽게. 블렌드 중 들어오는 컷이 이 기준이면 그 컷도 매 프레임 따라가고, 블렌드 끝에서 같은 추종 값을 이어 쓴다(안 튐).
- **타임라인 창**: 컷 인스펙터 '기준' = "처치자 무기(매 프레임)" → '무기 따라가기 부드러움(초)' 칸. 'Scene 뷰로'·'이 카메라 뷰로 Scene 카메라 세팅하기' = Play 중이면 실제 무기 위치(지금 컷이 이 기준이면 화면과 같은 추종 값), 아니면 이 세션 마지막 회차 무기 위치(SessionState). 디버그 씬 재생·스크럽도 같은 경로(TickCuts).
- 각 피어 로컬 카메라, 새 RPC 없음. 순수 계산 `AnchorPoint`·`SmoothFollow`·`FollowWeapon` — EditMode `BossVictoryWeaponAnchorTests`.


## 4.16 보스 표현 = 노이즈 / 역림라이트(11-12 — 어쌔신)

'보스 노이즈' 트랙 클립 구간(켜고 끄는 시각·세기 그대로) 동안 보스를 무엇으로 덮을지 프로필 칸 `bossLook`(`BossVictoryBossLookMode`)이 고른다. 어쌔신 = **림라이트**, 팔라딘·거너 = **노이즈**(지금과 같음). 보스에만, 플레이어 불변.

- **림라이트(역림)**: 보스 실루엣 안쪽 전부 = 팔레트 칸 '보스 림 안쪽'(`RimInside`) × `bossRimInsideIntensity`(기본 2 — 1 넘으면 블룸이 받는 과노출), 테두리만 '보스 림 테두리'(`RimEdge`). 노이즈 텍스처 없음.
- **테두리 = 프레넬**(카메라 기준 모든 가장자리, 방향 치우침 없음): 마스크 패스(`BossVictoryGlitchMask.shader`)가 보스를 그릴 때 G 채널 = 1 − saturate(N·V)(플레이어 G 0). 합성(`BossVictoryGlitch.shader`)에서 림 가중치 = smoothstep(문턱 − 부드러움, 문턱 + 부드러움, G), 문턱 = 1 − `bossRimWidth`(기본 0.35), 부드러움 `bossRimSoftness`(기본 0.1). 같은 식 = `BossVictoryBossLook.RimWeight`. 새 텍스처·메시·패스 없음(기존 마스크 ID 텍스처의 빈 G 채널), 셰이더는 공용 설정 `maskShader`·`compositeShader` 칸 그대로(빌드 포함 참조).
- **색(271 팔레트)**: 칸 15 `RimInside`·칸 16 `RimEdge` — 다른 칸처럼 독립 색(11-16 관계 끊음 전엔 노이즈 두 색을 따라갔다, §4.11). 노이즈 밝은 색 배율 `bossNoiseBrightIntensity` 는 노이즈에만(림 안쪽은 `bossRimInsideIntensity`).
- **편집**: 타임라인 창 '보스 노이즈' 클립 노드 칸 '보스 표현(캐릭터별)'(모드·굵기·부드러움·안쪽 세기) + '팔레트' 패널 두 칸. Play 중 바꾸면 바로 반영. 디버그 랩(DebugBossVictory) F1 로 어쌔신을 고르면 바로 확인. 어쌔신 설정 메뉴 `Tools/Boss Victory/어쌔신 보스 표현 = 림라이트`.
- 각 피어 로컬 표시, 새 RPC 없음. EditMode `BossVictoryBossLookTests`(기본값·캐릭터별 모드·팔레트 칸·림 가중치·셰이더 오류 없음).
- 한계: 프레넬은 메시 법선 기준이라 평평한 면이 넓은 부위는 테두리가 굵거나 얇게 보일 수 있다(굵기·부드러움으로 맞춤). 마스크는 점 샘플이라 테두리 경계가 픽셀 단위.


## 4.17 카메라 컷 기준 '보스 정면'(297 — 어쌔신)

컷 기준점(`BossVictoryCameraCuts.Anchor`)에 5번째 선택지 **보스 정면**(`BossFacing`, 직렬화 값 4). 다른 기준은 축 = 처치자→보스 수평이라 컷 시작 순간 처치자가 어디 있느냐에 구도가 달린다 — 보스 기준으로 순간이동하는 어쌔신에겐 맞지 않았다(§4.13 '매번 같게').

- **좌표계**: 원점 = 보스 발밑(트랜스폼 위치), +Z = 보스 **수평** 정면(트랜스폼 forward 수평, 없으면 월드 +Z), +Y = 위. 순간이동 두 점(`BossPoint` — 보스 위치 + 보스 수평 회전 × 오프셋)과 같은 좌표계라 컷 키 위치 = 보스 로컬 오프셋. 처치자 위치를 안 쓴다 → 프레임 순서·끊김·복제 지연과 무관, 보스 위치·회전은 복제값이라 **모든 피어 같은 화면**. 컷 시작 1회 고정(다른 고정 기준과 같음).
- 계산 = `Frame`/`FrameForward`/`AnchorPoint`(`Subjects.BossForward` 추가 — 연출은 `_boss.transform.forward`, 타임라인 창 '마지막 회차' 기준도 SessionState 에 함께 저장).
- **어쌔신 이관(일회성)**: `BossVictoryAssassinDash.RebaseCutsToBossFacing` — 컷마다 위치를 잡는 시각(컷 시작, 블렌드가 있으면 블렌드 시작)의 **타임라인상 예상 처치자 위치**(`ExpectedLocal` = `BossPoint` 좌표 + `Progress`, 높이 0)로 옛 기준 좌표계를 만들어 같은 화면이 되게 키를 다시 계산(`BossVictoryCameraCuts.Rebase`). 같은 시각에 시작하는 클립은 아직 안 들어간 것으로 본다(런타임은 그 순간이동을 다음 프레임에 해, 컷이 그 앞 위치를 잡아 저작됐다 — 컷 2(1.18초) = 이동 #3 도착점). 이미 보스 정면·처치자 무기·첫 클립 전 컷은 건너뛴다(다시 해도 그대로). 정본 `BossVictoryProfile_Assassin.asset` 은 297 커밋에 결과가 들어 있다(컷 1 = 0.09초 예상 위치 · 컷 2 = 이동 #3 도착점). 다른 브랜치·복구용 메뉴 `Tools/Boss Victory/어쌔신 카메라 컷 보스 정면 기준 이관 (297)`. 팔라딘·거너 컷은 그대로.
- 옛 구도와 다른 점: 옛 구도는 처치자가 실제로 예상 위치에 있을 때만 맞았다(지금 = 늘 그 구도). 높이 원점이 '가운데'(처치자·보스 발 높이 평균) → 보스 발 높이라 둘의 높이가 다르면 그 절반만큼 위아래.
- 타임라인 창: 컷 인스펙터 '기준' = "보스 정면". 'Scene 뷰로'·'이 카메라 뷰로 Scene 카메라 세팅하기' 그대로(보스 기준).
- EditMode `BossVictoryBossFacingTests`(좌표계·BossPoint 일치·예상 위치·이관 화면 보존·처치자 위치 무관·타임라인 이관 건너뜀/멱등·클립 시작 프레임 보정), `BossVictoryCameraCutTests` 왕복 테스트에 새 기준 포함.

## 5. 알려진 한계

- (272) 거너 가까움 판정은 처치 순간 각 피어 화면의 처치자 위치로 한다(새 복제 값 없음) — 문턱 근처면 피어마다 E·백대쉬 유무가 갈릴 수 있다(이동은 오너 기준이라 위치는 결국 같다).
- (272) 어쌔신 순간이동은 오너가 옮기고 다른 피어는 복제로 받아, 그 피어에선 클립마다 Q 애니가 순간이동 위치 도착보다 반 RTT 먼저 시작될 수 있다. 일섬 끝점은 계산값이라 벽에 막혀 덜 갔으면 실제 위치보다 길다(보스 위치·방향에 따라 두 점이 벽 밖이면 도착점이 달라진다 — 297 은 진단 로그만, 회피는 미룸). 이동 중 애니 배율은 클립 길이에 맞춰 바뀌어(짧으면 빠르게) 원래 Q 속도와 다를 수 있다. 느려짐 단계 중에 클립이 있으면 이동 배율이 느려짐 배율보다 먼저다.

- 프로필은 회차 시작 때 고정 — 연출 중 처치자 캐릭터가 바뀌어도(랩 F1) 그 회차는 옛 프로필, 랩이 바로 처음부터 다시 튼다.
- 끝 자르기 값은 클립 시간(초) 하나 — 기본 프로필(지금은 코드 기본값)을 여러 캐릭터가 쓰면 캐릭터마다 클립 길이가 달라 같은 값이 다른 프레임이다(캐릭터 프로필을 만들면 따로).
- 클립 노드 실측 표본(`BossVictoryClipTiming`)은 클립 이름 기준이라 프로필과 무관(같은 클립이면 공유).
- 클립 복사는 같은 자리에 이미 같은 클립이 있어도 하나 더 생긴다(중복 확인 안 함 — Undo 로 되돌림).
