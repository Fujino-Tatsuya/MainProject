# Unity MCP 포크 — 실측 감사와 개선 기록

검사 기간: 2026-09-08~09. 기준 패키지: `85f6c175c08233cc462c60557c6c2556dcde805e`.
**후속 상태:** 9월 9일 22:51 KST부터 실제 서버 연결에 성공해 후보 Play·실제 어셈블리 컴파일까지 추가 검증했다. 아래 초기 미접속 기록은 당시의 관측이며, 최신 결과는 §7을 참조한다.
실측 원본(레포 밖 · 팀 볼트): `C:/Users/user/Projects/TeamVault/MainProejectVault/04-report/mcp-audit-output/unity-mcp-audit-2026-09-08`.
수정 계획: [승인된 1차 범위](../superpowers/plans/2026-09-08-unity-mcp-audit-fixes.md).

## 판단

현재 구조는 이미 코드·DLL/PDB·직렬화 에셋을 결합하는 프로젝트 해석기다. 새 해석기를 처음부터 만들 필요는 없다.
가장 먼저 개선할 것은 응답의 종결 보장과 인덱스가 설명하는 시점의 정확성이다.
통신이 빨라도 다른 요청 ID로 응답하거나, 예전 DLL의 그래프를 현재 근거로 제시하면 AI의 후속 수정이 잘못된다.

이 보고서에서 ‘토큰’은 명시한 tokenizer로 실제 응답 문자열을 센 값이다. 공급자의 청구 토큰이나 이 대화의 사용량이 아니다.
‘정확도’는 특정 모집단에 대한 정밀도·재현율이다. 시스템 전체나 AI 작업 성공률로 확대 해석하지 않는다.

## 1. 이번에 실제로 실행한 검사

### 라이브 에디터 / 브릿지

Unity `6000.3.16f1`, 프로젝트 `C:/Users/user/Projects/MainProject`를 인증 파일의 프로젝트 경로로 확인했다. 인증 값은 결과에 저장하지 않았다.

| 검사 | 9월 8일 관측 |
|---|---|
| 실제 설치 런처 → initialize | 46ms, 버전 `2.3.0-dev.0.0.7` |
| 실제 설치 런처 → tools/list | 61ms, 85개, JSON 44,009B |
| 실제 설치 런처 → editor state | 102ms |
| Unity HTTP tools/list | Unity 도구 73개. 브릿지가 로컬 도구 12개를 합쳐 85개 |
| Play 진입 1 / 2 | 접수 3 / 1ms, job done + 실제 Play 확인 6,208 / 6,204ms |
| Play 이탈 1 / 2 | 접수 12 / 4ms, job done + 실제 Edit 확인 1,524 / 1,692ms |
| 재컴파일 요청 | 접수 100ms, job done 6,702ms |

Play 검증 시작과 종료는 모두 `MonsterScene`, 편집 모드였다. 앞선 읽기 전용 확인 시에는 `Dev_Boot`였지만 Play 검사 시작 전에 다시 현재 상태를 읽었다.
반복 Play와 컴파일 요청에서 무응답은 관측되지 않았다. 이 횟수로 모든 리로드 상황의 안전성을 증명할 수는 없다.

재컴파일 결과의 `observedAssemblyCount=0`이므로 **새 코드 컴파일 성공의 증거로 계산하지 않는다.** 이미 최신인 어셈블리가 생략된 주기다.
9월 9일 검사 초반에는 다른 작업의 머지 충돌이 있었고, 이후 소스/어셈블리도 변경됐다. 마지막 후보 조회 시에는 IPv4/IPv6 모두 MCP 서버 연결이 거절됐다. 어제의 정상 상태와 오늘의 상태를 혼동하지 않는다.
MPPM 2~3인, 강제 Unity 종료, 실제 씬 저장 모달, 실제 진행 중 게임 부작용의 취소는 이번 실측 범위 밖이다.

### 인덱스 / 응답 비용

