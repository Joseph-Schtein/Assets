using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Orbiting direction arrow — always appears IN FRONT of the player and
/// rotates to point toward the nearest "Spotlight" charging zone.
/// A second, dimmer arrow points toward the second-nearest spotlight.
///
/// Hide/show is driven by PlayerEnergy.isIlluminated so the arrow disappears
/// the instant the player enters a charging zone and reappears when they leave.
/// </summary>
public class ArrowPointer : MonoBehaviour
{
    [Header("References")]
    public Transform playerTransform;   // The Player's Transform
    public PlayerEnergy playerEnergy;      // Reads isIlluminated for zone enter/exit

    [Header("Orbit Settings")]
    [SerializeField] private float orbitRadius = 4f;    // Distance in front of the player
    [SerializeField] private float hoverHeight = 1.5f;  // Y offset above the player (primary arrow)
    [SerializeField] private float secondHoverHeight = 1.5f;  // Y offset for the secondary arrow

    [Header("Arrow Shape")]
    [SerializeField] private float stemLength = 1.0f;   // Shaft length
    [SerializeField] private float stemWidth = 0.4f;   // Shaft width
    [SerializeField] private float tipLength = 0.8f;   // Arrowhead length
    [SerializeField] private float tipWidth = 0.9f;   // Arrowhead base width

    [Header("Visuals")]
    [Tooltip("Arrow color (stays constant — only brightness varies).")]
    [SerializeField] private Color colorFullTime = new Color(1f, 1f, 0f, 1f);  // bright yellow
    [Tooltip("Glow intensity at 50% (dying spotlight — arrow fades to half brightness).")]
    [SerializeField] private float glowIntensityMin = 4.2f;
    [Tooltip("Glow intensity at 100% (fresh spotlight).")]
    [SerializeField] private float glowIntensityMax = 6f;
    [Header("Second Arrow Visuals")]
    [Tooltip("Glow intensity for the secondary arrow (now matches primary).")]
    // secondary uses same intensity as primary now

    [Header("Search")]
    [SerializeField] private float updateInterval = 0.15f;

    // ── Primary arrow internals ───────────────────────────────────────────────
    private MeshRenderer _mr;
    private Transform _target;
    private LightSource _targetLightSource;
    private GameObject _arrowVisual;

    // ── Secondary arrow internals ─────────────────────────────────────────────
    private MeshRenderer _mr2;
    private Transform _target2;
    private LightSource _targetLightSource2;
    private GameObject _arrowVisual2;

    private Mesh _mesh;
    private MaterialPropertyBlock _propBlock;

    // ─────────────────────────────────────────────────────────────────────────
    void Awake()
    {
        _propBlock = new MaterialPropertyBlock();

        Shader urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
        Shader fallback = Shader.Find("Sprites/Default");
        Material baseMat = new Material(urpUnlit != null ? urpUnlit : fallback);
        baseMat.EnableKeyword("_EMISSION");
        baseMat.enableInstancing = true; // Force instancing

        // ── Primary arrow ─────────────────────────────────────────────────────
        _arrowVisual = new GameObject("ArrowVisual");
        _arrowVisual.transform.SetParent(null);

        MeshFilter mf = _arrowVisual.AddComponent<MeshFilter>();
        _mr = _arrowVisual.AddComponent<MeshRenderer>();

        _mesh = new Mesh { name = "ArrowMesh" };
        mf.mesh = _mesh;
        _mr.sharedMaterial = baseMat;

        _arrowVisual.transform.rotation = Quaternion.identity;

        GenerateArrow(_mesh);

        // ── Secondary arrow ───────────────────────────────────────────────────
        _arrowVisual2 = new GameObject("ArrowVisual2");
        _arrowVisual2.transform.SetParent(null);

        Mesh mesh2 = new Mesh { name = "ArrowMesh2" };
        MeshFilter mf2 = _arrowVisual2.AddComponent<MeshFilter>();
        _mr2 = _arrowVisual2.AddComponent<MeshRenderer>();
        mf2.mesh = mesh2;
        _mr2.sharedMaterial = baseMat;

        _arrowVisual2.transform.rotation = Quaternion.identity;

        GenerateArrow(mesh2);
    }

    void Start() => StartCoroutine(SearchLoop());

    // ─────────────────────────────────────────────────────────────────────────
    void Update()
    {
        bool inZone = playerEnergy != null && playerEnergy.isIlluminated;

        // Force height to be identical regardless of Inspector overrides
        secondHoverHeight = hoverHeight;

        // ── Primary arrow ─────────────────────────────────────────────────────
        UpdateArrow(
            _target, _targetLightSource,
            _arrowVisual, _mr,
            hoverHeight,
            colorFullTime,
            glowIntensityMin,
            glowIntensityMax,
            inZone);

        // ── Secondary arrow ───────────────────────────────────────────────────
        UpdateArrow(
            _target2, _targetLightSource2,
            _arrowVisual2, _mr2,
            secondHoverHeight,
            colorFullTime,
            glowIntensityMin,
            glowIntensityMax,
            inZone);
    }

