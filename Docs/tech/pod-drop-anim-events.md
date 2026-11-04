# 포드 발사·착륙 연출 — 애니 이벤트 구간 (할 일 106 · 121 · 150 · 198)

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

`발사 클립(DP_LaunchCycle) 시작 → … → 하강 시작 이벤트(종류 3) → launchEndWaitSeconds(기본 0.5) → 끝 페이드 아웃 launchEndFadeSeconds(1) → 다 검어진 순간 서버가 로딩`

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
