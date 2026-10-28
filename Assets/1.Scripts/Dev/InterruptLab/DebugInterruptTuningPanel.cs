using UnityEngine;

/// <summary>
/// 간파 연출 튜닝 패널(DebugInterruptLab 씬에만 둔다, 할 일 132 §17) — 화면 OnGUI 하나로
/// 연출 효과마다 켜기/끄기 토글 + 디버그 배속(<see cref="DebugTimeScale"/> 재사용)을 만진다.
///
/// <list type="bullet">
/// <item>토글은 이 피어의 디렉터(<see cref="InterruptProductionDirector.Instance"/>) 런타임 값만 바꾼다(<see cref="InterruptProductionDirector.SetEffect"/>·
///       <see cref="InterruptProductionDirector.Set"/>). 프리팹·씬에 저장하지 않는다 — Play 를 끝내면 원래 값.</item>
/// <item>배속은 같은 오브젝트의 <see cref="DebugTimeScale"/> 슬라이더 값을 쓴다 — 0.05~2, 성공 후 1배속 복귀 규칙도 그 컴포넌트 그대로.</item>
/// <item>꺼진 효과는 그리기만 안 한다 — 상태 기계·시간표·섬광 순간(암전 걷힘 등)은 그대로 돈다.</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public sealed class DebugInterruptTuningPanel : MonoBehaviour
{
    /// <summary>패널 한 줄 — 효과 칸 또는 기존 스위치 칸(팀원 암순응 = A2).</summary>
    public readonly struct Row
    {
        public readonly string Label;
        public readonly bool IsCue;
        public readonly InterruptPresentationEffect Effect;
        public readonly InterruptProductionCue Cue;

        public Row(string label, InterruptPresentationEffect effect)
        {
            Label = label;
            IsCue = false;
            Effect = effect;
            Cue = default;
        }

        public Row(string label, InterruptProductionCue cue)
        {
            Label = label;
            IsCue = true;
            Effect = default;
            Cue = cue;
        }

        public bool Get(InterruptProductionDirector director) =>
            director != null && (IsCue ? director.Get(Cue) : director.IsEffectOn(Effect));

        public void Set(InterruptProductionDirector director, bool on)
        {
            if (director == null)
                return;

            if (IsCue)
                director.Set(Cue, on);
            else
                director.SetEffect(Effect, on);
        }
    }

    /// <summary>패널 줄 — 계획서 §17-1 의 효과 전부(효과 칸 9 + 팀원 암순응 A2).</summary>
    public static readonly Row[] Rows =
    {
        new Row("오너 암전", InterruptPresentationEffect.OwnerDim),
        new Row("빛 모임 (VFX+라이트)", InterruptPresentationEffect.LightGather),
        new Row("② 스타버스트", InterruptPresentationEffect.Starburst),
        new Row("③ 화이트아웃", InterruptPresentationEffect.Whiteout),
        new Row("④ 스미어", InterruptPresentationEffect.Smear),
        new Row("⑤ 색수차 블러", InterruptPresentationEffect.Aberration),
        new Row("⑥ 가라앉음 (블룸)", InterruptPresentationEffect.Settle),
        new Row("월드 섬광", InterruptPresentationEffect.WorldFlash),
        new Row("적중 지점 라이트", InterruptPresentationEffect.HitLight),
        new Row("팀원 암순응 (A2)", InterruptProductionCue.DarkAdaptation),
    };

    /// <summary>배속 버튼 — 끝값 + 인스펙터 프리셋.</summary>
    public static readonly float[] SpeedButtons =
        { DebugTimeScale.MinScale, 0.1f, 0.25f, 0.5f, 1f, DebugTimeScale.MaxScale };

    [Tooltip("패널을 펼친 채로 시작")]
    [SerializeField] bool expanded = true;

    [Tooltip("패널 너비(px)")]
    [SerializeField, Min(160f)] float width = 240f;

    DebugTimeScale _timeScale;

    void Awake() => TryGetComponent(out _timeScale);

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(Screen.width - width - 10f, 10f, width, Screen.height - 20f));
        GUILayout.BeginVertical(GUI.skin.box);

        if (GUILayout.Button(expanded ? "▼ 간파 연출 튜닝" : "▶ 간파 연출 튜닝"))
            expanded = !expanded;

        if (expanded)
        {
            DrawEffects();
            DrawSpeed();
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    void DrawEffects()
    {
        InterruptProductionDirector director = InterruptProductionDirector.Instance;
        if (director == null)
        {
            GUILayout.Label("디렉터 없음(NetworkManager 대기)");
            return;
        }

        foreach (Row row in Rows)
        {
            bool on = row.Get(director);
            bool next = GUILayout.Toggle(on, row.Label);
            if (next != on)
                row.Set(director, next);
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("전부 켬"))
            SetAll(director, true);
        if (GUILayout.Button("암전만"))
            SetDefaults(director);
        GUILayout.EndHorizontal();
    }

    void DrawSpeed()
    {
        if (_timeScale == null)
            return;

        GUILayout.Space(6f);
        GUILayout.Label($"배속 ×{_timeScale.Scale:0.00}" +
            (_timeScale.IsHoldingNormalSpeed ? $"  (성공 후 1배속 {_timeScale.NormalSpeedRemaining:0.0}s)" : ""));
        _timeScale.Scale = GUILayout.HorizontalSlider(_timeScale.Scale, DebugTimeScale.MinScale, DebugTimeScale.MaxScale);

        GUILayout.BeginHorizontal();
        foreach (float preset in SpeedButtons)
        {
            if (GUILayout.Button(preset.ToString("0.##")))
                _timeScale.Scale = preset;
        }

        GUILayout.EndHorizontal();
    }

    /// <summary>모든 줄을 켜거나 끈다.</summary>
    public static void SetAll(InterruptProductionDirector director, bool on)
    {
        foreach (Row row in Rows)
            row.Set(director, on);
    }

    /// <summary>기본값(오너 암전만 켬 — §17-1)으로 되돌린다. 팀원 암순응(A2)도 끈다.</summary>
    public static void SetDefaults(InterruptProductionDirector director)
    {
        foreach (Row row in Rows)
            row.Set(director, !row.IsCue && InterruptPresentationEffectRules.DefaultOn(row.Effect));
    }
}
