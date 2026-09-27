using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace GK2MoveBuildings
{
	[BepInPlugin(Guid, Name, Version)]
	public class Plugin : BaseUnityPlugin
	{
		public const string Guid = "gk2.movebuildings";
		public const string Name = "GK2 Move Buildings";
		public const string Version = "0.4.4";

		internal static ManualLogSource Log;

		internal static MoveRunner Runner;

		internal static string Directory;

		internal static BepInEx.Configuration.ConfigEntry<Vector2> CursorHotspot;

		private void Awake()
		{
			Log = Logger;
			Directory = System.IO.Path.GetDirectoryName(Info.Location);
			CursorHotspot = Config.Bind("Cursor", "Hotspot", new Vector2(-1f, -1f), "Click point of move_cursor.png in pixels from its top-left corner. -1,-1 = same as the game's remove cursor.");
			GameObject runnerObject = new GameObject("GK2MoveBuildings.Runner");
			Object.DontDestroyOnLoad(runnerObject);
			runnerObject.hideFlags = HideFlags.HideAndDontSave;
			Runner = runnerObject.AddComponent<MoveRunner>();
			new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
			Log.LogInfo($"{Name} {Version} loaded");
		}
	}

	/// <summary>Host for coroutines; mode switches are deferred to the next frame so they never run inside BuildController.Update.</summary>
	public class MoveRunner : MonoBehaviour
	{
		private void Update()
		{
			MoveSession.EnsureNothingHiddenWhenIdle();
		}
	}
}
