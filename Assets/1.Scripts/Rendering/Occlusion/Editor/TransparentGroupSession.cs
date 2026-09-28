// 투명화 그룹 툴 — 창과 시각화가 공유하는 에디터 세션 상태.
// PLAN-transparent-group-tool.md 결정 4·8·12·15·17.
//
// 왜 창 안에 두지 않는가: 시각화(S3)는 창이 닫혀 있어도 토글이 켜져 있으면 그려야 하고,
// 창은 그리기를 몰라야 한다. 둘이 공유하는 것은 "어떤 컨텍스트의 어떤 그룹이 선택돼 있고,
// 슬라이더가 얼마인가" 뿐이라 그것만 여기 모은다.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VeyTrace.Rendering.Occlusion.Editor
{
    [InitializeOnLoad]
    public static class TransparentGroupSession
    {
        // 비활성 그룹은 같은 색을 쓰되 blend 만 낮춘다(PLAN 결정 17-나). 셰이더를 늘리지 않는다.
        const float k_InactiveBlendScale = 0.35f;

        static readonly List<GroupContext> s_Contexts = new List<GroupContext>();
        static readonly Dictionary<GroupContext, TransparentGroupSet> s_Sets =
            new Dictionary<GroupContext, TransparentGroupSet>();
        static readonly HashSet<string> s_SelectedGroupIds = new HashSet<string>();

        /// <summary>컨텍스트·그룹·표시 설정 중 무엇이든 바뀌면 불린다.</summary>
        public static event Action Changed;

        // ── 표시 설정 (PLAN 결정 15·16) ─────────────────────────────────

        static bool s_OverlayEnabled;
        static float s_Blend = 0.5f;
        static float s_SceneSaturation = 0.5f;

        public static bool OverlayEnabled
        {
            get => s_OverlayEnabled;
            set { if (s_OverlayEnabled != value) { s_OverlayEnabled = value; Raise(); } }
        }

        public static float Blend
        {
            get => s_Blend;
            set { var v = Mathf.Clamp01(value); if (!Mathf.Approximately(s_Blend, v)) { s_Blend = v; Raise(); } }
        }

        public static float SceneSaturation
        {
            get => s_SceneSaturation;
            set { var v = Mathf.Clamp01(value); if (!Mathf.Approximately(s_SceneSaturation, v)) { s_SceneSaturation = v; Raise(); } }
        }

        // ── 선택(페인트) 모드 ───────────────────────────────────────────

        static bool s_PaintMode;

        /// <summary>
        /// 켜면 씬 뷰 클릭·드래그가 오브젝트 선택 대신 **그룹에 담기**로 바뀐다.
        /// 대상이 모호해지지 않도록 그룹이 **정확히 하나** 선택돼 있을 때만 켤 수 있다.
        /// </summary>
        public static bool PaintMode
        {
            get => s_PaintMode && CanPaint;
            set { if (s_PaintMode != value) { s_PaintMode = value; Raise(); } }
        }

        public static bool CanPaint => s_SelectedGroupIds.Count == 1;

        /// <summary>페인트가 들어갈 그룹. 선택이 정확히 하나가 아니면 false.</summary>
        public static bool TryGetPaintTarget(out GroupContext context, out TransparentGroupSet set, out GroupData group)
        {
            context = default;
            set = null;
            group = null;
            if (s_SelectedGroupIds.Count != 1) return false;

            foreach (var candidate in s_Contexts)
            {
                var candidateSet = GetSet(candidate);
                if (candidateSet == null) continue;

                foreach (var candidateGroup in candidateSet.Groups)
                {
                    if (!IsSelected(candidateGroup)) continue;
                    context = candidate;
                    set = candidateSet;
                    group = candidateGroup;
                    return true;
                }
            }
            return false;
        }

        // ── 컨텍스트 ────────────────────────────────────────────────────

        public static IReadOnlyList<GroupContext> Contexts => s_Contexts;

        public static TransparentGroupSet GetSet(GroupContext context)
        {
            return s_Sets.TryGetValue(context, out var set) ? set : null;
        }

        static TransparentGroupSession()
        {
            EditorApplication.delayCall += RefreshContexts;

            EditorSceneManager.sceneOpened += (_, __) => RefreshContexts();
            EditorSceneManager.sceneClosed += _ => RefreshContexts();
            EditorSceneManager.newSceneCreated += (_, __, ___) => RefreshContexts();
            PrefabStage.prefabStageOpened += _ => RefreshContexts();
            PrefabStage.prefabStageClosing += _ => EditorApplication.delayCall += RefreshContexts;

            // 그룹 조작은 Undo 스택에 얹혀 있다(PLAN 결정 8). 되돌아간 값을 JSON 에도 반영해야 한다.
            Undo.undoRedoPerformed += OnUndoRedo;

            AssemblyReloadEvents.beforeAssemblyReload += DisposeSets;
        }

        /// <summary>열려 있는 씬/프리팹에 맞춰 그룹 집합을 다시 만든다.</summary>
        public static void RefreshContexts()
        {
            var next = TransparentGroupStore.GetActiveContexts();

            // 사라진 컨텍스트의 집합은 버린다.
            var stale = new List<GroupContext>();
            foreach (var pair in s_Sets)
            {
                if (!next.Contains(pair.Key)) stale.Add(pair.Key);
            }
            foreach (var context in stale)
            {
                DestroySet(s_Sets[context]);
                s_Sets.Remove(context);
            }

            foreach (var context in next)
            {
                if (s_Sets.ContainsKey(context)) continue;

                var set = TransparentGroupSet.CreateFor(context);
                set.Changed += Raise;
                s_Sets[context] = set;
            }

            s_Contexts.Clear();
            s_Contexts.AddRange(next);
            PruneSelection();
            Raise();
        }

        // ── 그룹 선택 (= "활성". PLAN 결정 17) ──────────────────────────

        public static bool IsSelected(GroupData group) => group != null && s_SelectedGroupIds.Contains(group.id);

        public static void SelectOnly(GroupData group)
        {
            s_SelectedGroupIds.Clear();
            if (group != null) s_SelectedGroupIds.Add(group.id);
            PushToUnitySelection();
            Raise();
        }

        public static void ToggleSelected(GroupData group)
        {
            if (group == null) return;
            if (!s_SelectedGroupIds.Add(group.id)) s_SelectedGroupIds.Remove(group.id);
            PushToUnitySelection();
            Raise();
        }

        public static void ClearSelection()
        {
            if (s_SelectedGroupIds.Count == 0) return;
            s_SelectedGroupIds.Clear();
            Raise();
        }

        /// <summary>
        /// 선택된 그룹들의 멤버 합집합을 Unity <see cref="Selection"/> 에 넣는다.
        /// 값 편집 UI 를 따로 만들지 않는 이유가 이것이다 — 기본 인스펙터가 공통 컴포넌트를 묶어 준다(PLAN 결정 3).
        /// </summary>
        public static void PushToUnitySelection()
        {
            var targets = new List<UnityEngine.Object>();
            var seen = new HashSet<int>();

            foreach (var context in s_Contexts)
            {
                var set = GetSet(context);
                if (set == null) continue;

                foreach (var group in set.Groups)
                {
                    if (!IsSelected(group)) continue;

                    foreach (var member in group.members)
                    {
                        var resolved = TransparentGroupResolver.Resolve(context, member);
                        if (resolved.IsMissing) continue;
                        if (seen.Add(resolved.Target.GetInstanceID())) targets.Add(resolved.Target);
                    }
                }
            }

            Selection.objects = targets.ToArray();
        }

        // ── 시각화용 색 맵 ──────────────────────────────────────────────

        /// <summary>
        /// Renderer 인스턴스 ID → 색(rgb = 그룹 색, a = blend 세기).
        /// 🔴 키는 **Renderer 컴포넌트**의 InstanceID 다. `ObjectIdRequest` 의 idToObjectMapping 에
        /// 담기는 것이 GameObject 가 아니라 Renderer 이기 때문이다(S0 에서 확인).
        /// </summary>
        public static Dictionary<int, Color> BuildColorMap()
        {
            var map = new Dictionary<int, Color>();
            if (!s_OverlayEnabled) return map;

            // 비활성 → 활성 순으로 써서, 겹치는 오브젝트는 활성 그룹 색이 이기게 한다(PLAN 결정 12·17).
            WriteGroups(map, selectedPass: false);
            WriteGroups(map, selectedPass: true);
            return map;
        }

        static void WriteGroups(Dictionary<int, Color> map, bool selectedPass)
        {
            // 아무 그룹도 고르지 않았으면 전부 또렷하게 보여준다 — 그 편이 "왜 다 흐리지?" 보다 낫다.
            var nothingSelected = s_SelectedGroupIds.Count == 0;

            foreach (var context in s_Contexts)
            {
                var set = GetSet(context);
                if (set == null) continue;

                foreach (var group in set.Groups)
                {
                    var isActive = nothingSelected || IsSelected(group);
                    if (isActive != selectedPass) continue;

                    var color = group.color;
                    color.a = isActive ? s_Blend : s_Blend * k_InactiveBlendScale;

                    foreach (var member in group.members)
                    {
                        var resolved = TransparentGroupResolver.Resolve(context, member);
                        if (resolved.IsMissing) continue;

                        // PLAN 결정 19 — 자식 Renderer 까지 전부.
                        foreach (var renderer in resolved.Target.GetComponentsInChildren<Renderer>(true))
                        {
                            map[renderer.GetInstanceID()] = color;
                        }
                    }
                }
            }
        }

        // ── 내부 ────────────────────────────────────────────────────────

        static void OnUndoRedo()
        {
            foreach (var set in s_Sets.Values) set.OnUndoRedo();
            PruneSelection();
            Raise();
        }

        /// <summary>삭제되거나 되돌려져 사라진 그룹의 선택을 걷어낸다.</summary>
        static void PruneSelection()
        {
            if (s_SelectedGroupIds.Count == 0) return;

            var alive = new HashSet<string>();
            foreach (var context in s_Contexts)
            {
                var set = GetSet(context);
                if (set == null) continue;
                foreach (var group in set.Groups) alive.Add(group.id);
            }
            s_SelectedGroupIds.RemoveWhere(id => !alive.Contains(id));
        }

        static void DisposeSets()
        {
            foreach (var set in s_Sets.Values) DestroySet(set);
            s_Sets.Clear();
            s_Contexts.Clear();
        }

        static void DestroySet(TransparentGroupSet set)
        {
            if (set == null) return;
            set.Changed -= Raise;
            UnityEngine.Object.DestroyImmediate(set);
        }

        static void Raise()
        {
            Changed?.Invoke();
            SceneView.RepaintAll();
        }
    }
}
