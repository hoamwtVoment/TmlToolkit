// Differential harness for the vanilla backend.
//
// VanillaGameApi binds to Terraria purely by reflection, so it can be driven
// against small stand-ins for the Terraria types it looks up. This program
// runs a fixed script of game ticks, inventory operations and map reveal
// steps, paints InventorySlotGrid off-screen, optionally decodes real item
// XNB files, and prints every observable result. Build it from two revisions
// of the sources and diff the output: a behaviour-preserving change must print
// exactly the same trace.
//
// Build from this directory (x86 because XNA's content reader is x86-only;
// the output must be named Terraria.exe so VanillaGameApi.Connect finds it):
//   csc /nologo /target:exe /platform:x86 /out:Terraria.exe
//       /reference:System.Drawing.dll /reference:System.Windows.Forms.dll
//       ReflectionHarness.cs ..\VanillaGameApi.cs
//       ..\VanillaXnbTextureDecoder.cs ..\InventorySlotGrid.cs
// Run (the Terraria directory is optional and enables the XNB checks):
//   Terraria.exe "D:\steam\steamapps\common\Terraria" > trace.txt

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria1456Toolkit;
using GameMain = Terraria.Main;

[assembly: AssemblyVersion("1.4.5.6")]

internal static class HarnessLog
{
	private static readonly StringBuilder Output = new StringBuilder();

	public static string Text
	{
		get { return Output.ToString(); }
	}

	public static void Line(string text)
	{
		Output.AppendLine(text);
	}

	public static void Call(string text)
	{
		Output.Append("  call ").AppendLine(text);
	}

	public static string F(float value)
	{
		return value.ToString("R", CultureInfo.InvariantCulture);
	}
}

namespace Microsoft.Xna.Framework
{
	public struct Vector2
	{
		public float X;
		public float Y;

		public Vector2(float x, float y)
		{
			X = x;
			Y = y;
		}
	}
}

namespace Terraria1456Toolkit
{
	// Same shape as the definition in TrainerForm.cs, which is not compiled here.
	internal sealed class InventoryEntry
	{
		public int Slot { get; set; }
		public int Id { get; set; }
		public string Name { get; set; }
		public int Stack { get; set; }
		public int Prefix { get; set; }
	}
}

namespace Terraria
{
	public static class Main
	{
		public static object instance = new object();
		public static bool gameMenu;
		public static Player[] player;
		public static int myPlayer;
		public static int netMode;
		public static bool[] debuff = new bool[400];
		public static Vector2 screenPosition;
		public static int screenWidth;
		public static int screenHeight;
		public static float ForcedMinimumZoom;
		public static float GameZoomTarget;
		public static int maxTilesX;
		public static int maxTilesY;
		public static Terraria.Map.WorldMap Map;
		public static bool refreshMap;
		public static bool resetMapFull;

		public static uint GameUpdateCount { get; set; }

		public static event Action OnTickForThirdPartySoftwareOnly;

		public static void RaiseTick()
		{
			Action handler = OnTickForThirdPartySoftwareOnly;
			if (handler != null)
				handler();
		}
	}

	public static class Lighting
	{
		public static int Calls;
		public static double Checksum;

		public static void AddLight(int i, int j, float r, float g, float b)
		{
			Calls++;
			Checksum += i * 3.0 + j * 7.0 + r + g + b;
		}
	}

	public static class Collision
	{
		// A flat floor at y = 1000 and a two-tile block spanning x 1700..1732.
		public static bool SolidCollision(Vector2 Position, int Width, int Height)
		{
			float left = Position.X;
			float right = Position.X + Width;
			float top = Position.Y;
			float bottom = Position.Y + Height;
			if (bottom > 1000f)
				return true;
			return left < 1732f && right > 1700f && top < 1000f && bottom > 968f;
		}
	}

	public static class NetMessage
	{
		public static void SendData(int msgType, int remoteClient = -1,
			int ignoreClient = -1, object text = null, int number = 0,
			float number2 = 0f, float number3 = 0f, float number4 = 0f,
			int number5 = 0, int number6 = 0, int number7 = 0)
		{
			HarnessLog.Call("SendData(" + msgType + "," + remoteClient + "," +
				ignoreClient + "," + (text ?? "null") + "," + number + "," +
				HarnessLog.F(number2) + "," + HarnessLog.F(number3) + "," +
				HarnessLog.F(number4) + "," + number5 + "," + number6 + "," +
				number7 + ")");
		}
	}

