using UnityEngine;
using UnityEngine.UI;
using TMPro;
public class PlayerEnergy : MonoBehaviour
{
    [Header("Settings")]
    public float maxEnergy = 100f;
    public float drainRate = 12f;
    public float rechargeRate = 40f; // Speed of recharging inside the circle

    [Header("Arena Settings")]
    public float arenaLimit = 150f;

    [Header("Visual References")]
    public Light sparkLight;
    public TrailRenderer sparkTrail;
    [Tooltip("Assign the canvas Text that shows the respawn countdown (human player only).")]
    public TextMeshProUGUI respawnCountdownText;

    [ColorUsage(true, true)] public Color fullEnergyColor;
    [ColorUsage(true, true)] public Color lowEnergyColor;

    [Header("Visual Bounds")]
    public float maxLightIntensity = 5f;
    public float maxTrailTime = 3f;   // Seconds of trail history — decreased to force aggressive cut-offs
    public float maxTrailWidth = 3f;    // Width in world units — increase to stay visible from above
    public float trailBrightness = 500000f;    // HDR multiplier — drives bloom glow on the trail (try 5–20)

    [Header("Current State")]
    public float currentEnergy;
    public bool isIlluminated = false;

    public int deathCount = 0;
    public int killCount = 0;

    public bool isDead = false;

    private LightSpawner spawner;
    private bool hasTriggeredNextSpawn = false;
    private Material trailMat;  // Runtime material instance — we drive emission on this directly
    private Vector3 startPos;
    private Quaternion startRot;

    public bool randomiseColorOnStart = false;

    private static Collider[] _hitCache = new Collider[64];
    private LightcycleAI _aiComponent;
    private float _lastEnergyPercent = -1f;

    void Start()
    {
        startPos = transform.position;
        startRot = transform.rotation;
        currentEnergy = maxEnergy;
        spawner = FindFirstObjectByType<LightSpawner>();
        _aiComponent = GetComponent<LightcycleAI>();

        // Dynamically adjust arena limit to match the actual scaled GameField
        int aiCount = PlayerPrefs.GetInt("AIPlayerCount", 15);
        arenaLimit = Mathf.Lerp(100f, 250f, (aiCount - 5f) / 15f);

        // In non-game scenes (e.g. MainMenu) there is no bloom post-processing,
        // so the HDR values used in-game wash the spotlight and trail to white.
        // Cap both to sane values that show the correct colour without bloom.
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "SampleScene")
        {
            maxLightIntensity = 3e+11f;      // High intensity needed for scenes without bloom
            trailBrightness = 5f;            // drives emission to solid white
        }

        // Apply random colors immediately if enabled (great for Main Menu AIs)
        if (randomiseColorOnStart)
        {
            ApplyRandomColor();
        }

