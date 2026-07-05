using System.Collections.Generic;

namespace WorldGen.Update
{
    // Field names match GitHub's REST API JSON exactly (snake_case) so Newtonsoft can
    // deserialize with zero attributes/converters, matching this project's plain-POCO
    // convention (see ProjectSaveData).
    public class GitHubRelease
    {
        public string tag_name;
        public List<GitHubReleaseAsset> assets;
    }

    public class GitHubReleaseAsset
    {
        public string name;
        public string browser_download_url;
    }

    /// <summary>
    /// Pure SemVer (major.minor.patch only) comparison, no MonoBehaviour dependency —
    /// exercised directly by UpdateChecker's self-test without a running scene.
    /// </summary>
    public static class UpdateVersionCompare
    {
        public static bool IsNewer(string remoteTag, string localVersion)
        {
            var remote = ParseVersion(remoteTag);
            var local = ParseVersion(localVersion);
            if (remote == null || local == null) return false;

            if (remote.Value.major != local.Value.major) return remote.Value.major > local.Value.major;
            if (remote.Value.minor != local.Value.minor) return remote.Value.minor > local.Value.minor;
            return remote.Value.patch > local.Value.patch;
        }

        public static (int major, int minor, int patch)? ParseVersion(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;

            string s = raw.TrimStart('v', 'V');
            var parts = s.Split('.');
            if (parts.Length != 3) return null;
            if (!int.TryParse(parts[0], out int major)) return null;
            if (!int.TryParse(parts[1], out int minor)) return null;
            if (!int.TryParse(parts[2], out int patch)) return null;

            return (major, minor, patch);
        }
    }
}
