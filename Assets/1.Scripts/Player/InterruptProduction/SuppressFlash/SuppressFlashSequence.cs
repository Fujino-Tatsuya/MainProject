using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 제압 간파 섬광 컷의 명암(할 일 179). 네거티브 = 어두운 바탕·흰 섬광·흰 실루엣, 포지티브 = 그 3단 뒤집기, 복귀 = 원래 색으로 가라앉음,
/// 블룸 = 원래 색 화면 위 적중점 컬러 블룸·별·방사 블러(179 후속 3, 레퍼런스 B 의 적중 섬광 컷 — 3단 색을 쓰지 않는다).
/// </summary>
public enum SuppressFlashTone
{
    Negative,
    Positive,
    Return,
    Bloom,
}

/// <summary>제압 섬광 시퀀스 선택(179 후속 3) — A = 오늘 제작(보라 컷), B = 레퍼런스(포트폴리오). 디렉터 칸.</summary>
public enum SuppressFlashVariant
{
    A,
    B,
}

/// <summary>컷의 십자 빛기둥 종류(할 일 179).</summary>
public enum SuppressFlashCross
{
    None,
    /// <summary>적중점 중심 십자 하나.</summary>
    Single,
    /// <summary>적중점 기준 격자로 깔린 작은 십자들 + 교차점 작은 별(가운데 십자는 그대로).</summary>
    Grid,
}

/// <summary>
/// 제압 간파 섬광 컷 한 장(할 일 179) — 사양 SPEC.md "컷 구성" 표의 한 줄.
/// 시작/끝 칸(색수차·레터박스)은 컷 안에서 홀드 단위로 계단식으로 넘어간다(같은 값이면 고정).
/// </summary>
[Serializable]
public sealed class SuppressFlashCut
{
    public static readonly Color DefaultDark = new Color32(0x1C, 0x19, 0x30, 0xFF);
    public static readonly Color DefaultMid = new Color32(0x7D, 0x77, 0xA0, 0xFF);
    public static readonly Color DefaultLight = new Color32(0xEC, 0xE9, 0xF6, 0xFF);

    [Tooltip("컷 길이(프레임, 1프레임 = 1/프레임 속도 초)")]
    [Min(1)] public int frames = 2;
    [Tooltip("네거티브(어두운 바탕·흰 실루엣) / 포지티브(3단 뒤집기·짙은 실루엣) / 복귀(원래 색으로 가라앉음)")]
    public SuppressFlashTone tone = SuppressFlashTone.Negative;
    [Tooltip("십자 빛기둥 — 없음 / 단일 / 격자(교차점 작은 별)")]
    public SuppressFlashCross cross = SuppressFlashCross.Single;
    [Tooltip("스파이크 별 크기 배율(0 = 없음). 1 = 시퀀스의 별 반지름")]
    [Min(0f)] public float starSize = 1f;
    [Tooltip("방사 집중선·방사 블러 세기 [0, 1]")]
    [Range(0f, 1f)] public float speedLines = 0.5f;
    [Tooltip("가장자리 색수차 — 컷 시작 값")]
    [Min(0f)] public float aberration = 0.2f;
    [Tooltip("가장자리 색수차 — 컷 끝 값(같으면 고정)")]
    [Min(0f)] public float aberrationEnd = 0.2f;
    [Tooltip("포지티브 실루엣 안 빗금")]
    public bool hatching;
    [Tooltip("레터박스 들어온 정도 — 컷 시작 [0, 1]")]
    [Range(0f, 1f)] public float letterbox = 1f;
    [Tooltip("레터박스 들어온 정도 — 컷 끝(같으면 고정)")]
    [Range(0f, 1f)] public float letterboxEnd = 1f;
    [Tooltip("3단 색 — 짙은 색(#1C1930)")]
    public Color dark = DefaultDark;
    [Tooltip("3단 색 — 중간 색(#7D77A0)")]
    public Color mid = DefaultMid;
    [Tooltip("3단 색 — 밝은 색(#ECE9F6)")]
    public Color light = DefaultLight;