	public sealed class Mount
	{
		public bool Active { get; set; }
	}

	public sealed class Item
	{
		public int type;
		public int stack;
		public int prefix;
		public int maxStack;
		public int damage;
		public float knockBack;
		public int useTime;
		public int useAnimation;
		public float scale;
		public float shootSpeed;
		public int shoot;
		public int useAmmo;
		public int pick;
		public int axe;
		public int hammer;
		public int fishingPole;
		public int defense;
		public int crit;
		public int mana;
		public int healLife;
		public int healMana;
		public bool autoReuse;

		public string Name
		{
			get { return type <= 0 ? string.Empty : "Item" + type; }
		}

		public bool IsAir
		{
			get { return type <= 0 || stack <= 0; }
		}

		public void SetDefaults(int Type, bool noMatCheck = false)
		{
			type = Type;
			stack = 1;
			prefix = 0;
			maxStack = Type % 2 == 0 ? 9999 : 1;
			damage = Type * 3;
			knockBack = Type / 10f;
			useTime = 20 + Type % 7;
			useAnimation = useTime;
			scale = 1f;
			shootSpeed = Type % 5;
			shoot = Type % 3 == 0 ? Type : 0;
			useAmmo = 0;
			pick = Type % 11 == 0 ? 100 : 0;
			axe = 0;
			hammer = 0;
			fishingPole = 0;
			defense = Type % 4;
			crit = 4;
			mana = 0;
			healLife = 0;
			healMana = 0;
			autoReuse = Type % 2 == 1;
		}

		public void TurnToAir(bool fullReset = false)
		{
			type = 0;
			stack = 0;
			prefix = 0;
		}

		public override string ToString()
		{
			return "{type=" + type + " stack=" + stack + " prefix=" + prefix +
				" max=" + maxStack + " dmg=" + damage + " kb=" +
				HarnessLog.F(knockBack) + " use=" + useTime + "/" + useAnimation +
				" scale=" + HarnessLog.F(scale) + " shootSpeed=" +
				HarnessLog.F(shootSpeed) + " shoot=" + shoot + " def=" + defense +
				" crit=" + crit + " auto=" + autoReuse + "}";
		}
	}

	public sealed class Player
	{
		public bool active;
		public string name;
		public int whoAmI;
		public Item[] inventory;
		public int selectedItem;
		public bool creativeGodMode;
		public bool immune;
		public bool immuneNoBlink;
		public int immuneTime;
		public int statLife;
		public int statLifeMax2;
		public int statMana;
		public int statManaMax2;
		public int breath;
		public int breathMax;
		public int potionDelay;
		public bool noKnockback;
		public bool noFallDmg;
		public bool lavaImmune;
		public float moveSpeed;
		public float maxRunSpeed;
		public float accRunSpeed;
		public float runAcceleration;
		public float jumpSpeedBoost;
		public float wingTime;
		public int wingTimeMax;
		public int rocketTime;
		public int rocketTimeMax;
		public int[] buffType = new int[22];
		public bool controlLeft;
		public bool controlRight;
		public bool controlJump;
		public Vector2 velocity;
		public Vector2 position;
		public int width = 20;
		public int height = 42;
		public Mount mount = new Mount();
		public bool dead;
		public int respawnTimer;
		public int jump;
		public bool controlUseItem;
		public bool releaseUseItem;
		public int itemAnimation;
		public int itemTime;
		public int reuseDelay;
		public bool justJumped;
		public float gravDir = 1f;

		public Item HeldItem
		{
			get { return inventory[selectedItem]; }
		}

		public void DelBuff(int b)
		{
			HarnessLog.Call("DelBuff(" + whoAmI + "," + b + ")");
			buffType[b] = 0;
		}

		public object GetItemSource_Misc(int context)
		{
			return "source" + context;
		}

		public void QuickSpawnItem(object source, int item, int stack)
		{
			HarnessLog.Call("QuickSpawnItem(" + source + "," + item + "," + stack + ")");
		}
	}
}

namespace Terraria.Map
{
	public sealed class WorldMap
	{
		public int Updates;
		public long Checksum;

