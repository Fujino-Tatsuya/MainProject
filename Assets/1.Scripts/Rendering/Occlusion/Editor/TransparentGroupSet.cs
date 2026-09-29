// 투명화 그룹 툴 — 메모리 상의 그룹 목록.
// PLAN-transparent-group-tool.md 결정 7·8.
//
// 이 ScriptableObject 는 **에셋으로 저장되지 않는다**(hideFlags = DontSave).
// 존재 이유는 하나 — Undo 는 UnityEngine.Object 에만 걸린다. 순수 C# 객체나 JSON 에는 못 건다.
// 진짜 저장소는 JSON 이고, 여기서 바뀔 때마다 즉시 쓴다.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VeyTrace.Rendering.Occlusion.Editor
{
    public sealed class TransparentGroupSet : ScriptableObject
    {
        [SerializeField] List<GroupData> m_Groups = new List<GroupData>();

        // 컨텍스트는 struct 라 그대로 직렬화되지 않는다. 필드로 풀어 둔다.
        [SerializeField] GroupContextKind m_ContextKind;
        [SerializeField] string m_ContextGuid;
        [SerializeField] string m_ContextName;

        /// <summary>그룹이 바뀔 때마다 불린다. 창이 다시 그리는 데 쓴다.</summary>
        public event Action Changed;

        public IReadOnlyList<GroupData> Groups => m_Groups;

        public GroupContext Context => new GroupContext(m_ContextKind, m_ContextGuid, m_ContextName);

        public static TransparentGroupSet CreateFor(GroupContext context)
        {
            var set = CreateInstance<TransparentGroupSet>();
            set.hideFlags = HideFlags.HideAndDontSave;
            set.name = "TransparentGroupSet (" + context + ")";
            set.m_ContextKind = context.Kind;
            set.m_ContextGuid = context.Guid;
            set.m_ContextName = context.Name;

            var data = TransparentGroupStore.Load(context);
            set.m_Groups = data.groups ?? new List<GroupData>();
            return set;
        }

        // ── 조작 (전부 Undo 기록 + 즉시 저장) ────────────────────────────

        public GroupData CreateGroup(string groupName, IEnumerable<GameObject> members)
        {
            Record("Create Transparency Group");

            var group = new GroupData
            {
                id = Guid.NewGuid().ToString("N"),
                name = string.IsNullOrEmpty(groupName) ? DefaultName() : groupName,
                color = TransparentGroupPalette.GetColor(m_Groups.Count),
                colorIsCustom = false,
                members = new List<GroupMemberData>(),
            };
            m_Groups.Add(group);
            AddMembersInternal(group, members);

            Commit();
            return group;
        }

        public void AddMembers(GroupData group, IEnumerable<GameObject> members)
        {
            if (group == null) return;
            Record("Add To Transparency Group");
            AddMembersInternal(group, members);
            Commit();
        }

        public void RemoveMembers(GroupData group, IEnumerable<GameObject> members)
        {
            if (group == null || members == null) return;

            // 🔴 경로가 아니라 신원 키로 지운다. 경로로 지우면 이름이 같은 형제가 있을 때
            // 엉뚱한 오브젝트가 빠진다(TransparentGroupLogic.IdentityKey 주석 참조).
            var doomed = new HashSet<string>();
            foreach (var go in members)
            {
                if (go == null) continue;
                var key = TransparentGroupLogic.IdentityKey(TransparentGroupResolver.CreateMember(Context, go));
                if (key != null) doomed.Add(key);
            }
            if (doomed.Count == 0) return;

            Record("Remove From Transparency Group");
            group.members.RemoveAll(m => doomed.Contains(TransparentGroupLogic.IdentityKey(m)));
            Commit();
        }

        /// <summary>해석에 실패한 멤버만 걷어낸다(PLAN 결정 11 의 "정리" 버튼).</summary>
        public int RemoveMissingMembers(GroupData group)
        {
            if (group == null) return 0;

            var context = Context;
            var doomed = new List<GroupMemberData>();
            foreach (var member in group.members)
            {
                if (TransparentGroupResolver.Resolve(context, member).IsMissing) doomed.Add(member);
            }
            if (doomed.Count == 0) return 0;

            Record("Clean Missing Members");
            foreach (var member in doomed) group.members.Remove(member);
            Commit();
            return doomed.Count;
        }

        public void Rename(GroupData group, string newName)
        {
            if (group == null || string.IsNullOrEmpty(newName) || group.name == newName) return;
            Record("Rename Transparency Group");
            group.name = newName;
            Commit();
        }

        public void SetColor(GroupData group, Color color)
        {
            if (group == null || group.color == color) return;
            Record("Set Transparency Group Color");
            group.color = color;
            group.colorIsCustom = true;
            Commit();
        }

        public void Delete(GroupData group)
        {
            if (group == null) return;
            Record("Delete Transparency Group");
            m_Groups.Remove(group);
            Commit();
        }

        /// <summary>Undo/Redo 직후에 호출한다. 되돌아간 상태를 JSON 에도 반영해야 한다.</summary>
        public void OnUndoRedo()
        {
            Save();
            Changed?.Invoke();
        }

        // ── 내부 ────────────────────────────────────────────────────────

        void AddMembersInternal(GroupData group, IEnumerable<GameObject> members)
        {
            if (members == null) return;

            var context = Context;

            // 🔴 중복 판정도 신원 키로 한다. 경로로 하면 이름이 같은 형제가 조용히 버려진다.
            var existing = new HashSet<string>();
            foreach (var m in group.members)
            {
                var key = TransparentGroupLogic.IdentityKey(m);
                if (key != null) existing.Add(key);
            }

            foreach (var go in members)
            {
                if (go == null) continue;

                var member = TransparentGroupResolver.CreateMember(context, go);
                if (member == null) continue;

                // 같은 오브젝트를 한 그룹에 두 번 담지 않는다.
                // (다른 그룹과의 중복은 허용된다 — PLAN 결정 12)
                var key = TransparentGroupLogic.IdentityKey(member);
                if (key != null && !existing.Add(key)) continue;

                group.members.Add(member);
            }
        }

        string DefaultName()
        {
            return "Group " + (m_Groups.Count + 1);
        }

        void Record(string label)
        {
            // RegisterCompleteObjectUndo — 리스트 통째로 스냅샷. 멤버 추가/삭제가 섞여도 정확히 되돌아간다.
            Undo.RegisterCompleteObjectUndo(this, label);
        }

        void Commit()
        {
            Save();
            Changed?.Invoke();
        }

        // ── 저장 유예 ───────────────────────────────────────────────────
        //
        // 페인트 드래그는 마우스 이벤트마다 멤버를 더한다. 그때마다 JSON 을 다시 쓰면
        // 한 번 긋는 동안 파일을 수십 번 쓴다. 스트로크 동안 쓰기만 미루고,
        // 화면 갱신(Changed)은 그대로 흘려보내 색이 실시간으로 보이게 둔다.

        int m_SaveSuspendDepth;
        bool m_SavePending;

        public void SuspendSave() => m_SaveSuspendDepth++;

        public void ResumeSave()
        {
            if (m_SaveSuspendDepth > 0) m_SaveSuspendDepth--;
            if (m_SaveSuspendDepth > 0 || !m_SavePending) return;

            m_SavePending = false;
            Save();
        }

        void Save()
        {
            if (m_SaveSuspendDepth > 0)
            {
                m_SavePending = true;
                return;
            }

            var context = Context;
            if (!context.IsValid) return;

            TransparentGroupStore.Save(context, new GroupFileData
            {
                version = GroupFileData.CurrentVersion,
                contextKind = context.Kind.ToString(),
                contextGuid = context.Guid,
                contextName = context.Name,
                groups = m_Groups,
            });
        }
    }
}
