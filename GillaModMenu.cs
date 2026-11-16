// GillaModMenu.cs
// MIT License - free to use, modify, and redistribute.
// Adapted for MelonLoader / LemonLoader (Standalone Quest 3).

using System;
using System.Collections.Generic;
using System.Text;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(GillaModLoader), "GillaModMenu", "1.0.0", "Community")]
[assembly: MelonGame(null, null)]

public class GillaModLoader : MelonMod
{
    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        if (GameObject.Find("GillaModMenuObj") == null)
        {
            var go = new GameObject("GillaModMenuObj");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var menu = go.AddComponent<GillaModMenu>();
            menu.allowInReleaseBuilds = true;
        }
    }
}

public class GillaModMenu : MonoBehaviour
{
    [Header("Safety")]
    public bool allowInReleaseBuilds = true;

    [Header("Rig references (auto-found at runtime)")]
    public Transform head;
    public Transform leftTracked;
    public Transform rightTracked;
    public Transform leftHand;
    public Transform rightHand;
    public Rigidbody playerBody;
    public Collider[] bodyColliders;
    public Collider[] handColliders;

    [Header("Platforms")]
    public int platformLayer = 0;
    public Vector3 platformSize = new Vector3(0.35f, 0.04f, 0.35f);
    public Color platformColor = new Color(0.2f, 0.8f, 0.4f, 1f);
    public float platformDrop = 0.06f;

    [Header("Menu text")]
    public float menuDistance = 0.9f;

    // ---- state ----
    bool platformsOn, longArmsOn, flyOn, noclipOn;
    float armScale = 1.75f;
    float flySpeed = 6f;

    bool menuOpen, latchX, latchY, wasFree, savedGravity;
    int cursor;

    TextMesh menuText;
    GameObject leftPlatform, rightPlatform;
    List<Entry> entries;

    class Entry
    {
        public string name;
        public Func<string> state;
        public Action toggle;
        public Action<int> adjust;
        public Entry(string n, Func<string> s, Action t, Action<int> a = null)
        {
            name = n; state = s; toggle = t; adjust = a;
        }
    }

    bool Allowed => allowInReleaseBuilds || Application.isEditor || Debug.isDebugBuild;

    // ------------------------------------------------------------------ setup

    void Start()
    {
        if (!Allowed) { enabled = false; return; }

        FindRigReferences();

        entries = new List<Entry>
        {
            new Entry("Platforms", () => OnOff(platformsOn), () => platformsOn = !platformsOn),
            new Entry("Long Arms", () => OnOff(longArmsOn) + "  x" + armScale.ToString("0.00"),
                      () => longArmsOn = !longArmsOn,
                      d => armScale = Mathf.Clamp(armScale + 0.25f * d, 1f, 4f)),
            new Entry("Fly", () => OnOff(flyOn) + "  " + flySpeed.ToString("0") + " m/s",
                      () => flyOn = !flyOn,
                      d => flySpeed = Mathf.Clamp(flySpeed + d, 2f, 20f)),
            new Entry("Noclip", () => OnOff(noclipOn), ToggleNoclip),
        };

        BuildMenuText();
        leftPlatform = MakePlatform("GillaPlatform_L");
        rightPlatform = MakePlatform("GillaPlatform_R");
        SetMenu(false);
    }

    void FindRigReferences()
    {
        if (head == null && Camera.main != null)
            head = Camera.main.transform;

        var leftAnchor = GameObject.Find("LeftHandAnchor");
        if (leftAnchor != null) leftTracked = leftAnchor.transform;

        var rightAnchor = GameObject.Find("RightHandAnchor");
        if (rightAnchor != null) rightTracked = rightAnchor.transform;

        if (leftHand == null) leftHand = leftTracked;
        if (rightHand == null) rightHand = rightTracked;

        if (head != null && playerBody == null)
        {
            playerBody = head.GetComponentInParent<Rigidbody>();
            if (playerBody != null)
                bodyColliders = playerBody.GetComponentsInChildren<Collider>();
        }
    }

    void BuildMenuText()
    {
        var go = new GameObject("GillaModMenuText");
        DontDestroyOnLoad(go);
        menuText = go.AddComponent<TextMesh>();
        Font f = LoadFont();
        menuText.font = f;
        go.GetComponent<MeshRenderer>().material = f.material;
        menuText.fontSize = 64;
        menuText.characterSize = 0.01f;
        menuText.anchor = TextAnchor.MiddleCenter;
        menuText.alignment = TextAlignment.Left;
        menuText.color = Color.white;
    }

    static Font LoadFont()
    {
        try { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
        catch { return Resources.GetBuiltinResource<Font>("Arial.ttf"); }
    }

    GameObject MakePlatform(string objName)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DontDestroyOnLoad(go);
        go.name = objName;
        go.transform.localScale = platformSize;
        go.layer = platformLayer;
        go.GetComponent<Renderer>().material.color = platformColor;
        go.SetActive(false);
        return go;
    }

