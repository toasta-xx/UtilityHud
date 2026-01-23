using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using TMPro;
using System.Linq;
using GorillaLocomotion;
using Valve.Newtonsoft.Json;
using Object = UnityEngine.Object;

namespace UtilityHud
{
    public class MusicDisplayCoroutineRunner : MonoBehaviour { }

    public class MusicDisplay
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)] private static extern void keybd_event(uint bVk, uint bScan, uint dwFlags, uint dwExtraInfo);
        private enum VirtualKeyCodes : uint { NEXT_TRACK = 0xB0, PREVIOUS_TRACK = 0xB1, PLAY_PAUSE = 0xB3 }

        private static GameObject? parent, albumObj, musicObj;
        private static TextMeshPro? musicText;
        private static SpriteRenderer? albumSprite;
        private static Texture2D? albumTexture;
        private static bool musicEnabled = true, paused = true, controlMode = false, lastSecondaryState = false, lastPrimaryState = false, wasControlMode = false;
        private static float updateTime, startTime, endTime, elapsedTime, lastSyncTime, prevCooldown, nextCooldown, playCooldown, lastLeftIndex = 1f, lastRightIndex = 1f;
        private static string title = "No Media Playing", artist = "", platform = "", quickSongPath = "";
        private static Process? currentProcess;
        private static MusicDisplayCoroutineRunner? coroutineRunner;

        public static void InitializeMusicDisplay()
        {
            if (parent != null || Camera.main == null) return;
            parent = new GameObject("MusicDisplay");
            parent.transform.SetParent(Camera.main.transform, false);
            parent.transform.localPosition = new Vector3(0.5f, 0.3f, 0.5f);
            coroutineRunner = parent.AddComponent<MusicDisplayCoroutineRunner>();

            var rig = GorillaTagger.Instance?.offlineVRRig;
            var font = rig?.playerText1?.font ?? Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault();
            var material = rig != null ? Object.Instantiate(rig.playerText1.material) : null;

            albumObj = new GameObject("AlbumImage");
            albumObj.transform.SetParent(parent.transform, false);
            albumObj.transform.localPosition = new Vector3(-0.77f, -0.09f, 0f);
            albumSprite = albumObj.AddComponent<SpriteRenderer>();
            albumSprite.transform.localScale = Vector3.one * 0.05f;

            musicObj = new GameObject("MusicText");
            musicText = musicObj.AddComponent<TextMeshPro>();
            musicText.richText = true;
            if (material != null) musicText.material = material;
            if (font != null) musicText.font = font;
            musicText.fontSize = 1.5f;
            musicText.alignment = TextAlignmentOptions.TopLeft;
            musicText.transform.localScale = Vector3.one * 0.08f;
            musicText.transform.SetParent(parent.transform, false);
            musicText.transform.localPosition = new Vector3(0f, -0.32f, 0f);
            musicText.color = Color.white;

            quickSongPath = Path.Combine(Path.GetTempPath(), "QuickSong.exe");
            if (File.Exists(quickSongPath)) File.Delete(quickSongPath);
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("UtilityHud.Resources.QuickSong.exe"))
            using (var fs = new FileStream(quickSongPath, FileMode.Create, FileAccess.Write))
                stream?.CopyTo(fs);
        }

        public static void UpdateMusicDisplay()
        {
            if (!musicEnabled) { if (parent != null) parent.SetActive(false); return; }
            if (parent == null || musicText == null) { InitializeMusicDisplay(); return; }
            if (Camera.main == null) return;
            if (!parent.activeSelf) parent.SetActive(true);

            if (Time.time - updateTime > 0.5f)
            {
                updateTime = Time.time;
                _ = UpdateMusicInfoAsync();
            }

            var controlStatus = controlMode ? " [Control Mode]" : "";
            musicText.text = $"{title}\nCreator: {artist}{controlStatus}";

            if (ControllerInputPoller.instance == null) return;

            var secondaryPressed = ControllerInputPoller.instance.rightControllerSecondaryButton;
            if (secondaryPressed && !lastSecondaryState)
            {
                controlMode = !controlMode;
                if (controlMode && !wasControlMode)
                {
                    lastLeftIndex = ControllerInputPoller.instance.leftControllerIndexFloat;
                    lastRightIndex = ControllerInputPoller.instance.rightControllerIndexFloat;
                    lastPrimaryState = ControllerInputPoller.instance.rightControllerPrimaryButton;
                }
            }
            lastSecondaryState = secondaryPressed;

            if (controlMode)
            {
                if (wasControlMode)
                {
                    var leftIndex = ControllerInputPoller.instance.leftControllerIndexFloat;
                    if (lastLeftIndex >= 0.5f && leftIndex < 0.5f && Time.time > prevCooldown)
                    {
                        prevCooldown = Time.time + 0.5f;
                        PreviousTrack();
                    }
                    lastLeftIndex = leftIndex;

                    var rightIndex = ControllerInputPoller.instance.rightControllerIndexFloat;
                    if (lastRightIndex >= 0.5f && rightIndex < 0.5f && Time.time > nextCooldown)
                    {
                        nextCooldown = Time.time + 0.5f;
                        NextTrack();
                    }
                    lastRightIndex = rightIndex;

                    var primaryPressed = ControllerInputPoller.instance.rightControllerPrimaryButton;
                    if (primaryPressed && !lastPrimaryState && Time.time > playCooldown)
                    {
                        playCooldown = Time.time + 0.5f;
                        PlayPause();
                    }
                    lastPrimaryState = primaryPressed;
                }
            }
            wasControlMode = controlMode;
        }

        private static void CloseOldQuickSongProcesses()
        {
            try
            {
                if (currentProcess != null && !currentProcess.HasExited)
                {
                    try { currentProcess.Kill(); } catch { }
                    currentProcess.Dispose();
                    currentProcess = null;
                }
                foreach (var proc in Process.GetProcessesByName("QuickSong"))
                {
                    try { proc.Kill(); } catch { }
                }
            }
            catch { }
        }

        private static async Task UpdateMusicInfoAsync()
        {
            if (string.IsNullOrEmpty(quickSongPath) || !File.Exists(quickSongPath)) return;
            CloseOldQuickSongProcesses();
            try
            {
                currentProcess = new Process { StartInfo = new ProcessStartInfo { FileName = quickSongPath, Arguments = "-all", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true } };
                currentProcess.Start();
                var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(await currentProcess.StandardOutput.ReadToEndAsync());
                await Task.Run(() => currentProcess.WaitForExit());
                
                title = "No Media Playing";
                artist = "";
                startTime = 0f;
                endTime = 0f;
                elapsedTime = 0f;
                paused = true;
                
                if (data == null) { if (currentProcess != null) { currentProcess.Dispose(); currentProcess = null; } return; }
                
                title = data.TryGetValue("Title", out var t) ? (string)t : "No Media Playing";
                artist = data.TryGetValue("Artist", out var a) ? (string)a : "";
                var newStartTime = data.TryGetValue("StartTime", out var st) ? Convert.ToSingle(st) : 0f;
                var newEndTime = data.TryGetValue("EndTime", out var et) ? Convert.ToSingle(et) : 0f;
                var newElapsedTime = data.TryGetValue("ElapsedTime", out var el) ? Convert.ToSingle(el) : 0f;
                var newPaused = !data.TryGetValue("Status", out var s) || (string)s != "Playing";
                
                startTime = newStartTime;
                endTime = newEndTime;
                elapsedTime = newElapsedTime;
                paused = newPaused;
                lastSyncTime = Time.time;
                var appId = data.TryGetValue("AppId", out var id) ? (string)id : "";
                var appIdLower = appId.ToLower();
                if (appIdLower.Contains("spotify")) platform = "Spotify";
                else if (appIdLower.Contains("youtube music") || (appIdLower.Contains("youtube") && appIdLower.Contains("music"))) platform = "YouTube Music";
                else if (appIdLower.Contains("youtube")) platform = "YouTube";
                else if (appIdLower.Contains("chrome")) platform = "Chrome";
                else if (appIdLower.Contains("firefox")) platform = "Firefox";
                else if (appIdLower.Contains("edge") || appIdLower.Contains("msedge")) platform = "Edge";
                else if (appIdLower.Contains("brave")) platform = "Brave";
                else if (appIdLower.Contains("opera")) platform = "Opera";
                else if (appIdLower.Contains("safari")) platform = "Safari";
                else if (appIdLower.Contains("itunes")) platform = "iTunes";
                else if (appIdLower.Contains("vlc")) platform = "VLC";
                else if (appIdLower.Contains("windows media player")) platform = "Windows Media Player";
                else if (appIdLower.Contains("media player")) platform = "Media Player";
                else if (appIdLower.Contains("winamp")) platform = "Winamp";
                else if (appIdLower.Contains("foobar")) platform = "Foobar2000";
                else if (appIdLower.Contains("musicbee")) platform = "MusicBee";
                else if (appIdLower.Contains("web") || appIdLower.Contains("browser")) platform = "Web Player";
                else platform = appId.Length > 20 ? appId.Substring(0, 20) : (string.IsNullOrEmpty(appId) ? "Unknown" : appId);
                if (data.TryGetValue("ThumbnailBase64", out var thumb) && albumSprite != null && !string.IsNullOrEmpty((string)thumb))
                {
                    if (albumTexture != null) Object.Destroy(albumTexture);
                    albumTexture = new Texture2D(1, 1);
                    albumTexture.LoadImage(Convert.FromBase64String((string)thumb));
                    albumSprite.sprite = Sprite.Create(albumTexture, new Rect(0, 0, albumTexture.width, albumTexture.height), new Vector2(0.5f, 0.5f));
                }
                if (currentProcess != null) { currentProcess.Dispose(); currentProcess = null; }
            }
            catch { title = "No Media Playing"; artist = platform = ""; if (currentProcess != null) { try { currentProcess.Dispose(); } catch { } currentProcess = null; } }
        }


        private static void SendKey(VirtualKeyCodes vk) => keybd_event((uint)vk, 0, 0, 0);
        private static IEnumerator DelayedUpdate()
        {
            yield return new WaitForSeconds(0.1f);
            _ = UpdateMusicInfoAsync();
        }
        public static void NextTrack()
        {
            if (coroutineRunner != null) coroutineRunner.StartCoroutine(DelayedUpdate());
            elapsedTime = 0f;
            SendKey(VirtualKeyCodes.NEXT_TRACK);
        }
        public static void PreviousTrack()
        {
            if (coroutineRunner != null) coroutineRunner.StartCoroutine(DelayedUpdate());
            elapsedTime = 0f;
            SendKey(VirtualKeyCodes.PREVIOUS_TRACK);
        }
        public static void PlayPause()
        {
            paused = !paused;
            SendKey(VirtualKeyCodes.PLAY_PAUSE);
        }
    }
}
