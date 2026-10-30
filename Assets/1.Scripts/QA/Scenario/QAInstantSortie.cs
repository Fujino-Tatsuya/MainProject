using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 할 일 170 — QA 실행 동안만 로비 시연 즉시 출격(<see cref="LobbyRules.AllowInstantSortie"/>)을 런타임에 켠다(QA 전용, 게임 코드 무수정).
/// 157 로비는 회차마다 L00(라커 방문)부터라 출격 구역이 L40 전엔 꺼져 있다 — 켜 두면 명단 고정 전(L00·L10)에도 출격 구역이 켜지고
/// 출격 집결 확정 = 훈련 없이 메인 진입이라 봇이 튜토리얼 없이 출격 집결(<see cref="QASortieGather"/>)로 바로 간다.
/// <list type="bullet">
/// <item>대상: 메모리에 올라온 <see cref="LobbyRules"/> 전부(진행기가 실제 판정에 쓰는 인스턴스 포함 — 호스트가 판정, 클라는 표시만).
/// 직렬화 private 칸 <c>allowInstantSortie</c> 를 리플렉션으로 바꾼다(SetDirty 없음 — 에셋 저장 안 함). LobbyRules 는 데이터 테이블 대상이 아니라 xlsx 가 덮어쓰지 않는다.</item>
/// <item>켜는 시점: 출격 집결 시작마다(<see cref="QASortieGather.Gather"/> — 10분 시나리오·GoToMap·P572·103 Sortie103, 사이클 2·3 로비 복귀 뒤 포함)와 10분 세션 시작.
/// 진행기는 구역을 단계 전이 때만 다시 켜므로, 이미 스폰된 서버 진행기는 구역 갱신 표시(<c>_zonesDirty</c>)를 세운다.</item>
/// <item>복원: <see cref="Restore"/> — Play 종료(<see cref="Application.quitting"/>)·10분 세션 오브젝트 파괴. 바꾼 인스턴스만 원래 값으로.
/// 에디터에서 SO 런타임 값은 Play 뒤에도 메모리에 남아 다음 저장에 섞일 수 있으니 반드시 되돌린다.</item>
/// </list>
/// </summary>
public static class QAInstantSortie
{
    private static readonly FieldInfo AllowField =
        typeof(LobbyRules).GetField("allowInstantSortie", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo ZonesDirtyField =
        typeof(LobbyTrainingDirector).GetField("_zonesDirty", BindingFlags.Instance | BindingFlags.NonPublic);

    // 바꾼 인스턴스 → 원래 값.
    private static readonly Dictionary<LobbyRules, bool> Originals = new Dictionary<LobbyRules, bool>();
    private static bool _quitHooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Originals.Clear();
        _quitHooked = false;
    }

    /// <summary>바꿔 둔 인스턴스가 있고 그 값이 켜져 있는가(로그용).</summary>
    public static bool IsOn
    {
        get
        {
            foreach (KeyValuePair<LobbyRules, bool> e in Originals)
                if (e.Key != null && e.Key.AllowInstantSortie)
                    return true;
            return false;
        }
    }

    /// <summary>메모리의 LobbyRules 전부 즉시 출격 켬(이미 켜졌으면 그대로). 서버 진행기가 있으면 구역을 다시 맞추게 한다. 리플렉션이 깨졌으면 경고하고 false.</summary>
    public static bool Enable(string reason)
    {
        if (AllowField == null)
        {
            Debug.LogWarning("[QA] event=instant_sortie ok=false why=LobbyRules.allowInstantSortie_칸_없음");
            return false;
        }

        if (!_quitHooked)
        {
            _quitHooked = true;
            Application.quitting += Restore;
        }

        int changed = 0, total = 0;
        foreach (LobbyRules rules in Resources.FindObjectsOfTypeAll<LobbyRules>())
        {
            total++;
            if (rules.AllowInstantSortie)
                continue;
            if (!Originals.ContainsKey(rules))
                Originals[rules] = false;
            AllowField.SetValue(rules, true);
            changed++;
        }

        bool poked = false;
        LobbyTrainingDirector d = LobbyTrainingDirector.Active;
        if (changed > 0 && d != null && d.IsServer && ZonesDirtyField != null)
        {
            ZonesDirtyField.SetValue(d, true);
            poked = true;
        }

        if (changed > 0)
            Debug.Log($"[QA] event=instant_sortie on=true reason={reason} rules={total} changed={changed} zonesDirty={(poked ? "true" : "false")} {QASortieGather.Stage()}");
        return total > 0;
    }

    /// <summary>바꾼 인스턴스를 원래 값으로 되돌린다(여러 번 불러도 된다).</summary>
    public static void Restore()
    {
        if (Originals.Count == 0 || AllowField == null)
            return;

        int restored = 0;
        foreach (KeyValuePair<LobbyRules, bool> e in Originals)
        {
            if (e.Key == null)
                continue;
            AllowField.SetValue(e.Key, e.Value);
            restored++;
        }
        Originals.Clear();
        Debug.Log($"[QA] event=instant_sortie on=false restored={restored}");
    }
}
