using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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
        public static MusicDisplay instance;
        public static string Title = "No Media Playing", Artist = "";
        public static bool playerSpawned;

        static string helperPath;
        static Process helper;
        static StreamWriter helperIn;
        static StreamReader helperOut;
        static readonly Texture2D icon = new Texture2D(2, 2);
        static TextMeshPro musicText;
        static SpriteRenderer albumSprite;
        static Sprite spriteAsset;
        static Task fetchTask;
        static float fetchStart, nextPoll, nextFullPoll, cdPrev, cdNext, cdPlay, lastL = 1f, lastR = 1f;
        static string pendingTitle, pendingArtist;
        static int noMediaStreak;
        static bool uiReady, spriteQueued, dataQueued, controlMode, wasControl, lastB, lastA;
        static float initUntil;

        const float LightInterval = 0.05f, FullInterval = 1f, InputCooldown = 0.1f, InitDuration = 20f;
        static bool FetchIdle => fetchTask == null || fetchTask.IsCompleted;
        static bool HelperAlive => helper != null && !helper.HasExited;

        void Awake()
        {
            instance = this;
            helperPath = Path.Combine(Path.GetTempPath(), "QuickerSong.exe");
            try { foreach (var p in Process.GetProcessesByName("QuickerSong")) try { p.Kill(); p.Dispose(); } catch { } } catch { }
            try { if (File.Exists(helperPath)) File.Delete(helperPath); } catch { }
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("UtilityHud.Resources.QuickerSong.exe"))
                if (s != null) using (var f = new FileStream(helperPath, FileMode.Create, FileAccess.Write)) s.CopyTo(f);
            }
            catch { }
            StartHelper();
        }

        static void StartHelper()
        {
            if (string.IsNullOrEmpty(helperPath) || !File.Exists(helperPath)) return;
            try
            {
                helper = new Process { StartInfo = new ProcessStartInfo(helperPath) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
                helper.Start();
                helperIn = helper.StandardInput;
                helperOut = helper.StandardOutput;
            }
            catch { }
        }

        static void Send(string cmd) { if (HelperAlive) try { helperIn.WriteLine(cmd); helperIn.Flush(); } catch { } }

        public void InitializeUI()
        {
            if (uiReady || Camera.main == null) return;
            var cam = Camera.main.transform;
            var rig = GorillaTagger.Instance?.offlineVRRig;
            var font = rig?.playerText1?.font ?? Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault();
            var mat = rig != null ? Object.Instantiate(rig.playerText1.material) : null;

            albumSprite = new GameObject("AlbumArt").AddComponent<SpriteRenderer>();
            albumSprite.transform.SetParent(cam, false);
            albumSprite.transform.localPosition = new Vector3(-0.27f, 0.21f, 0.5f);
            albumSprite.transform.localScale = Vector3.one * 0.05f;

            var textObj = new GameObject("MusicText");
            musicText = textObj.AddComponent<TextMeshPro>();
            musicText.richText = true;
            if (mat != null) musicText.material = mat;
            if (font != null) musicText.font = font;
            musicText.fontSize = 1.5f;
            musicText.alignment = TextAlignmentOptions.TopLeft;
            musicText.color = Color.white;
            textObj.transform.SetParent(cam, false);
            textObj.transform.localPosition = new Vector3(0.5f, -0.02f, 0.5f);
            textObj.transform.localScale = Vector3.one * 0.08f;
            uiReady = true;
            initUntil = Time.time + InitDuration;
        }

        static async Task FetchAsync(bool withThumbnail)
        {
            if (!HelperAlive) { StartHelper(); if (!HelperAlive) return; }
            try
            {
                helperIn.WriteLine(withThumbnail ? "info" : "light");
                helperIn.Flush();
                var line = await helperOut.ReadLineAsync();
                if (string.IsNullOrEmpty(line)) return;
                var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(line);
                if (data == null) return;

                var newTitle = data.TryGetValue("Title", out var t) && !string.IsNullOrEmpty((string)t) ? (string)t : "No Media Playing";
                var newArtist = data.TryGetValue("Artist", out var a) ? (string)a : "";
                if (newTitle == "No Media Playing" && Title != "No Media Playing" && ++noMediaStreak < 3) return;
                noMediaStreak = 0;

                pendingTitle = newTitle;
                pendingArtist = newArtist;
                dataQueued = true;

                if (data.TryGetValue("ThumbnailBase64", out var thumb) && !string.IsNullOrEmpty((string)thumb))
                { icon.LoadImage(Convert.FromBase64String((string)thumb)); spriteQueued = true; }
            }
            catch { }
        }

        static void TriggerFetch() { if (FetchIdle) { fetchStart = Time.time; fetchTask = FetchAsync(false); } }

        void Update()
        {
            if (!playerSpawned) return;
            if (!uiReady) { InitializeUI(); return; }
            if (musicText == null || Camera.main == null) { uiReady = false; return; }

            if (!FetchIdle && !HelperAlive) fetchTask = null;
            if (!FetchIdle && Time.time - fetchStart > 10f) { fetchTask = null; try { if (HelperAlive) helper.Kill(); helper?.Dispose(); } catch { } helper = null; }

            if (FetchIdle && Time.time > nextPoll) { nextPoll = Time.time + LightInterval; var doFull = Time.time > nextFullPoll; if (doFull) nextFullPoll = Time.time + FullInterval; fetchStart = Time.time; fetchTask = FetchAsync(doFull); }

            if (dataQueued) { dataQueued = false; Title = pendingTitle; Artist = pendingArtist; }
            if (spriteQueued && albumSprite != null) { spriteQueued = false; if (spriteAsset != null) Object.Destroy(spriteAsset); spriteAsset = Sprite.Create(icon, new Rect(0, 0, icon.width, icon.height), new Vector2(0.5f, 0.5f)); albumSprite.sprite = spriteAsset; }

            musicText.text = Time.time < initUntil ? "Initializing..." : (controlMode ? $"{Title}\n{Artist} [Control Mode]" : $"{Title}\n{Artist}");

            var input = ControllerInputPoller.instance;
            if (input == null) return;

            var bDown = input.rightControllerSecondaryButton;
            if (bDown && !lastB) { controlMode = !controlMode; if (controlMode && !wasControl) { lastL = input.leftControllerIndexFloat; lastR = input.rightControllerIndexFloat; lastA = input.rightControllerPrimaryButton; } }
            lastB = bDown;

            if (controlMode && wasControl)
            {
                var li = input.leftControllerIndexFloat;
                if (lastL >= 0.5f && li < 0.5f && Time.time > cdPrev) { cdPrev = Time.time + InputCooldown; Send("prev"); TriggerFetch(); }
                lastL = li;
                var ri = input.rightControllerIndexFloat;
                if (lastR >= 0.5f && ri < 0.5f && Time.time > cdNext) { cdNext = Time.time + InputCooldown; Send("next"); TriggerFetch(); }
                lastR = ri;
                var aDown = input.rightControllerPrimaryButton;
                if (aDown && !lastA && Time.time > cdPlay) { cdPlay = Time.time + InputCooldown; Send("playpause"); TriggerFetch(); }
                lastA = aDown;
            }
            wasControl = controlMode;
        }

        void OnDestroy()
        {
            try { helperIn?.WriteLine("exit"); helperIn?.Flush(); } catch { }
            try { if (HelperAlive) helper.Kill(); helper?.Dispose(); } catch { }
        }
    }
}
