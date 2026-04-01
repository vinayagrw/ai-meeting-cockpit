using System.Drawing.Drawing2D;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MeetingRecorder.Windows.Shell;

internal enum UiButtonTone
{
    Primary,
    Secondary,
    Neutral,
    Ghost,
    Danger
}

internal static class UiTheme
{
    // ── Surface palette ──────────────────────────────────────────────
    public static readonly Color AppBackground = Color.FromArgb(243, 244, 248);
    public static readonly Color Surface = Color.FromArgb(255, 255, 255);
    public static readonly Color SurfaceAlt = Color.FromArgb(248, 249, 252);
    public static readonly Color SurfaceTint = Color.FromArgb(237, 239, 245);
    public static readonly Color SurfaceHover = Color.FromArgb(240, 242, 247);
    public static readonly Color Border = Color.FromArgb(222, 225, 233);
    public static readonly Color BorderSubtle = Color.FromArgb(232, 235, 241);

    // ── Accent ───────────────────────────────────────────────────────
    public static readonly Color Accent = Color.FromArgb(59, 130, 246);       // #3B82F6 — vibrant blue
    public static readonly Color AccentStrong = Color.FromArgb(37, 99, 235);  // #2563EB
    public static readonly Color AccentSoft = Color.FromArgb(239, 246, 255);  // #EFF6FF
    public static readonly Color AccentGlow = Color.FromArgb(96, 165, 250);   // #60A5FA — for glow halos
    public static readonly Color AccentGradientEnd = Color.FromArgb(99, 102, 241); // #6366F1 indigo

    // ── Semantic ─────────────────────────────────────────────────────
    public static readonly Color Success = Color.FromArgb(22, 163, 74);       // #16A34A
    public static readonly Color SuccessSoft = Color.FromArgb(220, 252, 231); // #DCFCE7
    public static readonly Color SuccessGlow = Color.FromArgb(74, 222, 128);  // #4ADE80
    public static readonly Color Warning = Color.FromArgb(234, 138, 0);       // #EA8A00
    public static readonly Color WarningSoft = Color.FromArgb(254, 243, 199); // #FEF3C7
    public static readonly Color WarningGlow = Color.FromArgb(251, 191, 36);  // #FBBF24
    public static readonly Color Danger = Color.FromArgb(220, 38, 38);        // #DC2626
    public static readonly Color DangerSoft = Color.FromArgb(254, 226, 226);  // #FEE2E2
    public static readonly Color DangerGlow = Color.FromArgb(248, 113, 113);  // #F87171

    // ── Text ─────────────────────────────────────────────────────────
    public static readonly Color TextPrimary = Color.FromArgb(15, 23, 42);    // #0F172A — slate-900
    public static readonly Color TextMuted = Color.FromArgb(100, 116, 139);   // #64748B — slate-500
    public static readonly Color TextSoft = Color.FromArgb(148, 163, 184);    // #94A3B8 — slate-400

