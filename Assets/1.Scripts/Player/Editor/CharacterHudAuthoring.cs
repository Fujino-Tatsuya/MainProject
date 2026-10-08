using UnityEngine;

/// <summary>
/// 캐릭터 고유 HUD 규약(PLAN-assassin.md A5 · player-prefabs.md): 캐릭터 전용 UI 프리팹은 <c>&lt;Char&gt;_Armature/HUD</c> 아래에 중첩한다.
/// 각 HUD 는 자체 Canvas 를 갖고 오너 화면에서만 보인다. 유령 상태에서 Armature 가 꺼지면 함께 숨는다.
/// 공용 CombatHUD 는 캐릭터를 모른다 — 캐릭터 고유 표시는 전부 여기로.
/// </summary>
public static class CharacterHudAuthoring
{
    public const string HudRootName = "HUD";

    /// <summary>Armature 루트 바로 아래 <c>HUD</c> 자식을 찾거나 만든다(재실행 안전).</summary>
    public static Transform EnsureHudRoot(Transform armatureRoot)
    {
        Transform hud = armatureRoot.Find(HudRootName);
        if (hud != null)
            return hud;

        var go = new GameObject(HudRootName);
        go.transform.SetParent(armatureRoot, false);
        return go.transform;
    }
}
