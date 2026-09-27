using System.Collections;
using System.Collections.Generic;
using LazyBearTechnology;
using UnityEngine;

namespace GK2MoveBuildings
{
	/// <summary>
	/// Relocates an existing WgoData without recreating it: the same object (same UniqueId, inventory,
	/// craft queue, fuel) is taken off the scene and put back at the new position — the pattern the game
	/// itself uses in WorldData.ChangeWgoData. Side effects of the removal that the game does not undo
	/// on re-add are restored here.
	/// </summary>
	internal static class MoveExecutor
	{
		private class DockRecord
		{
			public int Index;

			public DockPointData OldDockPoint;

			public WgoData Occupant;
		}

		private class SlotFilterRecord
		{
			public int SlotIndex;

			public Direction Direction;

			public string ItemId;
		}

		/// <summary>Objects currently between "removed" and "re-added"; patches use it to skip the game's cleanup for them.</summary>
		private static readonly HashSet<SGuid> moving = new HashSet<SGuid>();

		public static bool IsMoving(WgoData data)
		{
			return data != null && moving.Contains(data.UniqueId);
		}

		public static bool IsMoving(SGuid uniqueId)
		{
			return !SGuid.IsNullOrEmpty(uniqueId) && moving.Contains(uniqueId);
		}

		public static bool Move(MovePlan plan)
		{
			Wgo wgo = plan.Wgo;
			if (!MoveRules.CanMove(wgo, out string reason))
			{
				Plugin.Log.LogWarning("Move cancelled: " + reason);
				return false;
			}
			WgoData data = wgo.Data;
			Vector3 oldPosition = data.Position;
			Vector3 delta = plan.NewPosition - oldPosition;
			bool parentRotates = IsStateChange(data, plan.VariationId, plan.RotationIndex);
			List<WgoData> slotExtensions = MoveRules.GetSlotExtensions(data);
			// Which slot each extension sits in, so it can be re-seated in the same slot after a rotation.
			Dictionary<WgoData, int> slotIndices = parentRotates && slotExtensions.Count > 0 ? RecordSlotIndices(wgo, slotExtensions) : null;
			UnlinkWorkbenchRelations(data);
			foreach (WgoData extension in slotExtensions)
			{
				extension.RemoveWorkbenchParent(data.UniqueId);
			}
			Wgo newView = MoveSingle(data, plan.NewPosition, plan.VariationId, plan.RotationIndex);
			if (newView == null)
			{
				return false;
			}
			if (plan.NewSlotParent != null && plan.NewSlotParent.Data != null && plan.NewSlotParent.Data != data)
			{
				plan.NewSlotParent.Data.AddWorkbenchExtension(data.UniqueId);
				data.AddWorkbenchParent(plan.NewSlotParent.Data.UniqueId);
			}
			foreach (WgoData extension in slotExtensions)
			{
				UnlinkWorkbenchRelations(extension);
				if (slotIndices != null && slotIndices.TryGetValue(extension, out int slotIndex) && TryGetSlotPlacement(newView, extension, slotIndex, out Vector3 slotPosition, out int slotRotation))
				{
					MoveSingle(extension, slotPosition, extension.MainWgoPartData.variationId, slotRotation);
				}
				else
				{
					if (slotIndices != null)
					{
						Plugin.Log.LogWarning($"[{extension.id}] could not find its slot on the rotated [{data.id}], moved by offset instead");
					}
					MoveSingle(extension, extension.Position + delta, extension.MainWgoPartData.variationId, extension.MainWgoPartData.rotationIndex);
				}
				data.AddWorkbenchExtension(extension.UniqueId);
				extension.AddWorkbenchParent(data.UniqueId);
			}
			Plugin.Log.LogInfo($"Moved [{data.id}] {oldPosition} -> {data.Position}, rotation {data.MainWgoPartData.rotationIndex}, extensions {slotExtensions.Count}");
			return true;
		}

		private static bool IsStateChange(WgoData data, string variationId, int rotationIndex)
		{
			return rotationIndex != data.MainWgoPartData.rotationIndex || variationId != data.MainWgoPartData.variationId;
		}

