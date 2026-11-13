# 포드 발사·착륙 연출 — 애니 이벤트 구간 (할 일 106 · 121 · 150 · 198 · 230 · 284)

로비 포드 발사(`LobbyPodLaunch`)와 맵 포드 착륙(`MapPodArrival`)에서 **어느 구간을 클립 애니 이벤트가 정하고, 어느 구간을 SO 초가 정하는지** 한 곳에 모은다.
수치 원본은 `GameData.xlsx` Lobby 시트(`PodDropSettings`), 이벤트 원본은 SVN FBX 임포트 설정이다.

## 1. 이벤트가 있는 곳

- FBX: `Assets/50.Art/Environment/Models/Machinery/Platforms/DropPodLauncher/Consolidated/v001/SM_DropPod_LaunchStation_Combined_v001.fbx` (`.meta` = **SVN**)
  — 로비 발사대·맵 포드가 같은 FBX·컨트롤러(`AC_DropPod_LaunchStation_v001`)를 쓴다.
- 함수 이름 `PodClipEvent`(`PodAnimEventRelay`), **정수 인자 = 종류**:

| 종류 | 상수 | 뜻 | 실시간 콜백 |
|---|---|---|---|
| 0 | `KindStage` | 다음 단계(해치 열림·발사 끝·날개 접기 끝·다리 펴기 끝·해치 열기 끝) | 서버가 단계 확정 → 시각 복제 |
| 1 | `KindSpeedUp` | 로비 발사 클립 가속 1단계(배율 `launchFastSpeed`) | 전 피어 배율 변경 + 서버 시각 복제 |
| 2 | `KindSpeedUp2` | 로비 발사 클립 가속 2단계(배율 `launchFastSpeed2`) | 같음 |
| 3 | `KindDescent` | **로비 포드 하강 시작**(할 일 198) | 없음 — 이벤트 위치를 읽어 계산(§2) |

- 튜닝: FBX 임포트 창 → Animation 탭에서 이벤트를 끌어 옮긴다. 처음 넣기·검사 = `Tools/Lobby/포드 애니 이벤트 저작 (빠진 것만)` · `… 기본 위치로 재설정` · `… 검사`.
  - 하강 시작(종류 3) 처음 위치는 고정 프레임이 아니라 저작 메뉴가 발사 클립을 샘플링해 **포드가 아래로 내려가기 시작하는 프레임**을 찾는다
    (가장 크게 떨어지는 Transform 들의 하강 시작 프레임 다수결 — `PodAnimEventAuthoring.FindDescentFrame`).
- 이벤트를 옮긴 뒤엔 `.meta` 를 SVN 커밋하고 아트 핀(`art-svn.json`)을 올린다.
- 이벤트가 빠지면 클립 끝에서 넘어가고 경고한다(연출이 갇히지 않게).

## 2. 로비 발사 — 끝 페이드 (할 일 198)

`발사 클립(DP_LaunchCycle) 시작 → … → 하강 시작 이벤트(종류 3) → launchEndWaitSeconds(기본 0.5) → 끝 페이드 아웃 launchEndFadeSeconds(1) → 다 검어짐(EndBlackAt) → 암전 유지 launchEndBlackHoldSeconds(1, 할 일 230) → 서버가 로딩`

- **암전 유지(230)**: 사용자 요청 "화면이 완전히 암전된 뒤 1초 뒤 로딩 씬". `LoadAt = EndBlackAt + launchEndBlackHoldSeconds`(xlsx Lobby 행 `lobby.launchEndBlackHoldSeconds`). 0 = 예전처럼 다 검어진 순간 로딩.
  - 모든 피어가 같은 시간표(서버 시간 + 복제 시각)로 페이드를 그리므로, 서버가 로딩을 거는 순간 클라는 이미 유지 시간만큼 검은 채다 — 클라 서버시간 추정 오차·복제 지연이 유지 시간(1초)보다 작으면 깜빡임 없음.
    하강 이벤트가 없는 클립의 예비 기준(`_launchEndedAt` 복제)은 클라가 복제 지연만큼 늦게 검어지는데, 그것도 유지 시간 안에 흡수된다.
  - 로딩을 건 뒤에도 검은 화면(500)은 로비 씬이 내려갈 때까지(디스폰 → `Clear`) 그대로고, 로딩 화면(1000)이 그 위에 뜬다. HUD 숨김 규칙 그대로.