		public void Update(int x, int y, byte light)
		{
			Updates++;
			Checksum = Checksum * 31 + x * 7919 + y * 104729 + light;
		}
	}
}

namespace Terraria.GameInput
{
	public sealed class TriggersSet
	{
		public bool ViewZoomIn { get; set; }
		public bool ViewZoomOut { get; set; }
	}

	public sealed class TriggersPack
	{
		public TriggersSet Current = new TriggersSet();
	}

	public static class PlayerInput
	{
		public static TriggersPack Triggers = new TriggersPack();
	}
}

namespace Terraria.Testing
{
	public static class DebugOptions
	{
		public static bool devLightTilesCheat;
	}
}

namespace Terraria.ID
{
	public static class ItemID
	{
		public static readonly short Count = 6000;

		public static class Sets
		{
			public static int[] TextureCopyLoad = Enumerable.Repeat(-1, 6000).ToArray();
		}
	}
}

namespace Terraria.GameContent.Creative
{
	public sealed class CreativePowerManager
	{
		public static readonly CreativePowerManager Instance = new CreativePowerManager();
		private readonly Dictionary<Type, object> _powers = new Dictionary<Type, object>();

		public T GetPower<T>() where T : class, new()
		{
			object power;
			if (!_powers.TryGetValue(typeof(T), out power)) {
				power = new T();
				_powers[typeof(T)] = power;
			}
			return (T)power;
		}
	}

	public static class CreativePowers
	{
		public sealed class GodmodePower
		{
			private readonly bool[] _enabled = new bool[256];

			public void SetEnabledState(int playerIndex, bool state)
			{
				HarnessLog.Call("Godmode.SetEnabledState(" + playerIndex + "," + state + ")");
				_enabled[playerIndex] = state;
			}

			public bool IsEnabledForPlayer(int playerIndex)
			{
				return _enabled[playerIndex];
			}
		}
	}
}

internal static class Program
{
	private static VanillaGameApi _api;

	[STAThread]
	private static int Main(string[] args)
	{
		Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
		Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
		try {
			SetUpWorld();
			_api = VanillaGameApi.Connect();
			_api.AttachHooks();
			HarnessLog.Line("connected version=" + _api.Version + " maxItem=" +
				_api.MaxItemType + " hooked=" + _api.IsGameTickHooked);
			RunCheatScript();
			RunInventoryScript();
			RunMapRevealScript();
			_api.DetachHooks();
			Frame("after DetachHooks (no callbacks expected)");
			RunSlotGridScript();
			if (args.Length > 0)
				RunTextureScript(args[0]);
		}
		catch (Exception ex) {
			HarnessLog.Line("EXCEPTION " + ex);
		}
		Console.Write(HarnessLog.Text);
		return 0;
	}

	private static Player Local
	{
		get { return GameMain.player[0]; }
	}

	private static void SetUpWorld()
	{
		GameMain.maxTilesX = 400;
		GameMain.maxTilesY = 300;
		GameMain.Map = new Terraria.Map.WorldMap();
		GameMain.screenPosition = new Vector2(1600f, 800f);
		GameMain.screenWidth = 800;
		GameMain.screenHeight = 600;
		GameMain.ForcedMinimumZoom = 1.5f;
		GameMain.GameZoomTarget = 1f;
		GameMain.debuff[20] = true;
		GameMain.debuff[24] = true;

		Player local = NewPlayer(0, "Local");
		local.position = new Vector2(1679f, 958f);
		local.buffType[0] = 20;
		local.buffType[1] = 1;
		local.buffType[2] = 24;
		local.statLifeMax2 = 500;
		local.statManaMax2 = 200;
		local.breathMax = 200;
		local.wingTimeMax = 120;
		local.rocketTimeMax = 7;
		local.moveSpeed = 1f;
		local.maxRunSpeed = 3f;
		local.accRunSpeed = 3f;
		local.runAcceleration = 0.08f;
		Player remote = NewPlayer(1, "Remote");
		remote.selectedItem = 2;
		Player idle = NewPlayer(2, "Idle");
		idle.active = false;
		GameMain.player = new Player[] { local, remote, idle };
	}

