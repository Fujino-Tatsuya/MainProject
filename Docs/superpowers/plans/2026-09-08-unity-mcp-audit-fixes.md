# Unity MCP 실측 결함 수정 계획

상태: 사용자 ‘작업 이어서’ 지시로 구현 진행 승인. 2026-09-08.
추가 승인(2026-09-09): 실제 서버 검증 후 커밋·현재 프로젝트 적용. 설치 방식은 사용자가 ‘검증 브랜치 push 후 GitHub 커밋 핀 적용’을 선택했다.
게임의 PLAN.md는 중간보스 작업이 진행 중이므로 덮어쓰지 않는다. 기존 PLAN-mcp.md처럼 별도 도구 트랙이다.

## 목표와 현재 이해

포크 `C:/Users/user/Projects/unity-mcp-fork`의 `85f6c17`과 프로젝트 UPM 핀은 같다.
포크에는 사용자의 package.json 변경이 있으므로 보존한다. 게임 코드·씬·프리팹·패키지 핀 변경, push/merge는 범위 밖이다.
최종 변경은 포크에서 리뷰할 수 있는 patch와 재현 테스트로 남기고, 프로젝트 적용은 별도 단계다.

실측 원본은 `output/unity-mcp-audit-2026-09-08`에 있다.

| 우선순위 | 확인된 문제 | 수용 기준 |
|---|---|---|
| P0 | HTTP 200 잘못된 ID, 문자열로 변한 숫자 ID, ID 없는 성공 응답, 깨진 JSON을 그대로 stdout으로 보냄 | 모든 요청은 자기 ID의 유효한 성공/오류로 정확히 한 번 종결. 잘못된 성공을 정상 성공으로 바꾸지 말고 프로토콜 오류로 표시 |
| P0 | 45초는 소켓 idle 제한. 1초 간격 공백 스트림이 약 60초 동안 응답 없이 생존 | 재시도 포함 요청 전체 deadline. 만료 시 원래 ID의 오류 1개, HTTP 소켓·재시도 타이머 정리. 비멱등 재시도 금지 유지 |
| P1 | stdin EOF 뒤 SSE 소켓/재연결 때문에 프로세스 생존 | 종료 시 신규 요청/재시도 금지, 소켓과 타이머 정리, 유한 시간 내 종료 |
| P0 | 지문에 파일 경로가 없음. 파일+meta rename에 hash 불변 | 같은 크기·mtime의 rename도 탐지. 입력 순서가 바뀌어도 지문은 동일 |
| P0 | 디스크 캐시에 빌드 시 DLL/PDB 서명을 저장/검증하지 않음 | 프로세스 재시작 후 어셈블리만 바뀌어도 캐시 거부. 변경 없음은 캐시 재사용 |
| P1 | 프로브의 옛 절대경로·가변 프로젝트 개수 상수·실제 Assets에 임시 파일 작성 | root 해석 통일, 임시 fixture 격리, 독립 오라클/불변식으로 검사. 단순 기대 숫자 갱신 금지 |

## 접근과 선택

1. **추천: 현재 구조의 결함을 먼저 고친다.** 도구 이름과 스키마를 유지하고, 올바른 기존 응답은 동일하게 유지한다. 잘못된 프로토콜 응답만 명확한 오류로 바뀐다.
2. SDK 전송 계층으로 전면 교체: 표준 지원은 좋아지지만 Unity C# 서버까지 호환성 검증 범위가 크게 늘어난다. 이번에는 보류.
3. 공유 인덱스 daemon·Roslyn 전체 전환: 다음 단계 성능/정확도 목표에 맞춰 별도 설계. 기존 결함부터 닫은 뒤 효과를 측정한다.

## 구현 순서

### 1. 응답 종결과 종료 처리

