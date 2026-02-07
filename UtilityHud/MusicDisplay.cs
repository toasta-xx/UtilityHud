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
using Valve.Newtonsoft.Json;
using Object = UnityEngine.Object;

namespace UtilityHud
{
    public class MusicDisplay : MonoBehaviour
    {
        [DllImport("user32.dll")]
        static extern void keybd_event(uint bVk, uint bScan, uint dwFlags, uint dwExtraInfo);

        enum VK : uint { Next = 0xB0, Prev = 0xB1, PlayPause = 0xB3 }

        public static MusicDisplay instance;
        public static string Title = "No Media Playing", Artist = "";
        public static bool playerSpawned;

        static string qsPath;
        static readonly Texture2D icon = new Texture2D(2, 2);
        static TextMeshPro musicText;
        static SpriteRenderer albumSprite;
        static Sprite spriteAsset;
        static Task fetchTask;
        static bool uiReady, spriteQueued, controlMode, wasControl, lastB, lastA;
        static float nextPoll, cdPrev, cdNext, cdPlay, lastL = 1f, lastR = 1f;

        static bool FetchIdle => fetchTask == null || fetchTask.IsCompleted;

        static void Press(VK key)
        {
            keybd_event((uint)key, 0, 0, 0);
            keybd_event((uint)key, 0, 0x0002, 0);
        }

        void Awake()
        {
            instance = this;
            qsPath = Path.Combine(Path.GetTempPath(), "QuickSong.exe");
            try { foreach (var p in Process.GetProcessesByName("QuickSong")) try { p.Kill(); p.Dispose(); } catch { } } catch { }
            try { if (File.Exists(qsPath)) File.Delete(qsPath); } catch { }
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("UtilityHud.Resources.QuickSong.exe"))
                if (s != null) using (var f = new FileStream(qsPath, FileMode.Create, FileAccess.Write)) s.CopyTo(f);
            }
            catch { }
        }

        public void InitializeUI()
        {
            if (uiReady || Camera.main == null) return;
            var cam = Camera.main.transform;
            var rig = GorillaTagger.Instance?.offlineVRRig;
            var font = rig?.playerText1?.font ?? Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault();
            var mat = rig != null ? Object.Instantiate(rig.playerText1.material) : null;

            var album = new GameObject("AlbumArt");
            album.transform.SetParent(cam, false);
            album.transform.localPosition = new Vector3(-0.27f, 0.21f, 0.5f);
            album.transform.localScale = Vector3.one * 0.05f;
            albumSprite = album.AddComponent<SpriteRenderer>();

            var text = new GameObject("MusicText");
            musicText = text.AddComponent<TextMeshPro>();
            musicText.richText = true;
            if (mat != null) musicText.material = mat;
            if (font != null) musicText.font = font;
            musicText.fontSize = 1.5f;
            musicText.alignment = TextAlignmentOptions.TopLeft;
            text.transform.SetParent(cam, false);
            text.transform.localPosition = new Vector3(0.5f, -0.02f, 0.5f);
            text.transform.localScale = Vector3.one * 0.08f;
            musicText.color = Color.white;
            uiReady = true;
        }

        static async Task FetchAsync()
        {
            if (string.IsNullOrEmpty(qsPath) || !File.Exists(qsPath)) return;
            try
            {
                using (var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = qsPath, Arguments = "-all",
                        UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true
                    }
                })
                {
                    proc.Start();
                    var output = await proc.StandardOutput.ReadToEndAsync();
                    if (!proc.HasExited) proc.WaitForExit(5000);
                    if (!proc.HasExited) try { proc.Kill(); } catch { }

                    Title = "No Media Playing";
                    Artist = "";

                    var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(output);
                    if (data == null) return;

                    Title = data.TryGetValue("Title", out var t) ? (string)t : "No Media Playing";
                    Artist = data.TryGetValue("Artist", out var a) ? (string)a : "";

                    if (data.TryGetValue("ThumbnailBase64", out var thumb) && !string.IsNullOrEmpty((string)thumb))
                    {
                        icon.LoadImage(Convert.FromBase64String((string)thumb));
                        spriteQueued = true;
                    }
                }
            }
            catch { }
        }

        IEnumerator FetchAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (FetchIdle) fetchTask = FetchAsync();
        }

        void Update()
        {
            if (!playerSpawned) return;
            if (!uiReady) { InitializeUI(); return; }
            if (musicText == null || Camera.main == null) { uiReady = false; return; }

            if (Time.time > nextPoll && FetchIdle) { nextPoll = Time.time + 2f; fetchTask = FetchAsync(); }

            if (spriteQueued && albumSprite != null)
            {
                spriteQueued = false;
                if (spriteAsset != null) Object.Destroy(spriteAsset);
                spriteAsset = Sprite.Create(icon, new Rect(0, 0, icon.width, icon.height), new Vector2(0.5f, 0.5f));
                albumSprite.sprite = spriteAsset;
            }

            musicText.text = controlMode ? $"{Title}\nCreator: {Artist} [Control Mode]" : $"{Title}\nCreator: {Artist}";

            var input = ControllerInputPoller.instance;
            if (input == null) return;

            var bDown = input.rightControllerSecondaryButton;
            if (bDown && !lastB)
            {
                controlMode = !controlMode;
                if (controlMode && !wasControl)
                {
                    lastL = input.leftControllerIndexFloat;
                    lastR = input.rightControllerIndexFloat;
                    lastA = input.rightControllerPrimaryButton;
                }
            }
            lastB = bDown;

            if (controlMode && wasControl)
            {
                var li = input.leftControllerIndexFloat;
                if (lastL >= 0.5f && li < 0.5f && Time.time > cdPrev) { cdPrev = Time.time + 0.5f; Press(VK.Prev); StartCoroutine(FetchAfter(0.1f)); }
                lastL = li;

                var ri = input.rightControllerIndexFloat;
                if (lastR >= 0.5f && ri < 0.5f && Time.time > cdNext) { cdNext = Time.time + 0.5f; Press(VK.Next); StartCoroutine(FetchAfter(0.1f)); }
                lastR = ri;

                var aDown = input.rightControllerPrimaryButton;
                if (aDown && !lastA && Time.time > cdPlay) { cdPlay = Time.time + 0.5f; Press(VK.PlayPause); }
                lastA = aDown;
            }
            wasControl = controlMode;
        }

        void OnDestroy()
        {
            foreach (var p in Process.GetProcessesByName("QuickSong"))
                try { p.Kill(); p.Dispose(); } catch { }
        }
    }
}
