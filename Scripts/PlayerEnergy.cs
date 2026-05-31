using UnityEngine;
using TMPro;

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
    private bool hasTriggeredNextSpawn;
    private bool illuminatedThisFrame;
    private float _lastEnergyPercent = -1f;
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
            maxLightIntensity = 3e+11f;
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
        if (trailMat != null) return; // Already initialised

        // If the prefab has no material on the TrailRenderer, create one
        if (sparkTrail.sharedMaterial == null)
            sparkTrail.sharedMaterial = CreateTrailMaterial();

        trailMat = sparkTrail.material; // Runtime instance
        trailMat.EnableKeyword("_EMISSION");
        trailMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

        sparkTrail.emitting = true;

        // Apply the current color
        Color c = fullEnergyColor != Color.clear ? fullEnergyColor : Color.white;
        SyncTrailColor(c, trailBrightness);
    }

    /// <summary>
    /// Creates a URP Lit transparent + emissive material from scratch.
    /// Mirrors the settings in Assets/Materials/tail.mat.
    /// </summary>
    private static Material CreateTrailMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Sprites/Default");

        Material mat = new Material(shader) { name = "Trail_Runtime" };

        // Transparency
        mat.SetFloat("_Surface", 1f);       // Transparent
        mat.SetFloat("_Blend", 2f);         // Multiply
        mat.SetFloat("_SrcBlend", 5f);      // SrcAlpha
        mat.SetFloat("_DstBlend", 1f);      // One (additive glow)
        mat.SetFloat("_SrcBlendAlpha", 1f);
        mat.SetFloat("_DstBlendAlpha", 1f);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", 0f);          // Double-sided
        mat.renderQueue = 3000;

        mat.EnableKeyword("_EMISSION");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

        mat.SetColor("_BaseColor", Color.white);
        mat.SetColor("_Color", Color.white);
        mat.SetColor("_EmissionColor", Color.white);

        return mat;
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
        // Make sure the material exists (handles calls before Start)
        if (trailMat == null)
            InitTrailMaterial();

        if (trailMat != null)
        {
            float safe = Mathf.Clamp(brightness, 10f, 200f);
            Color hdr = colour * safe;
            hdr.a = 1f;
            trailMat.SetColor("_BaseColor", hdr);
            trailMat.SetColor("_Color", hdr);
            trailMat.SetColor("_EmissionColor", hdr);
        }

        // Set gradient: white vertex colour (material provides the hue),
        // with a gentle alpha fade at the tail end
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
                    new GradientAlphaKey(0.3f, 1f)
                }
            );
            sparkTrail.colorGradient = grad;
        }

        _lastEnergyPercent = -1f; // Force visual refresh
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  VISUALS — called every frame from Update()
    // ═══════════════════════════════════════════════════════════════════════

    private void UpdateVisuals()
    {
        float pct = currentEnergy / maxEnergy;

        // Spot light stays at constant full intensity — it's an identity glow, not a battery meter.
        // (Trail width and colour already communicate energy level.)
        if (sparkLight != null)
            sparkLight.intensity = maxLightIntensity * 5f;

        // Trail width + material colour
        if (sparkTrail != null && trailMat != null)
        {
            float w = Mathf.Max(1.5f, pct * maxTrailWidth);
            if (Mathf.Abs(sparkTrail.widthMultiplier - w) > 0.05f)
                sparkTrail.widthMultiplier = w;

            // Only update material when energy changes meaningfully
            if (Mathf.Abs(_lastEnergyPercent - pct) > 0.01f)
            {
                _lastEnergyPercent = pct;
                Color base_ = Color.Lerp(lowEnergyColor, fullEnergyColor, pct);
                base_.a = 1f;

                float safe = Mathf.Clamp(trailBrightness, 10f, 200f);
                Color hdr = base_ * safe;
                hdr.a = 1f;
                trailMat.SetColor("_BaseColor", hdr);
                trailMat.SetColor("_Color", hdr);
                trailMat.SetColor("_EmissionColor", hdr);
            }
        }
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