	private static Player NewPlayer(int index, string name)
	{
		Player player = new Player();
		player.active = true;
		player.name = name;
		player.whoAmI = index;
		player.inventory = new Item[59];
		for (int slot = 0; slot < player.inventory.Length; slot++) {
			Item item = new Item();
			if (slot % 4 == 0 || slot == 2) {
				item.SetDefaults(10 + slot + index * 100);
				item.stack = 1 + slot;
				item.prefix = slot % 3;
			}
			player.inventory[slot] = item;
		}
		return player;
	}

	// Terraria raises the third-party tick near the start and the end of
	// Main.DoUpdate, and GameUpdateCount advances in between.
	private static void Frame(string label)
	{
		GameMain.RaiseTick();
		GameMain.GameUpdateCount++;
		GameMain.RaiseTick();
		Dump(label);
	}

	private static void ApplyCheats(bool enabled)
	{
		_api.ApplyCheats(enabled, enabled, enabled, enabled, enabled, enabled,
			enabled, enabled, enabled, enabled, 3.5f, enabled, 2.5f, enabled,
			enabled, 3, enabled, enabled, enabled, enabled);
	}

	private static void RunCheatScript()
	{
		ApplyCheats(true);
		Local.statLife = 1;
		Frame("cheats on, idle");

		Local.statLife = 1;
		Local.controlRight = true;
		Frame("run right into a two-tile step");

		Local.controlRight = false;
		Local.velocity = new Vector2(0f, -5.01f);
		Local.justJumped = true;
		Local.controlJump = true;
		Local.jump = 15;
		Frame("jump start");

		Local.justJumped = false;
		Local.velocity = new Vector2(0f, -5.01f);
		Local.jump = 10;
		Frame("jump held");

		Local.controlJump = false;
		Local.controlUseItem = true;
		Local.itemAnimation = 10;
		Local.itemTime = 10;
		Local.reuseDelay = 5;
		Frame("attack pressed");

		Local.controlUseItem = false;
		Local.dead = true;
		Local.respawnTimer = 300;
		Frame("dead");

		Local.dead = false;
		Terraria.GameInput.PlayerInput.Triggers.Current.ViewZoomIn = true;
		Frame("zoom in");

		Terraria.GameInput.PlayerInput.Triggers.Current.ViewZoomIn = false;
		Terraria.GameInput.PlayerInput.Triggers.Current.ViewZoomOut = true;
		Frame("zoom out");

		Terraria.GameInput.PlayerInput.Triggers.Current.ViewZoomOut = false;
		Local.mount.Active = true;
		Local.controlRight = true;
		Local.velocity = new Vector2(0f, 0f);
		Frame("mounted, running right");

		Local.mount.Active = false;
		Local.controlRight = false;
		ApplyCheats(false);
		Frame("cheats off");
	}

	private static void RunInventoryScript()
	{
		DumpInventory("local inventory", _api.ReadInventory());

		_api.GiveItem(100, 5);
		_api.ReplaceSlot(3, 42, 99999);
		_api.ReplaceSlot(4, 43, 50);
		_api.ClearSlot(8);
		Frame("give / replace / clear");

		_api.RequestItemAttributes(3, delegate(ItemAttributeSnapshot snapshot, string error) {
			DumpAttributes("requested slot 3", snapshot, error);
		});
		Dictionary<string, object> values = new Dictionary<string, object>();
		values["damage"] = 77;
		values["scale"] = 1.5m;
		values["knockBack"] = 2.25m;
		values["autoReuse"] = true;
		values["type"] = 42;
		values["notAField"] = 1;
		_api.WriteItemAttributes(3, values);
		Dictionary<string, object> retype = new Dictionary<string, object>();
		retype["type"] = 44;
		retype["damage"] = 5;
		_api.WriteItemAttributes(12, retype);
		Dictionary<string, object> air = new Dictionary<string, object>();
		air["type"] = 0;
		_api.WriteItemAttributes(16, air);
		Frame("attribute writes");

		Dictionary<string, object> invalid = new Dictionary<string, object>();
		invalid["type"] = 999999;
		_api.WriteItemAttributes(20, invalid);
		Frame("invalid attribute write");

		GameMain.netMode = 1;
		_api.ReplaceSlot(9, 10, 3);
		Frame("multiplayer replace");
		GameMain.netMode = 0;

		DumpAttributes("direct slot 3", _api.ReadItemAttributes(3), null);
		DumpInventory("local inventory after edits", _api.ReadInventory());
		foreach (RemoteInventorySnapshot remote in _api.GetRemoteInventories())
			DumpInventory("remote P" + remote.PlayerIndex + " " + remote.PlayerName, remote.Items);
		foreach (RemoteHeldSnapshot held in _api.GetRemoteHeldItems()) {
			HarnessLog.Line("remote held P" + held.PlayerIndex + " " + held.PlayerName +
				" " + held.ItemType + " " + held.ItemName + " x" + held.Stack);
		}
		try {
			_api.ReplaceSlot(59, 1, 1);
		}
		catch (Exception ex) {
			HarnessLog.Line("slot 59 rejected: " + ex.GetType().Name);
		}
	}

