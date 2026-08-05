# BT 에셋이 열 때마다 항목을 잃는 문제 — 원인 확정

조사 2026-08-05 / 대상 `Assets/8.BehaviorTreeGraph/Enemy/CommonMeleeRobot.asset`
환경 Unity 6000.3.16f1, `com.unity.behavior` **1.0.16** (manifest·lock 일치, 에셋 `version: 2`)

## 결론 한 줄

**패키지 버전 문제도, 정상 정리도 아니다.** 삭제된 서브그래프 에셋
`Assets/8.BehaviorTreeGraph/Boss/Wells&No.23/MonsterArea.asset` 를 아직 두 그래프가 참조하고 있어서,
Unity가 열 때마다 그 참조를 끊고 에셋을 다시 쓴다. git으로 되돌리면 끊긴 참조가 되살아나므로 **무한 반복**한다.

## 증거

### 1. Unity 콘솔 메시지 (Editor.log)

```
CommonMeleeRobot: Referenced subgraph asset has been deleted. Clearing the RunSubgraph node reference.
  Unity.Behavior.SubgraphNodeModel:ValidateCachedRuntimeGraph () (SubgraphNodeModel.cs:434)
  Unity.Behavior.SubgraphNodeModel:OnValidate ()               (SubgraphNodeModel.cs:343)
```

임포트 직후(`NativeFormatImporter`) 바로 발생한다. 즉 **에디터가 의도적으로 참조를 지우고 저장**하는 동작이다.

### 2. 끊긴 참조의 정체

에셋이 참조하는 GUID를 전수 검사하니 실제 결손은 **딱 하나**다.

| 참조 | GUID | 상태 |
|---|---|---|
| `m_SubgraphAuthoringAsset` (type: 2 = 에셋) | `9e76e21e6f15f714a9401f1c4fbdc94c` | **결손** |
| `m_Script` 5건 (type: 3 = 스크립트) | — | 정상 (`com.unity.behavior` 패키지 소속이라 `Assets/` 밖에 있음) |

그 GUID = 삭제된 `MonsterArea.asset` 의 meta GUID다(`git show 58278e920^:...meta` 로 확인).

### 3. 삭제 시점과 정황

```
2026-07-30 09:54  9c05eebd5  fix(bt): MonsterArea 그래프 누락 관리 참조 복구
2026-07-30 12:05  494fc4acd  fix(bt): BossArea 그래프 누락 관리 참조 재복구
2026-07-31 02:50  58278e920  fix(boss): Wells 폭탄 투척 복구 — 중첩 NetworkObject 제거
                             └ Boss/Wells&No.23/MonsterArea.asset (1601줄) + .meta 삭제
```

7/30에 두 번 복구한 참조를, 다음날 **보스 폭탄 수정 커밋이 에셋 자체를 삭제**하면서 다시 깨뜨렸다.
보스 폭탄 수정과 BT 서브그래프 삭제는 무관하므로 **부수 피해로 보인다.**

### 4. 영향 범위 — 몹만이 아니다

GUID `9e76e21e...` 를 아직 참조하는 현존 에셋:

- `Assets/8.BehaviorTreeGraph/Enemy/CommonMeleeRobot.asset` (일반 근거리 몹)
- `Assets/8.BehaviorTreeGraph/Boss/Wells&No.23/No.23.asset` (**보스**)

### 5. 매번 사라지는 것들

블랙보드 변수 `Area`, `Self`, `MonsterArea` / 필드 바인딩 `Subgraph`, `Blackboard`, `Self`, `IsOpen`, `AreaName`, `Area`
(= `BlackboardVariable<BehaviorGraph>`, `BlackboardVariable<BehaviorBlackboardAuthoringAsset>`,
`BlackboardVariable<GameObject>`×2, `<String>`, `<Boolean>` + `FieldModel` 6개)

전부 **RunSubgraph 노드가 서브그래프에 넘기던 입력**이다. 즉 지금 이 몹·보스 그래프의 에어리어 관리 로직은
런타임에 동작하지 않는다고 봐야 한다(참조가 비어 있으므로).

## 재발 방지 — 선택지

### A. 삭제된 서브그래프 복구 (권장)

GUID가 보존되므로 파일만 되살리면 두 그래프의 참조가 **그대로 유효해지고 churn이 멈춘다.**

```bash
git checkout 58278e920^ -- "Assets/8.BehaviorTreeGraph/Boss/Wells&No.23/MonsterArea.asset" "Assets/8.BehaviorTreeGraph/Boss/Wells&No.23/MonsterArea.asset.meta"
```

복구 후 Unity에서 열어 ① 콘솔에 위 메시지가 더 안 뜨는지 ② `CommonMeleeRobot.asset` 이 dirty가 되지 않는지
③ MonsterArea 서브그래프가 1.0.16에서 정상 역직렬화되는지 확인한다.

### B. 정리 상태를 수용

MonsterArea가 정말 폐기 대상이면, 에디터에서 정리된 결과(항목 783개)를 그대로 커밋하고
두 그래프의 RunSubgraph 노드를 제거하거나 다른 서브그래프로 재지정한다. 그러면 끊긴 참조가 없어져 churn이 끝난다.
**단 에어리어 관리 기능을 대체할 로직이 있어야 한다.**

A인지 B인지는 "MonsterArea 서브그래프가 지금 필요한 기능인가"로 갈린다 — 보스 그래프(No.23)까지 참조하므로
**A를 먼저 시도**하고, 열어본 뒤 불필요하다고 판단되면 B로 정리하는 순서를 권한다.

## 하지 말 것

- `git checkout --` 으로 되돌리기만 반복하는 것. 끊긴 참조가 되살아나 다음 Unity 실행에서 같은 손실이 재현된다(2026-08-05 두 번 확인).
- 정리된 에셋을 무심코 커밋하는 것. 원인을 남긴 채 증상만 굳는다.
- 이 수정을 `feature/FloatingDamage` 에 섞는 것. 무관한 변경이다.

## 미확인

- `No.23.asset` 도 같은 churn을 겪는지는 이번 세션에서 dirty로 관측되지 않았다(그래프를 열지 않은 것으로 보인다). A 적용 전에 한 번 열어 확인할 것.
- `Assets/8.BehaviorTreeGraph/` 나머지 에셋(Wells, ReStart, Boss State Changed 등)의 결손 참조 전수 검사는 하지 않았다.