		private static void UnlinkWorkbenchRelations(WgoData data)
		{
			WorldData worldData = MainGame.Instance.GameSave.worldData;
			foreach (SGuid parentId in new List<SGuid>(data.WorkbenchParents))
			{
				worldData.GetWgoData(parentId)?.RemoveWorkbenchExtension(data.UniqueId);
				data.RemoveWorkbenchParent(parentId);
			}
			foreach (SGuid extensionId in new List<SGuid>(data.AttachedWorkbenchExtensions))
			{
				worldData.GetWgoData(extensionId)?.RemoveWorkbenchParent(data.UniqueId);
				data.RemoveWorkbenchExtension(extensionId);
			}
		}

		private static Wgo MoveSingle(WgoData data, Vector3 newPosition, string variationId, int rotationIndex)
		{
			WorldData worldData = MainGame.Instance.GameSave.worldData;
			Wgo oldView = GameScene.GetWgoViewGlobal(data.UniqueId);
			if (oldView != null && !oldView.gameObject.activeSelf)
			{
				// Conveyor disconnect on despawn looks for the view's active colliders.
				oldView.gameObject.SetActive(true);
				Physics.SyncTransforms();
			}
			Vector3 oldPosition = data.Position;
			List<Item> inventorySnapshot = SnapshotItems(data.Inventory);
			List<Item> craftInventorySnapshot = SnapshotItems(data.CraftInventory);
			List<DockRecord> docks = RecordDocks(data);
			List<Vector3> oldDockPositions = GetDockPositions(data, oldPosition);
			Rect oldFootprint = data.MainWgoPartData.GetCollisionBoundsRect(oldPosition);
			List<SlotFilterRecord> slotFilters = RecordSlotFilters(data);
			List<Item> autoCellItems = CollectAutoCellItems(data);
			bool hadDelayedEvents = GameAccess.HasDelayedEvents(data);
			bool rotationChanged = IsStateChange(data, variationId, rotationIndex);

			moving.Add(data.UniqueId);
			try
			{
				worldData.RemoveWgoDataFromGameScene(data, clearCraftComponent: false);

				// Auto-built conveyor cells were deleted with the old view; the reconnect below rebuilds them.
				if (data is ConveyorWgoData conveyorData)
				{
					conveyorData.HardConnectedWGOs.Clear();
				}
				data.isRemovingFromData = false;
				// Same resets ChangeId does: gd points and workbench-extension links get re-registered on spawn.
				data.gdPointsRegistered = false;
				data.wasSpawnedAtLeastOnce = false;
				data.Position = newPosition;
				if (rotationChanged)
				{
					// Same as WgoBuildPointer.TryDoBuildAction: copy the ghost's state fields directly.
					// TryApplyState can't be used — most buildings have an empty variation id and no
					// AvailableVariations on the data, and it silently refuses those.
					data.MainWgoPartData.variationId = variationId;
					data.MainWgoPartData.rotationIndex = rotationIndex;
				}

				// The old view's DeInit de-initialized the data (craft bindings, conveyor component).
				// Re-initialize before adding, the same order a freshly constructed WgoData goes through.
				data.PrepareForGame();
				worldData.AddWgoData(data, recheckVisibilityOnSpawn: true);
			}
			finally
			{
				moving.Remove(data.UniqueId);
			}
			RestoreRemovalSideEffects(data, hadDelayedEvents);
			RestoreItems(data, data.Inventory, inventorySnapshot, "inventory");
			RestoreItems(data, data.CraftInventory, craftInventorySnapshot, "craft inventory");
			List<WgoData> seated = RestoreDocks(data, docks, rotationChanged);
			RedirectWalkersHeadingHere(data, oldFootprint, oldDockPositions, newPosition - oldPosition, seated);
			if (GardenBedNavigation.IsGardenPlot(data))
			{
				// Neighbouring plots at the new spot need their approach points re-evaluated too.
				GardenBedNavigation.RefreshAround(data, null);
			}

			Wgo newView = GameScene.GetWgoViewGlobal(data.UniqueId);
			if (newView == null)
			{
				Plugin.Log.LogError($"[{data.id}] was re-added but has no view");
				return null;
			}
			// Build mode stays on during the move; mark the new view like the rest of the zone so the chunk system leaves it visible.
			newView.UpdateFlag(ChunkingIgnoreType.Building, newValue: true);
			if (data is ConveyorWgoData)
			{
				Plugin.Runner.StartCoroutine(ReconnectConveyorWhenLoaded(data, slotFilters, autoCellItems));
			}
			return newView;
		}

