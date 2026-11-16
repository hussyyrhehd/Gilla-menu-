// GillaModMenu.cs
// MIT License - free to use, modify, and redistribute.
//
// In-game mod / dev menu for Gilla VR (Unity + Meta XR Core SDK, standalone Quest).
// Features: Platforms, Long Arms, Fly, Noclip.
// FORCED TO RUN ON ALL BUILDS (Release/Public builds enabled).
// Custom Identity: ARCHIVES MENU
//
// CONTROLS (Quest Touch controllers)
//   Left Menu button ........ open / close the menu
//   Right thumbstick U/D .... move cursor
//   Right thumbstick L/R .... adjust value (arm length, fly speed)
//   A (menu open) ........... toggle the selected entry
//   B (menu open) ........... close the menu
//   Grip, each hand ......... hold to place a platform at that hand (Platforms on)
//   A (menu closed) ......... fly thrust toward the right controller (Fly or Noclip on)

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class GillaModMenu : MonoBehaviour
{
    [Header("Safety")]
    [Tooltip("Forced to true to bypass Developer-only checks.")]
    public bool allowInReleaseBuilds = true;

    [Header("Rig references (assign in the Inspector)")]
    public Transform head;            // OVRCameraRig/TrackingSpace/CenterEyeAnchor
    public Transform leftTracked;     // LeftHandAnchor  (real controller pose)
    public Transform rightTracked;    // RightHandAnchor (real controller pose)
    public Transform leftHand;        // your game's physical hand object (the one with the collider)
    public Transform rightHand;
    public Rigidbody playerBody;      // the player's rigidbody
    public Collider[] bodyColliders;  // disabled by Noclip
    public Collider[] handColliders;  // disabled by Noclip

    [Header("Platforms")]
    public int platformLayer = 0;     // set to a layer your hand/body colliders collide with
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

    MeshRenderer menuRenderer;
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

    // FORCED BYPASS: Always returns true so the menu runs anywhere
    bool Allowed => true;

    // ------------------------------------------------------------------ setup

    void Start()
    {
        if (!Allowed) { enabled = false; return; }

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

        if (playerBody != null)
        {
            savedGravity = playerBody.useGravity;
        }
    }

    void BuildMenuText()
    {
        var go = new GameObject("ArchivesModMenuText");
        menuText = go.AddComponent<TextMesh>();
        menuRenderer = go.GetComponent<MeshRenderer>();
        Font f = LoadFont();
        menuText.font = f;
        menuRenderer.material = f.material;
        menuText.fontSize = 64;
        menuText.characterSize = 0.01f;   // tune for readability
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
        if (OVRInput.GetDown(OVRInput.Button.Start, OVRInput.Controller.LTouch))
            SetMenu(!menuOpen);

        if (!menuOpen) return;

        Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);

        int dy = Step(stick.y, ref latchY);
        if (dy != 0) cursor = (cursor - dy + entries.Count) % entries.Count; // stick up = previous

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
        if (menuText != null) menuText.gameObject.SetActive(open);
        if (!open || head == null) return;

        Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        menuText.transform.position = head.position + fwd * menuDistance + Vector3.down * 0.1f;
        menuText.transform.rotation = Quaternion.LookRotation(fwd);
        RefreshText();
    }

    void RefreshText()
    {
        var sb = new StringBuilder("ARCHIVES MENU\n\n");
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

    void FixedUpdate()
    {
        // Handle Fly and Noclip physics movement
        if ((flyOn || noclipOn) && !menuOpen && playerBody != null && rightTracked != null)
        {
            if (OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RTouch))
            {
                playerBody.useGravity = false;
                playerBody.linearVelocity = rightTracked.forward * flySpeed;
                wasFree = true;
            }
            else if (wasFree)
            {
                playerBody.useGravity = noclipOn ? false : savedGravity;
                wasFree = false;
            }
        }
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
        
        bool gripHeld = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, ctrl);
        if (platformsOn && gripHeld)
        {
            if (!plat.activeSelf)
            {
                plat.transform.position = hand.position + Vector3.down * platformDrop;
                plat.transform.rotation = Quaternion.identity;
                plat.SetActive(true);
            }
        }
        else
        {
            if (plat.activeSelf) plat.SetActive(false);
        }
    }

    void ToggleNoclip()
    {
        noclipOn = !noclipOn;

        if (playerBody != null)
        {
            playerBody.useGravity = !noclipOn && savedGravity;
        }

        // Toggle collision array states based on Noclip mode
        SetCollidersState(bodyColliders, !noclipOn);
        SetCollidersState(handColliders, !noclipOn);
    }

    void SetCollidersState(Collider[] colliders, bool state)
    {
        if (colliders == null) return;
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null) colliders[i].enabled = state;
        }
    }
}
