using UnityEditor;

/// <summary>
/// Dev Boot 가 스폰할 캐릭터 선택(개인 EditorPrefs). 캐릭터 선택 UI 가 생기기 전까지의 개발용 경로.
/// DevSceneBooter 의 씬 필드(playerPrefabOverride)가 채워져 있으면 그쪽이 우선한다.
/// </summary>
public static class DevBootCharacterMenu
{
    private const string MenuRoot = "Dev/Dev Boot/캐릭터/";
    private const string PaladinPath = "Assets/2.Prefabs/Player/Paladin/Player_Paladin.prefab";
    private const string GunnerPath = "Assets/2.Prefabs/Player/Gunner/Player_Gunner.prefab";
    private const string AssassinPath = "Assets/2.Prefabs/Player/Assassin/Player_Assassin.prefab";

    [MenuItem(MenuRoot + "기본값 (NetworkManager)", priority = 0)]
    private static void UseDefault() => DevBootTarget.PlayerPrefabPath = string.Empty;

    [MenuItem(MenuRoot + "기본값 (NetworkManager)", true)]
    private static bool UseDefaultValidate()
    {
        Menu.SetChecked(MenuRoot + "기본값 (NetworkManager)", string.IsNullOrEmpty(DevBootTarget.PlayerPrefabPath));
        return true;
    }

    [MenuItem(MenuRoot + "가붕이", priority = 11)]
    private static void UsePaladin() => DevBootTarget.PlayerPrefabPath = PaladinPath;

    [MenuItem(MenuRoot + "가붕이", true)]
    private static bool UsePaladinValidate()
    {
        Menu.SetChecked(MenuRoot + "가붕이", DevBootTarget.PlayerPrefabPath == PaladinPath);
        return true;
    }

    [MenuItem(MenuRoot + "거너", priority = 12)]
    private static void UseGunner() => DevBootTarget.PlayerPrefabPath = GunnerPath;

    [MenuItem(MenuRoot + "거너", true)]
    private static bool UseGunnerValidate()
    {
        Menu.SetChecked(MenuRoot + "거너", DevBootTarget.PlayerPrefabPath == GunnerPath);
        return AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(GunnerPath) != null;
    }

    [MenuItem(MenuRoot + "어쌔신", priority = 13)]
    private static void UseAssassin() => DevBootTarget.PlayerPrefabPath = AssassinPath;

    [MenuItem(MenuRoot + "어쌔신", true)]
    private static bool UseAssassinValidate()
    {
        Menu.SetChecked(MenuRoot + "어쌔신", DevBootTarget.PlayerPrefabPath == AssassinPath);
        return AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(AssassinPath) != null;
    }
}
