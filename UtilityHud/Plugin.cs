using BepInEx;
using HarmonyLib;
using UnityEngine;
using GorillaLocomotion;
using Photon.Pun;
using TMPro;
using System.Collections;
using System.Linq;

namespace UtilityHud
{
    //creds to ii for quicksong
    //creds to ii/graze for keycode inputs
    [BepInPlugin(Constants.GUID, Constants.NAME, Constants.VERS)]
    public class Plugin : BaseUnityPlugin
    {
        private static Harmony harmonyInstance;
        public void Awake()
        {
            harmonyInstance = Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, Constants.GUID);
            gameObject.AddComponent<Main>();
        }
    }

    public class Main : MonoBehaviour
    {
        private static GameObject? hudObj;
        private static TextMeshPro? hudText;
        private static bool hudEnabled;
        private static float sessionStart;
        private static int fps;
        private static float fpsTime;

        private void Start()
        {
            GorillaTagger.OnPlayerSpawned(delegate { EnableUtilityHUD(); });
        }

        private void Update()
        {
            UpdateUtilityHUD();
            MusicDisplay.UpdateMusicDisplay();
        }

        public static void EnableUtilityHUD()
        {
            hudEnabled = true;
            if (sessionStart == 0f) sessionStart = Time.time;
            InitializeHUD();
        }

        private static void InitializeHUD()
        {
            if (hudObj != null || Camera.main == null) return;
            hudObj = new GameObject("UtilityHUD");
            hudText = hudObj.AddComponent<TextMeshPro>();
            hudText.richText = true;
            var rig = GorillaTagger.Instance?.offlineVRRig;
            if (rig != null)
            {
                hudText.material = Object.Instantiate(rig.playerText1.material);
                hudText.font = rig.playerText1.font;
            }
            else hudText.font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault();
            hudText.fontSize = 1.5f;
            hudText.alignment = TextAlignmentOptions.TopLeft;
            hudText.transform.localScale = Vector3.one * 0.08f;
            hudText.transform.SetParent(Camera.main.transform, false);
            hudText.transform.localPosition = new Vector3(0.9f, 0.01f, 0.5f);
            hudText.color = Color.white;
            MusicDisplay.InitializeMusicDisplay();
        }

        public static void UpdateUtilityHUD()
        {
            if (!hudEnabled || hudObj == null || hudText == null || Camera.main == null) { if (hudObj == null) InitializeHUD(); return; }
            try
            {
                if (Time.time - fpsTime > 0.5f)
                {
                    fpsTime = Time.time;
                    var rig = VRRig.LocalRig;
                    if (rig != null) fps = Traverse.Create(rig).Field("fps").GetValue<int>();
                }
                var rb = GTPlayer.Instance?.bodyCollider?.attachedRigidbody;
                var speed = rb != null ? $"Speed: {rb.linearVelocity.magnitude:F1} m/s\nMax Speed: {GTPlayer.Instance.maxJumpSpeed:F1} m/s" : "Speed: N/A";
                var elapsed = sessionStart == 0f ? 0f : Time.time - sessionStart;
                var h = Mathf.FloorToInt(elapsed / 3600f);
                var m = Mathf.FloorToInt((elapsed % 3600f) / 60f);
                var s = Mathf.FloorToInt(elapsed % 60f);
                var session = h > 0 ? $"Session: {h:D2}:{m:D2}:{s:D2}" : $"Session: {m:D2}:{s:D2}";
                var players = PhotonNetwork.InRoom && PhotonNetwork.PlayerList != null ? $"Players: {PhotonNetwork.PlayerList.Length}" : "Players: 0 (Not in room)";
                var ping = PhotonNetwork.IsConnected ? $"Ping: {PhotonNetwork.GetPing()}ms" : "Ping: N/A";
                hudText.text = $"{speed}\nFPS: {fps}\n{session}\n{players}\n{ping}";
            }
            catch { }
        }
    }
}