- 예전(121)엔 **발사 클립 끝** + 대기 1초였다. 이제는 하강 시작 기준이라 발사 클립이 끝나기 전에 로딩으로 넘어갈 수 있다.
- `launchEndWaitSeconds` 는 이름 그대로 두고 뜻만 "하강 시작 이벤트 → 페이드 시작"으로 바꿨다(xlsx 행 그대로, 값 1 → 0.5).
- **멀티 동기(새 복제 값·RPC 없음)**: 각 피어가 컨트롤러 클립의 `AnimationClip.events` 에서 하강·가속 이벤트 위치(클립 초)를 읽고,
  이미 복제된 값(시작·해치 열림 → 발사 시작, 가속 1·2단계 시각)으로 경과 시각을 계산한다 —
  `PodDropLobbyTimeline.LaunchElapsedAtClipSeconds`(= `LaunchClipSecondsAt` 의 역). 하강 이벤트보다 클립상 앞선 가속 시각이 아직 복제 안 됐으면 "아직 모름"(추측 안 함).
- 클립에 하강 이벤트가 없으면 발사 클립 끝(서버가 복제하는 `_launchEndedAt`)을 하강 시작으로 쓴다(경고 1회). 계산값은 클립 끝보다 늦지 않다.
- 인스펙터 시각표(`PodDropSettings`·`LobbyPodLaunch`)에 하강 시작·끝 대기·페이드·로딩 시각이 보인다.

## 3. 맵 착륙 — 앞 구간 건너뛰기 · 접기 끝부터 페이드 인 (할 일 198)

사용자 피드백 "연출이 너무 길다" → 연출 시작 시점을 뒤로 민다.

- **도착 페이드 인**(`fadeInSeconds`, 기본·asset 0 → **0.5**): 맵은 검은 화면으로 시작해 **날개 접기·다리 펴기 끝**(기존 끝 이벤트 — `deployedAt`)부터 밝아진다
  (`PodDropMapTimeline.FadeAlphaAt(elapsed, deployedAt)`).
- **앞 구간 건너뛰기**(`skipApproach`, 새 칸, 기본·asset **켬**): 검은 화면 시간이 늘지 않게 대기·빠른 낙하·급감속·날개 접기/다리 펴기를 건너뛴다.
  - 시간표: 대기·낙하·급감속 길이 0, 낙하·급감속 높이 = 연출 높이(5m), 접기·펴기 끝 = t=0(`EffectiveDeployedAt`) → 로딩이 걷힌 순간부터
    페이드 인(0.5s) + 자유낙하(√(2h/g) ≈ 1.01s) → 착지(이펙트·흔들림) → 해치 열기 → 디졸브.
  - 포드: 로딩 아래에서부터 `DP_WingFold`·`DP_GearDeploy` 를 클립 끝 자세로 재생(끝 이벤트는 기다리지 않는다). 서버는 시작 순간 `_deployedAt = 시작 시각`으로 확정.
  - 카메라: 처음부터 포드 추적(급감속 시각 = 0), 줌은 t=0 부터 그대로. 레터박스는 처음부터 덮여 있다 디졸브 때 걷힌다.
  - 대기·낙하·급감속 칸(`holdSeconds`·`fallHeight`·`fallSeconds`·`brakeDistance`·`brakeSeconds` 등)은 지우지 않았다 — 스위치를 끄면 예전 흐름으로 돌아가고,
    그때 페이드 인은 접기·펴기 끝 이벤트부터(그 전 낙하 구간은 검다).
- 새 복제 값·RPC 없음. xlsx Lobby 시트: `map.fadeInSeconds` 0 → 0.5(SVN r589 — 테이블 모드 Play 는 xlsx 값이 우선). `skipApproach` 는 bool 이라 데이터 테이블 대상이 아니다(숫자·문구만) — 테이블 모드에서도 SO(`PodDropSettings.asset`) 값을 쓴다.

## 4. SO 초가 정하는 구간 (이벤트로 못 옮김 — 클립이 안 도는 구간)

- 로비: t=0(출격 확정) ~ 해치 클립 시작 — 레터박스·시작 암전·순간이동·카메라 줌.
- 맵: 페이드 인·디졸브·레터박스 아웃·카메라 줌·흔들림 길이, (건너뛰기 끔일 때) 대기·빠른 낙하·급감속.

## 5. 레터박스·검은 화면 프리팹 (할 일 230)

로비 출격(`LobbyPodLaunch`)·맵 착륙(`MapPodArrival`)이 같은 화면 오버레이(`PodDropScreenOverlay`)를 쓰고, 그 캔버스를 **프리팹에서 고친다**.

