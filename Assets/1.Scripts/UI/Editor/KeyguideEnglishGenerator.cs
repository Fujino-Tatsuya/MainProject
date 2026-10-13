using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 로컬라이징 D-L4(언어별 이미지) — 원본 키가이드 <c>keyguide.png</c> 에서 한글 라벨만 지우고 영어 라벨을 얹어
/// <c>keyguide_en.png</c> 를 만든다. 원본 PNG·.meta 는 읽기만 한다(isReadable 그대로, 바이트를 직접 읽음).
///
/// 라벨 찾기: 투명 행으로 나뉜 띠마다 열 점유를 보고, <see cref="GroupGap"/> 이상 벌어진 덩어리 중 가장 오른쪽을 라벨로 본다.
/// 렌더: 임시 PreviewScene 의 TMP + 오프스크린 카메라로 채움 마스크만 <see cref="Supersample"/> 배로 그리고,
/// 외곽선은 그 마스크를 원판 팽창해 만든다(원본 외곽선이 1px 단색이라 TMP Outline 보다 두께를 맞추기 쉽다).
/// 산출물은 50.Art(SVN 영역)에 파일로만 남긴다 — git 에 넣지 않는다.
/// </summary>
public static class KeyguideEnglishGenerator
{
    const string Tag = "[KeyguideEN]";
    const string SourcePath = "Assets/50.Art/UI/HUD/keyguide.png";
    const string OutputPath = "Assets/50.Art/UI/HUD/keyguide_en.png";
    const string FontPath = "Assets/Resources/NotoSansKR-VariableFont_wght SDF.asset";
    const string PreviewDir = "Temp/KeyguidePreview";

    // 원본 위에서부터: 일반공격·간파스킬·스킬1·스킬2·궁극기·이동·대쉬 (용어집: 간파 = Interrupt)
    static readonly string[] Labels = { "Attack", "Interrupt", "Skill 1", "Skill 2", "Ultimate", "Move", "Dash" };

    const int Supersample = 4;
    const int GroupGap = 8;              // 이보다 좁은 투명 열 간격은 한 덩어리(글자 사이)로 본다
    const float CapHeightRatio = 0.72f;  // 영어 대문자 높이 = 한글 라벨 박스 안쪽 높이 × 이 값
    const float FaceDilate = 0.45f;      // NotoSansKR SDF 가 Thin 이라 SDF 팽창으로 굵기를 맞춘다
    const float FillBold = 0.35f;        // 출력 px — SDF 패딩(3)이 작아 FaceDilate 만으로는 한글 라벨 굵기가 안 나온다
    const float OutlineRadius = 1.4f;    // 출력 px — 원본 외곽선(1px 불투명 + 안티에일리어싱)과 진하기를 맞춘 값
    const int RightMargin = 1;

    public struct LabelBox
    {
        public int X, Y, W, H; // Y = 텍스처 좌표(아래가 0)의 박스 아래 행
        public Color32 Fill, Outline;
    }

