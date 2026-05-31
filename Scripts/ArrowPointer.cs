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
    public Transform    playerTransform;   // The Player's Transform
    public PlayerEnergy playerEnergy;      // Reads isIlluminated for zone enter/exit

    [Header("Orbit Settings")]
    [SerializeField] private float orbitRadius = 4f;    // Distance in front of the player
    [SerializeField] private float hoverHeight = 1.5f;  // Y offset above the player (primary arrow)
    [SerializeField] private float secondHoverHeight = 0.5f;  // Y offset for the secondary arrow

    [Header("Arrow Shape")]
    [SerializeField] private float stemLength = 1.0f;   // Shaft length
    [SerializeField] private float stemWidth  = 0.4f;   // Shaft width
    [SerializeField] private float tipLength  = 0.8f;   // Arrowhead length
    [SerializeField] private float tipWidth   = 0.9f;   // Arrowhead base width

    [Header("Visuals")]
    [Tooltip("Arrow color when the spotlight has plenty of time left.")]
    [SerializeField] private Color colorFullTime = new Color(1f, 1f, 0f, 1f);  // bright yellow
    [Tooltip("Arrow color when the spotlight is about to shrink.")]
    [SerializeField] private Color colorShrinking = new Color(0.25f, 0.25f, 0f, 1f); // dull yellow
    [SerializeField] private float glowIntensityMin = 2f;
    [SerializeField] private float glowIntensityMax = 6f;
    [SerializeField] private float pulseSpeed       = 3.5f;

    [Header("Second Arrow Visuals")]
    [Tooltip("Base color for the second arrow (full-time).")]
    [SerializeField] private Color secondColorFullTime   = new Color(0.4f, 0.8f, 1f, 1f);  // cool cyan-white
    [Tooltip("Urgency color for the second arrow.")]
    [SerializeField] private Color secondColorShrinking  = new Color(0.1f, 0.2f, 0.25f, 1f);
    [Tooltip("Glow intensity for the secondary arrow — intentionally dimmer.")]
    [SerializeField] private float secondGlowIntensity   = 1.2f;

    [Header("Search")]
    [SerializeField] private float updateInterval = 0.15f;

    // ── Primary arrow internals ───────────────────────────────────────────────
    private MeshRenderer  _mr;
    private Material      _mat;
    private Transform     _target;
    private LightSource   _targetLightSource;
    private GameObject    _arrowVisual;

    // ── Secondary arrow internals ─────────────────────────────────────────────
    private MeshRenderer  _mr2;
    private Material      _mat2;
    private Transform     _target2;
    private LightSource   _targetLightSource2;
    private GameObject    _arrowVisual2;

    private Mesh          _mesh;
    private List<Vector3> _vertices  = new List<Vector3>();
    private List<int>     _triangles = new List<int>();

    // ─────────────────────────────────────────────────────────────────────────
    void Awake()
    {
        // ── Primary arrow ─────────────────────────────────────────────────────
        _arrowVisual = new GameObject("ArrowVisual");
        _arrowVisual.transform.SetParent(null);

        MeshFilter mf = _arrowVisual.AddComponent<MeshFilter>();
        _mr           = _arrowVisual.AddComponent<MeshRenderer>();

        _mesh      = new Mesh { name = "ArrowMesh" };
        mf.mesh    = _mesh;

        _mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        if (_mat == null)
            _mat = new Material(Shader.Find("Sprites/Default"));

        _mat.SetColor("_BaseColor", colorFullTime * glowIntensityMin);
        _mr.material = _mat;

        _arrowVisual.transform.rotation = Quaternion.identity;

        GenerateArrow(_mesh);

        // ── Secondary arrow ───────────────────────────────────────────────────
        _arrowVisual2 = new GameObject("ArrowVisual2");
        _arrowVisual2.transform.SetParent(null);

        Mesh mesh2        = new Mesh { name = "ArrowMesh2" };
        MeshFilter mf2    = _arrowVisual2.AddComponent<MeshFilter>();
        _mr2              = _arrowVisual2.AddComponent<MeshRenderer>();
        mf2.mesh          = mesh2;

        _mat2 = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        if (_mat2 == null)
            _mat2 = new Material(Shader.Find("Sprites/Default"));

        _mat2.SetColor("_BaseColor", secondColorFullTime * secondGlowIntensity);
        _mr2.material = _mat2;

        _arrowVisual2.transform.rotation = Quaternion.identity;

        GenerateArrow(mesh2);
    }

    void Start() => StartCoroutine(SearchLoop());

    // ─────────────────────────────────────────────────────────────────────────
    void Update()
    {
        bool inZone = playerEnergy != null && playerEnergy.isIlluminated;

        // ── Primary arrow ─────────────────────────────────────────────────────
        UpdateArrow(
            _target, _targetLightSource,
            _arrowVisual, _mr, _mat,
            hoverHeight,
            colorFullTime, colorShrinking,
            glowIntensityMin,
            inZone);

        // ── Secondary arrow ───────────────────────────────────────────────────
        UpdateArrow(
            _target2, _targetLightSource2,
            _arrowVisual2, _mr2, _mat2,
            secondHoverHeight,
            secondColorFullTime, secondColorShrinking,
            secondGlowIntensity,
            inZone);
    }

    /// <summary>
    /// Shared update logic for a single arrow visual.
    /// </summary>
    private void UpdateArrow(
        Transform    target,
        LightSource  targetLS,
        GameObject   visual,
        MeshRenderer mr,
        Material     mat,
        float        yOffset,
        Color        colorFull,
        Color        colorUrgent,
        float        intensity,
        bool         inZone)
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

        // Intensity driven by the spotlight's remaining lifetime, NOT player energy.
        // t = 1 → spotlight just spawned  → 100% intensity (full glow)
        // t = 0 → spotlight about to die  → 50%  intensity (visible but clearly dimming)
        // Default t = 1f when LightSource can't be read so the arrow is never black.
        float t = 1f;
        if (targetLS != null && targetLS.MaxTimeRemaining > 0f)
            t = Mathf.Clamp01(targetLS.TimeRemaining / targetLS.MaxTimeRemaining);

        // Lerp from a colour that still reads clearly on dark backgrounds (colorUrgent at 50%) to
        // the fully-bright fresh colour, so the arrow is always visible.
        Color baseColor = Color.Lerp(colorUrgent, colorFull, t);

        // Scale overall glow: 100% at t=1 (fresh), 50% at t=0 (dying)
        float glow = Mathf.Lerp(intensity * 0.5f, intensity, t);
        mat.SetColor("_BaseColor", baseColor * glow);
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
        var tris  = new List<int>();

        float sw = stemWidth  * 0.5f;
        float tw = tipWidth   * 0.5f;

        verts.Add(new Vector3(-sw, 0f, 0f));
        verts.Add(new Vector3(-sw, 0f, stemLength));
        verts.Add(new Vector3( sw, 0f, 0f));
        verts.Add(new Vector3( sw, 0f, stemLength));

        tris.Add(0); tris.Add(1); tris.Add(2);
        tris.Add(2); tris.Add(1); tris.Add(3);

        verts.Add(new Vector3(-tw, 0f, stemLength));
        verts.Add(new Vector3( tw, 0f, stemLength));
        verts.Add(new Vector3(  0f, 0f, stemLength + tipLength));

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

        Transform   nearest      = null;  LightSource nearestLS   = null;  float nearestDist   = Mathf.Infinity;
        Transform   second       = null;  LightSource secondLS    = null;  float secondDist    = Mathf.Infinity;

        foreach (GameObject spot in spots)
        {
            float d = Vector3.Distance(playerTransform.position, spot.transform.position);
            if (d < nearestDist)
            {
                // Push old nearest down to second
                second    = nearest;    secondLS  = nearestLS;   secondDist  = nearestDist;
                nearest   = spot.transform;
                nearestLS = spot.GetComponent<LightSource>();
                nearestDist = d;
            }
            else if (d < secondDist)
            {
                second    = spot.transform;
                secondLS  = spot.GetComponent<LightSource>();
                secondDist = d;
            }
        }

        _target            = nearest;
        _targetLightSource = nearestLS;
        _target2           = second;
        _targetLightSource2 = secondLS;

        if (nearest == null) SetVisible(false);
        if (second  == null) SetVisibleObj(_mr2, _arrowVisual2, false);
    }

    private void SetVisible(bool show)
    {
        if (_mr  != null) _mr.enabled  = show;
        if (_arrowVisual  != null) _arrowVisual.SetActive(show);
        if (_mr2 != null) _mr2.enabled = show;
        if (_arrowVisual2 != null) _arrowVisual2.SetActive(show);
    }

    private static void SetVisibleObj(MeshRenderer mr, GameObject visual, bool show)
    {
        if (mr     != null) mr.enabled = show;
        if (visual != null) visual.SetActive(show);
    }

    void OnDestroy()
    {
        if (_arrowVisual  != null) Destroy(_arrowVisual);
        if (_arrowVisual2 != null) Destroy(_arrowVisual2);
    }
}