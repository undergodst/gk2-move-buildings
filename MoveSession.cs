using System;
using System.Collections;
using System.Collections.Generic;
using LazyBearTechnology;
using UnityEngine;

namespace GK2MoveBuildings
{
	internal enum MoveState
	{
		None,
		Selecting,
		Placing,
		Committing
	}

	/// <summary>
	/// Two-step flow inside the vanilla build mode:
	/// 1) Selecting: a RemovePointer-like cursor picks a building.
	/// 2) Placing: the vanilla WgoBuildPointer shows the building's ghost, free of charge; the click relocates the original.
	/// Afterwards it returns to step 1 so several buildings can be moved in a row.
	/// </summary>
	internal static class MoveSession
	{
		/// <summary>Dedicated instance recognised by reference; it is a Remove-mode BuildData so every vanilla code path treats it like the Remove tile.</summary>
		public static readonly BuildData MoveData = BuildData.GetDataForRemove();

		public static MoveState State { get; private set; }

		/// <summary>True while the mod itself is switching build modes, so our hooks don't treat it as the player's cancel.</summary>
		public static bool Transitioning { get; private set; }

		public static Wgo Original { get; private set; }

		public static bool LockRotation { get; private set; }

		/// <summary>Data, not views: a view can be destroyed mid-placement (scene change), the data and its saved IsHidden flag stay.</summary>
		private static readonly List<WgoData> hiddenData = new List<WgoData>();

		private static WorldZone zone;

		private static WgoBuildPointer placementPointer;

		private static int skipManagerDisableFrame = -1;

		private static bool playerRotated;

		private static List<MoveExecutor.SlotPlan> slotPlans = new List<MoveExecutor.SlotPlan>();

		/// <summary>WgoBuildPointer.UpdateSelectionCellsState postfix: also require the extensions' slots to be free.</summary>
		public static bool SlotsBlocked(WgoBuildPointer pointer)
		{
			return IsPlacementPointer(pointer) && slotPlans.Count > 0 && !MoveExecutor.AreSlotsFree(pointer.Target, slotPlans, Original);
		}

		/// <summary>BuildPointer.Rotate prefix, only while the player is placing.</summary>
		public static void OnPlayerRotate()
		{
			if (State == MoveState.Placing && !Transitioning)
			{
				playerRotated = true;
			}
		}

		public static bool IsMoveData(BuildData data)
		{
			return data != null && ReferenceEquals(data, MoveData);
		}

		public static bool IsCommittingPointer(object pointer)
		{
			return State == MoveState.Committing && pointer != null && ReferenceEquals(pointer, placementPointer);
		}

		public static bool IsPlacementPointer(object pointer)
		{
			return State == MoveState.Placing && pointer != null && ReferenceEquals(pointer, placementPointer);
		}

		public static void OnSelectionStarted()
		{
			State = MoveState.Selecting;
			Original = null;
		}

		public static void OnSelectionPointerDisabled()
		{
			if (!Transitioning && State == MoveState.Selecting)
			{
				State = MoveState.None;
			}
		}

		public static void RequestPlacement(Wgo wgo)
		{
			if (State != MoveState.Selecting)
			{
				return;
			}
			if (!MoveRules.CanMove(wgo, out string reason))
			{
				Plugin.Log.LogInfo("Cannot move: " + reason);
				LazyAudio.PlayAndForget("gui_click");
				return;
			}
			State = MoveState.Committing;
			Plugin.Runner.StartCoroutine(BeginPlacementNextFrame(wgo));
		}