- 수정: `Bridge/mcp-bridge.js`.
- 추가: 패키지 `Tools/verify`의 자동 전송 회귀 테스트. 실제 Node child와 loopback HTTP fault server를 사용한다.
- 원본 브릿지에서 실패하는 재현은 이미 `scripts/audit_unity_mcp_transport.cjs`에 확보했다.
- HTTP 응답 envelope의 JSON 파싱, jsonrpc, ID 타입/값, result/error 배타성을 검사한다.
- 기존 id-less error 복구는 유지한다. 잘못된 success는 원래 요청 ID의 오류로 종결한다.
- 요청 전체 timeout은 소켓 idle timer와 별도로 둔다. retry마다 초기화하지 않는다.
- EOF·SIGTERM·SIGINT 시 HTTP/SSE 소켓, 재연결/재시도 타이머, pending 요청을 정리한다.
- 검증: valid, id-less error, wrong ID, string ID, malformed, id-less success, empty, reset mutation, EOF, slow trickle, retry deadline, duplicate completion.

### 2. 디스크 캐시의 근거 보존

- 수정: `Bridge/index/scan.js`, `Bridge/index/tools.js`.
- 파일 경로를 지문에 포함한다. normalize한 상대 경로와 mtime/size를 사용해 열거 순서에 무관하게 계산한다.
- 캐시에 빌드 전에 수집한 어셈블리 서명을 저장한다. loadCache에서 현 서명과 비교한다.
- 캐시 schema version을 올린다. 이전 캐시는 1회 재빌드한다.
- 검증: file+meta rename, 입력 순서 불변, 프로세스 사이 DLL 변경, 변경 없는 재시작의 캐시 적중, 빌드 도중 어셈블리 변경 감지.
- 유효한 실제 DLL을 이용하는 독립 반례로 재확인한다. 현재 합성 invalid DLL fixture는 캐시 수락 경로만 증명한다.

### 3. 재현 가능한 검증과 문서 정정

- 수정: `Tools/probe-ecid-promotion.js`, `Tools/probe-overloads.js`, `Tools/probe-impact-analysis.js`, `Tools/verify/README.md`, `HANDOFF.md` 관련 부분.
- 프로젝트 root는 명시 인자 > UNITY_MCP_PROJECT > resolve된 index root 순서로 통일한다.
- 실제 Assets에 임시 fixture를 쓰지 않는다. 감사 output 또는 OS 임시 폴더에서 독립 index로 실행한다.
- 가변 개수는 독립 파일/메타 검사와 비교하고, 고정 합성 fixture는 명확한 기대값을 사용한다.
- 프로브 통과율을 기능 정확도로 부르지 않는다. 메서드/필드 정확도는 별도 Roslyn 오라클에서 산출한다.
- 토큰은 bytes/3.7 추정과 실제 tokenizer count를 분리한다. 공급자 과금 토큰 측정이라고 쓰지 않는다.

## 검증과 리스크

- 먼저 원본에서 RED, 수정본에서 GREEN, 기존 9개 suite 및 라이브 조회 회귀를 확인한다.
- 게임에 적용하지 않은 브릿지 후보도 현재 Unity의 읽기 도구와 핸드셰이크를 통해 검증할 수 있다.
- Play 2회와 recompile job 응답은 원본에서 실측 완료. 새 C# 코드를 변경하지 않으면 C# 재빌드 성공을 주장하지 않는다.
- timeout은 이미 Unity에 도달한 부작용의 롤백을 의미하지 않는다. 늦은 실행/취소는 별도 서버 계약으로 명시한다.
- 파일 콘텐츠가 같은 크기·mtime로 덮이는 경우까지 hash하는 것은 성능 절충이 필요하다. 이번에는 rename 누락 및 어셈블리 세대 누락을 확실히 해결한다.
- 다른 작업이 진행 중인 게임 파일 및 포크 package.json 변경을 유지한다.

## 후속 발전 방향 — 이번 수정 범위 밖

