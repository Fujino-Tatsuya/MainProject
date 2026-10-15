#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 튜토리얼 디버그 강제 동작 — 에디터 메뉴(<c>Tools/Tutorial/Debug/*</c>)와 런타임 구간 스킵(<see cref="TutorialSkipController"/>)이 함께 쓴다.
/// <b>서버(호스트)에서만</b> 부른다 — 게이트·휴면·피해는 서버 권한. 에디터·Development Build·DEV_HOTKEYS 빌드에만 있다(심볼을 지운 릴리스 빌드엔 클래스가 없다).
/// 로그는 <c>Debug.Log</c> — <c>Edit.*</c> 는 에디터 전용이라 빌드에서 사라진다.
/// </summary>
public static class TutorialDebugActions
{
    /// <summary>휴면은 무적이라 F9(DevMonsterKill)로는 못 죽인다 — 깨운 뒤 이 피해로 처치한다.</summary>
    public const int LethalDamage = 1_000_000;

    static readonly List<MonsterBase> Buffer = new List<MonsterBase>();

    /// <summary>
    /// <paramref name="target"/> 까지 닫힌 게이트를 G1 부터 차례로 강제로 연다(<see cref="TutorialDirector.TryOpenGateServer"/> — 순서 규칙 그대로).
    /// 하나라도 거부되면 거기서 멈춘다. 새로 연 게이트 수.
    /// </summary>
    public static int OpenGatesThrough(TutorialDirector d, TutorialGate target)
    {
        int opened = 0;
        for (int i = 0; i <= (int)target; i++)
        {
            var g = (TutorialGate)i;
            if (d.IsGateOpen(g)) continue;
            if (!d.TryOpenGateServer(g)) break;
            opened++;
        }
        Debug.Log($"[TutorialDebug] {target} 까지 강제 개방 — 새로 연 게이트 {opened}개.");
        return opened;
    }

    /// <summary>존 몬스터(살아 있는 것, 필터)를 깨워서 처치한다 — 장부가 사망을 받아 클리어·게이트를 처리한다. 처치 시도 수.</summary>
    public static int KillZoneMonsters(TutorialDirector d, TutorialArea zone, TutorialMonsterFilter filter)
    {
        d.GetZoneMonstersServer(zone, filter, Buffer);
        // 처치 → 존 클리어 → 게이트 → 흐름 이벤트가 같은 호출 안에서 돈다 — 공용 버퍼를 다시 채울 수 있어 복사해 돈다.
        MonsterBase[] targets = Buffer.ToArray();
        Buffer.Clear();
        Debug.Log($"[TutorialDebug] {zone} {filter} {targets.Length}마리 깨워서 처치");
        foreach (MonsterBase m in targets)
        {
            if (m == null) continue;
            d.SetMonsterDormantServer(m, false);
            m.BreakShield();
            m.TakeDamage(new AttackInfo(LethalDamage, AttackType.Default));
        }
        return targets.Length;
    }
}
#endif