		private static IEnumerator BeginPlacementNextFrame(Wgo wgo)
		{
			yield return null;
			BuildController controller = BuildController.Instance;
			zone = LazySingleton<BuildManager>.Instance.WorldZone;
			if (controller == null || !controller.IsBuildModeActive || State != MoveState.Committing)
			{
				// Player left build mode before the switch happened.
				State = MoveState.None;
				yield break;
			}
			string reason = null;
			if (zone == null || !MoveRules.CanMove(wgo, out reason))
			{
				Plugin.Log.LogWarning("Placement aborted: " + (reason ?? "world zone missing"));
				State = MoveState.Selecting;
				yield break;
			}
			MoveRules.TryGetPlaceDef(wgo.Data, out BuildingDef def);
			List<WgoData> slotExtensions = MoveRules.GetSlotExtensions(wgo.Data);
			Original = wgo;
			// Slot extensions are re-seated in their slots after the move, so rotating the parent is fine.
			LockRotation = false;
			try
			{
				Transitioning = true;
				// Must be read while the original is still visible (its slot colliders are live).
				slotPlans = MoveExecutor.RecordSlotPlans(wgo);
				HideWithWorkers(wgo);
				foreach (WgoData extension in slotExtensions)
				{
					HideWithWorkers(GameScene.GetWgoViewGlobal(extension.UniqueId));
				}
				Physics.SyncTransforms();
				GameAccess.SwapBuildData(controller, BuildData.GetDataForBuild(def), zone);
				placementPointer = GameAccess.GetBuildPointer(controller).PointerObject as WgoBuildPointer;
				if (placementPointer == null)
				{
					throw new InvalidOperationException("vanilla build pointer was not a WgoBuildPointer");
				}
				GameAccess.MakePlacementFree(placementPointer);
				if (slotPlans.Count > 0)
				{
					Plugin.Log.LogInfo($"Slot check: {slotPlans.Count} extension(s), ghost exposes {MoveExecutor.CountGhostSlots(placementPointer.Target, slotPlans)} matching slot(s)");
				}
				playerRotated = false;
				State = MoveState.Placing;
				LazyAudio.PlayAndForget("oh_wood_grab");
				Plugin.Log.LogInfo($"Moving [{wgo.Data.id}] {wgo.Data.UniqueId}, slot extensions: {slotExtensions.Count}");
			}
			catch (Exception ex)
			{
				Plugin.Log.LogError("Failed to start placement: " + ex);
				ShowHidden();
				ReturnToSelection(controller);
				yield break;
			}
			finally
			{
				Transitioning = false;
			}
			Plugin.Runner.StartCoroutine(MatchOriginalRotation(controller, wgo.Data.MainWgoPartData));
		}

		/// <summary>The ghost spawns in the default rotation; spin it until it matches the original.</summary>
		private static IEnumerator MatchOriginalRotation(BuildController controller, WgoPartData original)
		{
			if (original == null || original.rotationIndex == -1)
			{
				yield break;
			}
			for (int frame = 0; frame < 120; frame++)
			{
				Wgo ghost = placementPointer?.Target;
				if (State != MoveState.Placing || ghost == null)
				{
					yield break;
				}
				if (ghost.MainWgoPart != null)
				{
					break;
				}
				yield return null;
			}
			Wgo target = placementPointer?.Target;
			if (State != MoveState.Placing || target == null || target.MainWgoPart == null || !target.CanBeRotated())
			{
				yield break;
			}
			if (playerRotated)
			{
				yield break;
			}
			BuildPointer buildPointer = GameAccess.GetBuildPointer(controller);
			// Four steps cover a full turn; the preview's variation id may differ from the real object's, so compare rotation only.
			for (int i = 0; i < 4 && target.MainWgoPart.WgoPartData.rotationIndex != original.rotationIndex; i++)
			{
				buildPointer.Rotate();
			}
			GameAccess.RefreshPointer(controller);
		}

