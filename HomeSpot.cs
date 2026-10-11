using System.Collections.Generic;
using UnityEngine;

namespace GK2MoveBuildings
{
	/// <summary>
	/// Where a building stood before the mod first moved it. Objects the world starts with (a broken wheel you
	/// repair, for example) can sit where the game never lets you build, so once moved away neither the build
	/// menu nor the vanilla placement check would accept that spot again. The spot is kept in the object's own
	/// game resources, so it survives saving, and the ghost may always go back onto it.
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
		/// The ghost is on the home spot when it is in the original rotation and within half a build cell of it
		/// (objects placed by the level designers need not be on the build grid).
		/// </summary>
		public static bool IsGhostAtHome(WgoData original, Wgo ghost, out Vector3 home)
		{
			home = Vector3.zero;
			if (ghost == null || ghost.Data == null || original == null)
			{
				return false;
			}
			if (!TryGet(original, out home, out int homeRotation))
			{
				// Never moved yet: where it stands now is its home.
				home = original.Position;
				homeRotation = original.MainWgoPartData.rotationIndex;
			}
			int ghostRotation = ghost.MainWgoPart?.WgoPartData?.rotationIndex ?? -1;
			if (homeRotation != -1 && ghostRotation != -1 && ghostRotation != homeRotation)
			{
				return false;
			}
			Vector3 d = ghost.Data.Position - home;
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
