using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

// 보스룸 이동 관리자 (PLAN §6 개정). 씬 상주 NetworkObject — 이 오브젝트의 위치가 텔레포트 지점이다.
//
// 흐름(서버 권한):
//  1) BossEnterTrigger가 존 점유(생존 플레이어 유무)를 통지 — 점유 시작 → 카운트다운,
//     전원 이탈/전멸 → 취소·리셋(로아식, 재진입 시 재시작).
//  2) 만료 시각(서버시간)을 NetworkVariable로 복제 → 전 피어가 동일한 3·2·1 표시.
//  3) 만료 시 서버가 "생존" 플레이어 전원을 이 위치 주변으로 산개 텔레포트.
//     이동하는 본인 화면은 이동 직전 암전 → 이동 → 밝아짐(로컬 연출).
//
// 진입 패드 크기/표시 색/페이드는 전부 이 컴포넌트 인스펙터에서 튜닝한다(팀장 확정) —
// 범위 표시(BossEnterZoneVisual)는 런타임 부착이라 씬에서 직접 만질 수 없기 때문.
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class BossTeleportManager : NetworkBehaviour
{
    [Header("카운트다운/산개")]
    [SerializeField, Min(0.5f)] private float countdownSeconds = 3f;
    [SerializeField, Min(0f)] private float scatterRadius = 2f;

    [Header("진입 패드 (존 중앙 트리거+테두리 크기, m)")]
    [SerializeField] private Vector2 enterPadSize = new Vector2(6f, 6f);

    [Header("범위 표시 색")]
    [SerializeField] private Color idleColor = new Color(0.25f, 0.8f, 1f, 0.9f);
    [SerializeField] private Color activeColor = new Color(0.35f, 1f, 0.4f, 1f);

    [Header("이동 페이드 (본인 화면만)")]
    [SerializeField, Min(0.05f)] private float fadeOutSeconds = 0.3f;
    [SerializeField, Min(0.05f)] private float fadeInSeconds = 0.5f;
    [SerializeField] private Color fadeColor = Color.black;

    // 텔레포트 만료 시각(서버시간). 0 = 비활성. 서버 write / 모두 read.
    private readonly NetworkVariable<double> _teleportAt = new NetworkVariable<double>(
        0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 존 점유 여부(범위 표시 색 전환용 복제). 서버 write / 모두 read.
    private readonly NetworkVariable<bool> _occupied = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Coroutine _pending;
    private GUIStyle _countdownStyle;

    // 로컬 페이드 상태(이동하는 본인 화면 전용 연출).
    private float _fadeAlpha;
    private bool _fadingIn;

    public static BossTeleportManager Instance { get; private set; }

    /// <summary>존 안에 생존 플레이어가 있는지 (모든 피어에서 유효 — 범위 표시가 읽는다).</summary>
    public bool IsOccupied => _occupied.Value;

    /// <summary>카운트다운 진행 중인지 (모든 피어에서 유효).</summary>
    public bool IsCountdownActive => _teleportAt.Value > 0d;

    public Vector2 EnterPadSize => enterPadSize;
    public Color IdleColor => idleColor;
    public Color ActiveColor => activeColor;

    private void Awake()
    {
        Instance = this;
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    /// <summary>
    /// 존 점유 상태 통지(서버 전용). BossEnterTrigger가 호출한다.
    /// 점유 시작 → 카운트다운 시작 / 전원 이탈 → 카운트다운 취소(로아식, 팀장 확정).
    /// </summary>
    public void SetOccupied(bool occupied)
    {
        if (!IsServer || _occupied.Value == occupied) return;
        _occupied.Value = occupied;

        if (occupied)
        {
            if (_pending != null) return;
            _teleportAt.Value = NetworkManager.ServerTime.Time + countdownSeconds;
            _pending = StartCoroutine(TeleportAfter(countdownSeconds));
            Edit.Log($"[BossTeleport] 카운트다운 시작 — {countdownSeconds:0}초 후 생존자 전원 보스룸 이동.", this);
        }
        else if (_pending != null)
        {
            StopCoroutine(_pending);
            _pending = null;
            _teleportAt.Value = 0d;
            Edit.Log("[BossTeleport] 존 이탈 — 카운트다운 취소.", this);
        }
    }

    private IEnumerator TeleportAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        TeleportAlivePlayers();
        _pending = null;
        _teleportAt.Value = 0d; // 표시 종료
    }

    private void TeleportAlivePlayers()
    {
        if (!IsServer) return;

        int index = 0;
        foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
        {
            NetworkObject playerObject = client.PlayerObject;
            if (playerObject == null) continue;

            // 생존자만 이동(팀장 확정). 사망자는 현 위치에 남는다.
            Unit unit = playerObject.GetComponent<Unit>();
            if (unit == null || unit.CurrentHealth <= 0) continue;

            Vector3 destination = GetScatterPosition(index++);

            if (playerObject.TryGetComponent(out NetworkTransform netTransform))
                netTransform.Teleport(destination, playerObject.transform.rotation, playerObject.transform.localScale);

            TeleportOwnerClientRpc(destination, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { client.ClientId } }
            });
        }

        Edit.Log($"[BossTeleport] 생존자 {index}명 보스룸 이동 완료 @ {transform.position}", this);
    }

    // 오너 로컬에서도 위치를 강제 — 오너 권한 이동 구성에서 서버 텔레포트가 되돌려지는 것 방지.
    // 도착 직후 페이드인 시작(이동 직전 암전은 로컬에서 만료 시각 기준 선제 진행).
    [ClientRpc]
    private void TeleportOwnerClientRpc(Vector3 destination, ClientRpcParams rpcParams = default)
    {
        NetworkObject playerObject = NetworkManager.LocalClient?.PlayerObject;
        if (playerObject == null) return;

        if (playerObject.TryGetComponent(out Rigidbody rb))
        {
            rb.linearVelocity = Vector3.zero;
            rb.position = destination;
        }
        playerObject.transform.position = destination;

        _fadeAlpha = 1f;
        _fadingIn = true;
    }

    private Vector3 GetScatterPosition(int index)
    {
        if (index == 0 || scatterRadius <= 0f)
            return transform.position;

        float angle = index * 137f * Mathf.Deg2Rad; // 황금각 산개 — 인원수 몰라도 겹침 최소
        return transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * scatterRadius;
    }

    // 로컬 페이드 진행 — 이동 대상(생존한 본인)만 만료 직전 암전, 취소 시 복구.
    private void Update()
    {
        if (_fadingIn)
        {
            _fadeAlpha -= Time.deltaTime / fadeInSeconds;
            if (_fadeAlpha <= 0f) { _fadeAlpha = 0f; _fadingIn = false; }
            return;
        }

        double teleportAt = _teleportAt.Value;
        if (teleportAt > 0d && IsLocalPlayerAlive())
        {
            double remain = teleportAt - NetworkManager.Singleton.ServerTime.Time;
            if (remain <= fadeOutSeconds)
                _fadeAlpha = Mathf.Clamp01(1f - (float)remain / fadeOutSeconds);
        }
        else if (_fadeAlpha > 0f)
        {
            // 취소됨 — 어두워지던 화면을 빠르게 복구.
            _fadeAlpha = Mathf.MoveTowards(_fadeAlpha, 0f, Time.deltaTime / fadeOutSeconds);
        }
    }

    private bool IsLocalPlayerAlive()
    {
        NetworkManager nm = NetworkManager.Singleton;
        NetworkObject po = nm != null ? nm.LocalClient?.PlayerObject : null;
        if (po == null) return false;
        Unit unit = po.GetComponent<Unit>();
        return unit != null && unit.CurrentHealth > 0;
    }

    // 임시 표시(전 피어): 페이드 오버레이 + 카운트다운 숫자 — UI 시스템 합류 시 교체 전제.
    private void OnGUI()
    {
        if (_fadeAlpha > 0f)
        {
            Color c = fadeColor;
            c.a = _fadeAlpha;
            GUI.color = c;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        double teleportAt = _teleportAt.Value;
        if (teleportAt <= 0d || NetworkManager.Singleton == null) return;

        double remain = teleportAt - NetworkManager.Singleton.ServerTime.Time;
        if (remain <= 0d || remain > countdownSeconds + 0.5d) return;

        _countdownStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 96,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
        };
        _countdownStyle.normal.textColor = Color.white;

        int display = Mathf.CeilToInt((float)remain);
        Rect rect = new Rect(0f, Screen.height * 0.25f, Screen.width, 120f);
        GUI.Label(rect, display.ToString(), _countdownStyle);
    }
}
