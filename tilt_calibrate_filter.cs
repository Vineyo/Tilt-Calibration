using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using System.Numerics;

namespace tilt_calibrate;

[PluginName("Tilt calibration")]
public class TiltCalibration : IPositionedPipelineElement<IDeviceReport>
{
    public PipelinePosition Position => PipelinePosition.PreTransform;

    public event Action<IDeviceReport>? Emit;

    [Property("X^2 Coefficient"),DefaultPropertyValue(0.05)]
    public float X_sqr_coeff { get; set; }
    [Property("Y^2 Coefficient"),DefaultPropertyValue(0.05)]
    public float Y_sqr_coeff { get; set; }
    [Property("X Coefficient"),DefaultPropertyValue(0.1)]
    public float X_coeff { get; set; }
    [Property("Y Coefficient"),DefaultPropertyValue(0.1)]
    public float Y_coeff { get; set; }
    [Property("XY Coefficient"),DefaultPropertyValue(0.05),ToolTip("Suppress the diagonal movement")]
    public float XY_coeff { get; set; }
    // Any reports, including positionals, are received here
    public void Consume(IDeviceReport report)
    {
        var tilt = new Vector2(0, 0);
        if(report is ITiltReport tiltReport)
        {
            // Get the tilt values from the report
            (tilt.X, tilt.Y) = (tiltReport.Tilt.X, tiltReport.Tilt.Y);
        
            if(report is ITabletReport tabletReport)
            {
                // Filter the tilt values using the model
                var offset = Filter(tilt);
                
                // Update the report with the filtered values
                var newPosition = new Vector2(tabletReport.Position.X-offset.X, tabletReport.Position.Y-offset.Y);
                tabletReport.Position= newPosition;
                // SendNotification($"Tilt: {tilt.X}, {tilt.Y}");
                var returnReport = tabletReport;
                Emit?.Invoke(returnReport);
            }
        }
        else{
            // Emit the report without modification
            Emit?.Invoke(report);
        }
    }
    
    public Vector2 Filter(Vector2 input)
    {
        var result= new Vector2(
            X_sqr_coeff * input.X * Math.Abs(input.X)+ 
            X_coeff* input.X -
            XY_coeff* input.X * Math.Abs(input.Y),
            Y_sqr_coeff * input.Y * Math.Abs(input.Y) + 
            Y_coeff * input.Y-
            XY_coeff * input.Y * Math.Abs(input.X)
        );

        return result;
    }
}