9월 8일 스냅샷: 기본 GUID 3,177개, YAML 1,180개, 참조 엣지 8,441개, 사용자 어셈블리 14개·타입 1,201개.
프로젝트는 다른 작업과 함께 변하므로 이 개수를 미래 회귀 테스트의 정답 상수로 사용하면 안 된다.

| 검사 | 관측 | 조건 |
|---|---:|---|
| 강제 인덱스 재빌드 | 2,366ms | OS 파일 캐시는 따뜻할 수 있음. 디스크 냉시작이라고 주장하지 않음 |
| 지도 기본 예산 6,000 | 38~40ms, 10회 | 같은 프로세스 웜 질의 |
| Hurtbox 영향 분석 | 5ms | 1회 |
| Missing Script | 1,285ms | PackageCache GUID를 처음 병합하는 비용 포함 |

‘2,366ms’와 ‘40ms’는 서로 다른 경로다. 전자를 한 번 낸 뒤 모든 호출이 후자라는 보장도 없다. 캐시 무효화·전체 지문 검사 주기에는 비용이 다시 든다.

### 토큰 실측

`tiktoken 0.14.0`으로 JSON을 압축 직렬화해 계산했다. 데이터는 외부 API에 전송하지 않았고 공개 tokenizer 사전만 내려받았다.

| 응답 | bytes | o200k_base | cl100k_base |
|---|---:|---:|---:|
| 같은 85개 도구, annotation 압축 전 | 47,021 | 10,743 | 10,435 |
| 현재 도구 목록 | 44,009 | 9,914 | 9,652 |
| 지도 budget=2,000 | 7,127 | 1,873 | 1,883 |
| 지도 budget=4,000 | 14,557 | 3,950 | 4,003 |
| 지도 budget=6,000 | 21,891 | 5,955 | 6,019 |
| 지도 budget=8,000 | 29,234 | 7,933 | 8,026 |
| 지도 budget=10,000 | 36,610 | 9,917 | 10,023 |

압축 전 비교군은 **현재 서버 도구 + 현재 로컬 도구를 합친 뒤 annotation 생략을 적용하지 않은 동일 모집단**이다. 포크 이전 모든 최적화의 합산 효과가 아니다.
이번 비교에서 현재 압축은 bytes 6.4%, o200k 토큰 7.7%를 줄였다.
지도 `budgetTokens`는 bytes/3.7 기반 근사치다. 다른 tokenizer에서는 소폭 초과하므로 엄격한 토큰 상한이라고 문서화하면 안 된다.
MCP 클라이언트가 wrapper를 벗기는 방식에 따라 모델에 들어가는 문자열은 달라진다. 위 표는 wire 형태 기준이며 원본 JSON에 payload-only 값도 남겼다.

기존 지도 구조 프로브는 4,000 예산에서 5/7, 6,000에서 6/7, 8,000에서 7/7이었다.
기본 예산에서는 BombAction 타입 이름 참조가 빠졌다. **응답을 짧게 하는 것과 필요한 근거를 충분히 주는 것은 함께 평가해야 한다.**
이 7문항은 사람이 정한 구조 존재 검사이며 LLM이 실제 수정 과제를 성공한 비율은 아니다.

## 2. 재현한 결함

| 우선순위 | 원인과 재현 | 영향 |
|---|---|---|
| P0 | HTTP 200의 wrong ID, 숫자→문자열 ID, id-less success, malformed JSON이 그대로 stdout으로 전달됨 | 요청한 클라이언트의 pending 상태가 끝나지 않을 수 있음 |
| P0 | `req.setTimeout`만 사용. 1초마다 공백을 보내는 서버에서 약 59.9초 동안 응답 없음 | idle 제한을 전체 deadline으로 오해하면 장기 대기 가능 |
| P1 | stdin EOF 뒤 SSE/재연결이 프로세스를 유지 | 클라이언트가 종료되어도 브릿지 잔존 가능. 실제 관측된 모든 node 프로세스의 원인이라고 단정하지 않음 |
| P0 | 파일 지문에 경로가 없음. 같은 asset+meta를 rename해도 지문 동일 | 이동/이름 변경 뒤 낡은 경로를 사용 |
| P0 | 저장 캐시에 어셈블리 세대가 없음 | 브릿지 재시작 사이 DLL 변경 후 과거 그래프를 다시 사용 |
| P1 | `const string P = "첫 조각" + ...`에서 첫 조각을 완성된 값으로 채택 | 존재하는 컨트롤러가 없는 경로로 보고됨 |
| P1 | 런처가 cache 폴더 mtime으로 선택 | lock은 A인데 더 최근인 B를 실행. 작은 독립 fixture로 재현 |
| P1 | 검증이 옛 절대경로/프로젝트 개수에 의존하며 실제 Assets에 임시 데이터를 씀 | 검사 자체가 오답·부작용을 만들 수 있음 |

