using UnityEngine;

public class PlayerRotation : MonoBehaviour
{
    private PlayerInputReader inputReader;
    private Rigidbody rb;

    private Vector2 prevDir;

    [SerializeField]
    private float rotateSpeed;
    private float viewYaw = -45;

    private void Awake()
    {
        inputReader = GetComponent<PlayerInputReader>();
        rb = GetComponent<Rigidbody>();
        prevDir = Vector2.one;
        rotateSpeed = 10.0f;
    }

    private void FixedUpdate()
    {
        if (inputReader.HasMoveInput)
            prevDir = inputReader.Direction;

        Vector3 dir = new Vector3(prevDir.x, 0, prevDir.y);
        dir = Quaternion.Euler(0f, viewYaw, 0f) * dir;

        Quaternion targetRotation = Quaternion.LookRotation(dir);

        if (Vector3.Dot(dir, transform.forward) > 0.999f)
        {
            transform.rotation = targetRotation;
            return;
        }

        rb.MoveRotation(Quaternion.Slerp(transform.rotation, targetRotation, rotateSpeed * Time.deltaTime));
    }
}
