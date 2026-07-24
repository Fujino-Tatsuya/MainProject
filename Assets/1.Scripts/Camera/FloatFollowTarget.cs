using UnityEngine;

// Runtime proxy tracked by PlayerFollowFloatCamera. The source supplies only
// horizontal motion; vertical position remains fixed until explicitly changed.
public sealed class FloatFollowTarget : MonoBehaviour
{
    private Transform source;
    private float fixedWorldY;

    public Transform Source => source;
    public float FixedWorldY => fixedWorldY;

    public void SetSource(Transform newSource)
    {
        source = newSource;
        RefreshPosition();
    }

    public void SetFixedWorldY(float worldY)
    {
        fixedWorldY = worldY;
        RefreshPosition();
    }

    private void LateUpdate()
    {
        RefreshPosition();
    }

    private void RefreshPosition()
    {
        if (source == null)
        {
            return;
        }

        Vector3 sourcePosition = source.position;
        transform.position = new Vector3(sourcePosition.x, fixedWorldY, sourcePosition.z);
    }
}
