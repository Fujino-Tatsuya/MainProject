---
description: Codex가 커밋한 뒤([RELOAD] 로그) Claude가 최신 상태로 다시 동기화
---

워처 로그에 `[RELOAD]`가 찍혔거나, `work_completed` 메시지에 새 커밋 해시가 있을 때 이걸 돌려서
Codex가 바꾼 내용을 따라잡는다.

## 0. 레인을 먼저 확정한다 — 🔴 **프로젝트 이름 ≠ 레인 이름**

**레인 이름과 워크트리 경로를 하드코딩하지 마라.** 프로젝트가 `MainProject`라고 해서 레인이
`MainProject`인 것이 아니다. 레인은 *체크아웃(워크트리)* 단위이고, 같은 프로젝트에 여러 레인이 있다
(`MainProject` / `MainProject-WorkTree` / `MainProject-MLAgent` …). 엉뚱한 레인의 워크트리에서
`git log`를 읽으면 **남의 브랜치 커밋을 보고 엉뚱한 보고를 하게 된다.**

`-Lane` 없이 상태를 찍어 레인 전체와 각 레인의 브랜치를 보고, **지금 세션의 작업 디렉터리·브랜치와
일치하는 레인**을 고른다:
`powershell -File C:\Users\user\Desktop\Co_Working_for_Agents\agent-context-bridge\codex-lane.ps1 -Action status`

확실하지 않으면 추측하지 말고 사용자에게 묻는다. 아래의 `<WORKTREE>` 는 그 레인의 워크트리 경로다.

## 1. 최근 보고 확인

`read_agent_messages(recipient:"claude", sender:"codex", last_n:5)`로 가장 최근 `work_completed`
메시지를 찾는다. 거기 적힌 커밋 해시를 기록해둔다.

## 2. 실제 git 상태 확인

`git -C <WORKTREE> log -1 --oneline`으로 현재 HEAD를 확인하고, 1번의 보고 내용과
일치하는지 본다. 다르면(예: 그 사이 다른 handoff가 또 처리됨) 최신 것 기준으로 진행한다.

## 3. 변경 내용 확인

`git -C <WORKTREE> show --stat <커밋 해시>`로 이번 커밋에서 뭐가 바뀌었는지 파일
목록을 보고, 그중 지금 작업과 관련 있어 보이는 파일은 실제로 열어서 diff까지 확인한다
(`.meta`·바이너리는 건너뜀).

## 4. 필요하면 CONTEXT.md 갱신

Codex가 바꾼 내용이 "공유 이해"에 영향을 주는 수준이면(예: 새 컴포넌트 추가, 계약 변경) CONTEXT.md에
한 줄 추가한다. 사소한 수정이면 생략.

## 5. 사용자에게 요약

무엇이 바뀌었는지, 지금 작업과 겹치거나 충돌하는 부분이 있는지 짧게 보고한다.
