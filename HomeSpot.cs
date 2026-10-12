using System.Collections.Generic;
using UnityEngine;

namespace GK2MoveBuildings
{
	/// <summary>
	/// Where a building stood before the mod first moved it. Objects the world starts with (a repaired zombie
	/// carousel, for example) can sit where the game never lets you build, so once moved away neither the build
	/// menu nor the vanilla placement check would accept that spot again. The spot is kept in the object's own
	/// game resources, so it survives saving; the moved object may go back onto it, and a new one of the same
	/// kind may be built there.
	/// </summary>
	internal static class HomeSpot
	{
		private const string FlagKey = "gk2mb_home";

		private const string XKey = "gk2mb_home_x";

		private const string YKey = "gk2mb_home_y";

		private const string ZKey = "gk2mb_home_z";

		private const string RotationKey = "gk2mb_home_rot";

		public static void RememberBeforeFirstMove(WgoData data)
		{
			if (data == null || data.GetGameResInt(FlagKey) > 0)
			{
				return;
			}
			Vector3 position = data.Position;
			data.SetGameRes(XKey, position.x);
			data.SetGameRes(YKey, position.y);
			data.SetGameRes(ZKey, position.z);
			// Stored +1 so a non-rotatable object (-1) is not an empty value.
			data.SetGameRes(RotationKey, data.MainWgoPartData.rotationIndex + 1);
			data.SetGameRes(FlagKey, 1);
		}

		public static bool TryGet(WgoData data, out Vector3 position, out int rotationIndex)
		{
			position = Vector3.zero;
			rotationIndex = -1;
			if (data == null || data.GetGameResInt(FlagKey) <= 0)
			{
				return false;
			}
			position = new Vector3(data.GetGameRes(XKey), data.GetGameRes(YKey), data.GetGameRes(ZKey));
			rotationIndex = data.GetGameResInt(RotationKey) - 1;
			return true;
		}

		/// <summary>
		/// Where the level designers put a broken object that repairing replaces with the working one
		/// (zombie_carousel_fix spawns zombie_carousel on the broken one's spot). Those spots are outside the
		/// preset build slots, so once the repaired object is moved away, or was moved before the mod
		/// remembered home spots, nothing else would let one stand there again. Taken from the game's
		/// RuinedTemple scene content; scene offset is zero.
		/// </summary>
		private static readonly (string wgoId, string zoneId, Vector3 position)[] designerSpots =
		{
			("zombie_carousel", "conveyor", new Vector3(-96.24f, 0.006f, -410.275f)),
			("zombie_carousel", "conveyor", new Vector3(-96.24f, 0.006f, -418.675f))
		};

		/// <summary>
		/// Spots beyond the vanilla rules for objects with the given id in the current build zone: the home of
		/// the object being moved, vacated homes of other objects with that id, and designer spots.
		/// Rotation -1 means any. Whether a spot is free is checked separately.
		/// </summary>
		public static IEnumerable<(Vector3 position, int rotationIndex)> Candidates(string wgoId, WgoData moving)
		{
			if (string.IsNullOrEmpty(wgoId))
			{
				yield break;
			}
			if (moving != null && !IsSlotExtension(moving))
			{
				if (TryGet(moving, out Vector3 home, out int homeRotation))
				{
					yield return (home, homeRotation);
				}
				else
				{
					// Never moved yet: where it stands now is its home.
					yield return (moving.Position, moving.MainWgoPartData.rotationIndex);
				}
			}
			WorldZone zone = LazyBearTechnology.LazySingleton<BuildManager>.Instance.WorldZone;
			if (zone == null)
			{
				yield break;
			}
			foreach (Wgo wgo in zone.Wgos)
			{
				WgoData other = wgo != null ? wgo.Data : null;
				if (other != null && other != moving && other.id == wgoId && !IsSlotExtension(other) && TryGet(other, out Vector3 home, out int homeRotation))
				{
					yield return (home, homeRotation);
				}
			}
			string zoneId = zone.Data?.Definition?.id;
			foreach ((string spotWgoId, string spotZoneId, Vector3 position) in designerSpots)
			{
				if (spotWgoId == wgoId && spotZoneId == zoneId)
				{
					yield return (position, -1);
				}
			}
		}

		/// <summary>A slot extension's old spot is its parent's slot, which the vanilla rules already handle.</summary>
		private static bool IsSlotExtension(WgoData data)
		{
			return data.WorkbenchParents.Count > 0 && GameBalance.Me.buildableWgos.TryGetValue(data.id, out BuildingDef def) && def != null && def.chooseCustomBuildAreaType == BuildingDef.BuildAreaChoosingType.FullCoverSoft;
		}