    // 179 후속 3(레퍼런스 B) 칸 — 전부 기본 0 = 효과 없음이라 A 컷(이 칸이 없던 에셋 포함)은 예전 그림 그대로.
    [Tooltip("방향 블러 [0, 1] — 화면·실루엣을 공격 방향(시전자 → 적중점, 화면 투영)으로 끌어 번진다. 길이 = 시퀀스 '방향 블러 길이' × 이 값. 0 = 없음")]
    [Range(0f, 1f)] public float directionalBlur;
    [Tooltip("화면 흔들림 — 시퀀스 '흔들림 폭' × 이 값만큼 화면을 컷마다 다른 방향으로 민다(후처리 안 — 카메라는 그대로). 0 = 없음")]
    [Min(0f)] public float shake;
    [Tooltip("실루엣 밝기 낮춤 [0, 1] — 0 = 흰 실루엣(A), 0.25 = 밝은 회색(3단 경계와 함께 본다)")]
    [Range(0f, 1f)] public float silhouetteDim;
    [Tooltip("복귀 컷 시작 섞기 [0, 1] — 0 = 3단 색에서 시작(A), 1 = 첫 프레임부터 원래 색(색수차만 남김)")]
    [Range(0f, 1f)] public float returnStart;
    [Tooltip("색수차를 화면 전체에 고르게 [0, 1] — 0 = 가장자리만(A), 1 = 가운데도 같은 세기(캐릭터 외곽 RGB 갈라짐)")]
    [Range(0f, 1f)] public float aberrationUniform;
    [Tooltip("컬러 블룸 세기(블룸 명암 컷만) — 시퀀스 '블룸 색'·'블룸 반지름'. 0 = 없음")]
    [Min(0f)] public float bloom;

    public SuppressFlashCut() { }

    public SuppressFlashCut(int frames, SuppressFlashTone tone, SuppressFlashCross cross, float starSize, float speedLines,
        float aberration, float aberrationEnd, bool hatching, float letterbox, float letterboxEnd)
    {
        this.frames = frames;
        this.tone = tone;
        this.cross = cross;
        this.starSize = starSize;
        this.speedLines = speedLines;
        this.aberration = aberration;
        this.aberrationEnd = aberrationEnd;
        this.hatching = hatching;
        this.letterbox = letterbox;
        this.letterboxEnd = letterboxEnd;
    }

    /// <summary>3단 색을 한 번에 바꾼다(레퍼런스 B 의 흑백 컷용). 자기 자신을 돌려준다.</summary>
    public SuppressFlashCut WithColors(Color dark, Color mid, Color light)
    {
        this.dark = dark;
        this.mid = mid;
        this.light = light;
        return this;
    }
}

/// <summary>
/// 제압 간파 암순응 컷 한 장(할 일 192 Q7) — 섬광 시퀀스가 끝난(복귀 컷 뒤) 다음에 차례로 끊어 보인다. 마지막 컷 뒤 = 원래 색.
/// 어둡힘은 암순응 렌더(<see cref="InterruptDarkAdaptation"/> — 밝은 빛·이펙트는 남고 어두운 픽셀만 낮춤)의 강도 [0, 1].
/// 길이는 초(실제 시간) — 시퀀스 프레임 속도(A 24 / B 60)와 무관하게 같은 길이로 보인다.
/// </summary>
[Serializable]
public sealed class SuppressDarkAdaptCut
{
    [Tooltip("컷 길이(초, 실제 시간)")]
    [Min(0.01f)] public float duration = 0.15f;
    [Tooltip("어둡힘 [0, 1] — 1 = 암순응 최대(어두움), 0.5 = 중간. 0 = 원래 색")]
    [Range(0f, 1f)] public float darkness = 1f;

    public SuppressDarkAdaptCut() { }

    public SuppressDarkAdaptCut(float duration, float darkness)
    {
        this.duration = duration;
        this.darkness = darkness;
    }
}