기존 `id:null` **오류** 복구와 비멱등 요청의 connection reset 재시도 금지는 재현에서 정상 동작했다. 이전 수정이 무효였다는 뜻이 아니라 경계가 덜 덮였다는 뜻이다.
느린 스트림 테스트는 가짜 Unity HTTP 서버와 **출하 브릿지 프로세스**를 사용한 fault injection이다. 실제 Unity가 공백을 보내며 멈췄다고 주장하지 않는다.

런처 사본은 ‘낡아도 무해하다’는 기존 설명도 정정해야 한다. 브릿지 사본보다 교체 빈도가 낮을 뿐, 버전 선택 로직이 바뀌면 런처 사본 역시 갱신 대상이다.

## 3. 해석기의 독립 정확도 확인

패키지에 있던 Roslyn 오라클 원본을 Unity가 제공하는 C# 컴파일러·.NET 6 런타임으로 별도 빌드해 사용했다. 게임 어셈블리를 재빌드한 것이 아니다.
Roslyn AST의 필드 선언 줄과 출하 `unity_explain_compile_errors`의 분류를 대조했다.

| 항목 | 9월 8일 값 |
|---|---:|
| 전체 소스 AST | 파일 719개, 타입 936개, 필드 선언 4,293개 |
| 줄 분류 대조 범위 | 매핑된 585개 파일, 88,571줄 |
| 범위 내 정답 필드 줄 | 4,104 |
| 도구가 필드라고 분류 | 4,018 |
| 거짓 양성 | 34 |
| 놓친 필드 줄 | 120 |
| 정밀도 | 99.1538% |
| 재현율 | 97.0760% |

누락 120줄 중 중첩 타입 관련이 96줄이었다. 이후 정확도 개선은 이 부류를 먼저 줄이는 편이 타당하다.
이 결과는 ‘모든 심볼·네트워크 흐름 99% 정확’이라는 증거가 아니다.

- PDB 없는 타입 305개 중 파일명 폴백이 가능했던 109개는 실제 선언과 모두 일치했다. **이 프로젝트에 동명 namespace 충돌이 없었던 결과**이며 일반적 안전성을 증명하지 않는다.
- 활성 로드 호출 78개는 모두 탐지됐지만 비활성 `#if` 블록의 호출 1개도 포함됐다. 소스의 컴파일 조건을 해석하지 않는 한계다.
- 여러 줄 상수 경로의 오류는 탐지 개수만 비교하면 통과한다. 문자열 값까지 Roslyn/원문과 대조해야 잡힌다.
- 필드라고 분류한 후 반환한 member 중 잘못 귀속된 28건은 모두 정답 필드 줄이 아닌 곳이었다. 줄 분류의 거짓 양성과 member 귀속 문제를 별개의 성공률로 부풀려 합산하지 않는다.
- 기존 프로브 9개 중 6개 성공, 3개 실패였다. 실패 파일의 총계 상수를 새 숫자로 바꾸는 것으로 해결하지 않고, 실제 근거/독립 fixture 기반 검사로 수정한다.

## 4. 수정 적용 기록

작업 브랜치: 포크의 `codex/mcp-audit-reliability`. 원본 `optimized`와 사용자의 package.json 변경은 보존했다.
작업 공간은 게임 바깥의 Codex writable 경로에 있으며 프로젝트 UPM 핀은 변경하지 않았다.

