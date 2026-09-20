using System;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

// Runtime-only fixture: no scene or asset saves. Automated recordings are explicitly marked.
public static class PortalAppearanceRegression
{
    static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    static async Task Advance(float seconds)
    {
        float until = Time.time + seconds, deadline = Time.realtimeSinceStartup + seconds + 8;
        while (Time.time < until && Time.realtimeSinceStartup < deadline) await Task.Delay(50);
        Assert(Time.time >= until, "Simulation did not advance");
    }
    public static async Task<object> Main()
    {
        Assert(EditorApplication.isPlaying, "Requires Play mode");
        bool background = Application.runInBackground, paused = EditorApplication.isPaused;
        bool godMode = PlayerHealth.DebugGodMode;
        try
        {
            Application.runInBackground = true;
            EditorApplication.isPaused = false;
            PlayerHealth.DebugGodMode = true;
            var flow = GameFlowManager.Instance;
            flow.DebugBeginTest();
            PlaytestRecorder.Record("automated_validation", "qa", Vector3.zero, "PortalAppearanceRegression");
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            player.TeleportTo(new Vector3(20, 1, 1), Quaternion.identity);
            var manager = TargetManager.Instance;
            var portal = UnityEngine.Object.FindFirstObjectByType<ProceduralExitPortal>(FindObjectsInactive.Include);
            var notice = (TextMeshProUGUI)typeof(TargetManager).GetField("portalNoticeText",
                BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager);
            Assert(portal != null && notice != null, "Missing exit or notice reference");
            void Locked()
            {
                Assert(!portal.gameObject.activeSelf && !portal.IsUnlocked && !manager.PortalOpened, "Exit visible before goal");
                Assert(!portal.GetComponent<BoxCollider>().enabled, "Locked exit trigger enabled");
                Assert(!notice.gameObject.activeSelf, "Old notice visible after reset");
            }
            void Unlock()
            {
                while (manager.DestroyedCount < manager.TargetGoal - 1) manager.OnTargetDestroyed();
                Locked();
                manager.OnTargetDestroyed();
                Assert(portal.gameObject.activeInHierarchy && portal.IsUnlocked && manager.PortalOpened, "Goal did not reveal exit");
                Assert(portal.GetComponent<BoxCollider>().enabled, "Unlocked trigger disabled");
                Assert(notice.gameObject.activeInHierarchy && notice.text == "중앙에 포탈이 생성되었습니다", "Missing Korean notice");
                notice.ForceMeshUpdate();
                Assert(notice.font.HasCharacters(notice.text, out uint[] missing, true, true), "Missing Korean glyphs");
                Assert(flow.IsGameRunning, "Unlocking away from exit must not clear round");
            }
            manager.ResetGame(); Locked(); Unlock();
            Vector3 start = flow.PlayerStartPoint.position;
            Assert(new Vector2(portal.transform.position.x-start.x, portal.transform.position.z-start.z).magnitude < .01f, "Exit moved from fixed start");
            await Advance(2);
            manager.ResetGame(); Locked(); Unlock();
            // Past the first round's five-second timeout, but before this round's timeout.
            await Advance(3.3f);
            Assert(notice.gameObject.activeInHierarchy, "Old round coroutine hid new notice");
            manager.OnTargetDestroyed(); // Extra targets must not restart the announcement.
            await Advance(2);
            Assert(!notice.gameObject.activeSelf && portal.gameObject.activeInHierarchy, "Notice did not expire independently of exit");
            manager.ResetGame(); Locked();
            Assert(UnityEngine.Object.FindObjectsByType<ProceduralExitPortal>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 1, "Duplicate procedural exits");
            return new { passed = true, hiddenBeforeGoal = true, visibleAtGoal = true, koreanGlyphs = true,
                fixedStart = true, resetCancelsOldNotice = true, extraTargetsDoNotRepeatNotice = true, noticeExpires = true, reusedExit = true };
        }
        finally
        {
            Application.runInBackground = background;
            PlayerHealth.DebugGodMode = godMode;
            EditorApplication.isPaused = paused;
        }
    }
}
