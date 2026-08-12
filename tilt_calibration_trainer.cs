using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;

namespace tilt_calibrate;

[PluginName("Tilt calibration trainer")]
public sealed class TiltCalibrationTrainer : ITool
{
    [Property("tablet-data.txt path"), DefaultPropertyValue("tablet-data.txt")]
    [ToolTip("Path visible to the OpenTabletDriver daemon. Use the Tablet Debugger to create this file.")]
    public string TabletDataPath { get; set; } = "tablet-data.txt";

    [Property("Minimum pressure"), DefaultPropertyValue(50)]
    [ToolTip("Samples below this pressure are ignored.")]
    public float MinimumPressure { get; set; } = 50;

    [Property("Extra result file location"), DefaultPropertyValue("")]
    [ToolTip("Optional output path. Leave empty to save tilt-calibration-result.json in the plugin directory.")]
    public string ResultFilePath { get; set; } = string.Empty;

    public bool Initialize()
    {
        try
        {
            var inputPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(TabletDataPath));
            if (!File.Exists(inputPath))
                throw new FileNotFoundException("The tablet data file does not exist.", inputPath);

            var samples = CalibrationFiles.ImportOpenTabletDriverLog(
                File.ReadLines(inputPath), MinimumPressure).ToArray();
            if (samples.Length == 0)
                throw new InvalidDataException("No matching tablet samples were found. Check the file and minimum pressure.");

            var result = CalibrationTrainer.Train(samples);
            var outputPath = PluginResultPath.Resolve(ResultFilePath);
            CalibrationFiles.SaveResult(outputPath, result);

            Log.WriteNotify("Tilt calibration trainer",
                $"Training complete. The calibration result was saved to:\n{outputPath}",
                LogLevel.Info);
            return true;
        }
        catch (Exception ex)
        {
            Log.WriteNotify("Tilt calibration trainer", $"Training failed: {ex.Message}", LogLevel.Error);
            return false;
        }
    }

    public void Dispose()
    {
    }
}

internal static class PluginResultPath
{
    private const string DefaultFileName = "tilt-calibration-result.json";

    public static string Resolve(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(overridePath));

        var assemblyPath = typeof(PluginResultPath).Assembly.Location;
        if (!string.IsNullOrWhiteSpace(assemblyPath))
            return Path.Combine(Path.GetDirectoryName(assemblyPath)!, DefaultFileName);

        var pluginDirectory = GetOpenTabletDriverPluginDirectory();
        var assemblyFileName = $"{typeof(PluginResultPath).Assembly.GetName().Name}.dll";
        var installedCopies = Directory.EnumerateFiles(
            pluginDirectory, assemblyFileName, SearchOption.AllDirectories).ToArray();

        return installedCopies.Length switch
        {
            1 => Path.Combine(Path.GetDirectoryName(installedCopies[0])!, DefaultFileName),
            0 => throw new InvalidOperationException(
                $"OTD's plugin directory is '{pluginDirectory}', but {assemblyFileName} was not found there. Enter an extra result file location."),
            _ => throw new InvalidOperationException(
                $"Multiple copies of {assemblyFileName} were found under '{pluginDirectory}'. Remove duplicates or enter an extra result file location.")
        };
    }

    private static string GetOpenTabletDriverPluginDirectory()
    {
        var appInfoType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("OpenTabletDriver.Desktop.AppInfo", false))
            .FirstOrDefault(type => type is not null)
            ?? throw new InvalidOperationException(
                "OpenTabletDriver's application information is unavailable. Enter an extra result file location.");

        var current = appInfoType.GetProperty("Current")?.GetValue(null)
            ?? throw new InvalidOperationException(
                "OpenTabletDriver's application information is unavailable. Enter an extra result file location.");
        var directory = appInfoType.GetProperty("PluginDirectory")?.GetValue(current) as string;

        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException(
                "OpenTabletDriver's plugin directory is unavailable. Enter an extra result file location.");

        return Path.GetFullPath(directory);
    }
}
