# PLAN-mcp — Unity MCP 브릿지 신뢰성 수정 (2026-07-16)

> 승인: 팀장 단독 결정(2026-07-16 채팅). 근거 분석: `Docs/04-report/unity-mcp-reliability-analysis.md`
> PLAN.md(몬스터 FSM)와 별개 트랙. 대상 = `Packages/com.community.unity-mcp` (git 제외 폴더 → **패키지 내부 로컬 git**으로 이력 관리, 추후 Seoki2000/unity-mcp 포크 push는 사용자).

## 목표
증상 3개(컴파일 시 미복구, Play모드 오동작, 완료 후 무한대기)를 코드로 제거하고, 수용 테스트로 검증한다.

## 스코프
- **In**: 0단계(설정 2건) + 퀵윈 7건 + 2단계 구조 4건 + 보조 안전세트(ReadToEnd 타임아웃·스크린샷 상한·id 타입 보존) + InputSystem 입력 교체 + JSON 실파서 교체.
- **Out**: Reload Domain off(2단계 적용 후 재평가), 포트 디스커버리(해시 포트), 게임플레이 코드 일체.
- **불변식**: 도구 → `argsJson` 문자열 전달 계약 유지. 기존 도구 이름/스키마 하위호환. 패키지 외부 수정은 `.mcp.json`과 홈 사본 rename 두 건뿐.

## 배치 구성 (실행 순서)
### Batch A — 퀵윈 (sonnet)
| 항목 | 내용 | 위치 |
|---|---|---|
| 0-1+1-6 | `.mcp.json`: args를 패키지 `Bridge/mcp-bridge.js` 절대경로로 + `"timeout": 120000` 추가. 홈 `~/.unity-mcp/mcp-bridge.js` → `.bak-20260716` rename, `template.mcp.json` 안내 갱신 | `.mcp.json`, `~/.unity-mcp/` |
| 0-2 | 고아 상태파일 `unity-mcp-status-3cc3188b.json` → `.bak` rename | `~/.unity-mcp/` |
| 1-1 | 요청 컨텍스트에 `volatile bool Abandoned` — 워커가 30s 타임아웃/셧다운으로 포기 시 true 설정, 큐 액션은 실행 직전 체크 후 스킵(+스킵 로그) | `McpServer.cs:294-322` |
| 1-2 | Node 재시도 게이팅: ECONNREFUSED=항상 재시도(미도달 확정) / ECONNRESET·EPIPE=`tools/call`이면 읽기전용 프리픽스(`unity_get_`, `unity_list`, `unity_search`, `unity_raycast`, `unity_overlap`, `unity_take_screenshot`)만 재시도, 비멱등은 재시도 없이 명확한 에러("요청이 도달했을 수 있어 재시도 안 함") | 패키지 `Bridge/mcp-bridge.js:152-166` |
| 1-3 | `Start()` 바인딩 실패 시 지수 백오프 재시도(0.25→0.5→1→2→4→5s…, 최대 10회, delayCall+update 경과체크 이중, `_isRunning`/quitting 시 중단, 시도 로그) | `McpServer.cs:95-99` |
| 1-4 | `AutoStart` 가드: `Application.isBatchMode` 스킵 + MPPM 가상플레이어 스킵(`dataPath` 정규화 경로에 `/Library/VP/` 포함 시) + 스킵 로그 | `McpServer.cs` AutoStart |
| 1-5 | `enter/exit_play_mode`: 거짓 `isPlaying:true` 제거 → `{accepted:true, transition:"starting", isPlaying:<현재실제값>, note:"리로드로 잠시 끊김, unity_get_editor_state 폴링"}` (2-1에서 잡 연결) | `EditorTools.cs:103-111` 및 exit |
| 1-7 | `new { error = ... }` 익명타입 전수 치환 → `[Serializable] McpToolError { string error; string detail; }` (JsonUtility `{}` 유실 제거) | Tools 전반 grep |
| 보조b | POST 본문 읽기 타임아웃(5s) — `ReadToEnd` 무한블록 제거 | `McpServer.cs:281` |
| 보조c | 스크린샷 해상도 상한(기본 최대 1280) + base64 2MB 초과 시 자동 축소 재시도 | `ScreenshotTools.cs` |