        // Cache and prepare the trail's material instance for HDR emission.
        // IMPORTANT: SyncTrailColor (called from ApplyRandomColor before Start()) may have
        // already created trailMat eagerly. Reuse that instance so the color is preserved.
        if (sparkTrail != null)
        {
            if (trailMat == null)
            {
                trailMat = sparkTrail.material;    // Creates a unique runtime instance
                trailMat.EnableKeyword("_EMISSION");
            }
            sparkTrail.emitting = true;            // Guarantee the trail emits from the very start

            // Always re-sync colour + emission after Start() resolves the final trailBrightness.
            // This covers the case where ApplyRandomColor ran before trailBrightness was adjusted
            // for non-SampleScene (line 68 above).
            Color initColor = fullEnergyColor != Color.clear ? fullEnergyColor : Color.white;
            SyncTrailColor(initColor, trailBrightness);
        }
    }

    void Update()
    {
        if (isDead) return;

        // Field Limit Death Check
        if (Mathf.Abs(transform.position.x) >= arenaLimit || Mathf.Abs(transform.position.z) >= arenaLimit)
        {
            Debug.Log($"<color=red><b>{gameObject.name} disqualified! Hit the field limit.</b></color>");
            currentEnergy = 0;
            GameOver("Get out of bounds");
            return;
        }

        // Drive the visual effects every frame for smooth animation
        UpdateSparkVisuals();
    }

    public void ApplyRandomColor()
    {
        Color randomColor = Random.ColorHSV(0f, 1f, 0.6f, 1f, 0.8f, 1f);
        fullEnergyColor = randomColor;

        Color dimColor = randomColor * 0.2f;
        dimColor.a = 1f;
        lowEnergyColor = dimColor;

        if (sparkLight != null) sparkLight.color = randomColor;

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            if (!(r is TrailRenderer))
            {
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                r.GetPropertyBlock(block);
                block.SetColor("_Color", randomColor);
                block.SetColor("_BaseColor", randomColor);
                block.SetColor("_EmissionColor", randomColor * 2f);
                r.SetPropertyBlock(block);
            }
        }

        Transform headlight = transform.Find("Headlight");
        if (headlight != null)
        {
            Light hl = headlight.GetComponent<Light>();
            //if (hl != null) hl.color = Color.Lerp(Color.white, randomColor, 0.6f);
        }

        // Also apply the color to the trail material (including HDR emission for bloom glow).
        // SyncTrailColor initialises trailMat eagerly if Start() hasn't run yet.
        SyncTrailColor(randomColor, trailBrightness);
    }

    /// <summary>
    /// Called by MenuAISpawner (or any external spawner) immediately after assigning
    /// fullEnergyColor to push the colour into the cached trail material instance.
    /// Sets the full colorGradient (not just startColor/endColor) so both the colour
    /// AND the alpha fade are correct from the very first frame.
    /// </summary>
    public void SyncTrailColor(Color colour, float brightness)
    {
        if (trailMat == null)
        {
            // Start() hasn't run yet — cache now so the colour is ready.
            if (sparkTrail != null)
            {
                trailMat = sparkTrail.material;
                trailMat.EnableKeyword("_EMISSION");
            }
        }

        if (trailMat != null)
        {
            // KEY FIX: Set _BaseColor to a BRIGHT HDR value so the trail is visible
            // even without bloom. The vertex gradient multiplies this, giving a coloured glow.
            // Clamp at 200 so the colour doesn't wash to pure white on SDR displays.
            float safeBrightness = Mathf.Clamp(brightness, 10f, 200f);
            Color hdrColor = colour * safeBrightness;
            hdrColor.a = 1f;
            trailMat.SetColor("_BaseColor", hdrColor);
            trailMat.SetColor("_Color", hdrColor);
            // Also drive _EmissionColor for bloom when post-processing IS present
            trailMat.SetColor("_EmissionColor", hdrColor);
        }

        // Keep gradient fully opaque so the entire trail length is visible,
        // not just the newest segment at the head.
        if (sparkTrail != null)
        {
            Gradient grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.3f, 1f)   // slight fade at tail, but still visible
                }
            );
            sparkTrail.colorGradient = grad;
        }

        // Force UpdateSparkVisuals to re-apply on its next call — otherwise the
        // energy-delta threshold might skip the update if energy hasn't changed.
        _lastEnergyPercent = -1f;
    }

    void UpdateSparkVisuals()
    {
        float energyPercent = currentEnergy / maxEnergy;

        if (sparkLight != null)
        {
            sparkLight.intensity = energyPercent * maxLightIntensity * 5f;
        }

        if (sparkTrail != null && trailMat != null)
        {
            // Trail width: never go below 1.5 so it's always visible
            float targetWidth = Mathf.Max(1.5f, energyPercent * maxTrailWidth);
            if (Mathf.Abs(sparkTrail.widthMultiplier - targetWidth) > 0.05f)
                sparkTrail.widthMultiplier = targetWidth;

            // ONLY update material properties if energy has changed significantly
            if (Mathf.Abs(_lastEnergyPercent - energyPercent) > 0.01f)
            {
                _lastEnergyPercent = energyPercent;
                Color baseColor = Color.Lerp(lowEnergyColor, fullEnergyColor, energyPercent);
                baseColor.a = 1f;

                // KEY FIX: Set _BaseColor to HDR-bright value — visible with OR without bloom.
                // Capped at 200× so colour stays recognizable on SDR displays.
                float safeBrightness = Mathf.Clamp(trailBrightness, 10f, 200f);
                Color hdrColor = baseColor * safeBrightness;
                hdrColor.a = 1f;
                trailMat.SetColor("_BaseColor", hdrColor);
                trailMat.SetColor("_Color", hdrColor);
                trailMat.SetColor("_EmissionColor", hdrColor); // for bloom if present
            }
        }
    }

    public void UseEnergy(float amount)
    {
        currentEnergy -= amount;
    }

    // ── Spotlight detection ───────────────────────────────────────────────────
    // Uses OnTriggerStay (same as LightcycleAI) instead of Enter/Exit.
    // Enter/Exit is unreliable here because the player's BoxCollider (trigger)
    // also detects the trail's MeshCollider, causing false Enter events.
    // OnTriggerStay fires every physics frame while overlapping — we set a
    // one-frame flag and let it expire naturally when we leave the zone.

    private bool illuminatedThisFrame = false;

    void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("Spotlight"))
        {
            illuminatedThisFrame = true;
        }
    }

    void FixedUpdate()
    {
        // OnTriggerStay fires BEFORE FixedUpdate in Unity's execution order.
        // So by the time we read illuminatedThisFrame here, it is already set for this physics step.
        isIlluminated = illuminatedThisFrame;
        illuminatedThisFrame = false; // Reset; will be set again next FixedUpdate if still inside

        if (isDead) return;

        // ── Energy Charging / Draining (in FixedUpdate so it reads isIlluminated correctly) ──
        if (isIlluminated)
        {
            currentEnergy += rechargeRate * Time.fixedDeltaTime;

            // Once fully charged, consume this light and spawn the next one
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

        // ── Robust Manual Collision Check ────────────────────────────────────
        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, 1.5f, _hitCache, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = _hitCache[i];
            // 1. Hit a trail
            if (hit.CompareTag("DeadlyTrail"))
            {
                hit.TryGetComponent<TrailData>(out TrailData data);
                if (data != null && data.owner != this.gameObject)
                {
                    Debug.Log($"<color=red><b>{gameObject.name} disqualified! Crashed into {data.owner.name}'s trail.</b></color>");

                    data.owner.TryGetComponent<PlayerEnergy>(out PlayerEnergy killer);
                    if (killer != null) killer.killCount++;

                    data.owner.TryGetComponent<LightcycleAI>(out LightcycleAI killerAI);
                    if (killerAI != null) killerAI.RegisterKill(this.gameObject);

                    currentEnergy = 0;
                    GameOver("Hit the opponent trail");
                    return;
                }
            }
            // 2. Direct head-to-head crash
            else if (hit.CompareTag("Player") || hit.CompareTag("AI"))
            {
                if (hit.gameObject != this.gameObject && hit.transform.root.gameObject != this.gameObject)
                {
                    Debug.Log($"<color=red><b>{gameObject.name} disqualified! Head-on crash with {hit.name}.</b></color>");
                    currentEnergy = 0;
                    GameOver("Hit other player");
                    return;
                }
            }
        }
    }

    void GameOver(string reason)
    {
        if (isDead) return;
        isDead = true;
        deathCount++;

        // Notify the AI brain that it died so it receives the penalty and resets the episode
        LightcycleAI ai = GetComponent<LightcycleAI>();
        if (ai != null)
        {
            // Extra penalty for hitting the boundary so it learns to steer away
            if (reason.Contains("out of bounds") || reason.Contains("boundary"))
            {
                ai.AddReward(-0.2f); // Additional -0.2 (Total -0.4)
            }
            // Actually call the AI's Die method so it handles its own death penalty and respawn
            ai.Die(reason);
        }
        else
        {
            // If it's a human player, just use the local respawn routine
            StartCoroutine(RespawnRoutine(reason));
        }
    }

    private System.Collections.IEnumerator RespawnRoutine(string reason)
    {
        isDead = true;
        Debug.Log($"<color=orange><b>Player {gameObject.name} eliminated! Reason: {reason}. (Deaths: {deathCount})</b></color>");

        // ── Destroy trail meshes immediately on death ────────────────────────
        TrailCollision[] trailColls = GetComponentsInChildren<TrailCollision>();
        foreach (var tc in trailColls) tc.DestroyTrailMesh();

        // Disable components to "hide" the player and stop movement
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        PlayerMovement pm = GetComponent<PlayerMovement>();
        if (pm != null) pm.enabled = false;

        LightcycleAI ai = GetComponent<LightcycleAI>();
        if (ai != null) ai.enabled = false;

        Collider[] colliders = GetComponents<Collider>();
        foreach (var c in colliders) c.enabled = false;

        if (sparkTrail != null)
        {
            sparkTrail.emitting = false;
            sparkTrail.Clear();
        }

        if (sparkLight != null) sparkLight.enabled = false;

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach (var r in renderers) r.enabled = false;

        Light[] lights = GetComponentsInChildren<Light>();
        foreach (var l in lights) l.enabled = false;

        // ── Respawn countdown UI (human player only) ─────────────────────────
        const float respawnDelay = 3f;
        if (respawnCountdownText != null)
        {
            respawnCountdownText.gameObject.SetActive(true);
            float remaining = respawnDelay;
            while (remaining > 0f)
            {
                int secs = Mathf.CeilToInt(remaining);
                respawnCountdownText.text = $"<b>RESPAWN IN <size=72>{secs}</size> SEC</b>";
                yield return new WaitForSeconds(Mathf.Min(1f, remaining));
                remaining -= 1f;
            }
            respawnCountdownText.gameObject.SetActive(false);
        }
        else
        {
            yield return new WaitForSeconds(respawnDelay);
        }

        // Respawn logic
        currentEnergy = maxEnergy;

        if (ai != null)
        {
            ai.currentCharge = ai.maxCharge;
            ai.currentState = LightcycleAI.AIState.Hunting;
            ai.ResetDirection(); // Snap back to a clean cardinal direction
        }

        // Random respawn within arena limits (buffer of 20 units from the edge)
        float spawnRange = Mathf.Max(0, arenaLimit - 20f);
        transform.position = new Vector3(
            Random.Range(-spawnRange, spawnRange),
            1f,
            Random.Range(-spawnRange, spawnRange)
        );
        transform.rotation = Quaternion.Euler(0, 0, 0);

        if (sparkTrail != null)
        {
            sparkTrail.Clear();
            sparkTrail.emitting = true;
        }

        // Re-initialise fresh trail collision trackers after respawn
        TrailCollision[] freshTrailColls = GetComponentsInChildren<TrailCollision>();
        foreach (var tc in freshTrailColls) tc.ResetTrail();

        if (cc != null) cc.enabled = true;
        if (pm != null) pm.enabled = true;
        if (ai != null) ai.enabled = true;

        foreach (var c in colliders) c.enabled = true;
        foreach (var r in renderers) r.enabled = true;
        if (sparkLight != null) sparkLight.enabled = true;
        foreach (var l in lights) l.enabled = true;

        isDead = false;
    }

    public int CompareScore(PlayerEnergy other)
    {
        int myScore = (this.killCount * 3) - this.deathCount;
        int otherScore = (other.killCount * 3) - other.deathCount;

        // Primary: Higher total score is better
        if (myScore != otherScore)
        {
            return otherScore.CompareTo(myScore);
        }

        // Tie breaker 1: Higher kill count is better
        if (this.killCount != other.killCount)
        {
            return other.killCount.CompareTo(this.killCount);
        }

        // Tie breaker 2: Higher current energy is better
        return other.currentEnergy.CompareTo(this.currentEnergy);
    }
}
