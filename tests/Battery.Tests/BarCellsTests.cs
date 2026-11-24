using Battery.Core;

namespace Battery.Tests;

public class BarCellsTests
{
    [Fact]
    public void Strip_bar_has_five_cells_filling_its_width()
    {
        var cells = BarCells.Layout(28, 5, 1);

        Assert.Equal(5, cells.Count);
        Assert.Equal(0, cells[0].X);
        Assert.Equal(28, cells[^1].X + cells[^1].Width, 6);
        Assert.All(cells, c => Assert.Equal(4.8, c.Width, 6));
    }

    [Fact]
    public void Gaps_between_cells_are_equal()
    {
        var cells = BarCells.Layout(130, 10, 1);

        for (var i = 1; i < cells.Count; i++)
            Assert.Equal(1, cells[i].X - (cells[i - 1].X + cells[i - 1].Width), 6);
    }

    [Theory]
    [InlineData(28, 5)]
    [InlineData(130, 10)]
    public void Cell_count_by_bar_width(double width, int expected) =>
        Assert.Equal(expected, BarCells.CountFor(width));

    [Fact]
    public void BarStyle_parses_leniently_and_defaults_to_clean()
    {
        Assert.Equal(BarStyle.Gel, BarStyles.Parse("gel"));
        Assert.Equal(BarStyle.Cells, BarStyles.Parse("Cells"));
        Assert.Equal(BarStyle.Clean, BarStyles.Parse(null));
        Assert.Equal(BarStyle.Clean, BarStyles.Parse("sparkly"));
    }

    [Fact]
    public void BarStyles_menu_order_and_names()
    {
        Assert.Equal(new[] { (BarStyle.Clean, "Clean"), (BarStyle.Gel, "Glass gel"), (BarStyle.Cells, "Cells") },
            BarStyles.All);
    }
}
