#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System.Collections.Generic;
using BeaverLobby.Player.Dash;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 간파 판정 범위 기즈모(할 일 156) — 간파 보정 랩(InterruptCalibrationLab)에만 둔다. 근사 도형을 만들지 않고 시도 예측 오버랩이 질의하는 바로 그 형상
/// (<see cref="PlayerInterruptSkillBase.TryGetPlacedJudgeShape"/> = 판정 앵커를 공격 방향으로 돌려 판정 위치에 놓은 것)을 그린다.
///
/// <list type="bullet">
/// <item><b>상시 미리보기.</b> 이 피어의 로컬 플레이어(<see cref="Player.IsInputSource"/>)마다 지금 위치·조준 방향(<see cref="PlayerSkillController.CurrentAimDirection"/>
///       = 근접 간파 요청 방향)의 형상. 색 = 오너 예측과 같은 대상 탐색(<see cref="PlayerInterruptSkillBase.FindOwnerViewTarget"/>)의 대상 없음 / 창 닫힘 / 창 열림.</item>
/// <item><b>시도 잔상.</b> 계측 훅(<see cref="PlayerInterruptSkillBase.CalibrationOwnerPredicted"/> 등)이 오는 그 프레임에 그 예측의 판정 위치
///       (<see cref="DashInterruptPrediction.PredictedEndPosition"/> — 근접 = 시전 위치, 대쉬 = 예상 대시 끝)·공격 방향으로 형상을 잡아 몇 초 남긴다.
///       대쉬면 시전 위치 → 예상 대시 끝 선도. 색 = 예측 결과(대상 없음/약/강). 서버 예측은 시전자가 이 피어 로컬이 아닐 때만(호스트에서 남의 시도) 남긴다.</item>
/// <item><b>그리기.</b> Scene 뷰 = OnDrawGizmos, Game 뷰 = 메인 카메라 렌더 뒤 GL 선(Gizmos 버튼 무관). 선분은 매 프레임 같은 리스트를 다시 채운다(메시·텍스처 생성 없음).</item>
/// </list>
/// 전방위 HUD 강조 사거리(<see cref="PlayerInterruptSkillBase.HasTargetInOmniRange"/>)는 판정이 아니라 그리지 않는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class InterruptJudgeShapeGizmos : MonoBehaviour
{
    const float PlayerRefreshInterval = 0.5f;
    const float LabelLift = 2.2f;

    [Tooltip("로컬 플레이어마다 지금 위치·조준 방향의 간파 판정 형상을 늘 그린다.")]
    [SerializeField] bool showPreview = true;

    [Tooltip("간파 시도마다 예측이 실제 쓴 판정 위치·방향의 형상을 잔상으로 남긴다.")]
    [SerializeField] bool showAttempts = true;

    [Tooltip("Game 뷰에도 그린다(GL — Gizmos 버튼 무관). 끄면 Scene 뷰(또는 Game 뷰 Gizmos)만.")]
    [SerializeField] bool drawInGameView = true;

    [Tooltip("시도 잔상이 남는 실시간(초). 시간이 멈춘 동안(보정 질문 창)은 줄지 않는다.")]
    [SerializeField, Min(0.1f)] float afterimageSeconds = InterruptJudgeShapeGizmoRules.DefaultAfterimageSeconds;

    [Tooltip("패널 너비(px). 화면 왼쪽 위.")]
    [SerializeField, Min(200f)] float panelWidth = 280f;

    [Tooltip("패널 위쪽 여백(px).")]
    [SerializeField, Min(0f)] float panelTop = 10f;

    [Tooltip("패널을 펼친 채로 시작")]
    [SerializeField] bool expanded = true;

    struct Afterimage
    {
        public bool HasShape;
        public HitboxAnchorShape Shape;
        public Vector3 CastPosition;
        public Vector3 ResolvePosition;
        public bool ViaDash;
        public bool Repredicted;
        public InterruptAttemptForecast Forecast;
        public string Source;
        public float Age;
    }

    struct Preview
    {
        public HitboxAnchorShape Shape;
        public Vector3 Position;
        public InterruptJudgePreviewState State;
    }

    readonly List<Player> _localPlayers = new List<Player>();
    readonly List<Preview> _previews = new List<Preview>();
    readonly List<Afterimage> _afterimages = new List<Afterimage>();
    readonly List<Vector3> _shapeSegments = new List<Vector3>();
    readonly List<Vector3> _lines = new List<Vector3>(); // 끝점 쌍
    readonly List<Color> _lineColors = new List<Color>(); // 선분마다 하나
    readonly object _hoverGate = new object();

    float _nextPlayerRefresh;
    Material _lineMaterial;
    Rect _panelRect;
    bool _hoverGateHeld;
    string _lastAttempt = "-";

    void OnEnable()
    {
        PlayerInterruptSkillBase.CalibrationOwnerPredicted += HandleOwnerPredicted;
        PlayerInterruptSkillBase.CalibrationOwnerRepredicted += HandleOwnerRepredicted;
        PlayerInterruptSkillBase.CalibrationServerPredicted += HandleServerPredicted;
        PlayerInterruptSkillBase.CalibrationServerRepredicted += HandleServerRepredicted;
        RenderPipelineManager.endCameraRendering += HandleEndCameraRendering;
    }

    void OnDisable()
    {
        PlayerInterruptSkillBase.CalibrationOwnerPredicted -= HandleOwnerPredicted;
        PlayerInterruptSkillBase.CalibrationOwnerRepredicted -= HandleOwnerRepredicted;
        PlayerInterruptSkillBase.CalibrationServerPredicted -= HandleServerPredicted;
        PlayerInterruptSkillBase.CalibrationServerRepredicted -= HandleServerRepredicted;
        RenderPipelineManager.endCameraRendering -= HandleEndCameraRendering;
        SetHoverGate(false);
        _afterimages.Clear();
    }

    void OnDestroy()
    {
        if (_lineMaterial != null)
            Destroy(_lineMaterial);
    }

    // ─── 시도 잔상 ──────────────────────────────────────────────────────

    void HandleOwnerPredicted(PlayerInterruptSkillBase.CalibrationPrediction p) => Record(p, "오너", false);
    void HandleOwnerRepredicted(PlayerInterruptSkillBase.CalibrationPrediction p) => Record(p, "오너", true);

    // 호스트에서 내 시도는 오너 훅이 이미 남겼다 — 서버 훅은 남(원격 클라)의 시도만.
    void HandleServerPredicted(PlayerInterruptSkillBase.CalibrationPrediction p)
    {
        if (!IsLocalCaster(p.Skill))
            Record(p, "서버", false);
    }

    void HandleServerRepredicted(PlayerInterruptSkillBase.CalibrationPrediction p)
    {
        if (!IsLocalCaster(p.Skill))
            Record(p, "서버", true);
    }

    static bool IsLocalCaster(PlayerInterruptSkillBase skill)
    {
        Player player = skill != null ? skill.GetComponentInParent<Player>() : null;
        return player != null && player.IsInputSource;
    }

    // 훅은 예측과 같은 프레임에 온다 — 같은 판정 위치·공격 방향·시전자 위치로 예측 오버랩과 같은 형상을 잡는다.
    void Record(in PlayerInterruptSkillBase.CalibrationPrediction p, string source, bool repredicted)
    {
        var a = new Afterimage
        {
            CastPosition = p.Input.CastPosition,
            ResolvePosition = p.Prediction.PredictedEndPosition,
            ViaDash = p.ViaDash,
            Repredicted = repredicted,
            Forecast = p.Prediction.Forecast,
            Source = source,
        };
        a.HasShape = p.Skill != null && p.Skill.TryGetPlacedJudgeShape(a.ResolvePosition, p.AttackDirection, out a.Shape);

        if (_afterimages.Count >= InterruptJudgeShapeGizmoRules.MaxAfterimages)
            _afterimages.RemoveAt(0);
        _afterimages.Add(a);
        _lastAttempt = Describe(a);
    }

    static string Describe(in Afterimage a) =>
        $"{a.Source} {(a.ViaDash ? "대쉬" : "근접")}{(a.Repredicted ? " 재예측" : "")} · " +
        $"{InterruptJudgeShapeGizmoRules.DescribeForecast(a.Forecast)}" +
        (a.ViaDash ? $" · 대시 {Flat(a.ResolvePosition - a.CastPosition).magnitude:0.00}m" : "");

    // ─── 갱신 ───────────────────────────────────────────────────────────

    void Update()
    {
        for (int i = _afterimages.Count - 1; i >= 0; i--)
        {
            Afterimage a = _afterimages[i];
            a.Age = InterruptJudgeShapeGizmoRules.AdvanceAge(a.Age, Time.timeScale, Time.unscaledDeltaTime);
            if (InterruptJudgeShapeGizmoRules.IsExpired(a.Age, afterimageSeconds))
                _afterimages.RemoveAt(i);
            else
                _afterimages[i] = a;
        }

        UpdateHoverGate();
    }

    void LateUpdate()
    {
        UpdatePreviews();
        BuildLines();
    }

    void UpdatePreviews()
    {
        _previews.Clear();
        if (!showPreview)
            return;

        if (Time.realtimeSinceStartup >= _nextPlayerRefresh)
        {
            _nextPlayerRefresh = Time.realtimeSinceStartup + PlayerRefreshInterval;
            _localPlayers.Clear();
            foreach (Player player in FindObjectsByType<Player>(FindObjectsSortMode.None))
            {
                if (player != null && player.IsInputSource)
                    _localPlayers.Add(player);
            }
        }

        foreach (Player player in _localPlayers)
        {
            if (player == null || !player.TryGetComponent(out PlayerSkillController controller) ||
                !(controller.GetSkill(PlayerSkillSlot.Interrupt) is PlayerInterruptSkillBase skill))
                continue;

            Vector3 position = player.transform.position;
            Vector3 direction = controller.CurrentAimDirection;
            if (!skill.TryGetPlacedJudgeShape(position, direction, out HitboxAnchorShape shape))
                continue;

            Unit target = skill.FindOwnerViewTarget(position, direction, out bool open);
            _previews.Add(new Preview
            {
                Shape = shape,
                Position = position,
                State = InterruptJudgeShapeGizmoRules.PreviewStateOf(target != null, open),
            });
        }
    }

    void BuildLines()
    {
        _lines.Clear();
        _lineColors.Clear();

        foreach (Preview preview in _previews)
            AddShape(preview.Shape, InterruptJudgeShapeGizmoRules.ForPreview(preview.State));

        if (!showAttempts)
            return;

        foreach (Afterimage a in _afterimages)
        {
            Color color = InterruptJudgeShapeGizmoRules.WithAlphaScale(InterruptJudgeShapeGizmoRules.ForForecast(a.Forecast),
                InterruptJudgeShapeGizmoRules.AfterimageAlpha(a.Age, afterimageSeconds));
            if (a.HasShape)
                AddShape(a.Shape, color);
            if (a.ViaDash)
                AddLine(a.CastPosition, a.ResolvePosition, color);
        }
    }

    void AddShape(in HitboxAnchorShape shape, Color color)
    {
#if UNITY_EDITOR
        _shapeSegments.Clear();
        AttackShapeDrawing.CollectAnchorSegments(shape, _shapeSegments);
        for (int i = 0; i + 1 < _shapeSegments.Count; i += 2)
            AddLine(_shapeSegments[i], _shapeSegments[i + 1], color);
#endif
    }

    void AddLine(Vector3 a, Vector3 b, Color color)
    {
        _lines.Add(a);
        _lines.Add(b);
        _lineColors.Add(color);
    }

    // ─── 그리기 ─────────────────────────────────────────────────────────

    void OnDrawGizmos()
    {
        if (!Application.isPlaying)
            return;

        Color previous = Gizmos.color;
        for (int i = 0; i < _lineColors.Count; i++)
        {
            Gizmos.color = _lineColors[i];
            Gizmos.DrawLine(_lines[i * 2], _lines[i * 2 + 1]);
        }
        Gizmos.color = previous;

#if UNITY_EDITOR
        foreach (Preview preview in _previews)
            Handles.Label(preview.Position + Vector3.up * LabelLift,
                "간파 판정 " + InterruptJudgeShapeGizmoRules.DescribePreview(preview.State));
        if (showAttempts)
        {
            foreach (Afterimage a in _afterimages)
                Handles.Label(a.ResolvePosition + Vector3.up * (LabelLift + 0.4f), Describe(a));
        }
#endif
    }

    // Game 뷰 — 메인 카메라 렌더가 끝난 뒤 GL 선(깊이 무시). 오버레이·UI 카메라는 건너뛴다.
    void HandleEndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (!drawInGameView || _lineColors.Count == 0 || camera == null || camera.cameraType != CameraType.Game ||
            camera != Camera.main || !EnsureLineMaterial())
            return;

        GL.PushMatrix();
        GL.LoadProjectionMatrix(camera.projectionMatrix);
        GL.modelview = camera.worldToCameraMatrix;
        _lineMaterial.SetPass(0);
        GL.Begin(GL.LINES);
        for (int i = 0; i < _lineColors.Count; i++)
        {
            GL.Color(_lineColors[i]);
            GL.Vertex(_lines[i * 2]);
            GL.Vertex(_lines[i * 2 + 1]);
        }
        GL.End();
        GL.PopMatrix();
    }

    bool EnsureLineMaterial()
    {
        if (_lineMaterial != null)
            return true;

        Shader shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
            return false;

        _lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        _lineMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        _lineMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        _lineMaterial.SetInt("_Cull", (int)CullMode.Off);
        _lineMaterial.SetInt("_ZWrite", 0);
        _lineMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
        return true;
    }

    // ─── 패널 ───────────────────────────────────────────────────────────

    void OnGUI()
    {
        float height = expanded ? 300f : 30f;
        _panelRect = new Rect(10f, panelTop, panelWidth, height);
        GUILayout.BeginArea(_panelRect);
        GUILayout.BeginVertical(GUI.skin.box);

        if (GUILayout.Button(expanded ? "▼ 간파 판정 범위(156)" : "▶ 간파 판정 범위(156)"))
            expanded = !expanded;

        if (expanded)
        {
            showPreview = GUILayout.Toggle(showPreview, "상시 미리보기 (지금 위치·조준 방향)");
            showAttempts = GUILayout.Toggle(showAttempts, "시도 잔상 (예측이 쓴 판정 위치)");
            drawInGameView = GUILayout.Toggle(drawInGameView, "Game 뷰에도 그리기");

            GUILayout.BeginHorizontal();
            GUILayout.Label($"잔상 {afterimageSeconds:0.0}s", GUILayout.Width(70f));
            afterimageSeconds = Mathf.Max(0.1f, Mathf.Round(GUILayout.HorizontalSlider(afterimageSeconds, 0.5f, 10f) * 2f) / 2f);
            GUILayout.EndHorizontal();

            GUILayout.Label("미리보기:");
            Legend(InterruptJudgeShapeGizmoRules.PreviewNoTargetColor, "대상 없음");
            Legend(InterruptJudgeShapeGizmoRules.PreviewClosedColor, "대상 창 닫힘");
            Legend(InterruptJudgeShapeGizmoRules.PreviewOpenColor, "대상 창 열림");
            GUILayout.Label("시도 예측:");
            Legend(InterruptJudgeShapeGizmoRules.AttemptNoTargetColor, "대상 없음");
            Legend(InterruptJudgeShapeGizmoRules.AttemptWeakColor, "약");
            Legend(InterruptJudgeShapeGizmoRules.AttemptStrongColor, "강");

            GUILayout.Label("마지막 시도: " + _lastAttempt);
            if (GUILayout.Button("잔상 지우기"))
                _afterimages.Clear();
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    static void Legend(Color color, string label)
    {
        Color previous = GUI.contentColor;
        GUI.contentColor = new Color(color.r, color.g, color.b, 1f);
        GUILayout.Label("■ " + label);
        GUI.contentColor = previous;
    }

    // 패널 위에서는 로컬 플레이어 입력을 막는다 — 토글 클릭이 공격·간파로 새지 않게(보정 질문 패널과 같은 방식).
    void UpdateHoverGate()
    {
        Mouse mouse = Mouse.current;
        bool hover = false;
        bool anyMouseHeld = false;
        if (mouse != null)
        {
            Vector2 p = mouse.position.ReadValue();
            hover = _panelRect.Contains(new Vector2(p.x, Screen.height - p.y));
            anyMouseHeld = mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed;
        }
        SetHoverGate(hover || (_hoverGateHeld && anyMouseHeld));
    }

    void SetHoverGate(bool on)
    {
        if (on == _hoverGateHeld)
            return;
        _hoverGateHeld = on;
        if (on)
            UiInputGateManager.Acquire(_hoverGate);
        else
            UiInputGateManager.Release(_hoverGate);
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
#endif
