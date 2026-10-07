using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using System.Numerics;

namespace tilt_calibrate;

[PluginName("Tilt calibration")]
public sealed class TiltCalibration : IPositionedPipelineElement<IDeviceReport>
{
    private CalibrationCoefficients? _coefficients;
    private string _resultFilePath = string.Empty;
    private bool _loadPending = true;
    private bool _strokeRelative;
    private Vector2? _strokeStartOffset;

    public PipelinePosition Position => PipelinePosition.PreTransform;
    public event Action<IDeviceReport>? Emit;

    [Property("Stroke-relative correction"), DefaultPropertyValue(false)]
    [ToolTip("For tablets with built-in hover tilt correction. Leave hovering unchanged and correct tilt changes relative to the start of each stroke.")]
    public bool StrokeRelative
    {
        get => _strokeRelative;
        set
        {
            _strokeRelative = value;
            _strokeStartOffset = null;
        }
    }

    [Property("Extra result file location"), DefaultPropertyValue("")]
    [ToolTip("Optional path to tilt-calibration-result.json. Leave empty to read it from the plugin directory.")]
    public string ResultFilePath
    {
        get => _resultFilePath;
        set
        {
            _resultFilePath = value;
            _loadPending = true;
            _strokeStartOffset = null;
        }
    }

    public void Consume(IDeviceReport report)
    {
        EnsureResultLoaded();

        if (report is ITabletReport tabletReport)
        {
            // Pen-up reports must reset the stroke even when they have no tilt data.
            if (tabletReport.Pressure == 0)
                _strokeStartOffset = null;

            if (report is ITiltReport tiltReport && _coefficients is not null
                && (!StrokeRelative || tabletReport.Pressure > 0))
            {
                var offset = _coefficients.Predict(tiltReport.Tilt);
                if (StrokeRelative)
                {
                    _strokeStartOffset ??= offset;
                    offset -= _strokeStartOffset.Value;
                }
                tabletReport.Position -= offset;
            }
        }

        Emit?.Invoke(report);
    }

    private void EnsureResultLoaded()
    {
        if (!_loadPending)
            return;

        _loadPending = false;
        try
        {
            var path = PluginResultPath.Resolve(_resultFilePath);
            _coefficients = CalibrationFiles.LoadResult(path).Coefficients;
        }
        catch (Exception ex)
        {
            _coefficients = null;
            Log.WriteNotify("Tilt calibration", $"Could not load calibration result: {ex.Message}", LogLevel.Error);
        }
    }
}
