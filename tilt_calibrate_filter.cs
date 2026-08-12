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

    public PipelinePosition Position => PipelinePosition.PreTransform;
    public event Action<IDeviceReport>? Emit;

    [Property("Extra result file location"), DefaultPropertyValue("")]
    [ToolTip("Optional path to tilt-calibration-result.json. Leave empty to read it from the plugin directory.")]
    public string ResultFilePath
    {
        get => _resultFilePath;
        set
        {
            _resultFilePath = value;
            _loadPending = true;
        }
    }

    public void Consume(IDeviceReport report)
    {
        EnsureResultLoaded();

        if (report is ITiltReport tiltReport && report is ITabletReport tabletReport && _coefficients is not null)
        {
            var offset = _coefficients.Predict(tiltReport.Tilt);
            tabletReport.Position -= offset;
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
