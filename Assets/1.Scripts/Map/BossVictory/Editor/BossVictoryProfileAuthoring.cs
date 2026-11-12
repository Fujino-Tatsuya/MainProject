using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 캐릭터별 프로필 저작(할 일 267). 메뉴 <c>Tools/Boss Victory/캐릭터별 프로필 만들기 (267)</c> —
/// 표에 프로필이 없는 캐릭터(팔라딘·거너·어쌔신)를 <b>코드 기본값</b>(<see cref="BossVictoryProfile.CreateDefault"/> 와 같은 값)으로 만들어 표에 넣는다.
/// 이미 있는 것은 건너뛴다(덮어쓰지 않음). 타임라인 창 '이 캐릭터 프로필 만들기' 버튼도 같은 길.
/// 기본 프로필 에셋은 만들지 않는다(11-12 삭제 — 표에 없는 캐릭터는 코드 기본값). 복제 원본을 코드 기본값으로 둔 이유: 다른 캐릭터 프로필(예: 팔라딘)을 복제하면
/// 그 캐릭터 전용 칸(손 검 번개·낙하 검 배율·백대쉬 동시 등)까지 딸려 온다. 비슷하게 시작하려면 창 '다른 캐릭터에 복사' 로 노드를 옮긴다.
/// </summary>
public static class BossVictoryProfileAuthoring
{
    const string SettingsPath = "Assets/Resources/BossVictory/BossVictoryGlitchSettings.asset";
    public const string Folder = "Assets/Resources/BossVictory/Profiles";

    static readonly TutorialCharacter[] Characters = { TutorialCharacter.Paladin, TutorialCharacter.Gunner, TutorialCharacter.Assassin };

    public static string ProfilePath(TutorialCharacter character) => $"{Folder}/BossVictoryProfile_{character}.asset";

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
        foreach (TutorialCharacter c in Characters)
        {
            if (settings.HasOwnProfile(c)) continue;
            CreateCharacterProfile(settings, c);
            made++;
        }
        Debug.Log($"[BossVictory] 캐릭터별 프로필 — 새로 만든 것 {made}개(이미 있는 것은 건너뜀). 표: {SettingsPath}");
    }

    /// <summary>그 캐릭터 프로필을 코드 기본값으로 만들고(파일이 이미 있으면 그 파일) 표에 넣는다(빈 줄이 있으면 그 줄을 채움). 만든 프로필. None 은 만들지 않는다(null).</summary>
    public static BossVictoryProfile CreateCharacterProfile(BossVictoryGlitchSettings settings, TutorialCharacter character)
    {
        if (character == TutorialCharacter.None) return null;
        string path = ProfilePath(character);
        BossVictoryProfile profile = AssetDatabase.LoadAssetAtPath<BossVictoryProfile>(path);
        if (profile == null) profile = CreateAsset(path);
        Undo.RecordObject(settings, "보스 처치 연출 캐릭터 프로필 만들기");
        int row = settings.characterProfiles.FindIndex(r => r.character == character);
        var entry = new BossVictoryGlitchSettings.CharacterProfile { character = character, profile = profile };
        if (row >= 0) settings.characterProfiles[row] = entry;
        else settings.characterProfiles.Add(entry);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssetIfDirty(settings);
        Debug.Log($"[BossVictory] {character} 프로필 — {path}(코드 기본값)", profile);
        return profile;
    }

    static BossVictoryProfile CreateAsset(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var p = ScriptableObject.CreateInstance<BossVictoryProfile>();
        p.name = Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(p, AssetDatabase.GenerateUniqueAssetPath(path));
        AssetDatabase.SaveAssets();
        return p;
    }
}
