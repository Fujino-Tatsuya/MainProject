# PLAN — 몬스터 데이터 인스펙터 정리(한국어 · 접기) (2026-10-19 · 경석)

> 상태: ✅ **완료(10-19)** — 1차 표시 `b54596df` · `attackWindup` 연결 `2d9efa34`([PLAN-attack-windup.md](PLAN-attack-windup.md)) · 죽은 칸 6개 삭제(xlsx r461 전체 시트 행 0 확인 · 컴파일 0 · 전투 EditMode 246/0 · Verify "필드 없음" 0 · 차이 96 그대로).
> **2차(장판·드론·예고 스타일·중간보스)는 하지 않는다**(경석 10-19).
> 삭제 칸의 저장 줄은 에셋(No23 3종·BossElectricFloorData 2종·WellsDroneData)에 남아 있다 — Unity 가 무시하고 다음 저장 때 빠진다(에셋 6개를 손으로 고치지 않았다).
> 남은 미사용 **8칸**(maxShield · grabThrowDistance · bombThrowInterval · spreadAngle · 자폭 간파 3 · 자폭 감소율)은 xlsx 행이 있어 그대로 — **경석이 xlsx 행을 지워 SVN 에 올린 뒤** 코드에서 삭제(순서 거꾸로면 테이블 Play·빌드가 "필드 없음"으로 막힌다).
> (§3.4 원래 목록은 9칸이었다 — `attackWindup` 이 연결돼 빠졌다.)
>
> 구현 메모(계획 대비 추가): 묶음 "전투 기본 · 간파 · 그로기"·"원거리 몬스터 전용"을 새로 뒀다(사거리·공격 지속·슈퍼아머·간파 각도 등 원안 10묶음 어디에도 안 맞던 칸). 원거리 묶음은 원거리 유형일 때만 펼쳐서 시작.
> 배열은 끌어서 순서 바꾸기를 없앴다(개수 칸만 — 줄이면 끝에서 지움). `[Header]` 붙은 칸은 머리글이 엉뚱한 묶음에 따라 나오지 않게 값만 직접 그린다(Range·Min 직접 적용).
> 근거: 10-19 팀장 채팅 결정(§1) · 전수조사 [monster-data-survey-1019.md](Docs/tech/monster-data-survey-1019.md)(333칸 전체 표).

## 1. 확정된 결정 (팀장 10-19)

| 항목 | 결정 |
|---|---|
| 보는 사람 | **팀장 + 기획** |
| 방식 | **접기** — 자주 고치는 칸만 맨 위에 펼쳐 두고, 나머지는 묶음별로 접는다 |
| 표기 | **인스펙터에 보이는 이름은 한국어**. 코드 변수 이름(영어)은 그대로 — 바꾸면 xlsx 행·저장값이 끊긴다 |
| 목표 | "눈에 잘 보이고 읽기 쉽고 바로 이해되게" — 하루 종일 봐야 보이는 상태를 없앤다 |

## 2. 조사로 드러난 것 (요약 — 전체는 조사 문서)

- **23호 인스펙터 한 화면에 MonsterDataSO 60칸 + BossDataSO 115칸 + 공격 행 8개 × 29칸**이 한꺼번에 보인다.
- 🔴 **10월 이후 밸런스 튜닝은 에셋이 아니라 xlsx(SVN)에서 한다**(기획 김태현 — r425 중간보스 밸런스, r394 몬스터 HP).
  지금 **xlsx 와 인스펙터 값이 다른 칸이 70개** — 몬스터 공격력·공격속도·최대 HP 는 10/10, 보스 공격 쿨 12개가 다르다.
  이 칸들은 **테이블 모드·빌드에서 인스펙터 값이 무시된다.** "무엇을 고쳐야 할지 안 보인다"의 한 원인.
- 🔴 **기획이 죽은 칸을 튜닝하고 있었다** — `attackWindup` 은 코드가 읽지 않는데 r425 에서 4종 값이 바뀌었다.
- 죽은 칸 15개 · 23호에 의미 없는 원거리 전용 칸 15개 · "에셋끼리 값이 전부 같고 한 번도 안 바뀐" 칸 약 70개 · 아트/기술 측정값·애니 상태명 약 30개.
- 이 타입들에는 지금 **CustomEditor 가 하나도 없다** — 새로 만든다.

| 타입 | 칸 | 죽은 칸 | xlsx 행 있음 |
|---|---:|---:|---:|
| MonsterDataSO | 60 | 2 | 31 |
| BossDataSO 자체 | 115 | 9 | 87 |
| BossAttackEntry(공격 행) | 29 | 0 | 20 |
| 장판·드론·예고 스타일 | 14·32·17 | 2·2·0 | 시트 없음 |
| 중간보스 프리팹 3종 | 24·16·15 | 0 | 10·8·10 |

