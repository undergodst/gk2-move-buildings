using UnityEngine;

namespace GK2MoveBuildings
{
	/// <summary>Four-way arrow drawn at runtime so the mod ships as a single DLL.</summary>
	internal static class MoveIcon
	{
		public const string FileName = "move_icon.png";

		private const int Size = 64;

		private static Sprite sprite;

		public static Sprite Get(Sprite fallback)
		{
			if (sprite != null)
			{
				return sprite;
			}
			try
			{
				Texture2D custom = PngLoader.Load(FileName);
				sprite = custom != null ? ToSprite(custom) : Create();
			}
			catch (System.Exception ex)
			{
				Plugin.Log.LogWarning("Could not draw move icon, keeping the remove icon: " + ex.Message);
				return fallback;
			}
			return sprite;
		}

		private static Sprite Create()
		{
			Texture2D texture = new Texture2D(Size, Size, TextureFormat.RGBA32, mipChain: false)
			{
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp
			};
			Color32[] pixels = new Color32[Size * Size];
			Color32 outline = new Color32(40, 32, 28, 255);
			Color32 fill = new Color32(236, 222, 190, 255);
			Paint(pixels, outline, 3f);
			Paint(pixels, fill, 0f);
			texture.SetPixels32(pixels);
			texture.Apply();
			texture.hideFlags = HideFlags.HideAndDontSave;
			return ToSprite(texture);
		}

		private static Sprite ToSprite(Texture2D texture)
		{
			// Game icons are 1:1 pixel art; no smoothing when the UI scales them up.
			texture.filterMode = FilterMode.Point;
			Sprite result = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 50f);
			result.name = "gk2_move_icon";
			result.hideFlags = HideFlags.HideAndDontSave;
			return result;
		}

		private static void Paint(Color32[] pixels, Color32 color, float grow)
		{
			const float c = (Size - 1) / 2f;
			const float shaft = 4f;
			const float headLength = 12f;
			const float headHalfWidth = 11f;
			const float reach = 28f;
			for (int y = 0; y < Size; y++)
			{
				for (int x = 0; x < Size; x++)
				{
					float dx = x - c;
					float dy = y - c;
					if (InArrowCross(dx, dy, shaft + grow, headLength + grow, headHalfWidth + grow, reach + grow))
					{
						pixels[y * Size + x] = color;
					}
				}
			}
		}

		/// <summary>Plus-shaped shaft with a triangular head at each of the four ends.</summary>
		private static bool InArrowCross(float dx, float dy, float shaftHalf, float headLength, float headHalfWidth, float reach)
		{
			float along = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
			float across = Mathf.Abs(dx) >= Mathf.Abs(dy) ? Mathf.Abs(dy) : Mathf.Abs(dx);
			if (along > reach)
			{
				return false;
			}
			float headStart = reach - headLength;
			if (along >= headStart)
			{
				float t = (reach - along) / headLength;
				return across <= headHalfWidth * t;
			}
			return across <= shaftHalf;
		}
	}
}
