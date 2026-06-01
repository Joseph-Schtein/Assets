using System.Collections;
using UnityEngine;

public class LightSource : MonoBehaviour
{
    [Header("Timing")]
    public float expandDuration = 3f;   // Phase 2: seconds to grow to full size
    public float stayDuration = 20f;   // Phase 3: seconds held at peak
    public float shrinkDuration = 3f;   // Phase 5: seconds to shrink & fade out

    [Header("Light Settings")]
    public float maxIntensity = 5000000f;
    public float maxRadius = 45f;    // Peak inner & outer spot angle (degrees) — 45° from 75 units high gives ~60 unit radius on ground
    public float maxColliderRadius = 25f; // Peak collider radius

    [Header("References")]
    public Light spotLight;
    public CapsuleCollider lightCollider;

    /// <summary>Seconds until the light is fully gone (stay phase remaining + shrink duration).
    /// Returns 0 once the shrink phase has begun.</summary>
    public float TimeRemaining { get; private set; }

    /// <summary>Maximum possible TimeRemaining at the moment the stay phase begins.</summary>
    public float MaxTimeRemaining => stayDuration + shrinkDuration;

    void Start()
    {
        // In non-game scenes (e.g. MainMenu) there is no bloom post-processing,
        // so the HDR values used in-game wash the spotlight to solid white.
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "SampleScene")
        {
            maxIntensity = 5000f; // Sane cap for scenes without bloom
        }

        if (spotLight == null) spotLight = GetComponent<Light>();
        if (lightCollider == null) lightCollider = GetComponent<CapsuleCollider>();

        // PERFORMANCE FIX: Prevent BVH rebuilds when dynamically modifying the collider radius
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // Guarantee the light range reaches the ground: at Y=75 we need at least 150 units range.
        if (spotLight != null)
        {
            float minRange = Mathf.Abs(transform.position.y) * 2f + 100f;
            if (spotLight.range < minRange)
                spotLight.range = minRange;
        }

        // Initialise at zero so the expand phase starts clean
        ApplyLight(0f, 0f);

        // Arrows should show 100% brightness from the moment the spotlight appears,
        // so seed TimeRemaining at its maximum before the lifecycle begins.
        TimeRemaining = stayDuration + shrinkDuration;

        StartCoroutine(LifecycleRoutine());
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Full lifecycle coroutine
    // ─────────────────────────────────────────────────────────────────────────
    private IEnumerator LifecycleRoutine()
    {
        // ── Phase 2: Expand (3 s) ─────────────────────────────────────────────
        //    Inner Radius 0 → maxRadius, Outer Radius 0 → maxRadius,
        //    Intensity    0 → maxIntensity
        yield return Transition(
            fromRadius: 0f, toRadius: maxRadius,
            fromIntensity: 0f, toIntensity: maxIntensity,
            duration: expandDuration);

        // ── Phase 3: Stay ─────────────────────────────────────────────────────
        float stayElapsed = 0f;
        while (stayElapsed < stayDuration)
        {
            stayElapsed += Time.deltaTime;
            // TimeRemaining = stay time left + full shrink duration
            TimeRemaining = (stayDuration - stayElapsed) + shrinkDuration;
            yield return null;
        }
        TimeRemaining = shrinkDuration; // entering shrink

        // ── Phase 5: Shrink & Fade (shrinkDuration) ───────────────────────────
        //    Inner/Outer Radius maxRadius → 0, Intensity maxIntensity → 0
        TimeRemaining = 0f; // arrow treats shrink phase as "urgent"
        yield return Transition(
            fromRadius: maxRadius, toRadius: 0f,
            fromIntensity: maxIntensity, toIntensity: 0f,
            duration: shrinkDuration);

        // ── Cleanup ────────────────────────────────────────────────────────────
        Destroy(gameObject);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Generic lerp helper
    // ─────────────────────────────────────────────────────────────────────────
    private IEnumerator Transition(
        float fromRadius, float toRadius,
        float fromIntensity, float toIntensity,
        float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            ApplyLight(
                radius: Mathf.Lerp(fromRadius, toRadius, t),
                intensity: Mathf.Lerp(fromIntensity, toIntensity, t));

            yield return null; // Wait one frame — inherently framerate-independent
        }

        // Snap to exact final values to avoid floating-point drift
        ApplyLight(toRadius, toIntensity);
    }

    // ─────────────────────────────────────────────────────────────────────────
    private void ApplyLight(float radius, float intensity)
    {
        if (spotLight != null)
        {
            spotLight.innerSpotAngle = radius;
            spotLight.spotAngle = radius;
            spotLight.intensity = intensity;
        }

        if (lightCollider != null && maxRadius > 0f)
        {
            // Scale collider radius proportionally to the light's current expansion
            lightCollider.radius = (radius / maxRadius) * maxColliderRadius;
        }
    }
}