		/// <summary>Called from the TryDoBuildAction prefix instead of the vanilla build.</summary>
		public static void RequestCommit(WgoBuildPointer pointer)
		{
			if (!GameAccess.IsShownAsActive(pointer))
			{
				return;
			}
			Wgo ghost = pointer.Target;
			MovePlan plan = new MovePlan
			{
				Wgo = Original,
				NewPosition = ghost.Data.Position,
				VariationId = Original.Data.MainWgoPartData.variationId,
				RotationIndex = Original.Data.MainWgoPartData.rotationIndex
			};
			if (ghost.CanBeRotated() && ghost.MainWgoPart != null && ghost.MainWgoPart.WgoPartData.rotationIndex != -1)
			{
				plan.VariationId = ghost.MainWgoPart.WgoPartData.variationId;
				plan.RotationIndex = ghost.MainWgoPart.WgoPartData.rotationIndex;
			}
			ApplyBuildAreaRotationRequirement(pointer, plan);
			WgoPartData originalPart = Original.Data.MainWgoPartData;
			Plugin.Log.LogInfo($"Commit [{Original.Data.id}]: original {originalPart.variationId}/{originalPart.rotationIndex}, ghost {ghost.MainWgoPart?.WgoPartData?.variationId}/{ghost.MainWgoPart?.WgoPartData?.rotationIndex}, applying {plan.VariationId}/{plan.RotationIndex}");
			BuildArea slotArea = BuildController.Instance.CurrentFullCoverSoftHintArea;
			plan.NewSlotParent = slotArea != null ? slotArea.GetComponentInParent<Wgo>() : null;
			State = MoveState.Committing;
			Plugin.Runner.StartCoroutine(CommitNextFrame(plan));
		}

		/// <summary>Mirror of WgoBuildPointer.TrySetCustomRotation: some build areas force a rotation.</summary>
		private static void ApplyBuildAreaRotationRequirement(WgoBuildPointer pointer, MovePlan plan)
		{
			Bounds bounds = pointer.GetWorldRoundedBounds();
			Vector3 halfExtents = bounds.extents / 4f;
			halfExtents.y = 0.5f;
			Collider[] hits = new Collider[10];
			int count = Physics.OverlapBoxNonAlloc(bounds.center, halfExtents, hits, Quaternion.identity, 524288);
			for (int i = 0; i < count; i++)
			{
				if (hits[i] == null || !hits[i].TryGetComponent<BuildArea>(out BuildArea area) || !area.HasRotationRequirement)
				{
					continue;
				}
				// Only areas of other objects count: the ghost (and the hidden original) carry their own
				// build areas, and honouring those would snap every move back to one fixed rotation.
				Wgo owner = hits[i].GetComponentInParent<Wgo>();
				if (owner != null && (owner == pointer.Target || owner == plan.Wgo || (owner.Data != null && owner.Data.isTempObject)))
				{
					continue;
				}
				plan.RotationIndex = area.RotationRequirement;
				Plugin.Log.LogInfo($"Build area [{area.Id}] of [{owner?.Data?.id}] forces rotation {area.RotationRequirement}");
			}
		}

		private static IEnumerator CommitNextFrame(MovePlan plan)
		{
			yield return null;
			BuildController controller = BuildController.Instance;
			if (controller == null || !controller.IsBuildModeActive || State != MoveState.Committing)
			{
				ShowHidden();
				State = MoveState.None;
				yield break;
			}
			bool moved = false;
			try
			{
				Transitioning = true;
				// Drop the ghost first so its colliders don't interfere, keep build mode (and the camera) as is.
				GameAccess.GetBuildPointer(controller).Disable();
				ShowHidden();
				moved = MoveExecutor.Move(plan);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogError("Move failed: " + ex);
				ShowHidden();
			}
			finally
			{
				Transitioning = false;
			}
			if (moved)
			{
				LazyAudio.PlayAndForget("build_place");
			}
			ReturnToSelection(controller, pointerAlreadyDisabled: true);
		}

