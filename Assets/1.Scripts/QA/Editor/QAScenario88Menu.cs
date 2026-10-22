#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 88 간파 지연 QA 메뉴(Q2 = 원격 지연 제어). MPPM 가상 플레이어(클론) 루트 <c>Library/VP/mppm*/</c> 에만
/// <see cref="QANetSim88.FileName"/> 을 써서 원격 클라만 지연시킨다 — 메인 에디터(호스트)는 지연 0.
/// Play 전·중 어느 때나 쓸 수 있다(클론 하네스가 0.5초마다 다시 읽는다). 원복 = 파일 삭제(= 0).
/// <c>Tools/QA/48/Clear Scenario Mode</c> 도 원복한다. 스위트 러너(Q3)는 <see cref="SetRemoteDelay"/>·<see cref="ResetRemoteDelay"/> 를 부른다.
/// </summary>
public static class QAScenario88Menu
{
    private const string Root = "Tools/QA/88/";

    [MenuItem(Root + "NetSim/Remote Delay 5 ms", priority = 10)]
    public static void Delay5() => SetRemoteDelay(5);

    [MenuItem(Root + "NetSim/Remote Delay 35 ms", priority = 11)]
    public static void Delay35() => SetRemoteDelay(35);

    [MenuItem(Root + "NetSim/Remote Delay 80 ms", priority = 12)]
    public static void Delay80() => SetRemoteDelay(80);

    [MenuItem(Root + "NetSim/Remote Delay 120 ms", priority = 13)]
    public static void Delay120() => SetRemoteDelay(120);

    [MenuItem(Root + "NetSim/Reset Remote Delay (0)", priority = 30)]
    public static void Reset() => ResetRemoteDelay();

    [MenuItem(Root + "NetSim/Status", priority = 31)]
    public static void Status()
    {
        var parts = new List<string>();
        foreach (string root in CloneRoots())
        {
            string path = QANetSim88.FilePathFor(root);
            string value = File.Exists(path) ? File.ReadAllText(path).Trim() : "없음(0)";
            parts.Add($"{Path.GetFileName(root)}={value}");
        }
        Debug.Log($"[QA88] 원격 지연 파일 — {(parts.Count > 0 ? string.Join(", ", parts) : "MPPM 클론 폴더 없음")}");
    }

    /// <summary>MPPM 클론 전부에 원격 지연(ms, 송수신 각각)을 건다. 0 이하면 원복. 쓴 클론 수.</summary>
    public static int SetRemoteDelay(int delayMs)
    {
        if (delayMs <= 0)
            return ResetRemoteDelay();
        delayMs = Mathf.Min(delayMs, QAInterruptLag88Plan.MaxDelayMs);
        int n = 0;
        foreach (string root in CloneRoots())
        {
            File.WriteAllText(QANetSim88.FilePathFor(root), delayMs.ToString());
            n++;
        }
        Debug.Log(n > 0
            ? $"[QA88] 원격 지연 {delayMs}ms — 클론 {n}개에 기록(Play 중이면 0.5초 안에 적용)"
            : "[QA88] MPPM 클론 폴더(Library/VP/mppm*)가 없다 — Tools/QA/Activate Virtual Players (2,3) 를 먼저 한 번 켤 것.");
        return n;
    }

    /// <summary>원격 지연 원복(0) — 클론의 지연 파일을 지운다. 지운 수.</summary>
    public static int ResetRemoteDelay()
    {
        int n = 0;
        foreach (string root in CloneRoots())
        {
            string path = QANetSim88.FilePathFor(root);
            if (!File.Exists(path))
                continue;
            File.Delete(path);
            n++;
        }
        Debug.Log($"[QA88] 원격 지연 원복(0) — 지연 파일 {n}개 삭제");
        return n;
    }

    private static IEnumerable<string> CloneRoots()
    {
        string vp = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "VP");
        if (!Directory.Exists(vp))
            yield break;
        foreach (string dir in Directory.GetDirectories(vp, "mppm*"))
            yield return dir;
    }
}
#endif
