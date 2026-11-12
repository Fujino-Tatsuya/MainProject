using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 팔라딘 방패 손잡이 미리보기 — 메뉴 <c>Tools/Player/팔라딘 방패 손잡이 미리보기</c>.
/// [Play 아님] Player_Paladin 프리팹 모드에서 궁(Garen_Ultimate_Skill) 클립을 AnimationMode 로 샘플링(프레임 슬라이더·▶)하고
/// WeaponTransformRelay 보조 고정을 바로 적용 — 손잡이2·ElbowSocket 을 Scene 핸들로 옮기면 즉시 반영, 프리팹에 저장(Undo).
/// 창을 닫거나 '미리보기 끄기' = AnimationMode 종료로 본·방패 자세 원복(애니 포즈는 프리팹에 저장되지 않는다).
/// [Play 중] 런타임 팔라딘의 손잡이2·ElbowSocket 을 핸들로 옮기고 '프리팹에 저장'.
/// </summary>
public sealed class PaladinShieldGripPreviewWindow : EditorWindow
{
    float _time;
    bool _playing;
    bool _align = true;
    double _lastTick;
    AnimationClip _clip;
    GameObject _animRoot;
    WeaponTransformRelay _relay;

    [MenuItem("Tools/Player/팔라딘 방패 손잡이 미리보기")]
    static void Open() => GetWindow<PaladinShieldGripPreviewWindow>("방패 손잡이");

