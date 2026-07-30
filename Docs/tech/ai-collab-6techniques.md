# AI 협업 6기법 도입 (설계+실행 기록, 2026-07-07)

> 출처: Claude Code 개발자의 협업 기법 영상 (https://youtu.be/vZaG9KlT-40) — "개발자 본인이 병목이 되는 문제"를
> 푸는 6기법. 팀장이 실제로 겪는 병목이라 팀 표준 + 개인 설정에 도입하기로 결정.
> 문서 규모가 작아 설계와 실행 계획을 이 한 문서로 합침.

## 6기법 ↔ 기존 워크플로 대응

| 기법 | 기존 상태 | 조치 |
|---|---|---|
| ① 사각지대 점검 (지시 전 AI가 위험요소 브리핑) | 없음 | AIRULE.md에 "Blind-Spot Briefing" 절 신설 |
| ② 프로토타입 3안 (접근법 2~3개+장단점 먼저) | 없음 | AIRULE.md Grill Workflow에 규칙 추가 |
| ③ 역질문 인터뷰 (한 번에 하나씩) | **이미 있음** (Grill Workflow) | 변경 없음 |
| ④ 레퍼런스 활용 (참고 코드 분석→우리 환경 재구현) | 없음 | AIRULE.md "Reference-Based Implementation" 절 신설 |
| ⑤ 구현 노트 (이탈 시 보수적 선택+기록, 멈추지 않음) | 절반 (PLAN.md는 사전 계획만) | AIRULE.md Implementation Rules에 deviation log 규칙 추가 (`IMPLEMENTATION_NOTES.md`) |
| ⑥ 퀴즈 검증 (완료 후 이해도 퀴즈 3문항) | 없음 — `/diff-check` 이해도 체크가 유사 목적 | `/diff-check`에 📝 퀴즈 섹션 통합 + AIRULE.md Verification에 규칙 추가 |

## 적용 위치 (A+B+C 전부 — 사용자 승인)

- **A. 팀 표준**: `AIRULE.md` 확장 — AGENTS.md/CLAUDE.md가 자동 로드하므로 팀원의 AI가 규칙으로 따름
  (사람이 프롬프트를 외울 필요 없음). 기존 문서가 영어라 추가분도 영어로.
- **B. 복붙 템플릿**: `Docs/tech/ai-collab-prompts.md` — 영상의 프롬프트 원문 6개를 한국어 그대로.
  Codex 등 다른 AI 도구를 쓰는 팀원, Claude Code 밖(웹 챗 등) 상황용.
- **C. 개인 설정**: `~/.claude/CLAUDE.md`에 핵심 3개(사각지대 브리핑·구현노트·완료 후 퀴즈) 요약 추가
  — 개인 프로젝트(Aether 등)에서도 작동.

## 실행 체크리스트

- [x] AIRULE.md 확장 (①②④⑤⑥)
- [x] Docs/tech/ai-collab-prompts.md 생성 (원문 6기법 + 대응표)
- [x] .claude/commands/diff-check.md에 📝 퀴즈 섹션 추가
- [x] ~/.claude/CLAUDE.md 개인 규칙 추가
- [x] 통독 검증: AIRULE 기존 절과 모순 없음, diff-check 12개 한도와 퀴즈 별도 카운트 명시