## 3. 설계

### 3.1 공통 — "칸 사전" 하나 + 커스텀 인스펙터
- 에디터 전용 `MonsterInspectorCatalog`(칸 경로 → **한국어 이름 · 묶음 · 자주 고침 여부 · 숨김 여부**) 한 곳에 정리.
- `MonsterDataSO`·`BossDataSO` 용 CustomEditor 가 이 사전대로 그린다. 툴팁(지금 영어/한국어 설명)은 그대로 살린다 — 마우스를 올리면 상세 설명.
- 🔴 **사전에 없는 칸은 맨 아래 "미분류" 묶음에 자동으로 나온다** — 새 칸을 추가하고 사전을 깜빡해도 안 사라진다.
- 변수 이름·직렬화·xlsx 는 그대로라 **적용 경로(Play·빌드)는 영향 없음** — 적용은 `propertyPath` + `FindProperty` 로만 찾는다.
  ⚠️ 정정(10-19 교차검증): "DataTable 은 [DataTableIgnore] 만 본다"는 틀렸다. **Export 는 `[Header]`·`[Tooltip]` 을 읽어 xlsx 구역·설명을 만들고**(`DataTableTemplate.cs`), **Verify 는 `[Range]`·`[Min]` 을 읽는다**(`DataTableFields.cs`).
  → **한국어 이름·묶음은 에디터 사전에만 둔다. 런타임 코드의 Header·Tooltip·Range·Min 은 건드리지 않는다.**
- **배열 순서는 계약이다** — 공격 행 인덱스가 xlsx 행·슬롯과 묶여 있다(`BossDataSO.cs` 머리 주석). 화면 표시용으로 자동 정렬·재작성하지 않는다.
- 자식 칸은 `PropertyField` 로 그려 Range·Min·Tooltip 을 그대로 살리고, Drawer 는 `BeginProperty/EndProperty` 로 감싼다(프리팹 오버라이드 굵은 글씨·우클릭 메뉴 유지).
- 공용 에디터는 `[CustomEditor(typeof(MonsterDataSO), true)]` + `[CanEditMultipleObjects]` — BossDataSO 가 MonsterDataSO 를 상속한다.

### 3.2 xlsx 가 정하는 칸 표시 (제안 — §7-1)
- xlsx 에 행이 있는 칸 옆에 **📄 배지**: "이 칸은 테이블 모드·빌드에서 xlsx 값으로 덮인다".
- 인스펙터 값과 xlsx 값이 다르면 **노란색 + xlsx 값 표시**(예: `HP 2880  📄 표 3200`). Verify 결과를 칸 옆에 바로 보여 주는 것.
- 🔴 **새로 xlsx 를 읽지 않는다 — 이미 있다.** `DataTable/Editor/DataTableInspectorBadge.cs` 가 인스펙터 머리에
  "테이블과 다른 값 N개(최대 6개 나열)"를 띄우고, xlsx 수정 시각이 바뀔 때만 다시 읽는 캐시를 갖고 있다.
  `DataTableWrite` 에 `PropertyPath` 가 있으니 **그 캐시에 칸 단위 조회 함수(`TryGetWrite(target, path)`)만 추가**한다.
- 머리 박스와 칸 옆 배지가 같은 말을 두 번 하지 않게 한다(§7-5).
- xlsx 없음·읽기 오류 → 배지 대신 "비교 불가" 한 줄. 다중 선택 → 배지 생략(기존 머리 배지도 단일 선택만 그린다).

### 3.3 묶음 (23호 기준 · 일반 몬스터는 해당 묶음만)
| 순서 | 묶음 | 기본 | 들어가는 것(예) |
|---|---|---|---|
| 1 | ★ 자주 고치는 것 | **펼침** | 최대 HP · 공격력 · 공격 속도 · 이동/추격 속도 · 공격 간격 · 방어 · 그로기 시간 · 취약 시간 · 제압 시간 · 전역 공격 간격 |
| 2 | 공격 목록 | **펼침** | 행 제목 = **공격 이름**("왼훅" · "점프어택"). 행 안에서 피해 · 쿨 · 거리(최소~최대) · 가중치 · 간파 창만 보이고 나머지(애니 상태명·판정 모양 등)는 행 안에서 다시 접힘 |
| 3 | 콤보 | **펼침** | 행 제목 = 콤보 이름 · 가중치 · 쿨 · 순서(공격 이름으로) · 간격 · 추격 유예 · 개전 순서 |
| 4 | 공격별 피해·넉백 | 접힘 | 잡기 틱/내려치기 · 점프 착지 · 돌진 · 레이지 피해 · 넉백 4종 |
| 5 | 페이즈 · 레이드 | 접힘 | 페이즈 표 · 인원별 HP · ②③④ 칸 |
| 6 | 어그로 · 인지 | 접힘 | 인지/리쉬/추적 해제 반경 · 위협 어그로 · 원거리 압박 |
| 7 | 송전기 · 장판 · 드론 연결 | 접힘 | 송전탑 수 · 차징 시간 · 장판/드론 데이터 참조 |
| 8 | 연출 · 애니메이션 이름 | 접힘 | 애니 상태명 약 20개 · 파라미터명 · VFX 참조 · 클립 길이/프레임 측정값 |
| 9 | 기술값 | 접힘 | 버퍼 크기 · 디버그 로그 · 회피 반경 · locomotion 측정값 |
| 10 | ⚠️ 미사용 | 접힘·회색 | 죽은 칸(아래 §3.4) — "xlsx 행 정리 후 삭제 예정" 안내 |

