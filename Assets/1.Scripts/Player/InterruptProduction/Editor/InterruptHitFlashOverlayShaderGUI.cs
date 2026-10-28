using UnityEditor;
using UnityEngine;

/// <summary>
/// 적중 섬광 오버레이 셰이더(<c>BeaverLobby/UI/InterruptHitFlashOverlay</c>) 재질 인스펙터(할 일 132 단계 5).
/// 단계별 '덮어쓸 텍스처' 칸이 차면 그 단계 키워드(<see cref="InterruptHitFlashRules.OverlayTextureKeywords"/>)를 켜고, 비면 끈다(= 절차 생성).
/// </summary>
public sealed class InterruptHitFlashOverlayShaderGUI : ShaderGUI
{
    private static readonly string[] StageNames = { "1단계 폭발", "2단계 빛 갈라짐", "3·4단계 흰 반전" };

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        base.OnGUI(materialEditor, properties);

        foreach (Object target in materialEditor.targets)
        {
            if (target is Material material && InterruptHitFlashRules.ApplyOverlayKeywords(material))
                EditorUtility.SetDirty(material);
        }

        if (!(materialEditor.target is Material current))
            return;

        var lines = new System.Text.StringBuilder();
        for (int i = 0; i < StageNames.Length; i++)
        {
            bool overriding = current.IsKeywordEnabled(InterruptHitFlashRules.OverlayTextureKeywords[i]);
            lines.Append(StageNames[i]).Append(overriding ? " — 덮어쓸 텍스처 사용" : " — 절차 생성");
            if (i < StageNames.Length - 1)
                lines.Append('\n');
        }

        EditorGUILayout.HelpBox(lines.ToString(), MessageType.None);
    }

    public override void ValidateMaterial(Material material) => InterruptHitFlashRules.ApplyOverlayKeywords(material);
}