	private static void RunMapRevealScript()
	{
		_api.RequestMapReveal();
		for (int i = 0; i < 12 && (i == 0 || _api.IsMapRevealActive); i++)
			Frame("map reveal step " + i);

		GameMain.refreshMap = false;
		GameMain.resetMapFull = false;
		_api.RequestMapReveal();
		Frame("second reveal started");
		_api.CancelMapReveal();
		Frame("second reveal cancelled");
	}

	private static void RunSlotGridScript()
	{
		Bitmap icon = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
		using (Graphics g = Graphics.FromImage(icon))
			g.FillEllipse(Brushes.OrangeRed, 1, 1, 14, 14);

		List<InventoryEntry> entries = new List<InventoryEntry>();
		for (int slot = 0; slot <= 58; slot += 3) {
			entries.Add(new InventoryEntry {
				Slot = slot, Id = 100 + slot, Name = "Item" + (100 + slot),
				Stack = slot % 5 == 0 ? 1 : slot * 7, Prefix = slot % 4
			});
		}

		int changes = 0;
		using (InventorySlotGrid grid = new InventorySlotGrid()) {
			grid.Size = new Size(640, 300);
			grid.IconProvider = delegate(int id) { return id % 2 == 0 ? icon : null; };
			grid.SelectedSlotChanged += delegate { changes++; };
			grid.SetItems(entries);
			grid.SelectedSlot = 5;
			HarnessLog.Line("slot grid (small ids) " + Render(grid));

			grid.Size = new Size(380, 220);
			HarnessLog.Line("slot grid (compact ids) " + Render(grid));

			InvokeProtected(grid, "OnMouseDown",
				new MouseEventArgs(MouseButtons.Left, 1, 60, 80, 0));
			HarnessLog.Line("after click selected=" + grid.SelectedSlot + " changes=" + changes);
			InvokeProtected(grid, "OnKeyDown", new KeyEventArgs(Keys.Right));
			HarnessLog.Line("after right selected=" + grid.SelectedSlot + " changes=" + changes);
			InvokeProtected(grid, "OnKeyDown", new KeyEventArgs(Keys.Up));
			HarnessLog.Line("after up selected=" + grid.SelectedSlot + " changes=" + changes);
			grid.SelectionEnabled = false;
			InvokeProtected(grid, "OnKeyDown", new KeyEventArgs(Keys.Down));
			HarnessLog.Line("disabled selected=" + grid.SelectedSlot + " changes=" + changes);
		}
		icon.Dispose();
	}

	private static void InvokeProtected(Control control, string name, EventArgs args)
	{
		MethodInfo method = typeof(Control).GetMethod(name,
			BindingFlags.Instance | BindingFlags.NonPublic, null,
			new Type[] { args.GetType() }, null);
		method.Invoke(control, new object[] { args });
	}