		/// <summary>
		/// BuildController.DisableBuildMode prefix. The player's Back/Right click during placement becomes
		/// "put it back and pick another building" without leaving build mode (no camera jump).
		/// Returns false to skip the vanilla disable.
		/// </summary>
		public static bool OnBeforeBuildModeDisabled()
		{
			if (Transitioning || State != MoveState.Placing)
			{
				return true;
			}
			ShowHidden();
			bool playerCancel = LazyInput.GetKeyDown(GameKey.Back) || LazyInput.GetKeyDown(GameKey.RightClick);
			if (!playerCancel)
			{
				// Something else closes build mode (scene change, cutscene...): let it, the original is visible again.
				State = MoveState.None;
				placementPointer = null;
				return true;
			}
			ReturnToSelection(BuildController.Instance);
			skipManagerDisableFrame = Time.frameCount;
			return false;
		}

		/// <summary>BuildManager.Disable prefix: skipped right after a soft cancel, which stays in build mode.</summary>
		public static bool OnBuildManagerDisable()
		{
			if (skipManagerDisableFrame == Time.frameCount)
			{
				skipManagerDisableFrame = -1;
				return false;
			}
			return true;
		}

		private static void ReturnToSelection(BuildController controller, bool pointerAlreadyDisabled = false)
		{
			placementPointer = null;
			slotPlans = new List<MoveExecutor.SlotPlan>();
			Original = null;
			LockRotation = false;
			WorldZone worldZone = zone ?? LazySingleton<BuildManager>.Instance.WorldZone;
			if (controller == null || worldZone == null)
			{
				State = MoveState.None;
				return;
			}
			try
			{
				Transitioning = true;
				if (controller.IsBuildModeActive)
				{
					GameAccess.SwapBuildData(controller, MoveData, worldZone, pointerAlreadyDisabled);
				}
				else
				{
					controller.EnableBuildMode(MoveData, worldZone);
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogError("Failed to return to move selection: " + ex);
				State = MoveState.None;
			}
			finally
			{
				Transitioning = false;
			}
		}

		/// <summary>
		/// Uses the game's own hidden flag: the chunk system re-activates any visible GameObject every frame,
		/// so SetActive(false) alone does not stick. A hidden Wgo is deactivated (no colliders, no render).
		/// </summary>
		/// <summary>Zombies working at the building stand in its dock points; hide them with it until it is placed.</summary>
		private static void HideWithWorkers(Wgo wgo)
		{
			if (wgo == null || wgo.Data == null)
			{
				return;
			}
			Hide(wgo);
			List<DockPointData> docks = wgo.Data.MainWgoPartData?.GetDockPoints(DockPointData.Availability.OnlyOccupied);
			if (docks == null)
			{
				return;
			}
			foreach (DockPointData dock in docks)
			{
				Hide(GameScene.GetWgoViewGlobal(dock.OccupiedBy));
			}
		}

		private static void Hide(Wgo wgo)
		{
			WgoData data = wgo != null ? wgo.Data : null;
			if (data != null && !data.IsHidden)
			{
				data.IsHidden = true;
				hiddenData.Add(data);
			}
		}

		public static void ShowHidden()
		{
			foreach (WgoData data in hiddenData)
			{
				data.IsHidden = false;
			}
			hiddenData.Clear();
			Physics.SyncTransforms();
		}

		/// <summary>Failsafe from MoveRunner.Update: nothing may stay hidden once no placement is in progress.</summary>
		public static void EnsureNothingHiddenWhenIdle()
		{
			if (hiddenData.Count > 0 && State != MoveState.Placing && State != MoveState.Committing)
			{
				Plugin.Log.LogWarning($"Placement ended unexpectedly; showing {hiddenData.Count} hidden object(s) again");
				ShowHidden();
			}
		}

		/// <summary>IsHidden is saved with the WgoData; never let a hidden original reach a save file.</summary>
		public static void RestoreBeforeSave()
		{
			if (hiddenData.Count > 0)
			{
				Plugin.Log.LogWarning("Game is saving during placement; un-hiding the original first");
				ShowHidden();
			}
		}
	}

	internal class MovePlan
	{
		public Wgo Wgo;

		public Vector3 NewPosition;

		public string VariationId;

		public int RotationIndex;

		public Wgo NewSlotParent;
	}
}
