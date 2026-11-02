using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// [모든 피어] 무전 캡슐 한 줄 그리기 — 문구 id → 조각 나누기(<see cref="CinematicLineSplitter"/>) → 지금 시각의 조각(<see cref="CinematicLineSchedule"/>) → <see cref="CinematicRadioCaption"/>.
/// 일반 무전(<see cref="MapSceneRadioPresentation"/>)·2구역 소개(<see cref="MapSceneZone2IntroPresentation"/>)가 하나씩 든다.
/// 캡슐 모양·움직임·최대 폭은 4구역 연출 에셋(<see cref="MapSceneZone4IntroData"/>) 값을 같이 쓴다(4구역 표시와 같은 그림 — 한 곳에서 조절).
/// </summary>
public sealed class MapSceneRadioCaptionDriver
{
    private readonly string _tag;
    private readonly CinematicRadioCaption _caption;
    private readonly Dictionary<string, (string source, List<string> chunks)> _chunks = new Dictionary<string, (string, List<string>)>();
    private readonly HashSet<string> _warnedIds = new HashSet<string>();

    public MapSceneRadioCaptionDriver(string name)
    {
        _tag = $"[{name}]";
        _caption = new CinematicRadioCaption(name);
    }

    public bool Owns(Canvas canvas) => _caption.Owns(canvas);

    /// <summary>
    /// 대사 시작부터 <paramref name="local"/> 초 지난 조각을 보여 준다. 시작 전·간격·끝난 뒤·문구 없음이면 숨기고 false.
    /// <paramref name="holdUntil"/> ≥ 0 이면 마지막 조각을 그때까지 붙잡는다.
    /// </summary>
    public bool Show(string id, float local, float chunkSeconds, float gapSeconds, float holdUntil = -1f)
    {
        MapSceneZone4IntroData look = MapSceneZone4IntroData.Load();
        TMP_FontAsset font = ResolveFont(look);
        _caption.Configure(look.CaptionBackground, look.CaptionBadge, look.CaptionWalkie, font, look.CaptionAnchor, look.CaptionOffset,
                           look.CaptionScale, look.CaptionFontSize, look.CaptionTextColor);
        _caption.ConfigureMotion(look.CaptionRiseDistance, look.CaptionRiseSeconds, look.BadgePulseScale, look.BadgePulseSeconds);

        List<string> chunks = ChunksOf(id, font, look.MaxLineWidth);
        if (chunks == null || !CinematicLineSchedule.Sample(local, chunks.Count, chunkSeconds, gapSeconds, holdUntil,
                                                            out int index, out float within, out float duration))
        {
            _caption.Hide();
            return false;
        }
        float fade = look.CaptionFadeSeconds;
        float alpha = fade > 0f ? Mathf.Clamp01(Mathf.Min(within / fade, (duration - within) / fade)) : 1f;
        _caption.Show(chunks[index], alpha, within);   // within = 이 조각이 나타난 뒤 — 올라옴·배지 펄스
        return true;
    }

    /// <summary>문구 전체 길이(초, 붙잡기 없이). 문구를 아직 못 읽으면(안내 UI·글꼴 전) -1.</summary>
    public float TotalSeconds(string id, float chunkSeconds, float gapSeconds)
    {
        MapSceneZone4IntroData look = MapSceneZone4IntroData.Load();
        List<string> chunks = ChunksOf(id, ResolveFont(look), look.MaxLineWidth);
        return chunks == null ? -1f : CinematicLineSchedule.TotalSeconds(chunks.Count, chunkSeconds, gapSeconds);
    }

    public void Hide() => _caption.Hide();

    public void Dispose() => _caption.Dispose();

    private List<string> ChunksOf(string id, TMP_FontAsset font, float maxWidth)
    {
        if (string.IsNullOrEmpty(id)) return null;
        TutorialGuideUI ui = TutorialGuideUI.Instance;
        if (ui == null || font == null) return null;   // 글꼴 전에 재면 폭이 틀린 채로 굳는다(기본 글꼴엔 한글이 없다)
        if (!ui.TryResolveText(id, out string text))
        {
            if (_warnedIds.Add(id)) Edit.LogWarning($"{_tag} 무전 문구 '{id}' 가 TutorialGuideTexts/Strings 에 없다 — 건너뛴다.");
            return null;
        }
        if (_chunks.TryGetValue(id, out (string source, List<string> chunks) cached) && cached.source == text) return cached.chunks;

        List<string> split = CinematicLineSplitter.Split(text, _caption.Measure, maxWidth);
        _chunks[id] = (text, split);
        return split;
    }

    private static TMP_FontAsset ResolveFont(MapSceneZone4IntroData look)
    {
        if (look != null && look.CaptionFont != null) return look.CaptionFont;
        TutorialGuideUI ui = TutorialGuideUI.Instance;
        return ui != null ? ui.SubtitleFont : null;
    }
}
