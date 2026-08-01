using System;
using System.Globalization;
using UnityEngine;
using WorldGen.Workspace.Data;

namespace WorldGen.Workspace.Rendering
{
    /// <summary>
    /// Where a WorkspaceLayout is stored between sessions: PlayerPrefs, keyed by the project the tabs belong
    /// to. All the thinking about WHAT survives lives in WorkspaceOps.Serialize/TryDeserialize/Restore (pure,
    /// harness-tested); this file is only the two sides of the actual store, plus the key.
    ///
    /// NOT THE .dndproj, AND THAT IS A RULE, not a convenience. A project file is the definition of a WORLD —
    /// its cells, POIs, interiors, regions and notes — and it gets sent to other people. A tab layout is one
    /// DM's view of their own screen: how wide their navigator is, which pane the map sits in, which four
    /// editors they happened to leave open. Writing it into the project would mean opening someone else's
    /// world silently rearranged your window, and a diff of a shared project would churn on nothing. Same
    /// separation the arc's own "world definition separate from session state" principle states.
    ///
    /// LIVES IN Rendering/, NOT Data/, for one mechanical reason: PlayerPrefs is UnityEngine, and
    /// Assets/WorldGen/Workspace/Data must stay free of every UnityEngine reference or it stops compiling in
    /// Tools/notes-harness — which is what lets the whole layout layer be tested without an Editor. The
    /// consequence, stated rather than discovered: nothing in this file is covered by the offline harness.
    /// That is affordable precisely because everything with a rule in it was pushed down into WorkspaceOps;
    /// what remains here is a key, a read and a write.
    /// </summary>
    public static class WorkspacePrefs
    {
        /// <summary>Namespaced so the workspace's keys are recognisable beside the project's other PlayerPrefs
        /// entries (DisplayModeService's window mode, RecentProjectsList) rather than colliding with them.</summary>
        const string KeyPrefix = "Workspace.Layout.";

        /// <summary>The PlayerPrefs key for a project path. HASHED rather than used verbatim: a Windows path
        /// is long, is full of characters PlayerPrefs has no documented opinion about, and — on the registry
        /// backend Unity uses on Windows — is a value name with a length limit. A 32-bit FNV-1a in hex is 8
        /// characters and is stable across runs and machines, which a .NET string.GetHashCode is explicitly
        /// NOT (randomised per process since .NET Core) — that alternative would silently lose every stored
        /// layout on every launch, which is the exact failure this method must not have.
        ///
        /// NORMALISED FIRST, so that two spellings of one project are one key: GetFullPath collapses «.»,
        /// «..» and mixed separators, and ToLowerInvariant folds case because Windows paths are
        /// case-insensitive and this app is Windows-only (StandaloneFileBrowserWindows is the only real
        /// backend). GetFullPath throws on a malformed path rather than returning it, so the raw string is
        /// used as-is on that path — a key that is merely less canonical, not a crash inside a file dialog.
        ///
        /// AN EMPTY/NULL PATH IS A REAL SLOT, not an error: it is the session that has never been saved to a
        /// project, and the DM who has not saved yet still gets their tabs back on restart. Deliberately
        /// distinct from every project's slot, so "no project" cannot inherit some project's layout.
        ///
        /// A hash COLLISION would mean two projects sharing one layout. Accepted: the cost is a wrong (but
        /// well-formed, and immediately pruned) set of tabs, no data is at risk, and the alternative — storing
        /// the path beside the payload to disambiguate — buys that against a 2^-32 event.</summary>
        public static string KeyFor(string projectPath)
        {
            string normalized;
            if (string.IsNullOrEmpty(projectPath))
            {
                normalized = "";
            }
            else
            {
                try { normalized = System.IO.Path.GetFullPath(projectPath).ToLowerInvariant(); }
                catch (Exception) { normalized = projectPath.ToLowerInvariant(); }
            }

            // FNV-1a, 32-bit, over UTF-16 code units. Unchecked because the multiply is expected to wrap —
            // that IS the algorithm, and without it this would throw in a checked context.
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char c in normalized)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
                return KeyPrefix + hash.ToString("x8", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>The stored layout for a project, already parsed and already pruned — or null, meaning
        /// "nothing usable was stored; keep whatever you have". Every decision about what null means, and
        /// which tabs get dropped, belongs to WorkspaceOps.Restore; see its doc, including why `exists` may
        /// legitimately be null.</summary>
        public static WorkspaceLayout Load(string projectPath, Func<SurfaceRef, bool> exists)
        {
            string key = KeyFor(projectPath);
            if (!PlayerPrefs.HasKey(key)) return null;
            return WorkspaceOps.Restore(PlayerPrefs.GetString(key, ""), exists);
        }

        /// <summary>Writes the layout under the project's key, flushing to disk immediately.
        ///
        /// PlayerPrefs.Save() ON EVERY WRITE, deliberately. Unity flushes PlayerPrefs by itself on a clean
        /// quit, so the explicit call only matters when the app does NOT exit cleanly — a crash, a kill, or
        /// stopping Play Mode in the Editor, which is the single most common way this app is closed during
        /// development. Without it, "my tabs did not come back" would be reproducible only by whoever closed
        /// the window the wrong way. Affordable because this is an EVENT, not a frame: WorkspaceController
        /// persists on OnLayoutChanged, which fires on discrete user actions (open/close/activate/move a tab,
        /// a divider drag COMMIT — SetSplitRatioLive raises nothing, precisely so a drag is one write and not
        /// sixty) — a handful of times a minute at worst. Same argument SetShellActive makes for re-syncing
        /// unconditionally.
        ///
        /// A null layout is ignored rather than stored as an empty payload: there is no state in which
        /// erasing the DM's stored tabs is the right answer to "the caller had nothing to give".</summary>
        public static void Save(string projectPath, WorkspaceLayout layout)
        {
            if (layout == null) return;
            PlayerPrefs.SetString(KeyFor(projectPath), WorkspaceOps.Serialize(layout));
            PlayerPrefs.Save();
        }
    }
}
