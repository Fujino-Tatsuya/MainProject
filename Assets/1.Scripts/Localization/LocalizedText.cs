using TMPro;
using UnityEngine;

/// <summary>
/// 같은 오브젝트의 TMP_Text 에 <see cref="Key"/> 의 현재 언어 문구를 넣는다. 언어가 바뀌면(<see cref="Loc.Changed"/>) 다시 넣는다.
/// 키가 비어 있으면 텍스트를 건드리지 않는다. 자리표시자가 있는 문구는 이 컴포넌트 대신 코드에서 <see cref="Loc.Format"/> 를 쓴다.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public sealed class LocalizedText : MonoBehaviour
{
    [Tooltip("Strings 시트의 key — 예: UI.Lobby.Ready")]
    [SerializeField] private string key;

    private TMP_Text _text;

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

        _text.text = Loc.Get(key);
    }
}
