using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 문구 조회 창구. <c>Loc.Get("UI.Lobby.Ready")</c> → 현재 언어 문구. 키가 없으면 <c>#UI.Lobby.Ready</c> 를 돌려준다
/// (화면에 그대로 보여 빠진 키를 바로 찾게).
/// 언어는 <see cref="Current"/>(저장 = <see cref="UserLanguageConfig"/>). 바꾸면 <see cref="Changed"/> 가 나가고,
/// <see cref="LocalizedText"/>·<see cref="LocalizedImage"/> 가 그걸 듣고 다시 그린다.
/// 상세: Docs/tech/localization.md.
/// </summary>
public static class Loc
{
    public const string MissingPrefix = "#";

    private static StringTable table;
    private static bool loadAttempted;

    /// <summary>언어가 바뀌거나 표가 새로 걸렸을 때.</summary>
    public static event Action Changed;

    public static GameLanguage Current
    {
        get => UserLanguageConfig.Language;
        set
        {
            if (UserLanguageConfig.Language == value)
                return;

            UserLanguageConfig.Language = value;
            Changed?.Invoke();
        }
    }

    /// <summary>지금 쓰는 표. 에디터에서는 처음 조회 때 <see cref="StringTable.AssetPath"/> 를 읽는다.</summary>
    public static StringTable Table
    {
        get
        {
            if (table == null && !loadAttempted)
            {
                loadAttempted = true;
#if UNITY_EDITOR
                table = UnityEditor.AssetDatabase.LoadAssetAtPath<StringTable>(StringTable.AssetPath);
#endif
            }

            return table;
        }
    }

    /// <summary>표를 건다(빌드 시작 때 StringTable 자신이, 테스트는 임시 표로). null 이면 다음 조회 때 기본 위치를 다시 읽는다.</summary>
    public static void SetTable(StringTable newTable)
    {
        table = newTable;
        loadAttempted = newTable != null;
        Changed?.Invoke();
    }

    public static string Get(string key)
    {
        if (string.IsNullOrEmpty(key))
            return MissingPrefix;

        StringTable current = Table;
        return current != null && current.TryGet(key, Current, out string text) ? text : MissingPrefix + key;
    }

    /// <summary><c>{이름}</c> 자리표시자를 args 로 채운다. 치환 규칙은 <see cref="TutorialGuideText.Fill"/>(없는 이름은 그대로 남김).</summary>
    public static string Format(string key, IReadOnlyDictionary<string, string> args) =>
        TutorialGuideText.Fill(Get(key), args);

    // 도메인 리로드 없는 Play 진입에서 지난 세션 구독자가 남지 않게. 표는 에셋 참조라 그대로 둔다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSubscribers()
    {
        Changed = null;
    }
}
