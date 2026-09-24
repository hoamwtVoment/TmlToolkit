// Offline check of the render-target sizing rules in TmlViewZoom.cs.
//
// For common resolutions and every minimum-zoom setting it sweeps the zoom the
// way the game's zoom keys do (x1.02 per update), and verifies that the tile
// render targets always cover the zoomed-out view with movement slack, never
// exceed the size cap, and how many rebuilds a full zoom-out costs. The
// previous fixed-tier rules are measured alongside for comparison.
//
// Build and run from the repository root:
//   csc /nologo /out:%TEMP%\TmlViewZoomTest.exe test\TmlViewZoomTest.cs TmlViewZoom.cs
//   %TEMP%\TmlViewZoomTest.exe

using System;
using System.Collections.Generic;
using System.Globalization;
using TerrariaTmlToolkit;

internal static class TmlViewZoomTest
{
	private const int VanillaPadding = 192;
	private const int RequiredSlack = 60;
	private const int LayerTargets = 7;

	private static int Main()
	{
		System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
		int[][] screens = {
			new[] { 1280, 720 }, new[] { 1366, 768 }, new[] { 1600, 900 },
			new[] { 1920, 1080 }, new[] { 2560, 1080 }, new[] { 2560, 1440 },
			new[] { 3440, 1440 }, new[] { 3840, 2160 }, new[] { 1080, 1920 }
		};
		int failures = 0;
		double worstWaste = 0;
		int worstRebuilds = 0;
		for (int s = 0; s < screens.Length; s++) {
			int width = screens[s][0];
			int height = screens[s][1];
			for (int f = 30; f <= 100; f += 5) {
				float requested = f / 100F;
				float floor = TmlViewZoom.GetEffectiveMinimumZoom(
					requested, width, height, VanillaPadding, true);
				int rebuilds = 0;
				float lastTier = 1F;
				foreach (float zoom in ZoomSweep(floor)) {
					float tier = TmlViewZoom.GetTargetTier(
						zoom, floor, width, height, VanillaPadding);
					int padding = TmlViewZoom.ComputePadding(
						width, height, tier, VanillaPadding);
					if (Math.Abs(tier - lastTier) > 0.0001F) {
						rebuilds++;
						lastTier = tier;
					}
					double slack = Slack(width, height, padding, zoom);
					if (slack < RequiredSlack ||
						width + padding * 2 > TmlViewZoom.MaximumTargetDimension ||
						height + padding * 2 > TmlViewZoom.MaximumTargetDimension) {
						failures++;
						Console.WriteLine("FAIL {0}x{1} floor={2} zoom={3:0.0000} tier={4:0.0000} padding={5} slack={6:0.0}",
							width, height, requested, zoom, tier, padding, slack);
					}
					worstWaste = Math.Max(worstWaste,
						Area(width, height, padding) / Area(width, height, IdealPadding(width, height, zoom)));
				}
				worstRebuilds = Math.Max(worstRebuilds, rebuilds);
			}
		}

		Console.WriteLine("resolution   | new default (min {0:0.00})                  | old (min 0.30)",
			TmlViewZoom.DefaultMinimumZoom);
		Console.WriteLine("             | floor  target at floor   VRAM   rebuilds | target at 0.30   VRAM   rebuilds  uncovered zooms");
		for (int s = 0; s < screens.Length; s++) {
			int width = screens[s][0];
			int height = screens[s][1];
			float floor = TmlViewZoom.GetEffectiveMinimumZoom(
				TmlViewZoom.DefaultMinimumZoom, width, height, VanillaPadding, true);
			int padding = TmlViewZoom.ComputePadding(width, height,
				TmlViewZoom.GetTargetTier(floor, floor, width, height, VanillaPadding), VanillaPadding);
			int newRebuilds = CountRebuilds(floor, delegate(float zoom) {
				return TmlViewZoom.GetTargetTier(zoom, floor, width, height, VanillaPadding);
			});

			int oldPadding = OldPadding(width, height, OldTier(0.30F), VanillaPadding);
			int oldRebuilds = CountRebuilds(0.30F, OldTier);
			int oldUncovered = 0;
			foreach (float zoom in ZoomSweep(0.30F)) {
				if (Slack(width, height, OldPadding(width, height, OldTier(zoom), VanillaPadding), zoom) < 0)
					oldUncovered++;
			}
			Console.WriteLine("{0,4}x{1,-4}    | {2:0.00}   {3,5}x{4,-5}   {5,6}   {6,4}     | {7,5}x{8,-5} {9,6}   {10,4}      {11,4}",
				width, height, floor, width + padding * 2, height + padding * 2,
				Vram(width, height, padding), newRebuilds,
				width + oldPadding * 2, height + oldPadding * 2,
				Vram(width, height, oldPadding), oldRebuilds, oldUncovered);
		}
		Console.WriteLine();
		Console.WriteLine("worst spare target area (new) : {0:0.00}x of the ideal size", worstWaste);
		Console.WriteLine("most rebuilds for a full zoom-out (new, any setting): {0}", worstRebuilds);
		Console.WriteLine(failures == 0 ? "PASS: every zoom covered, no target over the size cap" : "FAILURES: " + failures);
		return failures == 0 ? 0 : 1;
	}

