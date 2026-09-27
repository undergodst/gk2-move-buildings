using System.Collections.Generic;
using LazyBearTechnology;

namespace GK2MoveBuildings
{
	/// <summary>The only player-facing string: the build-menu tile name, keyed by the game's language ids (LLBase.languages).</summary>
	internal static class Localization
	{
		private static readonly Dictionary<string, string> moveTile = new Dictionary<string, string>
		{
			{ "en", "Move" },
			{ "ru", "Переместить" },
			{ "uk-ua", "Перемістити" },
			{ "de", "Versetzen" },
			{ "fr", "Déplacer" },
			{ "es", "Mover" },
			{ "es-mx", "Mover" },
			{ "pt-br", "Mover" },
			{ "it", "Sposta" },
			{ "pl", "Przenieś" },
			{ "tr", "Taşı" },
			{ "ja", "移動" },
			{ "ko", "이동" },
			{ "zh_cn", "移动" },
			{ "zh_cht", "移動" },
			{ "th", "ย้าย" },
			{ "vn", "Di chuyển" }
		};

		public static string MoveTileName
		{
			get
			{
				string lang = LLBase.CurrentLang ?? "en";
				return moveTile.TryGetValue(lang, out string name) ? name : moveTile["en"];
			}
		}
	}
}