/// <summary>
/// 제압 간파 '애니메이션 섬광' 컷 데이터(할 일 179) — 디렉터(<see cref="InterruptProductionDirector"/>)가 재생 때마다 읽는다(캐싱 없음):
/// Play 중 인스펙터에서 값을 바꾸면 그 다음 렌더 프레임부터 쓰인다. 기본값 = 사양 표(<see cref="ResetToSpec"/>).
/// 그림은 전부 셰이더 절차 생성(SuppressFlash.shader) — 텍스처 칸 없음.
/// 문서: Docs/tech/interrupt-presentation.md §7.
/// </summary>
[CreateAssetMenu(fileName = "SuppressFlashSequence", menuName = "BeaverLobby/Interrupt/Suppress Flash Sequence")]
public sealed class SuppressFlashSequence : ScriptableObject
{
    [Header("재생 — 실제 시간(슬로우·히트스톱 무관)")]
    [Tooltip("프레임 속도(초당 프레임). 24 + 홀드 2 = 초당 12장")]
    [Min(1f)] public float frameRate = 24f;
    [Tooltip("한 그림을 유지하는 프레임 수(2 = 2프레임 홀드). 컷 안의 시작→끝 값도 이 단위로 계단식")]
    [Min(1)] public int holdFrames = 2;

    [Header("컷")]
    public List<SuppressFlashCut> cuts = CreateSpecCuts();

    [Header("제압 암순응 컷(할 일 192) — 복귀 컷 뒤, 디렉터 '제압 암순응 = 컷'일 때")]
    [Tooltip("어두움 → 중간 → (끝 = 원래 색). 각 컷은 초 단위·어둡힘 강도. 비우면 컷 암순응 없음(부드럽게 모드는 이 칸을 안 쓴다)")]
    public List<SuppressDarkAdaptCut> darkAdaptCuts = CreateDarkAdaptCuts();

    [Header("모양 — 화면 높이 단위")]
    [Tooltip("십자 빛기둥 중심 굵기(반폭)")]
    [Min(0f)] public float crossWidth = 0.07f;
    [Tooltip("십자 빛기둥이 바깥으로 가늘어지는 정도(클수록 빨리 가늘어짐)")]
    [Min(0f)] public float crossTaper = 1.6f;
    [Tooltip("십자 빛기둥 가장자리 울퉁불퉁함 [0, 1]")]
    [Range(0f, 1f)] public float crossRoughness = 0.45f;
    [Tooltip("이보다 가늘어진 빛기둥 끝은 지운다(가는 선 금지)")]
    [Min(0f)] public float crossMinWidth = 0.008f;
    [Tooltip("격자 십자 칸 크기")]
    [Min(0.05f)] public float gridCell = 0.32f;
    [Tooltip("격자 교차점 작은 별 반지름(별 크기 배율과 곱한다)")]
    [Min(0f)] public float gridStarRadius = 0.12f;
    [Tooltip("별 크기 1 의 별 반지름")]
    [Min(0f)] public float starRadius = 0.2f;
    [Tooltip("별 스파이크 수")]
    [Range(5, 24)] public int starSpikes = 11;
    [Tooltip("방사 집중선 수(한 바퀴)")]
    [Range(16, 160)] public int speedLineCount = 72;
    [Tooltip("방사 블러 최대 세기 [0, 1](컷 집중선 세기와 곱한다)")]
    [Range(0f, 1f)] public float radialBlur = 0.35f;

    [Header("색")]
    [Tooltip("화면 밝기 → 3단 경계(짙은|중간)")]
    [Range(0f, 1f)] public float darkThreshold = 0.3f;
    [Tooltip("화면 밝기 → 3단 경계(중간|밝은)")]
    [Range(0f, 1f)] public float lightThreshold = 0.62f;
    [Tooltip("바탕(화면) 밝기 배율 — 네거티브 바탕을 어둡게 누른다")]
    [Min(0f)] public float sceneExposure = 0.8f;
    [Tooltip("빗금 간격(픽셀)")]
    [Min(2f)] public float hatchPeriodPixels = 12f;
    [Tooltip("색수차 1 일 때 화면 가장자리 최대 번짐(화면 높이 비율)")]
    [Min(0f)] public float aberrationScale = 0.025f;
    [Tooltip("레터박스 띠 하나의 높이(화면 높이 비율)")]
    [Range(0f, 0.4f)] public float letterboxHeight = 0.11f;

