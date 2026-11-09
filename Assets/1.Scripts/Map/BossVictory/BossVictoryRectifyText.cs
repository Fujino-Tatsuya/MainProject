using TMPro;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 5단계 — "Rectify" 글자(PLAN-boss-victory-glitch Q4). 로컬 표시, 연출 오버레이 캔버스(<see cref="CinematicOverlay.Root"/>) 위에 런타임 생성.
/// 두 겹: 외곽선 글자(면 투명 · 노란 외곽선, 세기 = <c>textOutline</c> 곡선) + 채움 글자(글자마다 꼭짓점 색 — 왼쪽부터 다색으로 깜빡이며 차오르고
/// 다 찬 글자는 노랑, <c>textFlash</c> 동안 흰 섬광). 위치 흔들림 = <c>textJitter</c>. 시계 = 글리치 시각(처치 판정 뒤 초).
/// 글리치 렌더 패스는 카메라 화면에만 걸려 이 글자(Screen Space Overlay)는 깨끗하게 남는다.
/// </summary>
public sealed class BossVictoryRectifyText
{
    readonly RectTransform _root;
    readonly TMP_Text _outline;
    readonly TMP_Text _fill;
    readonly Material _outlineMaterial;

    public BossVictoryRectifyText(RectTransform parent, BossVictoryGlitchSettings s, TMP_FontAsset fallbackFont)
    {
        var go = new GameObject("BossVictoryRectify", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        _root = (RectTransform)go.transform;
        _root.anchorMin = s.textViewportPosition;
        _root.anchorMax = s.textViewportPosition;
        _root.pivot = new Vector2(0.5f, 0.5f);
        _root.sizeDelta = new Vector2(1800f, 500f);

        TMP_FontAsset font = s.textFont != null ? s.textFont : fallbackFont;
        string text = UiStrings.BossVictoryRectify.Text;

        _outline = MakeLabel("Outline", font, text, s);
        if (font != null && font.material != null)
        {
            _outlineMaterial = new Material(font.material) { name = "BossVictoryRectify Outline (runtime)", hideFlags = HideFlags.DontSave };
            _outlineMaterial.SetColor(ShaderUtilities.ID_FaceColor, new Color(1f, 1f, 1f, 0f));
            _outlineMaterial.SetColor(ShaderUtilities.ID_OutlineColor, s.textOutlineColor);
            _outlineMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, s.textOutlineWidth);
            _outlineMaterial.EnableKeyword(ShaderUtilities.Keyword_Outline);
            _outline.fontSharedMaterial = _outlineMaterial;
        }
        _fill = MakeLabel("Fill", font, text, s);

        SetAlpha(_outline, 0f);
        SetAlpha(_fill, 0f);
    }

    TMP_Text MakeLabel(string name, TMP_FontAsset font, string text, BossVictoryGlitchSettings s)
    {
        TMP_Text label = CinematicOverlay.Label(_root, name, s.textSize, TextAlignmentOptions.Center);
        CinematicOverlay.Stretch((RectTransform)label.transform);
        if (font != null) label.font = font;
        label.characterSpacing = s.textCharacterSpacing;
        label.text = text;
        return label;
    }

    /// <param name="t">글리치 시각(초). 타임라인 길이 뒤로는 호출측이 끝값으로 자른다.</param>
    public void Tick(float t, float rawTime, BossVictoryGlitchSettings s)
    {
        float outline = Mathf.Clamp01(s.textOutline.Evaluate(t));
        float fill = Mathf.Clamp01(s.textFill.Evaluate(t));
        float flash = Mathf.Clamp01(s.textFlash.Evaluate(t));
        float jitter = Mathf.Clamp01(s.textJitter.Evaluate(t));

        int tick = Mathf.FloorToInt(rawTime * 24f);   // 다색 깜빡임·흔들림 난수는 24fps 로 바뀐다
        float dx = jitter > 0f ? (Hash(tick) - 0.5f) * 2f * s.textJitterPixels * jitter : 0f;
        _root.anchoredPosition = new Vector2(dx, 0f);
        _root.localScale = Vector3.one * (1f + 0.05f * flash);

        SetAlpha(_outline, outline);
        PaintFill(fill, flash, tick, s);
    }

    // 글자마다 꼭짓점 색 — 채움 앞머리가 지난 만큼(띠 너비 안) 다색 깜빡임, 다 지나면 노랑. 섬광은 흰색 쪽으로.
    void PaintFill(float fill, float flash, int tick, BossVictoryGlitchSettings s)
    {
        if (fill <= 0f)
        {
            SetAlpha(_fill, 0f);
            return;
        }
        _fill.color = Color.white;
        _fill.ForceMeshUpdate();
        TMP_TextInfo info = _fill.textInfo;
        int count = info.characterCount;
        float band = Mathf.Max(0.05f, s.textFillBand);
        float front = fill * (1f + band);
        Color[] palette = s.textFillPalette;
        int visibleIndex = 0;
        int visibleCount = 0;
        for (int i = 0; i < count; i++) if (info.characterInfo[i].isVisible) visibleCount++;

        for (int i = 0; i < count; i++)
        {
            TMP_CharacterInfo c = info.characterInfo[i];
            if (!c.isVisible) continue;
            float x = visibleCount > 1 ? visibleIndex / (visibleCount - 1f) : 0f;
            float local = Mathf.Clamp01((front - x) / band);
            Color col;
            if (local >= 1f || palette == null || palette.Length == 0) col = s.textColor;
            else col = palette[(visibleIndex + tick) % palette.Length];
            col.a = local >= 1f ? s.textColor.a : local;
            float a = col.a;
            col = Color.Lerp(col, Color.white, flash);
            col.a = a;

            Color32 c32 = col;
            Color32[] colors = info.meshInfo[c.materialReferenceIndex].colors32;
            int v = c.vertexIndex;
            colors[v] = c32;
            colors[v + 1] = c32;
            colors[v + 2] = c32;
            colors[v + 3] = c32;
            visibleIndex++;
        }
        _fill.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    static void SetAlpha(TMP_Text label, float a)
    {
        if (label == null) return;
        Color c = label.color;
        if (Mathf.Approximately(c.a, a)) return;
        c.a = a;
        label.color = c;
    }

    static float Hash(int n)
    {
        uint x = (uint)n * 2654435761u;
        x ^= x >> 13;
        x *= 1274126177u;
        return (x & 0xFFFFFF) / 16777215f;
    }

    public void Dispose()
    {
        if (_root != null) Object.Destroy(_root.gameObject);
        if (_outlineMaterial != null) Object.Destroy(_outlineMaterial);
    }
}