    // ── Typography ───────────────────────────────────────────────────
    private static readonly string FontFamily = ResolveFontFamily();
    private static readonly string FontFamilySemibold = ResolveSemiboldFontFamily();
    private static readonly Font UiFont = new(FontFamily, 9.5F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font UiFontSemibold = new(FontFamilySemibold, 9.5F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font UiFontTitle = new(FontFamilySemibold, 20F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font UiFontSubtitle = new(FontFamily, 9.5F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font UiFontSection = new(FontFamilySemibold, 11.5F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font UiFontEyebrow = new(FontFamilySemibold, 7.5F, FontStyle.Regular, GraphicsUnit.Point);

    public static Font BodyFont => UiFont;
    public static Font BodyFontSemibold => UiFontSemibold;
    public static Font TitleFont => UiFontTitle;
    public static Font SubtitleFont => UiFontSubtitle;
    public static Font SectionFont => UiFontSection;
    public static Font EyebrowFont => UiFontEyebrow;

    // ── Mica backdrop (Windows 11 22621+) ────────────────────────────

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    public static void ApplyForm(Form form)
    {
        form.BackColor = AppBackground;
        form.ForeColor = TextPrimary;
        form.Font = BodyFont;
        TryEnableMica(form);
    }

    private static void TryEnableMica(Form form)
    {
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
            {
                return;
            }

            // 2 = Mica, 3 = Acrylic, 4 = MicaAlt (Tabbed)
            var value = 2;
            DwmSetWindowAttribute(form.Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref value, sizeof(int));
        }
        catch
        {
            // Graceful degradation — just use the flat color
        }
    }

    // ── Control styling ──────────────────────────────────────────────

    public static void StyleSplitContainer(SplitContainer splitContainer)
    {
        splitContainer.BackColor = Border;
        splitContainer.Panel1.BackColor = AppBackground;
        splitContainer.Panel2.BackColor = AppBackground;
    }

    public static void StyleTextBox(TextBox textBox, bool readOnly = false, bool code = false)
    {
        textBox.BackColor = readOnly ? SurfaceAlt : Surface;
        textBox.ForeColor = TextPrimary;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.Font = code
            ? new Font("Cascadia Code", 9.5F, FontStyle.Regular, GraphicsUnit.Point)
            : BodyFont;
    }

    public static void StyleLabel(Label label, bool muted = false)
    {
        label.ForeColor = muted ? TextMuted : TextPrimary;
        label.Font = muted ? BodyFont : BodyFontSemibold;
        label.BackColor = Color.Transparent;
    }

    public static void StyleListView(ListView listView)
    {
        listView.BackColor = Surface;
        listView.ForeColor = TextPrimary;
        listView.BorderStyle = BorderStyle.None;
        listView.Font = BodyFont;
        listView.OwnerDraw = false;
    }

    public static void StyleListBox(ListBox listBox)
    {
        listBox.BackColor = Surface;
        listBox.ForeColor = TextPrimary;
        listBox.BorderStyle = BorderStyle.None;
        listBox.Font = BodyFont;
    }

    public static void StyleComboBox(ComboBox comboBox)
    {
        comboBox.BackColor = Surface;
        comboBox.ForeColor = TextPrimary;
        comboBox.FlatStyle = FlatStyle.Flat;
        comboBox.Font = BodyFont;
    }

    // ── Layout helpers ───────────────────────────────────────────────

    public static Panel CreateHeaderPanel(string eyebrow, string title, string subtitle, Control? trailingControl = null)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = trailingControl is null ? 1 : 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 0, 12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        if (trailingControl is not null)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }

        var textPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        textPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var eyebrowLabel = new Label
        {
            AutoSize = true,
            Font = EyebrowFont,
            ForeColor = Accent,
            Margin = Padding.Empty,
            Text = eyebrow.ToUpperInvariant()
        };
        var titleLabel = new Label
        {
            AutoSize = true,
            Font = TitleFont,
            ForeColor = TextPrimary,
            Margin = new Padding(0, 6, 0, 0),
            Text = title
        };
        var subtitleLabel = new Label
        {
            AutoSize = true,
            Font = SubtitleFont,
            ForeColor = TextMuted,
            Margin = new Padding(0, 6, 0, 0),
            Text = subtitle
        };

        textPanel.Controls.Add(eyebrowLabel, 0, 0);
        textPanel.Controls.Add(titleLabel, 0, 1);
        textPanel.Controls.Add(subtitleLabel, 0, 2);
        WireResponsiveLabelWidth(textPanel, titleLabel, subtitleLabel);
        layout.Controls.Add(textPanel, 0, 0);

        if (trailingControl is not null)
        {
            trailingControl.Margin = new Padding(12, 18, 0, 0);
            layout.Controls.Add(trailingControl, 1, 0);
        }

        return layout;
    }

    public static Control CreateSectionTitle(string title, string? subtitle = null)
    {
        var titleLabel = new Label
        {
            AutoSize = true,
            Font = SectionFont,
            ForeColor = TextPrimary,
            Margin = Padding.Empty,
            Text = title
        };

        if (string.IsNullOrWhiteSpace(subtitle))
        {
            var singleLinePanel = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 0, 0, 8),
                Margin = Padding.Empty
            };
            singleLinePanel.Controls.Add(titleLabel);
            WireResponsiveLabelWidth(singleLinePanel, titleLabel);
            return singleLinePanel;
        }

        var subtitleLabel = new Label
        {
            AutoSize = true,
            Font = BodyFont,
            ForeColor = TextMuted,
            Margin = new Padding(0, 4, 0, 0),
            Text = subtitle
        };

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 0, 8)
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        stack.Controls.Add(titleLabel, 0, 0);
        stack.Controls.Add(subtitleLabel, 0, 1);
        WireResponsiveLabelWidth(stack, titleLabel, subtitleLabel);
        return stack;
    }

    // ── Drawing helpers ──────────────────────────────────────────────

    public static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(2, radius * 2);
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color Lighten(Color color, int amount)
    {
        return Color.FromArgb(
            color.A,
            Math.Min(255, color.R + amount),
            Math.Min(255, color.G + amount),
            Math.Min(255, color.B + amount));
    }

    public static Color Darken(Color color, int amount)
    {
        return Color.FromArgb(
            color.A,
            Math.Max(0, color.R - amount),
            Math.Max(0, color.G - amount),
            Math.Max(0, color.B - amount));
    }

    public static Color WithAlpha(Color color, int alpha)
    {
        return Color.FromArgb(Math.Clamp(alpha, 0, 255), color.R, color.G, color.B);
    }

    private static void WireResponsiveLabelWidth(Control host, params Label[] labels)
    {
        void Apply()
        {
            var maxWidth = Math.Max(180, host.ClientSize.Width - host.Padding.Horizontal);
            foreach (var label in labels)
            {
                label.MaximumSize = new Size(maxWidth, 0);
            }
        }

        host.SizeChanged += (_, _) => Apply();
        Apply();
    }

    private static string ResolveFontFamily()
    {
        using var test = new Font("Segoe UI Variable Text", 10F, FontStyle.Regular, GraphicsUnit.Point);
        return test.Name.Equals("Segoe UI Variable Text", StringComparison.OrdinalIgnoreCase)
            ? "Segoe UI Variable Text"
            : "Segoe UI";
    }

    private static string ResolveSemiboldFontFamily()
    {
        using var test = new Font("Segoe UI Variable Text Semibold", 10F, FontStyle.Regular, GraphicsUnit.Point);
        if (test.Name.Equals("Segoe UI Variable Text Semibold", StringComparison.OrdinalIgnoreCase))
        {
            return "Segoe UI Variable Text Semibold";
        }

        using var fallback = new Font("Segoe UI Semibold", 10F, FontStyle.Regular, GraphicsUnit.Point);
        return fallback.Name.Equals("Segoe UI Semibold", StringComparison.OrdinalIgnoreCase)
            ? "Segoe UI Semibold"
            : "Segoe UI";
    }
}

