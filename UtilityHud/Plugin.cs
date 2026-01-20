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
        private static GameObject? hudObject;
        private static TextMeshPro? hudText;
        private static bool hudEnabled;
        private static float sessionStartTime;
        private static int currentFPS;
        private static float fpsUpdateTime;

        private void Start()
        {
            GorillaTagger.OnPlayerSpawned(delegate { EnableUtilityHUD(); });
            StartCoroutine(InitWhenReady());
        }

        private System.Collections.IEnumerator InitWhenReady()
        {
            yield return new WaitForSeconds(1f);
            while (Camera.main == null || GTPlayer.Instance == null) yield return new WaitForSeconds(0.5f);
            EnableUtilityHUD();
        }

        private void Update() => UpdateUtilityHUD();

        public static void EnableUtilityHUD()
        {
            hudEnabled = true;
            if (sessionStartTime == 0f) sessionStartTime = Time.time;
            InitializeHUD();
        }

        public static void DisableUtilityHUD()
        {
            hudEnabled = false;
            if (hudObject != null) { Destroy(hudObject); hudObject = null; hudText = null; }
        }

        private static void InitializeHUD()
        {
            if (hudObject != null || Camera.main == null) return;
            hudObject = new GameObject("UtilityHUD");
            hudText = hudObject.AddComponent<TextMeshPro>();
            hudText.richText = true;
            if (GorillaTagger.Instance?.offlineVRRig != null)
            {
                hudText.material = Instantiate(GorillaTagger.Instance.offlineVRRig.playerText1.material);
                hudText.font = GorillaTagger.Instance.offlineVRRig.playerText1.font;
            }
            else hudText.font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault();
            hudText.fontSize = 1.5f;
            hudText.alignment = TextAlignmentOptions.TopLeft;
            hudText.transform.localScale = new Vector3(0.08f, 0.08f, 0.08f);
            hudText.transform.SetParent(Camera.main.transform, false);
            hudText.transform.localPosition = new Vector3(0.9f, 0.03f, 0.5f);
            hudText.color = Color.white;
        }

        public static void UpdateUtilityHUD()
        {
            if (!hudEnabled) return;
            if (hudObject == null || hudText == null) { InitializeHUD(); return; }
            if (Camera.main == null) return;
            try
            {
                if (Time.time - fpsUpdateTime > 0.5f)
                {
                    fpsUpdateTime = Time.time;
                    var localRig = VRRig.LocalRig;
                    if (localRig != null) currentFPS = Traverse.Create(localRig).Field("fps").GetValue<int>();
                }
                var rb = GTPlayer.Instance?.bodyCollider?.attachedRigidbody;
                var speed = rb != null ? $"Speed: {rb.linearVelocity.magnitude:F1} m/s\nMax Speed: {GTPlayer.Instance.maxJumpSpeed:F1} m/s" : "Speed: N/A";
                var fps = $"FPS: {currentFPS}";
                var elapsed = sessionStartTime == 0f ? 0f : Time.time - sessionStartTime;
                var hours = Mathf.FloorToInt(elapsed / 3600f);
                var minutes = Mathf.FloorToInt((elapsed % 3600f) / 60f);
                var seconds = Mathf.FloorToInt(elapsed % 60f);
                var session = hours > 0 ? $"Session: {hours:D2}:{minutes:D2}:{seconds:D2}" : $"Session: {minutes:D2}:{seconds:D2}";
                var players = PhotonNetwork.InRoom && PhotonNetwork.PlayerList != null ? $"Players: {PhotonNetwork.PlayerList.Length}" : "Players: 0 (Not in room)";
                var ping = PhotonNetwork.IsConnected ? $"Ping: {PhotonNetwork.GetPing()}ms" : "Ping: N/A";
                hudText.text = $"{speed}\n{fps}\n{session}\n{players}\n{ping}";
                hudText.transform.localPosition = new Vector3(0.9f, 0.03f, 0.5f);
                hudText.transform.localRotation = Quaternion.identity;
            }
            catch { }
        }
    }
}