    [Header("섬광 줄이기(옵션 켬일 때)")]
    [Tooltip("섬광 줄이기 — 반전을 끄고 이 대비로 낮춘다 [0, 1](1 = 그대로). 블룸·흔들림도 이 비율로 줄인다")]
    [Range(0f, 1f)] public float reducedContrast = 0.45f;

    // 179 후속 3(레퍼런스 B) — 컷의 방향 블러·흔들림·블룸 칸이 0 이면 쓰이지 않는다(A 는 안 씀).
    [Header("레퍼런스 B 칸 — 컷 칸이 0 이면 안 쓰임")]
    [Tooltip("방향 블러 1 일 때 끌리는 길이(화면 높이 비율)")]
    [Min(0f)] public float directionalBlurLength = 0.12f;
    [Tooltip("흔들림 1 일 때 화면 밀림(화면 높이 비율)")]
    [Min(0f)] public float shakeScale = 0.015f;
    [Tooltip("블룸 명암 컷의 적중점 블룸 색(마젠타)")]
    public Color bloomColor = DefaultBloomColor;
    [Tooltip("블룸 1 의 블룸 반지름(화면 높이) — 각도마다 조금씩 달라 완벽한 원이 아니다")]
    [Min(0.01f)] public float bloomRadius = 0.32f;

    public static readonly Color DefaultBloomColor = new Color32(0xFF, 0x3C, 0xD2, 0xFF);

    /// <summary>프레임 단위 전체 길이.</summary>
    public int TotalFrames => SuppressFlashRules.TotalFrames(cuts);

    /// <summary>사양 표(SPEC.md "컷 구성", 2026-11-02 확정) 값으로 되돌린다. 새 에셋·인스펙터 Reset 도 이 값.</summary>
    public void ResetToSpec()
    {
        frameRate = 24f;
        holdFrames = 2;
        cuts = CreateSpecCuts();
        darkAdaptCuts = CreateDarkAdaptCuts();
    }

    /// <summary>제압 암순응 기본 2컷(할 일 192 Q7) — 어두움 0.15초 → 중간 0.15초 → 원래 색.</summary>
    public static List<SuppressDarkAdaptCut> CreateDarkAdaptCuts() => new List<SuppressDarkAdaptCut>
    {
        new SuppressDarkAdaptCut(0.15f, 1f),
        new SuppressDarkAdaptCut(0.15f, 0.5f),
    };

    /// <summary>
    /// 레퍼런스 B(포트폴리오 5.40~6.08초, 60fps 프레임 분석 — 179 후속 3) 값으로 바꾼다. 60fps·홀드 1 = 컷마다 길이가 다르다.
    /// 예비 동작(약 0.1초)은 기존 강 시도 연출이 맡아 뺐다. 3단 경계·바탕 밝기도 B 흑백 컷에 맞춘다.
    /// </summary>
    [ContextMenu("레퍼런스 B 값으로")]
    public void ResetToReference()
    {
        frameRate = 60f;
        holdFrames = 1;
        cuts = CreateReferenceCuts();
        darkAdaptCuts = CreateDarkAdaptCuts();
        darkThreshold = 0.3f;
        lightThreshold = 0.85f;
        sceneExposure = 0.9f;
        radialBlur = 0.45f;
        aberrationScale = 0.03f;
        directionalBlurLength = 0.12f;
        shakeScale = 0.015f;
        bloomColor = DefaultBloomColor;
        bloomRadius = 0.32f;
    }

    [ContextMenu("사양 A 값으로")]
    private void ResetToSpecMenu() => ResetToSpec();

    public static readonly Color ReferenceGray = new Color(0.72f, 0.72f, 0.72f, 1f);

