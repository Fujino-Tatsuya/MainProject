using TMPro;
using UnityEngine;

/// <summary>
/// 준비 오브젝트 위 월드 공간 "준비 n/m" — PLAN-48 §4.6, C10. n = 복제 자리 목록의 Ready 수, m = 자리 수(접속자).
/// <see cref="LobbySession"/> 의 NetworkList 만 읽으므로 모든 피어가 같은 값을 본다. 세션이 없으면 숨긴다.
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyReadyCountLabel : MonoBehaviour
{
    [Tooltip("월드 공간 TextMeshPro(3D). 카메라를 향하게 하는 건 이 오브젝트 회전이다.")]
    [SerializeField] private TMP_Text label;

    private void OnEnable()
    {
        LobbySession.StateChanged += Apply;
        Loc.Changed += Apply;
        Apply();
    }

    private void OnDisable()
    {
        LobbySession.StateChanged -= Apply;
        Loc.Changed -= Apply;
    }

    private void Apply()
    {
        if (label == null)
            return;

        var session = LobbySession.Active;
        var total = session != null ? session.EntryCount : 0;
        label.enabled = total > 0;
        if (total == 0)
            return;

        var ready = 0;
        for (var i = 0; i < total; i++)
        {
            if (session.GetEntry(i).Ready)
                ready++;
        }

        label.text = LobbyUiStrings.FormatReadyCount(ready, total);
    }
}