후보 커밋: `db29827b86708e7f97c2adc8bc7b7293fe31b19f` (21파일). 작업 트리는 clean이다.
[전체 패치](C:/Users/user/Projects/TeamVault/MainProejectVault/04-report/mcp-audit-output/unity-mcp-audit-2026-09-09-fixed/unity-mcp-audit.patch)와 [전달 메타데이터·SHA256](C:/Users/user/Projects/TeamVault/MainProejectVault/04-report/mcp-audit-output/unity-mcp-audit-2026-09-09-fixed/delivery.json)를 보존했다.
원본 포크에서 `git apply --check`가 성공했고 원본의 사용자 package.json 수정도 그대로 남아 있다. 이 명령은 패치를 실제 적용하지 않는다.

### 완료한 수정

| 위치 | 수정한 동작 | 검증 |
|---|---|---|
| `Bridge/mcp-bridge.js` | HTTP 요청 전체 deadline 45초(재시도/응답 스트림 포함), 응답 ID·envelope 검증, 8MiB 응답 상한, EOF/종료 시 소켓·타이머 정리 | 실제 Node child + HTTP fault server 27/27 |
| `Bridge/index/scan.js`, `tools.js` | 상대 경로 지문, 빌드 전 DLL/PDB 서명 저장·캐시 복구 시 검증, 여러 줄 상수 접기 | 유효한 managed DLL 교체·재시작 등 13/13 |
| `Bridge/mcp-bridge-launcher.js` | manifest/lock이 지정한 버전만 선택. 새 mtime의 다른 캐시·잘못된 sibling으로 넘어가지 않음 | 설치 구조 fixture 9/9 |
| scanner·검증 보조 코드 | `.svn/.git/.hg` 보관본을 에셋에서 제외, 원본 YAML 인용 scalar 처리 | 독립 반례 2/2 |
| 기존 프로브 | 실제 Assets fixture 쓰기 제거, 과거 총계·삭제 파일 의존 감소, 캐시 그래프 전체 집합 대조, 추정 토큰 표기 정정 | 9개 실행 파일 모두 exit 0 |

새 회귀시험 **51/51**. 최종 실행은 2026-09-09 22:45 KST, Node `v24.19.0`이다.
결과: [regression-results.json](C:/Users/user/Projects/TeamVault/MainProejectVault/04-report/mcp-audit-output/unity-mcp-audit-2026-09-09-fixed/regression-results.json), [suite-results.json](C:/Users/user/Projects/TeamVault/MainProejectVault/04-report/mcp-audit-output/unity-mcp-audit-2026-09-09-fixed/suite-results.json).
`node scripts/audit_unity_mcp_fixed.cjs <후보 패키지 루트>`로 같은 묶음을 다시 실행할 수 있다. 실패한 하위 실행이 있으면 감사 드라이버도 비정상 종료한다.

기존 지도 프로브의 성공 기준은 기본 6k 예산에서 6문항 이상이다. 최종 결과는 **6/7**이므로 9개 스위트가 성공했다는 말은 모든 내부 질문이 성공했다는 뜻이 아니다.
현재 게임에서 BombAction 관련 데이터가 바뀌었으므로 과거 지도 질문은 스냅샷 평가로 남겼고, 타입 이름 연결 기능 자체는 독립 Behavior fixture의 양성 사례로 검사했다.
속성+필드 선언 시험은 디스크의 단순 선언 문법으로 표본을 고르며, 구현 답변이 맞는 파일을 찾아 고르는 방식은 사용하지 않는다.

SVN 문제는 실제 `SkillRange.png` 조회에 `Assets/50.Art/.svn/pristine/...svn-base`가 들어온 것으로 확인했다.
수정 후 원본 GUID를 독립 순회해 얻은 실제 참조 3개(그중 shadergraph 텍스트 참조 2개)와 도구 결과가 정확히 일치했다.
전체 엣지 수 감소를 곧바로 누락/성능 개선으로 부르지 않는다. 게임 자체도 변경됐기 때문이다.

### 설치와 검증 경계