- 프리팹: `Assets/2.Prefabs/UI/PodDrop/PodDropOverlay.prefab` — 루트 `Canvas`(Screen Space Overlay)·`CanvasScaler`, 직계 자식 `LetterboxTop`·`LetterboxBottom`(검은 `Image`)·`Fade`(전체 검은 화면, 알파 0·꺼짐). `Fade` 는 마지막 형제(띠 위에 그린다).
- 연결: `PodDropSettings.overlayPrefab`(`Assets/9.ScriptableObject/Lobby/PodDropSettings.asset`, 화면 칸). 비우면 코드가 같은 구성(검은 띠)을 만든다 = 예전 동작.
- 처음 만들기: `Tools/Lobby/포드 레터박스 프리팹 만들기 (없을 때만)` — 띠를 `letterboxHeight` 높이로 저장하고 `overlayPrefab` 칸이 비었으면 채운다. 있으면 덮어쓰지 않는다.
- **코드가 바꾸는 것(매 프레임)**: 위 띠 `anchorMin.y`·아래 띠 `anchorMax.y`(= 시간표 알파 × `letterboxHeight`), `Fade` 색의 알파·`enabled`, 루트 `sortingOrder` = **500 고정**(로딩 화면 1000·ESC 메뉴 900 아래, HUD 위).
  그 밖의 값 — 띠 색·스프라이트·재질, anchor x·offset, 자식 장식(띠 아래에 붙인 선·글자 등), `Fade` 의 RGB — 은 프리팹 값 그대로다.
- 이름 계약: 자식 이름 `LetterboxTop`·`LetterboxBottom`·`Fade` 를 바꾸거나 지우면 경고 한 줄 후 코드 기본 구성으로 대체한다. 장식은 이 세 오브젝트의 **자식**이나 새 형제로 붙인다.
- 띠 높이는 프리팹이 아니라 `PodDropSettings.letterboxHeight`(xlsx `Lobby` 시트 행)가 원본이다 — 프리팹에 저장된 높이는 편집 화면 미리보기용이고 연출 중엔 덮인다.
- 보면서 고치기:
  - Play 없이: 프리팹을 열면(Prefab Mode) 띠가 기본 높이로 보인다(Game 뷰 비율로 미리 보려면 Prefab Mode 의 Canvas 환경). 높이를 바꿔 보려면 `LetterboxTop`·`LetterboxBottom` anchor y 를 직접 움직여 보고, 확정 값은 `letterboxHeight` 에 넣는다.
  - Play 중: `letterboxHeight` 는 매 프레임 읽어서 인스펙터에서 바꾸면 바로 반영된다(테이블 모드면 Play 가 끝날 때 xlsx 값으로 되돌아간다 — 확정은 xlsx). 프리팹 에셋 수정은 **이미 만든 복제본에는 안 들어간다** — 다음 연출(다음 Play·다음 씬의 첫 연출)부터. Play 중 모양은 Hierarchy 의 `LobbyPodLaunch`/`MapPodArrival` 오브젝트 아래 `PodDropOverlay` 복제본을 고쳐 보고(Play 끝나면 사라짐) 프리팹에 옮긴다.
  - 출격·착륙 연출만 다시 트는 디버그 단축키·메뉴는 없다 — 로비 출격 집결 → 맵 착륙 정상 흐름으로 본다.
- 테스트: `PodDropScreenOverlayTests`(프리팹 값 보존·대체·커밋된 프리팹 계약).

## 6. 연출 중 플레이어 실루엣 끔 (할 일 230)

사용자 요청 "연출 상태에서는 캐릭터 실루엣 비활성화".

- 로비 출격: 출격 확정(시작 시각 복제)부터 로딩으로 로비 씬이 사라질 때까지(`ClearLocal`) 모든 플레이어의 벽 뒤 실루엣을 끈다 — 발사대 벽 뒤로 걸어 들어갈 때 윤곽선이 컷을 깨던 것.
- 맵 착륙: 진입(로딩 화면 아래)부터 **연출 끝**(`EndAt` — UI·튜토리얼이 돌아오는 순간, `ClearLocal`)까지. 예전엔 디졸브 끝(조작 복귀)에 되켰다 — 조작은 그대로 디졸브 끝에 돌아오고, 실루엣만 UI 와 같이 돌아온다.
- 방식: `PlayerSilhouetteSuppression`(`Rendering/Silhouette/`) — 스폰된 `PlayerSilhouetteTag` 마다 `SetSuppressed(true)`, 태그 수가 바뀌면 다시 찾아 늦게 스폰된 플레이어도 잡는다. 로컬 시각 값(복제 없음, 피어마다 자기 화면). 두 연출이 같은 묶음 클래스를 쓴다.
- 보스 승리 연출의 `PlayerSilhouetteFeature.SuppressedByCinematic`(피처 전체 끔, 정적 bool 하나)은 쓰지 않는다 — 여러 연출이 한 bool 을 쓰면 먼저 끝난 쪽이 다른 연출의 끔을 풀 수 있다. 보스 엘리베이터(`BossTeleportManager`)도 태그별 경로다.

