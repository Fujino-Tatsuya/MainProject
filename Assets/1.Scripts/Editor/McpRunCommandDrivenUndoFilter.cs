using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 공식 Unity MCP <c>Unity_RunCommand</c> 가 끝날 때 열린 씬을 괜히 dirty 로 만드는 것을 막는다(할 일 190).
///
/// 원인: RunCommand 는 실행 뒤 <c>ExecutionResult.End()</c> 에서 <c>Undo.CollapseUndoOperations</c> 를 부른다.
/// 그 사이 UGUI 레이아웃이 자식 RectTransform 을 구동(DrivenRectTransformTracker, Undo 이름 "Driving RectTransform")했으면
/// 그 값 변경이 Collapse 에서 Undo 수정으로 확정되며 씬이 dirty 가 된다. 평소 에디터 루프에서는 같은 구동이 씬을 dirty 로
/// 만들지 않는다. 구동 값은 레이아웃이 매번 다시 계산하므로 실제 편집이 아니다.
///
/// 처리: <see cref="Undo.postprocessModifications"/> 에서 <b>RunCommand 의 Collapse 안</b>일 때만,
/// <b>지금 구동 중인 RectTransform 속성</b>에 대한 수정만 버린다. 그 밖의 수정(명령이 실제로 바꾼 값)은 그대로 둔다.
/// 이미 dirty 인 씬을 지우지는 않는다 — 새로 dirty 가 되는 것만 막는다.
///
/// 상세·한계: Docs/tech/mcp-runcommand-scene-dirty.md
/// </summary>
[InitializeOnLoad]
internal static class McpRunCommandDrivenUndoFilter
{
    /// <summary>Collapse 를 부르는 MCP 패키지 메서드(com.unity.ai.assistant). 이름이 바뀌면 필터가 꺼질 뿐이다.</summary>
    internal const string RunCommandEndType = "Unity.AI.Assistant.Agent.Dynamic.Extension.Editor.ExecutionResult";
    internal const string RunCommandEndMethod = "End";

    // RectTransform.drivenProperties 는 internal 이다. 없으면(엔진 변경) 아무것도 버리지 않는다.
    static readonly PropertyInfo DrivenPropertiesInfo =
        typeof(RectTransform).GetProperty("drivenProperties", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    static McpRunCommandDrivenUndoFilter()
    {
        if (DrivenPropertiesInfo == null) return;
        Undo.postprocessModifications += Filter;
    }

    static UndoPropertyModification[] Filter(UndoPropertyModification[] modifications)
    {
        // 싼 검사 먼저 — RectTransform 수정이 없으면 스택을 보지 않는다.
        bool anyRect = false;
        foreach (var m in modifications)
        {
            if (m.currentValue?.target is RectTransform) { anyRect = true; break; }
        }
        if (!anyRect || !IsInsideRunCommandEnd()) return modifications;

        var kept = new List<UndoPropertyModification>(modifications.Length);
        foreach (var m in modifications)
        {
            if (m.currentValue?.target is RectTransform rect &&
                IsDrivenProperty(m.currentValue.propertyPath, (DrivenTransformProperties)DrivenPropertiesInfo.GetValue(rect)))
                continue;
            kept.Add(m);
        }
        return kept.Count == modifications.Length ? modifications : kept.ToArray();
    }

    static bool IsInsideRunCommandEnd()
    {
        foreach (var frame in new StackTrace(false).GetFrames())
        {
            var method = frame.GetMethod();
            if (method != null && method.Name == RunCommandEndMethod && method.DeclaringType?.FullName == RunCommandEndType)
                return true;
        }
        return false;
    }

    /// <summary>
    /// RectTransform 직렬화 경로가 <paramref name="driven"/> 에 포함된 구동 속성인가.
    /// 모르는 경로는 false(버리지 않음).
    /// </summary>
    internal static bool IsDrivenProperty(string propertyPath, DrivenTransformProperties driven)
    {
        if (driven == DrivenTransformProperties.None || string.IsNullOrEmpty(propertyPath)) return false;
        var flags = PropertyFlags(propertyPath);
        return flags != DrivenTransformProperties.None && (driven & flags) == flags;
    }

    internal static DrivenTransformProperties PropertyFlags(string propertyPath)
    {
        switch (propertyPath)
        {
            case "m_AnchoredPosition.x": return DrivenTransformProperties.AnchoredPositionX;
            case "m_AnchoredPosition.y": return DrivenTransformProperties.AnchoredPositionY;
            case "m_LocalPosition.z": return DrivenTransformProperties.AnchoredPositionZ;
            case "m_AnchorMin.x": return DrivenTransformProperties.AnchorMinX;
            case "m_AnchorMin.y": return DrivenTransformProperties.AnchorMinY;
            case "m_AnchorMax.x": return DrivenTransformProperties.AnchorMaxX;
            case "m_AnchorMax.y": return DrivenTransformProperties.AnchorMaxY;
            case "m_SizeDelta.x": return DrivenTransformProperties.SizeDeltaX;
            case "m_SizeDelta.y": return DrivenTransformProperties.SizeDeltaY;
            case "m_Pivot.x": return DrivenTransformProperties.PivotX;
            case "m_Pivot.y": return DrivenTransformProperties.PivotY;
            case "m_LocalRotation.x":
            case "m_LocalRotation.y":
            case "m_LocalRotation.z":
            case "m_LocalRotation.w": return DrivenTransformProperties.Rotation;
            case "m_LocalScale.x": return DrivenTransformProperties.ScaleX;
            case "m_LocalScale.y": return DrivenTransformProperties.ScaleY;
            case "m_LocalScale.z": return DrivenTransformProperties.ScaleZ;
            default: return DrivenTransformProperties.None;
        }
    }
}
