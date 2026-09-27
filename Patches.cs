using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GK2MoveBuildings
{
	/// <summary>Adds the Move tile next to Remove in every tab that has one.</summary>
	[HarmonyPatch(typeof(UIBuildingWindowData), MethodType.Constructor, new[] { typeof(Wgo), typeof(PlayerData), typeof(List<BuildData>), typeof(System.Action<BuildData, List<NeedItemData>>), typeof(System.Func<BuildData, List<NeedItemData>, bool>), typeof(List<Inventory>) })]
	internal static class AddMoveTilePatch
	{
		private static void Postfix(UIBuildingWindowData __instance)
		{
			foreach (List<BuildData> tab in __instance.TabSortedBuilds.Values)
			{
				int removeIndex = tab.FindIndex(b => b != null && b.BuildingMode == BuildingDef.BuildingMode.Remove && !MoveSession.IsMoveData(b));
				if (removeIndex >= 0 && !tab.Contains(MoveSession.MoveData))
				{
					tab.Insert(removeIndex + 1, MoveSession.MoveData);
				}
			}
		}
	}

	[HarmonyPatch(typeof(UIBuildingWidgetData), MethodType.Constructor, new[] { typeof(BuildData), typeof(MultiInventory), typeof(System.Action<BuildData, List<NeedItemData>>), typeof(System.Func<BuildData, List<NeedItemData>, bool>), typeof(System.Action), typeof(System.Action), typeof(WorldZoneData) })]
	internal static class MoveTileNamePatch
	{
		private static void Postfix(UIBuildingWidgetData __instance, BuildData buildData)
		{
			if (MoveSession.IsMoveData(buildData))
			{
				GameAccess.SetWidgetName(__instance, Localization.MoveTileName);
			}
		}
	}

	[HarmonyPatch(typeof(UIBuildingWidget), nameof(UIBuildingWidget.Redraw))]
	internal static class MoveTileIconPatch
	{
		private static void Postfix(UIBuildingWidget __instance)
		{
			UIBuildingWidgetData data = GameAccess.GetWidgetData(__instance);
			if (data != null && MoveSession.IsMoveData(data.BuildData))
			{
				UnityEngine.UI.Image icon = GameAccess.GetWidgetIcon(__instance);
				if (icon != null)
				{
					icon.sprite = MoveIcon.Get(icon.sprite);
				}
			}
		}
	}

	/// <summary>The Move tile gets our selection pointer instead of RemovePointer.</summary>
	[HarmonyPatch(typeof(BuildPointer), "CreatePointerObject")]
	internal static class CreateMovePointerPatch
	{
		private static bool Prefix(BuildPointer __instance, BuildData buildData, ref BuildPointerObject __result)
		{
			if (!MoveSession.IsMoveData(buildData))
			{
				return true;
			}
			GameObject pointerObject = new GameObject("MovePointerObject");
			pointerObject.transform.SetParent(__instance.gameObject.transform);
			pointerObject.transform.localPosition = Vector3.zero;
			__result = pointerObject.AddComponent<MovePointer>();
			return false;
		}
	}

	/// <summary>Placement click: relocate the original instead of building a new object.</summary>
	[HarmonyPatch]
	internal static class PlacementCommitPatch
	{
		private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(WgoBuildPointer), nameof(WgoBuildPointer.TryDoBuildAction));
			yield return AccessTools.Method(typeof(ConveyorBuildPointer), nameof(ConveyorBuildPointer.TryDoBuildAction));
		}

		private static bool Prefix(WgoBuildPointer __instance, ref bool __result)
		{
			if (MoveSession.IsCommittingPointer(__instance))
			{
				// A second click while the move is being applied must not reach the vanilla build (it would place a free copy).
				__result = false;
				return false;
			}
			if (!MoveSession.IsPlacementPointer(__instance))
			{
				return true;
			}
			MoveSession.RequestCommit(__instance);
			__result = false;
			return false;
		}
	}

	/// <summary>Slot extensions are moved by a fixed offset, so the parent may not rotate while carrying them.</summary>
	[HarmonyPatch(typeof(WgoBuildPointer), nameof(WgoBuildPointer.HasRotation))]
	internal static class LockRotationPatch
	{
		private static void Postfix(WgoBuildPointer __instance, ref bool __result)
		{
			if (__result && MoveSession.LockRotation && MoveSession.IsPlacementPointer(__instance))
			{
				__result = false;
			}
		}
	}

	/// <summary>The controller calls BuildPointer.Rotate on the Rotate key; our own alignment calls it with Transitioning off too, so it is marked separately.</summary>
	[HarmonyPatch(typeof(BuildController), "UpdateBuildModeInput")]
	internal static class DetectPlayerRotatePatch
	{
		private static void Prefix()
		{
			if (LazyBearTechnology.LazyInput.GetKeyDown(LazyBearTechnology.GameKey.Rotate))
			{
				MoveSession.OnPlayerRotate();
			}
		}
	}

	[HarmonyPatch(typeof(BuildController), nameof(BuildController.DisableBuildMode))]
	internal static class BuildModeDisabledPatch
	{
		private static bool Prefix()
		{
			return MoveSession.OnBeforeBuildModeDisabled();
		}
	}

	[HarmonyPatch(typeof(BuildManager), nameof(BuildManager.Disable))]
	internal static class BuildManagerDisablePatch
	{
		private static bool Prefix()
		{
			return MoveSession.OnBuildManagerDisable();
		}
	}
}

