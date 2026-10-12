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

    /// <summary>
    /// 표에 키가 있으면 현재 언어 문구, 없으면(빈 키·표 없음 포함) <paramref name="fallback"/> — <c>#키</c> 를 내지 않는다.
    /// SO 에 인라인 ko 문구가 있는 자동 키(<see cref="LocKey"/>)용: 시트에 행이 아직 없어도 화면이 깨지지 않는다.
    /// </summary>
    public static string Get(string key, string fallback)
    {
        if (string.IsNullOrEmpty(key))
            return fallback;

        StringTable current = Table;
        return current != null && current.TryGet(key, Current, out string text) ? text : fallback;
    }

    /// <summary><c>{이름}</c> 자리표시자를 args 로 채운다. 치환 규칙은 <see cref="TutorialGuideText.Fill"/>(없는 이름은 그대로 남김).</summary>
    public static string Format(string key, IReadOnlyDictionary<string, string> args) =>
        TutorialGuideText.Fill(Get(key), args);

    /// <summary>
    /// 로비·네트워크 오류 문구(ErrorCodes 시트). 코드가 없으면 <c>#코드</c> — <see cref="Get"/> 과 같은 규칙.
    /// args 가 있으면 <see cref="Format"/> 처럼 <c>{이름}</c> 을 채운다.
    /// </summary>
    public static string GetError(string code, IReadOnlyDictionary<string, string> args = null)
    {
        if (string.IsNullOrEmpty(code))
            return MissingPrefix;

        StringTable current = Table;
        string text = current != null && current.TryGetError(code, Current, out string found) ? found : MissingPrefix + code;
        return TutorialGuideText.Fill(text, args);
    }

    /// <summary>
    /// <see cref="GetError(string, IReadOnlyDictionary{string, string})"/> 와 같지만 코드가 없으면(빈 코드·표 없음 포함)
    /// <c>#코드</c> 대신 <paramref name="fallback"/>(코드에 적힌 ko 원문)에 args 를 채운다 — 시트에 행이 생기기 전에도 화면이 안 깨진다.
    /// </summary>
    public static string GetError(string code, string fallback, IReadOnlyDictionary<string, string> args)
    {
        StringTable current = string.IsNullOrEmpty(code) ? null : Table;
        string text = current != null && current.TryGetError(code, Current, out string found) ? found : fallback;
        return TutorialGuideText.Fill(text, args);
    }

    // 도메인 리로드 없는 Play 진입에서 지난 세션 구독자가 남지 않게. 표는 에셋 참조라 그대로 둔다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSubscribers()
    {
        Changed = null;
    }
}
