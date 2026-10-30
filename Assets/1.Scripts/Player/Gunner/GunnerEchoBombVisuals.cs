using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 잔류 포격 에너지 폭탄 1개의 복제 상태(할 일 149) — <c>GunnerBeamView</c> 의 NetworkList 항목.
/// 피해·폭발 시각은 서버만 든다(<see cref="GunnerEchoBombs"/>). 클라는 이걸로 붙은 구체만 그린다 — 늦게 합류해도 목록이 그대로 온다.
/// </summary>
public struct GunnerEchoBombState : INetworkSerializable, IEquatable<GunnerEchoBombState>
{
    public int BombId;
    public ulong TargetId;

    public GunnerEchoBombState(int bombId, ulong targetId)
    {
        BombId = bombId;
        TargetId = targetId;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref BombId);
        s.SerializeValue(ref TargetId);
    }

    public bool Equals(GunnerEchoBombState o) => BombId == o.BombId && TargetId == o.TargetId;
}

/// <summary>잔류 포격 폭탄 대상 찾기 — 서버(피해·생존 확인)와 클라(구체 위치)가 같은 함수를 쓴다.</summary>
public static class GunnerEchoBombTargets
{
    /// <summary>네트워크 ID → 스폰된 유닛(없거나 디스폰이면 null).</summary>
    public static Unit Find(ulong networkObjectId)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || manager.SpawnManager == null ||
            !manager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject networkObject) || networkObject == null)
            return null;

        return networkObject.TryGetComponent(out Unit unit) ? unit : networkObject.GetComponentInChildren<Unit>();
    }

    /// <summary>[서버] 폭탄이 터질 수 있는 대상인가 — 스폰돼 있고 살아 있다.</summary>
    public static bool IsAlive(ulong networkObjectId)
    {
        Unit unit = Find(networkObjectId);
        return unit != null && unit.IsSpawned && unit.CurrentHealth > 0;
    }

    /// <summary>
    /// 구체가 붙는 점 — 몸 콜라이더(캡슐 우선) bounds 의 윗면 가운데, 없으면 발 + 1m.
    /// 몸 중심(<see cref="BossPatternTargets.BodyCenter"/>)에 두면 작은 구체가 불투명한 몸 메시 안에 묻혀 안 보인다 — 탑다운 카메라에서 보이는 머리 위 표면에 붙인다.
    /// </summary>
    public static Vector3 BodyPoint(Unit unit)
    {
        Collider body = unit.GetComponent<CapsuleCollider>();
        if (body == null) body = unit.GetComponentInChildren<Collider>();
        if (body == null) return unit.transform.position + Vector3.up;

        Bounds bounds = body.bounds;
        return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
    }
}

/// <summary>
/// [전 피어] 잔류 포격 에너지 폭탄 구체 표시(할 일 149) — 폭탄마다 화면 공간 스프라이트(Image) 하나가 대상 몸 점을 매 프레임 따라간다(이동·넉백 포함).
/// 공용 오버레이 <see cref="OverheadHealthBarCanvas"/> 위에 그려 몸 메시에 가려지지 않는다(149 후속 4 — 3D 구체는 몸에 묻혔다). 화면 밖·카메라 뒤면 숨긴다.
/// 대상이 사라지면 마지막 위치에 남았다가 폭탄이 빠질 때 그 자리를 돌려준다(폭발 연출 위치). 런타임 메시·텍스처 생성 없음 — 에셋 스프라이트만 쓴다.
/// Q 적중 순간 예비 구체(<see cref="AddPreview(ulong)"/>)를 먼저 띄우고, 서버 부착(<see cref="Add(int, ulong)"/>)이 같은 대상이면 그 구체를 이어받는다.
/// 채택 안 된 예비 구체(중단·사망 등 폭탄 없음)는 대기 시간 뒤 폭발 연출 없이 지운다 — 정리 경로는 그것 하나.
/// </summary>
public sealed class GunnerEchoBombVisuals
{
    private sealed class Orb
    {
        public int Id;
        public ulong TargetId;
        public Unit Target;
        public Image Instance;
        public Vector3 LastPosition;
        // 한 번이라도 대상을 찾았는가 — 찾은 뒤 사라지면(사망·디스폰) 다시 찾지 않고 마지막 위치에 둔다.
        public bool Found;
        // 이 시각(Time.time) 전에는 구체를 띄우지 않는다 — 표시 지연(showDelay). 위치는 그 전에도 따라간다(폭발 연출 위치).
        public float ShowAt;
        // 예비 구체(Q 적중 순간, 서버 부착 전) — 폭탄 번호가 없다. 서버 부착(Add)이 같은 대상이면 이 구체를 그대로 이어받는다.
        public bool Preview;
        // 예비 구체가 이 시각까지 채택되지 않으면 조용히 지운다(같은 대상 재적중마다 늦춘다).
        public float ExpireAt;
    }