// ════════════════════════════════════════════════════════════════════
//  Cards — 3-layer shadow, gradient top-edge accent
// ════════════════════════════════════════════════════════════════════

internal sealed class UiCardPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColor { get; set; } = UiTheme.Surface;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = UiTheme.BorderSubtle;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color AccentColor { get; set; } = UiTheme.Accent;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 14;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowAccent { get; set; } = false;

    public UiCardPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.Transparent;
        Padding = new Padding(16);
        Margin = new Padding(0);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = ClientRectangle;
        bounds.Inflate(-3, -3);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        // Layer 1: diffuse ambient shadow
        var s1 = new Rectangle(bounds.X, bounds.Y + 1, bounds.Width, bounds.Height + 2);
        using (var p1 = UiTheme.CreateRoundedPath(s1, CornerRadius + 2))
        using (var b1 = new SolidBrush(Color.FromArgb(6, 0, 0, 0)))
            e.Graphics.FillPath(b1, p1);

        // Layer 2: medium key shadow
        var s2 = new Rectangle(bounds.X, bounds.Y + 2, bounds.Width, bounds.Height + 1);
        using (var p2 = UiTheme.CreateRoundedPath(s2, CornerRadius + 1))
        using (var b2 = new SolidBrush(Color.FromArgb(10, 0, 0, 0)))
            e.Graphics.FillPath(b2, p2);

        // Layer 3: tight contact shadow
        var s3 = new Rectangle(bounds.X, bounds.Y + 3, bounds.Width, bounds.Height);
        using (var p3 = UiTheme.CreateRoundedPath(s3, CornerRadius))
        using (var b3 = new SolidBrush(Color.FromArgb(16, 0, 0, 0)))
            e.Graphics.FillPath(b3, p3);

        // Card surface
        using var path = UiTheme.CreateRoundedPath(bounds, CornerRadius);
        using var fillBrush = new SolidBrush(FillColor);
        e.Graphics.FillPath(fillBrush, path);

        // Border
        using var borderPen = new Pen(BorderColor, 1F);
        e.Graphics.DrawPath(borderPen, path);

        // Gradient accent edge at top
        if (ShowAccent)
        {
            var accentWidth = Math.Max(80, bounds.Width / 4);
            var accentRect = new Rectangle(bounds.X + CornerRadius, bounds.Y + 1, accentWidth, 3);
            if (accentRect.Width > 0)
            {
                using var gradBrush = new LinearGradientBrush(
                    accentRect,
                    UiTheme.WithAlpha(AccentColor, 200),
                    UiTheme.WithAlpha(UiTheme.AccentGradientEnd, 120),
                    LinearGradientMode.Horizontal);
                using var accentPath = UiTheme.CreateRoundedPath(accentRect, 2);
                e.Graphics.FillPath(gradBrush, accentPath);
            }
        }
    }
}