		/// <summary>WgoData.OnRemove undoes these, but nothing redoes them on AddWgoData (the game only sets them when the data is first created).</summary>
		private static void RestoreRemovalSideEffects(WgoData data, bool hadDelayedEvents)
		{
			WGODef def = data.Definition;
			if (def.townQuality > 0)
			{
				MainGame.Instance.GameSave.townSystem.Quality += def.townQuality;
			}
			if (!string.IsNullOrEmpty(def.npcLifeSimGroup))
			{
				MainGame.Instance.GameSave.npcLifeSimulatorData.GetGroupById(def.npcLifeSimGroup)?.AddWgoToGroup(data);
			}
			List<SGuid> delayedIds = MainGame.Instance.GameSave.wgoDelayedEventSystemData.wgoUniqueIds;
			if (hadDelayedEvents && !delayedIds.Contains(data.UniqueId))
			{
				delayedIds.Add(data.UniqueId);
			}
		}

		private static List<Item> SnapshotItems(Inventory inventory)
		{
			List<Item> result = new List<Item>();
			List<Item> items = inventory?.Data?.Inventory;
			if (items == null)
			{
				return result;
			}
			foreach (Item item in items)
			{
				if (item != null && !item.IsEmpty)
				{
					result.Add(item);
				}
			}
			return result;
		}

		/// <summary>Safety net: the Item objects are the same references, so anything missing can be put back as-is.</summary>
		private static void RestoreItems(WgoData data, Inventory inventory, List<Item> snapshot, string label)
		{
			if (snapshot.Count == 0)
			{
				return;
			}
			List<Item> current = inventory?.Data?.Inventory;
			int restored = 0;
			foreach (Item item in snapshot)
			{
				if (current == null || !current.Contains(item))
				{
					if (inventory == null || !inventory.AddItemToInventory(item))
					{
						data.MakeDrop(item);
					}
					restored++;
				}
			}
			if (restored > 0)
			{
				Plugin.Log.LogError($"[{data.id}] {label}: {restored} item stack(s) went missing during the move and were put back");
			}
		}

		private static List<DockRecord> RecordDocks(WgoData data)
		{
			List<DockRecord> result = new List<DockRecord>();
			List<DockPointData> all = data.MainWgoPartData?.GetDockPoints();
			if (all == null)
			{
				return result;
			}
			WorldData worldData = MainGame.Instance.GameSave.worldData;
			for (int i = 0; i < all.Count; i++)
			{
				DockPointData dock = all[i];
				if (!dock.IsOccupied)
				{
					continue;
				}
				WgoData occupant = worldData.GetWgoData(dock.OccupiedBy);
				if (occupant != null)
				{
					result.Add(new DockRecord { Index = i, OldDockPoint = dock, Occupant = occupant });
				}
			}
			return result;
		}

		private static List<Vector3> GetDockPositions(WgoData data, Vector3 position)
		{
			List<Vector3> result = new List<Vector3>();
			List<DockPointData> all = data.MainWgoPartData?.GetDockPoints();
			if (all != null)
			{
				foreach (DockPointData dock in all)
				{
					result.Add(dock.GetPosFrom(position));
				}
			}
			return result;
		}

