using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MelonLoader;
using UnityEngine;
using UnityEngine.Video;
using Il2Cpp;
using Il2CppInterop.Runtime.Injection;

[assembly: MelonInfo(typeof(CampOfficeWindowMod.WindowModMain), "Camp Office Ultimate Edition", "4.0.0", "KarsonTR")]
[assembly: MelonGame("Hinterland", "TheLongDark")]

namespace CampOfficeWindowMod
{
    public class DynamicWindowController : MonoBehaviour
    {
        public DynamicWindowController(IntPtr ptr) : base(ptr) { }

        public int windowIndex;
        public string rootPath;
        public MeshRenderer originalRenderer;

        private Material myMaterial;
        private Weather weather;
        private TimeOfDay timeOfDay;
        private VideoPlayer videoPlayer;
        private RenderTexture videoRenderTexture;
        private string lastWeatherName = "";

        void Start()
        {
            myMaterial = GetComponent<MeshRenderer>().material;
            weather = GameManager.GetWeatherComponent();
            timeOfDay = GameManager.GetTimeOfDayComponent();

            videoPlayer = gameObject.AddComponent<VideoPlayer>();
            videoPlayer.playOnAwake = false;
            videoPlayer.isLooping = true;
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.audioOutputMode = VideoAudioOutputMode.None;

            // Siyah boşlukları engelleyen ayar
            videoPlayer.aspectRatio = VideoAspectRatio.Stretch;

            videoRenderTexture = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32);
            videoRenderTexture.wrapMode = TextureWrapMode.Clamp;
            videoRenderTexture.filterMode = FilterMode.Bilinear;
            videoRenderTexture.Create();

            videoPlayer.targetTexture = videoRenderTexture;
            myMaterial.mainTexture = videoRenderTexture;

            // Senin orijinal düzgün çalışan ölçeklerin
            myMaterial.mainTextureScale = new Vector2(0.85f, 0.85f);

            UpdateVisualsForWeather();
        }

        void Update()
        {
            if (originalRenderer != null && originalRenderer.enabled) originalRenderer.enabled = false;

            if (weather != null)
            {
                string currentWeather = weather.GetWeatherStage().ToString();
                if (currentWeather.Contains("Clear")) currentWeather = "Clear";

                if (currentWeather != lastWeatherName) UpdateVisualsForWeather();
            }

            // IŞIKLANDIRMA: UI/Default Shader'ı ile kusursuz çalışacak
            if (timeOfDay != null) myMaterial.color = CalculateTintColor(timeOfDay.GetHour());

            UpdateParallax();
        }

        private void UpdateVisualsForWeather()
        {
            lastWeatherName = weather.GetWeatherStage().ToString();
            if (lastWeatherName.Contains("Clear")) lastWeatherName = "Clear";

            string videoPath = Path.Combine(rootPath, lastWeatherName, $"Window_{windowIndex}.mp4");
            string absoluteVideoPath = Path.GetFullPath(videoPath);

            if (File.Exists(absoluteVideoPath))
            {
                videoPlayer.url = absoluteVideoPath;
                videoPlayer.Play();
                // PNG'den videoya dönüldüğünde ekranın takılı kalmasını önler
                myMaterial.mainTexture = videoRenderTexture;
            }
            else
            {
                videoPlayer.Stop();
                string fallbackPath = Path.Combine(rootPath, "Clear", $"Window_{windowIndex}.png");
                string absoluteFallbackPath = Path.GetFullPath(fallbackPath);

                if (File.Exists(absoluteFallbackPath))
                {
                    myMaterial.mainTexture = LoadStaticTexture(absoluteFallbackPath);
                }
            }
        }

        private void UpdateParallax()
        {
            var cam = GameManager.GetMainCamera();
            if (cam == null) return;
            Vector3 localPos = transform.InverseTransformPoint(cam.transform.position);
            myMaterial.mainTextureOffset = new Vector2((-localPos.x * 0.07f) + 0.075f, (-localPos.y * 0.07f) + 0.075f);
        }

