using FourC.Cmms.Core.Purchasing;

namespace FourC.Cmms.Tests;

/// <summary>0.18.0: whole packs, cost per piece and prices between packs.</summary>
public class PackMathTests
{
    [Fact]
    public void Packs_round_up_to_whole_packs_and_the_minimum()
    {
        Assert.Equal(1m, PackMath.Packs(20, 100, 1));
        Assert.Equal(3m, PackMath.Packs(250, 100, 1));
        Assert.Equal(1m, PackMath.Packs(100, 100, 1));
        Assert.Equal(2m, PackMath.Packs(20, 100, 2));
        Assert.Equal(2m, PackMath.Packs(5, 2.5m, 1));
        Assert.Equal(1m, PackMath.Packs(0, 100, 1));
        Assert.Equal(3m, PackMath.Packs(3, 1, 1));
    }

    [Fact]
    public void Cost_per_piece_divides_the_pack_price()
    {
        Assert.Equal(0.5m, PackMath.CostPerPiece(50, 1, 100));
        Assert.Equal(218.25m, PackMath.CostPerPiece(45, 48.5m, 10));
        Assert.Equal(3.3333m, PackMath.CostPerPiece(10, 1, 3));
    }

    [Fact]
    public void Price_converts_between_packs_through_pieces()
    {
        Assert.Equal(5m, PackMath.PriceForPack(50, 100, 10));
        Assert.Equal(50m, PackMath.PriceForPack(5, 10, 100));
        Assert.Equal(7m, PackMath.PriceForPack(7, 1, 1));
    }
}