    void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        StopPreview();
    }

    void OnPlayMode(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingEditMode) StopPreview();
    }

    bool Previewing => _relay != null && PaladinShieldGripPreview.Active;

    void OnGUI()
    {
        if (EditorApplication.isPlaying)
        {
            DrawPlayMode();
            return;
        }
        EditorGUILayout.LabelField("프리팹 모드에서 궁 자세를 샘플링해 손잡이2·ElbowSocket 을 맞춘다. 위치는 프리팹에 저장(Ctrl+S), 애니 자세는 끄면 원복.", EditorStyles.wordWrappedMiniLabel);
        if (!Previewing)
        {
            if (GUILayout.Button("Player_Paladin 열고 미리보기 시작")) StartPreview();
            return;
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(_playing ? "❚❚" : "▶", GUILayout.Width(40f)))
            {
                _playing = !_playing;
                _lastTick = EditorApplication.timeSinceStartup;
            }
            float len = _clip != null ? _clip.length : 1f;
            float fps = _clip != null ? _clip.frameRate : 30f;
            EditorGUI.BeginChangeCheck();
            _time = EditorGUILayout.Slider(_time, 0f, len);
            if (EditorGUI.EndChangeCheck()) _playing = false;
            GUILayout.Label($"{Mathf.RoundToInt(_time * fps)}f", GUILayout.Width(44f));
        }
        _align = EditorGUILayout.Toggle(new GUIContent("보정 켜기(손잡이2 → 팔꿈치)", "끄면 손 소켓만 따라간 원래 자세"), _align);
        if (_relay.TryGetSecondary(out _, out Transform handle, out Transform elbow, out float blend))
        {
            EditorGUILayout.ObjectField("손잡이2", handle, typeof(Transform), true);
            EditorGUILayout.ObjectField("ElbowSocket", elbow, typeof(Transform), true);
            EditorGUILayout.LabelField("블렌드(초)", blend.ToString("0.###") + "  — relay 인스펙터에서 고친다");
        }
        if (GUILayout.Button("미리보기 끄기(자세 원복)")) StopPreview();
        Sample();
    }

    void DrawPlayMode()
    {
        _relay = FindRuntimeRelay();
        if (_relay == null || !_relay.TryGetSecondary(out _, out Transform handle, out Transform elbow, out float blend))
        {
            EditorGUILayout.HelpBox("Play 중 팔라딘(보조 고정이 연결된 WeaponTransformRelay)이 없다. 플레이어를 선택하면 그 캐릭터를 쓴다.", MessageType.Info);
            return;
        }
        EditorGUILayout.LabelField($"런타임 — {_relay.transform.root.name}. Scene 핸들로 손잡이2·ElbowSocket 을 옮기고(궁을 쓰며 확인) 저장.", EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.LabelField("손잡이2 로컬", handle.localPosition.ToString("F3"));
        EditorGUILayout.LabelField("ElbowSocket 로컬", elbow.localPosition.ToString("F3"));
        if (GUILayout.Button("프리팹에 저장(Player_Paladin)"))
            PaladinShieldGripAuthoring.SaveGripToPrefab(handle.localPosition, elbow.localPosition, blend);
    }

    static WeaponTransformRelay FindRuntimeRelay()
    {
        GameObject sel = Selection.activeGameObject;
        if (sel != null)
        {
            WeaponTransformRelay r = sel.transform.root.GetComponentInChildren<WeaponTransformRelay>(true);
            if (r != null && r.TryGetSecondary(out _, out _, out _, out _)) return r;
        }
        foreach (WeaponTransformRelay r in Object.FindObjectsByType<WeaponTransformRelay>(FindObjectsSortMode.None))
            if (r.TryGetSecondary(out _, out _, out _, out _)) return r;
        return null;
    }

    void StartPreview()
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PaladinShieldGripAuthoring.PrefabPath);
        if (asset == null) return;
        AssetDatabase.OpenAsset(asset);
        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage == null || stage.prefabContentsRoot == null) return;
        GameObject root = stage.prefabContentsRoot;
        _relay = root.GetComponentInChildren<WeaponTransformRelay>(true);
        Animator animator = root.GetComponentInChildren<Animator>(true);
        _clip = animator != null && animator.runtimeAnimatorController != null
            ? AnimationEventRequirements.FindStateClip(animator.runtimeAnimatorController, PaladinShieldGripAuthoring.UltimateState) : null;
        if (_relay == null || animator == null || _clip == null)
        {
            Debug.LogError($"[PaladinShieldGrip] 미리보기 준비 실패 — relay {_relay != null} · Animator {animator != null} · 궁 클립 {_clip != null}");
            _relay = null;
            return;
        }
        _animRoot = animator.gameObject;
        PaladinShieldGripPreview.Start();
        Sample();
    }

    void StopPreview()
    {
        _playing = false;
        if (_relay != null) _relay.previewWeight = -1f;
        PaladinShieldGripPreview.Stop();
        _relay = null;
        _animRoot = null;
        SceneView.RepaintAll();
    }

    void Tick()
    {
        if (EditorApplication.isPlaying)
        {
            Repaint();
            return;
        }
        if (!Previewing || !_playing || _clip == null) return;
        double now = EditorApplication.timeSinceStartup;
        _time += (float)(now - _lastTick);
        _lastTick = now;
        if (_time > _clip.length) _time = 0f;
        Sample();
        Repaint();
    }

    void Sample()
    {
        if (!Previewing || _animRoot == null) return;
        _relay.TryGetSecondary(out Transform weapon, out _, out _, out _);
        PaladinShieldGripPreview.Sample(_animRoot, _clip, _time, weapon);
        _relay.previewWeight = _align ? 1f : 0f;
        _relay.ApplyNow();
        SceneView.RepaintAll();
    }

    void OnSceneGUI(SceneView view)
    {
        WeaponTransformRelay relay = EditorApplication.isPlaying ? _relay : (Previewing ? _relay : null);
        if (relay == null || !relay.TryGetSecondary(out Transform weapon, out Transform handle, out Transform elbow, out _)) return;

        Vector3 p1 = weapon.position;
        Handles.color = Color.white;
        Handles.SphereHandleCap(0, p1, Quaternion.identity, 0.04f, EventType.Repaint);
        Handles.Label(p1 + Vector3.up * 0.05f, "손잡이1");
        Handles.color = new Color(0.4f, 0.9f, 1f);
        Handles.DrawAAPolyLine(3f, p1, handle.position);
        Handles.SphereHandleCap(0, handle.position, Quaternion.identity, 0.04f, EventType.Repaint);
        Handles.Label(handle.position + Vector3.up * 0.05f, "손잡이2");
        Handles.color = new Color(1f, 0.6f, 0.2f);
        Handles.DrawAAPolyLine(3f, p1, elbow.position);
        Handles.SphereHandleCap(0, elbow.position, Quaternion.identity, 0.04f, EventType.Repaint);
        Handles.Label(elbow.position + Vector3.up * 0.05f, "ElbowSocket");

        MoveHandle(handle, "손잡이2 옮기기");
        MoveHandle(elbow, "ElbowSocket 옮기기");
    }

    void MoveHandle(Transform t, string undo)
    {
        EditorGUI.BeginChangeCheck();
        Vector3 moved = Handles.PositionHandle(t.position, Tools.pivotRotation == PivotRotation.Local ? t.rotation : Quaternion.identity);
        if (!EditorGUI.EndChangeCheck()) return;
        if (!EditorApplication.isPlaying) Undo.RecordObject(t, undo);   // 프리팹 모드 — 저장은 프리팹 저장(Ctrl+S·자동 저장)
        t.position = moved;
        if (!EditorApplication.isPlaying) Sample();
        Repaint();
    }
}

