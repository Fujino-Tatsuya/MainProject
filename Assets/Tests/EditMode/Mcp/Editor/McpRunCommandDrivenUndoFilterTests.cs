using NUnit.Framework;
using UnityEngine;

/// <summary>MCP RunCommand 뒤 씬 dirty 막기 — 할 일 190(레이아웃이 구동 중인 RectTransform 속성만 버리고 나머지는 둔다).</summary>
public sealed class McpRunCommandDrivenUndoFilterTests
{
    const DrivenTransformProperties LayoutChild =
        DrivenTransformProperties.AnchoredPosition | DrivenTransformProperties.Anchors | DrivenTransformProperties.SizeDelta;

    [TestCase("m_AnchoredPosition.x")]
    [TestCase("m_AnchoredPosition.y")]
    [TestCase("m_AnchorMin.x")]
    [TestCase("m_AnchorMax.y")]
    [TestCase("m_SizeDelta.x")]
    public void 레이아웃이_구동하는_속성은_버린다(string path)
    {
        Assert.That(McpRunCommandDrivenUndoFilter.IsDrivenProperty(path, LayoutChild), Is.True);
    }

    [TestCase("m_Pivot.x")]
    [TestCase("m_LocalScale.x")]
    [TestCase("m_LocalRotation.w")]
    public void 구동되지_않는_속성은_둔다(string path)
    {
        Assert.That(McpRunCommandDrivenUndoFilter.IsDrivenProperty(path, LayoutChild), Is.False);
    }

    [Test]
    public void 구동이_없으면_아무것도_버리지_않는다()
    {
        Assert.That(McpRunCommandDrivenUndoFilter.IsDrivenProperty("m_SizeDelta.x", DrivenTransformProperties.None), Is.False);
    }

    [TestCase("m_Name")]
    [TestCase("m_IsActive")]
    [TestCase("m_Color.r")]
    [TestCase("")]
    [TestCase(null)]
    public void 모르는_경로는_둔다(string path)
    {
        Assert.That(McpRunCommandDrivenUndoFilter.IsDrivenProperty(path, DrivenTransformProperties.All), Is.False);
    }

    [Test]
    public void 회전은_한_축만_바뀌어도_회전_구동으로_본다()
    {
        Assert.That(McpRunCommandDrivenUndoFilter.IsDrivenProperty("m_LocalRotation.z", DrivenTransformProperties.Rotation), Is.True);
    }
}
