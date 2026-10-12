using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 개인 뒤 배리어 디버그 메뉴(15 후속). Play 중 튜토리얼 활성일 때 — 복제값(<see cref="TutorialDirector.GetBackBarrierMask"/>)이라 어느 피어에서도 같다.
/// 서버면 판정 원본(<see cref="TutorialDirector.GetBackBarrierMaskServer"/>)도 같이 찍는다.
/// </summary>
internal static class TutorialBackBarrierDebugMenu
{
    const string Item = "Tools/Tutorial/Debug/뒤 배리어 마스크 로그 (클라별)";

    [MenuItem(Item, true)]
    static bool Validate()
    {
        TutorialDirector d = TutorialDirector.Instance;
        return Application.isPlaying && d != null && d.IsTutorialActive;
    }

    [MenuItem(Item, false, 90)]
    static void LogMasks()
    {
        TutorialDirector d = TutorialDirector.Instance;
        var sb = new StringBuilder("[TutorialDebug] 뒤 배리어 마스크 —");
        // 클라는 접속자 목록을 모른다 — 자기 것만.
        IReadOnlyList<ulong> ids = d.IsServer ? d.NetworkManager.ConnectedClientsIds : new[] { d.NetworkManager.LocalClientId };
        foreach (ulong id in ids)
        {
            int mask = d.GetBackBarrierMask(id);
            sb.Append($" client {id}: 0x{mask:X} ({TutorialBackBarrierRules.Describe(mask)})");
            if (d.IsServer) sb.Append($" [서버 원본 0x{d.GetBackBarrierMaskServer(id):X}]");
            sb.Append(" ·");
        }
        sb.Append($" 단계 {d.Step}");
        Debug.Log(sb.ToString());
    }
}
