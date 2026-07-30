// ----------------------------------------------------------------------------
//  FogPainterWindow.cs - 포그 페인트 마스크 에디터 툴
//  씬뷰에서 XZ 평면에 브러시로 칠해 포그를 추가/제거/색칠한다.
//  마스크는 RGBA32 Texture2D(.asset): A=밀도(중립 0.5), RGB=틴트.
//  순수 에디터 도구 — 런타임/네트워크 로직 없음.
// ----------------------------------------------------------------------------
using UnityEditor;
using UnityEngine;

public sealed class FogPainterWindow : EditorWindow
{
    private enum BrushMode { AddFog, EraseFog, PaintColor }

    private FogManager _manager;
    private Texture2D _mask;
    private Color32[] _pixels;
    private int _w, _h;

    private bool _painting;
    private BrushMode _mode = BrushMode.AddFog;
    private float _brushSize = 10f;       // 월드 단위 지름
    [Range(0f, 1f)] private float _strength = 0.5f;
    private Color _color = new Color(0.7f, 0.5f, 0.9f, 1f);
    private int _newMaskRes = 512;
    private bool _dirty;

    [MenuItem("Window/Rendering/Fog Painter")]
    private static void Open() => GetWindow<FogPainterWindow>("Fog Painter");

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        TryAutoFind();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        FlushIfDirty();
    }

    private void TryAutoFind()
    {
        if (_manager == null)
#if UNITY_2023_1_OR_NEWER
            _manager = FindAnyObjectByType<FogManager>();
#else
            _manager = FindObjectOfType<FogManager>();
#endif
        if (_manager != null && _manager.maskTexture != null && _mask != _manager.maskTexture)
            LoadMask(_manager.maskTexture);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("타깃", EditorStyles.boldLabel);
        _manager = (FogManager)EditorGUILayout.ObjectField("Fog Manager", _manager, typeof(FogManager), true);
        if (_manager == null)
        {
            EditorGUILayout.HelpBox("씬에 FogManager 가 필요합니다. (GameObject + Fog Manager 컴포넌트)", MessageType.Info);
            if (GUILayout.Button("씬에서 자동 탐색")) TryAutoFind();
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("마스크", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        Texture2D newMask = (Texture2D)EditorGUILayout.ObjectField("Mask Texture", _manager.maskTexture, typeof(Texture2D), false);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_manager, "Assign Fog Mask");
            _manager.maskTexture = newMask;
            if (newMask != null) { _manager.maskEnabled = true; LoadMask(newMask); }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            _newMaskRes = EditorGUILayout.IntPopup("New Res", _newMaskRes,
                new[] { "256", "512", "1024", "2048" }, new[] { 256, 512, 1024, 2048 });
            if (GUILayout.Button("Create New Mask"))
                CreateNewMask();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("마스크 월드 영역", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        Vector3 c = EditorGUILayout.Vector3Field("Center", _manager.maskCenter);
        Vector2 s = EditorGUILayout.Vector2Field("Size (X,Z)", _manager.maskSize);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_manager, "Edit Fog Mask Area");
            _manager.maskCenter = c;
            _manager.maskSize = s;
        }

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(_mask == null))
        {
            _painting = GUILayout.Toggle(_painting, _painting ? "● 페인팅 중 (클릭해서 끄기)" : "○ 페인팅 시작", "Button");
            _mode = (BrushMode)EditorGUILayout.EnumPopup("Brush", _mode);
            _brushSize = EditorGUILayout.Slider("Brush Size (world)", _brushSize, 0.5f, 100f);
            _strength = EditorGUILayout.Slider("Strength", _strength, 0.01f, 1f);
            if (_mode == BrushMode.PaintColor)
                _color = EditorGUILayout.ColorField("Color", _color);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Fill Neutral (no change)")) FillAll(new Color32(0, 0, 0, 128));
                if (GUILayout.Button("Clear (no fog)")) FillAll(new Color32(0, 0, 0, 0));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Fill Full Fog")) FillAll(new Color32(0, 0, 0, 255));
                if (GUILayout.Button("Save Mask")) { FlushIfDirty(); AssetDatabase.SaveAssets(); }
            }
        }

        if (_mask != null)
            EditorGUILayout.HelpBox($"마스크: {_mask.name}  {_w}x{_h}\nAdd=포그 추가 / Erase=포그 제거 / Paint Color=색칠.\n칠한 뒤 Save Mask 로 저장.", MessageType.None);
    }

    // ----------------------------------------------------------------------
    private void OnSceneGUI(SceneView sv)
    {
        if (!_painting || _manager == null || _mask == null || _pixels == null)
            return;

        Event e = Event.current;
        int id = GUIUtility.GetControlID(FocusType.Passive);
        HandleUtility.AddDefaultControl(id);

        // 브러시 프리뷰
        Plane plane = new Plane(Vector3.up, new Vector3(0f, _manager.maskCenter.y, 0f));
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (plane.Raycast(ray, out float enter))
        {
            Vector3 hit = ray.GetPoint(enter);
            Handles.color = _mode == BrushMode.EraseFog ? Color.red :
                            _mode == BrushMode.PaintColor ? _color : Color.cyan;
            Handles.DrawWireDisc(hit, Vector3.up, _brushSize * 0.5f);
            sv.Repaint();

            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && !e.alt)
            {
                PaintAt(hit);
                e.Use();
            }
        }

        if (e.type == EventType.MouseUp && e.button == 0)
            FlushIfDirty();
    }

    private void PaintAt(Vector3 worldHit)
    {
        Vector3 center = _manager.maskCenter;
        Vector2 size = _manager.maskSize;
        if (size.x < 1e-4f || size.y < 1e-4f) return;

        // 월드 → UV
        float minX = center.x - size.x * 0.5f;
        float minZ = center.z - size.y * 0.5f;
        float u = (worldHit.x - minX) / size.x;
        float v = (worldHit.z - minZ) / size.y;

        float radiusU = (_brushSize * 0.5f) / size.x;
        float radiusV = (_brushSize * 0.5f) / size.y;

        int cx = Mathf.RoundToInt(u * _w);
        int cy = Mathf.RoundToInt(v * _h);
        int rx = Mathf.Max(1, Mathf.CeilToInt(radiusU * _w));
        int ry = Mathf.Max(1, Mathf.CeilToInt(radiusV * _h));

        int x0 = Mathf.Clamp(cx - rx, 0, _w - 1);
        int x1 = Mathf.Clamp(cx + rx, 0, _w - 1);
        int y0 = Mathf.Clamp(cy - ry, 0, _h - 1);
        int y1 = Mathf.Clamp(cy + ry, 0, _h - 1);

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float nx = (x - cx) / (float)rx;
                float ny = (y - cy) / (float)ry;
                float d = Mathf.Sqrt(nx * nx + ny * ny);
                if (d > 1f) continue;

                float w = Mathf.SmoothStep(1f, 0f, d) * _strength;
                int idx = y * _w + x;
                Color cur = _pixels[idx];

                switch (_mode)
                {
                    case BrushMode.AddFog:
                        cur.a = Mathf.Lerp(cur.a, 1f, w);
                        break;
                    case BrushMode.EraseFog:
                        cur.a = Mathf.Lerp(cur.a, 0f, w);
                        break;
                    case BrushMode.PaintColor:
                        cur.r = Mathf.Lerp(cur.r, _color.r, w);
                        cur.g = Mathf.Lerp(cur.g, _color.g, w);
                        cur.b = Mathf.Lerp(cur.b, _color.b, w);
                        cur.a = Mathf.Max(cur.a, Mathf.Lerp(cur.a, 0.75f, w)); // 색 보이게 약간의 포그
                        break;
                }
                _pixels[idx] = cur;
            }
        }

        _mask.SetPixels32(_pixels);
        _mask.Apply(false);
        _dirty = true;
    }

    // ----------------------------------------------------------------------
    private void LoadMask(Texture2D tex)
    {
        _mask = tex;
        if (_mask == null) { _pixels = null; return; }
        _w = _mask.width;
        _h = _mask.height;
        try
        {
            _pixels = _mask.GetPixels32();
        }
        catch
        {
            _pixels = null;
            Debug.LogWarning("[FogPainter] 마스크 텍스처가 읽기 불가입니다. 'Create New Mask' 로 만든 .asset 마스크를 쓰거나, 임포트 설정에서 Read/Write 를 켜세요.");
        }
    }

    private void CreateNewMask()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "Create Fog Mask", "FogMask", "asset", "포그 마스크 텍스처를 저장할 위치");
        if (string.IsNullOrEmpty(path)) return;

        var tex = new Texture2D(_newMaskRes, _newMaskRes, TextureFormat.RGBA32, false, false)
        {
            name = System.IO.Path.GetFileNameWithoutExtension(path),
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        var fill = new Color32[_newMaskRes * _newMaskRes];
        var neutral = new Color32(0, 0, 0, 128); // 중립(밀도 변화 없음)
        for (int i = 0; i < fill.Length; i++) fill[i] = neutral;
        tex.SetPixels32(fill);
        tex.Apply(false);

        AssetDatabase.CreateAsset(tex, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Undo.RecordObject(_manager, "Create Fog Mask");
        _manager.maskTexture = tex;
        _manager.maskEnabled = true;
        LoadMask(tex);
    }

    private void FillAll(Color32 value)
    {
        if (_mask == null || _pixels == null) return;
        for (int i = 0; i < _pixels.Length; i++) _pixels[i] = value;
        _mask.SetPixels32(_pixels);
        _mask.Apply(false);
        _dirty = true;
        FlushIfDirty();
    }

    private void FlushIfDirty()
    {
        if (!_dirty || _mask == null) return;
        EditorUtility.SetDirty(_mask);
        _dirty = false;
    }
}