	// Zoom values reached while holding zoom-out from 1.0 to the floor and back.
	private static IEnumerable<float> ZoomSweep(float floor)
	{
		float zoom = 1F;
		while (zoom > floor) {
			yield return zoom;
			zoom = Math.Max(floor, zoom / 1.02F);
		}
		yield return floor;
		while (zoom < 1F) {
			zoom = Math.Min(1F, zoom * 1.02F);
			yield return zoom;
		}
	}

	private static int CountRebuilds(float floor, Func<float, float> tierOf)
	{
		int rebuilds = 0;
		float last = 1F;
		float zoom = 1F;
		while (zoom > floor) {
			zoom = Math.Max(floor, zoom / 1.02F);
			float tier = tierOf(zoom);
			if (Math.Abs(tier - last) > 0.0001F) {
				rebuilds++;
				last = tier;
			}
		}
		return rebuilds;
	}

	// Smallest slack (px) between the zoomed view and the target edges.
	private static double Slack(int width, int height, int padding, float zoom)
	{
		double slackX = (width + 2.0 * padding - width / zoom) / 2;
		double slackY = (height + 2.0 * padding - height / zoom) / 2;
		return Math.Min(slackX, slackY);
	}

	private static int IdealPadding(int width, int height, float zoom)
	{
		return VanillaPadding + (int)Math.Ceiling(Math.Max(0, Math.Max(width, height) * (1.0 / zoom - 1.0) * 0.5));
	}

	private static double Area(int width, int height, int padding)
	{
		return (width + 2.0 * padding) * (height + 2.0 * padding);
	}

	private static string Vram(int width, int height, int padding)
	{
		return (Area(width, height, padding) * 4 * LayerTargets / (1024.0 * 1024 * 1024)).ToString("0.00") + "GB";
	}

	// The previous rules, kept for comparison.
	private static float OldTier(float zoom)
	{
		if (zoom >= 0.875F) return 1F;
		if (zoom >= 0.625F) return 0.75F;
		if (zoom >= 0.45F) return 0.50F;
		if (zoom >= 0.35F) return 0.40F;
		return 0.30F;
	}

	private static int OldPadding(int width, int height, float zoomTier, int vanillaPadding)
	{
		float boundedTier = Math.Max(0.30F, Math.Min(1F, zoomTier));
		double extraScale = 1.0 / boundedTier - 1.0;
		int desiredExtra = (int)Math.Ceiling(Math.Max(width, height) * extraScale * 0.5);
		int maximumPadding = Math.Min((8192 - width) / 2, (8192 - height) / 2);
		maximumPadding = Math.Max(vanillaPadding, maximumPadding);
		return Math.Max(vanillaPadding, Math.Min(vanillaPadding + desiredExtra, maximumPadding));
	}
}
