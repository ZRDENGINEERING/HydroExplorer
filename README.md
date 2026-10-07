# HydroExplorer (BETA)

A Windows desktop data explorer for hydrologic and hydraulic modeling projects. Point it at a **HEC-RAS** or **HEC-HMS** project and browse results, compare plans, plot them, and see the model on a map, without opening the modeling software.

<div align="center">
  <p>
    <a href="https://github.com/ZRDENGINEERING/HydroExplorer/issues/new?labels=bug&template=bug-report---.md">Report Bug</a>
    &middot;
    <a href="https://github.com/ZRDENGINEERING/HydroExplorer/issues/new?labels=enhancement&template=feature-request---.md">Request Feature</a>
  </p>
</div>

<!-- Add a screenshot here once you have one, e.g. ![HydroExplorer](Images/screenshot.png) -->

<details>
  <summary>Table of Contents</summary>
  <ol>
    <li><a href="#features">Features</a></li>
    <li><a href="#getting-started">Getting Started</a></li>
    <li><a href="#usage">Usage</a></li>
    <li><a href="#built-with">Built With</a></li>
    <li><a href="#roadmap">Roadmap</a></li>
    <li><a href="#contributing">Contributing</a></li>
    <li><a href="#disclaimer">Disclaimer</a></li>
    <li><a href="#license">License</a></li>
    <li><a href="#contact">Contact</a></li>
  </ol>
</details>

## Features

**HEC-RAS (reads `.p##.hdf` plan files)**
- Side-by-side comparison of two plans (Plan A / Plan B): discharge, water surface elevation, and the delta between them, by river, reach, station, and profile.
- Steady-flow profile selection and WSEL / flow plots.
- Map view of the project with cross sections, river centerlines, and boundary exported as working shapefiles.
- **2D models (experimental):** 2D and mixed 1D/2D projects are detected automatically. 1D-only views (RAS Tables, profile picker) are disabled with an explanation for 2D-only plans, and the Map tab shows a sampled overlay of 2D cell results (maximum water surface elevation, or minimum terrain if the plan has no results), colored low to high.

**HEC-HMS (reads `.hms` projects and `.dss` files)**
- Browse DSS records by pathname part (basin, location, parameter, date, interval, version).
- Hydrograph and hyetograph charts, plus rainfall excess and loss views.
- Subbasin drainage area read from the HEC-HMS basin file.

**Reasonableness checks**
- Log-Pearson III and return-period plots.
- Creager envelope curve, plotting the project's drainage area and peak discharge against the regional envelope.
- TxDOT Omega EM regression estimates (see the [Disclaimer](#disclaimer)).
- USGS gage lookup shown on the map.

**Other**
- Recent-projects list and per-project settings (drainage area, coordinate system, and so on).
- Reads the coordinate system stored in the geometry HDF, including custom projections such as NAD83 / Texas Centric Albers Equal Area. If none is stored, it falls back to guessing a Texas State Plane zone.

## Getting Started

HydroExplorer runs locally on **Windows (x64)**. It is a WPF application, so it does not run on macOS or Linux.

Some features call public web services and need an internet connection: basemap tiles, USGS gage lookup, and the USGS Watershed Boundary Dataset fallback used to generate a project boundary.

### Option 1: Download a release

1. Download the latest build from the [Releases](https://github.com/ZRDENGINEERING/HydroExplorer/releases) page.
2. Unzip it and run `HydroExplorer.exe`.

### Option 2: Build from source

**Prerequisites**
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Optional: Visual Studio with the ".NET desktop development" workload

**Steps**

1. Clone the repo:
   ```sh
   git clone https://github.com/ZRDENGINEERING/HydroExplorer.git
   cd HydroExplorer
   ```
2. Build and run:
   ```sh
   dotnet run --project HydroExplorer.csproj
   ```
   Or open `hydroExplorer.slnx` in Visual Studio and press F5.
3. To produce a self-contained build:
   ```sh
   dotnet publish HydroExplorer.csproj -c Release -r win-x64 --self-contained
   ```

## Usage

1. Open a project folder containing a HEC-RAS `.prj` file or a HEC-HMS `.hms` file, or pick one from the recent-projects list.
2. Choose the **active paths** (Plan A, and optionally Plan B for comparison) for the HEC-RAS and/or HEC-HMS side of the project.
3. Work through the main areas:
   - **Hydraulics**: plan, profile, and reach selection for HEC-RAS results.
   - **Hydrology**: DSS record browser and HEC-HMS results.
   - **Geometry**: project map and spatial data.
4. Use the tabs on each pane (for example **Info**, **RAS Tables**, **HMS Charts**, **Publish**) to view tables, charts, and exports.

Automated exports (for example shapefiles in a `HydroXSpatial` folder next to the project) are working files for use inside this application. They are not part of your HEC-RAS or HEC-HMS model.

## Built With

- C# / .NET 10 / WPF
- [PureHDF](https://github.com/Apollo3zehn/PureHDF): HEC-RAS HDF5 results
- [Hec.Dss](https://www.nuget.org/packages/Hec.Dss): HEC-HMS DSS files
- [Mapsui](https://mapsui.com/) and SkiaSharp: map rendering
- [OxyPlot](https://oxyplot.github.io/): charts
- [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite) and [ProjNET](https://github.com/NetTopologySuite/ProjNet4GeoAPI): shapefiles and coordinate transformations

## Roadmap

- [x] Beta release
- [x] HEC-RAS 2D: detection, 1D-view guarding, basic cell-result map overlay
- [ ] HEC-RAS 2D: full geometry and results support (mesh, per-cell and profile-line results)
- [ ] PDF output
- [ ] DXF / CAD output

See the [open issues](https://github.com/ZRDENGINEERING/HydroExplorer/issues) for a full list of proposed features and known issues.

## Contributing

Contributions and suggestions are welcome. Reach out, or open an issue with the `enhancement` or `feature request` tag and we will review it. Pull requests are welcome too:

1. Fork the repo
2. Create a feature branch (`git checkout -b feature/my-feature`)
3. Commit your changes
4. Push the branch and open a pull request

## Disclaimer

**HydroExplorer is beta software provided for internal testing and workflow support only. It is not a substitute for professional engineering judgment.**

- This tool is under active development and may contain bugs, incomplete features, or calculation errors.
- Outputs (geometry exports, plots, regression estimates, GIS layers, reports, etc.) have not been independently verified or validated against industry-accepted software and should not be relied upon for design, permitting, regulatory submittal, or construction without independent review by a licensed professional engineer.
- Automated exports (e.g., shapefiles in `HydroXSpatial`) are working files generated for use within this application and are not part of the official HEC-RAS/HEC-HMS model or project deliverable.
- Any regression methods, coefficients, or empirical relationships (e.g., TxDOT Omega EM) implemented here should be independently verified against the source publication before use in any engineering analysis.
- The 2D results overlay is a sampled visual aid, not a results viewer. It does not replace RAS Mapper or your model's own outputs.
- Use of this software is at the user's own risk. The developer(s) assume no liability for damages, losses, or errors resulting from its use or misuse.

By using this software, you acknowledge that it is a beta tool intended to assist, not replace, sound engineering practice and professional judgment.

## License

Distributed under the MIT License. See `LICENSE.txt` for more information.

## Contact

frankd@zrdeng.com

https://zrdeng.com

Project Link: [https://github.com/ZRDENGINEERING/HydroExplorer](https://github.com/ZRDENGINEERING/HydroExplorer)
