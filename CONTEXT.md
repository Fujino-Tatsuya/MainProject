# CONTEXT.md - Shared Project Language

> 🆕 **새 환경에서 처음 여는 사람은 [Docs/tech/environment-setup.md](Docs/tech/environment-setup.md) 부터.**
> 이 프로젝트는 git 만으로 안 선다 — 아트가 SVN 에 있고, 없어도 Unity 는 조용히 열린다.
> 열기 전에 `python Docs/tech/check-environment.py` 를 돌릴 것.

This file defines the shared vocabulary for the project. Keep it concise. It is not a full spec and should not contain implementation plans.

Update this file when a term becomes important enough that future agents or teammates must use it consistently.

## ▶▶ 작업 세션 (2026-10-02 · 은희(Claude) · **데이터 테이블 xlsx → SO D1**, 브랜치 `feature/DataTable`)

계획 = [PLAN-data-table.md](PLAN-data-table.md). 이번 세션 = **D1 테이블 적용기 코어**(툴만, 게임 코드 변경 0).
🔴 **수정 예정 파일 — 동시 수정 금지:** `Assets/1.Scripts/DataTable/**` · `Assets/Tests/EditMode/DataTable/**` · `PLAN-data-table.md` · `Assets/1.Scripts/Dev/**` · Player SO·컴포넌트(어트리뷰트) · 몬스터 SO·컴포넌트(어트리뷰트만).
- §8 미확정 Q1·Q2·Q4·Q5·Q6 은 D1 에 영향 없음 → 추천안 가정으로 진행, D2 전에 확정.
- 상태: ✅ D1·D1.5·D1.6·D2·D3·D4 커밋, EditMode 54건(메뉴 `Tools/Tests/데이터 테이블 EditMode 테스트 실행`). `feature/DevBootToolbar` 병합(PR 하나로).
  🔴 **D3·D4 설계 변경**: 프리팹 값을 SO 로 옮기지 않고 **테이블이 프리팹 컴포넌트를 직접 덮어쓴다**(시트 = 컴포넌트 타입, Id = 프리팹 파일 이름). 상세 PLAN §D3·D4.
  🔴 **게임 코드 변경(은희 지시)**: `Player.moveSpeed` 삭제(Unit 이동 스탯 = `PlayerMovement.maxSpeed`) · `fallDamageRatio` 씬 → `PlayerGameRuleData` · `PlayerMovement.Start()` 의 rotate_Speed 덮어쓰기 제거.
  🔴 **경석 영역 변경(은희 지시)**: `MonsterDataSO`·`BossDataSO`·`MonsterMeleeAttack`·`MonsterCounterWindow`·`TurretHeadAim`·`Gauntlet/Spinner/WallBot`·`MonsterBase` 에 어트리뷰트만(동작 변경 0) — 경석 공유 필요.
  ✅ 은희 새 Export → Verify 0(10-02). Q10 = `GameRule.prefab` 분리(`d87eeeb7`). ✅ D6 운영 보강(병합 Export·범위 검증·씬 오버라이드 경고·인스펙터 표시·가이드 `Docs/tech/data-table.md`), EditMode 64건.
  ⏳ 은희: 테이블/인스펙터 Play(몬스터 HP·목숨 수 — Dev Boot·씬 직접 Play 둘 다)·MPPM · 실제 빌드 1회(후 git status 0) · `GameData.xlsx` SVN.