### Batch C — 입력 교체 (sonnet, A와 병렬 — 파일 비겹침)
| 2-5 | `unity_simulate_key/mouse`를 Input System 경로로: `Keyboard.current`/`Mouse.current`에 `InputSystem.QueueStateEvent` 주입(Play모드 게임입력 도달). `#if ENABLE_INPUT_SYSTEM` 조건부 + 기존 `SendEvent` 폴백 유지. 패키지 asmdef에 `Unity.InputSystem` 참조 추가(프로젝트 manifest에 InputSystem 존재 확인 후) | `InputTools.cs`, `*.asmdef` |

### Batch B — 구조 개선 (opus, A 컴파일 확인 후)
| 항목 | 내용 |
|---|---|
| 2-1 | **pending+poll**: 신규 `Editor/Core/McpJobStore.cs` — JobRecord{id,tool,status,resultJson,error,ticks}를 `Library/McpJobs.json`에 저널(리로드 생존, 최근 50개/24h 보존). 리로드 유발 도구(recompile, enter/exit_play_mode)는 잡 생성→즉시 `{accepted, jobId}` 응답→`delayCall`로 부작용 실행(응답 flush 후 리로드). 신규 도구 `unity_get_job_status{jobId?}`: 저널+라이브 상태(isCompiling/컴파일에러/isPlaying) 결합 판정, 인자 없으면 최근 잡 목록 |
| 2-2 | `AssemblyReloadEvents.afterAssemblyReload` 훅: 재기동(idempotent) + 미완(accepted/running) 잡을 실제 상태로 확정 |
| 2-3 | 하트비트: bridge가 10s 주기 내부 전용 HTTP ping(**stdout 출력 금지** — 프로토콜 오염 방지, postToUnity 재사용 말고 별도 함수), `lastAliveAt` 기록, 에러 메시지에 "Unity last responded Xs ago" 첨부. 서버는 `beforeAssemblyReload`에서 SSE `unity/reloading` 노티(死코드 `SendNotification` 부활), bridge는 수신 시 내부 상태만 마킹(로그) |
| 2-4 | `open_scene/new_scene`에 `saveMode: save(기본)/discard/prompt` 인자 — 기본값이 모달 없이 자동저장(`SaveOpenScenes`) 후 진행 |
| 파서 | `JsonRpcHandler` 정규식 Extract 전면 교체: 프로젝트에 `com.unity.nuget.newtonsoft-json` 있으면 asmdef 참조로 사용, 없으면 MiniJSON 단일파일 추가. **id는 원문 타입 보존**(숫자↔문자열 변형 제거 = 보조a 포함). `params.arguments`는 원문 substring으로 추출해 argsJson 계약 유지 |

## 리스크 / 완화
- C# 저장마다 리로드로 MCP 단절 반복 = 정상(작업 부산물). 배치 완료 시점에만 컴파일 트리거.
- bridge.js/.mcp.json 변경은 **CC 재시작 후 적용** — 재시작 전엔 구버전 bridge가 신버전 서버와 통신(하위호환 유지 필수, Batch B에서 프로토콜 파괴 금지).
- 타이밍 상수(재시도 딜레이 등) 근거 없이 축소 금지(CoplayDev #1173 회귀 사례).
- MPPM 감지는 경로 휴리스틱 — MPPM 업데이트 시 재확인.
- 각 배치 후 패키지 로컬 git 커밋(롤백 지점).

## 검증 (수용 기준)
1. 배치별 Unity 컴파일 0 에러(`unity_get_compilation_status`/콘솔).
2. `unity_recompile_scripts`가 즉시 accepted+jobId 반환 → 리로드 후 `unity_get_job_status`로 완료 확인(왕복 자동 테스트).
3. Play 진입→이탈 반복 중 매 호출이 명확한 응답(무응답 0회).
4. 타임아웃으로 포기된 요청의 큐 액션이 실행되지 않음(로그 확인).
5. 신규 bridge를 수동 기동해 stdio 스모크(initialize/ping/읽기도구 1개) — CC 재시작 전 검증.
6. 미저장 씬 상태에서 `open_scene` 모달 없이 완료.
7. (CC 재시작 후, 사용자) 도구 목록에 `unity_get_job_status` 노출 + 일상 사용 체감 확인.

## 완료 조건
전 배치 컴파일 통과 + 수용 1~6 통과 + opus 코드리뷰 지적 반영 + 패키지 git 이력 3커밋(A/C/B) + 구현노트(이탈 기록) 정리 + 최종 보고.