		/// <summary>
		/// Zombies working at the building stand in its dock points; seat them the way the game does
		/// (exact dock position, facing the dock direction). After a rotation the dock set is different:
		/// keep the same index when it is a compatible free point, otherwise take any compatible free point.
		/// </summary>
		private static List<WgoData> RestoreDocks(WgoData data, List<DockRecord> docks, bool rotationChanged)
		{
			List<WgoData> seated = new List<WgoData>();
			if (docks.Count == 0)
			{
				return seated;
			}
			List<DockPointData> all = data.MainWgoPartData.GetDockPoints();
			foreach (DockRecord record in docks)
			{
				DockPointData dock = record.OldDockPoint;
				if (rotationChanged)
				{
					dock = FindCompatibleDock(all, record);
					record.OldDockPoint.UnOccupy();
					if (dock == null)
					{
						Plugin.Log.LogWarning($"[{data.id}] no free dock point after rotation, releasing [{record.Occupant.id}]");
						record.Occupant.takenDockPointsParentSGuid = SGuid.Empty;
						continue;
					}
					record.Occupant.OccupyDockPoint(dock, data.UniqueId);
				}
				bool isWorker = data.Worker != null && data.Worker.Id == record.Occupant.UniqueId;
				if (isWorker)
				{
					data.TryRemoveWorkerCutUnit(data.Worker);
				}
				record.Occupant.Position = dock.GetPosFrom(data.Position);
				record.Occupant.direction.Value = dock.Direction.ConvertToVector2XZ();
				if (isWorker)
				{
					data.TryAddWorkerCutUnit(data.Worker);
				}
				seated.Add(record.Occupant);
			}
			return seated;
		}

		private static DockPointData FindCompatibleDock(List<DockPointData> docks, DockRecord record)
		{
			DockPointData.Baked wanted = record.OldDockPoint.BakedData;
			bool Compatible(DockPointData d)
			{
				return d != null && d.BakedData != null && (!d.IsOccupied || d.IsOccupiedBy(record.Occupant.UniqueId)) && (wanted == null || (d.BakedData.IsForZombie == wanted.IsForZombie && d.BakedData.DockPointTag == wanted.DockPointTag));
			}
			if (record.Index < docks.Count && Compatible(docks[record.Index]))
			{
				return docks[record.Index];
			}
			return docks.Find(Compatible);
		}

		/// <summary>
		/// Anyone already walking to the building has a path to the old spot. Re-issue the same path request
		/// (same callbacks, so they do what they were going to do) with the destination moved along.
		/// </summary>
		private static void RedirectWalkersHeadingHere(WgoData data, Rect oldFootprint, List<Vector3> oldDocks, Vector3 delta, List<WgoData> skip)
		{
			GameSceneData scene = MainGame.Instance.GameSave.worldData.GetGameSceneDataById(data.WorldId);
			if (scene?.wgoDataList == null || delta.sqrMagnitude < 0.0001f && oldDocks.Count == 0)
			{
				return;
			}
			List<Vector3> newDocks = GetDockPositions(data, data.Position);
			Rect nearFootprint = new Rect(oldFootprint.xMin - 0.5f, oldFootprint.yMin - 0.5f, oldFootprint.width + 1f, oldFootprint.height + 1f);
			int redirected = 0;
			foreach (WgoData walker in new List<WgoData>(scene.wgoDataList))
			{
				// Only zombies work at buildings; NPCs passing by must not be touched.
				if (!(walker is ZombieWgoData zombie) || skip.Contains(walker))
				{
					continue;
				}
				MovementComponent movement = zombie.MovementComponent;
				if (movement == null || !movement.IsMoving)
				{
					continue;
				}
				Vector3 end = GameAccess.GetPathEnd(movement);
				Vector3? newEnd = null;
				int dockIndex = NearestIndex(oldDocks, end, 0.35f);
				if (dockIndex >= 0)
				{
					newEnd = dockIndex < newDocks.Count ? newDocks[dockIndex] : end + delta;
				}
				else if (zombie.AttachedWgoData == data && nearFootprint.Contains(new Vector2(end.x, end.z)))
				{
					newEnd = end + delta;
				}
				if (newEnd.HasValue && GameAccess.TryRedirectPath(movement, newEnd.Value))
				{
					redirected++;
				}
			}
			if (redirected > 0)
			{
				Plugin.Log.LogInfo($"[{data.id}] redirected {redirected} walker(s) to the new spot");
			}
		}

		private static int NearestIndex(List<Vector3> points, Vector3 target, float maxDistance)
		{
			int best = -1;
			float bestSqr = maxDistance * maxDistance;
			for (int i = 0; i < points.Count; i++)
			{
				Vector3 d = points[i] - target;
				d.y = 0f;
				if (d.sqrMagnitude <= bestSqr)
				{
					bestSqr = d.sqrMagnitude;
					best = i;
				}
			}
			return best;
		}

