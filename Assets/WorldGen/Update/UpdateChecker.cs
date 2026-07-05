using System;
using System.Collections;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace WorldGen.Update
{
    /// <summary>
    /// Checks GitHub Releases for a newer version on launch and offers a one-click
    /// silent update. Self-contained — add to any GameObject, no Inspector wiring needed.
    /// </summary>
    public class UpdateChecker : MonoBehaviour
    {
        const string ApiUrl = "https://api.github.com/repos/Akz0rt/RealmWeaver/releases/latest";
        const int BannerSortingOrder = 500; // above ProjectMenuBar's 100, below ConfirmDialog's 32000

        Font builtinFont;
        Transform bannerCanvasTransform;
        GameObject bannerGO;
        Text statusText;
        Text actionLabel;

        string downloadUrl;
        string latestVersion;

        void Awake()
        {
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystemExists();
        }

        void Start()
        {
            StartCoroutine(CheckForUpdate());
        }

        static void EnsureEventSystemExists()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem (auto-created)");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        IEnumerator CheckForUpdate()
        {
            using var request = UnityWebRequest.Get(ApiUrl);
            request.SetRequestHeader("User-Agent", "RealmWeaver-UpdateChecker");
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"UpdateChecker: version check failed: {request.error}");
                yield break;
            }

            GitHubRelease release;
            try
            {
                release = JsonConvert.DeserializeObject<GitHubRelease>(request.downloadHandler.text);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"UpdateChecker: failed to parse release JSON: {ex.Message}");
                yield break;
            }

            if (release == null || string.IsNullOrEmpty(release.tag_name)) yield break;
            if (!UpdateVersionCompare.IsNewer(release.tag_name, Application.version)) yield break;

            GitHubReleaseAsset installerAsset = null;
            if (release.assets != null)
            {
                foreach (var asset in release.assets)
                {
                    if (asset.name != null && asset.name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        installerAsset = asset;
                        break;
                    }
                }
            }

            if (installerAsset == null)
            {
                Debug.LogWarning("UpdateChecker: newer release found but no .exe asset attached.");
                yield break;
            }

            latestVersion = release.tag_name.TrimStart('v', 'V');
            downloadUrl = installerAsset.browser_download_url;
            ShowBanner();
        }

        // ── Banner UI ──────────────────────────────────────────────────────────

        void ShowBanner()
        {
            var canvasGO = new GameObject("UpdateBannerCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = BannerSortingOrder;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            // Captured only after AddComponent<Canvas>() — see ProjectMenuBar.cs for why a
            // reference grabbed before that conversion silently points at a destroyed Transform.
            bannerCanvasTransform = canvasGO.transform;

            bannerGO = new GameObject("UpdateBanner");
            bannerGO.transform.SetParent(bannerCanvasTransform, false);
            bannerGO.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.1f, 0.96f);
            var rect = bannerGO.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(260f, 64f);
            rect.anchoredPosition = new Vector2(-10f, -10f);

            var statusGO = new GameObject("Status");
            statusGO.transform.SetParent(bannerGO.transform, false);
            statusText = statusGO.AddComponent<Text>();
            statusText.text = $"Доступна версия {latestVersion}";
            statusText.font = builtinFont;
            statusText.fontSize = 12;
            statusText.color = Color.white;
            statusText.alignment = TextAnchor.MiddleLeft;
            var statusRect = statusGO.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0f, 0.55f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.offsetMin = new Vector2(10f, 0f);
            statusRect.offsetMax = new Vector2(-8f, -4f);

            AddDismissButton();
            AddActionButton();
        }

        void AddDismissButton()
        {
            var go = new GameObject("Dismiss");
            go.transform.SetParent(bannerGO.transform, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(Dismiss);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(20f, 20f);
            rect.anchoredPosition = new Vector2(-4f, -4f);

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = "×";
            text.font = builtinFont;
            text.fontSize = 14;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
        }

        void AddActionButton()
        {
            var go = new GameObject("ActionButton");
            go.transform.SetParent(bannerGO.transform, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.2f, 0.45f, 0.25f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(OnDownloadClicked);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.05f, 0.1f);
            rect.anchorMax = new Vector2(0.95f, 0.5f);
            rect.sizeDelta = Vector2.zero;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            actionLabel = textGO.AddComponent<Text>();
            actionLabel.text = "Скачать и установить";
            actionLabel.font = builtinFont;
            actionLabel.fontSize = 12;
            actionLabel.color = Color.white;
            actionLabel.alignment = TextAnchor.MiddleCenter;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
        }

        void Dismiss()
        {
            if (bannerGO != null) Destroy(bannerGO.transform.parent.gameObject); // destroy the whole banner canvas
            bannerGO = null;
        }

        void OnDownloadClicked()
        {
            // Implemented in Task 8.
        }

        // ── Self-test ──────────────────────────────────────────────────────────

        [ContextMenu("Self-Test: Version Compare")]
        public void SelfTestVersionCompare()
        {
            bool t1 = UpdateVersionCompare.IsNewer("v1.2.0", "1.1.9");
            bool t2 = UpdateVersionCompare.IsNewer("v1.1.0", "1.1.0");
            bool t3 = UpdateVersionCompare.IsNewer("v1.0.9", "1.1.0");
            bool t4 = UpdateVersionCompare.IsNewer("v2.0.0", "1.9.9");
            bool t5 = !UpdateVersionCompare.IsNewer("garbage", "1.0.0");

            bool ok = t1 && !t2 && !t3 && t4 && t5;
            Debug.Log(ok
                ? "Self-Test Version Compare: PASS"
                : $"Self-Test Version Compare: FAIL (t1={t1}, t2={t2}, t3={t3}, t4={t4}, t5={t5})");
        }
    }
}
