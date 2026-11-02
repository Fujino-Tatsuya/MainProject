# MCP RunCommand 뒤 씬 dirty 막기 (할 일 190)

> 코드: `Assets/1.Scripts/Editor/McpRunCommandDrivenUndoFilter.cs` (에디터 전용, 런타임 빌드에 안 들어감)
> 테스트: `Assets/Tests/EditMode/Mcp/Editor/McpRunCommandDrivenUndoFilterTests.cs`

## 증상
레인·워커가 공식 Unity MCP `Unity_RunCommand` 로 Refresh·테스트·메뉴를 돌리면, 씬을 고친 적이 없는데
열린 씬(특히 UI 가 있는 로비 씬)이 dirty 가 되어 에디터를 닫을 때 저장을 묻는다. 저장하면 레이아웃이 계산한
RectTransform 값이 씬 파일에 섞여 들어간다(181 조사).

## 원인 (2026-11-02 확인)
- RunCommand 는 실행 전후에 `ExecutionResult.Start()` → `Undo.IncrementCurrentGroup`,
  `ExecutionResult.End()` → `Undo.CollapseUndoOperations` 를 부른다
  (`Packages/com.unity.ai.assistant/Editor/Assistant/RunCommand/AgentExtensionAssembly/ExecutionResult.cs:136~150`).
- 그 사이 UGUI 레이아웃이 자식 RectTransform 을 다시 구동하면(DrivenRectTransformTracker, Undo 이름 `Driving RectTransform`),
  그 값 변경이 Collapse 에서 Undo 수정으로 확정되며 `Undo.postprocessModifications` 를 거쳐 씬이 dirty 가 된다.
  호출 스택: `EditorSceneManager.Internal_SceneDirtied` ← `Undo.CollapseUndoOperations` ← `ExecutionResult.End()`.
- 평소 에디터 루프에서는 같은 구동이 씬을 dirty 로 만들지 않는다. 구동 값은 레이아웃이 매번 다시 계산하므로 실제 편집이 아니다.
- 언제 레이아웃이 명령 안에서 돌지는 타이밍에 달려 있어 재현이 들쭉날쭉하다(도메인 리로드 직후·씬 연 직후 등).
  확정 재현: 로비 씬에서 RunCommand 로 레이아웃 자식(`ObjectiveText`)의 anchoredPosition 을 Undo 없이 흔들고
  `Canvas.ForceUpdateCanvases()` → 다음 RunCommand 에서 `isDirty=True`.
- 패키지 설정에 이 Undo 처리를 끄는 옵션은 없다(패키지는 로컬 설치·git 미추적, 수정 금지).

## 처리 — 메커니즘 하나
`Undo.postprocessModifications` 훅에서 **RunCommand 의 `ExecutionResult.End()` 안(호출 스택 확인)** 일 때만,
**지금 구동 중인 RectTransform 속성**(internal `RectTransform.drivenProperties` 에 포함된 경로)에 대한 수정을 버린다.

- 명령이 실제로 바꾼 값(구동되지 않는 속성·다른 컴포넌트·오브젝트 추가/삭제)은 그대로 기록되고 dirty 도 그대로다.
- 이미 dirty 인 씬을 지우지 않는다 — 새로 dirty 가 되는 원인 하나만 막는다.
- 비용: 수정 묶음에 RectTransform 이 있을 때만 스택을 본다.

## 검증 (2026-11-02, 로비 씬)
| 경우 | 훅 없음 | 훅 있음 |
|---|---|---|
| 레이아웃 자식 흔들기 + ForceUpdateCanvases 뒤 | dirty=True | dirty=False |
| `AssetDatabase.Refresh()` 만 | — | dirty=False |
| `ExecuteMenuItem("Tools/Tests/EditMode 전체 실행")` 뒤 | — | dirty=False |
| 빈 GameObject 추가(`RegisterObjectCreation`) | — | dirty=True (유지) |
| 레이아웃 자식 흔들기 + 같은 명령에서 GameObject 이름 변경(`RegisterObjectModification`) | — | dirty=True (실제 변경 유지) |
| 이미 dirty 인 씬 + Refresh | — | dirty=True (지우지 않음) |

`—` = 훅 없이는 재현 타이밍이 들쭉날쭉해 따로 비교하지 않음(위 확정 재현으로 대조).

## 한계
- 패키지(`com.unity.ai.assistant`)가 `ExecutionResult.End` 이름·위치를 바꾸면 필터가 조용히 꺼진다(원래 증상으로 돌아갈 뿐).
- 엔진이 internal `RectTransform.drivenProperties` 를 없애면 훅이 구독하지 않는다(같음).
- RunCommand 밖(사람이 에디터에서 작업) 동작은 바꾸지 않는다.
- 레이아웃 말고 다른 원인(예: 명령 코드가 `SetDirty`·`MarkSceneDirty` 를 직접 부름)으로 생긴 dirty 는 정상으로 본다.
