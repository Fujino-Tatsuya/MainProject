// 투명화 그룹 툴 — 멤버 키 ↔ 실제 GameObject.
// PLAN-transparent-group-tool.md 결정 9·10·11.
//
// 해석 순서: GlobalObjectId → 경로 폴백 → 유실. 씬과 프리팹이 같은 경로를 탄다(R7 확정).
// 유실은 드롭하지 않고 표시만 한다. 조용히 사라지면 디버깅이 불가능하다.

using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VeyTrace.Rendering.Occlusion.Editor
{
    public readonly struct MemberResolution
    {
        public readonly GameObject Target;
        public readonly MemberLookupStrategy UsedStrategy;

        /// <summary>1차 키로 못 찾아 경로로 건졌다는 뜻. 키를 갱신해 줄 수 있다.</summary>
        public readonly bool ResolvedByFallback;

        public MemberResolution(GameObject target, MemberLookupStrategy used, bool byFallback)
        {
            Target = target;
            UsedStrategy = used;
            ResolvedByFallback = byFallback;
        }

        public bool IsMissing => Target == null;

        public static MemberResolution Missing => new MemberResolution(null, MemberLookupStrategy.Unusable, false);
    }

    public static class TransparentGroupResolver
    {
        // ── 멤버 만들기 ──────────────────────────────────────────────────

        public static GroupMemberData CreateMember(GroupContext context, GameObject target)
        {
            if (target == null) return null;

            var member = new GroupMemberData
            {
                path = GetPath(context, target),
                lastSeenName = target.name,
            };

            // 씬·프리팹 공통. 프리팹 편집 모드에서도 GlobalObjectId 는 프리팹 에셋을 참조한다(R7 확정).
            var id = GlobalObjectId.GetGlobalObjectIdSlow(target);
            // 저장된 적 없는 씬 오브젝트는 identifier 가 0 이다 — 씬을 저장해야 담을 수 있다(PLAN R4).
            if (id.targetObjectId != 0) member.globalObjectId = id.ToString();

            return member;
        }

        /// <summary>담을 수 없는 오브젝트인지. 창에서 경고를 띄우는 데 쓴다.</summary>
        public static bool IsUnsavedSceneObject(GroupContext context, GameObject target)
        {
            if (context.Kind != GroupContextKind.Scene || target == null) return false;
            return GlobalObjectId.GetGlobalObjectIdSlow(target).targetObjectId == 0;
        }

        // ── 멤버 해석 ────────────────────────────────────────────────────

        public static MemberResolution Resolve(GroupContext context, GroupMemberData member)
        {
            var strategy = TransparentGroupLogic.ChooseStrategy(member);
            if (strategy == MemberLookupStrategy.Unusable) return MemberResolution.Missing;

            if (strategy == MemberLookupStrategy.GlobalObjectId)
            {
                var byId = ResolveByGlobalObjectId(member.globalObjectId);
                if (byId != null) return new MemberResolution(byId, strategy, byFallback: false);
            }

            // 1차 키가 없거나 실패했다. 경로로 한 번 더 시도한다.
            if (!string.IsNullOrEmpty(member.path))
            {
                var byPath = ResolveByPath(context, member.path);
                if (byPath != null)
                {
                    var wasFallback = strategy != MemberLookupStrategy.PathOnly;
                    return new MemberResolution(byPath, MemberLookupStrategy.PathOnly, wasFallback);
                }
            }

            return MemberResolution.Missing;
        }

        static GameObject ResolveByGlobalObjectId(string serialized)
        {
            if (!GlobalObjectId.TryParse(serialized, out var id)) return null;
            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
        }

        static GameObject ResolveByPath(GroupContext context, string path)
        {
            foreach (var root in GetRoots(context))
            {
                var found = FindByPath(root, path);
                if (found != null) return found;
            }
            return null;
        }

        // ── 경로 ────────────────────────────────────────────────────────

        /// <summary>
        /// 씬은 루트 이름부터, 프리팹은 prefabContentsRoot 아래부터의 경로.
        /// 프리팹 루트 자신은 빈 문자열이다.
        /// </summary>
        public static string GetPath(GroupContext context, GameObject target)
        {
            if (target == null) return null;

            var stopAtRoot = context.Kind == GroupContextKind.Prefab
                ? PrefabStageUtility.GetCurrentPrefabStage()?.prefabContentsRoot?.transform
                : null;

            var builder = new StringBuilder();
            for (var t = target.transform; t != null && t != stopAtRoot; t = t.parent)
            {
                if (builder.Length > 0) builder.Insert(0, '/');
                builder.Insert(0, t.name);
            }
            return builder.ToString();
        }

        static GameObject FindByPath(GameObject root, string path)
        {
            if (root == null) return null;
            if (string.IsNullOrEmpty(path)) return root;

            var segments = path.Split('/');
            var index = 0;
            Transform current;

            // 씬 경로는 루트 이름으로 시작한다. 프리팹 경로는 루트 아래부터다.
            if (segments[0] == root.name)
            {
                current = root.transform;
                index = 1;
            }
            else
            {
                current = root.transform;
            }

            for (; index < segments.Length; index++)
            {
                current = current.Find(segments[index]);
                if (current == null) return null;
            }
            return current.gameObject;
        }

        static IEnumerable<GameObject> GetRoots(GroupContext context)
        {
            if (context.Kind == GroupContextKind.Prefab)
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null && stage.prefabContentsRoot != null) yield return stage.prefabContentsRoot;
                yield break;
            }

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                if (AssetDatabase.AssetPathToGUID(scene.path) != context.Guid) continue;

                foreach (var root in scene.GetRootGameObjects()) yield return root;
            }
        }
    }
}
