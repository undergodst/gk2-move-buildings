using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GK2MoveBuildings
{
	/// <summary>Reflection access to the private game members the mod needs, resolved once.</summary>
	internal static class GameAccess
	{
		private static readonly FieldInfo buildControllerPointer = AccessTools.Field(typeof(BuildController), "buildPointer");

		private static readonly FieldInfo removeCurrentSelection = AccessTools.Field(typeof(RemovePointer), "currentRemovingSelection");

		private static readonly FieldInfo removeTintHex = AccessTools.Field(typeof(RemovePointer), "selectionTintColorHex");

		private static readonly FieldInfo wgoPointerCanTakeResources = AccessTools.Field(typeof(WgoBuildPointer), "canTakeResources");

		private static readonly FieldInfo wgoPointerTakeResources = AccessTools.Field(typeof(WgoBuildPointer), "takeResourcesAction");

		private static readonly FieldInfo pointerShownAsActive = AccessTools.Field(typeof(BuildPointerObject), "shownAsActive");

		private static readonly FieldInfo wgoPartsLoaded = AccessTools.Field(typeof(Wgo), "wgoPartsLoaded");

		private static readonly FieldInfo wgoDataDelayedEvents = AccessTools.Field(typeof(WgoData), "delayedEvents");

		private static readonly FieldInfo widgetData = AccessTools.Field(typeof(UIBuildingWidget), "data");

		private static readonly FieldInfo widgetResultIcon = AccessTools.Field(typeof(UIBuildingWidget), "resultIcon");

		private static readonly PropertyInfo widgetDataName = AccessTools.Property(typeof(UIBuildingWidgetData), "Name");

		private static readonly MethodInfo buildControllerUpdatePointerAtPos = AccessTools.Method(typeof(BuildController), "UpdatePointerAtPos");

		/// <summary>Same refresh the controller does after the Rotate key.</summary>
		public static void RefreshPointer(BuildController controller)
		{
			buildControllerUpdatePointerAtPos.Invoke(controller, new object[] { controller.LastCursorScreenPosition, true });
			controller.BuildLayout.UpdateBuildingMode();
			GetBuildPointer(controller).UpdateAvailability();
		}

		private static readonly FieldInfo bcCurrentBuildData = AccessTools.Field(typeof(BuildController), "currentBuildData");

		private static readonly FieldInfo bcGridStep = AccessTools.Field(typeof(BuildController), "gridStep");

		private static readonly MethodInfo bcClearFullCoverSoftHints = AccessTools.Method(typeof(BuildController), "ClearFullCoverSoftBuildAreaHints");

		private static readonly MethodInfo bcUpdateFullCoverSoftHints = AccessTools.Method(typeof(BuildController), "UpdateFullCoverSoftBuildAreaHints");

		private static readonly MethodInfo bcUpdateDockPointsHints = AccessTools.Method(typeof(BuildController), "UpdateDockPointsHints");

		/// <summary>
		/// Swaps the pointer inside an active build mode — what EnableBuildMode does minus the camera,
		/// HUD and screen-centering, so the view doesn't jump and the new pointer appears under the cursor.
		/// </summary>
		public static void SwapBuildData(BuildController controller, BuildData data, WorldZone zone, bool pointerAlreadyDisabled = false)
		{
			BuildPointer pointer = GetBuildPointer(controller);
			if (!pointerAlreadyDisabled)
			{
				pointer.Disable();
			}
			bcClearFullCoverSoftHints.Invoke(controller, null);
			bcCurrentBuildData.SetValue(controller, data);
			bcGridStep.SetValue(controller, data.Definition != null ? BuildConsts.BUILD_GRID_SIZE * data.Definition.customGridStep : BuildConsts.BUILD_GRID_SIZE);
			Vector3 buildPos = zone.GetBuildPos();
			pointer.Enable(data, zone.Id, buildPos);
			bool useExtensions = pointer.PointerObject is WgoBuildPointer wgoPointer && wgoPointer.DrawBuffAreas;
			controller.BuildLayout.DisableBuildingMode();
			controller.BuildLayout.EnableBuildingMode(buildPos, zone.Id, zone.Data.wholeZoneRect, data.Definition, useExtensions, zone.GetBuildElevationAreas());
			bcUpdateFullCoverSoftHints.Invoke(controller, null);
			bcUpdateDockPointsHints.Invoke(controller, new object[] { true });
			buildControllerUpdatePointerAtPos.Invoke(controller, new object[] { controller.LastCursorScreenPosition, true });
		}

		private static readonly MethodInfo bcTryGetWgoBuildAreaBounds = AccessTools.Method(typeof(BuildController), "TryGetWgoBuildAreaBounds");

		/// <summary>Union of the object's layer-19 build colliders — what the game measures for slot placement.</summary>
		public static bool TryGetBuildAreaBounds(Wgo wgo, out Bounds bounds)
		{
			object[] args = { wgo, default(Bounds) };
			bool found = (bool)bcTryGetWgoBuildAreaBounds.Invoke(null, args);
			bounds = (Bounds)args[1];
			return found;
		}

		private static readonly FieldInfo mcEndPos = AccessTools.Field(typeof(MovementComponent), "endPos");

		private static readonly FieldInfo mcEndDock = AccessTools.Field(typeof(MovementComponent), "endDockPointData");

		private static readonly FieldInfo mcEndWorldId = AccessTools.Field(typeof(MovementComponent), "endWorldId");

		private static readonly FieldInfo mcCurrentWorldId = AccessTools.Field(typeof(MovementComponent), "currentWorldId");

		private static readonly FieldInfo mcSpeed = AccessTools.Field(typeof(MovementComponent), "speed");

		private static readonly FieldInfo mcMovementType = AccessTools.Field(typeof(MovementComponent), "movementType");

		private static readonly FieldInfo mcCompleteEvent = AccessTools.Field(typeof(MovementComponent), "onPathCompleteEvent");

		private static readonly FieldInfo mcGraphMask = AccessTools.Field(typeof(MovementComponent), "graphMask");

		private static readonly FieldInfo mcOnFinishPath = AccessTools.Field(typeof(MovementComponent), "onFinishPath");

		private static readonly FieldInfo mcSeeker = AccessTools.Field(typeof(MovementComponent), "seeker");

		private static readonly FieldInfo mcDestinationType = AccessTools.Field(typeof(MovementComponent), "destinationType");

		public static Vector3 GetPathEnd(MovementComponent movement)
		{
			return (Vector3)mcEndPos.GetValue(movement);
		}

		/// <summary>Re-issues the walker's current path request with a new destination, keeping its callbacks.</summary>
		public static bool TryRedirectPath(MovementComponent movement, Vector3 newEnd)
		{
			try
			{
				DockPointData.Baked dock = (DockPointData.Baked)mcEndDock.GetValue(movement);
				string worldId = (string)mcCurrentWorldId.GetValue(movement);
				float speed = (float)mcSpeed.GetValue(movement);
				MovementType type = (MovementType)mcMovementType.GetValue(movement);
				string completeEvent = (string)mcCompleteEvent.GetValue(movement);
				Action onFinish = (Action)mcOnFinishPath.GetValue(movement);
				MovementComponent.DestinationType destination = (MovementComponent.DestinationType)mcDestinationType.GetValue(movement);
				MovementComponent.StartPathResult result = type == MovementType.WorldZone
					? movement.StartPath(newEnd, (Pathfinding.GraphMask)mcGraphMask.GetValue(movement), worldId, speed, completeEvent, onFinish, destination)
					: movement.StartPath(newEnd, worldId, (string)mcEndWorldId.GetValue(movement), type, speed, completeEvent, onFinish, (Pathfinding.Seeker)mcSeeker.GetValue(movement), destination);
				if (result == MovementComponent.StartPathResult.Started && dock != null)
				{
					mcEndDock.SetValue(movement, dock);
				}
				return result == MovementComponent.StartPathResult.Started || result == MovementComponent.StartPathResult.AlreadyAtDestinationPoint;
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("Could not redirect a walker: " + ex.Message);
				return false;
			}
		}

		public static BuildPointer GetBuildPointer(BuildController controller)
		{
			return (BuildPointer)buildControllerPointer.GetValue(controller);
		}

		public static IBuildRemovable GetRemoveSelection()
		{
			return (IBuildRemovable)removeCurrentSelection.GetValue(null);
		}

		/// <summary>RemovePointer keeps the hovered object in a static; clear it so a stale (already moved) object can't be clicked.</summary>
		public static void ClearRemoveSelection()
		{
			removeCurrentSelection.SetValue(null, null);
		}

		private static readonly MethodInfo removeRefreshGamepadCursorSprite = AccessTools.Method(typeof(RemovePointer), "RefreshGamepadCursorSprite");

		public static void RefreshGamepadCursorSprite(RemovePointer pointer, bool hoverRemovable)
		{
			removeRefreshGamepadCursorSprite.Invoke(pointer, new object[] { hoverRemovable });
		}

		private static readonly FieldInfo cursorConfigurations = AccessTools.Field(typeof(CursorController), "cursorConfigurations");

		public static List<CursorConfiguration> GetCursorConfigurations(CursorController controller)
		{
			return cursorConfigurations.GetValue(controller) as List<CursorConfiguration>;
		}

		private static readonly FieldInfo removeUnDestroyable = AccessTools.Field(typeof(RemovePointer), "unDestroyableRemovables");

		/// <summary>RemovePointer's grey "can't touch this" set: members are tinted grey and never hovered.</summary>
		public static HashSet<IBuildRemovable> GetUnDestroyableSet(RemovePointer pointer)
		{
			return (HashSet<IBuildRemovable>)removeUnDestroyable.GetValue(pointer);
		}

		public static void SetRemoveTintHex(RemovePointer pointer, string hex)
		{
			removeTintHex.SetValue(pointer, hex);
		}

		public static void MakePlacementFree(WgoBuildPointer pointer)
		{
			wgoPointerCanTakeResources.SetValue(pointer, (Func<bool>)(() => true));
			wgoPointerTakeResources.SetValue(pointer, null);
		}

		private static readonly MethodInfo pointerUpdateCellStatus = AccessTools.Method(typeof(BuildPointerObject), "UpdateCellStatus");

		/// <summary>Marks the ghost as not placeable (red cells), like the vanilla availability check does.</summary>
		public static void MarkUnavailable(BuildPointerObject pointer)
		{
			pointerShownAsActive.SetValue(pointer, false);
			pointerUpdateCellStatus.Invoke(pointer, null);
		}

		public static bool IsShownAsActive(BuildPointerObject pointer)
		{
			return (bool)pointerShownAsActive.GetValue(pointer);
		}

		public static bool ArePartsLoaded(Wgo wgo)
		{
			return wgo != null && (bool)wgoPartsLoaded.GetValue(wgo);
		}

		public static bool HasDelayedEvents(WgoData data)
		{
			return wgoDataDelayedEvents.GetValue(data) is ICollection collection && collection.Count > 0;
		}

		public static UIBuildingWidgetData GetWidgetData(UIBuildingWidget widget)
		{
			return (UIBuildingWidgetData)widgetData.GetValue(widget);
		}

		public static UnityEngine.UI.Image GetWidgetIcon(UIBuildingWidget widget)
		{
			return (UnityEngine.UI.Image)widgetResultIcon.GetValue(widget);
		}

		public static void SetWidgetName(UIBuildingWidgetData data, string name)
		{
			widgetDataName.SetValue(data, name);
		}

		/// <summary>ConveyorChestComponent and ConveyorChestOutComponent both expose SlotsData but share no base type for it.</summary>
		public static List<ConveyorChestSlotData> GetChestSlots(ConveyorComponent component)
		{
			if (component == null)
			{
				return null;
			}
			PropertyInfo property = AccessTools.Property(component.GetType(), "SlotsData");
			return property?.GetValue(component) as List<ConveyorChestSlotData>;
		}
	}
}