    private readonly List<Orb> orbs = new List<Orb>();
    private readonly Sprite sprite;
    private readonly float size;
    private readonly Color color;
    private readonly float showDelay;
    private readonly Vector3 offset;
    private readonly float previewTimeout;

    /// <param name="sprite">폭탄 스프라이트. null 이면 표시하지 않는다(피해·폭발 연출은 그대로).</param>
    /// <param name="showDelay">Q 적중 순간(예비 구체 생성) 뒤 구체를 띄우기까지 지연(초) — 표시 전용, 서버 폭발 시각과 무관.
    /// 예비 구체 없이 바로 붙은 폭탄(늦은 합류 등)은 받은 순간부터 같은 지연.</param>
    /// <param name="offset">몸 점(<see cref="GunnerEchoBombTargets.BodyPoint"/>)에 더하는 월드 오프셋(m).</param>
    /// <param name="previewTimeout">예비 구체가 서버 부착을 기다리는 시간(초) — 지나면 폭발 연출 없이 지운다.</param>
    /// <param name="size">스프라이트 한 변 크기(오버레이 캔버스 단위 = 세로 1080 기준 px).</param>
    /// <param name="color">스프라이트 색(null = 흰색).</param>
    public GunnerEchoBombVisuals(Sprite sprite, float showDelay = 0f, Vector3 offset = default, float previewTimeout = 1.5f,
        float size = 40f, Color? color = null)
    {
        this.sprite = sprite;
        this.size = Mathf.Max(0f, size);
        this.color = color ?? Color.white;
        this.showDelay = Mathf.Max(0f, showDelay);
        this.offset = offset;
        this.previewTimeout = Mathf.Max(0f, previewTimeout);
    }

    /// <summary>예비 구체 포함 구체 수.</summary>
    public int Count => orbs.Count;

