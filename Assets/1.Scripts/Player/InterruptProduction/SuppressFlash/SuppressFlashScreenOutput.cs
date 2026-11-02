using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 제압 간파 섬광 렌더 출력(할 일 179) — <see cref="SuppressFlashPlayer"/> 의 <see cref="ISuppressFlashOutput"/>.
/// <list type="bullet">
/// <item>시작: 실루엣 대상 렌더러에 렌더링 레이어 비트 7(<see cref="SuppressFlashMaskLayers"/>)을 붙인다 — 132 암전 태거를 다른 비트로 재사용.</item>
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
    // 방향 블러 기준(179 후속 3) — 실루엣 대상 첫 번째(발동자) → 적중점을 화면에 투영한 방향.
    private Transform _caster;

    public SuppressFlashScreenOutput(Func<Material> material)
    {
        _material = material;
    }

    /// <summary>지금 비트를 붙인 렌더러 수.</summary>
    public int TaggedCount => _tagger.Count;

    public void SetSubjects(IReadOnlyList<GameObject> subjects)
    {
        _tagger.RestoreAll();
        _seedBase = (_seedBase + 17) % 997; // 재생마다 별·집중선 모양이 조금씩 달라지게
        _caster = null;
        if (subjects == null)
            return;

        if (subjects.Count > 0 && subjects[0] != null)
            _caster = subjects[0].transform;
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
        Vector2 direction = Vector2.right;
        if (camera != null && _caster != null)
        {
            Vector2 from = InterruptHitFlashRules.ViewportCenter(camera.WorldToViewportPoint(_caster.position + Vector3.up));
            direction = AttackDirection(from, center, aspect);
        }

        SuppressFlashState.Material = material;
        SuppressFlashState.Params = Build(frame, sequence, center, aspect, _seedBase + frame.CutIndex * 7 + 1, direction);
        SuppressFlashState.Active = true;
    }

    public void Hide()
    {
        SuppressFlashState.Active = false;
        _tagger.RestoreAll();
        _caster = null;
    }

    /// <summary>
    /// 방향 블러 방향(179 후속 3) — 뷰포트 시전자 → 적중점을 화면 높이 단위(가로 × 화면비)로 정규화. 너무 가까우면 오른쪽.
    /// </summary>
    public static Vector2 AttackDirection(Vector2 fromViewport, Vector2 toViewport, float aspect)
    {
        Vector2 delta = (toViewport - fromViewport) * new Vector2(Mathf.Max(1e-4f, aspect), 1f);
        return delta.sqrMagnitude > 1e-6f ? delta.normalized : Vector2.right;
    }

    /// <summary>
    /// 컷 모양 + 시퀀스 칸 → 셰이더 파라미터(순수 — 테스트용으로 공개). <paramref name="direction"/> = 방향 블러 방향(화면 높이 단위, 0 이면 오른쪽).
    /// 179 후속 3 칸(방향 블러·흔들림·실루엣 밝기·고른 색수차·블룸)은 컷 값이 0 이면 0 — A 셰이더 경로 그대로.
    /// </summary>
    public static SuppressFlashParams Build(in SuppressFlashFrame frame, SuppressFlashSequence s, Vector2 center, float aspect, int seed,
        Vector2 direction = default)
    {
        if (direction.sqrMagnitude < 1e-6f)
            direction = Vector2.right;
        aspect = Mathf.Max(1e-4f, aspect);

        // 흔들림 — 컷 씨앗마다 다른 방향(황금각), 화면 높이 → UV(가로는 화면비로 나눔).
        float angle = seed * 2.39996f;
        float shake = s.shakeScale * frame.Shake;
        Vector2 shakeUv = new Vector2(Mathf.Cos(angle) * shake / aspect, Mathf.Sin(angle) * shake);

        return new SuppressFlashParams
        {
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
            Extra = new Vector4(frame.Tone == SuppressFlashTone.Bloom ? 1f : 0f, frame.SilhouetteDim, frame.AberrationUniform, 0f),
            Direction = new Vector4(direction.x, direction.y, s.directionalBlurLength * frame.DirectionalBlur, 0f),
            Shake = new Vector4(shakeUv.x, shakeUv.y, frame.Bloom, s.bloomRadius),
            Bloom = s.bloomColor.linear,
        };
    }
}
