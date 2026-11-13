using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로비 상단 플레이어 슬롯 한 칸 — 아트 A안(할 일 276, SVN <c>Assets/50.Art/UI/Lobby/Unity/Lobby_A</c>, assembly.json).
/// 상태 3가지(<see cref="LobbySlotState"/>)마다 프레임·마스크 바탕색·링·+·캡슐·아이콘·글자색을 바꾼다.
/// 계층은 저작 메뉴 <c>Tools/UI/Authoring/Lobby Player Slots (A안)</c> 가 만든다 — Docs/tech/lobby-player-slots.md.
/// </summary>
public class LobbyPlayerSlotView : MonoBehaviour
{
    [Header("초상화")]
    [SerializeField] private Image frameImage;
    [SerializeField] private Image portraitMaskImage;
    [Tooltip("PortraitMask 자식. 로스터 faceCrop 으로 원 안에 얼굴이 오게 키우고 옮긴다(PortraitFaceCrop).")]
    [SerializeField] private Image portraitImage;
    [SerializeField] private GameObject readyRing;
    [SerializeField] private GameObject emptyDashedRing;
    [SerializeField] private GameObject emptyPlus;

    [Header("글자·캡슐")]
    [SerializeField] private TMP_Text playerNumberText;
    [SerializeField] private TMP_Text playerNameText;
    [SerializeField] private Image statusCapsuleImage;
    [SerializeField] private Image statusIconImage;
    [SerializeField] private TMP_Text statusText;

    [Header("상태별 그림 (A안)")]
    [SerializeField] private Sprite frameConnected;
    [SerializeField] private Sprite frameDisconnected;
    [SerializeField] private Sprite capsuleReady;
    [SerializeField] private Sprite capsuleWaiting;
    [SerializeField] private Sprite capsuleDisconnected;
    [SerializeField] private Sprite iconCheck;
    [SerializeField] private Sprite iconDot;
    [SerializeField] private Sprite iconDash;

    [Header("상태별 색 (README: 마스크 접속 43,47,52 / 미접속 31,33,35)")]
    [SerializeField] private Color maskConnected = new Color32(43, 47, 52, 255);
    [SerializeField] private Color maskDisconnected = new Color32(31, 33, 35, 255);
    [SerializeField] private Color textConnected = new Color32(219, 219, 219, 255);
    [SerializeField] private Color textDisconnected = new Color32(117, 117, 117, 255);
    [SerializeField] private Color statusConnected = new Color32(181, 181, 181, 255);
    [SerializeField] private Color statusDisconnected = new Color32(115, 115, 115, 255);

    /// <param name="character">초상화·얼굴 크롭 출처. null 이면 빈 초상화.</param>
    /// <param name="playerLabel">배지 글자(P1 등).</param>
    /// <param name="playerName">이름 줄. 미접속이면 무시하고 "플레이어 대기".</param>
    public void SetState(bool connected, bool ready, CharacterRoster.Entry character, string playerLabel, string playerName)
    {
        var state = StateOf(connected, ready);
        bool on = state != LobbySlotState.Disconnected;

        if (frameImage != null) frameImage.sprite = on ? frameConnected : frameDisconnected;
        if (portraitMaskImage != null) portraitMaskImage.color = on ? maskConnected : maskDisconnected;
        SetPortrait(on ? character : null);
        SetActive(readyRing, state == LobbySlotState.Ready);
        SetActive(emptyDashedRing, !on);
        SetActive(emptyPlus, !on);

        SetText(playerNumberText, playerLabel, on ? textConnected : textDisconnected);
        SetText(playerNameText, on ? playerName : LobbyUiStrings.SlotEmptyName.Text, on ? textConnected : textDisconnected);

        if (statusCapsuleImage != null)
            statusCapsuleImage.sprite = state == LobbySlotState.Ready ? capsuleReady : state == LobbySlotState.Waiting ? capsuleWaiting : capsuleDisconnected;
        if (statusIconImage != null)
        {
            statusIconImage.sprite = state == LobbySlotState.Ready ? iconCheck : state == LobbySlotState.Waiting ? iconDot : iconDash;
            var layout = StatusIconLayout(state);
            statusIconImage.rectTransform.anchoredPosition = layout.position;
            statusIconImage.rectTransform.sizeDelta = layout.size;
        }
        SetText(statusText, StatusString(state).Text, on ? statusConnected : statusDisconnected);
    }

    /// <summary>접속·준비 → 슬롯 상태. 미접속이면 준비 값은 무시한다.</summary>
    public static LobbySlotState StateOf(bool connected, bool ready) =>
        !connected ? LobbySlotState.Disconnected : ready ? LobbySlotState.Ready : LobbySlotState.Waiting;

    public static UiString StatusString(LobbySlotState state) =>
        state == LobbySlotState.Ready ? LobbyUiStrings.SlotReady
        : state == LobbySlotState.Waiting ? LobbyUiStrings.SlotWaiting
        : LobbyUiStrings.SlotDisconnected;

    /// <summary>
    /// 캡슐 안 상태 아이콘 자리(캡슐 가운데 기준, 디자인 단위) — assembly.json 의 아이콘 위치 − 캡슐 위치(0.402, -131.4225).
    /// 체크 22×19 · 점 11×11 · 선 15×7.
    /// </summary>
    public static (Vector2 position, Vector2 size) StatusIconLayout(LobbySlotState state)
    {
        switch (state)
        {
            case LobbySlotState.Ready: return (new Vector2(-46.5f, 1f), new Vector2(22f, 19f));
            case LobbySlotState.Waiting: return (new Vector2(-48f, 0f), new Vector2(11f, 11f));
            default: return (new Vector2(-48f, 0f), new Vector2(15f, 7f));
        }
    }

    // 미접속 = 초상화 끔(마스크 바탕색 + 점선 링 + '+'). 접속 = 로스터 초상화를 원 안 얼굴 크롭으로.
    private void SetPortrait(CharacterRoster.Entry character)
    {
        if (portraitImage == null)
            return;

        portraitImage.sprite = character != null ? character.Portrait : null;
        portraitImage.enabled = portraitImage.sprite != null;
        PortraitFaceCrop.Apply(portraitImage, character != null ? character.FaceCrop : PortraitFaceCrop.Full);
    }

    private static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active)
            go.SetActive(active);
    }

    private static void SetText(TMP_Text text, string value, Color color)
    {
        if (text == null)
            return;

        text.text = value ?? string.Empty;
        text.color = color;
    }
}

public enum LobbySlotState
{
    Disconnected,
    Waiting,
    Ready,
}
