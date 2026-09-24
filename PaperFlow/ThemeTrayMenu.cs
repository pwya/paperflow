using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace PaperFlow;

public sealed class ThemeTrayMenu : ContextMenuStrip
{
    public ThemeTrayMenu()
    {
        Renderer = new ToolStripProfessionalRenderer(new Palette()) { RoundedEdges = false };
        ShowImageMargin = false;
    }

    protected override void OnOpening(CancelEventArgs e)
    {
        BackColor = ColorTranslator.FromHtml(Appearance.Current.Card);
        ForeColor = ColorTranslator.FromHtml(Appearance.Current.Ink);
        foreach (ToolStripItem item in Items) item.ForeColor = ForeColor;
        base.OnOpening(e);
    }

    private sealed class Palette : ProfessionalColorTable
    {
        public Palette() { UseSystemColors = false; }
        private static Color Card => ColorTranslator.FromHtml(Appearance.Current.Card);
        private static Color Soft => ColorTranslator.FromHtml(Appearance.Current.Soft);
        private static Color Line => ColorTranslator.FromHtml(Appearance.Current.Border);
        public override Color ToolStripDropDownBackground => Card;
        public override Color MenuBorder => Line;
        public override Color MenuItemBorder => Line;
        public override Color MenuItemSelected => Soft;
        public override Color MenuItemSelectedGradientBegin => Soft;
        public override Color MenuItemSelectedGradientEnd => Soft;
        public override Color MenuItemPressedGradientBegin => Soft;
        public override Color MenuItemPressedGradientMiddle => Soft;
        public override Color MenuItemPressedGradientEnd => Soft;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Line;
    }
}
