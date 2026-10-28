using UnityEditor;
using UnityEngine;

/// <summary>
/// 빛 모임 셰이더(<c>BeaverLobby/VFX/InterruptLightGather</c>) 재질 인스펙터(할 일 132 단계 4).
/// '덮어쓸 텍스처' 칸이 차면 <see cref="InterruptLightGatherRules.OverrideTextureKeyword"/> 를 켜고, 비면 끈다(= 절차 생성).
/// </summary>
public sealed class InterruptLightGatherShaderGUI : ShaderGUI
{
    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        base.OnGUI(materialEditor, properties);

        foreach (Object target in materialEditor.targets)
        {
            if (target is Material material && InterruptLightGatherRules.ApplyOverrideKeyword(material))
                EditorUtility.SetDirty(material);
        }

        bool overriding = materialEditor.target is Material current &&
                          current.IsKeywordEnabled(InterruptLightGatherRules.OverrideTextureKeyword);
        EditorGUILayout.HelpBox(overriding
            ? "덮어쓸 텍스처 사용 중 — 절차 생성 모양은 쓰지 않는다."
            : "덮어쓸 텍스처 비어 있음 — 절차 생성 모양으로 그린다.", MessageType.None);
    }

    public override void ValidateMaterial(Material material) => InterruptLightGatherRules.ApplyOverrideKeyword(material);
}
