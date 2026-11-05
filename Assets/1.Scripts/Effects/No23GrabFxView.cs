using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Local geometry on an EffectManager-owned Shuriken part. The presenter obtains it through GetInstances
/// for the current handle each frame, never caching a pooled GameObject across frames.
/// </summary>
[DisallowMultipleComponent]
public sealed class No23GrabFxView : MonoBehaviour
{
    [SerializeField] Material lineMaterial;
    [SerializeField] Color electricColor = new Color(0.60f, 0.94f, 1f, 1f);
    [SerializeField, Range(2, 8)] int lineCount = 2;

    [Header("전기 줄(Segment) — 가닥 = lineCount / 2")]
    [Tooltip("가운데 가닥이 휘는 정도(m). 가닥이 1개면 무시")]
    [SerializeField, Min(0f)] float centerBend = 0.05f;
    [Tooltip("바깥 가닥들이 휘는 정도(m). 클수록 가닥이 서로 벌어진다")]
    [SerializeField, Min(0f)] float outerBend = 0.14f;
    [Tooltip("지글거림 세기(m). 클수록 거칠게 떨린다")]
    [SerializeField, Min(0f)] float jitterAmount = 0.07f;
    [Tooltip("지글거림 속도")]
    [SerializeField, Min(0f)] float jitterSpeed = 37f;
    LineRenderer[] _lines;

    void Awake()
    {
        _lines = new LineRenderer[lineCount];
        for (int i = 0; i < _lines.Length; i++)
        {
            var obj = new GameObject("Electric strand " + i);
            obj.layer = gameObject.layer;
            obj.transform.SetParent(transform, false);
            LineRenderer line = obj.AddComponent<LineRenderer>();
            line.sharedMaterial = lineMaterial;
            line.useWorldSpace = true;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.View;
            line.positionCount = 0;
            _lines[i] = line;
        }
    }

    void OnEnable() { if (_lines != null) foreach (LineRenderer line in _lines) line.positionCount = 0; }
    void OnDisable() { if (_lines != null) foreach (LineRenderer line in _lines) line.positionCount = 0; }

    void Prepare(int index, int count, float width, float alpha, bool loop = false)
    {
        LineRenderer line = _lines[index];
        line.loop = loop;
        line.positionCount = count;
        line.widthMultiplier = width;
        Color color = electricColor; color.a *= alpha;
        line.startColor = color; line.endColor = color;
    }

    public void Ring(Vector3 center, float radius, float alpha)
    {
        for (int k = 0; k < 2 && k < _lines.Length; k++)
        {
            Prepare(k, 96, k == 0 ? 0.035f : 0.012f, alpha * (k == 0 ? 1f : 0.65f), true);
            float r = radius * (k == 0 ? 1f : 0.88f);
            for (int i = 0; i < 96; i++)
            {
                float angle = i * Mathf.PI * 2f / 96f;
                _lines[k].SetPosition(i, center + new Vector3(Mathf.Sin(angle) * r, 0f, Mathf.Cos(angle) * r));
            }
        }
        for (int k = 2; k < _lines.Length; k++)
        {
            Prepare(k, 2, 0.025f, alpha);
            float angle = (k - 2) * Mathf.PI * 0.5f;
            Vector3 direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            _lines[k].SetPosition(0, center + direction * radius * 0.82f);
            _lines[k].SetPosition(1, center + direction * radius * 1.12f);
        }
    }

    // 전기 줄 — 가닥마다 [심, 번짐] 두 줄. lineCount 2 = 1가닥, 6 = 3가닥(11-05 민경 — MagneticLine·PullTether 3가닥).
    // 가닥마다 휘는 방향(옆·위로 돌아가며)·지글거림 위상·주기를 달리해 서로 엉키며 흐르게 한다. 양 끝은 한 점에 모인다.
    public void Segment(Vector3 from, Vector3 to, float width, float alpha)
    {
        Vector3 delta = to - from;
        Vector3 forward = delta.sqrMagnitude > 1e-6f ? delta.normalized : Vector3.forward;
        Vector3 side = Vector3.Cross(forward, Vector3.up);
        if (side.sqrMagnitude < 0.001f) side = Vector3.right;
        side.Normalize();
        Vector3 up = Vector3.Cross(side, forward).normalized;

        int strands = Mathf.Max(1, _lines.Length / 2);
        for (int s = 0; s < strands; s++)
        {
            // 가닥 0 = 가운데(덜 휨), 나머지는 단면 둘레로 고르게 돌아가며 휜다.
            float angle = s * Mathf.PI * 2f / strands + 0.6f;
            Vector3 bendDir = side * Mathf.Cos(angle) + up * Mathf.Sin(angle);
            float bend = strands == 1 ? 0f : (s == 0 ? centerBend : outerBend);
            float phase = s * 2.17f;
            float freq = 8.31f + s * 1.9f;
            for (int layer = 0; layer < 2; layer++)
            {
                int k = s * 2 + layer;
                if (k >= _lines.Length) break;
                Prepare(k, 25, width * (layer == 0 ? 1f : 2.8f), alpha * (layer == 0 ? 1f : 0.23f));
                for (int i = 0; i < 25; i++)
                {
                    float t = i / 24f;
                    float envelope = Mathf.Sin(t * Mathf.PI);   // 양 끝 0 — 손·몸에서 모인다
                    float jitter = Mathf.Sin(i * freq + phase + Time.time * jitterSpeed) * jitterAmount * envelope;
                    Vector3 jitterDir = side * Mathf.Cos(angle + i * 0.9f) + up * Mathf.Sin(angle + i * 0.9f);
                    _lines[k].SetPosition(i, Vector3.Lerp(from, to, t) + bendDir * bend * envelope + jitterDir * jitter);
                }
            }
        }
        // 남는 줄(홀수 lineCount)은 숨긴다.
        for (int k = strands * 2; k < _lines.Length; k++)
            _lines[k].positionCount = 0;
    }

    public void Scuff(Vector3 origin, Vector3 toward, float alpha)
    {
        Vector3 direction = toward - origin; direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) direction = Vector3.forward;
        direction.Normalize();
        Vector3 side = Vector3.Cross(direction, Vector3.up);
        for (int k = 0; k < _lines.Length; k++)
        {
            Prepare(k, 8, 0.04f, alpha * 0.45f);
            for (int i = 0; i < 8; i++)
                _lines[k].SetPosition(i, origin + direction * (i / 7f * 0.75f - 0.2f) +
                    side * ((k - 1) * 0.13f + Mathf.Sin(i * 5f + k) * 0.025f));
        }
    }

    public void Spark(Vector3 origin, float progress)
    {
        for (int k = 0; k < _lines.Length; k++)
        {
            Prepare(k, 3, 0.025f, 1f - progress);
            float angle = k * Mathf.PI * 2f / _lines.Length;
            Vector3 direction = new Vector3(Mathf.Sin(angle), (k % 2) * 0.4f, Mathf.Cos(angle));
            float r = Mathf.Lerp(0.08f, 0.62f, progress);
            _lines[k].SetPosition(0, origin + direction * r * 0.4f);
            _lines[k].SetPosition(1, origin + direction * r * 0.7f + Vector3.up * 0.05f);
            _lines[k].SetPosition(2, origin + direction * r);
        }
    }
}
