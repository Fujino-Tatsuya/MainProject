using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스 B 승리 연출 설정 에셋(<c>Resources/BossVictory/BossVictoryConfig.asset</c>) 생성·배선(멱등) — PLAN-boss-victory-cinematic.
/// 셰이더·글꼴·타격 VFX 를 에셋이 참조해야 빌드에 들어간다(Hidden 셰이더는 참조가 없으면 빌드에서 빠진다).
/// 이미 있으면 **비어 있는 참조만** 채운다 — 사람이 바꾼 수치·참조는 건드리지 않는다.
/// </summary>
public static class BossVictoryConfigAuthoring
{
    const string AssetPath = "Assets/Resources/BossVictory/BossVictoryConfig.asset";
    const string FontPath = "Assets/50.Art/UI/Fonts/SeoulAlrimTTF-Heavy SDF.asset";
    const string HitVfxPath = "Assets/50.Art/VFX/Common/FX_Hit_Spark.prefab";

    [MenuItem("Tools/BossVictory/설정 에셋 만들기·배선 (멱등)")]
    public static void CreateOrWire()
    {
        var cfg = AssetDatabase.LoadAssetAtPath<BossVictoryConfig>(AssetPath);
        bool created = false;
        if (cfg == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            cfg = ScriptableObject.CreateInstance<BossVictoryConfig>();
            AssetDatabase.CreateAsset(cfg, AssetPath);
            created = true;
        }

        if (cfg.maskShader == null) cfg.maskShader = Shader.Find(BossVictoryRender.MaskShaderName);
        if (cfg.compositeShader == null) cfg.compositeShader = Shader.Find(BossVictoryRender.CompositeShaderName);
        if (cfg.typoFont == null) cfg.typoFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (cfg.hitVfxPrefab == null) cfg.hitVfxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HitVfxPath);

        EditorUtility.SetDirty(cfg);
        AssetDatabase.SaveAssets();

        Debug.Log($"[BossVictory] 설정 에셋 {(created ? "생성" : "배선 확인")} — {AssetPath} · " +
                  $"마스크 셰이더 {(cfg.maskShader != null ? "OK" : "없음")} · 합성 셰이더 {(cfg.compositeShader != null ? "OK" : "없음")} · " +
                  $"글꼴 {(cfg.typoFont != null ? cfg.typoFont.name : "없음(TMP 기본)")} · 타격 VFX {(cfg.hitVfxPrefab != null ? cfg.hitVfxPrefab.name : "없음")}");
    }
}