// ════════════════════════════════════════════════════════════════════
//  Buttons — gradient primary fill, shadow on hover, press scale
// ════════════════════════════════════════════════════════════════════

internal sealed class UiButton : Button
{
    private bool _isHovered;
    private bool _isPressed;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public UiButtonTone Tone { get; set; } = UiButtonTone.Neutral;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 8;

    public UiButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        ForeColor = UiTheme.TextPrimary;
        Font = UiTheme.BodyFontSemibold;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _isHovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _isHovered = false;
        _isPressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _isPressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _isPressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = ClientRectangle;
        bounds.Inflate(-1, -1);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var (fill, border, text) = ResolvePalette();

        // Hover: shadow lift + lighten
        if (_isHovered && !_isPressed && Enabled)
        {
            using var hs1 = UiTheme.CreateRoundedPath(new Rectangle(bounds.X, bounds.Y + 1, bounds.Width, bounds.Height), CornerRadius);
            using var hb1 = new SolidBrush(Color.FromArgb(10, 0, 0, 0));
            e.Graphics.FillPath(hb1, hs1);
            using var hs2 = UiTheme.CreateRoundedPath(new Rectangle(bounds.X, bounds.Y + 2, bounds.Width, bounds.Height), CornerRadius);
            using var hb2 = new SolidBrush(Color.FromArgb(8, 0, 0, 0));
            e.Graphics.FillPath(hb2, hs2);
            fill = UiTheme.Lighten(fill, Tone == UiButtonTone.Neutral || Tone == UiButtonTone.Ghost ? 3 : 12);
        }
        else if (_isPressed)
        {
            fill = UiTheme.Darken(fill, 14);
        }

        using var path = UiTheme.CreateRoundedPath(bounds, CornerRadius);

        // Primary tone gets a gradient fill
        if (Tone == UiButtonTone.Primary && Enabled && bounds.Height > 0)
        {
            using var gradBrush = new LinearGradientBrush(
                bounds,
                _isPressed ? UiTheme.Darken(UiTheme.Accent, 20) : UiTheme.Accent,
                _isPressed ? UiTheme.Darken(UiTheme.AccentGradientEnd, 20) : UiTheme.AccentGradientEnd,
                LinearGradientMode.Horizontal);
            e.Graphics.FillPath(gradBrush, path);
        }
        else
        {
            using var fillBrush = new SolidBrush(fill);
            e.Graphics.FillPath(fillBrush, path);
        }

        using var borderPen = new Pen(Tone == UiButtonTone.Primary && Enabled ? Color.Transparent : border, 1F);
        e.Graphics.DrawPath(borderPen, path);

