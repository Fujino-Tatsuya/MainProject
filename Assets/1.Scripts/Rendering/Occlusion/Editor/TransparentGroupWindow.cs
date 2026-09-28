// 투명화 그룹 툴 — 창.
// PLAN-transparent-group-tool.md 결정 22, 그리고 조작 목록(Q12).
//
// 이 창은 값을 편집하지 않는다. 그룹을 고르면 Unity Selection 에 밀어 넣고,
// 값 편집은 기본 다중 오브젝트 인스펙터가 한다(결정 3). 여기서 하는 일은
// "무엇이 한 묶음인가" 를 만들고, 보여주고, 색을 주는 것뿐이다.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VeyTrace.Rendering.Occlusion.Editor
{
    public sealed class TransparentGroupWindow : EditorWindow
    {
        const string k_MenuPath = "Tools/Group Painter";

        Vector2 m_Scroll;
        GroupData m_RenameTarget;
        string m_RenameBuffer;

        [MenuItem(k_MenuPath)]
        static void Open()
        {
            var window = GetWindow<TransparentGroupWindow>();
            window.titleContent = new GUIContent("Group Painter");
            window.minSize = new Vector2(360f, 240f);
            window.Show();
        }

        void OnEnable()
        {
            TransparentGroupSession.Changed += Repaint;
            Selection.selectionChanged += Repaint;
            TransparentGroupSession.RefreshContexts();
        }

        void OnDisable()
        {
            TransparentGroupSession.Changed -= Repaint;
            Selection.selectionChanged -= Repaint;
        }

        void OnGUI()
        {
            DrawToolbar();

            var contexts = TransparentGroupSession.Contexts;
            if (contexts.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "그룹을 담을 대상이 없다.\n\n" +
                    "저장된 씬을 열거나 프리팹 편집 모드로 들어가라. " +
                    "한 번도 저장하지 않은 씬은 GUID 가 없어 그룹을 붙일 수 없다.",
                    MessageType.Info);
                return;
            }

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            for (var i = 0; i < contexts.Count; i++) DrawContext(contexts[i]);
            EditorGUILayout.EndScrollView();

            DrawUnsavedSelectionWarning();
        }

        // ── 툴바 ────────────────────────────────────────────────────────

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                TransparentGroupSession.OverlayEnabled = GUILayout.Toggle(
                    TransparentGroupSession.OverlayEnabled,
                    new GUIContent("씬 뷰 표시", "그룹 색으로 씬 뷰를 칠한다. 플레이 모드에서는 자동으로 꺼진다."),
                    EditorStyles.toolbarButton, GUILayout.Width(80f));

                // 대상이 모호해지지 않도록 그룹이 정확히 하나 선택됐을 때만 켤 수 있다.
                using (new EditorGUI.DisabledScope(!TransparentGroupSession.CanPaint))
                {
                    var paintTooltip = TransparentGroupSession.CanPaint
                        ? "씬 뷰 클릭·드래그가 오브젝트 선택 대신 이 그룹에 담기로 바뀐다.\n" +
                          "드래그로 쓸고 지나간 오브젝트가 전부 들어간다. Ctrl 을 누르고 드래그하면 뺀다. Esc 로 종료."
                        : "그룹을 정확히 하나만 선택해야 켤 수 있다. (지금 " +
                          $"{CountSelectedGroups()}개 선택됨)";

                    TransparentGroupSession.PaintMode = GUILayout.Toggle(
                        TransparentGroupSession.PaintMode,
                        new GUIContent("선택 모드", paintTooltip),
                        EditorStyles.toolbarButton, GUILayout.Width(70f));
                }

                using (new EditorGUI.DisabledScope(!TransparentGroupSession.OverlayEnabled))
                {
                    GUILayout.Label("색 세기", EditorStyles.miniLabel, GUILayout.Width(44f));
                    TransparentGroupSession.Blend = GUILayout.HorizontalSlider(
                        TransparentGroupSession.Blend, 0f, 1f, GUILayout.Width(70f));

                    GUILayout.Label("배경 채도", EditorStyles.miniLabel, GUILayout.Width(54f));
                    TransparentGroupSession.SceneSaturation = GUILayout.HorizontalSlider(
                        TransparentGroupSession.SceneSaturation, 0f, 1f, GUILayout.Width(70f));
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button(new GUIContent("새로고침", "열린 씬/프리팹을 다시 읽는다."),
                        EditorStyles.toolbarButton))
                {
                    TransparentGroupSession.RefreshContexts();
                }
            }
        }

        // ── 컨텍스트 한 덩어리 ──────────────────────────────────────────

        void DrawContext(GroupContext context)
        {
            var set = TransparentGroupSession.GetSet(context);
            if (set == null) return;

            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                var icon = context.Kind == GroupContextKind.Prefab ? "Prefab Icon" : "SceneAsset Icon";
                GUILayout.Label(EditorGUIUtility.IconContent(icon), GUILayout.Width(20f), GUILayout.Height(18f));
                GUILayout.Label(context.Name, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(Selection.gameObjects.Length == 0))
                {
                    if (GUILayout.Button(
                            new GUIContent("선택으로 새 그룹", "지금 선택한 오브젝트들로 그룹을 만든다."),
                            GUILayout.Width(110f)))
                    {
                        var group = set.CreateGroup(null, Selection.gameObjects);
                        TransparentGroupSession.SelectOnly(group);
                    }
                }
            }

            if (set.Groups.Count == 0)
            {
                EditorGUILayout.LabelField("   그룹 없음", EditorStyles.miniLabel);
                return;
            }

            // 삭제는 순회 뒤로 미룬다.
            GroupData doomed = null;
            foreach (var group in set.Groups)
            {
                if (DrawGroupRow(context, set, group)) doomed = group;
            }
            if (doomed != null) set.Delete(doomed);
        }

        /// <returns>이 그룹을 삭제해야 하면 true.</returns>
        bool DrawGroupRow(GroupContext context, TransparentGroupSet set, GroupData group)
        {
            var missing = CountMissing(context, group);
            var deleteRequested = false;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var selected = TransparentGroupSession.IsSelected(group);
                    var nowSelected = GUILayout.Toggle(selected, GUIContent.none, GUILayout.Width(16f));
                    if (nowSelected != selected)
                    {
                        if (Event.current.control || Event.current.command) TransparentGroupSession.ToggleSelected(group);
                        else if (nowSelected) TransparentGroupSession.SelectOnly(group);
                        else TransparentGroupSession.ClearSelection();
                    }

                    var newColor = EditorGUILayout.ColorField(
                        GUIContent.none, group.color, showEyedropper: false, showAlpha: false, hdr: false,
                        GUILayout.Width(40f));
                    if (newColor != group.color) set.SetColor(group, newColor);

                    DrawName(set, group);

                    GUILayout.Label($"{group.members.Count}개", EditorStyles.miniLabel, GUILayout.Width(40f));

                    if (missing > 0)
                    {
                        var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.red } };
                        GUILayout.Label(new GUIContent($"유실 {missing}",
                            "멤버를 찾지 못했다. 삭제됐거나 이름·위치가 바뀌었다. 항목은 지우지 않고 남겨 둔다."),
                            style, GUILayout.Width(52f));
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(20f);

                    using (new EditorGUI.DisabledScope(Selection.gameObjects.Length == 0))
                    {
                        if (GUILayout.Button(new GUIContent("+ 추가", "지금 선택한 오브젝트를 이 그룹에 넣는다.")))
                            set.AddMembers(group, Selection.gameObjects);

                        if (GUILayout.Button(new GUIContent("− 제거", "지금 선택한 오브젝트를 이 그룹에서 뺀다.")))
                            set.RemoveMembers(group, Selection.gameObjects);
                    }

                    using (new EditorGUI.DisabledScope(missing == 0))
                    {
                        if (GUILayout.Button(new GUIContent("정리", "유실된 멤버만 걷어낸다.")))
                        {
                            var removed = set.RemoveMissingMembers(group);
                            Debug.Log($"[GroupPainter] '{group.name}' 에서 유실 멤버 {removed}개를 정리했다.");
                        }
                    }

                    if (GUILayout.Button(new GUIContent("삭제", "그룹을 지운다. Ctrl+Z 로 되돌릴 수 있다.")))
                        deleteRequested = true;
                }
            }

            return deleteRequested;
        }

        void DrawName(TransparentGroupSet set, GroupData group)
        {
            if (m_RenameTarget == group)
            {
                GUI.SetNextControlName("rename");
                m_RenameBuffer = EditorGUILayout.TextField(m_RenameBuffer);

                var commit = Event.current.type == EventType.KeyDown &&
                             (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
                if (commit || GUILayout.Button("확인", GUILayout.Width(36f)))
                {
                    set.Rename(group, m_RenameBuffer);
                    m_RenameTarget = null;
                    GUI.FocusControl(null);
                }
                return;
            }

            if (GUILayout.Button(group.name, EditorStyles.label))
            {
                m_RenameTarget = group;
                m_RenameBuffer = group.name;
            }
        }

        // ── 경고 ────────────────────────────────────────────────────────

        void DrawUnsavedSelectionWarning()
        {
            var contexts = TransparentGroupSession.Contexts;
            if (contexts.Count == 0) return;

            // 씬에 저장된 적 없는 오브젝트는 GlobalObjectId 가 0 이라 그룹에 담아도 복원되지 않는다(PLAN R4).
            var unsaved = new List<string>();
            foreach (var go in Selection.gameObjects)
            {
                foreach (var context in contexts)
                {
                    if (context.Kind != GroupContextKind.Scene) continue;
                    if (TransparentGroupResolver.IsUnsavedSceneObject(context, go)) { unsaved.Add(go.name); break; }
                }
            }
            if (unsaved.Count == 0) return;

            EditorGUILayout.HelpBox(
                $"선택한 오브젝트 {unsaved.Count}개는 아직 씬에 저장된 적이 없다 ({string.Join(", ", unsaved)}).\n" +
                "그룹에 담아도 Unity 를 다시 켜면 복원되지 않는다. 씬을 먼저 저장하라.",
                MessageType.Warning);
        }

        static int CountSelectedGroups()
        {
            var count = 0;
            foreach (var context in TransparentGroupSession.Contexts)
            {
                var set = TransparentGroupSession.GetSet(context);
                if (set == null) continue;
                foreach (var group in set.Groups)
                {
                    if (TransparentGroupSession.IsSelected(group)) count++;
                }
            }
            return count;
        }

        static int CountMissing(GroupContext context, GroupData group)
        {
            var count = 0;
            foreach (var member in group.members)
            {
                if (TransparentGroupResolver.Resolve(context, member).IsMissing) count++;
            }
            return count;
        }
    }
}
