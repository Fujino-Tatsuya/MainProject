# 보스 처치 연출 — 캐릭터별 프로필 (할 일 267)

> 계획: [PLAN-boss-victory-character-profiles.md](../history/PLANS/PLAN-boss-victory-character-profiles.md) · 연출 전체: [PLAN-boss-victory-glitch.md](../../PLAN-boss-victory-glitch.md)

## 1. 구조

| 무엇 | 어디 | 캐릭터별? |
|------|------|-----------|
| 타임라인 전체(카메라 컷·처치자 평타·마커·끝 자르기·느려짐·보스 노드·화면 효과 세기 클립·TV 꺼짐·로비 전환) | `BossVictoryProfile.timeline` | ✅ |
| 백대쉬(거리·시간·곡선·보정 각·후 대기) | `BossVictoryProfile.backDash*` | ✅ |
| 대상별 시간 배율 4개(하던 동작·처치자 End 후·팀원·보스 End 후) | `BossVictoryProfile.*Scale` | ✅ |
| 처치 연출 스킬 칸 | `BossVictoryProfile.finisherSlot` | ✅ |
| 팔레트 기준 색·대비 설정·칸별 직접 지정(271, §4.11) | `BossVictoryProfile.baseColor`·`contrast*`·`paletteOverrides` | ✅ (공용 색 = 기준 팔레트) |
| 화면 효과 모양(호박색·주사선·찢김·보스 노이즈·CRT 색·셰이더)·벽 숨김 기준·보스 피격 애니 상태·마커 옵션(포커스 블렌드·숨김 대상) | `BossVictoryGlitchSettings` | ❌ 공용 |