- 질문 단위 평가셋: 입력→스킬→애니메이션→데미지→네트워크 권한 경로의 근거를 사람이 확인한 정답지와 비교.
- 프로젝트 전체 지도 + 대상별 상세 조회를 조합하고, 전체 85개 도구 노출의 비용/선택 오류를 별도 평가.
- 응답마다 source file/GUID/fileID, source generation, verified/inferred/unknown, omitted count 제공.
- prefab variant/override, UnityEvent, Animator/Behavior/InputAction, RPC·NetworkVariable의 연결을 우선순위별 확장.
- 여러 bridge의 중복 인덱싱과 메모리를 실측한 뒤 공유 daemon/worker 및 점진 갱신 여부 결정.

## 실행 기록

### 실제 적용 단계 (9월 9일 추가 승인)

후속 교차검증(사용자 ‘이어서 작업’): `hadErrors=false`가 컴파일 세대/소스 일치를 증명하지 않는데 `current`로 표시하는 기존 오류를 추가 수정한다. 현재 계약으로 증명 가능한 것은 보고된 오류 여부뿐이다. 명시적 오류는 last-good, 나머지는 unknown으로 보수적으로 표시하고 보고된 오류 여부를 별도 보존한다. 새 도구나 게임 기능은 추가하지 않는다. 무관측·0세대·양수 세대·명시 오류·미보고 입력 회귀시험 및 기존 진단 suite로 확인한다.

1. 후보 브릿지로 실제 서버 조회, Play 진입/이탈 2회, 임시 Editor 전용 어셈블리의 실제 생성 및 제거를 확인한다. 임시 파일은 이 실행이 소유한 폴더에만 쓰고 삭제 전 경로·파일 목록을 검증한다.
2. 검증 결과를 포크 문서에 반영하고 검증 브랜치만 원격에 push한다. optimized/main 병합은 하지 않는다.
3. 기존 manifest의 MCP 의존성만 원격 커밋 SHA로 변경하고 Unity가 lock/cache를 갱신하게 한다. 고정 경로의 설치 런처도 검증본으로 갱신한다.
4. 실제 설치 런처가 새 PackageCache 브릿지를 실행하는지, 서버와 로컬 해석기 도구가 동작하는지 확인한다. 실패하면 MCP pin/launcher만 이전 버전으로 되돌린다.
5. MainProject에는 패키지 pin/lock, 감사 스크립트·보고서만 커밋한다. 작업 전부터 변경된 Dev_Boot 씬, PLAN.md, 다른 작업 파일은 포함하지 않는다.

- 승인 후 worktree 브랜치 `codex/mcp-audit-reliability`에서 작업한다. 원본 `optimized`와 사용자의 package.json 변경은 보존한다.
- 전송 계층과 캐시 계층은 서로 수정 파일이 달라 독립 구현 후 교차 리뷰한다(적용한 subagent-driven-development 스킬의 분업 지침).
- 추가 발견: 여러 줄의 const 문자열 결합을 첫 문자열로 잘라 잘못된 missing 경로를 답한다. 동일 `scan.js`의 근거 정확도 결함이므로 합성 반례 확보 후 보수적으로 수리한다. 해석할 수 없는 식은 unknown/dynamic으로 유지한다.
- 추가 발견: 설치 런처가 PackageCache의 최신 mtime을 선택해 프로젝트 핀과 다른 브릿지를 실행한다. manifest/lock 해시 선택 및 모호한 상태 거절로 수정했고 독립 fixture 9개로 확인했다.
- 통합 중 추가 발견: Assets의 SVN 정션 안 `.svn/pristine`이 실제 참조로 집계된다. `.svn/.git/.hg` 보관 경로를 제외하고 schema를 20으로 올렸다. 원본 실패→수정 성공의 합성 반례로 확인했다.
- 게임이 작업 중 변경되어 삭제된 Behavior 소스와 이전 호출자 수에 의존하던 검사도 실패했다. 숫자를 새 값으로 교체하지 않고 디스크에서 고른 단순 선언/고정 그래프 fixture/현재 응답 계약 검사로 역할을 구분했다.
- 배포·게임 저장소의 UPM 핀 변경·설치 런처 교체·push/merge는 이번 감사 산출물에 포함하지 않는다. 결과와 검증 한계는 `Docs/04-report/unity-mcp-audit-2026-09-09.md`에 기록한다.
