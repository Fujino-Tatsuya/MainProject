# handoff — 로비 데모 흐름이 타이틀을 거친다 (할 일 70, 2026-10-21)

> 대상: `qa/LobbyDemo` 48.3 QA 자동화(시나리오 러너 `Assets/1.Scripts/QA/**`) 담당. 운영 문서: [lobby-demo-flow.md](lobby-demo-flow.md) §1·§6.

## 바뀐 것
- `0.BootStrapScene` 부팅 → **1.TitleScene 이 먼저 뜬다**(호스트·클라·MPPM `Client` 태그 가상 플레이어 모두). 예전처럼 곧장 대기 씬/호스팅으로 가지 않는다.
- `DemoSessionDirector` 는 Awake 에서 설정 파일·전송 타임아웃만 준비하고 **루프를 멈춘 채 기다린다**. 타이틀 START 의 시작 연출이 끝나면 `TitleSceneManager` 가 `DemoSessionDirector.TryHandleTitleStart()` 를 부르고, 그때 `6.DemoConnectScene` → 호스팅/자동 접속이 시작된다.
- 그 뒤(판 끝 로비 복귀·끊김 재접속)는 종전과 같다 — 타이틀을 다시 거치지 않는다.

## QA 부트스트랩이 할 일
- 무인 시나리오는 타이틀에서 멈춘다. 각 인스턴스에서 **`DemoSessionDirector.TryHandleTitleStart()` 를 한 번 부르면** 타이틀 연출을 건너뛰고 바로 데모 흐름이 시작된다(반환 true = 시작했거나 이미 돌고 있음, false = 데모 흐름 꺼짐).
  - 부를 시점: **1.TitleScene 이 활성 씬이 된 뒤**를 권한다(GameManager.Start 의 타이틀 로드와 같은 프레임에 겹치는 경우는 확인하지 않았다). 두 번 불러도 두 번째는 무시된다.
- 타이틀 입력을 흉내 내려면 아무 키 → 메뉴 → START 버튼(`TitleFlowDirector.StartGame`) 순서이고 카메라 접근·시작 연출만큼(수 초) 기다려야 한다 — 직접 호출을 권한다.
- 코드: [DemoSessionDirector.cs](../../Assets/1.Scripts/Network/Session/DemoSessionDirector.cs) · 판단 [DemoSessionRules.ResolveTitleStart](../../Assets/1.Scripts/Network/Session/DemoSessionRules.cs).
