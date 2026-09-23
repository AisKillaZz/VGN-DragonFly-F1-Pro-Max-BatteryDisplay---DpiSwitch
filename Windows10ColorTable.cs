using System.Drawing;
using System.Windows.Forms;

namespace VgnTrayBattery;

internal sealed class Windows10ColorTable : ProfessionalColorTable
{
    public override Color MenuBorder => Color.FromArgb(210, 210, 210);
    public override Color MenuItemBorder => Color.FromArgb(180, 180, 180);
    public override Color MenuItemSelected => Color.FromArgb(232, 232, 232);
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(232, 232, 232);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(232, 232, 232);
    public override Color ToolStripDropDownBackground => Color.White;
    public override Color ImageMarginGradientBegin => Color.White;
    public override Color ImageMarginGradientMiddle => Color.White;
    public override Color ImageMarginGradientEnd => Color.White;
    public override Color SeparatorDark => Color.FromArgb(225, 225, 225);
    public override Color SeparatorLight => Color.White;
}