namespace GK2MoveBuildings
{
	[HarmonyLib.HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.Save))]
	internal static class UnhideBeforeSavePatch
	{
		private static void Prefix()
		{
			MoveSession.RestoreBeforeSave();
		}
	}

	/// <summary>RemovePointer hard-codes the destroy cursor; swap it for ours while the Move pointer is active.</summary>
	[HarmonyLib.HarmonyPatch(typeof(RemovePointer), "ApplyCursorHoverState")]
	internal static class MoveCursorHoverPatch
	{
		private static bool Prefix(RemovePointer __instance, int removableFoundState)
		{
			if (!(__instance is MovePointer) || !MoveCursor.IsAvailable)
			{
				return true;
			}
			if (!LazyBearTechnology.LazyInput.IsGamepadActive)
			{
				CursorController.RemoveCursorState((ICursorChanger)__instance);
				if (removableFoundState == 2)
				{
					CursorController.AddCursorState(MoveCursor.Type, (ICursorChanger)__instance);
				}
			}
			else
			{
				CursorController.RemoveCursorState((ICursorChanger)__instance);
			}
			GameAccess.RefreshGamepadCursorSprite(__instance, removableFoundState == 2);
			return false;
		}
	}

	/// <summary>Gamepad cursor sprite: RemovePointer asks for the destroy cursor; answer with ours during Move setup.</summary>
	[HarmonyLib.HarmonyPatch(typeof(RemovePointer), "TryCreateCursorSprite")]
	internal static class MoveGamepadCursorPatch
	{
		private static void Prefix(ref CursorType type)
		{
			if (MoveCursor.SettingUpMovePointer && type == CursorType.BuildingModeDestroy && MoveCursor.IsAvailable)
			{
				type = MoveCursor.Type;
			}
		}
	}
}

namespace GK2MoveBuildings
{
	/// <summary>Removing a garden plot drops the gardener's orders for it; a moved plot keeps its id, so keep the orders.</summary>
	[HarmonyLib.HarmonyPatch(typeof(WorldZoneData), nameof(WorldZoneData.RemoveGardenOrdersByTarget))]
	internal static class KeepGardenOrdersPatch
	{
		private static bool Prefix(SGuid targetUniqueId)
		{
			return !MoveExecutor.IsMoving(targetUniqueId);
		}
	}

	/// <summary>Keep a moved building's script running (and its in-memory state) instead of destroying and recreating it.</summary>
	[HarmonyLib.HarmonyPatch(typeof(WgoDataScriptsManager), nameof(WgoDataScriptsManager.DestroyScript))]
	internal static class KeepScriptPatch
	{
		private static bool Prefix(WgoData wgoData)
		{
			return !MoveExecutor.IsMoving(wgoData);
		}
	}
}

namespace GK2MoveBuildings
{
	[HarmonyLib.HarmonyPatch(typeof(WgoBuildPointer), nameof(WgoBuildPointer.UpdateSelectionCellsState))]
	internal static class SlotAvailabilityPatch
	{
		private static void Postfix(WgoBuildPointer __instance)
		{
			if (GameAccess.IsShownAsActive(__instance) && MoveSession.SlotsBlocked(__instance))
			{
				GameAccess.MarkUnavailable(__instance);
			}
		}
	}
}

namespace GK2MoveBuildings
{
	/// <summary>In Move mode, buildings the mod can't relocate are shown grey and can't be hovered, instead of clicking silently.</summary>
	[HarmonyLib.HarmonyPatch(typeof(RemovePointer), nameof(RemovePointer.UpdateUnDestroyableRemovables))]
	internal static class GreyOutUnmovablePatch
	{
		private static void Postfix(RemovePointer __instance)
		{
			if (!(__instance is MovePointer))
			{
				return;
			}
			WorldZone zone = LazyBearTechnology.LazySingleton<BuildManager>.Instance.WorldZone;
			HashSet<IBuildRemovable> grey = GameAccess.GetUnDestroyableSet(__instance);
			if (zone == null || grey == null)
			{
				return;
			}
			foreach (Wgo wgo in zone.Wgos)
			{
				if (wgo != null && wgo.Data != null && wgo.IsBuildRemovable() && !MoveRules.TryGetPlaceDef(wgo.Data, out _))
				{
					grey.Add(wgo);
					MoveRules.LogBuildDefsFor(wgo.Data.id);
				}
			}
		}
	}
}