- ~~23호에서 원거리 전용 base 칸 15개는 아예 안 그린다~~ ⚠️ 뒤집음(10-19 교차검증): 23호 단타는 `base.HandleAttack` 을 타고
  `cancelWindupIfTargetLeavesRange` 를 읽는다(`TwentyThreeBoss.cs` → `MonsterBase.cs:1196`). "원거리 전용"이 실명 목록 없이
  묶여 있어 공통 칸이 섞일 수 있다. → **숨기지 않고 BossDataSO 에서 "원거리 몬스터 전용" 묶음으로 접어 둔다.**
  `cancelWindupIfTargetLeavesRange` 는 공통 공격 설정(★ 아래 기술값)으로 둔다. 숨김 기능은 사전에 남기되 1차에선 안 쓴다.

### 3.4 죽은 칸 처리
| 칸 | xlsx 행 | 처리 |
|---|---|---|
| jumpTelegraphPrefab · bombPrefab · Obsolete 색 4개(장판·드론) | 없음 | **코드에서 삭제** |
| maxShield · attackWindup · raidSelfDestruct Counter 3개·DamageReduction · grabThrowDistance · bombThrowInterval · spreadAngle | **있음** | 지우면 Play·빌드가 막힌다 → **"미사용" 묶음으로만 이동**. 다음 병합 Export 때 xlsx 행을 정리한 뒤 삭제 |
- `attackWindup` 은 기획이 r425 에서 값을 바꿨다 → 기획에게 "효과 없는 칸"이라고 알린다(또는 의도대로 연결할지 §7-2).
- "죽은 칸" = **전투 효과 없음**. 이름 참조가 전혀 없는 칸과는 다르다(교차검증 10-19):
  - `attackWindup` — 프로퍼티 `MonsterBase.AttackWindupSeconds` 가 읽지만 호출자 0
  - `grabThrowDistance` — 경고 로그 문구에만 쓰임
  - `bombThrowInterval` — `BossWells` 타이머에 들어가지만 만료 콜백 `OnWellsAttackCycle() { }` 가 비어 있음
  - 🔴 `bombPrefab`·`jumpTelegraphPrefab` — **에디터 도구 `BossDataWiring.cs`·`BossVariantAuthoring.cs` 가 직접 읽는다.** 같이 고치지 않으면 컴파일 실패
- 삭제 6개는 현재 `GameData.xlsx` 에 행이 없고, 장판·드론 SO 에 `[DataTableSheet]` 도 없다(Codex 가 xlsx 직접 확인) → f5b48b27 같은 "필드 없음" 재발 근거 없음. **삭제 직전 SVN 최신 xlsx 로 한 번 더 확인.**

### 3.5 범위 · 순서
1. **1차**: MonsterDataSO + BossDataSO + 공격 행 + 콤보(23호·일반 몬스터 화면 — 가장 크고 자주 봄)
2. **2차**: 장판 · 드론 · 예고 스타일 SO, 중간보스 프리팹 칸(GauntletBot·SpinnerBot·WallBot)

## 4. 수정 파일 (예정)
- 신규: `Monster/Editor/MonsterInspectorCatalog.cs`(칸 사전) · `MonsterDataSOEditor.cs` · `BossAttackEntryDrawer.cs` · `BossComboEntryDrawer.cs` · 테스트(사전에 없는 칸이 미분류로 나오는지 · 사전 경로가 실제 칸과 맞는지)
- `Monster/Boss/BossDataSO.cs` · `BossElectricFloorDataSO.cs` · `WellsDroneDataSO.cs`(죽은 칸 6개 삭제만)
- `Monster/Editor/BossDataWiring.cs` · `BossVariantAuthoring.cs`(삭제 칸 참조 제거 — 교차검증에서 발견)
- `DataTable/Editor/DataTableInspectorBadge.cs`(칸 단위 조회 함수 추가 · 머리 박스 중복 처리)
- 데이터 에셋: 값 변경 없음(삭제 칸의 저장 줄만 다음 저장 때 빠짐)
- 🔴 **순서**: `No23`·`No23_Raid` 에 미커밋 콤보 변경이 걸려 있다. **칸 삭제는 콤보 커밋 뒤** — 안 그러면 diff 가 섞인다.
  인스펙터(표시만) 작업은 에셋을 안 건드리니 먼저 해도 된다.

