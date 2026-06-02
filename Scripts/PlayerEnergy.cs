using UnityEngine;
using TMPro;

// Run after LightcycleAI (which moves/rotates the player in Update).
// A higher number means later execution — ensures our LateUpdate corrections
// are the final word on the spotlight position and rotation each frame.
[DefaultExecutionOrder(100)]
public class PlayerEnergy : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════════════════════
    //  SERIALIZED FIELDS
    // ═══════════════════════════════════════════════════════════════════════

    [Header("Energy")]
    public float maxEnergy = 100f;
    public float drainRate = 12f;
    public float rechargeRate = 40f;

    [Header("Arena")]
    public float arenaLimit = 150f;

    [Header("Visual References")]
    public Light sparkLight;
    public TrailRenderer sparkTrail;
    [Tooltip("Canvas text for respawn countdown (human player only).")]
    public TextMeshProUGUI respawnCountdownText;

    [Header("Colors")]
    [ColorUsage(true, true)] public Color fullEnergyColor;
    [ColorUsage(true, true)] public Color lowEnergyColor;

    [Header("Visual Tuning")]
    public float maxLightIntensity = 5f;
    public float maxTrailTime = 3f;
    public float maxTrailWidth = 3f;
    public float trailBrightness = 500000f;
    [Tooltip("Spotlight outer angle (degrees) at full energy — scales with trail width.")]
    public float maxSpotAngle = 80f;
    [Tooltip("Spotlight inner angle (degrees) at full energy — scales with trail width.")]
    public float maxInnerSpotAngle = 60f;

    [Header("Runtime State")]
    public float currentEnergy;
    public bool isIlluminated = false;
    public int deathCount = 0;
    public int killCount = 0;
    public bool isDead = false;
    public bool randomiseColorOnStart = false;

    // ═══════════════════════════════════════════════════════════════════════
    //  PRIVATE STATE
    // ═══════════════════════════════════════════════════════════════════════

    private LightSpawner spawner;
    private LightcycleAI _aiComponent;
    private Material trailMat;
    private bool _trailInitialised;
    private bool hasTriggeredNextSpawn;
    private bool illuminatedThisFrame;
    private float _lastEnergyPercent = -1f;
    private Color _lastTrailColor = new Color(-1, -1, -1, -1); // sentinel
    private static readonly Collider[] _hitCache = new Collider[64];

    // ═══════════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ═══════════════════════════════════════════════════════════════════════

    void Start()
    {
        currentEnergy = maxEnergy;
        spawner = FindFirstObjectByType<LightSpawner>();
        _aiComponent = GetComponent<LightcycleAI>();

        // Scale arena to match AI count
        int aiCount = PlayerPrefs.GetInt("AIPlayerCount", 15);
        arenaLimit = Mathf.Lerp(100f, 250f, (aiCount - 5f) / 15f);

        // Non-game scenes (MainMenu) lack bloom, so cap HDR values
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "SampleScene")
        {
            maxLightIntensity = 1e+11f;
            trailBrightness = 5f;
        }

        if (randomiseColorOnStart)
            ApplyRandomColor();

        InitTrailMaterial();
    }

    void Update()
    {
        if (isDead) return;

        // Out-of-bounds check
        if (Mathf.Abs(transform.position.x) >= arenaLimit ||
            Mathf.Abs(transform.position.z) >= arenaLimit)
        {
            Debug.Log($"<color=red><b>{gameObject.name} disqualified! Hit the field limit.</b></color>");
            currentEnergy = 0;
            GameOver("Get out of bounds");
            return;
        }

        UpdateVisuals();
    }

    void FixedUpdate()
    {
        // OnTriggerStay fires before FixedUpdate, so the flag is already set
        isIlluminated = illuminatedThisFrame;
        illuminatedThisFrame = false;

        if (isDead) return;

        // Energy drain / recharge
        if (isIlluminated)
        {
            currentEnergy += rechargeRate * Time.fixedDeltaTime;
            if (currentEnergy >= maxEnergy && !hasTriggeredNextSpawn)
            {
                currentEnergy = maxEnergy;
                if (spawner != null)
                {
                    spawner.SpawnNextLight();
                    hasTriggeredNextSpawn = true;
                }
            }
        }
        else
        {
            currentEnergy -= drainRate * Time.fixedDeltaTime;
            hasTriggeredNextSpawn = false;
        }

        currentEnergy = Mathf.Clamp(currentEnergy, 0, maxEnergy);
        if (currentEnergy <= 0) GameOver("Spark power is over");

        // Manual collision check
        CheckCollisions();
    }

    void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("Spotlight"))
            illuminatedThisFrame = true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TRAIL MATERIAL — guaranteed to exist for every player
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ensures sparkTrail has a valid material. Called in Start() and also
    /// from SyncTrailColor() if it's invoked before Start() (e.g. by a spawner).
    /// </summary>
    private void InitTrailMaterial()
    {
        if (sparkTrail == null) return;
        if (_trailInitialised) return; // Already run
        _trailInitialised = true;

        // If a material is assigned (e.g. tail.mat), grab an instance so we can
        // tint it at runtime. If no material is assigned, leave trailMat null and
        // rely entirely on the gradient vertex colour, which is what makes the
        // human player's trail visible (the baked yellow gradient + default material).
        if (sparkTrail.sharedMaterial != null)
        {
            trailMat = sparkTrail.material;
            trailMat.EnableKeyword("_EMISSION");
        }

        sparkTrail.emitting = true;

        // Seed the gradient with the correct bike colour immediately
        Color c = fullEnergyColor != Color.clear ? fullEnergyColor : Color.white;
        SyncTrailColor(c, trailBrightness);
    }


    // ═══════════════════════════════════════════════════════════════════════
    //  COLOR
    // ═══════════════════════════════════════════════════════════════════════

    public void ApplyRandomColor()
    {
        Color col = Random.ColorHSV(0f, 1f, 0.6f, 1f, 0.8f, 1f);
        fullEnergyColor = col;

        Color dim = col * 0.2f;
        dim.a = 1f;
        lowEnergyColor = dim;

        if (sparkLight != null) sparkLight.color = col;

        // Tint body renderers (skip the TrailRenderer)
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r is TrailRenderer) continue;
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor("_Color", col);
            block.SetColor("_BaseColor", col);
            block.SetColor("_EmissionColor", col * 2f);
            r.SetPropertyBlock(block);
        }

        SyncTrailColor(col, trailBrightness);
    }

    /// <summary>
    /// Pushes a colour + brightness into the trail material and gradient.
    /// Safe to call before Start() — will initialise the material if needed.
    /// </summary>
    public void SyncTrailColor(Color colour, float brightness)
    {
        // Make sure InitTrailMaterial has run (handles calls before Start)
        if (!_trailInitialised)
            InitTrailMaterial();

        // KEY INSIGHT: The gradient vertex colour IS the trail colour.
        // Use sparkLight.color as the source so trail always matches the spotlight.
        if (sparkTrail != null)
        {
            Color source = (sparkLight != null) ? sparkLight.color : colour;
            Color ldrColor = new Color(
                Mathf.Clamp01(source.r),
                Mathf.Clamp01(source.g),
                Mathf.Clamp01(source.b),
                1f);

            Gradient grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(ldrColor, 0f),
                    new GradientColorKey(ldrColor, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.4f, 1f)
                }
            );
            sparkTrail.colorGradient = grad;
        }

        // Also drive the material's emission for HDR bloom on top of the LDR colour.
        // Use sparkLight.color as the authoritative source (same as the gradient above)
        // so the material emission always matches the spotlight — not the stale fullEnergyColor.
        if (trailMat != null)
        {
            Color matSource = (sparkLight != null) ? sparkLight.color : colour;
            float safe = Mathf.Clamp(brightness, 1f, 500f);
            Color hdr = matSource * safe;
            hdr.a = 1f;
            trailMat.SetColor("_BaseColor", hdr);
            trailMat.SetColor("_Color", hdr);
            trailMat.SetColor("_EmissionColor", hdr);
        }

        _lastEnergyPercent = -1f; // Force visual refresh
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  VISUALS — called every frame from Update()
    // ═══════════════════════════════════════════════════════════════════════

    private void UpdateVisuals()
    {
        float pct = currentEnergy / maxEnergy;

        // Spot light intensity AND angle — angle is derived from trail width so the
        // spotlight footprint on the ground matches the trail renderer width exactly.
        if (sparkLight != null)
        {
            sparkLight.intensity = pct * maxLightIntensity * 5f;

            // ── Angle calculation ─────────────────────────────────────────────────
            // Current trail width in world units (matches widthMultiplier set below)
            float trailW = pct * maxTrailWidth;

            // Height of the light above the ground — use the light's world Y.
            float lightHeight = Mathf.Max(sparkLight.transform.position.y, 0.1f);

            // Half-angle whose tangent = (half trail width) / height → full outer angle
            float outerAngle = 2f * Mathf.Atan2(trailW * 0.5f, lightHeight) * Mathf.Rad2Deg;
            // Clamp to Unity's valid spotlight range [1°, 179°]
            outerAngle = Mathf.Clamp(outerAngle, 1f, 179f);
            sparkLight.spotAngle = outerAngle;
            // Inner angle at ~80 % of outer for a soft falloff edge
            sparkLight.innerSpotAngle = Mathf.Clamp(outerAngle * 0.8f, 0f, outerAngle);
        }

        if (sparkTrail != null)
        {
            // Trail width scales with energy — shrinks to 0 at empty
            float w = pct * maxTrailWidth;
            if (Mathf.Abs(sparkTrail.widthMultiplier - w) > 0.01f)
                sparkTrail.widthMultiplier = w;

            // Force the trail gradient to match the spotlight color every frame.
            // No caching guard — we need this to always reflect the current color.
            ApplyTrailGradient();

            // Material HDR emission — only update on energy change
            if (trailMat != null && Mathf.Abs(_lastEnergyPercent - pct) > 0.01f)
            {
                _lastEnergyPercent = pct;
                Color source = (sparkLight != null) ? sparkLight.color : fullEnergyColor;
                // All three colour slots get the HDR value so the trail glows brightly.
                // The vertex gradient is white (see ApplyTrailGradient) so it doesn't dim this.
                float bloomBoost = Mathf.Clamp(trailBrightness, 1f, 500f);
                Color hdr = source * bloomBoost;
                hdr.a = 1f;
                trailMat.SetColor("_BaseColor", hdr);
                trailMat.SetColor("_Color", hdr);
                trailMat.SetColor("_EmissionColor", hdr);
            }
        }
    }

    // LateUpdate runs after ALL Update() calls — movement is fully resolved here.
    void LateUpdate()
    {
        if (isDead) return;

        // Re-apply the gradient in case something overwrote it during Update.
        if (sparkTrail != null)
            ApplyTrailGradient();

        // ── Pin spotlight exactly at player centre, pointing straight down ────────
        // localPosition = zero keeps the light at the player pivot (same XZ as trail).
        // We derive the required LOCAL rotation so the WORLD rotation is exactly
        // straight-down (Euler 90,0,0) regardless of how the parent (bike) has turned.
        if (sparkLight != null)
        {
            sparkLight.transform.localPosition = Vector3.zero;
            // Target world rotation = straight down (90° around world X)
            Quaternion worldDown = Quaternion.Euler(90f, 0f, 0f);
            // Required local rotation = parent⁻¹ * worldDown
            sparkLight.transform.localRotation =
                Quaternion.Inverse(sparkLight.transform.parent.rotation) * worldDown;
        }
    }

    /// <summary>
    /// Builds and applies a trail gradient that matches the current spotlight color.
    /// Always uses sparkLight.color as the authoritative source.
    /// </summary>
    private void ApplyTrailGradient()
    {
        if (sparkTrail == null) return;
        Color source = (sparkLight != null) ? sparkLight.color : fullEnergyColor;

        Gradient grad = new Gradient();

        if (trailMat != null)
        {
            // When a material is driving the trail, set vertex colors to WHITE so the
            // material's _BaseColor/_EmissionColor controls the hue and brightness fully.
            // This prevents the LDR vertex color from dimming the HDR material emission.
            grad.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.8f, 1f)
                }
            );
        }
        else
        {
            // No material — vertex color IS the trail color (human player path)
            Color ldrBase = new Color(
                Mathf.Clamp01(source.r),
                Mathf.Clamp01(source.g),
                Mathf.Clamp01(source.b),
                1f);
            grad.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(ldrBase, 0f),
                    new GradientColorKey(ldrBase, 1f)
                },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.6f, 1f)
                }
            );
        }

        sparkTrail.colorGradient = grad;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ENERGY API
    // ═══════════════════════════════════════════════════════════════════════

    public void UseEnergy(float amount)
    {
        currentEnergy -= amount;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  COLLISION
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckCollisions()
    {
        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position, 1.5f, _hitCache, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = _hitCache[i];

            if (hit.CompareTag("DeadlyTrail"))
            {
                hit.TryGetComponent<TrailData>(out TrailData data);
                if (data != null && data.owner != gameObject)
                {
                    Debug.Log($"<color=red><b>{gameObject.name} crashed into {data.owner.name}'s trail.</b></color>");
                    data.owner.TryGetComponent<PlayerEnergy>(out PlayerEnergy killer);
                    if (killer != null) killer.killCount++;
                    data.owner.TryGetComponent<LightcycleAI>(out LightcycleAI killerAI);
                    if (killerAI != null) killerAI.RegisterKill(gameObject);
                    currentEnergy = 0;
                    GameOver("Hit the opponent trail");
                    return;
                }
            }
            else if (hit.CompareTag("Player") || hit.CompareTag("AI"))
            {
                if (hit.gameObject != gameObject && hit.transform.root.gameObject != gameObject)
                {
                    Debug.Log($"<color=red><b>{gameObject.name} head-on crash with {hit.name}.</b></color>");
                    currentEnergy = 0;
                    GameOver("Hit other player");
                    return;
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  DEATH & RESPAWN
    // ═══════════════════════════════════════════════════════════════════════

    void GameOver(string reason)
    {
        if (isDead) return;
        isDead = true;
        deathCount++;

        LightcycleAI ai = GetComponent<LightcycleAI>();
        if (ai != null)
        {
            if (reason.Contains("out of bounds") || reason.Contains("boundary"))
                ai.AddReward(-0.2f);
            ai.Die(reason);
        }
        else
        {
            StartCoroutine(RespawnRoutine(reason));
        }
    }

    private System.Collections.IEnumerator RespawnRoutine(string reason)
    {
        isDead = true;
        Debug.Log($"<color=orange><b>{gameObject.name} eliminated! Reason: {reason}. (Deaths: {deathCount})</b></color>");

        // Destroy trail collision meshes
        foreach (var tc in GetComponentsInChildren<TrailCollision>())
            tc.DestroyTrailMesh();

        // --- Disable everything ---
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        PlayerMovement pm = GetComponent<PlayerMovement>();
        if (pm != null) pm.enabled = false;

        LightcycleAI ai = GetComponent<LightcycleAI>();
        if (ai != null) ai.enabled = false;

        Collider[] colliders = GetComponents<Collider>();
        foreach (var c in colliders) c.enabled = false;

        if (sparkTrail != null) { sparkTrail.emitting = false; sparkTrail.Clear(); }
        if (sparkLight != null) sparkLight.enabled = false;

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach (var r in renderers) r.enabled = false;

        Light[] lights = GetComponentsInChildren<Light>();
        foreach (var l in lights) l.enabled = false;

        // --- Countdown ---
        const float respawnDelay = 3f;
        if (respawnCountdownText != null)
        {
            respawnCountdownText.gameObject.SetActive(true);
            float remaining = respawnDelay;
            while (remaining > 0f)
            {
                respawnCountdownText.text = $"<b>RESPAWN IN <size=72>{Mathf.CeilToInt(remaining)}</size> SEC</b>";
                yield return new WaitForSeconds(Mathf.Min(1f, remaining));
                remaining -= 1f;
            }
            respawnCountdownText.gameObject.SetActive(false);
        }
        else
        {
            yield return new WaitForSeconds(respawnDelay);
        }

        // --- Respawn ---
        currentEnergy = maxEnergy;

        if (ai != null)
        {
            ai.currentCharge = ai.maxCharge;
            ai.currentState = LightcycleAI.AIState.Hunting;
            ai.ResetDirection();
        }

        float spawnRange = Mathf.Max(0, arenaLimit - 20f);
        transform.position = new Vector3(
            Random.Range(-spawnRange, spawnRange),
            1f,
            Random.Range(-spawnRange, spawnRange));
        transform.rotation = Quaternion.Euler(0, 0, 0);

        // Re-enable trail and re-sync its colour
        if (sparkTrail != null) { sparkTrail.Clear(); sparkTrail.emitting = true; }
        Color respawnColor = fullEnergyColor != Color.clear ? fullEnergyColor : Color.white;
        SyncTrailColor(respawnColor, trailBrightness);

        foreach (var tc in GetComponentsInChildren<TrailCollision>())
            tc.ResetTrail();

        // --- Re-enable everything ---
        if (cc != null) cc.enabled = true;
        if (pm != null) pm.enabled = true;
        if (ai != null) ai.enabled = true;
        foreach (var c in colliders) c.enabled = true;
        foreach (var r in renderers) r.enabled = true;
        if (sparkLight != null) sparkLight.enabled = true;
        foreach (var l in lights) l.enabled = true;

        isDead = false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SCORING
    // ═══════════════════════════════════════════════════════════════════════

    public int CompareScore(PlayerEnergy other)
    {
        int myScore = (killCount * 3) - deathCount;
        int otherScore = (other.killCount * 3) - other.deathCount;

        if (myScore != otherScore)
            return otherScore.CompareTo(myScore);
        if (killCount != other.killCount)
            return other.killCount.CompareTo(killCount);
        return other.currentEnergy.CompareTo(currentEnergy);
    }
}
