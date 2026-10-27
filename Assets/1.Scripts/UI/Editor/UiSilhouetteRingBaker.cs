using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 아트 PNG 의 알파 실루엣을 두르는 흰 링 PNG 를 굽는다(할 일 109 후속 — SELECT 셰브론 hover 아웃라인).
/// 링 = 실루엣에서 <c>gap</c> 만큼 띄운 곳부터 <c>thickness</c> 두께(가장자리 1px 안티에일리어싱). 원본에 알파가 있는 픽셀에는 절대 칠하지 않는다.
/// 원본은 파일 바이트를 직접 읽는다(SVN 아트는 Read/Write 꺼짐 — .meta 를 고치지 않는다). 런타임 생성 없음 — 결과는 git 추적 PNG + Sprite 임포트.
/// 같은 입력·인자 → 같은 픽셀(재현 가능).
/// </summary>
public static class UiSilhouetteRingBaker
{
    /// <summary>원본 픽셀이 실루엣 안으로 치는 알파(이상).</summary>
    const byte InsideAlpha = 128;

    /// <summary>
    /// 굽고 저장·임포트한다. <paramref name="displaySize"/> = 원본 Sprite 가 화면(캔버스 기준 해상도)에 보이는 크기 —
    /// 두께·간격은 그 화면 px 로 받고 텍스처 px 로 바꿔 굽는다. 결과 Sprite 의 PPU 는 원본 표시 비율을 그대로 따르므로
    /// 링 Image rect = 원본 rect 를 사방 <paramref name="marginDisplay"/> 만큼 늘린 것.
    /// </summary>
    public static Sprite Bake(string sourcePngPath, string outputPngPath, Vector2 displaySize, float thickness, float gap,
        out float marginDisplay)
    {
        marginDisplay = 0f;
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!source.LoadImage(File.ReadAllBytes(sourcePngPath)))
            {
                Debug.LogError($"[UiSilhouetteRingBaker] PNG 를 읽지 못했다: {sourcePngPath}");
                return null;
            }

            float scale = source.width / displaySize.x; // 텍스처 px / 화면 px
            if (!Mathf.Approximately(scale, source.height / displaySize.y))
                Debug.LogWarning($"[UiSilhouetteRingBaker] 가로·세로 표시 비율이 다르다({source.width}x{source.height} → {displaySize}) — 가로 비율로 굽는다.");

            int margin = MarginPixels(thickness * scale, gap * scale);
            Color32[] ring = BakePixels(source.GetPixels32(), source.width, source.height, thickness * scale, gap * scale, margin);
            int width = source.width + margin * 2, height = source.height + margin * 2;

            var output = new Texture2D(width, height, TextureFormat.RGBA32, false);
            output.SetPixels32(ring);
            output.Apply();
            File.WriteAllBytes(outputPngPath, output.EncodeToPNG());
            Object.DestroyImmediate(output);
            marginDisplay = margin / scale;

            AssetDatabase.ImportAsset(outputPngPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(outputPngPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f * scale; // 캔버스 referencePixelsPerUnit 100 — 원본과 같은 표시 비율
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(outputPngPath);
        }
        finally
        {
            Object.DestroyImmediate(source);
        }
    }

    /// <summary>링이 원본 rect 밖으로 나가는 여백(텍스처 px) — 바깥 가장자리 + 안티에일리어싱 1px.</summary>
    public static int MarginPixels(float thicknessPx, float gapPx) => Mathf.CeilToInt(gapPx + thicknessPx) + 1;

    /// <summary>
    /// 링 픽셀(흰색, 알파 = 링 덮임). 출력 = 원본을 사방 <paramref name="margin"/> px 늘린 크기, 원본은 (margin, margin) 에 놓인다.
    /// 거리 = 실루엣(알파 ≥ 128) 픽셀 중심까지 최단 거리 − 0.5(픽셀 반 폭 ≈ 실루엣 가장자리).
    /// </summary>
    public static Color32[] BakePixels(Color32[] source, int width, int height, float thicknessPx, float gapPx, int margin)
    {
        int outWidth = width + margin * 2, outHeight = height + margin * 2;
        var result = new Color32[outWidth * outHeight];

        float outer = gapPx + thicknessPx;
        int radius = Mathf.CeilToInt(outer + 1f);
        List<(int dx, int dy, float d)> offsets = Offsets(radius);

        for (int y = 0; y < outHeight; y++)
        for (int x = 0; x < outWidth; x++)
        {
            int sx = x - margin, sy = y - margin;
            byte sourceAlpha = Inside(sx, sy, width, height) ? source[sy * width + sx].a : (byte)0;

            float distance = float.PositiveInfinity;
            foreach ((int dx, int dy, float d) in offsets) // 거리순 — 처음 만난 실루엣 픽셀이 최단
            {
                int qx = sx + dx, qy = sy + dy;
                if (Inside(qx, qy, width, height) && source[qy * width + qx].a >= InsideAlpha)
                {
                    distance = d - 0.5f;
                    break;
                }
            }

            float coverage = 0f;
            if (!float.IsPositiveInfinity(distance))
                coverage = Mathf.Min(Mathf.Clamp01(distance - gapPx + 0.5f), Mathf.Clamp01(outer - distance + 0.5f));
            if (sourceAlpha > 0)
                coverage = 0f; // 원본 픽셀(안티에일리어싱 가장자리 포함)은 가리지 않는다

            result[y * outWidth + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(coverage * 255f));
        }

        return result;
    }

    static bool Inside(int x, int y, int width, int height) => x >= 0 && y >= 0 && x < width && y < height;

    static List<(int dx, int dy, float d)> Offsets(int radius)
    {
        var offsets = new List<(int dx, int dy, float d)>();
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            if (d <= radius)
                offsets.Add((dx, dy, d));
        }
        offsets.Sort((a, b) => a.d.CompareTo(b.d));
        return offsets;
    }
}