    // ------------------------------------------------------------------ input / menu

    void Update()
    {
        if (head == null) FindRigReferences();

        if (OVRInput.GetDown(OVRInput.Button.Start, OVRInput.Controller.LTouch))
            SetMenu(!menuOpen);

        if (!menuOpen) return;

        Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);

        int dy = Step(stick.y, ref latchY);
        if (dy != 0) cursor = (cursor - dy + entries.Count) % entries.Count;

        Entry e = entries[cursor];
        int dx = Step(stick.x, ref latchX);
        if (dx != 0 && e.adjust != null) e.adjust(dx);

        if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch)) e.toggle();
        if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch)) SetMenu(false);

        RefreshText();
    }

    static int Step(float v, ref bool latched)
    {
        if (Mathf.Abs(v) < 0.5f) { latched = false; return 0; }
        if (latched) return 0;
        latched = true;
        return v > 0f ? 1 : -1;
    }

    void SetMenu(bool open)
    {
        menuOpen = open;
        menuText.gameObject.SetActive(open);
        if (!open || head == null) return;

        Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        menuText.transform.position = head.position + fwd * menuDistance + Vector3.down * 0.1f;
        menuText.transform.rotation = Quaternion.LookRotation(fwd);
        RefreshText();
    }

    void RefreshText()
    {
        var sb = new StringBuilder("GILLA VR MOD MENU\n\n");
        for (int i = 0; i < entries.Count; i++)
        {
            sb.Append(i == cursor ? "> " : "   ")
              .Append(entries[i].name).Append(": ")
              .Append(entries[i].state()).Append('\n');
        }
        sb.Append("\nStick: move/adjust   A: toggle   B: close");
        menuText.text = sb.ToString();
    }

    static string OnOff(bool b) { return b ? "ON" : "off"; }

    // ------------------------------------------------------------------ features

    void LateUpdate()
    {
        if (longArmsOn && head != null)
        {
            ExtendHand(leftHand, leftTracked, -1f);
            ExtendHand(rightHand, rightTracked, 1f);
        }

        UpdatePlatform(leftPlatform, leftHand != null ? leftHand : leftTracked, OVRInput.Controller.LTouch);
        UpdatePlatform(rightPlatform, rightHand != null ? rightHand : rightTracked, OVRInput.Controller.RTouch);
    }

    void ExtendHand(Transform hand, Transform tracked, float side)
    {
        if (hand == null || tracked == null) return;
        Quaternion yaw = Quaternion.Euler(0f, head.eulerAngles.y, 0f);
        Vector3 shoulder = head.position + yaw * new Vector3(0.18f * side, -0.2f, 0f);
        hand.position = shoulder + (tracked.position - shoulder) * armScale;
        hand.rotation = tracked.rotation;
    }

    void UpdatePlatform(GameObject plat, Transform hand, OVRInput.Controller ctrl)
    {
        if (plat == null || hand == null) return;

        if (!platformsOn || menuOpen)
        {
            if (plat.activeSelf) plat.SetActive(false);
            return;
        }

        if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, ctrl))
        {
            plat.transform.SetPositionAndRotation(
                hand.position + Vector3.down * platformDrop,
                Quaternion.Euler(0f, hand.eulerAngles.y, 0f));
            plat.SetActive(true);
        }
        if (OVRInput.GetUp(OVRInput.Button.PrimaryHandTrigger, ctrl))
            plat.SetActive(false);
    }

    void ToggleNoclip()
    {
        noclipOn = !noclipOn;
        ApplyNoclip(noclipOn);
    }

    void ApplyNoclip(bool on)
    {
        if (bodyColliders != null)
            foreach (var c in bodyColliders) if (c != null) c.enabled = !on;
        if (handColliders != null)
            foreach (var c in handColliders) if (c != null) c.enabled = !on;
    }

    void FixedUpdate()
    {
        if (playerBody == null) return;

        bool free = flyOn || noclipOn;
        if (free && !wasFree) savedGravity = playerBody.useGravity;
        if (!free && wasFree) playerBody.useGravity = savedGravity;
        wasFree = free;
        if (!free) return;

        playerBody.useGravity = false;

        bool thrust = !menuOpen && rightTracked != null &&
                      OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RTouch);
        Vector3 target = thrust ? rightTracked.forward * flySpeed : Vector3.zero;
        SetVelocity(Vector3.Lerp(GetVelocity(), target, 8f * Time.fixedDeltaTime));
    }

    Vector3 GetVelocity()
    {
#if UNITY_6000_0_OR_NEWER
        return playerBody.linearVelocity;
#else
        return playerBody.velocity;
#endif
    }

    void SetVelocity(Vector3 v)
    {
#if UNITY_6000_0_OR_NEWER
