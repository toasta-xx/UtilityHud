using BepInEx;
using HarmonyLib;
using UnityEngine;
using GorillaLocomotion;
using Photon.Pun;
using TMPro;
using Object = UnityEngine.Object;

namespace UtilityHud
{
    //If you wish to use this in your own project please ask as likely i will say yes
    [BepInPlugin(Constants.GUID, Constants.NAME, Constants.VERS)]
    public class Plugin : BaseUnityPlugin
    {
        public void Awake()
        {
            Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, Constants.GUID);
            gameObject.AddComponent<Main>();
        }
    }

    public class Main : MonoBehaviour
    {
        private struct HudState
        {
            public GameObject? Object;
            public TextMeshPro? Text;
            public bool Enabled;
            public float SessionStart;
            public int FPS;
            public float FpsUpdateTime;
        }

        private static HudState state;

        public static void EnableUtilityHUD()
        {
            state.Enabled = true;
            InitializeHUD();
            if (state.SessionStart == 0f) state.SessionStart = Time.time;
        }

        public static void DisableUtilityHUD()
        {
            state.Enabled = false;
            if (state.Object != null)
            {
                Object.Destroy(state.Object);
                state.Object = null;
                state.Text = null;
            }
        }

        private static void InitializeHUD()
        {
            if (state.Object != null || Camera.main == null) return;
            state.Object = new GameObject("UtilityHUD");
            state.Text = state.Object.AddComponent<TextMeshPro>();
            state.Text.richText = true;
            if (GorillaTagger.Instance?.offlineVRRig != null)
            {
                state.Text.material = Object.Instantiate(GorillaTagger.Instance.offlineVRRig.playerText1.material);
                state.Text.font = GorillaTagger.Instance.offlineVRRig.playerText1.font;
            }
            else
            {
                var font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault();
                if (font != null) state.Text.font = font;
            }
            state.Text.fontSize = 1.5f;
            state.Text.alignment = TextAlignmentOptions.TopLeft;
            state.Text.transform.localScale = new Vector3(0.08f, 0.08f, 0.08f);
            state.Text.transform.SetParent(Camera.main.transform, false);
            state.Text.transform.localPosition = new Vector3(0.9f, 0.01f, 0.5f);
            state.Text.color = Color.white;
        }

        public static void UpdateUtilityHUD()
        {
            if (!state.Enabled) return;
            if (state.Object == null || state.Text == null) { InitializeHUD(); return; }
            if (Camera.main == null || state.Text == null) return;
            state.Text.text = $"{GetSpeedText()}\n{GetFPSText()}\n{GetSessionTimeText()}\n{GetPlayerCountText()}\n{GetPingText()}";
            if (Camera.main != null)
            {
                state.Text.transform.localPosition = new Vector3(0.9f, 0.01f, 0.5f);
                state.Text.transform.localRotation = Quaternion.identity;
            }
        }

        private static string GetSpeedText()
        {
            try
            {
                var rb = GTPlayer.Instance?.bodyCollider?.attachedRigidbody;
                if (rb == null) return "Speed: N/A";
                return $"Speed: {rb.linearVelocity.magnitude:F1} m/s\nMax Speed: {GTPlayer.Instance.maxJumpSpeed:F1} m/s";
            }
            catch { return "Speed: N/A"; }
        }

        private static string GetFPSText()
        {
            if (Time.time - state.FpsUpdateTime > 0.5f)
            {
                state.FpsUpdateTime = Time.time;
                try { if (VRRig.LocalRig != null) state.FPS = Traverse.Create(VRRig.LocalRig).Field("fps").GetValue<int>(); }
                catch { }
            }
            return $"FPS: {state.FPS}";
        }

        private static string GetSessionTimeText()
        {
            if (state.SessionStart == 0f) return "Session: 00:00";
            float e = Time.time - state.SessionStart;
            int h = Mathf.FloorToInt(e / 3600f), m = Mathf.FloorToInt((e % 3600f) / 60f), s = Mathf.FloorToInt(e % 60f);
            return h > 0 ? $"Session: {h:D2}:{m:D2}:{s:D2}" : $"Session: {m:D2}:{s:D2}";
        }

        private static string GetPlayerCountText()
        {
            try { return PhotonNetwork.InRoom && PhotonNetwork.PlayerList != null ? $"Players: {PhotonNetwork.PlayerList.Length}" : "Players: 0 (Not in room)"; }
            catch { return "Players: N/A"; }
        }

        private static string GetPingText()
        {
            try { return PhotonNetwork.IsConnected ? $"Ping: {PhotonNetwork.GetPing()}ms" : "Ping: N/A"; }
            catch { return "Ping: N/A"; }
        }
    }
}