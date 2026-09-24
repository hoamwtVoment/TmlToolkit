using System;

namespace TerrariaTmlToolkit
{
	/// <summary>
	/// Sizing rules for the tile render targets behind the unrestricted view
	/// zoom. Tile, wall and lighting work all grow with the visible world area,
	/// so the targets cover exactly the view that can be reached, with vanilla's
	/// padding kept as camera-movement slack. Free of Terraria types so the rules
	/// can be checked offline by test/TmlViewZoomTest.cs.
	/// </summary>
	internal static class TmlViewZoom
	{
		public const float AbsoluteMinimumZoom = 0.30F;
		public const float DefaultMinimumZoom = 0.50F;
		public const int MaximumTargetDimension = 8192;

		// Vanilla-sized targets are kept while they still leave this much
		// padding on each side of the zoomed-out view.
		private const int MinimumVanillaSlack = 64;
		// Each expanded size covers 20% more view width than the next smaller
		// one: few rebuilds while zooming, bounded spare area at rest.
		private const double TierStep = 1.2;
		// A size is only chosen if it covers 3% more than the current view.
		// The zoom keys move 2% per update, so one key press can never
		// trigger rebuilds on two consecutive updates.
		private const double TierLookahead = 1.03;

		/// <summary>
		/// Smallest zoom the view may reach: the requested limit, raised when
		/// size-capped targets could not cover it (that would only show
		/// unrendered borders while still paying for the largest targets).
		/// </summary>
		public static float GetEffectiveMinimumZoom(
			float requested,
			int width,
			int height,
			int vanillaPadding,
			bool canExpandTargets)
		{
			float minimum = Math.Max(
				AbsoluteMinimumZoom, Math.Min(1F, requested));
			int longest = Math.Max(width, height);
			if (!canExpandTargets || longest <= 0)
				return minimum;

			int room = MaximumPadding(width, height) - vanillaPadding - 1;
			float coverable = room > 0
				? longest / (float)(longest + 2 * room)
				: VanillaCoverageZoom(longest, vanillaPadding);
			return Math.Min(1F, Math.Max(minimum, coverable));
		}

		/// <summary>
		/// Zoom the render targets are sized for (1 = vanilla size). Never above
		/// <paramref name="zoom"/>, so the targets always cover the view. Sizes
		/// step geometrically down from <paramref name="minimumZoom"/>, so the
		/// fully zoomed-out view needs no spare area at all.
		/// </summary>
		public static float GetTargetTier(
			float zoom,
			float minimumZoom,
			int width,
			int height,
			int vanillaPadding)
		{
			int longest = Math.Max(1, Math.Max(width, height));
			float vanillaZoom = VanillaCoverageZoom(longest, vanillaPadding);
			if (zoom >= vanillaZoom)
				return 1F;

			double floor = Math.Max(
				AbsoluteMinimumZoom, Math.Min(vanillaZoom, minimumZoom));
			double view = 1.0 / Math.Max(floor, zoom);
			double fullView = 1.0 / floor;
			// Smallest size fullView / step^k that still covers the view plus
			// the lookahead; the full view itself is always the largest size.
			int steps = Math.Max(
				0, FloorSteps(fullView / (view * TierLookahead)));
			return (float)(floor * Math.Pow(TierStep, steps));
		}

		/// <summary>
		/// Main.offScreenRange that lets width x height targets cover the view at
		/// <paramref name="tierZoom"/>, keeping vanilla's padding as movement
		/// slack and no target larger than MaximumTargetDimension.
		/// </summary>
		public static int ComputePadding(
			int width,
			int height,
			float tierZoom,
			int vanillaPadding)
		{
			double boundedTier = Math.Max(
				AbsoluteMinimumZoom, Math.Min(1F, tierZoom));
			int extra = (int)Math.Ceiling(
				Math.Max(width, height) * (1.0 / boundedTier - 1.0) * 0.5);
			int maximum = Math.Max(vanillaPadding, MaximumPadding(width, height));
			return Math.Max(
				vanillaPadding, Math.Min(vanillaPadding + extra, maximum));
		}

		private static float VanillaCoverageZoom(int longest, int vanillaPadding)
		{
			int usable = Math.Max(0, vanillaPadding - MinimumVanillaSlack);
			return longest / (float)(longest + 2 * usable);
		}

		private static int MaximumPadding(int width, int height)
		{
			return Math.Min(
				(MaximumTargetDimension - width) / 2,
				(MaximumTargetDimension - height) / 2);
		}

		// Largest k with step^k <= ratio; the epsilon keeps an exact tier
		// boundary from rounding into the next larger size.
		private static int FloorSteps(double ratio)
		{
			return (int)Math.Floor(Math.Log(ratio) / Math.Log(TierStep) + 1e-4);
		}
	}
}
