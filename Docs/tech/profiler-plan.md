# 계획 — 런타임 비주얼 프로파일러 (Graphy + 렌더패스 모듈)

> 브랜치: `feature/profiler` (development 기반). 승인 후 구현. 팀 머지는 팀장 PR 리뷰.

## 목표
플레이 중 화면에서 성능을 **예쁘고 직관적으로** 보는 런타임 오버레이.
범용 지표(FPS/메모리/오디오)는 검증된 라이브러리로, **렌더 패스별 ms**는 커스텀 모듈로.

## 접근 (확정: EditorWindow + IMGUI 병행)
UI 충돌 회피가 최우선 → **에디터 창**이 게임 uGUI와 완전 분리라 충돌 0.

- **메인: EditorWindow (`Tools > Profiler HUD`)** — UI Toolkit, 도킹 가능, 충돌 0, 예쁨.
  패스별 ms·FPS·드로우콜·GC·메모리 + 라이브 그래프. `Editor` 폴더 배치로 빌드 자동 제외. (에디터/MPPM 전용)
- **보조: 기존 IMGUI ProfilerHUD** — 실기기/스탠드얼론 빌드 프로파일링용으로 유지(IMGUI는 uGUI와 별도 레이어라 충돌 없음).
- **Graphy 도입은 취소** — uGUI Canvas+EventSystem이라 유일하게 충돌 위험이 있었음. 에디터 창이 더 깔끔.

## 작업 단계
1. **Graphy 도입**: `Packages/manifest.json`에 의존성 추가 (OpenUPM 스코프드 레지스트리 또는 git URL `com.tayx.graphy`). 버전 핀 고정.
2. **THIRD_PARTY_NOTICES.md**에 Graphy(MIT) 고지 추가.
3. **Graphy 배치**: 디버그용 프리팹/부트스트랩에 Graphy 매니저 추가(에디터/개발빌드 한정).
4. **RenderPassMonitor**: 현재 `ProfilerHUD`를 렌더 패스 ms + GPU ms 중심으로 슬림화(또는 그대로 병행). 신 Input System, `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 유지.
5. **검증 (Unity MCP)**: 재시작 후 unity MCP로 플레이모드 진입 → 스크린샷·콘솔 확인(에러 0). 패스 ms·FPS 그래프 표시 확인.
6. **정리**: 동작 확인되면 feature/profiler 푸시 → development PR(팀장 리뷰).

## 리스크 / 체크포인트
- **manifest.json 의존성 추가는 팀 전체 영향** → 머지 전 팀장 합의 필요(브랜치 단계에선 격리됨).
- Graphy uGUI Canvas/EventSystem가 기존 UI와 충돌하지 않는지 확인.
- Graphy의 Unity 6 호환 설치 방식(정확한 git URL/패키지 경로) 구현 시 검증.
- **MCP 브릿지 상태가 stale**(Gladiator/포트6400/4월): MainProject로 Unity가 열려 있고 브릿지가 그쪽에 바인딩되는지 먼저 확인.
- 릴리스 빌드 제외 게이트 유지(미싱 스크립트 방지).

## 비범위 (YAGNI)
- 지금은 완전 커스텀 UI Toolkit 대시보드는 만들지 않음(Graphy 재사용).
- 원격 텔레메트리/저장/리플레이 등은 범위 밖.

## 검증 상태
작성 환경에 Unity 없음 → 컴파일/플레이 검증은 MCP 연결된 재시작 세션 또는 사용자 에디터에서 수행.
