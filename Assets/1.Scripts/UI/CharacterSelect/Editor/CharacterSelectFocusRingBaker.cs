using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 가운데 카드 금색 테두리(<c>Ring_Focus</c>)를 원본보다 얇게(할 일 162 — 두께 0.7배) 구운 PNG.
/// 원본 <c>card_selected_ring.png</c>(SVN)는 테두리가 아니라 **채워진 카드 실루엣**이고, 카드 뒤에 조금 크게(Ring_Focus rect 694×551) 깔려
/// 카드 검은 테두리(<c>card_selected_gap.png</c> 636×501, 가운데 정렬) 밖으로 나온 띠만 보인다.
/// 굽기 = Ring_Focus rect 크기 1:1 픽셀로, 원본 링 그림을 가운데 기준 가로·세로 배율만큼 **줄여 그대로 다시 그린다**(모양·모서리·안티에일리어싱 = 원본).
/// 배율 = 가운데 가로줄·세로줄에서 카드 밖으로 보이는 띠가 <see cref="ThicknessScale"/> 배가 되는 값(원본 기준 가로 ≈0.968·세로 ≈0.972).
/// 원본 띠 두께가 곳마다 다른 것(옆 ≈21·위아래 ≈25·뾰족 모서리 더 두꺼움)은 링을 카드보다 크게 늘려 생긴 것이라 비율이 그대로 따라간다.
/// 원본은 파일 바이트를 직접 읽는다(SVN 아트 Read/Write 꺼짐 — .meta 무수정). 런타임 생성 없음. 같은 입력 → 같은 픽셀.
/// 사용법 = Docs/tech/character-select-panel.md.
/// </summary>
public static class CharacterSelectFocusRingBaker
{
    const string Tag = "[CharacterSelectFocusRing]";
    public const string SourceRingPath = "Assets/50.Art/UI/char_select/card_selected_ring.png";
    public const string CardGapPath = "Assets/50.Art/UI/char_select/card_selected_gap.png";
    public const string OutputPath = "Assets/2.Prefabs/UI/Textures/CharacterSelectFocusRing.png";
    public const float ThicknessScale = 0.7f;
    const string RingName = "Ring_Focus";
    const byte InsideAlpha = 128;

