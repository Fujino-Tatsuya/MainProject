# Vault Q&A Discord 봇 설계 (Phase C — AI 질의응답)

> 목적: 팀원(특히 기획자/아트)이 개발자를 거치지 않고, 팀 지식베이스(Vault)를 근거로 AI에게
> 프로젝트 질문을 하고 출처와 함께 답을 받게 하여 소통 비용을 줄인다.
> 기반 인프라: [knowledge-base-vault-design.md](knowledge-base-vault-design.md) (Phase A) —
> 서버 PC에 Vault 전체 + `MainProject-Docs/`가 Syncthing으로 실시간 동기화되어 있다.

## 결정 사항 (2026-07-07 합의)

- **인터페이스**: 기존 팀 Discord 서버의 `#ask-vault` 채널. 채널에 올라오는 **모든 메시지에 응답**
  (별도 명령어 없음 — 기획자 진입장벽 최소화).
- **LLM**: Gemini Flash, **무료 티어** — 운영비 ~0원. 모델명은 환경변수로 교체 가능하게 둔다.
- **실행 위치**: 서버 PC(상시 켜짐)에서 봇 프로세스 상주. Discord는 아웃바운드 연결만 사용하므로
  포트 개방 불필요 (`0.0.0.0` 바인딩 금지 원칙과 충돌 없음).
- **런타임**: Node.js + discord.js.

## 아키텍처

```
[팀원] Discord #ask-vault 채널에 질문
   ↓
[서버 PC] 봇 프로세스 (Node.js, discord.js)
   ├─ 1차 선별: 파일 인덱스(경로+제목+요약) + 질문 → Gemini가 관련 파일 선별
   ├─ 2차 답변: 선별된 파일 본문(최대 N개) + 질문 → Gemini가 근거 기반 답변 생성
   └─ 답변 + 출처(파일명/섹션) Discord에 회신
   ↑
지식 소스: 서버 PC의 Syncthing 동기화본 Vault (읽기 전용)
```

### 2단계 검색 (임베딩/DB 없음)

Vault+Docs 전체는 약 370KB(≈20만 토큰)라 매 질문마다 통째로 넣으면 무료 티어의
분당 토큰 한도에 걸린다. 대신:

1. **인덱스**: 봇 시작 시 + 주기적(5분)으로 md 파일의 경로·첫 헤딩·요약 몇 줄을 스캔해
   메모리에 유지. 파일 수십 개 규모에선 이것으로 충분 — 임베딩·벡터DB는 도입하지 않는다.
2. **1차 호출**: 인덱스 + 질문을 Gemini에 주고 관련 파일 목록만 고르게 한다.
3. **2차 호출**: 선별된 파일의 본문 전체 + 질문으로 최종 답변 생성.

### 지식 소스 범위

- **포함**: `MainProject-Docs/`(git 정식 문서), `Core/`, `GameDesign/`, `Programming/`,
  `Art-Planning/`, `Meetings/회의록/`
- **제외**: `.obsidian/`, `.stversions/`, `.stfolder`, `.claude/`, `_processed/`, `_inbox`,
  그리고 개인 폴더 `DailyTodo/`·`Worklog/` (질의응답 근거로는 노이즈)

### 답변 원칙 (evidence-only)

- 근거 파일에 없는 내용은 지어내지 않는다. 근거가 없으면
  "Vault에 근거 없음 — 담당자에게 확인 필요"를 명시한다.
- 모든 답변에 **출처 파일명**(가능하면 섹션)을 표기한다.
- 답변은 한국어.

## 운영·보안

- **API 키**: 서버 PC의 사용자 환경변수 `GEMINI_API_KEY`에만 저장.
  Vault·리포 안에 절대 두지 않는다 (Syncthing으로 전파됨 — Deepgram 키와 동일 원칙).
  Discord 봇 토큰도 동일하게 환경변수 `DISCORD_BOT_TOKEN`.
- **상시 구동**: Windows 작업 스케줄러 등록 (로그온 시 시작 + 실패 시 재시작).
- **읽기 전용**: 봇은 Vault를 읽기만 한다. 쓰기·수정 없음.
- **무료 티어 한도 대응**: 분당 요청 한도 초과 시 "잠시 후 다시 물어봐 주세요"로 응답
  (큐잉 등 복잡한 처리는 하지 않는다 — YAGNI).

## 수동 단계 (사용자 직접, AI 원격 접근 불가 구간)

1. Discord Developer Portal에서 봇 생성 · 토큰 발급 · **Message Content Intent 활성화**
2. 팀 서버에 봇 초대 + `#ask-vault` 채널 생성
3. 서버 PC에서 설치 스크립트 실행 (Node 설치 + 봇 파일 복사 + 환경변수 + 스케줄러 등록)

## 개발·검증 순서

1. 본인 PC에서 봇 개발, 본인 PC Vault(`C:\Users\user\MainProjectVault`)로 E2E 테스트
   — 실제 질문 몇 개로 답변 품질·출처 정확성 검증
2. 서버 배포 패키지(설치 스크립트 + 가이드) 작성 → 사용자가 서버 PC에서 실행
3. 팀 Discord에서 실사용 검증

## 범위 밖

- 회의 실시간 참여 AI(브레인스토밍 단계), Phase D(AI diff 체크리스트)
- 임베딩 기반 검색 (파일 수가 수백 개 이상으로 늘면 재검토)
- Slack 등 다른 메신저 지원
