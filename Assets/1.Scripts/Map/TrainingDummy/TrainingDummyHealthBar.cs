using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 허수아비 머리 위 체력바. 항상 보이고 게이지와 숫자를 같이 띄운다.
/// 월드스페이스 캔버스 자식에 부착하고, 카메라 회전을 그대로 따라가는 화면 정렬 빌보드로 돈다.
///
/// 기존 UnitOverheadHealthBar 를 쓰지 않는 이유 — 그쪽은 GetComponentInParent&lt;Player&gt;() 와
/// "!IsOwner 일 때만 표시"가 박혀 있는 플레이어 전용 컴포넌트다. 범용화하면 원격 플레이어
/// 체력바 표시 규칙에 회귀 위험이 생긴다.
/// </summary>
[DisallowMultipleComponent]
public sealed class TrainingDummyHealthBar : MonoBehaviour
{
    [Tooltip("비워두면 부모에서 찾는다.")]
    [SerializeField] Unit owner;
    [SerializeField] Image hpFill;
    [SerializeField] TMP_Text hpText;
    [SerializeField] string textFormat = "{0} / {1}";

    // 매 프레임 string.Format 을 돌리지 않으려고 마지막으로 그린 값을 기억한다.
    int _shownHp = -1;
    int _shownMaxHp = -1;

    void Awake()
    {
        if (owner == null)
            owner = GetComponentInParent<Unit>();
    }

    void LateUpdate()
    {
        if (owner == null)
            return;

        Camera cam = Camera.main;
        if (cam != null)
            transform.rotation = cam.transform.rotation;

        int maxHp = owner.FinalMaxHp;
        int currentHp = owner.CurrentHealth;

        if (hpFill != null)
            hpFill.fillAmount = maxHp > 0 ? Mathf.Clamp01((float)currentHp / maxHp) : 0f;

        if (hpText != null && (currentHp != _shownHp || maxHp != _shownMaxHp))
        {
            _shownHp = currentHp;
            _shownMaxHp = maxHp;
            hpText.text = string.Format(textFormat, currentHp, maxHp);
        }
    }
}
