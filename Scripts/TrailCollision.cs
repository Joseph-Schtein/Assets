using UnityEngine;

[RequireComponent(typeof(TrailRenderer))]
public class TrailCollision : MonoBehaviour
{
    private TrailRenderer trail;
    private GameObject colliderContainer;
    
    [Header("WebGL-Safe Collision Settings")]
    [Tooltip("How far the cycle must move before dropping a new collision box.")]
    public float colliderDropDistance = 2.5f; 
    public float colliderHeight = 2.0f;
    
    [Header("Optimization")]
    [Tooltip("Number of pre-allocated colliders. Prevents Garbage Collection lag!")]
    public int poolSize = 100;
    
    private Vector3 lastDropPosition;
    private GameObject[] segmentPool;
    private int poolIndex = 0;

    void Start()
    {
        trail = GetComponent<TrailRenderer>();
        
        colliderContainer = new GameObject("TrailSegments_" + gameObject.name);
        colliderContainer.transform.position = Vector3.zero;
        colliderContainer.transform.rotation = Quaternion.identity;
        
        lastDropPosition = transform.position;

        // ── PRE-ALLOCATE OBJECT POOL (Zero GC lag during gameplay) ──
        segmentPool = new GameObject[poolSize];
        Collider playerCollider = GetComponentInParent<Collider>();

        for (int i = 0; i < poolSize; i++)
        {
            GameObject segment = new GameObject("TrailSegment_" + i);
            segment.transform.parent = colliderContainer.transform;
            segment.transform.position = new Vector3(0, -1000f, 0); // Hide underground instead of SetActive

            BoxCollider box = segment.AddComponent<BoxCollider>();
            box.isTrigger = false;

            // CRITICAL PERFORMANCE FIX: 
            // Moving/activating static colliders (colliders without rigidbodies) forces Unity to rebuild the entire physics BVH tree.
            // By adding a kinematic Rigidbody, Unity treats it as a dynamic object and prevents massive physics lag spikes!
            Rigidbody rb = segment.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            if (playerCollider != null)
            {
                Physics.IgnoreCollision(playerCollider, box);
            }

            segment.tag = "DeadlyTrail";
            TrailData data = segment.AddComponent<TrailData>();
            data.owner = transform.root.gameObject;

            TrailSegmentTimer timer = segment.AddComponent<TrailSegmentTimer>();
            
            segmentPool[i] = segment;
        }
    }

    void Update()
    {
        if (trail == null || !trail.emitting) 
        {
            lastDropPosition = transform.position;
            return;
        }

        float dist = Vector3.Distance(transform.position, lastDropPosition);
        if (dist >= colliderDropDistance)
        {
            CreateTrailSegment(dist);
            lastDropPosition = transform.position;
        }
    }

    void CreateTrailSegment(float distance)
    {
        // 1. Grab the next available collider from the pool
        GameObject segment = segmentPool[poolIndex];
        poolIndex = (poolIndex + 1) % poolSize;

        Vector3 direction = (transform.position - lastDropPosition).normalized;
        segment.transform.position = lastDropPosition + (direction * (distance * 0.5f));
        segment.transform.position = new Vector3(segment.transform.position.x, 1f, segment.transform.position.z);
        
        if (direction != Vector3.zero)
        {
            segment.transform.rotation = Quaternion.LookRotation(direction);
        }
        
        BoxCollider box = segment.GetComponent<BoxCollider>();
        float safeWidth = Mathf.Max(1.0f, trail.startWidth);
        box.size = new Vector3(safeWidth, colliderHeight, distance + 2.0f); 

        // 2. Set its lifespan to match the TrailRenderer and start the timer
        TrailSegmentTimer timer = segment.GetComponent<TrailSegmentTimer>();
        timer.lifespan = trail.time;
        timer.ResetTimer();
    }

    public void DestroyTrailMesh()
    {
        // Deactivate all pooled segments instantly
        if (segmentPool != null)
        {
            foreach (var seg in segmentPool)
            {
                if (seg != null) seg.transform.position = new Vector3(0, -1000f, 0); // Move underground
            }
        }
    }

    public void ResetTrail()
    {
        if (trail != null) trail.Clear();
        DestroyTrailMesh();
        lastDropPosition = transform.position;
    }

    void OnDestroy()
    {
        if (colliderContainer != null)
        {
            Destroy(colliderContainer);
        }
    }
}

// Lightweight timer to recycle pooled segments automatically
public class TrailSegmentTimer : MonoBehaviour
{
    public float lifespan;
    private float age;
    private bool isRunning = false;

    public void ResetTimer()
    {
        age = 0f;
        isRunning = true;
    }

    void Update()
    {
        if (!isRunning) return;

        age += Time.deltaTime;
        if (age >= lifespan)
        {
            transform.position = new Vector3(0, -1000f, 0); // Return to pool (underground)
            isRunning = false;
        }
    }
}

public class TrailData : MonoBehaviour
{
    public GameObject owner;
}
