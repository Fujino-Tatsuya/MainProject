using System.Text;
using UnityEditor;
using UnityEngine;

// 조사 도구 — 선택한 오브젝트의 **자식 트랜스폼 경로**를 콘솔에 덤프한다 (2026-08-10).
//
// 왜 필요한가: 보스·웰즈의 히트박스와 손 소켓은 **리그의 본에 붙여야** 한다. 그런데 본 이름은
// fbx 안에 있고, MCP 의 `unity_get_hierarchy` 는 3단계까지만 열거해서 본이 보이지 않는다.
// 인스펙터로 손으로 펼치는 것 말고는 경로를 확인할 방법이 없어서 만들었다.
//
// 🔴 이 도구는 **읽기 전용**이다. 아무것도 수정하지 않는다.
public static class BonePathDump
{
    // 이름에 이 조각들이 들어간 것만 뽑는다(전체를 찍으면 수백 줄이라 신호가 묻힌다 — 교훈 #8).
    static readonly string[] Interesting =
    {
        "hand", "wrist", "spine", "root", "shoulder", "forearm", "head", "socket", "traj", "pos",
    };

    [MenuItem("Tools/Boss/선택 오브젝트 — 본 경로 덤프 (관심 본만)")]
    public static void DumpFiltered() => Dump(filtered: true);

    [MenuItem("Tools/Boss/선택 오브젝트 — 본 경로 덤프 (전체)")]
    public static void DumpAll() => Dump(filtered: false);

    static void Dump(bool filtered)
    {
        GameObject go = Selection.activeGameObject;
        if (go == null)
        {
            Debug.LogError("[본 덤프] 선택된 GameObject 가 없다 — Hierarchy 에서 대상을 고르고 다시 누를 것.");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"[본 덤프] {go.name} — {(filtered ? "관심 본만" : "전체")}");

        int total = 0, shown = 0;
        Transform root = go.transform;
        Transform[] all = go.GetComponentsInChildren<Transform>(true);

        foreach (Transform t in all)
        {
            total++;
            if (t == root) continue;
            if (filtered && !IsInteresting(t.name)) continue;

            shown++;
            // 🔴 스케일을 같이 찍는다. 이 프로젝트의 리그는 100배라, 본 밑에 콜라이더를 넣으면
            //    그 스케일을 상속한다(크기를 1/100 로 넣어야 의도한 월드 크기가 나온다).
            sb.AppendLine(
                $"  {Path(root, t)}\n" +
                $"      localScale {Fmt(t.localScale)}  lossyScale {Fmt(t.lossyScale)}  worldPos {Fmt(t.position)}");
        }

        sb.AppendLine($"  → 전체 {total}개 중 {shown}개 표시");
        Debug.Log(sb.ToString());
    }

    static bool IsInteresting(string name)
    {
        string lower = name.ToLowerInvariant();
        for (int i = 0; i < Interesting.Length; i++)
            if (lower.Contains(Interesting[i])) return true;
        return false;
    }

    // 선택 오브젝트 기준 상대 경로(그대로 Transform.Find 에 넣을 수 있는 형태).
    static string Path(Transform root, Transform t)
    {
        string path = t.name;
        Transform p = t.parent;
        while (p != null && p != root)
        {
            path = p.name + "/" + path;
            p = p.parent;
        }
        return path;
    }

    static string Fmt(Vector3 v) => $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";
}