## 5. 리스크
- **새 칸을 사전에 안 넣으면** — "미분류"로 나오니 숨지는 않는다(설계로 막음).
- **변수 이름은 안 바꾼다** — FormerlySerializedAs·xlsx 행·테스트·저작 도구가 이름에 묶여 있다(조사 §위험).
- 커스텀 인스펙터에서 **여러 에셋 동시 편집 · Undo** 가 깨지지 않게 SerializedProperty 로만 그린다.
- xlsx 배지(§3.2)는 xlsx 를 읽어야 해 인스펙터가 느려질 수 있다 → 에셋 선택 때 한 번만 읽고 캐시.
- 중간보스는 프리팹 컴포넌트라 프리팹 오버라이드(굵은 글씨) 표시를 유지해야 한다(2차).

## 6. 검증
- Tools/Data/Verify — "필드 없음" 0 · 차이 수 변화 없음(지금 96).
- 에셋을 인스펙터로 열고 저장해도 **git diff 0**(삭제 칸 6개 줄 제외). HEAD 가 아니라 **작업 직전 사본**과 비교한다(미커밋 변경이 있어서).
- **열기·접기만으로 에셋이 dirty 되지 않는지**(접힘 상태는 `SessionState`/`EditorPrefs` 에만 저장).
- 값 수정 → Undo/Redo · 다중 선택(공격 행 개수가 다른 두 에셋 포함) · 공격/콤보/steps 행 추가·삭제.
- 테이블 모드 Play 진입·종료 · 빌드(xlsx 경로).
- EditMode 전체 회귀 + 사전 테스트.
- 팀장·기획이 23호·ChompBot 인스펙터를 열어 "자주 고치는 칸이 바로 보이는지" 확인.

## 7. 팀장 확인 필요 → ✅ 10-19 결정
| # | 결정 |
|---|---|
| 1 | **넣는다** — 칸 옆 배지 |
| 2 | **`attackWindup` 을 공격 선딜로 실제 연결** — 단 동작 변경(몬스터 4종 체감)이라 인스펙터와 **별도 작업**. 현재 공격 흐름 조사 → 연결 방식 질문 → Play 검증. 그때까지 인스펙터에선 "미사용(연결 예정)"으로 표시 |
| 3 | **초안대로 시작** — 사전에서 줄 단위로 조정 |
| 4 | **표시 작업부터**, 칸 삭제는 콤보 커밋 뒤 |
| 5 | 머리 박스는 **요약 한 줄**로 줄인다 |
| 6 | (10-19 화면 확인 후) **표 값 ≠ SO 값인 칸은 맞추지 않고 그대로 둔다** — 수치는 xlsx 를 고치고 빌드가 xlsx 를 읽는다. SO 값은 정본이 아니다. 노란 줄은 "여기 고쳐도 소용없다" 안내로만 쓴다 |

(원래 질문)
1. **xlsx 가 정하는 칸 표시(§3.2)** 넣을지 — 넣으면 "인스펙터에서 바꿨는데 안 바뀐다"가 사라진다(제안: 넣음).
2. **`attackWindup`**: 미사용으로 둘지, 기획 의도(공격 선딜)대로 실제로 연결할지.
3. **"★ 자주 고치는 것" 목록**이 맞는지 — 조사(git·xlsx 변경 이력) 기준 초안이라 팀장·기획이 더하거나 뺀다.
4. 1차 범위(§3.5)로 시작해도 되는지.
5. (교차검증 추가) 칸 옆 배지를 넣으면 기존 **머리 박스**는 — 요약 한 줄로 줄일지, 이 에디터에서만 끌지.

## 8. 교차검증 기록 (10-19 · Claude + Codex 병렬)
- 두 쪽 공통: §3.2 기존 배지 재사용 · `BossDataWiring`/`BossVariantAuthoring` 누락 · 죽은 칸 동작 판정 맞음.
- Codex 단독: Export 가 Header·Tooltip 을, Verify 가 Range·Min 을 읽는다(§3.1 정정) · `cancelWindupIfTargetLeavesRange` 공통 사용(§3.3 뒤집음) · 배열 순서 계약.
- Claude 단독: 미커밋 콤보 에셋과 삭제 순서 충돌(§4).
- 미검증(둘 다): Unity 컴파일·Verify·재저장 바이트 동일성 — 구현 단계에서 확인.