    [MenuItem("Tools/Localization/키가이드 영어판 생성")]
    static void Generate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError($"{Tag} Play 모드에서는 실행하지 않는다.");
            return;
        }

        var src = LoadPng(SourcePath);
        int w = src.width, h = src.height;
        var original = src.GetPixels32();
        Object.DestroyImmediate(src);

        var boxes = FindLabelBoxes(original, w, h, out string error);
        if (boxes == null || boxes.Count != Labels.Length)
        {
            Debug.LogError($"{Tag} 라벨 박스 {Labels.Length}개를 찾지 못해 멈춘다 — {error ?? $"찾은 개수 {boxes?.Count ?? 0}"}");
            return;
        }
        LogBoxes(boxes, h);

        var px = (Color32[])original.Clone();
        foreach (var b in boxes)
            EraseBox(px, w, h, b);

        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"{Tag} 폰트를 못 찾았다: {FontPath}");
            return;
        }

        var fill = RenderFillMask(font, boxes, w, h, out float[] fontSizes, out float[] shrink);
        Compose(px, fill, w, h, boxes);

        var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        outTex.SetPixels32(px);
        outTex.Apply();
        File.WriteAllBytes(OutputPath, outTex.EncodeToPNG());
        Object.DestroyImmediate(outTex);

        AssetDatabase.ImportAsset(OutputPath, ImportAssetOptions.ForceUpdate);
        CopyImporterSettings(SourcePath, OutputPath);

        string previewPath = WriteCompare(original, px, w, h);

        var sb = new StringBuilder($"{Tag} 완료 → {OutputPath} / 미리보기 {previewPath}\n폰트 {font.name} Bold, FaceDilate {FaceDilate}, 굵기 보정 {FillBold}px, 외곽선 {OutlineRadius}px\n");
        for (int i = 0; i < Labels.Length; i++)
            sb.AppendLine($"  {Labels[i],-10} fontSize {fontSizes[i]:0.0}{(shrink[i] < 1f ? $"  (폭 초과 → {shrink[i] * 100f:0}% 로 축소)" : "")}");
        Debug.Log(sb.ToString());
    }

    /// <summary>라벨 박스를 위에서부터 순서대로 돌려준다. 띠에 아이콘·라벨 두 덩어리가 없으면 null.</summary>
    public static List<LabelBox> FindLabelBoxes(Color32[] px, int w, int h, out string error)
    {
        error = null;
        var bands = new List<Vector2Int>(); // (아래 행, 위 행)
        int start = -1;
        for (int y = 0; y <= h; y++)
        {
            bool has = y < h && RowHasInk(px, w, y, 0, w - 1);
            if (has && start < 0) start = y;
            else if (!has && start >= 0) { bands.Add(new Vector2Int(start, y - 1)); start = -1; }
        }
        bands.Reverse(); // 위에서부터

        var boxes = new List<LabelBox>();
        foreach (var band in bands)
        {
            var groups = new List<Vector2Int>();
            int gs = -1, lastInk = -1;
            for (int x = 0; x < w; x++)
            {
                bool ink = false;
                for (int y = band.x; y <= band.y && !ink; y++) ink = px[y * w + x].a > 0;
                if (!ink) continue;
                if (gs < 0) gs = x;
                else if (x - lastInk - 1 >= GroupGap) { groups.Add(new Vector2Int(gs, lastInk)); gs = x; }
                lastInk = x;
            }
            if (gs >= 0) groups.Add(new Vector2Int(gs, lastInk));
            if (groups.Count < 2)
            {
                error = $"행 {h - 1 - band.y}~{h - 1 - band.x}(위 기준) 띠에 덩어리가 {groups.Count}개뿐";
                return null;
            }

            var g = groups[groups.Count - 1];
            int y0 = -1, y1 = -1;
            for (int y = band.x; y <= band.y; y++)
            {
                if (!RowHasInk(px, w, y, g.x, g.y)) continue;
                if (y0 < 0) y0 = y;
                y1 = y;
            }
            var box = new LabelBox { X = g.x, Y = y0, W = g.y - g.x + 1, H = y1 - y0 + 1 };
            SampleColors(px, w, ref box);
            boxes.Add(box);
        }
        return boxes;
    }

    static bool RowHasInk(Color32[] px, int w, int y, int x0, int x1)
    {
        for (int x = x0; x <= x1; x++)
            if (px[y * w + x].a > 0) return true;
        return false;
    }

    // 불투명 픽셀 중 밝은 쪽 평균 = 채움, 어두운 쪽 평균 = 외곽선
    static void SampleColors(Color32[] px, int w, ref LabelBox b)
    {
        Vector4 fill = Vector4.zero, line = Vector4.zero;
        int nf = 0, nl = 0;
        for (int y = b.Y; y < b.Y + b.H; y++)
        for (int x = b.X; x < b.X + b.W; x++)
        {
            var c = px[y * w + x];
            if (c.a < 250) continue;
            int lum = (c.r + c.g + c.b) / 3;
            if (lum >= 190) { fill += new Vector4(c.r, c.g, c.b); nf++; }
            else if (lum < 70) { line += new Vector4(c.r, c.g, c.b); nl++; }
        }
        if (nf > 0) fill /= nf; else fill = new Vector4(240, 240, 230);
        if (nl > 0) line /= nl; else line = new Vector4(30, 30, 30);
        b.Fill = new Color32((byte)fill.x, (byte)fill.y, (byte)fill.z, 255);
        b.Outline = new Color32((byte)line.x, (byte)line.y, (byte)line.z, 255);
    }

    static void LogBoxes(List<LabelBox> boxes, int h)
    {
        var sb = new StringBuilder($"{Tag} 라벨 박스 (y = 위 기준)\n  #  label      x    y    w    h   fill         outline\n");
        for (int i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            sb.AppendLine($"  {i + 1}  {Labels[i],-9} {b.X,4} {h - (b.Y + b.H),4} {b.W,4} {b.H,4}   #{ColorUtility.ToHtmlStringRGB(b.Fill)}      #{ColorUtility.ToHtmlStringRGB(b.Outline)}");
        }
        Debug.Log(sb.ToString());
    }

    // 박스 + 여백 1px 을 지운다. 둘레가 투명이면 alpha 0, 아니면 둘레 평균색으로 채운다.
    static void EraseBox(Color32[] px, int w, int h, LabelBox b)
    {
        int x0 = Mathf.Max(0, b.X - 1), x1 = Mathf.Min(w - 1, b.X + b.W);
        int y0 = Mathf.Max(0, b.Y - 1), y1 = Mathf.Min(h - 1, b.Y + b.H);

        Vector4 sum = Vector4.zero;
        int n = 0;
        for (int y = y0 - 1; y <= y1 + 1; y++)
        for (int x = x0 - 1; x <= x1 + 1; x++)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) continue;
            if (x >= x0 && x <= x1 && y >= y0 && y <= y1) continue;
            var c = px[y * w + x];
            sum += new Vector4(c.r, c.g, c.b, c.a);
            n++;
        }
        var bg = new Color32(0, 0, 0, 0);
        if (n > 0 && sum.w / n >= 8f)
            bg = new Color32((byte)(sum.x / n), (byte)(sum.y / n), (byte)(sum.z / n), (byte)(sum.w / n));

        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
            px[y * w + x] = bg;
    }

    /// <summary>모든 라벨의 채움 마스크(0~1)를 (w·S)×(h·S) 해상도로 그린다. 텍스처 좌표처럼 아래 행이 0.</summary>
    static float[] RenderFillMask(TMP_FontAsset font, List<LabelBox> boxes, int w, int h, out float[] fontSizes, out float[] shrink)
    {
        int S = Supersample, sw = w * S, sh = h * S;
        fontSizes = new float[boxes.Count];
        shrink = new float[boxes.Count];

        var scene = EditorSceneManager.NewPreviewScene();
        var temp = new List<Object>();
        var rt = new RenderTexture(sw, sh, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
        var mat = new Material(font.material) { hideFlags = HideFlags.HideAndDontSave };
        temp.Add(mat);
        try
        {
            mat.SetColor(ShaderUtilities.ID_FaceColor, Color.white);
            mat.SetFloat(ShaderUtilities.ID_FaceDilate, FaceDilate);
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0f);
            mat.SetFloat(ShaderUtilities.ID_OutlineSoftness, 0f);
            mat.DisableKeyword(ShaderUtilities.Keyword_Outline);
            mat.DisableKeyword(ShaderUtilities.Keyword_Underlay);
            mat.DisableKeyword(ShaderUtilities.Keyword_Glow);
            ShaderUtilities.UpdateShaderRatios(mat);

            var camGo = NewHidden("KeyguideCam", scene, temp);
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.scene = scene;
            cam.orthographic = true;
            cam.orthographicSize = h * 0.5f; // 1 월드 단위 = 출력 1px
            cam.aspect = w / (float)h;
            cam.transform.position = new Vector3(w * 0.5f, h * 0.5f, -10f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black; // 흰 글자 → R 채널 = 덮인 비율
            cam.targetTexture = rt;

            var probe = NewText("Probe", scene, temp, font, mat);
            probe.fontSize = 100f;
            probe.text = "H";
            probe.ForceMeshUpdate();
            // textBounds 높이는 줄 높이(ascender~descender)라 대문자 높이는 글리프 메트릭에서 잰다
            var hInfo = probe.textInfo.characterInfo[0];
            float capPer100 = hInfo.topLeft.y - hInfo.baseLine;
            probe.gameObject.SetActive(false);

            for (int i = 0; i < boxes.Count; i++)
            {
                var b = boxes[i];
                float innerH = b.H - 2f; // 위아래 외곽선 1px 제외
                float size = 100f * (innerH * CapHeightRatio) / capPer100;

                var tmp = NewText(Labels[i], scene, temp, font, mat);
                tmp.fontSize = size;
                tmp.text = Labels[i];
                tmp.ForceMeshUpdate();

                float left = b.X + OutlineRadius + FillBold;
                float avail = w - RightMargin - OutlineRadius - FillBold - left;
                InkX(tmp, out float minX, out float maxX);
                shrink[i] = 1f;
                if (maxX - minX > avail)
                {
                    shrink[i] = avail / (maxX - minX);
                    size *= shrink[i];
                    tmp.fontSize = size;
                    tmp.ForceMeshUpdate();
                    InkX(tmp, out minX, out maxX);
                }
                fontSizes[i] = size;

                // 대문자 높이 구간을 박스 세로 중앙에 둔다(기준선이 라벨끼리 일정 — 'p' 꼬리는 아래로 나간다)
                float capH = capPer100 * size / 100f;
                float baseline = tmp.textInfo.lineInfo[0].baseline;
                float targetBaseline = b.Y + b.H * 0.5f - capH * 0.5f;
                tmp.transform.position = new Vector3(left - minX, targetBaseline - baseline, 0f);
            }

            cam.Render();

            var read = new Texture2D(sw, sh, TextureFormat.RGBA32, false, true);
            temp.Add(read);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, sw, sh), 0, 0);
            read.Apply();
            RenderTexture.active = prev;

            var raw = read.GetPixels32();
            var mask = new float[raw.Length];
            for (int i = 0; i < raw.Length; i++) mask[i] = raw[i].r / 255f;
            return mask;
        }
        finally
        {
            foreach (var o in temp)
                if (o != null) Object.DestroyImmediate(o);
            rt.Release();
            Object.DestroyImmediate(rt);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    // 보이는 글리프의 가로 잉크 범위(로컬). textBounds 는 글자 진행폭까지 포함한다.
    static void InkX(TextMeshPro tmp, out float minX, out float maxX)
    {
        minX = float.MaxValue;
        maxX = float.MinValue;
        var info = tmp.textInfo;
        for (int i = 0; i < info.characterCount; i++)
        {
            var c = info.characterInfo[i];
            if (!c.isVisible) continue;
            minX = Mathf.Min(minX, c.topLeft.x);
            maxX = Mathf.Max(maxX, c.bottomRight.x);
        }
    }

    static GameObject NewHidden(string name, Scene scene, List<Object> temp)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(go, scene);
        temp.Add(go);
        return go;
    }

    static TextMeshPro NewText(string name, Scene scene, List<Object> temp, TMP_FontAsset font, Material mat)
    {
        var tmp = NewHidden(name, scene, temp).AddComponent<TextMeshPro>();
        tmp.font = font;
        tmp.fontSharedMaterial = mat;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.alignment = TextAlignmentOptions.BottomLeft;
        tmp.extraPadding = true;
        tmp.rectTransform.sizeDelta = new Vector2(1000f, 200f);
        return tmp;
    }

    // 원판 최댓값 필터(팽창). 반지름은 슈퍼샘플 px.
    static float[] Dilate(float[] src, int sw, int sh, float radius)
    {
        int r = Mathf.CeilToInt(radius);
        float r2 = radius * radius;
        var dst = new float[src.Length];
        for (int y = 0; y < sh; y++)
        for (int x = 0; x < sw; x++)
        {
            float m = 0f;
            for (int dy = -r; dy <= r && m < 1f; dy++)
            {
                int yy = y + dy;
                if (yy < 0 || yy >= sh) continue;
                for (int dx = -r; dx <= r; dx++)
                {
                    int xx = x + dx;
                    if (xx < 0 || xx >= sw || dx * dx + dy * dy > r2) continue;
                    float v = src[yy * sw + xx];
                    if (v > m) m = v;
                }
            }
            dst[y * sw + x] = m;
        }
        return dst;
    }

    // 채움 마스크를 조금 팽창해 굵기를 맞추고, 한 번 더 팽창해 외곽선을 만든 뒤 출력 해상도로 줄여 외곽선 → 채움 순으로 덮는다.
    static void Compose(Color32[] px, float[] mask, int w, int h, List<LabelBox> boxes)
    {
        int S = Supersample, sw = w * S;
        var fill = Dilate(mask, sw, h * S, FillBold * S);
        var line = Dilate(fill, sw, h * S, OutlineRadius * S);

        foreach (var b in boxes)
        {
            // 영어 라벨이 원본보다 넓을 수 있어 오른쪽 끝까지, 'p' 꼬리를 위해 위아래로 넉넉히
            int ox0 = Mathf.Max(0, b.X - 2), ox1 = w - 1;
            int oy0 = Mathf.Max(0, b.Y - 6), oy1 = Mathf.Min(h - 1, b.Y + b.H + 2);

            for (int oy = oy0; oy <= oy1; oy++)
            for (int ox = ox0; ox <= ox1; ox++)
            {
                float f = 0f, o = 0f;
                for (int sy = oy * S; sy < (oy + 1) * S; sy++)
                for (int sx = ox * S; sx < (ox + 1) * S; sx++)
                {
                    f += fill[sy * sw + sx];
                    o += line[sy * sw + sx];
                }
                f /= S * S;
                o /= S * S;
                if (o <= 0f) continue;

                // 외곽선 위에 채움
                float a = f + o * (1f - f);
                Vector3 rgb = (ToV(b.Fill) * f + ToV(b.Outline) * o * (1f - f)) / a;

                // 기존 픽셀 위에 덮기(지운 자리는 투명)
                var dst = px[oy * w + ox];
                float da = dst.a / 255f;
                float outA = a + da * (1f - a);
                Vector3 outRgb = outA > 0f ? (rgb * a + ToV(dst) * da * (1f - a)) / outA : Vector3.zero;
                px[oy * w + ox] = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(outRgb.x), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(outRgb.y), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(outRgb.z), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(outA * 255f), 0, 255));
            }
        }
    }

    static Vector3 ToV(Color32 c) => new Vector3(c.r, c.g, c.b);

    static Texture2D LoadPng(string path)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(path));
        return tex;
    }

    // guid 는 Unity 가 새로 준다 — 임포트 설정만 원본과 같게 맞춘다.
    static void CopyImporterSettings(string srcPath, string dstPath)
    {
        var srcImp = (TextureImporter)AssetImporter.GetAtPath(srcPath);
        var dstImp = (TextureImporter)AssetImporter.GetAtPath(dstPath);
        var settings = new TextureImporterSettings();
        srcImp.ReadTextureSettings(settings);
        dstImp.SetTextureSettings(settings);
        dstImp.SetPlatformTextureSettings(srcImp.GetDefaultPlatformTextureSettings());
        dstImp.SetPlatformTextureSettings(srcImp.GetPlatformTextureSettings("Standalone"));
        dstImp.SaveAndReimport();
    }

    // 원본 | 영어판 을 어두운 배경 위에 2배로 나란히
    static string WriteCompare(Color32[] a, Color32[] b, int w, int h)
    {
        const int scale = 2, pad = 8;
        int cw = w * scale * 2 + pad * 3, ch = h * scale + pad * 2;
        var bg = new Color32(38, 40, 46, 255);
        var outPx = new Color32[cw * ch];
        for (int i = 0; i < outPx.Length; i++) outPx[i] = bg;

        void Blit(Color32[] src, int left)
        {
            for (int y = 0; y < h * scale; y++)
            for (int x = 0; x < w * scale; x++)
            {
                var s = src[(y / scale) * w + x / scale];
                float t = s.a / 255f;
                outPx[(y + pad) * cw + left + x] = Color32.Lerp(bg, new Color32(s.r, s.g, s.b, 255), t);
            }
        }
        Blit(a, pad);
        Blit(b, pad * 2 + w * scale);

        var tex = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
        tex.SetPixels32(outPx);
        tex.Apply();
        Directory.CreateDirectory(PreviewDir);
        string path = PreviewDir + "/keyguide_compare.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        return path;
    }
}
