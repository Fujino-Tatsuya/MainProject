using Unity.Netcode;
using UnityEngine;

public class CameraTestPlayer : NetworkBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private Renderer targetRenderer;
    private MaterialPropertyBlock propertyBlock;

    private void Awake()
    {
        targetRenderer = GetComponent<Renderer>();
        propertyBlock = new MaterialPropertyBlock();
    }

    public override void OnNetworkSpawn()
    {
        ApplyClientColor(GetClientColor(OwnerClientId));
    }

    private void ApplyClientColor(Color color)
    {
        if (targetRenderer == null)
        {
            return;
        }

        targetRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(BaseColorId, color);
        targetRenderer.SetPropertyBlock(propertyBlock);
    }

    private static Color GetClientColor(ulong clientId)
    {
        switch (clientId)
        {
            case 0:
                return Color.red;

            case 1:
                return Color.green;

            case 2:
                return Color.blue;

            default:
                float hue = (clientId * 0.61803398875f) % 1f;
                return Color.HSVToRGB(hue, 0.75f, 1f);
        }
    }
}
