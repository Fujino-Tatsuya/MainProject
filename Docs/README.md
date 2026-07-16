# Docs — 문서 인덱스

이 폴더는 프로젝트의 **단일 출처(Single Source of Truth)** 다. 기획/기술 문서는 코드처럼 PR로 리뷰한다.
작업 추적(태스크)은 가능하면 GitHub Issues/Projects를 쓰고, 이 문서들은 "정의/규칙"을 담는다.

## 설계 (GDD)
- [design/boss-wells-and-no23.md](design/boss-wells-and-no23.md) — 보스 "웰즈 & 23호" 컨셉·기믹·페이즈
- [design/player.md](design/player.md) — Player Prefab, 입력·이동·피격 책임
- [design/interaction-policy.md](design/interaction-policy.md) — Instigator/Receiver/Target 상호작용 책임 정책
- [design/character.md](design/character.md) — Character 데이터, Prefab, UserProfile 보정
- [design/character/character_garen.md](design/character/character_garen.md) — Garen 캐릭터 요약
- [design/character/character_jinx.md](design/character/character_jinx.md) — Jinx 캐릭터 요약
- [design/ability.md](design/ability.md) — Ability 슬롯, 실행 타입, 패시브 구조
- [design/status-effects.md](design/status-effects.md) — 상태이상 정의/게이팅
- [design/builds.md](design/builds.md) — 빌드(모디파이어) 시스템, R 잠금/해금
- [design/level-system.md](design/level-system.md) — 레벨/난이도 4축, 콘텐츠 난이도, 난이도↔전투 상호작용

## 기술
- [tech/game-structure-uml.md](tech/game-structure-uml.md) — C# 189개와 씬·프리팹·SO·Behavior Graph를 교차검증한 현재 게임 구조 UML
- [tech/script-inventory.md](tech/script-inventory.md) — 전체 C# 189개 파일별 역할·상속·실제 연결/미연결 상태 전수 목록
- [tech/architecture.md](tech/architecture.md) — UnitBase·컴포넌트, 스탯/상태 자료구조, FSM/페이즈
- [tech/networking.md](tech/networking.md) — NGO / IPv4·Steamworks, 권한 모델, 연결 흐름
- [tech/conventions.md](tech/conventions.md) — 네이밍/폴더/Input/SO 규칙
- [tech/workflow.md](tech/workflow.md) — git 브랜치·PR, Unity 협업 세팅, SVN/Git 하이브리드
- [tech/map-generation.md](tech/map-generation.md) — 절차적 맵 생성 흐름, 영역/티어 규칙, NGO 서버권한 생성+결과복제

## 일정
- [tasks/roadmap.md](tasks/roadmap.md) — 마일스톤(6~7월)

---
> 루트 [AGENT.md](../AGENT.md) 를 먼저 읽을 것. (온보딩 + 지금 당장 고칠 것)
