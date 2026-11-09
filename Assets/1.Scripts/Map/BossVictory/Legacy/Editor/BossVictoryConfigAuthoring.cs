using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스 B 승리 연출 설정 에셋(<c>Resources/BossVictory/BossVictoryConfig.asset</c>) 생성·배선(멱등) — PLAN-boss-victory-cinematic.
/// 셰이더를 에셋이 참조해야 빌드에 들어간다(Hidden 셰이더는 참조가 없으면 빌드에서 빠진다).
/// 이미 있으면 **비어 있는 셰이더 참조만** 채운다 — 사람이 바꾼 수치·참조는 건드리지 않는다.
/// 할 일 200: 타이포 글꼴 칸 삭제, 승리 타격 VFX 는 일부러 비워 둔다(자동으로 채우지 않는다).
/// </summary>
public static class BossVictoryConfigAuthoring
{
    const string AssetPath = "Assets/Resources/BossVictory/BossVictoryConfig.asset";

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

        EditorUtility.SetDirty(cfg);
        AssetDatabase.SaveAssets();

        Debug.Log($"[BossVictory] 설정 에셋 {(created ? "생성" : "배선 확인")} — {AssetPath} · " +
                  $"마스크 셰이더 {(cfg.maskShader != null ? "OK" : "없음")} · 합성 셰이더 {(cfg.compositeShader != null ? "OK" : "없음")} · " +
                  $"타격 VFX {(cfg.hitVfxPrefab != null ? cfg.hitVfxPrefab.name : "없음")}");
    }
}