/// <summary>
/// 손잡이 미리보기 샘플링 — AnimationMode 안에서 클립 자세를 샘플링하고, 방패(따라가는 무기) transform 도 AnimationMode 에 등록해
/// 끄면(<see cref="Stop"/>) 본·방패 모두 원래 값으로 돌아가게 한다(프리팹에 애니 포즈가 저장되지 않음).
/// </summary>
public static class PaladinShieldGripPreview
{
    static readonly string[] TransformProps =
    {
        "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
        "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w",
    };

    public static bool Active => AnimationMode.InAnimationMode();

    public static void Start()
    {
        if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
    }

    static Transform s_follower;
    static Vector3 s_followerPos;
    static Quaternion s_followerRot;

    public static void Stop()
    {
        if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
        if (s_follower != null && (s_follower.localPosition != s_followerPos || s_follower.localRotation != s_followerRot))
            s_follower.SetLocalPositionAndRotation(s_followerPos, s_followerRot);   // 보험 — 미리보기 전 방패 값으로
        s_follower = null;
    }

    /// <summary><paramref name="animRoot"/> 에 <paramref name="clip"/> 의 <paramref name="time"/> 자세를 샘플링. <paramref name="follower"/>(방패)는 원복 대상으로 등록만.</summary>
    public static void Sample(GameObject animRoot, AnimationClip clip, float time, Transform follower)
    {
        if (!AnimationMode.InAnimationMode() || animRoot == null || clip == null) return;
        AnimationMode.BeginSampling();
        if (follower != null) Register(follower);
        AnimationMode.SampleAnimationClip(animRoot, clip, time);
        AnimationMode.EndSampling();
    }

    static void Register(Transform t)
    {
        if (s_follower != t)
        {
            s_follower = t;   // 미리보기 전 값을 한 번만 잡는다(샘플마다 relay 가 옮긴 값이 아니라)
            s_followerPos = t.localPosition;
            s_followerRot = t.localRotation;
        }
        Vector3 p = s_followerPos;
        Quaternion r = s_followerRot;
        float[] values = { p.x, p.y, p.z, r.x, r.y, r.z, r.w };
        for (int i = 0; i < TransformProps.Length; i++)
        {
            var binding = EditorCurveBinding.FloatCurve("", typeof(Transform), TransformProps[i]);
            var mod = new PropertyModification
            {
                target = t,
                propertyPath = TransformProps[i],
                value = values[i].ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            AnimationMode.AddPropertyModification(binding, mod, true);
        }
    }
}
