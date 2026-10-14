using TMPro;
using UnityEngine;

/// <summary>
/// 같은 오브젝트의 TMP_Text 에 <see cref="Key"/> 의 현재 언어 문구를 넣는다. 언어가 바뀌면(<see cref="Loc.Changed"/>) 다시 넣는다.
/// 시트에 행이 없으면 켜질 때 저작된 글자(씬·프리팹의 ko 원문)를 그대로 둔다 — 저작 글자가 비었을 때만 <c>#키</c>.
/// 키가 비어 있으면 텍스트를 건드리지 않는다. 자리표시자가 있는 문구는 이 컴포넌트 대신 코드에서 <see cref="Loc.Format"/> 를 쓴다.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public sealed class LocalizedText : MonoBehaviour
{
    [Tooltip("Strings 시트의 key — 예: UI.Lobby.Ready")]
    [SerializeField] private string key;

    private TMP_Text _text;
    private string _fallback;

    public string Key
    {
        get => key;
        set
        {
            key = value;
            Refresh();
        }
    }

    private void Awake()
    {
        _text = GetComponent<TMP_Text>();
        _fallback = _text != null ? _text.text : null;
    }

    private void OnEnable()
    {
        Loc.Changed += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        Loc.Changed -= Refresh;
    }

    public void Refresh()
    {
        if (string.IsNullOrEmpty(key) || _text == null)
            return;

        _text.text = Text(key, _fallback);
    }

    /// <summary>키 문구. 시트에 행이 없으면 저작된 글자(ko 원문), 그것도 비면 <c>#키</c>.</summary>
    public static string Text(string key, string authoredText) =>
        string.IsNullOrEmpty(authoredText) ? Loc.Get(key) : Loc.Get(key, authoredText);
}
