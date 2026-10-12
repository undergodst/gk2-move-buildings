using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GK2MoveBuildings
{
	/// <summary>
	/// Green see-through copies of the object on every free remembered spot (<see cref="HomeSpot"/>), the same
	/// hint the game shows on free carousel slots, so the player can see where the ghost may go.
	/// </summary>
	internal static class SpotHints
	{
		private class Hint
		{
			public Wgo View;

			public Bounds Bounds;

			public bool HasBounds;
		}

		private static readonly Dictionary<Vector3, Hint> hints = new Dictionary<Vector3, Hint>();

		private static string hintId;

		private static readonly Collider[] overlap = new Collider[32];

		/// <summary>Same layers the game checks when it decides whether a free-slot hint is shown (8, 16, 19).</summary>
		private const int ObstacleMask = 590080;

		public static void Update(BuildController controller)
		{
			WgoBuildPointer pointer = GameAccess.GetBuildPointer(controller)?.PointerObject as WgoBuildPointer;
			string id = null;
			WgoData moving = null;
			BuildingDef def = null;
			if (pointer != null && MoveSession.IsPlacementPointer(pointer))
			{
				moving = MoveSession.Original?.Data;
				id = moving?.id;
				MoveRules.TryGetPlaceDef(moving, out def);
			}
			else if (pointer != null && !MoveSession.IsCommittingPointer(pointer))
			{
				def = pointer.BuildData?.Definition;
				id = HomeSpot.SpotIdFor(def);
			}
			if (id != hintId)
			{
				Clear();
				hintId = id;
			}
			if (id == null)
			{
				return;
			}
			HashSet<Vector3> current = new HashSet<Vector3>();
			foreach ((Vector3 position, int rotationIndex) in HomeSpot.Candidates(id, moving))
			{
				if (!current.Add(position))
				{
					continue;
				}
				Hint hint = GetOrCreate(position, rotationIndex, id, pointer);
				if (hint != null)
				{
					bool show = !HomeSpot.IsGhostOn(pointer.Target, position, rotationIndex) && IsFree(hint, pointer, def);
					hint.View.gameObject.SetActive(show);
				}
			}
			List<Vector3> stale = new List<Vector3>();
			foreach (Vector3 position in hints.Keys)
			{
				if (!current.Contains(position))
				{
					stale.Add(position);
				}
			}
			foreach (Vector3 position in stale)
			{
				Destroy(hints[position]);
				hints.Remove(position);
			}
		}

		public static void Clear()
		{
			foreach (Hint hint in hints.Values)
			{
				Destroy(hint);
			}
			hints.Clear();
			hintId = null;
		}

		private static void Destroy(Hint hint)
		{
			if (hint.View != null)
			{
				Object.Destroy(hint.View.gameObject);
			}
		}

		/// <summary>Spawned the way BuildController.GetOrCreateFullCoverSoftHint spawns its slot hints.</summary>
		private static Hint GetOrCreate(Vector3 position, int rotationIndex, string id, WgoBuildPointer pointer)
		{
			if (hints.TryGetValue(position, out Hint existing) && existing.View != null)
			{
				return existing;
			}
			GameScene scene = MainGame.PlayerController?.CurrentGameScene;
			if (scene == null)
			{
				return null;
			}
			WgoData data = new WgoData(id, position, scene.Id)
			{
				isTempObject = true
			};
			WgoPartData ghostPart = pointer.Target?.MainWgoPart?.WgoPartData;
			if (ghostPart != null)
			{
				data.MainWgoPartData.variationId = ghostPart.variationId;
				data.MainWgoPartData.rotationIndex = rotationIndex != -1 ? rotationIndex : ghostPart.rotationIndex;
			}
			Wgo view = Wgo.Spawn(data, scene.transform, registerInChunkManagerIfStatic: true, ignoreChunkRegistration: true, applyDefaultWgoPartState: true);
			if (view == null)
			{
				return null;
			}
			view.UpdateChunkVisibility(isVisible: true);
			view.transform.position = position;
			Physics.SyncTransforms();
			Hint hint = new Hint { View = view };
			foreach (Collider collider in view.GetComponentsInChildren<Collider>(includeInactive: true))
			{
				if (collider != null && collider.gameObject.activeInHierarchy && collider.gameObject.layer == 19)
				{
					if (hint.HasBounds)
					{
						hint.Bounds.Encapsulate(collider.bounds);
					}
					else
					{
						hint.Bounds = collider.bounds;
						hint.HasBounds = true;
					}
				}
			}
			foreach (Collider collider in view.GetComponentsInChildren<Collider>(includeInactive: true))
			{
				if (collider != null)
				{
					collider.gameObject.SetActive(false);
				}
			}
			foreach (NavMeshCutBoxCustom cut in view.GetComponentsInChildren<NavMeshCutBoxCustom>(includeInactive: true))
			{
				if (cut != null)
				{
					cut.gameObject.SetActive(false);
				}
			}
			view.SetSelectionTint(Color.green, 0.35f);
			hints[position] = hint;
			return hint;
		}

		/// <summary>Mirror of BuildController.IsFullCoverSoftBuildAreaFree over the hint's own build footprint.</summary>
		private static bool IsFree(Hint hint, WgoBuildPointer pointer, BuildingDef def)
		{
			if (!hint.HasBounds)
			{
				return true;
			}
			Vector3 halfExtents = Vector3.Max(Vector3.zero, hint.Bounds.extents - VisualConsts.XYZ_STEP);
			int count = Physics.OverlapBoxNonAlloc(hint.Bounds.center, halfExtents, overlap, Quaternion.identity, ObstacleMask);
			for (int i = 0; i < count; i++)
			{
				Collider hit = overlap[i];
				if (hit == null || hit.TryGetComponent<BuildArea>(out _) || hit.TryGetComponent<ModuleSlotArea>(out _) || hit.TryGetComponent<WorldZone>(out _))
				{
					continue;
				}
				if (PlacementBlockingArea.TryGet(hit, out _))
				{
					if (PlacementBlockingArea.IsBlockingFor(hit, def))
					{
						return false;
					}
					continue;
				}
				Wgo owner = hit.GetComponentInParent<Wgo>();
				if (owner != null)
				{
					if (owner != pointer.Target && owner.Data != null && !owner.Data.isTempObject && (def == null || !def.ShouldIgnoreWgoGroupAsObstacle(owner.Data.Definition.wgoGroup)))
					{
						return false;
					}
					continue;
				}
				int layer = hit.gameObject.layer;
				if (layer == 8 || layer == 16)
				{
					return false;
				}
			}
			return true;
		}
	}

	[HarmonyPatch(typeof(BuildController), "UpdateFullCoverSoftBuildAreaHints")]
	internal static class SpotHintsUpdatePatch
	{
		private static void Postfix(BuildController __instance)
		{
			SpotHints.Update(__instance);
		}
	}

	[HarmonyPatch(typeof(BuildController), "ClearFullCoverSoftBuildAreaHints")]
	internal static class SpotHintsClearPatch
	{
		private static void Postfix()
		{
			SpotHints.Clear();
		}
	}
}