- 수정 후보는 별도 worktree 브랜치에 보존했다. **MainProject UPM 핀, 설치 런처, 실행 중 패키지는 교체하지 않았다.** push/merge도 하지 않았다.
- 후보 읽기 연결은 initialize 38ms, tools/list 약 7,545ms·editor 약 7,560ms 후 연결 불가 오류로 종료됐다. 무한 대기는 없었지만 **온라인 Unity 통합 성공은 미확인**이다. [후보 연결 결과](C:/Users/user/Projects/TeamVault/MainProejectVault/04-report/mcp-audit-output/unity-mcp-audit-2026-09-09-fixed/live-results.json)
- Play 2회/재컴파일 job은 9월 8일 기준 설치본의 실측이다. 이번 수정본의 Play/새 C# 컴파일 성공으로 옮겨 적지 않는다.
- 변경 코드의 로컬 diff 검토와 회귀시험은 완료했다. 하위 에이전트의 후속 교차 리뷰는 사용량 제한으로 완료되지 않아 별도 독립 리뷰 통과로 표시하지 않는다.
- 캐시 schema는 18 → 20이다. 최초 적용 시 이전 캐시는 한 번 재빌드한다. 리뷰 후 실제 적용할 때 UPM 핀과 고정 경로에 둔 런처를 함께 갱신하고, 온라인 조회·Play·실제 assembly 생성 재컴파일을 다시 확인해야 한다.

### 해결했다고 주장하지 않는 것

동기 인덱싱의 CPU 정지는 Node timer 자체를 지연시킬 수 있다. 이번 deadline은 HTTP 비동기 경로의 종결 보장이다.
timeout은 이미 Unity에 전달된 변경의 취소나 롤백이 아니다. SSE 무제한 프레임 버퍼, 프로토콜 버전 협상, 서버 측 cancellation은 후속 과제다.
mtime/size 기반 지문은 같은 크기·같은 mtime으로 내용을 덮는 경우까지 보장하지 않는다.
상수 경로 해석은 제한된 문법이며 이스케이프·보간·조건부 컴파일을 모두 해석하는 Roslyn 대체물이 아니다. 알 수 없는 식은 dynamic으로 남긴다.

## 5. 다음 발전 순서

### 먼저: 근거의 시점과 누락을 공통 계약으로 만든다

각 응답에서 다음을 구별할 수 있어야 한다: 디스크 에셋 관측 시각, 마지막 성공 빌드의 DLL/PDB 세대, 현재 컴파일 실패 여부, 확인한 참조 축, 제외/누락한 항목 수.
현재 소스의 의도와 마지막 성공 빌드의 구조가 다르면 모두 현재 사실인 것처럼 조인하면 안 된다.
`verified / inferred / unknown`과 `source file + GUID/fileID + generation`을 활용하고, 잘린 응답에는 구체적인 다음 조회 방법을 준다.

### 그다음: 실제 작업 질문으로 평가한다

‘AI의 이상적인 이해’는 도구 개수로 재기 어렵다. 아래처럼 사람이 정답 근거를 정한 질문을 평가셋으로 만들 것을 권한다.

| 질문 | 필요한 근거 | 실패 판정 |
|---|---|---|
| 공격 입력에서 데미지 적용까지 어디를 통과하나? | InputAction→Player skill→animation/event→Hitbox/Hurtbox→damage authority | 근거 없는 연결을 확정하거나 오너/서버를 뒤집음 |
| BombAction을 이름 변경하면 무엇이 깨지나? | 코드 호출 외 Behavior 타입 이름 참조 | callers=0만 보고 미사용으로 판정 |
| prefab 값이 기본 코드값과 다른 이유는? | base prefab, variant, instance override, serialized field | 코드 초기값을 실제 실행 값으로 오인 |
| 컴파일 실패 직후 새 필드를 찾을 수 있나? | 현재 source AST + last-good assembly 구별 | 이전 DLL에 없다고 실제 소스에도 없다고 단정 |
| 에셋 이동 뒤 어디를 고쳐야 하나? | GUID 참조와 문자열 경로를 각각 추적 | GUID가 유지된다는 이유로 문자열 로드를 누락 |

