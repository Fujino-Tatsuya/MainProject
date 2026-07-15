using UnityEngine;

public enum AreaMergeType
{
    None,
    GrowOnOverlap
}

public class BombAreaEffect : MonoBehaviour
{
    [SerializeField] bool canGrowOnOverlap;
    [SerializeField] float growAmount;
    [SerializeField] float maxScale;
    [SerializeField] AreaMergeType mergeGroup;


    public bool CanGrowOnOverlap { get { return canGrowOnOverlap; } }
    public AreaMergeType MergeGroup { get { return mergeGroup; } }

    public void Grow()
    {
        Vector3 scale = transform.localScale;

        scale.x = Mathf.Min(scale.x + growAmount, maxScale);
        scale.y = Mathf.Min(scale.y + growAmount, maxScale);
        scale.z = Mathf.Min(scale.z + growAmount, maxScale);

        transform.localScale = scale;
    }
    
}
