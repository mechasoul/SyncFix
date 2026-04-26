using SyncFix.FrameRecorder;
using HarmonyLib;
using LLBML.Settings;
using Multiplayer;
using UnityEngine;

namespace SyncFix.Patches
{
    [HarmonyPatch]
    public class Debug_Patches
    {
        //convenient place to hook into in order to save frame recorders. always called at the end of an online match, and only called at the end of an online match
        //note: may be called redundantly. need to be defensive about that...
        [HarmonyPatch(typeof(Sync), nameof(Sync.StopNow))]
        [HarmonyPrefix]
        public static bool RedirectStopNow()
        {
            if (SyncFixConfig.Instance.RecordDebugInfo)
            {
                FrameRecorders.SaveAll();
            }
            return true;
        }

        //handles rollback debug info. initializes stat tracking and gui
        [HarmonyPatch(typeof(World), nameof(World.Init1))]
        [HarmonyPostfix]
        public static void Init1Postfix(World __instance)
        {
            if (GameSettings.IsOnline)
            {
                RollbackStats.Reset();
                var component = __instance.gameObject.AddComponent<DebugInfo>();
                component.parent = __instance;
            }
        }

        //simple hook for rollback debug gui toggling
        [HarmonyPatch(typeof(World), nameof(World.Update))]
        [HarmonyPrefix]
        public static bool RedirectUpdate(World __instance)
        {
            if (Input.GetKeyDown(SyncFixConfig.Instance.DebugInfoKey))
            {
                SyncFixConfig.Instance.ShowDebugInfo = !SyncFixConfig.Instance.ShowDebugInfo;
            }
            return true;
        }

        //for tracking rollbacks
        [HarmonyPatch(typeof(Sync), nameof(Sync.Rollback))]
        [HarmonyPrefix]
        public static bool RedirectRollback(int frame)
        {
            int depth = Sync.curFrame - frame;
            if (SyncFixConfig.Instance.RecordDebugInfo) FrameRecorders.Record<int>("rollbacks", Sync.curFrame, depth);
            RollbackStats.AddRollback(depth);
            return true;
        }

        //for tracking waits...
        [HarmonyPatch(typeof(P2P), nameof(P2P.Wait))]
        [HarmonyPrefix]
        public static bool RedirectWait(float wait)
        {
            if (SyncFixConfig.Instance.RecordDebugInfo) FrameRecorders.Record<float>("wait", Sync.curFrame, wait);
            RollbackStats.AddSleep(wait);
            return true;
        }

        //for tracking ping..........
        //[HarmonyPatch(typeof(Sync), nameof(Sync.DoFrame))]
        //[HarmonyPrefix]
        //public static bool RedirectDoFrame()
        //{
        //    Player remotePlayer = P2P.isHost ? Player.GetPlayer(1) : Player.GetPlayer(0);
        //    FrameRecorders.Record<float>("ping", Sync.curFrame, remotePlayer?.peer?.ping ?? -1);
        //    return true;
        //}
    }

    public class DebugInfo : MonoBehaviour
    {
        public World parent;
        public GUIStyle guiStyle;

        void Awake()
        {
            guiStyle = new GUIStyle();
            guiStyle.fontSize = 12;
            guiStyle.normal.textColor = new Color32(100, 100, 100, 255);
        }

        void OnGUI()
        {
            if (SyncFixConfig.Instance.ShowDebugInfo)
            {
                GUI.Label(new Rect(20f, 20f, 100f, 100f), RollbackStats.GetStats(), guiStyle);
            }
        }
    }
}