    /// <summary>
    /// 레퍼런스 B 5컷(22프레임 = 0.367초, 60fps):
    /// ② 적중 직전 1 — 원래 색·빨강/파랑 궤적(고른 색수차 + 방향 블러)·흔들림 /
    /// ③ 화이트아웃 실루엣 5 — 흰 바탕·검은 실루엣·밝은 이펙트도 검정(흑백 두 색, 뒤집기)·강한 방향 블러 /
    /// ④ 적중 섬광 6 — 원래 색 위 마젠타 블룸·스파이크 별·방사 블러·가장자리 색수차 /
    /// ⑤ 흑백 반전 2 — 검은 바탕·흰 이펙트·밝은 회색 실루엣 /
    /// ⑥ 복귀 8 — 첫 프레임부터 원래 색, 캐릭터 외곽 RGB 색수차 1 → 0.
    /// </summary>
    public static List<SuppressFlashCut> CreateReferenceCuts() => new List<SuppressFlashCut>
    {
        new SuppressFlashCut(1, SuppressFlashTone.Return, SuppressFlashCross.None, 0f, 0f, 1.2f, 1.2f, false, 0f, 0f)
        {
            returnStart = 1f, aberrationUniform = 1f, directionalBlur = 0.5f, shake = 1f,
        },
        new SuppressFlashCut(5, SuppressFlashTone.Positive, SuppressFlashCross.None, 0f, 0f, 0f, 0f, false, 0f, 0f)
        {
            directionalBlur = 1f, shake = 0.4f,
        }.WithColors(Color.black, Color.white, Color.white),
        new SuppressFlashCut(6, SuppressFlashTone.Bloom, SuppressFlashCross.None, 1.4f, 0.7f, 0.8f, 0.3f, false, 0f, 0f)
        {
            bloom = 1.2f,
        },
        new SuppressFlashCut(2, SuppressFlashTone.Negative, SuppressFlashCross.None, 0.8f, 0.3f, 0.2f, 0.2f, false, 0f, 0f)
        {
            silhouetteDim = 0.25f,
        }.WithColors(Color.black, ReferenceGray, Color.white),
        new SuppressFlashCut(8, SuppressFlashTone.Return, SuppressFlashCross.None, 0f, 0f, 1f, 0f, false, 0f, 0f)
        {
            returnStart = 1f, aberrationUniform = 1f,
        },
    };

    /// <summary>사양 표 7컷. 레터박스 = 1컷에 밀려 들어오고 7컷에 빠진다.</summary>
    public static List<SuppressFlashCut> CreateSpecCuts() => new List<SuppressFlashCut>
    {
        new SuppressFlashCut(2, SuppressFlashTone.Negative, SuppressFlashCross.Single, 1.0f, 0.5f, 0.2f, 0.2f, false, 0.6f, 0.6f),
        new SuppressFlashCut(2, SuppressFlashTone.Positive, SuppressFlashCross.Grid, 0.45f, 0.2f, 0.5f, 0.5f, true, 1f, 1f),
        new SuppressFlashCut(2, SuppressFlashTone.Negative, SuppressFlashCross.Single, 1.25f, 1.0f, 0.3f, 0.3f, false, 1f, 1f),
        new SuppressFlashCut(2, SuppressFlashTone.Positive, SuppressFlashCross.Single, 0.7f, 0.6f, 1.0f, 1.0f, true, 1f, 1f),
        new SuppressFlashCut(2, SuppressFlashTone.Negative, SuppressFlashCross.Single, 1.9f, 1.0f, 0.4f, 0.4f, false, 1f, 1f),
        new SuppressFlashCut(2, SuppressFlashTone.Positive, SuppressFlashCross.None, 0f, 0.15f, 0.6f, 0.6f, true, 1f, 1f),
        new SuppressFlashCut(3, SuppressFlashTone.Return, SuppressFlashCross.None, 0f, 0f, 0.8f, 0f, false, 1f, 0f),
    };

    private void Reset() => ResetToSpec();
}
