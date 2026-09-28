// 투명화 그룹 툴 — 씬 뷰 "선택 모드"(페인트).
//
// 켜면 씬 뷰의 클릭·드래그가 오브젝트 선택 대신 **선택된 그룹에 담기**로 바뀐다.
// 드래그로 쓸고 지나간 자리의 오브젝트가 전부 들어간다. Ctrl 을 누르고 있으면 뺀다.
//
// 설계상 정해 둔 것:
//  - 피킹은 `HandleUtility.PickGameObject` — 콜라이더가 없어도 렌더러 기준으로 잡힌다.
//    벽에 콜라이더가 없는 경우가 있어 `Physics.Raycast` 는 쓸 수 없다.
//  - 마우스 이동 이벤트는 픽셀을 건너뛴다. 빠르게 그으면 중간이 빠지므로 **경로를 보간**한다.
//  - 드래그 한 번이 Undo 한 번이다. 안 묶으면 Ctrl+Z 를 수십 번 눌러야 한다.
//  - Alt 는 건드리지 않는다 — 씬 뷰 카메라 오비트라 빼앗으면 화면을 못 돌린다.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VeyTrace.Rendering.Occlusion.Editor
{
    [InitializeOnLoad]
    static class TransparentGroupPaintTool
    {
        /// <summary>드래그 경로 보간 간격(픽셀). 촘촘할수록 안 놓치지만 피킹 횟수가 는다.</summary>
        const float k_StepPixels = 6f;

        /// <summary>한 드래그에서 이미 처리한 오브젝트. 같은 것을 반복해서 담지 않는다.</summary>
        static readonly HashSet<int> s_StrokeTouched = new HashSet<int>();

        static bool s_Painting;
        static int s_UndoGroup;
        static Vector2 s_LastMousePosition;
        static GameObject s_Hover;

        static TransparentGroupPaintTool()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        static void OnSceneGui(SceneView sceneView)
        {
            if (!TransparentGroupSession.PaintMode) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!TransparentGroupSession.TryGetPaintTarget(out _, out var set, out var group)) return;

            // 이 컨트롤을 기본값으로 만들어 씬 뷰의 기본 클릭 선택과 이동 기즈모를 가져온다.
            var controlId = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(controlId);

            var e = Event.current;
            var removing = e.control || e.command;

            switch (e.type)
            {
                case EventType.MouseMove:
                    UpdateHover(e.mousePosition, sceneView);
                    break;

                case EventType.MouseDown when e.button == 0 && !e.alt:
                    BeginStroke(e.mousePosition, set, group, removing);
                    GUIUtility.hotControl = controlId;
                    e.Use();
                    break;

                case EventType.MouseDrag when e.button == 0 && !e.alt && s_Painting:
                    ContinueStroke(e.mousePosition, set, group, removing);
                    e.Use();
                    break;

                case EventType.MouseUp when e.button == 0 && s_Painting:
                    EndStroke();
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;

                case EventType.Repaint:
                    DrawHover(removing);
                    DrawHud(sceneView, group, removing);
                    break;

                // 모드를 끄지 않고도 빠져나갈 수 있게. 드래그 중이면 먼저 정리한다.
                case EventType.KeyDown when e.keyCode == KeyCode.Escape:
                    if (s_Painting) EndStroke();
                    TransparentGroupSession.PaintMode = false;
                    e.Use();
                    break;
            }
        }

        // ── 스트로크 ────────────────────────────────────────────────────

        static void BeginStroke(Vector2 position, TransparentGroupSet set, GroupData group, bool removing)
        {
            s_Painting = true;
            s_StrokeTouched.Clear();
            s_LastMousePosition = position;

            // 드래그 전체를 Undo 하나로 묶는다. EndStroke 에서 접는다.
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(removing ? "Unpaint Transparency Group" : "Paint Transparency Group");
            s_UndoGroup = Undo.GetCurrentGroup();

            Apply(CollectAt(position), set, group, removing);
        }

        static void ContinueStroke(Vector2 position, TransparentGroupSet set, GroupData group, bool removing)
        {
            var picked = CollectAlong(s_LastMousePosition, position);
            s_LastMousePosition = position;
            Apply(picked, set, group, removing);
        }

        static void EndStroke()
        {
            s_Painting = false;
            s_StrokeTouched.Clear();
            Undo.CollapseUndoOperations(s_UndoGroup);
        }

        static void Apply(List<GameObject> picked, TransparentGroupSet set, GroupData group, bool removing)
        {
            if (picked.Count == 0) return;

            // 이벤트당 한 번만 호출한다. 오브젝트마다 부르면 그만큼 JSON 을 다시 쓴다.
            if (removing) set.RemoveMembers(group, picked);
            else set.AddMembers(group, picked);
        }

        // ── 피킹 ────────────────────────────────────────────────────────

        static List<GameObject> CollectAt(Vector2 position)
        {
            var result = new List<GameObject>();
            AddPick(position, result);
            return result;
        }

        /// <summary>
        /// 두 지점 사이를 <see cref="k_StepPixels"/> 간격으로 나눠 전부 피킹한다.
        /// 마우스를 빠르게 움직였을 때 중간 오브젝트가 통째로 빠지는 것을 막는다.
        /// </summary>
        static List<GameObject> CollectAlong(Vector2 from, Vector2 to)
        {
            var result = new List<GameObject>();
            var distance = Vector2.Distance(from, to);
            var steps = Mathf.Max(1, Mathf.CeilToInt(distance / k_StepPixels));

            for (var i = 1; i <= steps; i++)
            {
                AddPick(Vector2.Lerp(from, to, i / (float)steps), result);
            }
            return result;
        }

        static void AddPick(Vector2 position, List<GameObject> into)
        {
            // selectPrefabRoot: false — 눈에 보이는 그 오브젝트를 집는다.
            // true 면 씬에서 존 프리팹 루트가 통째로 잡혀 벽 하나를 고를 수 없다.
            var go = HandleUtility.PickGameObject(position, selectPrefabRoot: false);
            if (go == null) return;
            if (!s_StrokeTouched.Add(go.GetInstanceID())) return;
            into.Add(go);
        }

        static void UpdateHover(Vector2 position, SceneView sceneView)
        {
            var next = HandleUtility.PickGameObject(position, selectPrefabRoot: false);
            if (ReferenceEquals(next, s_Hover)) return;
            s_Hover = next;
            sceneView.Repaint();
        }

        // ── 그리기 ──────────────────────────────────────────────────────

        static void DrawHover(bool removing)
        {
            if (s_Hover == null) return;
            Handles.DrawOutline(new[] { s_Hover }, removing ? Color.red : Color.white);
        }

        static void DrawHud(SceneView sceneView, GroupData group, bool removing)
        {
            Handles.BeginGUI();
            var text = removing
                ? $"선택 모드 — 제거:  {group.name}"
                : $"선택 모드 — 추가:  {group.name}\n(Ctrl 누르고 드래그하면 제거 · Esc 로 종료)";

            var style = new GUIStyle(EditorStyles.helpBox) { fontSize = 11, alignment = TextAnchor.UpperLeft };
            var size = style.CalcSize(new GUIContent(text));
            var rect = new Rect(8f, sceneView.position.height - size.y - 28f, size.x + 8f, size.y + 4f);

            var previous = GUI.backgroundColor;
            GUI.backgroundColor = removing ? new Color(1f, 0.6f, 0.6f) : group.color;
            GUI.Label(rect, text, style);
            GUI.backgroundColor = previous;
            Handles.EndGUI();
        }
    }
}