		private static List<BuildArea> GetSlotAreas(Wgo parent, string areaId)
		{
			List<BuildArea> result = new List<BuildArea>();
			if (parent == null || string.IsNullOrEmpty(areaId))
			{
				return result;
			}
			foreach (BuildArea area in parent.GetComponentsInChildren<BuildArea>())
			{
				if (area.Id == areaId && (area.Collider != null || area.GetComponent<Collider>() != null))
				{
					result.Add(area);
				}
			}
			return result;
		}

		private static Collider AreaCollider(BuildArea area)
		{
			return area.Collider != null ? area.Collider : area.GetComponent<Collider>();
		}

		/// <summary>An extension that will be re-seated in slot <see cref="Index"/> of the parent.</summary>
		internal class SlotPlan
		{
			public WgoData Extension;

			public BuildingDef Def;

			public int Index;
		}

		public static List<SlotPlan> RecordSlotPlans(Wgo parent)
		{
			List<SlotPlan> result = new List<SlotPlan>();
			List<WgoData> extensions = MoveRules.GetSlotExtensions(parent.Data);
			if (extensions.Count == 0)
			{
				return result;
			}
			foreach (KeyValuePair<WgoData, int> pair in RecordSlotIndices(parent, extensions))
			{
				result.Add(new SlotPlan
				{
					Extension = pair.Key,
					Def = GameBalance.Me.buildableWgos[pair.Key.id],
					Index = pair.Value
				});
			}
			return result;
		}

		public static int CountGhostSlots(Wgo ghost, List<SlotPlan> plans)
		{
			int count = 0;
			foreach (SlotPlan plan in plans)
			{
				count += plan.Index < GetSlotAreas(ghost, plan.Def.customBuildAreaId).Count ? 1 : 0;
			}
			return count;
		}

		private static readonly Collider[] slotOverlap = new Collider[64];

		/// <summary>
		/// Slots can stick out of the parent's own footprint (a furnace's bellows sit next to it), and the vanilla
		/// ghost check only covers the parent. Checks each slot of the ghost the way BuildController checks a free
		/// full-cover slot: anything solid inside it that isn't the ghost, a temp object or what we are moving blocks it.
		/// </summary>
		public static bool AreSlotsFree(Wgo ghost, List<SlotPlan> plans, Wgo original)
		{
			if (ghost == null || plans == null || plans.Count == 0)
			{
				return true;
			}
			Physics.SyncTransforms();
			foreach (SlotPlan plan in plans)
			{
				List<BuildArea> areas = GetSlotAreas(ghost, plan.Def.customBuildAreaId);
				if (plan.Index >= areas.Count)
				{
					continue;
				}
				Bounds bounds = AreaCollider(areas[plan.Index]).bounds;
				Vector3 halfExtents = Vector3.Max(Vector3.zero, bounds.extents - new Vector3(0.02f, 0f, 0.02f));
				int count = Physics.OverlapBoxNonAlloc(bounds.center, halfExtents, slotOverlap, Quaternion.identity, 590080);
				for (int i = 0; i < count; i++)
				{
					Collider hit = slotOverlap[i];
					if (hit == null || hit.TryGetComponent<BuildArea>(out _) || hit.TryGetComponent<ModuleSlotArea>(out _))
					{
						continue;
					}
					if (PlacementBlockingArea.TryGet(hit, out _))
					{
						if (PlacementBlockingArea.IsBlockingFor(hit, plan.Def))
						{
							return false;
						}
						continue;
					}
					Wgo owner = hit.GetComponentInParent<Wgo>();
					if (owner != null)
					{
						bool ours = owner == ghost || owner == original || owner.Data == null || owner.Data.isTempObject || plans.Exists(p => p.Extension == owner.Data);
						if (ours || plan.Def.ShouldIgnoreWgoGroupAsObstacle(owner.Data.Definition.wgoGroup))
						{
							continue;
						}
						return false;
					}
					int layer = hit.gameObject.layer;
					if (layer == 8 || layer == 16)
					{
						return false;
					}
				}
			}
			return true;
		}