        var textBounds = bounds;
        textBounds.Inflate(-12, -8);
        var flags = ResolveTextFlags();

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            textBounds,
            Enabled ? text : UiTheme.TextSoft,
            flags);
    }

    private (Color Fill, Color Border, Color Text) ResolvePalette()
    {
        if (!Enabled)
        {
            return (UiTheme.SurfaceTint, UiTheme.Border, UiTheme.TextSoft);
        }

        return Tone switch
        {
            UiButtonTone.Primary => (UiTheme.Accent, UiTheme.AccentStrong, Color.White),
            UiButtonTone.Secondary => (UiTheme.AccentSoft, UiTheme.WithAlpha(UiTheme.Accent, 40), UiTheme.AccentStrong),
            UiButtonTone.Danger => (UiTheme.DangerSoft, UiTheme.WithAlpha(UiTheme.Danger, 60), UiTheme.Danger),
            UiButtonTone.Ghost => (UiTheme.SurfaceHover, UiTheme.BorderSubtle, UiTheme.TextMuted),
            _ => (UiTheme.Surface, UiTheme.Border, UiTheme.TextPrimary)
        };
    }

    private TextFormatFlags ResolveTextFlags()
    {
        var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping;
        flags |= Text.Contains(Environment.NewLine, StringComparison.Ordinal) || Text.Contains('\n')
            ? TextFormatFlags.WordBreak
            : TextFormatFlags.SingleLine;

        flags |= TextAlign switch
        {
            ContentAlignment.TopLeft or ContentAlignment.MiddleLeft or ContentAlignment.BottomLeft => TextFormatFlags.Left,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => TextFormatFlags.Right,
            _ => TextFormatFlags.HorizontalCenter
        };

        flags |= TextAlign switch
        {
            ContentAlignment.TopLeft or ContentAlignment.TopCenter or ContentAlignment.TopRight => TextFormatFlags.Top,
            ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight => TextFormatFlags.Bottom,
            _ => TextFormatFlags.VerticalCenter
        };

        if (RightToLeft == RightToLeft.Yes)
        {
            flags |= TextFormatFlags.RightToLeft;
        }

        return flags;
    }
}

// ════════════════════════════════════════════════════════════════════
//  Badges — colored glow halo behind the pill
// ════════════════════════════════════════════════════════════════════

