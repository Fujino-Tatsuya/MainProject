// 투명화 그룹 툴 — JSON 저장소와 컨텍스트 판별.
// PLAN-transparent-group-tool.md 결정 5·6·7.
//
// 저장 위치는 Assets/ **밖**, 프로젝트 루트의 TransparentGroups/ 다.
// 그래서 .meta 도, 리임포트도, 씬 dirty 도 없다. git 에는 추적된다(결정 5).

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VeyTrace.Rendering.Occlusion.Editor
{
    /// <summary>그룹이 붙는 대상 하나(씬 또는 프리팹 에셋).</summary>
    public readonly struct GroupContext : IEquatable<GroupContext>
    {
        public readonly GroupContextKind Kind;
        public readonly string Guid;
        public readonly string Name;

        public GroupContext(GroupContextKind kind, string guid, string name)
        {
            Kind = kind;
            Guid = guid;
            Name = name;
        }

        public bool IsValid => Kind != GroupContextKind.None && !string.IsNullOrEmpty(Guid);

        public bool Equals(GroupContext other) => Kind == other.Kind && Guid == other.Guid;
        public override bool Equals(object obj) => obj is GroupContext other && Equals(other);
        public override int GetHashCode() => (Guid != null ? Guid.GetHashCode() : 0) ^ (int)Kind;
        public override string ToString() => $"{Kind}:{Name}({TransparentGroupLogic.ShortGuid(Guid)})";
    }

    public static class TransparentGroupStore
    {
        public const string DirectoryName = "TransparentGroups";

        /// <summary>프로젝트 루트(Assets 의 부모) 아래. Assets/ 밖이라 Unity 가 임포트하지 않는다.</summary>
        public static string RootDirectory
        {
            get
            {
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
                return Path.Combine(projectRoot, DirectoryName);
            }
        }

        // ── 컨텍스트 판별 ────────────────────────────────────────────────

        /// <summary>
        /// 프리팹 편집 모드면 그 프리팹이, 아니면 열려 있는 씬들이 컨텍스트다.
        /// 프리팹 모드에서는 씬 그룹을 보여주지 않는다(PLAN 결정 4).
        /// </summary>
        public static List<GroupContext> GetActiveContexts()
        {
            var result = new List<GroupContext>();

            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                var guid = AssetDatabase.AssetPathToGUID(prefabStage.assetPath);
                if (!string.IsNullOrEmpty(guid))
                {
                    result.Add(new GroupContext(
                        GroupContextKind.Prefab, guid, Path.GetFileNameWithoutExtension(prefabStage.assetPath)));
                }
                return result;
            }

            // additive 로 여러 씬을 열었으면 전부 모은다(Q26 후속 합의).
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded) continue;

                // 저장된 적 없는 씬은 GUID 가 없다 — 그룹을 담을 수 없다.
                if (string.IsNullOrEmpty(scene.path)) continue;

                var guid = AssetDatabase.AssetPathToGUID(scene.path);
                if (string.IsNullOrEmpty(guid)) continue;

                result.Add(new GroupContext(GroupContextKind.Scene, guid, scene.name));
            }

            return result;
        }

        // ── 파일 입출력 ──────────────────────────────────────────────────

        /// <summary>
        /// 이 컨텍스트의 JSON 경로. 이름이 바뀌어 파일명이 어긋나 있으면 GUID 로 찾아 정정한다(PLAN 결정 6).
        /// </summary>
        public static string GetFilePath(GroupContext context)
        {
            var expected = Path.Combine(RootDirectory, TransparentGroupLogic.MakeFileName(context.Name, context.Guid));
            if (File.Exists(expected)) return expected;

            var existing = FindByGuid(context.Guid);
            if (existing == null) return expected;

            // 컨텍스트 이름이 바뀐 경우. 파일을 새 이름으로 옮겨 둔다.
            try
            {
                Directory.CreateDirectory(RootDirectory);
                File.Move(existing, expected);
                Debug.Log($"[TransparentGroup] 이름 변경 감지 — 파일명 정정: " +
                          $"{Path.GetFileName(existing)} → {Path.GetFileName(expected)}");
                return expected;
            }
            catch (IOException e)
            {
                // 옮기지 못해도 읽기는 되어야 한다. 원래 파일을 계속 쓴다.
                Debug.LogWarning($"[TransparentGroup] 파일명 정정 실패, 기존 경로를 그대로 쓴다: {e.Message}");
                return existing;
            }
        }

        static string FindByGuid(string guid)
        {
            if (!Directory.Exists(RootDirectory) || string.IsNullOrEmpty(guid)) return null;

            var target = TransparentGroupLogic.ShortGuid(guid);
            foreach (var file in Directory.GetFiles(RootDirectory, "*.json"))
            {
                if (string.Equals(TransparentGroupLogic.ExtractShortGuid(Path.GetFileName(file)), target,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }
            return null;
        }

        /// <summary>없거나 못 읽으면 빈 데이터를 돌려준다. 예외를 밖으로 던지지 않는다.</summary>
        public static GroupFileData Load(GroupContext context)
        {
            if (!context.IsValid) return NewEmpty(context);

            var path = GetFilePath(context);
            if (!File.Exists(path)) return NewEmpty(context);

            try
            {
                var data = JsonUtility.FromJson<GroupFileData>(File.ReadAllText(path));
                if (data == null) return NewEmpty(context);

                if (data.version > GroupFileData.CurrentVersion)
                {
                    Debug.LogWarning($"[TransparentGroup] {Path.GetFileName(path)} 의 version={data.version} 은 " +
                                     $"이 툴({GroupFileData.CurrentVersion})보다 높다. 읽기는 하지만 모르는 필드는 버려진다.");
                }

                data.groups ??= new List<GroupData>();
                foreach (var group in data.groups) group.members ??= new List<GroupMemberData>();
                return data;
            }
            catch (Exception e)
            {
                // 깨진 파일을 덮어쓰지 않는다 — 사용자가 손으로 고칠 수 있어야 한다(결정 5의 전제).
                Debug.LogError($"[TransparentGroup] {path} 를 읽지 못했다. 이 컨텍스트는 빈 상태로 연다: {e.Message}");
                return NewEmpty(context);
            }
        }

        public static void Save(GroupContext context, GroupFileData data)
        {
            if (!context.IsValid || data == null) return;

            data.version = GroupFileData.CurrentVersion;
            data.contextKind = context.Kind.ToString();
            data.contextGuid = context.Guid;
            data.contextName = context.Name;

            var path = GetFilePath(context);
            try
            {
                // 그룹이 없으면 파일을 두지 않는다. `Undo.undoRedoPerformed` 는 에디터의 **모든** Undo 에
                // 대해 불리므로, 이게 없으면 프리팹을 열어보기만 해도 빈 JSON 이 하나씩 쌓인다.
                if (data.groups == null || data.groups.Count == 0)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }

                var json = JsonUtility.ToJson(data, prettyPrint: true);

                // 같은 내용이면 건드리지 않는다. 위와 같은 이유로 무관한 Undo 마다 mtime 이 튀는 것을 막는다.
                if (File.Exists(path) && File.ReadAllText(path) == json) return;

                Directory.CreateDirectory(RootDirectory);
                File.WriteAllText(path, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[TransparentGroup] {path} 에 저장하지 못했다: {e.Message}");
            }
        }

        static GroupFileData NewEmpty(GroupContext context)
        {
            return new GroupFileData
            {
                version = GroupFileData.CurrentVersion,
                contextKind = context.Kind.ToString(),
                contextGuid = context.Guid,
                contextName = context.Name,
                groups = new List<GroupData>(),
            };
        }
    }
}