## 7. 포드 베이 호박색 회전 경광등 (할 일 284)

로비 출격 때 포드 베이 벽 윗단의 경광등 3개가 호박색 빛줄기를 돌린다. 표시 전용 — **새 복제 값·RPC 없음**, 모든 피어가 같은 시간표로 켜고 끈다.

- **켜는 구간**: 포드 연출 시작(경과 0 = 출격 확정, 시작 시각 복제 — 레터박스·시작 암전과 같은 순간) ~ 끝 페이드가 **다 검어진 순간**(`EndBlackAt` = 하강 시작 + `launchEndWaitSeconds` + `launchEndFadeSeconds`, §2).
  하강 시각을 아직 모르면(+∞) 켜진 채다. 해치 열림(종류 0)보다 앞선 가장 이른 포드 연출 지점이라 이 시점을 골랐다(사용자 11-13 "그냥 포드 연출 시작부터").
  - 판정 = 순수 함수 `PodLaunchBeaconRules.IsOn(elapsed, endBlackAt)`.
- **회전 빨라짐**: 발사 클립 가속 단계마다 한 바퀴 주기 1.2초 → 0.8초(가속 1, 종류 1) → 0.5초(가속 2, 종류 2).
  단계 = 복제된 가속 1·2단계 시각(`_launchFastAt`·`_launchFast2At`)과 이 피어 발사 클립의 가속 단계(`_launchFastStage`, 자기 이벤트) 중 큰 쪽(`PodLaunchBeaconRules.Stage`).
- **끄는 곳**: 매 프레임 판정 + `LobbyPodLaunch.ClearLocal`(연출 취소·재시작·디스폰·로비 씬 내려감). 경광등 자체도 `Awake` 에서 꺼진 채 시작하고 프리팹 Spot 은 꺼진 채 저장돼 있다 —
  회차 반복(로비 씬 새로 로드)·재접속(늦게 들어온 피어는 복제 시각으로 같은 상태 계산)·튜토리얼 재시작 때 꺼진 상태로 돌아온다. 끄면 회전 각도 0 으로.
- **프리팹**: `Assets/2.Prefabs/Lobby/PodLaunchBeacon/PF_PodLaunchBeacon.prefab`(git — 기본 도형 Cylinder 받침 + Sphere 돔, 지름 약 0.3m, 콜라이더·그림자 없음, **Static 끔** — 회전하므로).
  - **바닥 무늬(284 후속, 11-13 사용자)** — 위에서 보면 원을 4등분해 마주 보는 두 칸만 호박색, 나머지 두 칸은 어두운 충돌 시험 표식 모양이 경광등과 함께 돈다.
    구조: `Aim`(로컬 X −30° — 프리팹 +Z 쪽으로 기울임) → `Rotor`(Y 회전, 코드) → `Spot`(X 90° = Rotor 의 −Y, 즉 빛 축) 하나. Rotor 의 Y 축이 빛 축이라 돌면 바닥 원은 제자리에서 무늬만 돈다.
    Spot = Realtime·그림자 끔·세기 40·거리 6·각 64°(안쪽 60° — 가장자리는 쿠키 원이 정한다), 쿠키 `T_PodLaunchBeaconCookie.png`(256², 흰 두 칸·검은 두 칸·원 밖 검정, 경계 3~4px 부드럽게, Clamp·밉맵 없음·비압축·Alpha from Grayscale — PowerShell System.Drawing 으로 만든 고정 PNG, 런타임 생성 없음).
    쿠키 원 반각 ≈ 30° = 기울기 30° 라 원의 가까운 끝이 경광등 바로 밑(벽 밑동)에 오고 바닥 쪽으로 약 3.5m 까지 비친다 — 벽 너머(락커룸)로는 원뿔이 넘어가지 않는다.
    **URP Light Cookies**: `PC_RPAsset` 의 `m_SupportsLightCookies` 가 꺼져 있어 켰다(Mobile·INab 에셋은 원래 켜짐 — 셰이더 변형은 이미 빌드에 들어가던 것).
    세기·각도·거리는 Spot Light 칸, 기울기는 `Aim` 회전, 방향은 씬 인스턴스 루트 Y 회전에서 고친다.
  - `PodLaunchBeacon` 칸: 색(Spot·돔 공용, 기본 호박색 1/0.55/0.08)·켜짐 돔 이미시브 배율(10)·꺼짐 배율(0)·단계별 한 바퀴 주기(1.2/0.8/0.5).
  - 돔 머티리얼 `M_PodLaunchBeacon_Dome`(URP Lit) — 이미시브는 `MaterialPropertyBlock` 으로만 바꾼다(277 `PropEmissiveAnimator` 와 같은 방식).
    머티리얼 `_EmissionColor` 를 아주 어두운 호박색, GI 플래그를 Realtime Emissive 로 둔 건 의도다 — 검정·None 이면 URP 검증이 `_EMISSION` 키워드를 꺼서 MPB 색이 안 먹는다.
