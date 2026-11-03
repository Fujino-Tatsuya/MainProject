using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 제압 간파 섬광 렌더 출력(할 일 179) — <see cref="SuppressFlashPlayer"/> 의 <see cref="ISuppressFlashOutput"/>.
/// <list type="bullet">
/// <item>시작: 실루엣 대상 렌더러에 렌더링 레이어 비트 7(<see cref="SuppressFlashMaskLayers"/>)을 붙인다 — 132 암전 태거를 다른 비트로 재사용.
///       A7 대상 내부만이면 넘겨받은 루트가 간파 대상뿐이라 같은 비트 7 마스크가 곧 '그릴 범위'가 된다(새 비트 없음).</item>
/// <item>매 프레임: 적중점을 이 화면 카메라(Camera.main)로 뷰포트에 투영하고 컷 모양 + 시퀀스 모양 칸을 <see cref="SuppressFlashState"/> 에 쓴다.
///       렌더 기능(<see cref="DarkAdaptationFeature"/>)의 전체 화면 패스 1개가 그린다.</item>
/// <item>끝: 비트 원복·상태 끔.</item>
/// </list>
/// 모든 피어가 자기 화면에서 돈다(공용). 오너 암전(132)과는 별개 — 암전은 오너만.
/// </summary>
public sealed class SuppressFlashScreenOutput : ISuppressFlashOutput
{
    private readonly Func<Material> _material;
    private readonly InterruptOwnerMaskTagger _tagger = new InterruptOwnerMaskTagger(SuppressFlashMaskLayers.Mask);
    private int _seedBase;

    public SuppressFlashScreenOutput(Func<Material> material)
    {
        _material = material;
    }

    /// <summary>지금 비트를 붙인 렌더러 수.</summary>
    public int TaggedCount => _tagger.Count;

    /// <summary>이번 재생의 그리는 범위(A7).</summary>
    public SuppressFlashMaskMode MaskMode { get; private set; }

    public void SetSubjects(IReadOnlyList<GameObject> subjects, SuppressFlashMaskMode mode)
    {
        _tagger.RestoreAll();
        MaskMode = mode;
        _seedBase = (_seedBase + 17) % 997; // 재생마다 별·집중선 모양이 조금씩 달라지게
        if (subjects == null)
            return;

        for (int i = 0; i < subjects.Count; i++)
            _tagger.Tag(subjects[i]);
    }

    public void Show(in SuppressFlashFrame frame, Vector3 worldCenter, SuppressFlashSequence sequence)
    {
        Material material = _material?.Invoke();
        if (material == null || sequence == null || !frame.Active)
        {
            SuppressFlashState.Active = false;
            return;
        }

        Camera camera = Camera.main;
        Vector2 center = camera != null
            ? InterruptHitFlashRules.ViewportCenter(camera.WorldToViewportPoint(worldCenter))
            : new Vector2(0.5f, 0.5f);
        float aspect = camera != null ? Mathf.Max(1e-4f, camera.aspect) : 16f / 9f;

        SuppressFlashState.Material = material;
        SuppressFlashState.Params = Build(frame, sequence, center, aspect, _seedBase + frame.CutIndex * 7 + 1, MaskMode);
        SuppressFlashState.Active = true;
    }

    public void Hide()
    {
        SuppressFlashState.Active = false;
        _tagger.RestoreAll();
    }

    /// <summary>컷 모양 + 시퀀스 칸 → 셰이더 파라미터(순수 — 테스트용으로 공개).</summary>
    public static SuppressFlashParams Build(in SuppressFlashFrame frame, SuppressFlashSequence s, Vector2 center, float aspect, int seed,
        SuppressFlashMaskMode mode = SuppressFlashMaskMode.FullScreen)
    {
        return new SuppressFlashParams
        {
            Mask = new Vector4(mode == SuppressFlashMaskMode.TargetInterior ? 1f : 0f, 0f, 0f, 0f),
            Center = new Vector4(center.x, center.y, aspect, seed),
            Tone = new Vector4(frame.Invert ? 1f : 0f, frame.Contrast, frame.ReturnBlend, frame.Hatching ? 1f : 0f),
            Fx = new Vector4(s.starRadius * frame.StarSize, frame.SpeedLines, s.aberrationScale * frame.Aberration,
                s.letterboxHeight * frame.Letterbox),
            Cross = new Vector4((float)frame.Cross, s.crossWidth, s.crossTaper, s.crossRoughness),
            Shape = new Vector4(s.starSpikes, s.gridCell, s.gridStarRadius * Mathf.Max(frame.StarSize, 0.35f), s.crossMinWidth),
            Look = new Vector4(s.radialBlur, s.sceneExposure, s.hatchPeriodPixels, s.speedLineCount),
            Thresholds = new Vector4(s.darkThreshold, Mathf.Max(s.darkThreshold, s.lightThreshold), 0f, 0f),
            Dark = frame.Dark.linear,
            Mid = frame.Mid.linear,
            Light = frame.Light.linear,
        };
    }
}
