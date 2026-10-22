using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// TRAIN_01 무기 점검 표시(할 일 86 T3, 명세 v0.2 §7 "개인 체크 6개와 파티 완료 인원") — 로컬 플레이어 기준.
/// <see cref="LobbyTrainingWeaponCheck"/> 의 복제 체크만 읽으므로 늦게 그린 클라·재접속도 지금 상태를 본다.
/// 점검 진행 중이고 내가 훈련 명단일 때만 <see cref="root"/> 를 켠다(늦은 참가자는 표시 없음). 완료자는 계속 연습 가능 — 표시만 유지.
/// 로비에는 튜토리얼 목표 패널(TutorialGuideUI)이 없어(T6 에서 배치) 간단한 문구 뷰로 둔다.
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyWeaponCheckView : MonoBehaviour
{
    [Tooltip("켜고 끄는 표시 본체(이 컴포넌트가 붙은 오브젝트 말고 자식).")]
    [SerializeField] private GameObject root;

    [Tooltip("제목(\"무기 점검\"). 비워도 된다.")]
    [SerializeField] private TMP_Text titleLabel;

    [Tooltip("개인 체크 6줄(기본 공격·Q·E·R·우클릭 스킬·대시) — 마친 항목은 흐리게 취소선.")]
    [SerializeField] private TMP_Text checklistLabel;

    [Tooltip("\"완료 n/m\"(파티 완료 인원).")]
    [SerializeField] private TMP_Text partyLabel;

    private const string DoneColor = "#8A8A8A";

    private readonly StringBuilder _builder = new StringBuilder();
    private int _shownMask = -1;
    private int _shownDone = -1;
    private int _shownTotal = -1;

    private void OnEnable()
    {
        Loc.Changed += Invalidate;
        LobbyTrainingWeaponCheck.Changed += Invalidate;
    }

    private void OnDisable()
    {
        Loc.Changed -= Invalidate;
        LobbyTrainingWeaponCheck.Changed -= Invalidate;
        SetVisible(false);
    }

    private void Update()
    {
        var check = LobbyTrainingWeaponCheck.Active;
        var nm = Unity.Netcode.NetworkManager.Singleton;
        var mask = 0;
        var visible = check != null && nm != null && check.IsRunning && check.TryGetMask(nm.LocalClientId, out mask);
        SetVisible(visible);
        if (!visible)
            return;

        check.GetPartyProgress(out var done, out var total);
        if (mask == _shownMask && done == _shownDone && total == _shownTotal)
            return;

        _shownMask = mask;
        _shownDone = done;
        _shownTotal = total;

        if (titleLabel != null)
            titleLabel.text = LobbyUiStrings.Train01Title.Text;

        if (checklistLabel != null)
        {
            _builder.Clear();
            foreach (var item in LobbyWeaponCheckRules.DisplayOrder)
            {
                if (_builder.Length > 0)
                    _builder.Append('\n');

                var name = LobbyUiStrings.Train01Item(item).Text;
                if (LobbyWeaponCheckRules.Has(mask, item))
                    _builder.Append("<color=").Append(DoneColor).Append("><s>").Append(name).Append("</s></color>");
                else
                    _builder.Append(name);
            }

            checklistLabel.text = _builder.ToString();
        }

        if (partyLabel != null)
            partyLabel.text = LobbyUiStrings.FormatTrain01PartyDone(done, total);
    }

    private void Invalidate()
    {
        _shownMask = -1;
        _shownDone = -1;
        _shownTotal = -1;
    }

    private void SetVisible(bool visible)
    {
        if (!visible)
            Invalidate();
        if (root != null && root.activeSelf != visible)
            root.SetActive(visible);
    }
}