- 에셋: 공용 `Assets/Resources/BossVictory/BossVictoryGlitchSettings.asset` · 프로필 `Assets/Resources/BossVictory/Profiles/BossVictoryProfile_{Paladin,Gunner,Assassin}.asset`. 기본 프로필 에셋 `BossVictoryProfile_Default` 는 **11-12 삭제**(사용자 지시) — 공용 `defaultProfile` 칸은 비어 있다.
- **고르기**: 공용 설정의 `characterProfiles` 표(캐릭터 → 프로필). 캐릭터 = 기존 캐릭터 판별 `TutorialCharacter`(로스터 칸, `TutorialLocalPlayerInfo.ResolveCharacter`). 같은 캐릭터 두 줄이면 앞 줄. **표에 없거나 프로필이 비면 `defaultProfile`, 그것도 비면(정본) 코드 기본값** `BossVictoryProfile.CreateDefault()`(저장 안 되는 인스턴스 하나, 처음 쓸 때 로그 1회). 순수 함수 `BossVictoryProfileSelect.Select`.
- 처음 내용: 267 이전 설정 에셋 값을 그대로 복제(기본·세 캐릭터 같은 내용) → 그 뒤 캐릭터별 튜닝. 기본 프로필 에셋은 11-12 삭제 — 지금은 표에 없는 캐릭터·처치자를 못 찾은 회차만 코드 기본값(코드 초기값 타임라인 = 옛 기본 곡선, 처치자 검·백대쉬 동시 끔, 기준 색 #D6AB69)을 쓴다.
- 프리팹 수정 없음. `PlayerVictoryFinisher`(어느 프리팹에도 안 붙어 있던 Variant 스킬 칸 컴포넌트)는 삭제 — 스킬 칸은 프로필.

## 2. 네트워크

새 RPC·NetworkVariable 없음. 각 피어가 `BossVictoryGlitchPresentation.Begin` 에서 복제된 `VictoryState.KillerClientId` 로 처치자 `Player` 를 찾고 → 그 캐릭터 → 프로필을 회차 시작 때 고정한다(늦은 참가자도 같은 길). 호스트의 로비 전환(타임라인 '로비 전환' 마커 → `PresentationFinished`)도 같은 프로필 시계. 처치자를 못 찾으면(나감 등) 기본 프로필 — 칸이 비어 코드 기본값(모든 피어 같은 코드 값이라 결과 같음).

## 3. 편집법 — 타임라인 창 `Tools/Boss Victory/처치 연출 타임라인`

- **도구 막대 캐릭터 드롭다운**(팔라딘·거너·어쌔신) = 그 프로필을 편집. 옛 '미리보기 캐릭터'(스킬 클립 길이 표시)와 통합. ('기본' 줄은 기본 프로필 에셋 삭제와 함께 뺐다, 11-12.)
- Play 중 연출이 돌면 **실제 처치자 캐릭터를 따라간다**(드롭다운 잠김, '실제 처치자'). 처치자를 못 찾은 회차면 '코드 기본값'으로 보이고 고쳐도 저장되지 않는다.
- 표에 프로필이 없는 캐릭터를 고르면 코드 기본값이 보이고(고쳐도 저장 안 됨 안내) '이 캐릭터 프로필 만들기(코드 기본값)' 버튼. 메뉴 `Tools/Boss Victory/캐릭터별 프로필 만들기 (267)` 도 같은 길 — **기본 프로필 에셋은 다시 만들지 않는다**. 새 프로필 원본을 다른 캐릭터 복제가 아니라 코드 기본값으로 둔 이유: 팔라딘 등을 복제하면 그 캐릭터 전용 칸(손 검 번개·낙하 검 배율·백대쉬 동시)까지 딸려 온다. 비슷하게 시작하려면 노드 복사로 옮긴다.
- **노드 복사**: 클립·마커 우클릭 → '다른 캐릭터에 복사…' → 캐릭터 체크 → 복사. Ctrl/⌘+클릭으로 여러 노드를 골라 한 번에. 같은 시각으로:
  - 마커(백대쉬 시작·스킬 연출 시작·느려짐·보스 노드·로비 전환 등 하나뿐인 것) = **덮어쓰기**. 애니 클립 노드(스킬 연출 시작·보스 피격 애니)는 끝 자르기도 같이.
  - 클립(카메라 컷·화면 효과 구간·HUD 숨김) = **새로 추가**(카메라 컷 값·키 포함). 1개뿐인 트랙(TV 꺼짐·처치자 평타)은 덮어쓰기.
  - Undo 한 번(Ctrl+Z)에 전부 되돌림. 순수 함수 `BossVictoryTimeline.CopyMarkerFrom`·`CopyClipFrom`.
- 노드 인스펙터의 백대쉬·배율·스킬 칸은 지금 프로필 칸, 트랙 모양 값(색·노이즈 등)은 공용 설정 칸.
- 메뉴 `Tools/Boss Victory/캐릭터별 프로필 만들기 (267)` — 없는 캐릭터 프로필(팔라딘·거너·어쌔신)만 코드 기본값으로 만들어 표에 넣는다(덮어쓰지 않음, 기본 프로필 에셋은 안 만듦).
- **스크롤**(11-12): 위 시간 눈금 = 세로 고정·가로(시간 스크롤) 따라감, 왼쪽 트랙 이름 열 = 세로 따라감·가로 고정. 트랙이 넘치면 세로 막대(맨 밑에 트랙 1칸 여유), 확대해서 시간이 넘치면 맨 아래 가로 막대. 휠(트랙·눈금 위) = 줌, 휠(이름 열 위) = 세로 스크롤, 가운데 끌기 = 시간 이동. 아래 노드 인스펙터는 따로 세로 스크롤.
- **카메라 컷 → Scene 뷰**(11-12): 컷 인스펙터 '이 카메라 뷰로 Scene 카메라 세팅하기'(컷 시작) · '재생 헤드 N초 화면으로'(이동 컷 그 순간, 재생 헤드가 컷 구간 안일 때만). 자세 = 런타임과 같은 `BossVictoryCameraCuts.Pose`, 기준 = 'Scene 뷰로' 와 같은 처치자·보스 위치(Play 중 지금 회차 / 아니면 이 에디터 세션 마지막 회차). 기준이 없거나 Scene 뷰가 없으면 비활성 + 이유. 앞 컷과 섞는 블렌드 구간은 반영하지 않는다(이 컷 자신의 화면).

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
- **주황 채움**(`rectifyOrange*`): 마지막(흰) 원이 다 퍼진 뒤(생성 + 페이드 + 회색→흰 + 확장) `rectifyOrangeHold`(0.05초) 머물고, 레퍼런스(raw 12~23 프레임)처럼 가로줄(4px)마다 들쭉날쭉한 경계가 오른쪽 → 왼쪽으로 지나가며(0.1초, 들쭉날쭉 단어 폭 0.2) 흰색을 불투명한 주황으로 바꾼다. 색 = 왼쪽 #FFA53A → 오른쪽 #F8E862 보간(세기 `rectifyOrangeGradient`).
- 블룸: 글리치 합성은 `AfterRenderingTransparents`(후처리 전)라 RECTIFY 도 볼륨 블룸을 받지만, 값이 0~1(LDR)이라 맵 볼륨 threshold(0.9~1.25)를 거의 못 넘어 번짐이 없다 — 대신 전용 글로우(§4.8).
- 순수 함수 `BossVictoryRectifyFill`(원 상태 `Circle`·원 색·주황 시작/진행·와이프·색·단어 사각형·Rmax) → 셰이더가 글자 SDF 마스크에 그린다 — `rectifyFillCoversOutline`(기본 켬, 11-12 사용자 요청)이면 마스크를 외곽선 바깥 경계(sd = −외곽선 반두께, 바깥 AA 유지)까지 넓혀 색이 지나간 자리는 외곽선도 같은 색, 흰 외곽선은 색이 아직 닿지 않은 만큼(1 − 덮인 정도)만 남는다(첫 원 전 = 흰 외곽선만). 끄면 예전처럼 안쪽만·외곽선 흰색. 안쪽 가는 가로 주사선 칸(간격 화면 높이 0.3%, `rectifyScanlineDark`) — 11-12 사용자 요청으로 기본 0(끔), 칸은 다시 켤 여지로 남김. 덮기 끔일 때만 흰 외곽선이 그 위.
- 모양 값은 공용 설정 `rectifyCircles`·`rectifyCircleGray*`·`rectifyFillSoftness/CoversOutline`·`rectifyScanline*`. 시각 t 로 결정 — 스크럽·피어 동일, 새 RPC 없음. 복사 = 클립 추가.

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
- **느려짐 제외**(사용자 결정): 손 검 번개와 어쌔신 처치 오라(267 후속)는 '느려짐 시작' 뒤에도 원래 속도. `BossVictoryGlitchPresentation.KeepRealtime(handle)` 로 등록 → 처치자 이펙트 배율을 걸 때마다(`SetEffectRate`) 등록 핸들은 `EffectManager.SetPlayRate(handle, 1)`. 회차 시작·정리 때 비운다. (손 검 번개는 손 검을 따라가 원래도 `SetPlayRateForTarget(처치자 루트)` 에 안 걸리지만 같은 규칙으로 등록.)
  - 낙하 검은 월드 고정 원샷이라 처치자 배율에 원래 안 걸린다 — 느려짐 뒤에도 원래 속도로 떨어진다(맞추지 않음: 공중에서 거의 멈춘 칼이 되고, 마무리 낙뢰 타이밍이 클립 Custom0 과 어긋난다).
- 프리팹 수정 없음(런타임). 다른 캐릭터는 `PaladinSkillView` 가 없어 칸을 켜도 무시. 새 RPC 없음 — 복제 처치 상태로 각 피어가 `Begin`(늦은 참가자 같음). 순수 판단 `BossVictoryGlitchPresentation.UsesKillerSword`(EditMode `BossVictoryProfileTests`).

## 4.11 기준 색 → 팔레트 자동(271)

프로필 칸 하나(`baseColor`, 기준 색)로 처치 연출 화면 효과 색 15칸을 계산한다. 순수 함수 `BossVictoryPaletteCalc`(`Map/BossVictory/BossVictoryPalette.cs`), EditMode `BossVictoryPaletteTests`.

- **기준 팔레트 = 공용 설정 색 그대로**: `BossVictoryGlitchSettings` 의 amberColor·bossNoiseBright/Dark·rectifyColor·rectifyCircles[0..2] 좌우·rectifyCircleGrayColor·rectifyOrangeLeft/Right·rectifyGlowColor·crtOffGlowColor 는 이제 '팔라딘 기준 팔레트'다. 기준 색의 기준 = 공용 `amberColor`, 대비 기준 색의 기준 = 공용 `crtOffGlowColor`. (`rectifyWhiteColor` 는 옛 이관 전용 숨은 칸이라 빠진다.)
- **칸** = `BossVictoryPaletteSlot`(호박색·노이즈 밝음/어두움·RECTIFY 외곽선·①②③ 원 좌우·③ 회색·주황 좌우·글로우·TV 꺼짐). 직렬화 값이라 순서를 바꾸지 않는다.
- **기조 그룹 규칙**(HSV, sRGB 값): 색 = 공용 색에서 색조 += (새 기준 색조 − 공용 호박색 색조), 채도·밝기 ×= (새 기준 ÷ 공용 호박색). 호박색 칸 = 기준 색 자체. 1 이하(LDR) 색은 밝기 1 에서 자르고, 1 넘는 HDR 색은 세기를 비율 그대로 유지(노이즈 밝은 색 배율 `bossNoiseBrightIntensity` 는 색 계산 뒤 그대로 곱한다).
- **무채색 그대로**: 채도 < 0.02(흰 외곽선·③ 흰·③ 회색·글로우 흰색)는 색조가 없어 기준 색을 바꿔도 그대로.
- **대비 그룹**(①·② 원 좌우 4색 + TV 꺼짐 발광): 대비 기준 색을 따라간다(그룹 안 색 = 공용 TV 꺼짐 파랑 → 대비 기준 색 관계, 위와 같은 규칙). TV 꺼짐 발광 = 대비 기준 색 자체.
  - `contrastMode = Auto`(기본): 색조 = 기준 색 색조 + `contrastHueOffset`(기본 174.0697° = 공용 TV 꺼짐 파랑 − 공용 호박색, 팔라딘 결과 그대로 — 창 '색조 차이 = 공용에서 역산' 버튼). 채도·밝기 = 공용 TV 꺼짐 × 기조와 같은 비율 → **하한** `contrastMinSaturation`(0.45)·`contrastMinValue`(0.6).
  - **하한을 둔 이유**: 비율 규칙만 쓰면 기준 색이 회색(채도 0)일 때 대비 기준 채도도 0 → ①·② 원·TV 꺼짐까지 회색이 돼 RECTIFY 안쪽 색 연출의 '색이 터지는' 단계가 사라진다. 어두운 기준 색도 같은 이유로 밝기 하한. 기조 그룹은 하한 없이 회색·어둡게 따라간다(회색 캐릭터 = 회색 화면이 자연스러움). 팔라딘 값(채도 0.75·밝기 1)은 하한 위라 영향 없음.
  - `contrastMode = Manual`: `contrastColor` 를 그대로 대비 기준 색으로(하한 안 걸림).
- **칸별 직접 지정**: 프로필 `paletteOverrides`(칸·manual·색). manual 켬 = 그 칸은 그 색 그대로(HDR 가능), 끄면 자동(색은 다시 켤 때를 위해 남김). 같은 칸 두 줄이면 앞 줄. 기본 = 비어 있음(전부 자동).
- **처음 값**: 세 프로필(팔라딘·거너·어쌔신)과 코드 기본값 모두 기준 색 #D6AB69(= 공용 호박색) → 지금 색과 같다. 테스트는 값이 아니라 관계를 고정한다(기준 색 = 공용 호박색이면 공용 색 그대로, 오차 1e-3 — 세 프로필 기준 색은 사용자 튜닝 값, 코드 기본값만 #D6AB69 고정). 기본 프로필 에셋은 11-12 삭제.
- **런타임**: `BossVictoryGlitchPresentation.Begin` 이 처치자 프로필을 고르는 순간 `_palette` 를 계산해 `BossVictoryGlitchRender.Enable(settings, palette)`·TV 꺼짐 `BossVictoryCrtOff` 에 넘긴다. 새 RPC 없음 — 복제된 `KillerClientId` → 처치자 캐릭터 → 프로필 → 같은 팔레트(늦은 참가자도). 렌더러 `Enable` 은 이미 켜져 있으면 색만 다시 넣는다.
- **편집**: 타임라인 창 인스펙터 위 **'팔레트'** 버튼(또는 노란 틴트·보스 노이즈·TV 꺼짐·RECTIFY 트랙 칸의 '팔레트') — 기준 색·대비 모드/색조 차이/하한(또는 대비 색), 계산된 대비 기준 색, 15칸 견본(자동/직접 표시·체크 = 직접 지정·직접이면 색 편집). Play 중 연출이 돌면 바꾼 값이 바로 화면에(`RefreshLook`, TV 꺼짐은 다음 꺼짐부터). 트랙 칸의 공용 색을 고치면 기준 팔레트가 바뀌어 모든 캐릭터에 같은 관계로 퍼진다.

## 5. 알려진 한계

- 프로필은 회차 시작 때 고정 — 연출 중 처치자 캐릭터가 바뀌어도(랩 F1) 그 회차는 옛 프로필, 랩이 바로 처음부터 다시 튼다.
- 끝 자르기 값은 클립 시간(초) 하나 — 기본 프로필(지금은 코드 기본값)을 여러 캐릭터가 쓰면 캐릭터마다 클립 길이가 달라 같은 값이 다른 프레임이다(캐릭터 프로필을 만들면 따로).
- 클립 노드 실측 표본(`BossVictoryClipTiming`)은 클립 이름 기준이라 프로필과 무관(같은 클립이면 공유).
- 클립 복사는 같은 자리에 이미 같은 클립이 있어도 하나 더 생긴다(중복 확인 안 함 — Undo 로 되돌림).
