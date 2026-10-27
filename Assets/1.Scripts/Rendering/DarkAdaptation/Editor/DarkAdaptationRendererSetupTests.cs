using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 암순응 렌더러 기능이 PC·Mobile 렌더러에 들어 있고, 셰이더 참조가 비어 있지 않으며 목록 맨 끝인지(할 일 87 C3).
/// </summary>
public sealed class DarkAdaptationRendererSetupTests
{
    [TestCase("Assets/99.Settings/PC_Renderer.asset")]
    [TestCase("Assets/99.Settings/Mobile_Renderer.asset")]
    public void Renderer_HasFeatureWithShader(string path)
    {
        var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
        Assert.IsNotNull(data, path);

        int count = data.rendererFeatures.Count;
        Assert.Greater(count, 0, "기능 없음");

        var feature = data.rendererFeatures[count - 1] as DarkAdaptationFeature;
        Assert.IsNotNull(feature, "DarkAdaptationFeature 가 목록 맨 끝에 없다");

        var shader = new SerializedObject(feature).FindProperty("_shader").objectReferenceValue as Shader;
        Assert.IsNotNull(shader, "셰이더 참조가 비었다");
        Assert.IsTrue(shader.isSupported, "셰이더가 컴파일되지 않았다");
    }
}
