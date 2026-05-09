using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Indicators;

[Indicator(AccessRights = AccessRights.FullAccess, IsOverlay = true)]
public class emaandatroverlay : Indicator
{
    [Parameter("ATR Length", DefaultValue = 14)]
    public int AtrLength { get; set; }

    [Parameter("ATR Buffer Multiplier", DefaultValue = 0.5)]
    public double AtrBufferMult { get; set; }

    [Output("EMA 5", LineColor = "Red", Thickness = 1)]
    public IndicatorDataSeries Ema5Plot { get; set; }

    [Output("EMA 15", LineColor = "Orange", Thickness = 1)]
    public IndicatorDataSeries Ema15Plot { get; set; }

    [Output("EMA 35", LineColor = "Yellow", Thickness = 1)]
    public IndicatorDataSeries Ema35Plot { get; set; }

    [Output("EMA 89", LineColor = "Cyan", Thickness = 1)]
    public IndicatorDataSeries Ema89Plot { get; set; }

    [Output("EMA 200", LineColor = "Blue", Thickness = 2)]
    public IndicatorDataSeries Ema200Plot { get; set; }

    [Output("ATR Upper", LineColor = "Red", LineStyle = LineStyle.Dots, Thickness = 1)]
    public IndicatorDataSeries AtrUpperPlot { get; set; }

    [Output("ATR Lower", LineColor = "Lime", LineStyle = LineStyle.Dots, Thickness = 1)]
    public IndicatorDataSeries AtrLowerPlot { get; set; }

    private ExponentialMovingAverage _ema5;
    private ExponentialMovingAverage _ema15;
    private ExponentialMovingAverage _ema35;
    private ExponentialMovingAverage _ema89;
    private ExponentialMovingAverage _ema200;
    private AverageTrueRange _atr;

    protected override void Initialize()
    {
        _ema5 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 5);
        _ema15 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 15);
        _ema35 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 35);
        _ema89 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 89);
        _ema200 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 200);
        _atr = Indicators.AverageTrueRange(AtrLength, MovingAverageType.Simple);
    }

    public override void Calculate(int index)
    {
        Ema5Plot[index] = _ema5.Result[index];
        Ema15Plot[index] = _ema15.Result[index];
        Ema35Plot[index] = _ema35.Result[index];
        Ema89Plot[index] = _ema89.Result[index];
        Ema200Plot[index] = _ema200.Result[index];

        var buffer = _atr.Result[index] * AtrBufferMult;
        AtrUpperPlot[index] = Bars.HighPrices[index] + buffer;
        AtrLowerPlot[index] = Bars.LowPrices[index] - buffer;
    }
}
