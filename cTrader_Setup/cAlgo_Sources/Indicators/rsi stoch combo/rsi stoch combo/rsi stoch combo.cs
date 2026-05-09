using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Indicators;

[Indicator(AccessRights = AccessRights.FullAccess, IsOverlay = false)]
public class rsistochcombo : Indicator
{
    [Parameter("RSI Length", DefaultValue = 14)]
    public int RsiLength { get; set; }

    [Parameter("RSI MA Length", DefaultValue = 14)]
    public int RsiMaLength { get; set; }

    [Parameter("%K Length", DefaultValue = 9)]
    public int PeriodK { get; set; }

    [Parameter("%D Smoothing", DefaultValue = 3)]
    public int PeriodD { get; set; }

    [Parameter("%K Smoothing", DefaultValue = 3)]
    public int SmoothK { get; set; }

    [Output("RSI", LineColor = "White", Thickness = 2)]
    public IndicatorDataSeries RsiPlot { get; set; }

    [Output("RSI MA", LineColor = "Yellow", Thickness = 1)]
    public IndicatorDataSeries RsiMaPlot { get; set; }

    [Output("Stoch %K", LineColor = "Lime", Thickness = 1)]
    public IndicatorDataSeries StochKPlot { get; set; }

    [Output("Stoch %D", LineColor = "Red", Thickness = 1)]
    public IndicatorDataSeries StochDPlot { get; set; }

    // RSI zones (70/30) — soft sky blue, ~50% alpha
    [Output("RSI 70", LineColor = "#806496FF", Thickness = 1)]
    public IndicatorDataSeries Rsi70 { get; set; }

    [Output("RSI 30", LineColor = "#806496FF", Thickness = 1)]
    public IndicatorDataSeries Rsi30 { get; set; }

    // Stoch zones (80/20) — soft pink, ~50% alpha
    [Output("Stoch 80", LineColor = "#80FF8FB0", Thickness = 1)]
    public IndicatorDataSeries Stoch80 { get; set; }

    [Output("Stoch 20", LineColor = "#80FF8FB0", Thickness = 1)]
    public IndicatorDataSeries Stoch20 { get; set; }

    // Mid 50 — faint dotted gray
    [Output("Mid 50", LineColor = "#60AAAAAA", LineStyle = LineStyle.Dots, Thickness = 1)]
    public IndicatorDataSeries Mid50 { get; set; }

    private RelativeStrengthIndex _rsi;
    private SimpleMovingAverage _rsiMa;
    private StochasticOscillator _stoch;

    protected override void Initialize()
    {
        _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, RsiLength);
        _rsiMa = Indicators.SimpleMovingAverage(_rsi.Result, RsiMaLength);
        _stoch = Indicators.StochasticOscillator(PeriodK, SmoothK, PeriodD, MovingAverageType.Simple);
    }

    public override void Calculate(int index)
    {
        RsiPlot[index] = _rsi.Result[index];
        RsiMaPlot[index] = _rsiMa.Result[index];
        StochKPlot[index] = _stoch.PercentK[index];
        StochDPlot[index] = _stoch.PercentD[index];

        Rsi70[index] = 70;
        Rsi30[index] = 30;
        Stoch80[index] = 80;
        Stoch20[index] = 20;
        Mid50[index] = 50;
    }
}
