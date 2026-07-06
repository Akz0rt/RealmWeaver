using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Networking;
using UnityEngine.UI;
using WorldGen.Notes.Rendering;
using WorldGen.Rendering.Theme;
using Debug = UnityEngine.Debug;

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
        Text actionLabel;
        Button dismissButton;
        bool downloading;

        string downloadUrl;
        string latestVersion;
        string expectedSha256; // integrity hash from the TLS-validated api.github.com response

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
            expectedSha256 = ExtractSha256(installerAsset.digest, release.body);
            ShowBanner();
        }

        /// <summary>Expected installer SHA-256, taken over the TLS-validated api.github.com channel:
        /// GitHub's own asset digest if present, else a "sha256: &lt;hex&gt;" line in the release body.</summary>
        static string ExtractSha256(string assetDigest, string releaseBody)
        {
            return MatchSha256(assetDigest) ?? MatchSha256(releaseBody);
        }

        static string MatchSha256(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(text, @"sha256[:=]\s*([0-9a-fA-F]{64})");
            return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
        }

        /// <summary>Verifies the downloaded file's SHA-256 equals the expected hash. Fails (returns
        /// false) when no hash is available — a release without a checksum is treated as untrusted.</summary>
        static bool VerifyDownload(string path, string expected, out string error)
        {
            if (string.IsNullOrEmpty(expected))
            {
                error = "у релиза нет контрольной суммы (sha256), установка отменена в целях безопасности.";
                return false;
            }

            string actual;
            try
            {
                using var sha = System.Security.Cryptography.SHA256.Create();
                using var stream = File.OpenRead(path);
                actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
            catch (Exception ex)
            {
                error = $"не удалось вычислить контрольную сумму: {ex.Message}";
                return false;
            }

            if (actual != expected)
            {
                error = "контрольная сумма файла не совпала — возможна подмена, установка отменена.";
                return false;
            }

            error = null;
            return true;
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
            var bannerImg = bannerGO.AddComponent<Image>();
            ThemeService.Tag(bannerImg, ThemeRole.Panel, 0.96f);
            var rect = bannerGO.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(260f, 64f);
            rect.anchoredPosition = new Vector2(-10f, -10f);

            var statusGO = new GameObject("Status");
            statusGO.transform.SetParent(bannerGO.transform, false);
            var statusText = statusGO.AddComponent<Text>();
            statusText.text = $"Доступна версия {latestVersion}";
            statusText.font = builtinFont;
            statusText.fontSize = 12;
            ThemeService.Tag(statusText, ThemeRole.Txt);
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
            dismissButton = btn;
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
            ThemeService.Tag(text, ThemeRole.Txt);
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
            ThemeService.Tag(img, ThemeRole.Accent);
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
            ThemeService.Tag(actionLabel, ThemeRole.AccentInk);
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
            if (downloading) return;
            StartCoroutine(DownloadAndInstall());
        }

        IEnumerator DownloadAndInstall()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"RealmWeaver-Setup-{latestVersion}.exe");

            downloading = true;
            dismissButton.interactable = false;
            actionLabel.text = "Загрузка... 0%";

            using var request = UnityWebRequest.Get(downloadUrl);
            request.downloadHandler = new DownloadHandlerFile(tempPath);
            request.SetRequestHeader("User-Agent", "RealmWeaver-UpdateChecker");
            // GitHub redirects release-asset downloads to a CDN host (objects.githubusercontent.com)
            // whose TLS chain Unity's bundled validator rejects on some Windows setups ("не удалось
            // установить SSL соединение"), even though api.github.com (the version check) validates
            // fine. Accept the cert for this download — the URL itself came from GitHub's already
            // TLS-validated API response, so we're only trusting where GitHub told us to fetch from.
            request.certificateHandler = new AcceptDownloadCertificate();
            request.disposeCertificateHandlerOnDispose = true;
            var op = request.SendWebRequest();

            while (!op.isDone)
            {
                actionLabel.text = $"Загрузка... {Mathf.RoundToInt(request.downloadProgress * 100f)}%";
                yield return null;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"UpdateChecker: download failed: {request.error}");
                ConfirmDialog.ShowInfo(builtinFont, "Не удалось скачать обновление", request.error);
                actionLabel.text = "Скачать и установить";
                dismissButton.interactable = true;
                downloading = false;
                yield break;
            }

            // The .exe arrived over a cert-bypassed CDN connection (see the download request above),
            // so verify its SHA-256 against the hash delivered over the TLS-validated api.github.com
            // channel BEFORE running it. Fail closed — never launch an installer we can't verify.
            if (!VerifyDownload(tempPath, expectedSha256, out string verifyError))
            {
                Debug.LogWarning($"UpdateChecker: integrity check failed: {verifyError}");
                try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
                ConfirmDialog.ShowInfo(builtinFont, "Обновление не прошло проверку", verifyError);
                actionLabel.text = "Скачать и установить";
                dismissButton.interactable = true;
                downloading = false;
                yield break;
            }

            try
            {
                var psi = new ProcessStartInfo(tempPath, "/VERYSILENT /SUPPRESSMSGBOXES /NOICONS")
                {
                    UseShellExecute = true
                };
                Process.Start(psi);
                Debug.Log("UpdateChecker: installer launched, quitting so it can replace this build's files.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"UpdateChecker: failed to launch installer: {ex.Message}");
                ConfirmDialog.ShowInfo(builtinFont, "Не удалось запустить установщик", ex.Message);
                actionLabel.text = "Скачать и установить";
                dismissButton.interactable = true;
                downloading = false;
                yield break;
            }

            // Unity standalone builds don't cooperate with Windows Restart Manager (no
            // RegisterApplicationRestart call, no WM_QUERYENDSESSION handling), so Inno
            // Setup's /CLOSEAPPLICATIONS can never actually close this process -- confirmed
            // by real-machine testing, where the installer hung indefinitely waiting for a
            // graceful shutdown that never came. Quitting ourselves, right after launching
            // the installer, is what actually lets the silent install proceed. The installer
            // relaunches the app afterward via its own unconditional [Run] post-install step
            // (installer/RealmWeaver.iss), not via /RESTARTAPPLICATIONS.
            Application.Quit();
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
            bool t6 = UpdateVersionCompare.IsNewer("v1.0.2", "1.0.1");

            bool ok = t1 && !t2 && !t3 && t4 && t5 && t6;
            Debug.Log(ok
                ? "Self-Test Version Compare: PASS"
                : $"Self-Test Version Compare: FAIL (t1={t1}, t2={t2}, t3={t3}, t4={t4}, t5={t5}, t6={t6})");
        }

        /// <summary>Accepts the TLS cert on the installer download. See the call site for why —
        /// GitHub's CDN redirect host fails Unity's bundled cert validation on some machines.</summary>
        class AcceptDownloadCertificate : CertificateHandler
        {
            protected override bool ValidateCertificate(byte[] certificateData) => true;
        }
    }
}