		public static bool TryFindSpot(string wgoId, WgoData moving, Wgo ghost, out Vector3 spot)
		{
			spot = Vector3.zero;
			if (ghost == null || ghost.Data == null)
			{
				return false;
			}
			foreach ((Vector3 position, int rotationIndex) in Candidates(wgoId, moving))
			{
				if (IsGhostOn(ghost, position, rotationIndex))
				{
					spot = position;
					return true;
				}
			}
			return false;
		}

		/// <summary>What a building from the build menu ends up as, the same id the game shows on free-slot hints.</summary>
		public static string SpotIdFor(BuildingDef def)
		{
			if (def == null || (def.buildingMode != BuildingDef.BuildingMode.Place && def.buildingMode != BuildingDef.BuildingMode.ConveyorPlace))
			{
				return null;
			}
			return string.IsNullOrEmpty(def.customWgoPlacePreview) ? def.wgoId : def.customWgoPlacePreview;
		}

		/// <summary>The build-menu ghost (not ours) stands on a spot of the object it builds.</summary>
		public static bool TryFindBuildSpot(WgoBuildPointer pointer, out Vector3 spot)
		{
			spot = Vector3.zero;
			if (pointer == null || MoveSession.IsPlacementPointer(pointer) || MoveSession.IsCommittingPointer(pointer))
			{
				return false;
			}
			return TryFindSpot(SpotIdFor(pointer.BuildData?.Definition), null, pointer.Target, out spot);
		}

		/// <summary>A new building may go onto a free spot; resources and limits are still required.</summary>
		public static bool AllowBuildSpot(WgoBuildPointer pointer)
		{
			return TryFindBuildSpot(pointer, out _) && GameAccess.CanTakeResources(pointer) && IsFootprintFree(pointer, null, pointer.BuildData.Definition);
		}

		/// <summary>
		/// The ghost is on a spot when it is in the spot's rotation (-1: any) and within half a build cell of it
		/// (objects placed by the level designers need not be on the build grid).
		/// </summary>
		public static bool IsGhostOn(Wgo ghost, Vector3 spot, int spotRotation)
		{
			int ghostRotation = ghost.MainWgoPart?.WgoPartData?.rotationIndex ?? -1;
			if (spotRotation != -1 && ghostRotation != -1 && ghostRotation != spotRotation)
			{
				return false;
			}
			Vector3 d = ghost.Data.Position - spot;
			Vector2 half = BuildConsts.CELL_SIZE * 0.5f;
			return Mathf.Abs(d.x) < half.x && Mathf.Abs(d.z) < half.y;
		}

		private static readonly Collider[] overlap = new Collider[32];

		/// <summary>
		/// The vanilla check minus the build-area and zone rules (the object already stood here):
		/// only other objects and solid scenery in the footprint block it.
		/// </summary>
		public static bool IsFootprintFree(WgoBuildPointer pointer, Wgo original, BuildingDef def)
		{
			List<BuildSelectionCell> cells = GameAccess.GetCells(pointer);
			List<Collider> ghostColliders = GameAccess.GetBuildColliders(pointer);
			if (cells == null || cells.Count == 0)
			{
				return false;
			}
			int mask = GameAccess.GetOverlapMask(pointer);
			Physics.SyncTransforms();
			foreach (BuildSelectionCell cell in cells)
			{
				if (cell == null || cell is BuffCell)
				{
					continue;
				}
				int count = cell.OverlapBoxNonAlloc(overlap, mask);
				for (int i = 0; i < count; i++)
				{
					Collider hit = overlap[i];
					if (hit == null || (ghostColliders != null && ghostColliders.Contains(hit)))
					{
						continue;
					}
					int layer = hit.gameObject.layer;
					if (layer != 8 && layer != 19 && layer != 16)
					{
						continue;
					}
					if (hit.TryGetComponent<WorldZone>(out _) || hit.TryGetComponent<BuildArea>(out _) || PlacementBlockingArea.TryGet(hit, out _))
					{
						continue;
					}
					Wgo owner = hit.GetComponentInParent<Wgo>();
					if (owner != null && (owner == pointer.Target || owner == original || owner.Data == null || owner.Data.isTempObject || (def != null && def.ShouldIgnoreWgoGroupAsObstacle(owner.Data.Definition.wgoGroup))))
					{
						continue;
					}
					return false;
				}
			}
			return true;
		}
	}
}
