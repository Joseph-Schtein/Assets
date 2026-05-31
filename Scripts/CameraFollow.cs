using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Tooltip("The object the camera should follow (drag your Player here)")]
    public Transform target;
    
    [Tooltip("The fixed offset from the player. (e.g. 0, 80, -20 for a wide top-down view)")]
    public Vector3 offset = new Vector3(0, 80, -20);

    [Tooltip("How fast the camera catches up to the player. Higher = faster.")]
    public float smoothSpeed = 10f;

    [Tooltip("Check this if you want the camera to look perfectly straight down at the player.")]
    public bool lookStraightDown = false;

    private bool isInitialized = false;


    void Start()
    {
        // If an offset isn't set manually, let's grab the current difference as the starting offset
        if (target != null && offset == Vector3.zero)
        {
            offset = transform.position - target.position;
        }
    }



    void LateUpdate()
    {
        // Auto-assign to an AI if the player is removed for training/spectating
        if (target == null) 
        {
            GameObject ai = GameObject.FindGameObjectWithTag("AI");
            if (ai != null) target = ai.transform;
            else return;
        }
        
        // 1. Calculate where the camera SHOULD be
        Vector3 desiredPosition = target.position + offset;
        
        if (!isInitialized)
        {
            transform.position = desiredPosition;
            isInitialized = true;
        }
        else
        {
            // 2. Smoothly move the camera there without rotating it
            transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
        }
        
        // 3. Optional: force camera to look straight down
        if (lookStraightDown)
        {
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }
    }
}
