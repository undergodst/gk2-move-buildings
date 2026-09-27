using UnityEngine;

namespace GK2MoveBuildings
{
	/// <summary>Selection step: reuses RemovePointer's hover/highlight logic, but a click picks the building instead of removing it.</summary>
	public class MovePointer : RemovePointer
	{
		public override void SetupSelectionCells(BuildSelectionCell[] prefabCells, Transform parent)
		{
			GameAccess.ClearRemoveSelection();
			GameAccess.SetRemoveTintHex(this, "#2b8cca");
			MoveCursor.EnsureRegistered();
			MoveCursor.SettingUpMovePointer = true;
			try
			{
				base.SetupSelectionCells(prefabCells, parent);
			}
			finally
			{
				MoveCursor.SettingUpMovePointer = false;
			}
			MoveSession.OnSelectionStarted();
		}

		public override bool TryDoBuildAction()
		{
			Wgo wgo = MoveRules.ToWgo(GameAccess.GetRemoveSelection());
			if (wgo != null)
			{
				MoveSession.RequestPlacement(wgo);
			}
			// Never report success: that would play the build sound and fire the BuildBuilding quest trigger.
			return false;
		}

		public override void OnPointerDisable()
		{
			base.OnPointerDisable();
			MoveSession.OnSelectionPointerDisabled();
		}
	}
}