    /// <summary>아직 서버 부착을 못 받은 예비 구체 수.</summary>
    public int PreviewCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < orbs.Count; i++)
                if (orbs[i].Preview) n++;
            return n;
        }
    }

    /// <summary>예비 구체가 만료됐는가 — 채택 대기 시간이 지났다.</summary>
    public static bool IsPreviewExpired(float now, float expireAt) => now >= expireAt;

    /// <summary>구체·폭발 연출·Attached 사건이 보는 점 — 몸 점 + 월드 오프셋.</summary>
    public Vector3 PointFor(Unit unit) => OrbPoint(GunnerEchoBombTargets.BodyPoint(unit), offset);

    public static Vector3 OrbPoint(Vector3 bodyPoint, Vector3 offset) => bodyPoint + offset;

    /// <summary>표시 지연이 지났는가 — 지연 0 이면 붙는 즉시.</summary>
    public static bool IsShowTime(float now, float showAt) => now >= showAt;

    /// <summary>
    /// Q 가 그 대상에 맞았다(할 일 149 후속 3) — 서버 부착 전에 예비 구체를 띄운다. 표시 지연은 이 순간부터.
    /// 그 대상에 미채택 예비 구체가 이미 있으면(같은 시전 재적중·집속 연사) 새로 만들지 않고 만료만 늦춘다.
    /// </summary>
    public void AddPreview(ulong targetId) => AddPreview(targetId, Time.time);

    public void AddPreview(ulong targetId, float now)
    {
        int index = IndexOfPreview(targetId);
        if (index >= 0)
        {
            orbs[index].ExpireAt = now + previewTimeout;
            return;
        }

        var orb = new Orb { TargetId = targetId, ShowAt = now + showDelay, Preview = true, ExpireAt = now + previewTimeout };
        orbs.Add(orb);
        Follow(orb);
    }

    /// <summary>
    /// 폭탄이 붙었다 — 그 대상의 미채택 예비 구체가 있으면 같은 구체(인스턴스·표시 시각 그대로)를 이 폭탄 번호로 이어받는다.
    /// 없으면(늦은 합류 Sync 등) 이 피어가 받은 시각부터 표시 지연.
    /// </summary>
    public void Add(int id, ulong targetId) => Add(id, targetId, Time.time);

    public void Add(int id, ulong targetId, float now)
    {
        if (IndexOf(id) >= 0)
            return;

        int preview = IndexOfPreview(targetId);
        if (preview >= 0)
        {
            Orb adopted = orbs[preview];
            adopted.Id = id;
            adopted.Preview = false;
            Follow(adopted);
            return;
        }

        var orb = new Orb { Id = id, TargetId = targetId, ShowAt = now + showDelay };
        orbs.Add(orb);
        Follow(orb);
    }

    /// <summary>그 폭탄 구체가 보이기 시작하는(했던) 시각. 모르는 번호면 false.</summary>
    public bool TryGetShowAt(int id, out float showAt)
    {
        int index = IndexOf(id);
        showAt = index >= 0 ? orbs[index].ShowAt : 0f;
        return index >= 0;
    }

    /// <summary>그 폭탄이 붙은 대상의 네트워크 ID(특성 연출 사건용, 할 일 145). 모르는 번호면 false.</summary>
    public bool TryGetTargetId(int id, out ulong targetId)
    {
        int index = IndexOf(id);
        targetId = index >= 0 ? orbs[index].TargetId : 0UL;
        return index >= 0;
    }

    /// <summary>폭탄이 빠졌다(터짐·빈 폭발) — 구체를 지우고 마지막 위치를 돌려준다. 모르는 번호거나 대상을 한 번도 못 찾았으면 false(위치 없음).</summary>
    public bool Remove(int id, out Vector3 lastPosition)
    {
        lastPosition = default;
        int index = IndexOf(id);
        if (index < 0)
            return false;

        Orb orb = orbs[index];
        Follow(orb);
        lastPosition = orb.LastPosition;
        DestroyInstance(orb);
        orbs.RemoveAt(index);
        return orb.Found;
    }

    /// <summary>예약 취소(사수 사망·씬 종료·디스폰) — 터뜨리지 않고 조용히 지운다.</summary>
    public void Clear()
    {
        for (int i = 0; i < orbs.Count; i++)
            DestroyInstance(orbs[i]);
        orbs.Clear();
    }

    /// <summary>복제 목록과 맞춘다(늦은 합류·전체 갱신) — 목록에 없는 구체는 조용히 지우고, 새 항목은 붙인다. 예비 구체는 목록과 무관(만료가 정리).</summary>
    public void Sync(IReadOnlyList<GunnerEchoBombState> states)
    {
        for (int i = 0; i < states.Count; i++)
            Add(states[i].BombId, states[i].TargetId);

        for (int i = orbs.Count - 1; i >= 0; i--)
        {
            if (orbs[i].Preview || Contains(states, orbs[i].Id))
                continue;
            DestroyInstance(orbs[i]);
            orbs.RemoveAt(i);
        }
    }

    private static bool Contains(IReadOnlyList<GunnerEchoBombState> states, int id)
    {
        for (int i = 0; i < states.Count; i++)
        {
            if (states[i].BombId == id)
                return true;
        }

        return false;
    }

    /// <summary>매 프레임(LateUpdate) — 대상 몸 점(월드)을 갱신한다. 화면 위치 반영은 <see cref="ApplyScreenPositions"/>. 대상이 사라졌으면 마지막 위치에 둔다. 채택 시간이 지난 예비 구체는 조용히 지운다.</summary>
    public void Tick() => Tick(Time.time);

    public void Tick(float now)
    {
        for (int i = orbs.Count - 1; i >= 0; i--)
        {
            Orb orb = orbs[i];
            if (orb.Preview && IsPreviewExpired(now, orb.ExpireAt))
            {
                DestroyInstance(orb);
                orbs.RemoveAt(i);
                continue;
            }

            Follow(orb);
        }
    }

    private void Follow(Orb orb)
    {
        // 클라에서는 복제 순서상 대상이 폭탄보다 늦게 스폰될 수 있다 — 아직 못 찾았으면 다시 찾는다. 한 번도 못 찾았으면 구체를 띄우지 않는다.
        if (orb.Target == null && !orb.Found)
            orb.Target = GunnerEchoBombTargets.Find(orb.TargetId);
        if (orb.Target != null && orb.Target.IsSpawned)
        {
            orb.Found = true;
            orb.LastPosition = PointFor(orb.Target);
            if (orb.Instance == null && sprite != null && IsShowTime(Time.time, orb.ShowAt))
                orb.Instance = CreateImage();
        }
    }

    /// <summary>
    /// Canvas 렌더 직전(<c>Canvas.willRenderCanvases</c>)에 부른다 — 카메라가 LateUpdate 에서 움직인 뒤라 대상에서 밀리지 않는다(머리 위 체력바와 같은 이유).
    /// 카메라 없음·카메라 뒤·화면 밖은 투명 처리.
    /// </summary>
    public void ApplyScreenPositions()
    {
        Camera cam = Camera.main;
        Vector2 screenSize = new Vector2(Screen.width, Screen.height);
        Vector2 halfExtent = new Vector2(size, size) * 0.5f;
        for (int i = 0; i < orbs.Count; i++)
        {
            Image image = orbs[i].Instance;
            if (image == null)
                continue;

            bool visible = false;
            Canvas canvas = image.canvas;
            if (cam != null && canvas != null)
            {
                visible = OverheadHealthBarScreenPlacement.TryGetCanvasPosition(
                    cam.WorldToScreenPoint(orbs[i].LastPosition), screenSize, canvas.scaleFactor,
                    Vector2.zero, halfExtent, out Vector2 position);
                if (visible)
                    image.rectTransform.anchoredPosition = position;
            }

            image.canvasRenderer.SetAlpha(visible ? 1f : 0f);
        }
    }

    // 공용 오버레이 캔버스 위 Image 하나(왼쪽 아래 앵커, 가운데 피벗). 첫 배치 전에는 투명 — (0,0) 에서 한 프레임 번쩍이지 않게.
    private Image CreateImage()
    {
        var go = new GameObject("EchoBomb", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = (RectTransform)go.transform;
        rect.SetParent(OverheadHealthBarCanvas.Instance.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(size, size);

        Image image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.canvasRenderer.SetAlpha(0f);
        return image;
    }

    private static void DestroyInstance(Orb orb)
    {
        if (orb.Instance != null)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(orb.Instance.gameObject);
            else
                UnityEngine.Object.DestroyImmediate(orb.Instance.gameObject);
        }
        orb.Instance = null;
    }

    // 폭탄 번호로 찾는다 — 예비 구체는 번호가 없어 제외.
    private int IndexOf(int id)
    {
        for (int i = 0; i < orbs.Count; i++)
        {
            if (!orbs[i].Preview && orbs[i].Id == id)
                return i;
        }

        return -1;
    }

    private int IndexOfPreview(ulong targetId)
    {
        for (int i = 0; i < orbs.Count; i++)
        {
            if (orbs[i].Preview && orbs[i].TargetId == targetId)
                return i;
        }

        return -1;
    }
}
