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
/// <item>제압 간파 섬광 A6 토글 + 허수아비 간파를 제압 간파로 재현하는 토글(179 후속 3, <see cref="DebugSuppressInterruptRepro"/>)
///       + A7 섬광 범위 버튼(전체 화면 ↔ 대상 내부만, 179/192 후속).</item>
/// <item>간파 연출 4단계(할 일 192) — 지금 단계 표시, 실패/근접/대쉬/제압 재현 버튼(<see cref="DebugInterruptTierPreview"/>).</item>
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

    /// <summary>패널 줄 — 계획서 §17-1 의 효과 전부(효과 칸 9 + 팀원 암순응 A2) + A4 성공 이펙트 칸(프리팹 값 켬 그대로).</summary>
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
        new Row("성공 이펙트 (A4)", InterruptProductionCue.SuccessFxSlot),
    };

    /// <summary>배속 버튼 — 끝값 + 인스펙터 프리셋.</summary>
    public static readonly float[] SpeedButtons =
        { DebugTimeScale.MinScale, 0.1f, 0.25f, 0.5f, 1f, DebugTimeScale.MaxScale };

    [Tooltip("패널을 펼친 채로 시작")]
    [SerializeField] bool expanded = true;

    [Tooltip("패널 너비(px)")]
    [SerializeField, Min(160f)] float width = 240f;

    DebugTimeScale _timeScale;
    readonly DebugSuppressInterruptRepro _suppressRepro = new DebugSuppressInterruptRepro();
    readonly DebugInterruptTierPreview _tierPreview = new DebugInterruptTierPreview();

    void Awake() => TryGetComponent(out _timeScale);

    void Update()
    {
        float now = Time.unscaledTime;
        _suppressRepro.Tick(now);
        _suppressRepro.SyncForecast();
        _tierPreview.Tick(InterruptProductionDirector.Instance, now);
    }

    void OnDestroy()
    {
        _suppressRepro.Dispose();
        InterruptProductionDirector.DebugSuppressForecastOverride = null;
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(Screen.width - width - 10f, 10f, width, Screen.height - 20f));
        GUILayout.BeginVertical(GUI.skin.box);

        if (GUILayout.Button(expanded ? "▼ 간파 연출 튜닝" : "▶ 간파 연출 튜닝"))
            expanded = !expanded;

        if (expanded)
        {
            DrawEffects();
            DrawTiers();
            DrawSuppressFlash();
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

    // 간파 연출 4단계(할 일 192) — 지금 단계 표시 + 단계 재현 버튼 4개(이 화면만, 슬로우·줌 없음).
    void DrawTiers()
    {
        InterruptProductionDirector director = InterruptProductionDirector.Instance;
        if (director == null)
            return;

        GUILayout.Space(6f);
        GUILayout.Label($"간파 단계 — 내 마지막: {DebugInterruptTierPreview.Label(director.LastOwnerTier)}" +
                        $" · 받은 성공: {DebugInterruptTierPreview.Label(director.LastSuccessTier)}");

        GUILayout.BeginHorizontal();
        foreach (InterruptPresentationTier tier in DebugInterruptTierPreview.All)
        {
            if (GUILayout.Button(DebugInterruptTierPreview.Label(tier)))
                _tierPreview.Begin(director, tier, Time.unscaledTime);
        }

        GUILayout.EndHorizontal();
        if (!string.IsNullOrEmpty(_tierPreview.Status))
            GUILayout.Label("  " + _tierPreview.Status);

        DrawTierVersions(director);
    }

    // 적중 라이트 연출 버전(할 일 194) — 일반·대쉬 단계마다 V0/V1/V2·암순응 가/나(이 피어 런타임 값만, 다음 성공부터) + 그 단계 재생.
    // '실제' = 데이터·프리팹이 없어 V0 로 돌아갔는지까지 반영한 값.
    void DrawTierVersions(InterruptProductionDirector director)
    {
        foreach (InterruptPresentationTier tier in DebugInterruptTierPreview.VersionTiers)
        {
            InterruptTierProfile profile = director.TierProfile(tier);
            if (profile == null)
                continue;

            GUILayout.Label($"{DebugInterruptTierPreview.Label(tier)} 버전 — 실제 {DebugInterruptTierPreview.LookLabel(director.SuccessLook(tier))}");
            GUILayout.BeginHorizontal();
            foreach (InterruptTierFlash flash in DebugInterruptTierPreview.Versions)
            {
                string label = DebugInterruptTierPreview.Selected(DebugInterruptTierPreview.VersionLabel(flash), profile.flash == flash);
                if (GUILayout.Button(label))
                    profile.flash = flash;
            }

            foreach (InterruptDarkAdaptMode mode in DebugInterruptTierPreview.DarkAdaptModes)
            {
                string label = DebugInterruptTierPreview.Selected(DebugInterruptTierPreview.DarkAdaptLabel(mode), profile.darkAdapt == mode);
                if (GUILayout.Button(label))
                    profile.darkAdapt = mode;
            }

            if (GUILayout.Button("재생"))
                _tierPreview.Begin(director, tier, Time.unscaledTime);
            GUILayout.EndHorizontal();
        }
    }

    // 제압 간파 섬광(179 후속 3) — A6 토글 + 허수아비 간파를 제압 간파로 재현하는 토글(호스트에서만 효과).
    // A6 를 켜면 재현도 켠다(허수아비엔 제압 칸이 없어 그대로는 시퀀스가 안 나온다). 끔은 재현을 건드리지 않는다.
    void DrawSuppressFlash()
    {
        InterruptProductionDirector director = InterruptProductionDirector.Instance;
        if (director == null)
            return;

        GUILayout.Space(6f);
        bool on = director.SuppressFlashEnabled;
        bool next = GUILayout.Toggle(on, "제압 간파 섬광 (A6)");
        if (next != on)
        {
            director.SuppressFlashEnabled = next;
            if (next)
                _suppressRepro.Enabled = true;
        }

        // A7 섬광 = 대상 내부만(179/192 후속) — 이 피어 런타임 값만, 다음 재생부터.
        bool targetOnly = director.SuppressFlashTargetOnly;
        if (GUILayout.Button(TargetOnlyLabel(targetOnly)))
            director.SuppressFlashTargetOnly = !targetOnly;

        _suppressRepro.Enabled = GUILayout.Toggle(_suppressRepro.Enabled, "허수아비 간파 = 제압 간파(제압 직전)");
        if (_suppressRepro.Enabled && !DebugSuppressInterruptRepro.IsServer)
            GUILayout.Label("  ※ 호스트 화면에서만 효과(표식은 서버)");
        else if (_suppressRepro.Enabled)
            GUILayout.Label($"  허수아비 {_suppressRepro.DummyCount} · 제압 간파 {_suppressRepro.MarkedCount}회");
    }

    /// <summary>A7 버튼 글자 — 지금 상태 표시(누르면 반대로).</summary>
    public static string TargetOnlyLabel(bool targetOnly) =>
        targetOnly ? "섬광 범위 (A7): 대상 내부만" : "섬광 범위 (A7): 전체 화면";

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

    /// <summary>
    /// 기본값(오너 암전만 켬 — §17-1)으로 되돌린다. 팀원 암순응(A2)도 끈다.
    /// A4 성공 이펙트 칸은 §17 효과 칸이 아니라 켬 유지 스위치라 건드리지 않는다(지금 값 그대로).
    /// </summary>
    public static void SetDefaults(InterruptProductionDirector director)
    {
        foreach (Row row in Rows)
        {
            if (row.IsCue && row.Cue == InterruptProductionCue.SuccessFxSlot)
                continue;

            row.Set(director, !row.IsCue && InterruptPresentationEffectRules.DefaultOn(row.Effect));
        }
    }
}