		private static Dictionary<WgoData, int> RecordSlotIndices(Wgo parent, List<WgoData> extensions)
		{
			Dictionary<WgoData, int> result = new Dictionary<WgoData, int>();
			Physics.SyncTransforms();
			foreach (WgoData extension in extensions)
			{
				if (!GameBalance.Me.buildableWgos.TryGetValue(extension.id, out BuildingDef def))
				{
					continue;
				}
				List<BuildArea> areas = GetSlotAreas(parent, def.customBuildAreaId);
				Wgo extensionView = GameScene.GetWgoViewGlobal(extension.UniqueId);
				Vector3 center = extensionView != null && GameAccess.TryGetBuildAreaBounds(extensionView, out Bounds bounds) ? bounds.center : extension.Position;
				int best = -1;
				float bestSqr = float.MaxValue;
				for (int i = 0; i < areas.Count; i++)
				{
					Vector3 d = AreaCollider(areas[i]).bounds.center - center;
					d.y = 0f;
					if (d.sqrMagnitude < bestSqr)
					{
						bestSqr = d.sqrMagnitude;
						best = i;
					}
				}
				if (best >= 0)
				{
					result[extension] = best;
				}
			}
			return result;
		}

		/// <summary>
		/// Where an extension goes in slot N of the (rotated) parent — the same placement the game uses for
		/// its full-cover slot hints: a temporary copy is measured and centred on the slot's collider.
		/// </summary>
		private static bool TryGetSlotPlacement(Wgo parent, WgoData extension, int slotIndex, out Vector3 position, out int rotation)
		{
			position = extension.Position;
			rotation = extension.MainWgoPartData.rotationIndex;
			if (!GameBalance.Me.buildableWgos.TryGetValue(extension.id, out BuildingDef def))
			{
				return false;
			}
			parent.UpdateChunkVisibility(isVisible: true);
			if (!GameAccess.ArePartsLoaded(parent))
			{
				return false;
			}
			Physics.SyncTransforms();
			List<BuildArea> areas = GetSlotAreas(parent, def.customBuildAreaId);
			if (slotIndex >= areas.Count)
			{
				return false;
			}
			BuildArea area = areas[slotIndex];
			if (area.HasRotationRequirement)
			{
				rotation = area.RotationRequirement;
			}
			GameScene scene = MainGame.PlayerController.CurrentGameScene;
			WgoData probeData = new WgoData(extension.id, Vector3.zero, scene.Id)
			{
				isTempObject = true
			};
			probeData.MainWgoPartData.variationId = extension.MainWgoPartData.variationId;
			probeData.MainWgoPartData.rotationIndex = rotation;
			Wgo probe = Wgo.Spawn(probeData, scene.transform, registerInChunkManagerIfStatic: true, ignoreChunkRegistration: true, applyDefaultWgoPartState: true);
			try
			{
				probe.UpdateChunkVisibility(isVisible: true);
				Physics.SyncTransforms();
				if (!GameAccess.TryGetBuildAreaBounds(probe, out Bounds probeBounds))
				{
					return false;
				}
				Vector3 slotCenter = AreaCollider(area).bounds.center;
				position = new Vector3(slotCenter.x - probeBounds.center.x, area.transform.position.y, slotCenter.z - probeBounds.center.z);
				return true;
			}
			finally
			{
				probe.gameObject.SetActive(false);
				Object.Destroy(probe.gameObject);
				UndoProbeCreationSideEffects(probeData);
			}
		}

		/// <summary>Constructing any WgoData applies town quality, NPC group and script; the measuring probe must not leave those behind.</summary>
		private static void UndoProbeCreationSideEffects(WgoData probe)
		{
			WGODef def = probe.Definition;
			if (def == null)
			{
				return;
			}
			if (def.townQuality > 0)
			{
				MainGame.Instance.GameSave.townSystem.Quality -= def.townQuality;
			}
			if (!string.IsNullOrEmpty(def.npcLifeSimGroup))
			{
				MainGame.Instance.GameSave.npcLifeSimulatorData.GetGroupById(def.npcLifeSimGroup)?.RemoveWgoFromGroup(probe);
			}
			if (!string.IsNullOrEmpty(def.attachedScript))
			{
				WgoDataScriptsManager.DestroyScript(probe);
			}
		}