평가 지표는 성공률·잘못된 확정 답변·누락·도구 호출 수·총 토큰·수정 후 검증 성공·응답 지연으로 나눈다. 고정 평가셋 외에 별도 holdout을 두어 특정 프리팹 이름에 최적화되는 것을 막는다.

### 토큰은 전체 지도보다 대상 조회를 먼저 개선한다

현재 85개 도구의 목록 비용이 약 9.9k 토큰이므로 schema 생략만으로 큰 폭의 개선을 기대하기 어렵다.
다음 실험은 도구 묶음의 필요 시 노출, `concise/detailed`, 선택 필드, pagination, 작은 지도→대상 상세 순서가 적합하다.
다만 6k 지도에서 한 축이 빠진 실측을 고려하면 무조건 더 압축하는 것이 목표가 될 수 없다. 같은 질문의 정답률과 함께 비교한다.

### 기능 확장은 세 가지 연결을 우선한다

1. Prefab variant/instance override와 Animator/AnimationEvent/InputAction의 연결.
2. RPC/NetworkVariable, 오너·서버 권한, 실제 스폰 경로의 연결. 정적 힌트와 런타임 관측을 구분.
3. 다중 브릿지의 중복 인덱싱을 측정한 뒤 worker/공유 daemon·증분 갱신 도입. 현재는 동기 인덱스 빌드가 Node 이벤트 루프를 막을 수 있어 전송 deadline만으로 모든 로컬 CPU 정지를 해결할 수 없다.

## 6. 조사한 1차 출처와 적용 판단

