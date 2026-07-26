using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TerrariaTmlToolkit
{
	/// <summary>
	/// Terraria-style inventory selector. Slots 0-49 are the main 10x5
	/// inventory, 50-53 are coins, 54-57 are ammo, and 58 is the
	/// inventory mouse-item slot. Terraria's trash item is a separate field.
	/// </summary>
	internal sealed class TmlInventorySlotGrid : Control
	{
		private const int MainSlotCount = 50;
		private const int LastVisibleSlot = 58;
		private const int Gap = 4;
		private const int GroupGap = 10;
		private const int LabelHeight = 25;
		private const int OuterPadding = 8;

		private readonly Dictionary<int, InventoryEntry> _items =
			new Dictionary<int, InventoryEntry>();
		private readonly Dictionary<int, Bitmap> _icons =
			new Dictionary<int, Bitmap>();
		private readonly Dictionary<int, DateTime> _iconFailures =
			new Dictionary<int, DateTime>();
		private readonly ToolTip _toolTip = new ToolTip();

		private Func<int, Bitmap> _itemIconProvider;
		private int _selectedSlot = -1;
		private int _hoverSlot = -1;

		public event EventHandler SelectedSlotChanged;

		public TmlInventorySlotGrid()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint |
				ControlStyles.OptimizedDoubleBuffer |
				ControlStyles.ResizeRedraw |
				ControlStyles.UserPaint |
				ControlStyles.Selectable, true);
			TabStop = true;
			BackColor = Color.FromArgb(25, 28, 36);
			ForeColor = Color.WhiteSmoke;
			MinimumSize = new Size(370, 190);
			_toolTip.AutoPopDelay = 8000;
			_toolTip.InitialDelay = 250;
			_toolTip.ReshowDelay = 80;
		}

		public Func<int, Bitmap> ItemIconProvider
		{
			get { return _itemIconProvider; }
			set {
				if (ReferenceEquals(_itemIconProvider, value))
					return;
				_itemIconProvider = value;
				ClearIconCache();
				Invalidate();
			}
		}

		public int SelectedSlot
		{
			get { return _selectedSlot; }
			set {
				int normalized = value >= 0 && value <= LastVisibleSlot ? value : -1;
				if (_selectedSlot == normalized)
					return;
				_selectedSlot = normalized;
				Invalidate();
				EventHandler handler = SelectedSlotChanged;
				if (handler != null)
					handler(this, EventArgs.Empty);
			}
		}

		public void SetItems(IEnumerable<InventoryEntry> entries)
		{
			_items.Clear();
			if (entries != null) {
				foreach (InventoryEntry entry in entries) {
					if (entry != null && entry.Slot >= 0 && entry.Slot <= LastVisibleSlot)
						_items[entry.Slot] = entry;
				}
			}
			UpdateToolTip();
			Invalidate();
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			Graphics graphics = e.Graphics;
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

			LayoutMetrics metrics = CalculateLayout();
			using (SolidBrush labelBrush = new SolidBrush(Color.FromArgb(218, 230, 255)))
			using (Font labelFont = new Font(Font.FontFamily, Math.Max(7.5F, Font.Size - 1F), FontStyle.Bold)) {
				DrawCenteredText(graphics, "\u94b1\u5e01", labelFont, labelBrush,
					new Rectangle(metrics.CoinX - 4, metrics.StartY - LabelHeight,
						metrics.Cell + 8, LabelHeight - 2));
				DrawCenteredText(graphics, "\u5f39\u836f", labelFont, labelBrush,
					new Rectangle(metrics.AmmoX - 4, metrics.StartY - LabelHeight,
						metrics.Cell + 8, LabelHeight - 2));
			}

			for (int slot = 0; slot <= LastVisibleSlot; slot++)
				DrawSlot(graphics, slot, GetSlotRectangle(slot, metrics));
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			if (e.Button != MouseButtons.Left)
				return;
			int slot = HitTest(e.Location);
			if (slot >= 0) {
				Focus();
				SelectedSlot = slot;
			}
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			int slot = HitTest(e.Location);
			if (_hoverSlot == slot)
				return;
			_hoverSlot = slot;
			UpdateToolTip();
			Invalidate();
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			base.OnMouseLeave(e);
			if (_hoverSlot < 0)
				return;
			_hoverSlot = -1;
			_toolTip.SetToolTip(this, null);
			Invalidate();
		}

		protected override bool IsInputKey(Keys keyData)
		{
			switch (keyData & Keys.KeyCode) {
				case Keys.Left:
				case Keys.Right:
				case Keys.Up:
				case Keys.Down:
					return true;
			}
			return base.IsInputKey(keyData);
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			if (_selectedSlot < 0)
				return;

			int next = _selectedSlot;
			if (_selectedSlot < MainSlotCount) {
				if (e.KeyCode == Keys.Left && _selectedSlot % 10 > 0)
					next--;
				else if (e.KeyCode == Keys.Right && _selectedSlot % 10 < 9)
					next++;
				else if (e.KeyCode == Keys.Up && _selectedSlot >= 10)
					next -= 10;
				else if (e.KeyCode == Keys.Down && _selectedSlot < 40)
					next += 10;
			}

			if (next != _selectedSlot) {
				SelectedSlot = next;
				e.Handled = true;
			}
		}

		private void DrawSlot(Graphics graphics, int slot, Rectangle rectangle)
		{
			if (rectangle.Width <= 0 || rectangle.Height <= 0)
				return;

			bool selected = slot == _selectedSlot;
			bool hovered = slot == _hoverSlot;
			Color fill;
			if (slot < 10)
				fill = Color.FromArgb(218, 45, 75, 158);
			else if (slot >= 50 && slot <= 53)
				fill = Color.FromArgb(218, 83, 72, 143);
			else if (slot >= 54 && slot <= 57)
				fill = Color.FromArgb(218, 57, 91, 137);
			else if (slot == 58)
				fill = Color.FromArgb(218, 75, 75, 104);
			else
				fill = Color.FromArgb(205, 43, 65, 137);
			if (hovered)
				fill = Blend(fill, Color.White, 0.16F);

			Rectangle inner = Rectangle.Inflate(rectangle, -1, -1);
			using (GraphicsPath path = RoundedRectangle(inner, Math.Max(4, rectangle.Width / 8)))
			using (SolidBrush brush = new SolidBrush(fill))
			using (Pen border = new Pen(
				selected ? Color.FromArgb(255, 244, 193, 66) : Color.FromArgb(235, 97, 128, 211),
				selected ? 3F : 1.5F)) {
				graphics.FillPath(brush, path);
				graphics.DrawPath(border, path);
			}

			InventoryEntry entry;
			bool hasItem = _items.TryGetValue(slot, out entry) &&
				entry != null && entry.Id > 0 && entry.Stack > 0;
			if (hasItem) {
				Bitmap icon = GetIcon(entry.Id);
				Rectangle iconBounds = Rectangle.Inflate(rectangle, -5, -5);
				if (slot < 10)
					iconBounds.Y += 2;
				if (icon != null) {
					Rectangle target = FitRectangle(icon.Size, iconBounds);
					InterpolationMode oldInterpolation = graphics.InterpolationMode;
					PixelOffsetMode oldPixelOffset = graphics.PixelOffsetMode;
					graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
					graphics.PixelOffsetMode = PixelOffsetMode.Half;
					graphics.DrawImage(icon, target);
					graphics.InterpolationMode = oldInterpolation;
					graphics.PixelOffsetMode = oldPixelOffset;
				}
				else {
					string idText = entry.Id.ToString();
					float idSize = idText.Length >= 4
						? Math.Max(6F, Math.Min(7F, rectangle.Width / 6F))
						: Math.Max(7F, Math.Min(10F, rectangle.Width / 5F));
					using (Font idFont = new Font(Font.FontFamily,
						idSize, FontStyle.Bold))
					using (SolidBrush idBrush = new SolidBrush(Color.FromArgb(205, 235, 240, 255))) {
						DrawCenteredText(graphics, idText, idFont, idBrush, iconBounds);
					}
				}

				if (entry.Stack > 1) {
					string stack = entry.Stack.ToString();
					using (Font stackFont = new Font(Font.FontFamily,
						Math.Max(7F, Math.Min(9F, rectangle.Width / 5F)), FontStyle.Bold)) {
						SizeF size = graphics.MeasureString(stack, stackFont);
						float x = rectangle.Right - size.Width - 3F;
						float y = rectangle.Bottom - size.Height - 1F;
						DrawShadowedString(graphics, stack, stackFont, x, y);
					}
				}
			}

			if (slot < 10) {
				string hotkey = slot == 9 ? "0" : (slot + 1).ToString();
				using (Font hotkeyFont = new Font(Font.FontFamily,
					Math.Max(7F, Math.Min(9F, rectangle.Width / 5F)), FontStyle.Bold)) {
					DrawShadowedString(graphics, hotkey, hotkeyFont, rectangle.Left + 4F, rectangle.Top + 2F);
				}
			}
			else if (slot == 58 && !hasItem) {
				using (Font mouseFont = new Font(Font.FontFamily,
					Math.Max(7F, Math.Min(9F, rectangle.Width / 5F)), FontStyle.Bold)) {
					DrawShadowedString(graphics, "\u9f20", mouseFont,
						rectangle.Left + 4F, rectangle.Top + 2F);
				}
			}
		}

		private Bitmap GetIcon(int itemId)
		{
			Bitmap icon;
			if (_icons.TryGetValue(itemId, out icon))
				return icon;
			if (_itemIconProvider == null)
				return null;

			DateTime failedAt;
			if (_iconFailures.TryGetValue(itemId, out failedAt) &&
				DateTime.UtcNow - failedAt < TimeSpan.FromMilliseconds(250))
				return null;

			try {
				icon = _itemIconProvider(itemId);
				if (icon != null) {
					_icons[itemId] = icon;
					_iconFailures.Remove(itemId);
					return icon;
				}
			}
			catch {
			}
			_iconFailures[itemId] = DateTime.UtcNow;
			return null;
		}

		private void UpdateToolTip()
		{
			if (_hoverSlot < 0) {
				_toolTip.SetToolTip(this, null);
				return;
			}
			InventoryEntry entry;
			if (_items.TryGetValue(_hoverSlot, out entry) &&
				entry != null && entry.Id > 0 && entry.Stack > 0) {
				string slotName = _hoverSlot == 58 ? " (\u9f20\u6807\u7269\u54c1)" : string.Empty;
				_toolTip.SetToolTip(this, string.Format(
					"\u69fd\u4f4d {0}{1}  |  ID {2}  |  {3} \u00d7 {4}",
					entry.Slot, slotName, entry.Id, entry.Name, entry.Stack));
			}
			else {
				string slotName = _hoverSlot == 58 ? " (\u9f20\u6807\u7269\u54c1)" : string.Empty;
				_toolTip.SetToolTip(this, string.Format(
					"\u69fd\u4f4d {0}{1}  |  (\u7a7a)", _hoverSlot, slotName));
			}
		}

		private int HitTest(Point point)
		{
			LayoutMetrics metrics = CalculateLayout();
			for (int slot = 0; slot <= LastVisibleSlot; slot++) {
				if (GetSlotRectangle(slot, metrics).Contains(point))
					return slot;
			}
			return -1;
		}

		private LayoutMetrics CalculateLayout()
		{
			int widthForCells = Math.Max(1, ClientSize.Width - OuterPadding * 2 - GroupGap - Gap * 11);
			int heightForCells = Math.Max(1, ClientSize.Height - LabelHeight - OuterPadding - Gap * 4);
			int cell = Math.Min(48, Math.Min(widthForCells / 12, heightForCells / 5));
			cell = Math.Max(24, cell);

			int contentWidth = cell * 12 + Gap * 11 + GroupGap;
			int startX = Math.Max(OuterPadding, (ClientSize.Width - contentWidth) / 2);
			int startY = LabelHeight;
			if (ClientSize.Height > LabelHeight + cell * 5 + Gap * 4)
				startY += (ClientSize.Height - LabelHeight - cell * 5 - Gap * 4) / 2;

			int coinX = startX + 10 * (cell + Gap) + GroupGap;
			return new LayoutMetrics {
				Cell = cell,
				StartX = startX,
				StartY = startY,
				CoinX = coinX,
				AmmoX = coinX + cell + Gap
			};
		}

		private static Rectangle GetSlotRectangle(int slot, LayoutMetrics metrics)
		{
			if (slot >= 0 && slot < MainSlotCount) {
				int row = slot / 10;
				int column = slot % 10;
				return new Rectangle(
					metrics.StartX + column * (metrics.Cell + Gap),
					metrics.StartY + row * (metrics.Cell + Gap),
					metrics.Cell, metrics.Cell);
			}
			if (slot >= 50 && slot <= 53) {
				int row = slot - 50;
				return new Rectangle(metrics.CoinX,
					metrics.StartY + row * (metrics.Cell + Gap),
					metrics.Cell, metrics.Cell);
			}
			if (slot >= 54 && slot <= 57) {
				int row = slot - 54;
				return new Rectangle(metrics.AmmoX,
					metrics.StartY + row * (metrics.Cell + Gap),
					metrics.Cell, metrics.Cell);
			}
			if (slot == 58) {
				return new Rectangle(metrics.CoinX,
					metrics.StartY + 4 * (metrics.Cell + Gap),
					metrics.Cell, metrics.Cell);
			}
			return Rectangle.Empty;
		}

		private static Rectangle FitRectangle(Size source, Rectangle bounds)
		{
			if (source.Width <= 0 || source.Height <= 0)
				return bounds;
			float ratio = Math.Min((float)bounds.Width / source.Width, (float)bounds.Height / source.Height);
			int width = Math.Max(1, (int)Math.Round(source.Width * ratio));
			int height = Math.Max(1, (int)Math.Round(source.Height * ratio));
			return new Rectangle(
				bounds.X + (bounds.Width - width) / 2,
				bounds.Y + (bounds.Height - height) / 2,
				width, height);
		}

		private static void DrawCenteredText(Graphics graphics, string text, Font font, Brush brush, Rectangle bounds)
		{
			using (StringFormat format = new StringFormat()) {
				format.Alignment = StringAlignment.Center;
				format.LineAlignment = StringAlignment.Center;
				format.Trimming = StringTrimming.EllipsisCharacter;
				format.FormatFlags = StringFormatFlags.NoWrap;
				graphics.DrawString(text, font, brush, bounds, format);
			}
		}

		private static void DrawShadowedString(Graphics graphics, string text, Font font, float x, float y)
		{
			using (SolidBrush shadow = new SolidBrush(Color.FromArgb(220, 14, 17, 30)))
			using (SolidBrush foreground = new SolidBrush(Color.White)) {
				graphics.DrawString(text, font, shadow, x + 1F, y + 1F);
				graphics.DrawString(text, font, foreground, x, y);
			}
		}

		private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
		{
			int diameter = Math.Max(2, radius * 2);
			GraphicsPath path = new GraphicsPath();
			path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
			path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
			path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
			path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
			path.CloseFigure();
			return path;
		}

		private static Color Blend(Color first, Color second, float amount)
		{
			float keep = 1F - amount;
			return Color.FromArgb(
				(int)(first.A * keep + second.A * amount),
				(int)(first.R * keep + second.R * amount),
				(int)(first.G * keep + second.G * amount),
				(int)(first.B * keep + second.B * amount));
		}

		private void ClearIconCache()
		{
			foreach (Bitmap bitmap in _icons.Values) {
				if (bitmap != null)
					bitmap.Dispose();
			}
			_icons.Clear();
			_iconFailures.Clear();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing) {
				ClearIconCache();
				_toolTip.Dispose();
			}
			base.Dispose(disposing);
		}

		private struct LayoutMetrics
		{
			public int Cell;
			public int StartX;
			public int StartY;
			public int CoinX;
			public int AmmoX;
		}
	}
}
