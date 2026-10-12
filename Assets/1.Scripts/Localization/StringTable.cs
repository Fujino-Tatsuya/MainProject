using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 런타임 문구 표 — GameData.xlsx 의 <c>Strings</c>·<c>ErrorCodes</c> 시트에서 만든다(손으로 고치지 않는다).
/// 두 시트는 한 에셋의 다른 칸이다 — UI 문구는 <see cref="Entries"/>(키), 로비·네트워크 오류 문구는 <see cref="Errors"/>(오류 코드).
/// 키 공간이 따로라 같은 문자열이 양쪽에 있어도 된다.
/// Play 진입·빌드 때 <c>StringTableBuild</c> 가 검증을 통과한 내용으로 <see cref="AssetPath"/> 를 갱신한다.
/// en 빈 칸은 생성 때 ko 로 채워 둔다. 읽기는 <see cref="Loc"/> 로만 한다.
/// <para>
/// 빌드에서는 Preloaded Assets 로 들어가(빌드 동안만 등록) 시작 때 <see cref="OnEnable"/> 이 자신을 <see cref="Loc"/> 에 건다.
/// 에디터에서는 <see cref="Loc"/> 가 AssetDatabase 로 직접 읽는다.
/// </para>
/// </summary>
public sealed class StringTable : ScriptableObject
{
    public const string AssetPath = "Assets/9.ScriptableObject/Localization/StringTable.asset";

    [Serializable]
    public struct Entry
    {
        public string key;
        public string ko;
        public string en;

        public Entry(string key, string ko, string en)
        {
            this.key = key;
            this.ko = ko;
            this.en = en;
        }
    }

    [SerializeField] private List<Entry> entries = new List<Entry>();

    [Tooltip("ErrorCodes 시트 — key = 오류 코드.")]
    [SerializeField] private List<Entry> errors = new List<Entry>();

    private Dictionary<string, int> _index;
    private Dictionary<string, int> _errorIndex;

    /// <summary>Strings 시트.</summary>
    public IReadOnlyList<Entry> Entries => entries;

    /// <summary>ErrorCodes 시트(key = 오류 코드).</summary>
    public IReadOnlyList<Entry> Errors => errors;

    /// <summary>Strings 내용을 통째로 바꾼다(생성기·테스트용). 조회 캐시도 버린다.</summary>
    public void SetEntries(IEnumerable<Entry> newEntries)
    {
        entries = new List<Entry>(newEntries);
        _index = null;
    }

    /// <summary>ErrorCodes 내용을 통째로 바꾼다(생성기·테스트용). 조회 캐시도 버린다.</summary>
    public void SetErrors(IEnumerable<Entry> newErrors)
    {
        errors = new List<Entry>(newErrors);
        _errorIndex = null;
    }

    /// <summary>키의 문구. en 이 비어 있으면 ko. 키가 없으면 false.</summary>
    public bool TryGet(string key, GameLanguage language, out string text) =>
        Lookup(entries, ref _index, key, language, out text);

    /// <summary>오류 코드의 문구. en 이 비어 있으면 ko. 코드가 없으면 false.</summary>
    public bool TryGetError(string code, GameLanguage language, out string text) =>
        Lookup(errors, ref _errorIndex, code, language, out text);

    private static bool Lookup(List<Entry> list, ref Dictionary<string, int> index, string key, GameLanguage language, out string text)
    {
        if (index == null)
        {
            index = new Dictionary<string, int>(list.Count, StringComparer.Ordinal);
            for (int i = 0; i < list.Count; i++)
            {
                if (!string.IsNullOrEmpty(list[i].key))
                    index[list[i].key] = i;
            }
        }

        if (key == null || !index.TryGetValue(key, out int at))
        {
            text = null;
            return false;
        }

        Entry entry = list[at];
        text = language == GameLanguage.En && !string.IsNullOrEmpty(entry.en) ? entry.en : entry.ko ?? string.Empty;
        return true;
    }

    private void OnEnable()
    {
        _index = null;
        _errorIndex = null;
#if !UNITY_EDITOR
        Loc.SetTable(this);
#endif
    }
}