        private Texture2D LoadStaticTexture(string path)
        {
            byte[] fileData = File.ReadAllBytes(path);
            var il2cppBytes = (Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>)fileData;
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            ImageConversion.LoadImage(tex, il2cppBytes);
            tex.Apply(false, true);
            return tex;
        }

        private Color CalculateTintColor(float hour)
        {
            Color dayColor = Color.white;
            Color nightColor = new Color(0.08f, 0.12f, 0.20f, 1f); // Gece rengi hafif açıldı (tam siyah olmasın diye)
            Color sunsetColor = new Color(0.8f, 0.4f, 0.2f, 1f);

            if (hour >= 7f && hour < 17f) return dayColor;
            else if (hour >= 17f && hour < 19f)
            {
                float t = (hour - 17f) / 2f;
                return (t < 0.5f) ? Color.Lerp(dayColor, sunsetColor, t * 2f) : Color.Lerp(sunsetColor, nightColor, (t - 0.5f) * 2f);
            }
            else if (hour >= 19f || hour < 5f) return nightColor;
            else
            {
                float t = (hour - 5f) / 2f;
                return (t < 0.5f) ? Color.Lerp(nightColor, sunsetColor, t * 2f) : Color.Lerp(sunsetColor, dayColor, (t - 0.5f) * 2f);
            }
        }
    }

    public class WindowModMain : MelonMod
    {
        private string textureFolderPath;

        public override void OnInitializeMelon()
        {
            ClassInjector.RegisterTypeInIl2Cpp<DynamicWindowController>();
            textureFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods", "WindowTextures");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (sceneName == "CampOffice") ReplaceWindowTextures();
        }

        private void ReplaceWindowTextures()
        {
            var allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
            List<GameObject> windows = new List<GameObject>();

            foreach (var obj in allObjects)
            {
                if (obj.name.Contains("OBJ_LakeCabinInteriorWindow")) windows.Add(obj);
            }

            var sortedWindows = windows.OrderBy(w => w.transform.position.y)
                                       .ThenBy(w => w.transform.position.x)
                                       .ThenBy(w => w.transform.position.z)
                                       .ToList();

            int index = 0;
            foreach (var window in sortedWindows)
            {
                ModifySingleWindow(window, index);
                index++;
            }
        }

        private void ModifySingleWindow(GameObject windowObject, int index)
        {
            MeshRenderer renderer = windowObject.GetComponent<MeshRenderer>();
            if (renderer == null) return;

            renderer.enabled = false;
            GameObject myCustomScreen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            myCustomScreen.name = $"WindowScreen_{index}";
            myCustomScreen.transform.SetParent(windowObject.transform, false);

            myCustomScreen.transform.position = renderer.bounds.center;
            myCustomScreen.transform.rotation = windowObject.transform.rotation;

            // KUSURSUZ KONUMLANDIRMA GERİ GELDİ (O sihirli 180 derece burada)
            myCustomScreen.transform.Rotate(0, 180, 180, Space.Self);

            float finalX = 1.4f; float finalY = 1.5f;
            if (index == 0) { finalX = -1.4f; finalY = -1.5f; }
            else if (index == 2 || index == 5 || index == 7) { finalX = -1.4f; }
            myCustomScreen.transform.localScale = new Vector3(finalX, finalY, 1f);

            // Gündüz pırıl pırıl, gece ışık karartmasını destekleyen Shader
            Shader shader = Shader.Find("UI/Default");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");

            Material mat = new Material(shader);
            myCustomScreen.GetComponent<MeshRenderer>().material = mat;

            var controller = myCustomScreen.AddComponent<DynamicWindowController>();
            controller.windowIndex = index;
            controller.rootPath = textureFolderPath;
            controller.originalRenderer = renderer;
        }
    }
}