internal sealed class UiBadgeLabel : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColor { get; set; } = UiTheme.AccentSoft;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = UiTheme.AccentSoft;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color TextColor { get; set; } = UiTheme.AccentStrong;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color GlowColor { get; set; } = UiTheme.AccentGlow;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowGlow { get; set; } = false;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 10;

    public UiBadgeLabel()
    {
        DoubleBuffered = true;
        Height = 28;
        Width = 140;
        Font = UiTheme.BodyFontSemibold;
        ForeColor = TextColor;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = ClientRectangle;
        bounds.Inflate(-4, -4);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        // Glow halo behind the badge
        if (ShowGlow)
        {
            var glowBounds = bounds;
            glowBounds.Inflate(6, 6);
            using var glowPath = UiTheme.CreateRoundedPath(glowBounds, CornerRadius + 6);
            using var glowBrush = new SolidBrush(UiTheme.WithAlpha(GlowColor, 45));
            e.Graphics.FillPath(glowBrush, glowPath);

            var glow2 = bounds;
            glow2.Inflate(3, 3);
            using var glow2Path = UiTheme.CreateRoundedPath(glow2, CornerRadius + 3);
            using var glow2Brush = new SolidBrush(UiTheme.WithAlpha(GlowColor, 30));
            e.Graphics.FillPath(glow2Brush, glow2Path);
        }

        using var path = UiTheme.CreateRoundedPath(bounds, CornerRadius);
        using var fillBrush = new SolidBrush(FillColor);
        using var borderPen = new Pen(BorderColor, 1F);
        e.Graphics.FillPath(fillBrush, path);
        e.Graphics.DrawPath(borderPen, path);

        // Status dot
        if (ShowGlow)
        {
            var dotSize = 7;
            var dotX = bounds.X + 10;
            var dotY = bounds.Y + (bounds.Height - dotSize) / 2;
            using var dotBrush = new SolidBrush(GlowColor);
            e.Graphics.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);

            // Shift text right to make room for dot
            var textBounds = new Rectangle(bounds.X + 22, bounds.Y, bounds.Width - 22, bounds.Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, TextColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        else
        {
            TextRenderer.DrawText(e.Graphics, Text, Font, bounds, TextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}

// ════════════════════════════════════════════════════════════════════
//  Tabs — gradient underline indicator, clean minimal style
// ════════════════════════════════════════════════════════════════════

internal sealed class UiTabControl : TabControl
{
    public UiTabControl()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(170, 38);
        Padding = new Point(16, 8);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= TabPages.Count)
        {
            return;
        }

        var selected = SelectedIndex == e.Index;
        var tabBounds = GetTabRect(e.Index);
        tabBounds.Inflate(-4, -4);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        // Selected tab gets a subtle surface tint
        if (selected)
        {
            using var bgPath = UiTheme.CreateRoundedPath(
                new Rectangle(tabBounds.X, tabBounds.Y, tabBounds.Width, tabBounds.Height - 4), 8);
            using var bgBrush = new SolidBrush(UiTheme.SurfaceHover);
            e.Graphics.FillPath(bgBrush, bgPath);
        }

        TextRenderer.DrawText(
            e.Graphics,
            TabPages[e.Index].Text,
            selected ? UiTheme.BodyFontSemibold : UiTheme.BodyFont,
            tabBounds,
            selected ? UiTheme.Accent : UiTheme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        // Gradient underline indicator
        if (selected)
        {
            var indicatorWidth = Math.Min(tabBounds.Width - 12, 52);
            var indicatorX = tabBounds.X + (tabBounds.Width - indicatorWidth) / 2;
            var indicatorY = tabBounds.Bottom - 3;
            var indicatorRect = new Rectangle(indicatorX, indicatorY, indicatorWidth, 3);
            if (indicatorRect.Width > 2)
            {
                using var indicatorPath = UiTheme.CreateRoundedPath(indicatorRect, 2);
                using var gradBrush = new LinearGradientBrush(
                    indicatorRect,
                    UiTheme.Accent,
                    UiTheme.AccentGradientEnd,
                    LinearGradientMode.Horizontal);
                e.Graphics.FillPath(gradBrush, indicatorPath);
            }
        }
    }
}

// ════════════════════════════════════════════════════════════════════
//  Nav Item — sidebar navigation with left accent bar
// ════════════════════════════════════════════════════════════════════

internal sealed class UiNavItem : Control
{
    private bool _selected;
    private bool _hovered;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    public UiNavItem()
    {
        DoubleBuffered = true;
        Height = 40;
        Width = 248;
        Font = UiTheme.BodyFont;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = ClientRectangle;

        // Background
        if (_selected)
        {
            using var bgPath = UiTheme.CreateRoundedPath(bounds, 8);
            using var bgBrush = new SolidBrush(UiTheme.AccentSoft);
            e.Graphics.FillPath(bgBrush, bgPath);
        }
        else if (_hovered)
        {
            using var bgPath = UiTheme.CreateRoundedPath(bounds, 8);
            using var bgBrush = new SolidBrush(UiTheme.SurfaceHover);
            e.Graphics.FillPath(bgBrush, bgPath);
        }

        // Left accent bar when selected
        if (_selected)
        {
            var barRect = new Rectangle(bounds.X + 2, bounds.Y + 8, 3, bounds.Height - 16);
            using var barPath = UiTheme.CreateRoundedPath(barRect, 2);
            using var barBrush = new SolidBrush(UiTheme.Accent);
            e.Graphics.FillPath(barBrush, barPath);
        }

        // Text
        var textBounds = new Rectangle(bounds.X + 16, bounds.Y, bounds.Width - 20, bounds.Height);
        var textColor = _selected ? UiTheme.Accent : (_hovered ? UiTheme.TextPrimary : UiTheme.TextMuted);
        var textFont = _selected ? UiTheme.BodyFontSemibold : UiTheme.BodyFont;
        TextRenderer.DrawText(e.Graphics, Text, textFont, textBounds, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

// ════════════════════════════════════════════════════════════════════
//  ComboBox — owner-drawn with rounded border and custom chevron
// ════════════════════════════════════════════════════════════════════

internal sealed class UiComboBox : ComboBox
{
    private bool _hovered;
    private bool _focused;

    public UiComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        ItemHeight = 28;
        Font = UiTheme.BodyFont;
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.TextPrimary;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        _focused = true;
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        _focused = false;
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);

        // Fill
        using var fillBrush = new SolidBrush(UiTheme.Surface);
        using var bgPath = UiTheme.CreateRoundedPath(bounds, 8);
        e.Graphics.FillPath(fillBrush, bgPath);

        // Border
        var borderColor = _focused ? UiTheme.Accent : (_hovered ? UiTheme.Accent : UiTheme.Border);
        using var borderPen = new Pen(borderColor, _focused ? 1.5F : 1F);
        e.Graphics.DrawPath(borderPen, bgPath);

        // Subtle focus glow
        if (_focused)
        {
            var glowBounds = bounds;
            glowBounds.Inflate(2, 2);
            using var glowPath = UiTheme.CreateRoundedPath(glowBounds, 10);
            using var glowPen = new Pen(UiTheme.WithAlpha(UiTheme.Accent, 30), 2F);
            e.Graphics.DrawPath(glowPen, glowPath);
        }

        // Selected text
        var text = SelectedItem?.ToString() ?? "";
        var textBounds = new Rectangle(10, 0, Width - 34, Height);
        TextRenderer.DrawText(e.Graphics, text, Font, textBounds, UiTheme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        // Chevron arrow
        var chevronX = Width - 22;
        var chevronY = Height / 2 - 2;
        using var chevronPen = new Pen(UiTheme.TextMuted, 1.5F) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
        e.Graphics.DrawLine(chevronPen, chevronX - 4, chevronY, chevronX, chevronY + 4);
        e.Graphics.DrawLine(chevronPen, chevronX, chevronY + 4, chevronX + 4, chevronY);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var bg = selected ? UiTheme.AccentSoft : UiTheme.Surface;

        using var bgBrush = new SolidBrush(bg);
        e.Graphics.FillRectangle(bgBrush, e.Bounds);

        var text = Items[e.Index]?.ToString() ?? "";
        var textColor = selected ? UiTheme.Accent : UiTheme.TextPrimary;
        TextRenderer.DrawText(e.Graphics, text, Font, new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height), textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

// ════════════════════════════════════════════════════════════════════
//  TextBox Wrapper — rounded border panel wrapping a borderless TextBox
// ════════════════════════════════════════════════════════════════════

internal sealed class UiTextBoxWrapper : Panel
{
    private readonly TextBox _innerBox;
    private bool _focused;

    public TextBox InnerTextBox => _innerBox;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string TextContent
    {
        get => _innerBox.Text;
        set => _innerBox.Text = value;
    }

    public UiTextBoxWrapper(bool multiline = false, bool readOnly = false, bool code = false)
    {
        DoubleBuffered = true;
        Padding = new Padding(10, 8, 10, 8);
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        _innerBox = new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = readOnly ? UiTheme.SurfaceAlt : UiTheme.Surface,
            ForeColor = UiTheme.TextPrimary,
            Font = code ? new Font("Cascadia Code", 9.5F, FontStyle.Regular, GraphicsUnit.Point) : UiTheme.BodyFont,
            Multiline = multiline,
            ReadOnly = readOnly,
            WordWrap = true
        };

        if (multiline)
        {
            _innerBox.ScrollBars = ScrollBars.Vertical;
        }

        _innerBox.GotFocus += (_, _) => { _focused = true; Invalidate(); };
        _innerBox.LostFocus += (_, _) => { _focused = false; Invalidate(); };

        Controls.Add(_innerBox);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);

        // Fill
        var fillColor = _innerBox.ReadOnly ? UiTheme.SurfaceAlt : UiTheme.Surface;
        using var fillBrush = new SolidBrush(fillColor);
        using var bgPath = UiTheme.CreateRoundedPath(bounds, 8);
        e.Graphics.FillPath(fillBrush, bgPath);

        // Border
        var borderColor = _focused && !_innerBox.ReadOnly ? UiTheme.Accent : UiTheme.Border;
        using var borderPen = new Pen(borderColor, _focused && !_innerBox.ReadOnly ? 1.5F : 1F);
        e.Graphics.DrawPath(borderPen, bgPath);

        // Focus glow
        if (_focused && !_innerBox.ReadOnly)
        {
            var glowBounds = bounds;
            glowBounds.Inflate(2, 2);
            using var glowPath = UiTheme.CreateRoundedPath(glowBounds, 10);
            using var glowPen = new Pen(UiTheme.WithAlpha(UiTheme.Accent, 30), 2F);
            e.Graphics.DrawPath(glowPen, glowPath);
        }
    }
}
