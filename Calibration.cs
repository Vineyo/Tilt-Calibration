using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace tilt_calibrate;

public sealed record CalibrationSample(double PositionX, double PositionY, int TiltX, int TiltY, double Pressure);

public sealed record CalibrationCoefficients(double XSquared, double YSquared, double XLinear, double YLinear, double Cross)
{
    public Vector2 Predict(Vector2 tilt) => new(
        (float)(XSquared * tilt.X * Math.Abs(tilt.X) + XLinear * tilt.X - Cross * tilt.X * Math.Abs(tilt.Y)),
        (float)(YSquared * tilt.Y * Math.Abs(tilt.Y) + YLinear * tilt.Y - Cross * tilt.Y * Math.Abs(tilt.X)));
}

public sealed record CalibrationResult(
    CalibrationCoefficients Coefficients,
    double RootMeanSquaredError,
    int UniqueTilts,
    int Samples);

public sealed record CalibrationResultFile(
    CalibrationCoefficients Coefficients,
    double RootMeanSquaredError,
    int UniqueTilts,
    int Samples,
    DateTimeOffset TrainedAt);

public static class CalibrationTrainer
{
    public static CalibrationResult Train(IEnumerable<CalibrationSample> source)
    {
        var samples = source.ToArray();
        if (samples.Length < 12)
            throw new InvalidOperationException("Collect at least 12 samples before training.");

        var grouped = samples.GroupBy(sample => (sample.TiltX, sample.TiltY))
            .Select(group => new CalibrationSample(
                group.Average(sample => sample.PositionX),
                group.Average(sample => sample.PositionY),
                group.Key.TiltX,
                group.Key.TiltY,
                group.Average(sample => sample.Pressure)))
            .ToArray();
        if (grouped.Length < 6)
            throw new InvalidOperationException("Use more distinct pen angles before training.");

        var center = grouped.SingleOrDefault(sample => sample.TiltX == 0 && sample.TiltY == 0)
            ?? throw new InvalidOperationException(
                "No center samples were found. Collect samples with Tilt X = 0 and Tilt Y = 0 before training.");

        var ata = new double[5, 5];
        var atb = new double[5];
        foreach (var sample in grouped)
        {
            AddEquation(
                new[] { sample.TiltX * Math.Abs(sample.TiltX), 0d, sample.TiltX, 0d, -sample.TiltX * Math.Abs(sample.TiltY) },
                sample.PositionX - center.PositionX,
                ata,
                atb);
            AddEquation(
                new[] { 0d, sample.TiltY * Math.Abs(sample.TiltY), 0d, sample.TiltY, -sample.TiltY * Math.Abs(sample.TiltX) },
                sample.PositionY - center.PositionY,
                ata,
                atb);
        }

        var values = Solve(ata, atb);
        var coefficients = new CalibrationCoefficients(values[0], values[1], values[2], values[3], values[4]);
        var squaredError = grouped.Sum(sample =>
        {
            var predicted = coefficients.Predict(new Vector2(sample.TiltX, sample.TiltY));
            return Math.Pow(predicted.X - (sample.PositionX - center.PositionX), 2)
                + Math.Pow(predicted.Y - (sample.PositionY - center.PositionY), 2);
        });

        return new CalibrationResult(
            coefficients,
            Math.Sqrt(squaredError / (grouped.Length * 2)),
            grouped.Length,
            samples.Length);
    }

    private static void AddEquation(double[] row, double target, double[,] ata, double[] atb)
    {
        for (var i = 0; i < row.Length; i++)
        {
            atb[i] += row[i] * target;
            for (var j = 0; j < row.Length; j++)
                ata[i, j] += row[i] * row[j];
        }
    }

    private static double[] Solve(double[,] matrix, double[] vector)
    {
        var size = vector.Length;
        var values = (double[,])matrix.Clone();
        var result = (double[])vector.Clone();

        for (var column = 0; column < size; column++)
        {
            var pivot = column;
            for (var row = column + 1; row < size; row++)
                if (Math.Abs(values[row, column]) > Math.Abs(values[pivot, column]))
                    pivot = row;

            if (Math.Abs(values[pivot, column]) < 1e-10)
                throw new InvalidOperationException("The sample angles are too similar. Collect a wider range of tilts.");

            for (var j = column; j < size; j++)
                (values[column, j], values[pivot, j]) = (values[pivot, j], values[column, j]);
            (result[column], result[pivot]) = (result[pivot], result[column]);

            var divisor = values[column, column];
            for (var j = column; j < size; j++)
                values[column, j] /= divisor;
            result[column] /= divisor;

            for (var row = 0; row < size; row++)
            {
                if (row == column)
                    continue;

                var factor = values[row, column];
                for (var j = column; j < size; j++)
                    values[row, j] -= factor * values[column, j];
                result[row] -= factor * result[column];
            }
        }

        return result;
    }
}

public static partial class CalibrationFiles
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly Regex LogPattern = LogLineRegex();

    public static void SaveResult(string path, CalibrationResult result)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var file = new CalibrationResultFile(
            result.Coefficients,
            result.RootMeanSquaredError,
            result.UniqueTilts,
            result.Samples,
            DateTimeOffset.UtcNow);
        File.WriteAllText(path, JsonSerializer.Serialize(file, JsonOptions));
    }

    public static CalibrationResultFile LoadResult(string path) =>
        JsonSerializer.Deserialize<CalibrationResultFile>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException("The calibration result file is empty.");

    public static IEnumerable<CalibrationSample> ImportOpenTabletDriverLog(
        IEnumerable<string> lines,
        double minimumPressure = 0)
    {
        foreach (var line in lines)
        {
            var match = LogPattern.Match(line);
            if (!match.Success)
                continue;

            var values = match.Groups.Cast<Group>()
                .Skip(1)
                .Select(group => int.Parse(group.Value.Trim(), CultureInfo.InvariantCulture))
                .ToArray();
            if (values[2] >= minimumPressure)
                yield return new CalibrationSample(values[0], values[1], values[3], values[4], values[2]);
        }
    }

    [GeneratedRegex(@"Position:\[\s*([-+]?\d+)\s*,\s*([-+]?\d+)\s*\].*?Pressure:\s*([-+]?\d+).*?Tilt:\[\s*([-+]?\d+)\s*,\s*([-+]?\d+)\s*\]", RegexOptions.Compiled)]
    private static partial Regex LogLineRegex();
}