    /// <summary>
    /// Shared update logic for a single arrow visual.
    /// </summary>
    private void UpdateArrow(
        Transform target,
        LightSource targetLS,
        GameObject visual,
        MeshRenderer mr,
        float yOffset,
        Color arrowColor,
        float intensityMin,
        float intensityMax,
        bool inZone)
    {
        if (playerTransform == null || target == null) { SetVisibleObj(mr, visual, false); return; }

        Vector3 toTarget = target.position - playerTransform.position;
        toTarget.y = 0f;

        if (toTarget.sqrMagnitude < 0.01f) { SetVisibleObj(mr, visual, false); return; }
        toTarget.Normalize();

        Vector3 arrowPos = playerTransform.position + toTarget * orbitRadius;
        arrowPos.y = playerTransform.position.y + yOffset;
        visual.transform.position = arrowPos;
        visual.transform.rotation = Quaternion.LookRotation(toTarget, Vector3.up);

        if (inZone) { SetVisibleObj(mr, visual, false); return; }
        SetVisibleObj(mr, visual, true);

        // Lerp glow from 100% (fresh spotlight) → 50% (dying spotlight) based on remaining lifetime.
        float lifetimeRatio = 1f; // default to full if no LightSource
        if (targetLS != null && targetLS.MaxTimeRemaining > 0f)
        {
            lifetimeRatio = Mathf.Clamp01(targetLS.TimeRemaining / targetLS.MaxTimeRemaining);
        }
        // Map ratio 0→1 to intensity range: 50% → 100% of intensityMax
        float glow = Mathf.Lerp(intensityMax * 0.5f, intensityMax, lifetimeRatio);
        Color finalColor = arrowColor * glow;
        finalColor.a = 1f; // Prevent alpha multiplication issues
        
        mr.GetPropertyBlock(_propBlock);
        _propBlock.SetColor("_BaseColor", finalColor);
        _propBlock.SetColor("_Color", finalColor);
        _propBlock.SetColor("_EmissionColor", finalColor);
        mr.SetPropertyBlock(_propBlock);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Procedural arrow mesh — flat in local XZ plane, pointing in local +Z
    //
    //  Top-down layout (X is right, Z is forward/up in this view):
    //
    //          [6]  ← tip point
    //          /  \
    //       [4]   [5]   ← head wings
    //        |     |
    //       [1]   [3]   ← shaft front
    //        |     |
    //       [0]   [2]   ← shaft back (origin)
    // ─────────────────────────────────────────────────────────────────────────
    private void GenerateArrow(Mesh mesh)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();

        float sw = stemWidth * 0.5f;
        float tw = tipWidth * 0.5f;

        verts.Add(new Vector3(-sw, 0f, 0f));
        verts.Add(new Vector3(-sw, 0f, stemLength));
        verts.Add(new Vector3(sw, 0f, 0f));
        verts.Add(new Vector3(sw, 0f, stemLength));

        tris.Add(0); tris.Add(1); tris.Add(2);
        tris.Add(2); tris.Add(1); tris.Add(3);

        verts.Add(new Vector3(-tw, 0f, stemLength));
        verts.Add(new Vector3(tw, 0f, stemLength));
        verts.Add(new Vector3(0f, 0f, stemLength + tipLength));

        tris.Add(4); tris.Add(6); tris.Add(5);

        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Throttled search: find nearest + second-nearest Spotlight-tagged objects
    // ─────────────────────────────────────────────────────────────────────────
    private IEnumerator SearchLoop()
    {
        while (true)
        {
            FindSpotlights();
            yield return new WaitForSeconds(updateInterval);
        }
    }

    private void FindSpotlights()
    {
        if (playerTransform == null) { SetVisible(false); return; }

        GameObject[] spots = GameObject.FindGameObjectsWithTag("Spotlight");

        Transform nearest = null; LightSource nearestLS = null; float nearestDist = Mathf.Infinity;
        Transform second = null; LightSource secondLS = null; float secondDist = Mathf.Infinity;

        foreach (GameObject spot in spots)
        {
            float d = Vector3.Distance(playerTransform.position, spot.transform.position);
            if (d < nearestDist)
            {
                // Push old nearest down to second
                second = nearest; secondLS = nearestLS; secondDist = nearestDist;
                nearest = spot.transform;
                nearestLS = spot.GetComponent<LightSource>();
                nearestDist = d;
            }
            else if (d < secondDist)
            {
                second = spot.transform;
                secondLS = spot.GetComponent<LightSource>();
                secondDist = d;
            }
        }

        _target = nearest;
        _targetLightSource = nearestLS;
        _target2 = second;
        _targetLightSource2 = secondLS;

        if (nearest == null) SetVisible(false);
        if (second == null) SetVisibleObj(_mr2, _arrowVisual2, false);
    }

    private void SetVisible(bool show)
    {
        if (_mr != null) _mr.enabled = show;
        if (_arrowVisual != null) _arrowVisual.SetActive(show);
        if (_mr2 != null) _mr2.enabled = show;
        if (_arrowVisual2 != null) _arrowVisual2.SetActive(show);
    }

    private static void SetVisibleObj(MeshRenderer mr, GameObject visual, bool show)
    {
        if (mr != null) mr.enabled = show;
        if (visual != null) visual.SetActive(show);
    }

    void OnDestroy()
    {
        if (_arrowVisual != null) Destroy(_arrowVisual);
        if (_arrowVisual2 != null) Destroy(_arrowVisual2);
    }
}