- **씬**: `3.LobbyScene` 루트 `PodLaunch/PodLaunchBeacons` 아래 `Beacon_1_NW`(−4.25, 2.05, 11.75)·`Beacon_2_NE`(7.0, 2.05, 11.75)·`Beacon_3_W`(−4.25, 2.05, 7.0), `LobbyPodLaunch.beacons` 칸에 연결. 비우면 경광등 없음.
  루트 Y 회전(벽 반대쪽 = 프리팹 +Z): `Beacon_1_NW` 135°(남동)·`Beacon_2_NE` 180°(남)·`Beacon_3_W` 90°(동).
- **씬 암전(284 후속, 11-13 사용자)**: 경광등이 켜진 동안 씬의 다른 라이트를 끄고 경광등 Spot 만 남긴다. `LobbyPodLaunch` 칸 `beaconBlackout`(기본 켬)·`beaconBlackoutAmbient`(환경광·반사 배율, 기본 0.03).
  - 켬 전이 순간 1회 `FindObjectsByType<Light>` → 켜져 있던 것(경광등 Spot·태양 제외)을 끄고 목록 기억(`PodLaunchLightBlackout`), 끔 전이에 그것만 다시 켠다(원래 꺼져 있던 건 그대로, 사라진 건 건너뜀). 매 프레임 찾기 없음.
  - 태양(Directional, `SceneLightDim.FindSceneSun` — `RenderSettings.sun` 또는 켜진 첫 Directional)은 `SceneLightDim.Shared.SetSun(this, 0)`, 환경광은 `SetAmbient(this, 배율)` — 23호 차징·간파 암순응과 같은 공용 배율 창구라 겹쳐도 원복이 꼬이지 않는다. 다른 Directional 은 일반 라이트처럼 끈다.
  - 반사 세기 `RenderSettings.reflectionIntensity` 는 공용 창구가 없어 직접 × 배율, 끔 때 되돌린다(이미 활성 씬이 바뀌었으면 새 씬 값을 덮지 않는다).
  - 끄는 곳 = 켬 구간이 끝나는 프레임(다 검어짐) + `ClearLocal`(연출 취소·디스폰·`OnDestroy`). 로비 씬 기준 라이트 컴포넌트 39개(대부분 Realtime 점광원, 스폿 몇 개, Directional 1개 — 프리팹 인스턴스 안 라이트 포함 실제 수는 켤 때 콘솔 `[LobbyPodLaunch] 경광등 암전 — 라이트 N개 끔` 로그).
  - 모니터·LED 같은 **이미시브 머티리얼은 라이트가 아니라 그대로** 빛난다. 암전 중 다른 코드가 라이트를 다시 켜면 그 라이트는 켜진다(막지 않음). 각 피어 로컬, 새 복제 값·RPC 없음.
- **소리**: 따로 없다 — 기존 출발음(`Direct_Pod_Start`)만(사용자 결정).
- **알려진 한계**: Spot 그림자는 끈 채다. 284 후속에서 원뿔을 벽 반대쪽으로 기울이고 거리를 12 → 6 으로 줄여 서쪽 경광등 빛이 락커룸에 새던 것을 막았다 — 그래도 보이면 `Aim` 기울기를 키우거나 거리를 줄인다.
- 테스트: `PodLaunchBeaconTests`(켜고 끄는 구간·가속 단계·주기·회전·프리팹 배선(쿠키 포함)·켜고 끔), `PodLaunchLightBlackoutTests`(켜져 있던 것만 끄고 되돌림·Spot/태양 제외·멱등·사라진 라이트).
