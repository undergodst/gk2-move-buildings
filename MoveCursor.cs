using System.Collections.Generic;
using System.IO;
using LazyBearTechnology;
using UnityEngine;

namespace GK2MoveBuildings
{
	/// <summary>
	/// Optional custom hover cursor: move_cursor.png next to the DLL is registered in CursorController
	/// as an extra CursorType. Without the file the vanilla destroy cursor is kept.
	/// </summary>
	internal static class MoveCursor
	{
		public const string FileName = "move_cursor.png";

		public static readonly CursorType Type = (CursorType)100;

		public static bool SettingUpMovePointer;

		private static bool loadAttempted;

		private static bool vanillaSizeLogged;

		private static Texture2D texture;

		public static bool IsAvailable
		{
			get
			{
				EnsureRegistered();
				return texture != null && CursorController.TryGetCursorConfiguration(Type, out _);
			}
		}

		public static void EnsureRegistered()
		{
			if (!loadAttempted)
			{
				loadAttempted = true;
				texture = PngLoader.Load(FileName);
				if (texture != null)
				{
					// Hardware cursors must be uncompressed, unfiltered and exactly the file's size.
					texture.filterMode = FilterMode.Point;
					Plugin.Log.LogInfo($"Custom cursor {texture.width}x{texture.height}, hotspot {Plugin.CursorHotspot.Value}");
				}
			}
			CursorController controller = LazySingleton<CursorController>.Instance;
			if (!vanillaSizeLogged && controller != null && CursorController.TryGetCursorConfiguration(CursorType.BuildingModeDestroy, out CursorConfiguration vanilla))
			{
				vanillaSizeLogged = true;
				Plugin.Log.LogInfo($"Vanilla destroy cursor: {vanilla.sprite.width}x{vanilla.sprite.height}, hotspot {vanilla.hotSpot}");
			}
			if (texture == null || controller == null)
			{
				return;
			}
			List<CursorConfiguration> configurations = GameAccess.GetCursorConfigurations(controller);
			if (configurations == null || configurations.Exists(c => c.type == Type))
			{
				return;
			}
			Vector2 hotspot = Plugin.CursorHotspot.Value;
			if (hotspot.x < 0f || hotspot.y < 0f)
			{
				hotspot = CursorController.TryGetCursorConfiguration(CursorType.BuildingModeDestroy, out CursorConfiguration destroyCursor) ? destroyCursor.hotSpot : Vector2.zero;
			}
			configurations.Add(new CursorConfiguration
			{
				type = Type,
				sprite = texture,
				hotSpot = new Vector2(Mathf.Clamp(hotspot.x, 0, texture.width - 1), Mathf.Clamp(hotspot.y, 0, texture.height - 1))
			});
		}
	}

	internal static class PngLoader
	{
		public static Texture2D Load(string fileName)
		{
			string path = Path.Combine(Plugin.Directory, fileName);
			if (!File.Exists(path))
			{
				return null;
			}
			Texture2D result = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
			{
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.HideAndDontSave,
				name = Path.GetFileNameWithoutExtension(fileName)
			};
			if (!result.LoadImage(File.ReadAllBytes(path), markNonReadable: false))
			{
				Plugin.Log.LogWarning($"{fileName} is not a valid PNG, ignoring it");
				Object.Destroy(result);
				return null;
			}
			return result;
		}
	}
}
