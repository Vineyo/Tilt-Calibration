# Tilt Calibration for OpenTabletDriver

An [OpenTabletDriver](https://github.com/OpenTabletDriver/OpenTabletDriver) plugin that measures and corrects cursor movement caused by changes in pen tilt.

## Requirements

- OpenTabletDriver 0.6.5.1 or later
- A tablet and pen that report tilt
- The .NET 8 runtime used by OpenTabletDriver

## Installation

1. Download `tilt_calibrate.dll` from the latest GitHub release.
2. In OpenTabletDriver, open **Plugins** and install the DLL.
3. Restart OpenTabletDriver if prompted.

## Calibration

### 1. Record the tablet data

Open OpenTabletDriver's **Tablet Debugger** and start recording. Keep the pen tip at one fixed physical point throughout the recording; using a simple support to hold the tip in place is recommended.

While applying normal writing pressure:

- Slowly tilt the pen through a wide range of angles and directions.
- Pass through the vertical position multiple times to ensure that enough data is collected for the center point.

Stop recording when the available tilt range has been covered. OpenTabletDriver saves a file named similar to `tablet-data_****.txt` in its [log directory](https://opentabletdriver.net/Wiki/Documentation/Logs#daemon-log).

Common log locations include:

- Linux: `~/.config/OpenTabletDriver`
- Windows: `%LOCALAPPDATA%\OpenTabletDriver`

### 2. Train the calibration

1. In OpenTabletDriver, open **Tools** and enable **Tilt calibration trainer**.
2. Set **tablet-data.txt path** to the recorded file's full path.
3. Adjust **Minimum pressure** if necessary. The default is `50`; samples below this value are ignored.
4. Click **Apply**.

Training with more than 20,000 samples takes less than one second. After successful training, the plugin saves `tilt-calibration-result.json` beside the installed DLL. This file contains the fitted calibration coefficients and training information.

Training requires:

- At least 12 accepted samples
- At least 6 distinct `(Tilt X, Tilt Y)` pairs
- At least one exact `(0, 0)` tilt group to establish the center position

### 3. Enable the filter

1. Open **Filters** and enable **Tilt calibration**.
2. Click **Apply**.

The filter automatically loads `tilt-calibration-result.json` and corrects incoming tablet positions. To recalibrate, run the trainer again and then reapply the filter so it reloads the result.

### Tablets with built-in hover correction

For tablets such as the Huion Kamvas Pro 16 that already compensate for tilt while hovering, enable **Stroke-relative correction** in the **Tilt calibration** filter and click **Apply**. This option is off by default.

In this mode, hover reports (zero pressure) are unchanged. The first report with positive pressure and tilt data establishes the stroke's starting offset and is also unchanged. Later reports subtract only the change in predicted offset since that starting tilt. Lifting the pen resets the reference for the next stroke.

Use the same calibration file and training procedure. For best results, keep the pen tip fixed, start with the pen upright, and maintain contact while collecting the tilt sweep. The filter subtracts `Predict(currentTilt) - Predict(startingTilt)`; it does not predict from the tilt difference, because the fitted model is nonlinear. This mode preserves the tablet's starting position rather than correcting an inaccurate initial placement.

## Custom result location

Normally, no result path needs to be configured. If the plugin cannot resolve its installation directory, set **Extra result file location** to the same absolute JSON path in both **Tilt calibration trainer** and **Tilt calibration**.

All input and output paths must be accessible to the OpenTabletDriver daemon. If multiple copies of `tilt_calibrate.dll` are installed, remove the duplicates or configure an explicit result path.

## How it works

The trainer groups samples with identical integer tilt values and averages their reported positions. The average position of the `(0, 0)` group becomes the center. It then uses least-squares fitting to determine five coefficients for this second-order model:

```text
offset.x = xSquared * tilt.x * abs(tilt.x)
         + xLinear * tilt.x
         - cross * tilt.x * abs(tilt.y)

offset.y = ySquared * tilt.y * abs(tilt.y)
         + yLinear * tilt.y
         - cross * tilt.y * abs(tilt.x)
```

For every tablet report containing tilt data, the filter predicts this offset and subtracts it from the reported position.

## Troubleshooting

- **No matching tablet samples were found:** Verify the data-file path and lower **Minimum pressure** if valid samples are being excluded.
- **No center samples were found:** Record again and include samples with both tilt values exactly equal to `0`.
- **The sample angles are too similar:** Record a wider range of tilt angles and directions.
- **Could not load calibration result:** Run the trainer first, verify the JSON file exists, and ensure both components use the same result location.

## Build from source

Install the .NET 8 SDK, clone this repository, and run:

```bash
dotnet restore
dotnet build -c Release
```

The compiled plugin is written to `bin/Release/net8.0/tilt_calibrate.dll`. Only this DLL needs to be installed in OpenTabletDriver.

## License

This project is available under the [MIT License](LICENSE).
