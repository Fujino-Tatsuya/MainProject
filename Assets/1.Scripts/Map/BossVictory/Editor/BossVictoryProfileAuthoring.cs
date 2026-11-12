using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 캐릭터별 프로필 저작(할 일 267). 메뉴 <c>Tools/Boss Victory/캐릭터별 프로필 만들기 (267)</c> —
/// 기본 프로필이 없으면 코드 기본값으로 만들고, 표에 프로필이 없는 캐릭터(팔라딘·거너·어쌔신)는 기본 프로필을 복제해 만든 뒤 표에 넣는다.
/// 이미 있는 것은 건너뛴다(덮어쓰지 않음). 타임라인 창 '이 캐릭터 프로필 만들기' 버튼도 같은 길.
/// </summary>
public static class BossVictoryProfileAuthoring
{
    const string SettingsPath = "Assets/Resources/BossVictory/BossVictoryGlitchSettings.asset";
    public const string Folder = "Assets/Resources/BossVictory/Profiles";

    static readonly TutorialCharacter[] Characters = { TutorialCharacter.Paladin, TutorialCharacter.Gunner, TutorialCharacter.Assassin };

    public static string ProfilePath(TutorialCharacter character) =>
        $"{Folder}/BossVictoryProfile_{(character == TutorialCharacter.None ? "Default" : character.ToString())}.asset";

    [MenuItem("Tools/Boss Victory/캐릭터별 프로필 만들기 (267)")]
    public static void CreateMissing()
    {
        var settings = AssetDatabase.LoadAssetAtPath<BossVictoryGlitchSettings>(SettingsPath);
        if (settings == null)
        {
            Debug.LogError($"[BossVictory] 설정 에셋이 없다 — {SettingsPath}");
            return;
        }
        int made = 0;
        if (settings.defaultProfile == null)
        {
            Undo.RecordObject(settings, "보스 처치 연출 기본 프로필 만들기");
            settings.defaultProfile = CreateAsset(ProfilePath(TutorialCharacter.None), null);
            made++;
        }
        foreach (TutorialCharacter c in Characters)
        {
            if (settings.HasOwnProfile(c)) continue;
            CreateCharacterProfile(settings, c);
            made++;
        }
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssetIfDirty(settings);
        Debug.Log($"[BossVictory] 캐릭터별 프로필 — 새로 만든 것 {made}개(이미 있는 것은 건너뜀). 표: {SettingsPath}");
    }

    /// <summary>그 캐릭터 프로필을 기본 프로필 복제로 만들고 표에 넣는다(빈 줄이 있으면 그 줄을 채움). 만든 프로필.</summary>
    public static BossVictoryProfile CreateCharacterProfile(BossVictoryGlitchSettings settings, TutorialCharacter character)
    {
        string path = ProfilePath(character);
        BossVictoryProfile profile = AssetDatabase.LoadAssetAtPath<BossVictoryProfile>(path);
        if (profile == null) profile = CreateAsset(path, settings.defaultProfile);
        Undo.RecordObject(settings, "보스 처치 연출 캐릭터 프로필 만들기");
        int row = settings.characterProfiles.FindIndex(r => r.character == character);
        var entry = new BossVictoryGlitchSettings.CharacterProfile { character = character, profile = profile };
        if (row >= 0) settings.characterProfiles[row] = entry;
        else settings.characterProfiles.Add(entry);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssetIfDirty(settings);
        Debug.Log($"[BossVictory] {character} 프로필 — {path}({(settings.defaultProfile != null ? "기본 복제" : "코드 기본값")})", profile);
        return profile;
    }

    static BossVictoryProfile CreateAsset(string path, BossVictoryProfile copyFrom)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var p = ScriptableObject.CreateInstance<BossVictoryProfile>();
        if (copyFrom != null) p.CopyFrom(copyFrom);
        p.name = Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(p, AssetDatabase.GenerateUniqueAssetPath(path));
        AssetDatabase.SaveAssets();
        return p;
    }
}
