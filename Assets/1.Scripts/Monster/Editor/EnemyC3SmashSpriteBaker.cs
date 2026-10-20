using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 57 S3 — 스매시 09 sprite 를 에디터에서 미리 굽는다(런타임 Sprite.Create·Bake 0).
/// CPU <c>EnemyC3BakeReference.Circle("09")</c> 를 PNG 로 저장하고, 임포트 PPU 를 예전 런타임 식
/// <c>tex.width / (원본 sprite 폭 × OuterScale("09"))</c> 과 같게 맞춘다 → 화면 크기 그대로.
/// 프리셋(EnemyC3Selected.json)의 09 값이 바뀌면 이 메뉴를 다시 돌리고 PNG·meta 를 커밋한다
/// (EnemyC3SmashSpriteTests 가 어긋남을 잡는다).
/// </summary>
public static class EnemyC3SmashSpriteBaker
{
    public const string AssetPath = "Assets/Resources/" + EnemyC3Visuals.SmashSpritePath + ".png";
    // 스매시 예고 VFX — 이 SpriteRenderer 의 원본 sprite 크기가 기준이다(읽기만 한다).
    public const string SourcePrefabPath = "Assets/50.Art/VFX/Common/Monsters/FX_Mob_Smah_Warning.prefab";

    /// <summary>원본 sprite 폭(월드 단위) — 예전 ApplySmash 가 쓰던 old.bounds.size.x.</summary>
    public static float SourceSpriteWidth()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
        var sr = prefab != null ? prefab.GetComponentInChildren<SpriteRenderer>(true) : null;
        if (sr == null || sr.sprite == null) throw new IOException("Smash source sprite missing: " + SourcePrefabPath);
        return sr.sprite.bounds.size.x;
    }

    public static float PixelsPerUnit(int width) => width / (SourceSpriteWidth() * EnemyC3Visuals.OuterScale("09"));

    [MenuItem("Tools/Enemy C3/Bake smash 09 sprite")]
    public static void Bake()
    {
        var tex = EnemyC3BakeReference.Circle("09");
        File.WriteAllBytes(Path.GetFullPath(AssetPath), tex.EncodeToPNG());
        AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
        var ti = (TextureImporter)AssetImporter.GetAtPath(AssetPath);
        // Bake 텍스처와 같은 값이 되게: sRGB·무압축·밉 없음·Bilinear·Clamp, 알파 번짐 없음.
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.sRGBTexture = true;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.alphaIsTransparency = false;
        ti.mipmapEnabled = false;
        ti.isReadable = false;
        ti.filterMode = FilterMode.Bilinear;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.maxTextureSize = 2048;
        var settings = new TextureImporterSettings();
        ti.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        settings.spritePixelsPerUnit = PixelsPerUnit(tex.width);
        settings.spriteExtrude = 0;
        ti.SetTextureSettings(settings);
        ti.SaveAndReimport();
        Debug.Log($"[Enemy C3] Smash 09 sprite baked → {AssetPath} ({tex.width}px, PPU {settings.spritePixelsPerUnit:F4})");
    }
}