- [Unreal Fest Seoul 2026 공식 일정](https://epiclounge.co.kr/unrealfest2026/schedule.php): 강현우·넥스트스테이지의 NarshaADK 세션과 코드/에셋 이해 주제를 확인했다. 발표 영상 전체를 시청한 것으로 주장하지 않는다.
- [NarshaADK 개발자 공식 소개·9월 5일 업데이트](https://forums.unrealengine.com/t/next-stage-inc-narshaadk-analyze-fix-generate-ue-c-and-blueprints-with-ai/2718411): 소스·컴파일러 심볼·에셋의 결합, 저장 에셋의 오프라인 분석, 캐시 메모리 예산과 stale-read 개선을 설명한다. 이는 제품 개발자의 설명이며 여기서 해당 제품 성능을 독립 측정한 것은 아니다.
- [MCP 2025-11-25 lifecycle](https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle): 진행 알림과 별개로 최대 timeout을 유지하고 stdio 종료를 처리하라는 규약이 이번 전송 수정의 근거다. 현재 브릿지가 지원한다고 말할 수 있는 프로토콜 버전과 실제 구현 능력은 별도 검증 대상이다.
- [Node.js HTTP request.setTimeout](https://nodejs.org/api/http.html#requestsettimeouttimeout-callback): socket timeout 의미를 확인했다. 요청 전체 deadline과 구분해야 한다.
- [Anthropic — Writing effective tools for agents](https://www.anthropic.com/engineering/writing-tools-for-agents): 과제 중심 도구 설계, concise/detailed, pagination, holdout 평가를 제안한다. 이 프로젝트에는 ‘추가 도구 수’보다 ‘정답 근거를 적은 호출로 얻는가’로 적용하는 것이 적합하다는 판단이다.

## 재현 파일

게임 저장소의 `scripts/audit_unity_mcp*.cjs`, `scripts/audit_unity_mcp_tokens.py`, `scripts/audit_unity_mcp_oracle.ps1`은 이번 감사 드라이버다.
기준 버전과 출력 폴더가 고정된 실행은 9월 8일 기준선을 보존한다. 후보 패키지 재검증은 지원하는 드라이버에 `AUDIT_PACKAGE`, `AUDIT_OUTPUT`을 별도 지정한다.
새 전송·캐시·런처 회귀 테스트는 포크 `Tools/verify/`에 포함하며 게임 프로젝트 없이 실행한다.
프로브 결과는 산출물 JSON/로그와 함께 읽는다. 오래된 README의 명령만으로 ‘재현 가능’하다고 가정하지 않는다.

## 7. 실제 서버 검증 및 현재 프로젝트 적용 (9월 9일 후속)

사용자는 실제 서버 검증 후 커밋·프로젝트 적용을 승인했고, 설치 방식으로 ‘검증 브랜치 push 후 GitHub 커밋 핀 적용’을 선택했다.
검증 브릿지는 `db29827`이며 후속 커밋 `69d494e`는 검증 기록과 지도 예산 설명을 정정한다. 도구 이름·파라미터 계약은 유지한다.

| 후보 브릿지 실제 검사 | 결과 |
|---|---|
| 최초 initialize / tools/list / editor | 39ms / 56ms(85개) / 95ms |
| Play 진입 1 / 2 | 5,897 / 5,747ms |
| Play 이탈 1 / 2 | 1,711 / 1,641ms |
| 실제 컴파일 | 10,278ms, diagnosticsState=complete, observedAssemblyCount=1, 오류·경고 0 |
| 독립 Editor 전용 DLL | 임시 소스/asmdef에서 4,608B DLL 실제 생성 |
| 임시 파일 제거 후 재컴파일 | 7,783ms, 소스·asmdef·meta 및 DLL 제거 확인 |
| 시작 / 최종 상태 | BootStrapScene 편집 모드 → 타이틀 씬 실행 → BootStrapScene 편집 모드 |
| 콘솔 Error 조회 | 검사 전후 반환 0건; 리로드 중 모든 로그의 보존을 증명한 것은 아님 |

[Play·컴파일 원본](C:/Users/user/Projects/TeamVault/MainProejectVault/04-report/mcp-audit-output/unity-mcp-live-2026-09-09/play-results.json), [최신 조회·해석기 원본](C:/Users/user/Projects/TeamVault/MainProejectVault/04-report/mcp-audit-output/unity-mcp-live-2026-09-09/live-results.json).
후속 조회에서 지도·Hurtbox 영향 분석·인덱스 상태까지 실제 stdio 연결을 통해 모두 정상 반환됐다.
독립 코드 리뷰는 핵심 4파일의 `85f6c17..db29827` 변경을 읽고 도입된 release blocker를 발견하지 않았다. 리뷰어가 실행 시험을 독립 재실행한 것은 아니다.
기존 ‘하위 에이전트 리뷰 미완료’ 상태는 이 후속 리뷰로 해소됐다.

현재 프로젝트의 기존 pin/lock과 설치 런처는 `C:/Users/user/Projects/TeamVault/MainProejectVault/04-report/mcp-audit-output/unity-mcp-install-2026-09-09/before-*`에 바이트 그대로 백업했다.
최종 설치 커밋·원격 확인·설치 후 결과는 적용 검증 후 이 절에 기록한다.

## 8. 추가 조사 결과

[후속 조사·교차검증 보고서](unity-mcp-followup-crosscheck-2026-09-09.md)에 공식 자료 대조, 남은 기능 범위와 평가 기준을 정리했다.
추가로 `hadErrors=false`만으로 `current`라고 표시하는 기존 진단 오류를 재현·수정했다. 새 6개를 포함해 회귀시험 57개, 기존 프로브 9개가 각 통과 기준을 충족했다.
후속 수정은 로컬 커밋 `f16c698`에 보존했다. 원격 push가 자동 승인 검토에서 차단되어 프로젝트 UPM 핀은 아직 기준 버전이다.
[후속 회귀 결과](C:/Users/user/Projects/TeamVault/MainProejectVault/04-report/mcp-audit-output/unity-mcp-followup-2026-09-09/regression-results.json), [실제 연결 결과](C:/Users/user/Projects/TeamVault/MainProejectVault/04-report/mcp-audit-output/unity-mcp-followup-2026-09-09/live-results.json).
이 수정은 새로운 실제 컴파일을 실행하지 않아도 되는 로컬 진단 라벨 계약 변경이다. 기존 Play/DLL 생성 검증과 추가 stdio 진단 검증을 구분한다.
