namespace TVM.Demo.Views;

/// <summary>Shared colours and control factories so every screen looks consistent.</summary>
internal static class Ui
{
    public static readonly Color Navy = Color.FromArgb(0x1F, 0x4E, 0x79);
    public static readonly Color Blue = Color.FromArgb(0x2E, 0x75, 0xB6);
    public static readonly Color Back = Color.FromArgb(0xF4, 0xF7, 0xFB);
    public static readonly Color Gold = Color.FromArgb(0xFF, 0xC0, 0x00);
    public static readonly Color Red = Color.FromArgb(0xC0, 0x00, 0x00);
    public static readonly Color Green = Color.FromArgb(0x70, 0xAD, 0x47);
    public static readonly Color Grey = Color.FromArgb(0x5B, 0x6B, 0x7B);

    public static Font F(float size, FontStyle style = FontStyle.Regular) => new("Segoe UI", size, style);

    public static Button Btn(string text, Color back, Color fore, int w = 220, int h = 60)
    {
        var b = new Button
        {
            Text = text, Width = w, Height = h, BackColor = back, ForeColor = fore,
            FlatStyle = FlatStyle.Flat, Font = F(14, FontStyle.Bold), Cursor = Cursors.Hand,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowOnly, MinimumSize = new Size(w, h), Padding = new Padding(12, 0, 12, 0),
        };
        b.FlatAppearance.BorderColor = fore == Color.White ? back : fore;
        b.FlatAppearance.BorderSize = 2;
        return b;
    }

    public static Button Primary(string text, int w = 220, int h = 60) => Btn(text, Blue, Color.White, w, h);
    public static Button Ghost(string text, int w = 180, int h = 60) => Btn(text, Color.White, Navy, w, h);
    public static Button Danger(string text, int w = 160, int h = 60) => Btn(text, Color.White, Red, w, h);

    public static Label Lbl(string text, float size, FontStyle style = FontStyle.Regular, Color? color = null) => new()
    {
        Text = text, Font = F(size, style), ForeColor = color ?? Color.FromArgb(0x1F, 0x2D, 0x3D), AutoSize = true,
        BackColor = Color.Transparent,
    };
}