    [MenuItem("Tools/UI/Authoring/Character Select Focus Ring (0.7배 굽기·연결)")]
    public static void BakeAndApply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError($"{Tag} Play 중에는 실행하지 않는다 — 먼저 정지할 것.");
            return;
        }

        // 기준 크기 = 카드 프리팹 Ring_Focus rect(지금 보이는 크기 그대로 1:1 로 굽는다)
        GameObject card = PrefabUtility.LoadPrefabContents(CharacterSelectPanelAuthoring.CardPath);
        try
        {
            var ring = card.transform.Find(RingName) as RectTransform;
            if (ring == null)
            {
                Debug.LogError($"{Tag} {CharacterSelectPanelAuthoring.CardPath} 에 {RingName} 이 없다.");
                return;
            }

            Sprite sprite = Bake(Vector2Int.RoundToInt(ring.rect.size));
            if (sprite == null)
                return;
            ring.GetComponent<Image>().sprite = sprite;
            PrefabUtility.SaveAsPrefabAsset(card, CharacterSelectPanelAuthoring.CardPath, out bool cardSaved);
            Debug.Log($"{Tag} 카드 프리팹 {RingName} 연결 {(cardSaved ? "성공" : "실패")}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(card);
        }

        // 패널의 가운데 카드 원본(Card_Template)이 스프라이트를 덮어쓰고 있으면 새 링으로 맞춘다
        GameObject panel = PrefabUtility.LoadPrefabContents(CharacterSelectPanelAuthoring.PanelPath);
        try
        {
            Transform ring = panel.transform.Find($"Content/Cards/{CharacterSelectPanelAuthoring.CardTemplateName}/{RingName}");
            if (ring == null)
            {
                Debug.LogWarning($"{Tag} 패널에 {CharacterSelectPanelAuthoring.CardTemplateName}/{RingName} 이 없다 — 카드 프리팹만 바꿨다.");
                return;
            }

            ring.GetComponent<Image>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(OutputPath);
            PrefabUtility.SaveAsPrefabAsset(panel, CharacterSelectPanelAuthoring.PanelPath, out bool panelSaved);
            Debug.Log($"{Tag} 패널 {CharacterSelectPanelAuthoring.CardTemplateName}/{RingName} 연결 {(panelSaved ? "성공" : "실패")}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(panel);
        }
    }

    /// <summary><see cref="OutputPath"/> 에 <paramref name="size"/>(= Ring_Focus rect, 화면 px) 크기로 굽고 Sprite(PPU 100) 로 임포트한다.</summary>
    public static Sprite Bake(Vector2Int size)
    {
        Texture2D ring = LoadPng(SourceRingPath), gap = LoadPng(CardGapPath);
        if (ring == null || gap == null)
            return null;

        try
        {
            Color32[] pixels = BakePixels(ring.GetPixels32(), ring.width, ring.height, gap.GetPixels32(), gap.width, gap.height,
                size.x, size.y, ThicknessScale);
            var output = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false);
            output.SetPixels32(pixels);
            output.Apply();
            File.WriteAllBytes(OutputPath, output.EncodeToPNG());
            Object.DestroyImmediate(output);

            AssetDatabase.ImportAsset(OutputPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(OutputPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f; // 캔버스 referencePixelsPerUnit 100 — rect = PNG 픽셀
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();

            Color32[] gapPixels = gap.GetPixels32();
            int row = size.y / 2;
            Debug.Log($"{Tag} 굽기: {OutputPath} {size.x}x{size.y} — 띠 두께 {ThicknessScale}배(가운데 줄 왼쪽 띠 " +
                      $"{BandWidth(OriginalPixels(ring, gap, size), size.x, size.y, gapPixels, gap.width, gap.height, row)}px → " +
                      $"{BandWidth(pixels, size.x, size.y, gapPixels, gap.width, gap.height, row)}px)");
            return AssetDatabase.LoadAssetAtPath<Sprite>(OutputPath);
        }
        finally
        {
            Object.DestroyImmediate(ring);
            Object.DestroyImmediate(gap);
        }
    }

    /// <summary>원본 링을 같은 크기로 늘린 것(배율 1) — 비교·테스트용.</summary>
    public static Color32[] OriginalPixels(Texture2D ring, Texture2D gap, Vector2Int size) =>
        BakePixels(ring.GetPixels32(), ring.width, ring.height, gap.GetPixels32(), gap.width, gap.height, size.x, size.y, 1f);

    /// <summary>
    /// 흰 픽셀(알파 = 원본 링 알파), 크기 <paramref name="width"/>×<paramref name="height"/>. 원본 링을 이 크기로 늘린 뒤 가운데 기준
    /// <see cref="ShrinkFactors"/> 만큼 줄여 이중선형으로 읽는다. <paramref name="scale"/> = 1 이면 원본을 늘린 것 그대로.
    /// </summary>
    public static Color32[] BakePixels(Color32[] ring, int ringWidth, int ringHeight, Color32[] gap, int gapWidth, int gapHeight,
        int width, int height, float scale)
    {
        Vector2 shrink = ShrinkFactors(ring, ringWidth, ringHeight, gap, gapWidth, gapHeight, width, height, scale);
        float cx = width * 0.5f, cy = height * 0.5f;
        var result = new Color32[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            // 출력 픽셀 중심 → 줄이기 전 자리 → 원본 텍스처 좌표(텍셀 중심 기준)
            float u = (cx + (x + 0.5f - cx) / shrink.x) * ringWidth / width - 0.5f;
            float v = (cy + (y + 0.5f - cy) / shrink.y) * ringHeight / height - 0.5f;
            result[y * width + x] = new Color32(255, 255, 255, SampleAlpha(ring, ringWidth, ringHeight, u, v));
        }
        return result;
    }

    /// <summary>
    /// 가로·세로 줄이는 배율 — 가운데 가로줄(세로줄)에서 원본 링 폭 R·카드 폭 G 를 재어, 띠 (R − G)/2 가 <paramref name="scale"/> 배가 되게
    /// (G + scale·(R − G)) / R. 링·카드 모두 가운데 정렬이라 줄여도 띠는 양쪽 같은 폭으로 줄어든다.
    /// </summary>
    public static Vector2 ShrinkFactors(Color32[] ring, int ringWidth, int ringHeight, Color32[] gap, int gapWidth, int gapHeight,
        int width, int height, float scale)
    {
        int midX = width / 2, midY = height / 2;
        float ringW = Span(i => RingInside(ring, ringWidth, ringHeight, width, height, i, midY), width);
        float ringH = Span(i => RingInside(ring, ringWidth, ringHeight, width, height, midX, i), height);
        float gapW = Span(i => GapInside(gap, gapWidth, gapHeight, width, height, i, midY), width);
        float gapH = Span(i => GapInside(gap, gapWidth, gapHeight, width, height, midX, i), height);
        return new Vector2(ringW > 0f ? (gapW + scale * (ringW - gapW)) / ringW : 1f,
                           ringH > 0f ? (gapH + scale * (ringH - gapH)) / ringH : 1f);
    }

    static bool RingInside(Color32[] ring, int ringWidth, int ringHeight, int width, int height, int x, int y)
    {
        int rx = Mathf.Min(ringWidth - 1, (int)((x + 0.5f) * ringWidth / width));
        int ry = Mathf.Min(ringHeight - 1, (int)((y + 0.5f) * ringHeight / height));
        return ring[ry * ringWidth + rx].a >= InsideAlpha;
    }

    static bool GapInside(Color32[] gap, int gapWidth, int gapHeight, int width, int height, int x, int y)
    {
        int gx = x - (width - gapWidth) / 2, gy = y - (height - gapHeight) / 2;
        return gx >= 0 && gy >= 0 && gx < gapWidth && gy < gapHeight && gap[gy * gapWidth + gx].a >= InsideAlpha;
    }

    /// <summary>안쪽인 첫 칸부터 마지막 칸까지 폭(없으면 0).</summary>
    static float Span(System.Func<int, bool> inside, int count)
    {
        int first = -1, last = -1;
        for (int i = 0; i < count; i++)
        {
            if (!inside(i))
                continue;
            if (first < 0)
                first = i;
            last = i;
        }
        return first < 0 ? 0f : last - first + 1;
    }

    /// <summary>이중선형 알파(텍스처 밖 = 0).</summary>
    static byte SampleAlpha(Color32[] pixels, int width, int height, float u, float v)
    {
        int x0 = Mathf.FloorToInt(u), y0 = Mathf.FloorToInt(v);
        float fx = u - x0, fy = v - y0;
        float a00 = Alpha(pixels, width, height, x0, y0), a10 = Alpha(pixels, width, height, x0 + 1, y0);
        float a01 = Alpha(pixels, width, height, x0, y0 + 1), a11 = Alpha(pixels, width, height, x0 + 1, y0 + 1);
        float a = Mathf.Lerp(Mathf.Lerp(a00, a10, fx), Mathf.Lerp(a01, a11, fx), fy);
        return (byte)Mathf.RoundToInt(a);
    }

    static float Alpha(Color32[] pixels, int width, int height, int x, int y) =>
        x < 0 || y < 0 || x >= width || y >= height ? 0f : pixels[y * width + x].a;

    /// <summary>
    /// 가로줄 <paramref name="row"/> 에서 카드 왼쪽 테두리 밖으로 보이는 띠 폭(px, 알파 ≥ 128) — 두께 비교용.
    /// 카드 테두리(gap)는 <see cref="BakePixels"/> 와 같이 가운데 정렬.
    /// </summary>
    public static int BandWidth(Color32[] pixels, int width, int height, Color32[] gap, int gapWidth, int gapHeight, int row)
    {
        int gapX = (width - gapWidth) / 2, gy = row - (height - gapHeight) / 2;
        int band = 0;
        for (int x = 0; x < width / 2; x++)
        {
            int gx = x - gapX;
            bool card = gy >= 0 && gy < gapHeight && gx >= 0 && gx < gapWidth && gap[gy * gapWidth + gx].a >= InsideAlpha;
            if (!card && pixels[row * width + x].a >= InsideAlpha)
                band++;
        }
        return band;
    }

    public static Texture2D LoadPng(string assetPath)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (texture.LoadImage(File.ReadAllBytes(assetPath)))
            return texture;

        Debug.LogError($"{Tag} PNG 를 읽지 못했다: {assetPath}");
        Object.DestroyImmediate(texture);
        return null;
    }
}
