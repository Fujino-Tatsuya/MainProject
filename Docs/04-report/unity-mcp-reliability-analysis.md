# Unity MCP 브릿지 신뢰성 분석 리포트

- 작성: 2026-07-16 (분석 전용 — 구현 없음, 코드/설정 무변경)
- 대상: `com.community.unity-mcp` 로컬 포크 (Node `mcp-bridge.js` 232줄 + C# Editor 서버 ~2,200줄)
- 분석 방법: opus 에이전트 전수 정독 + 핵심 주장 5건 메인 세션 직접 재검증 + sonnet 에이전트 외부 구현체/공식문서 조사
- 표기: **[확정]** 코드로 직접 확인 / **[유력]** 구조상 강한 뒷받침, 런타임 관측 필요 / **[추정]** 가설

---

## 0. 요약

| 증상 | 근본원인 | 등급 |
|---|---|---|
| ① 컴파일 등에서 MCP 스레드 끊김/재기동 | 도메인 리로드마다 서버 파괴/재생성은 **설계상 정상**. 문제는 재기동 시 포트 재바인딩 실패하면 **1회 포기하고 죽어있음** (`McpServer.cs:95-99`) | 확정+유력 |
| ② Play 모드 오동작 | 펌프는 Play 중에도 돎(오해 주의). 실원인 = (a) Play 진입/이탈마다 도메인 리로드 창이 열림 (b) 입력 시뮬레이션이 에디터 창 이벤트라 게임 입력에 미도달 (c) `enter_play_mode`가 전환 완료 전 조기 응답 | 확정 |
| ③ 작업은 끝났는데 클라 무한대기 | **완료와 응답의 분리**: 타임아웃/리로드로 대기 포기 후에도 큐 액션은 나중에 실행됨 + 리로드 유발 명령의 in-flight 응답이 워커 중단으로 유실(선응답/저널 없음) + 모달 다이얼로그로 펌프 무기한 정지. Claude Code stdio idle 기본 30분이라 체감상 무한 | 확정 |

가장 효과 큰 개선 3개: **(1) 큐 액션 취소 + 비멱등 재시도 게이팅(반나절), (2) `Start()` 백오프 재시도 + MPPM 클론 가드(반나절), (3) 리로드 생존 pending+poll 구조(2~3일, CoplayDev 검증 패턴 이식)**.

---

## 1. 아키텍처 (요지)

```
Claude Code ←stdio(JSON-RPC/줄단위)→ mcp-bridge.js(Node) ←HTTP POST /message→ Unity HttpListener(:3000)
                                            └─GET /sse (킵얼라이브만, 응답 전달엔 미사용 = 死코드)
```

- C# 서버: `[InitializeOnLoad]`로 자동시작, 리스너 스레드 + 요청당 스레드풀 워커, 메인스레드 디스패치는 `EditorApplication.update` 펌프(`_mainThreadQueue`).
- 리로드 훅: `beforeAssemblyReload → Stop()` **만** 존재. `afterAssemblyReload`/`SessionState`/`playModeStateChanged` 사용처 **0건** (grep 재확인). 재기동은 `[InitializeOnLoad]`+`delayCall` 재실행에만 의존.
- 타임아웃: 서버 30s(WaitAny) / Node 45s(req.setTimeout, 타임아웃 시 재시도 안 함) / Node 연결실패(ECONNREFUSED·ECONNRESET·EPIPE)만 5회×1.5s 재시도.
- 포트: 양쪽 3000 하드코딩, 디스커버리 없음. `~/.unity-mcp/unity-mcp-status-*.json`은 **타 프로젝트(Gladiator)의 고아 파일**로 현 배선과 무관 — 진단 시 오도 주의.
- 브릿지 사본 이원화: 실행본 `~/.unity-mcp/mcp-bridge.js` vs 패키지 `Bridge/mcp-bridge.js` (2026-07-16 현재 바이트 동일, 드리프트 감시 없음).

---

## 2. 증상별 근본원인 상세

### ① 컴파일 시 끊김/재기동
- **[확정]** `beforeAssemblyReload → Stop()` → AppDomain 언로드로 전 스레드 소멸 → 리로드 후 `[InitializeOnLoad]` 재실행 + `delayCall`로 `Start()`. "끊겼다 다시 도는" 현상 = 이 사이클 자체. (이 Stop() 훅은 기존 최적화로 이미 들어간 것 — 없으면 더 나빴음)
- **[유력]** 간헐적 "리로드 후 안 붙음": `Stop()`이 리스너 스레드만 `Join(1000)` 후 리턴 → OS가 3000 해제 전에 `AutoStart`가 재바인딩 시도 → `HttpListenerException` → **catch에서 로그만 남기고 영구 포기** (`McpServer.cs:95-99`). 재시도/백오프/포트폴백 전무. 콘솔 `[MCP] Failed to start server: 각 소켓 주소는 하나만...`이 이 경로의 증거.
- **MPPM 변수**: 가상 플레이어(클론 에디터)도 `[InitializeOnLoad]`가 각자 실행되어 3000 바인딩 경쟁. 본체가 물고 있으면 클론은 실패(무해하나 로그 소음 + 어느 인스턴스가 서빙하는지 비결정). 클론 감지 스킵 없음.

### ② Play 모드 오동작
- **[확정]** "플레이모드라 디스패치가 멈춘다"는 가설은 코드상 성립 안 함 — `EditorApplication.update` 펌프는 Play 중에도 발화.
- **(a) [확정/유력]** 기본 설정(Reload Domain on Enter Play Mode)에서 Play 진입/이탈마다 증상①의 파괴/재생성 창이 열림. `unity_enter_play_mode`는 `EditorApplication.isPlaying=true` 직후 **전환 완료 전에 `isPlaying:true` 조기 반환** (`EditorTools.cs:103-111`) → 클라가 "이미 플레이 중"으로 오인한 채 다음 호출이 리로드 창에 부딪힘.
- **(b) [확정]** `unity_simulate_key/mouse`는 `EditorWindow.focusedWindow.SendEvent()`(`InputTools.cs:61-65,116-119`) — 에디터 IMGUI 이벤트라 게임 런타임 입력(특히 new Input System)에 미도달. 플레이 중 조작 테스트가 안 되는 직접 원인.
- **(c) [추정]** 스크린샷의 `Camera.main.targetTexture` 교체+수동 `Render()`(`ScreenshotTools.cs:94-97`)는 URP/Play 중 라이브 렌더와 충돌 가능(검은 프레임 등). 런타임 확인 필요.

### ③ 완료됐는데 무한대기 (응답 유실)
- **경로 1 [확정구조/유력원인] — 완료/응답 분리의 핵심**: 워커가 30s 타임아웃 또는 `_shutdownEvent`로 대기를 포기하고 에러를 회신해도, **이미 큐에 들어간 액션은 취소되지 않음** (`McpServer.cs:294-322`). 펌프 재개 시 액션이 실행되어 **부작용(오브젝트 생성/씬 저장 등)은 발생**하고 결과는 버려짐 → "작업은 끝났는데 클라는 에러/대기"에 정확히 부합.
- **경로 2 [확정구조/유력]**: 리로드 유발 명령(recompile, AssetDatabase.Refresh, 임의 메뉴)의 처리 중 AppDomain 언로드 → POST 회신 write가 중단되어 응답 유실. **선응답(respond-before-reload)도 리로드 후 재응답(저널)도 없음.** 이때 Node는 ECONNRESET을 "서버에 안 닿음=재시도 안전"으로 판정해 재전송(`mcp-bridge.js:156-161`) — 그러나 ECONNRESET/EPIPE는 **요청이 이미 도달한 뒤에도** 발생하므로 **비멱등 작업 이중 실행** 위험 (주석의 가정이 틀림; ECONNREFUSED만 안전).
- **경로 3 [확정구조/유력]**: 메인스레드 모달 — `unity_open_scene/new_scene`의 `SaveCurrentModifiedScenesIfUserWantsTo()`(`SceneTools.cs:79,149`), 임의 `execute_menu`. 모달이 뜨면 펌프 정지 → 사람이 닫을 때까지 전 요청 무기한 정지.
- **경로 4 [잠복 증폭요인]**: 서버가 응답 id를 항상 문자열로 에코(`JsonRpcHandler.cs:133-134,152-153`) → 숫자 id 타입 변형. 평소엔 클라 관용 매칭으로 은폐되나 유실을 증폭할 수 있는 결함.
- **클라이언트 한도**: Claude Code stdio MCP는 요청당 기본 타임아웃이 사실상 없고(전역 기본 ~28h) idle 기본 30분 → 브릿지가 에러라도 못 보내는 경우(위 경로들) 체감상 무한대기. `.mcp.json` 서버 항목에 `timeout`(ms) 필드로 상한 설정 가능.
- **반증된 가설**: "스크린샷 등 비동기 completion 미설정"은 해당 없음 — 스크린샷은 한 액션 안에서 동기 완료(`ScreenshotTools.cs:15-63`).

### 과거 실측 장애 모드 (이번 코드 분석과 별개, 메모리 기록)
- `mcp__unity__*`는 "Cannot reach"인데 `curl localhost:3000`은 즉답 → **node 브릿지 프로세스만 wedge**. 복구는 `/mcp` 재연결 또는 CC 재시작. 원인 미규명(IPv6(::1) vs IPv4 해석 불일치 가설 포함) — 하트비트/자가진단(D3) 도입 시 함께 계측 권장.

---

## 3. 그 외 발견 결함 (증상 무관, 신뢰성 영향)

- SSE 응답 채널 死코드: `SendNotification` 호출처 0. Node의 SSE 재연결은 복구에 기여 안 함. SSE 워커가 연결당 `Thread.Sleep(1000)` 루프로 스레드풀 점유 → 리로드 반복 시 워커 누적 가능.
- 수제 정규식 JSON 파싱(`JsonRpcHandler.cs:172-289`): 인자에 `"method"`/`"name"` 키 중첩 시 오추출 위험.
- JsonUtility 직렬화 함정(과거 세션 검증): 익명타입 `new { error = ... }` → `{}`로 나가 **에러 메시지 유실**. `EditorTools.cs:100` 등 에러 반환 경로 다수 해당.
- 스크린샷 base64 PNG를 상한 없이 JSON 이중 직렬화 → 대용량 페이로드.
- `StreamReader.ReadToEnd()` 읽기 타임아웃 없음(`McpServer.cs:281`).
- Unity 공식 트러블슈팅 경고: Canvas/RectTransform 등 UI 컴포넌트에 `get_components`류 호출 시 에디터 프리징 사례 — 본 프로젝트도 UI 사용하므로 확인 가치.
- 버전/문서 드리프트: package.json 2.2.0(2024-12) vs 실코드는 개인 최적화 다수 미문서화.

---

## 4. 개선 로드맵 (우선순위)

### 0단계 — 설정만, 코드 0줄 (즉시)
| # | 내용 | 효과 |
|---|---|---|
| 0-1 | `.mcp.json` unity 항목에 `"timeout": 120000` 추가 | 무한대기 → 2분 뒤 명확한 타임아웃 에러로 전환 |
| 0-2 | 고아 상태파일 `~/.unity-mcp/unity-mcp-status-3cc3188b.json` 삭제(타 프로젝트 잔재) | 진단 오도 제거 |
| 0-3 | (선택·팀 합의) Enter Play Mode Options에서 Reload Domain off 실험 | 증상①② 빈도 급감. 단 static 상태 유지 부작용 — NGO 싱글톤/static 초기화 의존이 있으면 위험, 실험 후 결정 |

### 1단계 — 퀵윈 (각 0.2~0.5일)
| # | 내용 | 위치 | 근거/선례 |
|---|---|---|---|
| 1-1 | 타임아웃/셧다운으로 대기 포기 시 **큐 액션 취소 플래그**(포기된 요청은 실행 스킵) | `McpServer.cs:294-322` | 증상③ 경로1 직접 차단 |
| 1-2 | Node 재시도를 **멱등 명령 화이트리스트로 게이팅**(조회성만 재시도, ECONNRESET/EPIPE는 비멱등 재시도 금지) | `mcp-bridge.js:156-161` | 이중 실행 차단 |
| 1-3 | `Start()` 실패 시 **지수 백오프 재시도**(0.25s→5s, 최대 10회) | `McpServer.cs:95-99` | CoderGamester/mcp-unity 검증 패턴 |
| 1-4 | `Application.isBatchMode` 가드 + **MPPM 클론 에디터 서버 기동 스킵**(본체만 호스트) | `McpServer.cs` AutoStart | CoderGamester 패턴, MPPM 실사용 프로젝트라 직결 |
| 1-5 | `enter/exit_play_mode`를 `playModeStateChanged`로 **전환 완료 후 정확 상태 응답** | `EditorTools.cs:103-111` | 증상② (c) 제거 |
| 1-6 | 사본 이원화 해소: `.mcp.json`을 패키지 `Bridge/` 직결 또는 시작 시 해시 비교 드리프트 경고 | `.mcp.json`, `McpServerWindow.cs:29-44` | 운영 사고 예방 |
| 1-7 | 에러 반환을 JsonUtility-안전 타입으로 통일(익명타입 `{}` 유실 제거) | Tools 전반 | 진단성 |

### 2단계 — 구조 개선 (1~3일)
| # | 내용 | 설계 요지 |
|---|---|---|
| 2-1 | **리로드 생존 pending+poll** (증상③ 구조적 해결) | 리로드 유발 명령은 "accepted" **선응답** 후 실행. 작업 상태를 `Library/McpState_*.json`(또는 `SessionState`)에 저널 → `afterAssemblyReload`에서 재기동+미완 작업 상태 복원 → 클라가 `status` 폴링. CoplayDev unity-mcp의 `RequiresPolling`+`McpJobStateStore` 검증 패턴 이식. 폴링 상한(예: 10분) 필수 |
| 2-2 | `afterAssemblyReload` 재기동 훅 추가(delayCall 단일 의존 탈피) + `playModeStateChanged`에서 Play 전환 신호를 Node/클라에 노출 | CoderGamester는 커스텀 WS 종료코드로 신호 — 우리는 SSE 이벤트로 대체 가능(死코드 SSE의 부활 용도) |
| 2-3 | 하트비트 정식화: Node가 `ping` 주기 폴링 → "reloading/alive" 상태를 에러 메시지에 반영, node-wedge 장애 모드 계측 | 기존 `_lastMainThreadPump` 하트비트 확장 |
| 2-4 | 모달 방지: `open_scene/new_scene`을 무대화(예: 자동저장 or 저장 스킵 옵션 인자화), `execute_menu`에 모달 유발 메뉴 경고 목록 | `SceneTools.cs:79,149` |
| 2-5 | 입력 시뮬레이션을 new Input System 경로(`InputSystem.QueueStateEvent` 등)로 교체 | 증상② (b). Play 중 조작 테스트가 목표일 때만 투자 |

### 보조 (여유 시)
- 정규식 JSON 파싱 → 실제 파서 교체, id 타입 보존, 스크린샷 페이로드 상한/축소 옵션, `ReadToEnd` 타임아웃, SSE 워커 정리(2-2와 통합), 포트 디스커버리(IvanMurzak식 프로젝트경로 해시 포트 — MPPM 클론과도 궁합).

### 반면교사 (외부 이슈에서)
- CoplayDev #1173: 리로드 대기 상수(2000ms→500ms) 근거 없이 줄였다가 좀비 포트 회귀 발생 — **타이밍 상수 축소 금지**.
- Arodoid/UnityMCP: 방치 시 기본 연결 문제도 안 고쳐짐 — 참고 제외.
- NGO#2900: MPPM+Rider 조합에서 클론 에디터가 도메인 리로드를 건너뛰는 버그 — 클론이 stale일 때 MCP 탓으로 오진하지 말 것.

---

## 5. 구현 시 검증 기준 (수용 테스트 초안)

1. 스크립트 수정→컴파일 10회 반복 후 `unity_get_editor_state` 즉답 (재기동 실패 0회)
2. `unity_recompile_scripts` 호출이 30초 내 "accepted"류 응답 반환 + 컴파일 완료 후 상태 폴링으로 결과 확인 가능
3. Play 진입→퇴장 5회 반복 중 매 단계 도구 호출이 명확한 응답(성공 or "reloading, retry") 반환 — 무응답 0회
4. 타임아웃 후 늦게 완료된 작업이 이중 실행되지 않음(비멱등 명령 재시도 게이팅 확인)
5. MPPM 가상 플레이어 2개 기동 시 클론의 3000 바인딩 시도 로그 0건
6. 미저장 씬 상태에서 `unity_open_scene` 호출 시 모달로 정지하지 않음

## 6. 미확인/추정으로 남긴 것
- Enter Play Mode Options 현재 설정값(에디터 확인 필요) — 증상② (a)의 전제
- 스크린샷 URP 충돌(② (c))은 런타임 재현 필요
- node-wedge 장애 모드의 근본 원인(IPv6/IPv4 가설 미검증)
- Unity 6000.3.16f1에서 `[OnCodeDeinitializing]` 계열 신규 콜백 실재 여부(문서는 6000.5 기준) — `AssemblyReloadEvents` 사용이 안전
- 외부 조사 중 upstream(usmanbutt-dev)은 이슈 0건 저활동 repo로 참고 가치 없음이 결론

## 7. 참고 링크
- CoplayDev/unity-mcp — Long-Running(Polled) Tools 가이드, `McpJobStateStore.cs`, Issue #1173/#657/#814/#672
- CoderGamester/mcp-unity — `McpUnityServer.cs` (백오프·isBatchMode·MPPM·playMode 신호)
- IvanMurzak/Unity-MCP — Troubleshooting 위키(재연결=정상 문서화), 해시 기반 포트
- Unity 매뉴얼 — Enter Play Mode Options / Code lifecycle / AssemblyReloadEvents
- Claude Code MCP 문서 — `MCP_TIMEOUT`, `MCP_TOOL_TIMEOUT`, `.mcp.json` `timeout`, stdio idle 30분

---

## 부록 (2026-07-19 실측): 구현 중 추가 발견된 근본원인 3건

구현·수용테스트 과정에서 본편이 "미확인 가설"로 남겼던 항목들이 실측으로 확정되어 기록한다.

### A. "서버 켜짐+포트 열림인데 못 찾음" = IPv4/IPv6 루프백 바인딩 불일치 (본편 §2 'node-wedge 미규명'의 정답)
- **실측**: `netstat`에서 Unity(Mono HttpListener)가 `http://localhost:{port}/` 프리픽스를 **`[::1]:3000` 단독**으로 바인딩(127.0.0.1 없음). Node 브릿지는 `localhost`를 127.0.0.1로 골라 접속 → **서버 생존 상태에서 ECONNREFUSED**. curl은 자체적으로 양쪽 패밀리를 시도해서 즉답 — 과거 "node만 stuck, curl은 됨" 미스터리의 실체.
- **수정**: 서버는 `localhost`+`127.0.0.1`+`[::1]` 3중 프리픽스 명시 바인딩(H1, IPv6 불가 환경 폴백 포함). 브릿지는 `['127.0.0.1','::1']` 후보를 ECONNREFUSED 시 순환하는 sticky 호스트 선택(H2).
- **부수 발견**: Mono HttpListenerRequest는 IPv6 리터럴 Host 헤더(`[::1]:3000`)를 파싱 못 해 **400 "Invalid url: http://[:3000/..."** 를 반환. 브릿지는 ::1로 접속할 때 Host 헤더를 `localhost:{port}`로 위장해 우회(H2).

### B. CC 시작 시 MCP 연결 실패 = initialize의 Unity 왕복 의존
- **구조**: 기존 브릿지는 `initialize`도 Unity까지 전달 — CC 기동 순간 Unity가 컴파일/리로드 중이면 핸드셰이크가 실패하고 **서버 전체가 '연결 실패'로 마킹**(도구 61개 소실, `/mcp` 수동 재연결 전까지 지속).
- **수정(H3)**: `initialize`/`ping`은 브릿지 로컬 즉답, notification 전부 로컬 소화, `tools/list`는 성공 시 `~/.unity-mcp/tools-cache.json`에 캐시하고 Unity 불통이면 캐시로 응답. **검증**: 가짜 포트로 Unity 불통을 시뮬레이션해도 핸드셰이크+62개 도구 목록 성립.

### C. `EditorApplication.delayCall`은 미포커스 에디터에서 무기한 기아 (Batch B 설계 결함)
- **실측**: 에디터 미포커스 상태에서 요청 펌프(`EditorApplication.update`)는 정상 틱하는데 **delayCall은 수 분째 미발화** → B1의 지연 부작용(Refresh+재컴파일, isPlaying 전환)이 영영 실행되지 않음. 또한 재컴파일 "대기 중" 표시(`MarkRecompileDispatched`)가 그 delayCall 안에 있어, 폴링이 "컴파일 없음+에러 0"을 **조기 done으로 오판**(6초 만에 done, 실제 컴파일 0회).
- **수정(F1/F2)**: update 틱 기반 1회성 디스패처 `McpEditorDispatch.RunOnNextEditorUpdate`(멀티캐스트 스냅샷 특성상 최소 다음 틱 실행 보장 = 응답 flush 후 실행 유지)로 recompile/enter/exit 3개 사이트 교체 + 대기 표시를 잡 생성 시점으로 이동.
- **일반화 교훈**: MCP처럼 **에디터 미포커스가 기본 상태**인 자동화에서는 delayCall 기반 지연 실행을 쓰면 안 된다. update 콜백만 신뢰할 것.

> 규칙 하나 추가: **`unity_recompile_scripts`가 즉답한 뒤 실제 컴파일이 도는지는 콘솔/잡 폴링으로 확인** — "accepted 응답 수신"과 "부작용 실행"은 별개 단계다.
