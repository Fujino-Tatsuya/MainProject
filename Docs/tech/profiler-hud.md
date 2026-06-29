# ProfilerHUD — 런타임 프로파일링 오버레이

`Assets/1.Scripts/Dev/Profiler/ProfilerHUD.cs`

빌드/플레이 중 화면에 성능 지표를 띄우는 가벼운 디버그 HUD. 외부 패키지 의존성 없음(Unity 내장 IMGUI + `ProfilerRecorder` + `FrameTimingManager`).

- **에디터/개발 빌드 전용** — `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 로 감싸 릴리스 빌드에서는 컴파일 제외.
- 프로젝트 기준: Unity 6000.3.16f1 / URP 17.3 / 신 Input System.

## 표시 항목

- FPS / 프레임 ms (프레임 예산 대비 🟢🟡🔴 색상)
- CPU / GPU / Main Thread ms
- Draw Calls / SetPass / Triangles / GC alloc(KB) / 사용 메모리(MB)
- **렌더 패스별 ms** (인스펙터 Custom Markers — 기본 `FullScreenFog`, `FullScreenFog CopyBack`)
- 프레임타임 미니 그래프 + 예산 라인

## 사용법

1. 빈 GameObject 에 `ProfilerHUD` 컴포넌트 추가. 추가 시 `Reset()`이 이 프로젝트의 실제 패스 마커를 자동으로 채움.
2. 플레이 → **F8** 토글(인스펙터에서 키 변경).
3. 다른 RenderGraph 패스를 추적하려면 Custom Markers 에 항목 추가:
   - **Marker Name = `AddRasterRenderPass<T>("이름", ...)` / `AddComputePass<T>("이름", ...)` 의 "이름"**.
   - 이름이 틀리면 `(마커 없음)`으로 표시됨.

## 주의 / 트러블슈팅

- **릴리스 씬/프리팹에 붙여두지 말 것.** 릴리스 빌드엔 클래스가 없어 미싱 스크립트 경고가 남. 프로파일링할 때 수동으로 붙였다 떼는 용도.
- **GPU ms 가 0/N/A** 면 Project Settings > Player > Other Settings > **Frame Timing Stats** 활성화. 값은 몇 프레임 지연되어 채워짐. 미보고 플랫폼에서는 총 프레임 ms 가 `deltaTime` 폴백으로 표시되고 패스별 CPU ms 는 정상 동작.
- 패스 ms 는 15프레임 평균. 컴퓨트 디스패치는 CPU 쪽엔 "제출 비용"만 잡힐 수 있으니 정확한 GPU ms 는 RenderDoc / Nsight 로 교차 확인.

## 깊은 분석 도구(무료, 별도)

- **RenderDoc** — 프레임 캡처, 패스/버퍼/텍스처 시각화
- **NVIDIA Nsight Graphics** — GPU 하드웨어 타이밍, 컴퓨트 병목
- **Unity Frame Debugger / Rendering Debugger(URP)** — 드로우콜·오버드로우

## 검증 상태

표준 Unity API 만 사용(`ProfilerRecorder`, `FrameTimingManager`, IMGUI). 작성 환경에 Unity 가 없어 **에디터 컴파일/플레이 검증은 미완** — feature/profiler 브랜치에서 한 번 열어 확인 후 development 로 올린다.