		private static List<SlotFilterRecord> RecordSlotFilters(WgoData data)
		{
			List<SlotFilterRecord> result = new List<SlotFilterRecord>();
			List<ConveyorChestSlotData> slots = GameAccess.GetChestSlots((data as ConveyorWgoData)?.ConveyorComponent);
			if (slots == null)
			{
				return result;
			}
			foreach (ConveyorChestSlotData slot in slots)
			{
				if (!string.IsNullOrEmpty(slot.slotItemId))
				{
					result.Add(new SlotFilterRecord
					{
						SlotIndex = slot.slotIndex,
						Direction = slot.slotPosDirection,
						ItemId = slot.slotItemId
					});
				}
			}
			return result;
		}

		/// <summary>The game deletes auto-built belt cells together with their owner; whatever they carry would be lost.</summary>
		private static List<Item> CollectAutoCellItems(WgoData data)
		{
			List<Item> result = new List<Item>();
			if (!(data is ConveyorWgoData conveyorData))
			{
				return result;
			}
			WorldData worldData = MainGame.Instance.GameSave.worldData;
			foreach (SGuid cellId in conveyorData.HardConnectedWGOs)
			{
				result.AddRange(SnapshotItems(worldData.GetWgoData(cellId)?.Inventory));
			}
			return result;
		}

		private static IEnumerator ReconnectConveyorWhenLoaded(WgoData data, List<SlotFilterRecord> slotFilters, List<Item> autoCellItems)
		{
			Wgo view = null;
			for (int frame = 0; frame < 300; frame++)
			{
				view = GameScene.GetWgoViewGlobal(data.UniqueId);
				if (view == null)
				{
					Plugin.Log.LogWarning($"[{data.id}] view disappeared before conveyor reconnect");
					DropItems(data, autoCellItems);
					yield break;
				}
				if (GameAccess.ArePartsLoaded(view))
				{
					break;
				}
				yield return null;
			}
			if (!GameAccess.ArePartsLoaded(view))
			{
				Plugin.Log.LogError($"[{data.id}] parts never loaded, conveyor links not restored; rebuild the adjacent belt piece to reconnect");
				DropItems(data, autoCellItems);
				yield break;
			}
			yield return new WaitForFixedUpdate();
			ConveyorBuildPointer.TryMakeConnectionsOutsideBuildSystem(view);
			List<ConveyorChestSlotData> slots = GameAccess.GetChestSlots((data as ConveyorWgoData)?.ConveyorComponent);
			if (slots != null)
			{
				foreach (SlotFilterRecord record in slotFilters)
				{
					ConveyorChestSlotData slot = record.SlotIndex >= 0 ? slots.Find(s => s.slotIndex == record.SlotIndex) : slots.Find(s => s.slotPosDirection == record.Direction && string.IsNullOrEmpty(s.slotItemId));
					slot?.SetItemSlotId(record.ItemId);
				}
			}
			ReturnAutoCellItems(data, autoCellItems);
			(data as ConveyorWgoData)?.UpdateAttachedWgoViewWidgets();
			Plugin.Log.LogInfo($"[{data.id}] conveyor links rebuilt, {slotFilters.Count} slot filter(s) restored");
		}

		private static void ReturnAutoCellItems(WgoData data, List<Item> items)
		{
			if (items.Count == 0)
			{
				return;
			}
			WorldData worldData = MainGame.Instance.GameSave.worldData;
			List<SGuid> cells = (data as ConveyorWgoData)?.HardConnectedWGOs;
			if (cells == null || cells.Count == 0)
			{
				Plugin.Log.LogWarning($"[{data.id}] auto-built belt cell could not be rebuilt (spot blocked?); {items.Count} item stack(s) dropped on the ground");
			}
			List<Item> leftovers = new List<Item>();
			foreach (Item item in items)
			{
				bool placed = false;
				if (cells != null)
				{
					foreach (SGuid cellId in cells)
					{
						Inventory inventory = worldData.GetWgoData(cellId)?.Inventory;
						if (inventory != null && inventory.AddItemToInventory(item))
						{
							placed = true;
							break;
						}
					}
				}
				if (!placed)
				{
					leftovers.Add(item);
				}
			}
			DropItems(data, leftovers);
		}

		private static void DropItems(WgoData data, List<Item> items)
		{
			foreach (Item item in items)
			{
				data.MakeDrop(item);
			}
		}
	}
}
