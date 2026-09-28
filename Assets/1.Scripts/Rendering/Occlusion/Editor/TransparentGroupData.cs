// 투명화 그룹 툴 — 직렬화 데이터 모델과 순수 로직.
// PLAN-transparent-group-tool.md §3.
//
// 여기 있는 것은 Unity 에디터 상태에 의존하지 않는다. 그래서 EditMode 테스트가 그대로 돌릴 수 있다.
// 에디터 API 를 타는 것(컨텍스트 판별, 파일 입출력, 오브젝트 해석)은 Store/Resolver 로 나가 있다.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace VeyTrace.Rendering.Occlusion.Editor
{
    /// <summary>그룹이 어디에 속하는지. 씬과 프리팹은 완전히 분리된다(PLAN 결정 4).</summary>
    public enum GroupContextKind
    {
        None = 0,
        Scene = 1,
        Prefab = 2,
    }

    /// <summary>
    /// 멤버 하나를 가리키는 키. <see cref="globalObjectId"/> 로 찾고, 실패하면 <see cref="path"/> 로 폴백한다.
    /// <para>
    /// 🔴 씬과 프리팹이 **같은 키를 쓴다**(2026-09-28 R7 프로브로 확정). 프리팹 편집 모드의 오브젝트도
    /// <c>GlobalObjectId</c> 가 임시 씬이 아니라 **프리팹 에셋 GUID** 를 참조하고(identifierType=2),
    /// 문자열로 저장했다 되돌리면 같은 오브젝트가 나온다. 중첩 프리팹 인스턴스도 targetPrefabId 로 구분된다.
    /// </para>
    /// <para>
    /// 쓰지 않기로 한 것: <c>PrefabUtility.GetCorrespondingObjectFromSource</c> 의 fileID.
    /// 그것은 *원본 프리팹* 의 오브젝트를 가리켜서, 같은 프리팹을 35번 넣은 존이라면 35개가 전부 같은 값이 된다.
    /// </para>
    /// </summary>
    [Serializable]
    public class GroupMemberData
    {
        /// <summary>1차 키. 씬·프리팹 공통.</summary>
        public string globalObjectId;

        /// <summary>폴백 키. 컨텍스트 루트로부터의 Transform 경로.</summary>
        public string path;

        /// <summary>유실 표시에 쓸 사람이 읽는 이름. 해석에는 쓰지 않는다.</summary>
        public string lastSeenName;
    }

    [Serializable]
    public class GroupData
    {
        /// <summary>이름과 무관한 안정 키. 이름을 바꿔도 그룹 동일성이 유지된다.</summary>
        public string id;

        public string name;
        public Color color;

        /// <summary>false 면 팔레트 자동 배정(PLAN 결정 13). 사용자가 고르면 true.</summary>
        public bool colorIsCustom;

        public List<GroupMemberData> members = new List<GroupMemberData>();
    }

    /// <summary>JSON 파일 하나 = 씬 또는 프리팹 하나(PLAN 결정 6).</summary>
    [Serializable]
    public class GroupFileData
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public string contextKind;
        public string contextGuid;
        public string contextName;
        public List<GroupData> groups = new List<GroupData>();
    }

    /// <summary>멤버를 실제 오브젝트로 되살릴 때 어느 키를 쓸지. 순수 판정이라 테스트 가능하다.</summary>
    public enum MemberLookupStrategy
    {
        /// <summary>쓸 수 있는 키가 하나도 없다 — 데이터가 깨진 것이다.</summary>
        Unusable = 0,
        GlobalObjectId = 1,
        PathOnly = 2,
    }

    public static class TransparentGroupLogic
    {
        /// <summary>
        /// 1차 키가 있으면 그것을, 없으면 경로 폴백을 고른다.
        /// 경로마저 없으면 Unusable — 이 멤버는 유실로 표시된다(PLAN 결정 11).
        /// <para>씬이든 프리팹이든 같은 판정이다 — 키가 하나로 통일됐다(<see cref="GroupMemberData"/> 주석 참조).</para>
        /// </summary>
        public static MemberLookupStrategy ChooseStrategy(GroupMemberData member)
        {
            if (member == null) return MemberLookupStrategy.Unusable;
            if (!string.IsNullOrEmpty(member.globalObjectId)) return MemberLookupStrategy.GlobalObjectId;
            return string.IsNullOrEmpty(member.path) ? MemberLookupStrategy.Unusable : MemberLookupStrategy.PathOnly;
        }

        /// <summary>
        /// JSON 파일명: &lt;컨텍스트이름&gt;.&lt;GUID 앞 8자&gt;.json (PLAN 결정 6).
        /// 이름은 읽히라고 넣는 것이고, 동일성의 근거는 GUID 쪽이다.
        /// </summary>
        public static string MakeFileName(string contextName, string contextGuid)
        {
            return SanitizeFileName(contextName) + "." + ShortGuid(contextGuid) + ".json";
        }

        /// <summary>파일명에서 GUID 앞 8자만 뽑는다. 이름이 바뀐 파일을 찾아 정정할 때 쓴다.</summary>
        public static string ExtractShortGuid(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;

            const string suffix = ".json";
            if (!fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;

            var withoutExt = fileName.Substring(0, fileName.Length - suffix.Length);
            var dot = withoutExt.LastIndexOf('.');
            if (dot < 0 || dot == withoutExt.Length - 1) return null;

            return withoutExt.Substring(dot + 1);
        }

        public static string ShortGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return "nogiud";
            return guid.Length <= 8 ? guid : guid.Substring(0, 8);
        }

        static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";

            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var buffer = new System.Text.StringBuilder(name.Length);
            foreach (var c in name)
            {
                // '.' 은 GUID 구분자로 쓰므로 이름 쪽에서는 지운다.
                if (c == '.' || Array.IndexOf(invalid, c) >= 0) buffer.Append('_');
                else buffer.Append(c);
            }
            return buffer.ToString();
        }
    }
}