	private static string Render(Control control)
	{
		using (Bitmap bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb))
		using (Graphics graphics = Graphics.FromImage(bitmap)) {
			graphics.Clear(control.BackColor);
			InvokeProtected(control, "OnPaint", new PaintEventArgs(
				graphics, new System.Drawing.Rectangle(0, 0, control.Width, control.Height)));
			return control.Width + "x" + control.Height + " " + PixelHash(bitmap);
		}
	}

	private static void RunTextureScript(string terrariaDirectory)
	{
		int[] ids = { 1, 2, 8, 29, 757, 3063, 4956, 5000, 999999 };
		foreach (int id in ids) {
			try {
				using (Bitmap bitmap = VanillaXnbTextureDecoder.DecodeItemTexture(terrariaDirectory, id))
					HarnessLog.Line("xnb " + id + " " + bitmap.Width + "x" + bitmap.Height + " " + PixelHash(bitmap));
			}
			catch (Exception ex) {
				HarnessLog.Line("xnb " + id + " " + ex.GetType().Name);
			}
		}
	}

	private static string PixelHash(Bitmap bitmap)
	{
		BitmapData data = bitmap.LockBits(
			new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
			ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
		try {
			byte[] row = new byte[bitmap.Width * 4];
			using (SHA256 sha = SHA256.Create()) {
				for (int y = 0; y < bitmap.Height; y++) {
					Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)data.Stride * y), row, 0, row.Length);
					sha.TransformBlock(row, 0, row.Length, null, 0);
				}
				sha.TransformFinalBlock(new byte[0], 0, 0);
				return BitConverter.ToString(sha.Hash, 0, 8).Replace("-", string.Empty);
			}
		}
		finally {
			bitmap.UnlockBits(data);
		}
	}

	private static void Dump(string label)
	{
		HarnessLog.Line("== " + label);
		StringBuilder line = new StringBuilder("  local");
		foreach (FieldInfo field in typeof(Player).GetFields(BindingFlags.Public | BindingFlags.Instance)
			.OrderBy(f => f.Name, StringComparer.Ordinal)) {
			if (field.Name == "inventory" || field.Name == "name")
				continue;
			line.Append(' ').Append(field.Name).Append('=').Append(Format(field.GetValue(Local)));
		}
		HarnessLog.Line(line.ToString());
		HarnessLog.Line("  world zoomMin=" + HarnessLog.F(GameMain.ForcedMinimumZoom) +
			" zoom=" + HarnessLog.F(GameMain.GameZoomTarget) +
			" devLight=" + Terraria.Testing.DebugOptions.devLightTilesCheat +
			" lights=" + Lighting.Calls + "/" + Lighting.Checksum.ToString("R", CultureInfo.InvariantCulture) +
			" map=" + GameMain.Map.Updates + "/" + GameMain.Map.Checksum +
			" refresh=" + GameMain.refreshMap + "/" + GameMain.resetMapFull);
		HarnessLog.Line("  api ready=" + _api.IsWorldReady +
			" reveal=" + _api.IsMapRevealActive + "/" + _api.IsMapRevealCancelling +
			" " + _api.MapRevealDone + "/" + _api.MapRevealTotal +
			" actionError=" + (_api.LastGameActionError ?? "null") +
			" threadError=" + _api.LastGameThreadError);
	}

	private static void DumpInventory(string label, IEnumerable<InventoryEntry> entries)
	{
		StringBuilder line = new StringBuilder(label + ":");
		foreach (InventoryEntry entry in entries) {
			if (entry.Id != 0)
				line.Append(' ').Append(entry.Slot).Append(':').Append(entry.Id)
					.Append('x').Append(entry.Stack).Append('p').Append(entry.Prefix)
					.Append('"').Append(entry.Name).Append('"');
		}
		HarnessLog.Line(line.ToString());
	}

	private static void DumpAttributes(string label, ItemAttributeSnapshot snapshot, string error)
	{
		if (snapshot == null) {
			HarnessLog.Line(label + ": error=" + error);
			return;
		}
		StringBuilder line = new StringBuilder(label + ": slot=" + snapshot.Slot);
		foreach (string key in snapshot.SupportedFields.OrderBy(k => k, StringComparer.Ordinal)) {
			object value;
			snapshot.Values.TryGetValue(key, out value);
			line.Append(' ').Append(key).Append('=').Append(Format(value));
		}
		HarnessLog.Line(line.ToString());
	}

	private static string Format(object value)
	{
		if (value == null)
			return "null";
		if (value is float)
			return HarnessLog.F((float)value);
		if (value is Vector2) {
			Vector2 vector = (Vector2)value;
			return "(" + HarnessLog.F(vector.X) + "," + HarnessLog.F(vector.Y) + ")";
		}
		if (value is Mount)
			return "mount:" + ((Mount)value).Active;
		int[] numbers = value as int[];
		if (numbers != null)
			return "[" + string.Join(",", numbers.Select(n => n.ToString(CultureInfo.InvariantCulture)).ToArray()) + "]";
		return Convert.ToString(value, CultureInfo.InvariantCulture);
	}
}
