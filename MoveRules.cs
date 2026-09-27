using System.Collections.Generic;
using LazyBearTechnology;
using UnityEngine;

namespace GK2MoveBuildings
{
	internal static class MoveRules
	{
		public static bool TryGetPlaceDef(WgoData data, out BuildingDef def)
		{
			def = null;
			if (data == null || GameBalance.Me == null || !GameBalance.Me.buildableWgos.TryGetValue(data.id, out def) || def == null)
			{
				return false;
			}
			// Fight buildings are registered in militaryBaseData and only exist during fights; leave them alone.
			return def.buildingMode == BuildingDef.BuildingMode.Place || def.buildingMode == BuildingDef.BuildingMode.ConveyorPlace;
		}

		private static readonly HashSet<string> diagnosed = new HashSet<string>();

		/// <summary>Once per object id: which building definitions mention it, to learn how such objects are built.</summary>
		public static void LogBuildDefsFor(string wgoId)
		{
			if (string.IsNullOrEmpty(wgoId) || !diagnosed.Add(wgoId) || GameBalance.Me == null)
			{
				return;
			}
			int cut = wgoId.LastIndexOf('_');
			string stem = cut > 0 ? wgoId.Substring(0, cut) : wgoId;
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			foreach (BuildingDef def in GameBalance.Me.buildingDefs)
			{
				if (def == null)
				{
					continue;
				}
				bool related = def.wgoId == wgoId || def.customWgoPlacePreview == wgoId || (def.id != null && def.id.Contains(stem)) || (def.wgoId != null && def.wgoId.Contains(stem));
				if (related)
				{
					sb.Append($"\n    def [{def.id}] wgo [{def.wgoId}] mode {def.buildingMode} area {def.chooseCustomBuildAreaType}/{def.customBuildAreaId} preview [{def.customWgoPlacePreview}] builtIn [{string.Join(",", def.buildsIn)}]");
				}
			}
			Plugin.Log.LogInfo($"Diagnostics for unmovable [{wgoId}]:" + (sb.Length > 0 ? sb.ToString() : " no related building definitions"));
		}

		public static bool CanMove(Wgo wgo, out string reason)
		{
			reason = null;
			if (wgo == null || wgo.Data == null || wgo.IsDespawning)
			{
				reason = "object is gone";
				return false;
			}
			WgoData data = wgo.Data;
			if (!TryGetPlaceDef(data, out _))
			{
				reason = $"[{data.id}] has no Place/ConveyorPlace building definition";
				return false;
			}
			if (!wgo.IsBuildRemovable())
			{
				reason = $"[{data.id}] is locked by the game (not removable)";
				return false;
			}
			if (data.CraftComponent.IsDestroyingCraftActive)
			{
				reason = $"[{data.id}] is being dismantled";
				return false;
			}
			if (data.CraftComponent.IsPreFinishHeld)
			{
				// The view would despawn late and de-initialize the data after we re-added it.
				reason = $"[{data.id}] is finishing a craft, try again in a moment";
				return false;
			}
			return true;
		}

		/// <summary>Extensions that physically sit in the parent's build-area slots and must travel with it.</summary>
		public static List<WgoData> GetSlotExtensions(WgoData parent)
		{
			List<WgoData> result = new List<WgoData>();
			WorldData worldData = MainGame.Instance.GameSave.worldData;
			foreach (SGuid extensionId in parent.AttachedWorkbenchExtensions)
			{
				WgoData extension = worldData.GetWgoData(extensionId);
				if (extension != null && GameBalance.Me.buildableWgos.TryGetValue(extension.id, out BuildingDef def) && def.chooseCustomBuildAreaType == BuildingDef.BuildAreaChoosingType.FullCoverSoft)
				{
					result.Add(extension);
				}
			}
			return result;
		}

		public static Wgo ToWgo(IBuildRemovable removable)
		{
			if (removable is Wgo wgo)
			{
				return wgo;
			}
			if (removable is Component component && component != null)
			{
				return component.GetComponentInParent<Wgo>();
			}
			return null;
		}
	}
}
