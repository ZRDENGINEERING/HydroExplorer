using HydroExplorer.Core;
using HydroExplorer.Helpers;
using Microsoft.Win32;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;


namespace HydroExplorer.ViewModel.TabItem
{
    public abstract class TabViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public abstract string Header { get; set; }
        public object Content { get; set; } = new();

        protected IUserSettingsRepo? SettingsRepo { get; set; }


        // ── Collections ─────────────────────────────────────────────────────
        private ObservableCollection<RecentProjectEntry> _recentProjects = [];
        public ObservableCollection<RecentProjectEntry> RecentProjects
        {
            get => _recentProjects;
            protected set { _recentProjects = value; OnPropertyChanged(nameof(RecentProjects)); }
        }

        private ObservableCollection<GeometryPathEntry> _geometryPaths = [];
        public ObservableCollection<GeometryPathEntry> GeometryPaths
        {
            get => _geometryPaths;
            protected set
            {
                _geometryPaths = value;
                OnPropertyChanged(nameof(GeometryPaths));
            }
        }


        // ── Selected paths ───────────────────────────────────────────────────
        private GeometryPathEntry? _selectedSectionsPath;
        public GeometryPathEntry? SelectedSectionsPath
        {
            get => _selectedSectionsPath;
            set { _selectedSectionsPath = value; OnPropertyChanged(nameof(SelectedSectionsPath)); }
        }


        private GeometryPathEntry? _selectedRiverPath;
        public GeometryPathEntry? SelectedRiverPath
        {
            get => _selectedRiverPath;
            set { _selectedRiverPath = value; OnPropertyChanged(nameof(SelectedRiverPath)); }
        }


        private GeometryPathEntry? _selectedSubbasinsPath;
        public GeometryPathEntry? SelectedSubbasinsPath
        {
            get => _selectedSubbasinsPath;
            set { _selectedSubbasinsPath = value; OnPropertyChanged(nameof(SelectedSubbasinsPath)); }
        }

        private GeometryPathEntry? _selectedBoundaryPath;
        public GeometryPathEntry? SelectedBoundaryPath
        {
            get => _selectedBoundaryPath;
            set { _selectedBoundaryPath = value; OnPropertyChanged(nameof(SelectedBoundaryPath)); }
        }

        private GeometryPathEntry? _selectedRasGeometryPath;
        public GeometryPathEntry? SelectedRasGeometryPath
        {
            get => _selectedRasGeometryPath;
            set { _selectedRasGeometryPath = value; OnPropertyChanged(); }
        }

        // ── Commands ─────────────────────────────────────────────────────────
        private ICommand? _browseShpCommand;
        public ICommand BrowseShpCommand =>
            _browseShpCommand ??= new RelayCommand(param => ExecuteBrowseShp(param as string));

        private ICommand? _clearSelectedPathCommand;
        public ICommand ClearSelectedPathCommand =>
            _clearSelectedPathCommand ??= new RelayCommand(param => ExecuteClearSelectedPath(param as string));

        /// <summary>
        /// Clears the selected reference for one Active Paths field, without
        /// touching the underlying file on disk. Does not affect ProjectSettings'
        /// cached SpatialBndyPath/SpatialXsPath either — those are re-derived by
        /// MapOverView.BuildPaths regardless, so clearing here is purely a UI
        /// convenience for re-browsing without remembering the prior selection.
        /// </summary>
        private void ExecuteClearSelectedPath(string? layerType)
        {
            switch (layerType)
            {
                case "Boundary":
                    SelectedBoundaryPath = null;
                    break;
                case "Sections":
                    SelectedSectionsPath = null;
                    break;
                case "River":
                    SelectedRiverPath = null;
                    break;
                case "Subbasins":
                    SelectedSubbasinsPath = null;
                    break;
                case "RasGeometry":
                    SelectedRasGeometryPath = null;
                    break;
            }
        }

        private ICommand? _browseBndyCommand;
        public ICommand BrowseBndyCommand =>
            _browseBndyCommand ??= new RelayCommand(async _ => await ExecuteBrowseBndy());

        /// <summary>
        /// Lets the user pick any shapefile to derive a project boundary from, when
        /// BNDY.shp doesn't already exist for the active project. The source can be
        /// either a multi-polygon shapefile (dissolved into one boundary, same as the
        /// HMS subbasins flow in ExporterBndy) or an already-single-polygon boundary
        /// (the dissolve is a no-op in that case — unioning one geometry returns itself).
        /// </summary>
        private async Task ExecuteBrowseBndy()
        {
            if (SettingsRepo is null) return;

            var settings = await SettingsRepo.GetSettingsFresh();
            if (string.IsNullOrEmpty(settings.LastProjPath) ||
                !settings.Projects.TryGetValue(settings.LastProjPath, out var projSettings))
            {
                Application.Current.Dispatcher.Invoke(() => MessageBox.Show(
                    "No active project — open a project before setting a boundary.",
                    "No Active Project", MessageBoxButton.OK, MessageBoxImage.Warning));
                return;
            }

            string bndyPath = projSettings.SpatialBndyPath;
            if (string.IsNullOrEmpty(bndyPath))
            {
                Application.Current.Dispatcher.Invoke(() => MessageBox.Show(
                    "Project's Spatial path hasn't been resolved yet — open the project's map view first.",
                    "Spatial Path Not Set", MessageBoxButton.OK, MessageBoxImage.Warning));
                return;
            }

            if (File.Exists(bndyPath))
            {
                var overwrite = Application.Current.Dispatcher.Invoke(() => MessageBox.Show(
                    "A boundary (BNDY.shp) already exists for this project. Replace it?",
                    "Boundary Already Exists", MessageBoxButton.YesNo, MessageBoxImage.Question));

                if (overwrite != MessageBoxResult.Yes) return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "Select shapefile to derive project boundary from",
                Filter = "Shapefiles (*.shp)|*.shp|All Files (*.*)|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog() != true) return;

            string sourcePath = dialog.FileName;
            if (!File.Exists(sourcePath)) return;

            // No .prj sidecar means we don't know the source CRS — guess from the
            // shapefile's own raw extent rather than silently assuming a fixed zone,
            // since picking the wrong adjacent zone can place the result hundreds
            // of miles off (confirmed: 2277 vs 2278 mismatch landed near Nebraska).
            int? sourceEpsgOverride = null;
            string sourcePrjPath = Path.ChangeExtension(sourcePath, ".prj");

            if (!File.Exists(sourcePrjPath))
            {
                var extent = TryReadShpExtent(sourcePath);
                int guessedEpsg = extent != null ? GISUtil.GuessTexasStatePlaneZone(extent) : -1;

                string promptMessage = guessedEpsg > 0
                    ? $"This shapefile has no projection (.prj) file. Based on its coordinates, " +
                      $"it's likely EPSG:{guessedEpsg}. Use this projection?\n\n" +
                      $"(Choose No to cancel and add a .prj file manually.)"
                    : "This shapefile has no projection (.prj) file, and a Texas State Plane " +
                      "zone could not be determined from its coordinates.\n\n" +
                      "Cancel and add a .prj file manually before retrying.";

                if (guessedEpsg <= 0)
                {
                    Application.Current.Dispatcher.Invoke(() => MessageBox.Show(
                        promptMessage, "Unknown Projection", MessageBoxButton.OK, MessageBoxImage.Warning));
                    return;
                }

                var confirm = Application.Current.Dispatcher.Invoke(() => MessageBox.Show(
                    promptMessage, "Unknown Projection — Confirm Guess",
                    MessageBoxButton.YesNo, MessageBoxImage.Question));

                if (confirm != MessageBoxResult.Yes) return;

                sourceEpsgOverride = guessedEpsg;
            }

            string pathTMP = Path.Combine(@"C:\Temp", $"tmp_{Guid.NewGuid():N}.shp");

            try
            {
                await ExporterBndy.ExportBNDY(
                    pathSubBasins: sourcePath,
                    pathTMP: pathTMP,
                    pathBNDY: bndyPath,
                    sourceEpsgOverride: sourceEpsgOverride
                );

                if (!File.Exists(bndyPath))
                {
                    Application.Current.Dispatcher.Invoke(() => MessageBox.Show(
                        "Could not build a boundary from the selected file.",
                        "Boundary Build Failed", MessageBoxButton.OK, MessageBoxImage.Warning));
                    return;
                }

                var entry = new GeometryPathEntry
                {
                    ShpPath = bndyPath,
                    Name = Path.GetFileName(bndyPath),
                    Directory = Path.GetDirectoryName(bndyPath) ?? string.Empty,
                    LayerType = "Boundary",
                    LastOpened = DateTime.Now
                };

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var existing = GeometryPaths
                        .FirstOrDefault(p => string.Equals(p.ShpPath, bndyPath,
                            StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                        GeometryPaths.Remove(existing);

                    GeometryPaths.Insert(0, entry);
                    SelectedBoundaryPath = entry;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ExecuteBrowseBndy error: {ex.Message}");
                Application.Current.Dispatcher.Invoke(() => MessageBox.Show(
                    $"Failed to build boundary: {ex.Message}",
                    "Boundary Build Failed", MessageBoxButton.OK, MessageBoxImage.Error));
            }
            finally
            {
                GISUtil.DeleteShapefileIfExists(pathTMP);
            }
        }

        /// <summary>
        /// Reads just the bounding envelope of a shapefile's geometries, without
        /// any reprojection — used to get a raw-coordinate centroid for zone guessing
        /// when no .prj sidecar exists to tell us what those coordinates even are.
        /// </summary>
        private static Envelope? TryReadShpExtent(string shpPath)
        {
            try
            {
                var reader = new ShapefileDataReader(shpPath, new GeometryFactory());
                Envelope? extent = null;

                while (reader.Read())
                {
                    var env = reader.Geometry.EnvelopeInternal;
                    if (extent is null)
                        extent = env.Copy();
                    else
                        extent.ExpandToInclude(env);
                }
                reader.Close();

                return extent;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TryReadShpExtent failed for '{shpPath}' — {ex.Message}");
                return null;
            }
        }

        private async void ExecuteBrowseShp(string? layerType)
        {
            string initialDir = string.Empty;

            if (string.Equals(layerType, "Sections", StringComparison.OrdinalIgnoreCase))
                initialDir = SelectedSectionsPath?.Directory ?? string.Empty;
            else if (string.Equals(layerType, "River", StringComparison.OrdinalIgnoreCase))
                initialDir = SelectedRiverPath?.Directory ?? string.Empty;
            else if (string.Equals(layerType, "Subbasins", StringComparison.OrdinalIgnoreCase))
                initialDir = SelectedSubbasinsPath?.Directory ?? string.Empty;
            else if (string.Equals(layerType, "Boundary", StringComparison.OrdinalIgnoreCase))
                initialDir = SelectedBoundaryPath?.Directory ?? string.Empty;
            else if (string.Equals(layerType, "RasGeometry", StringComparison.OrdinalIgnoreCase))
                initialDir = SelectedRasGeometryPath?.Directory ?? string.Empty;

            var dialog = new OpenFileDialog
            {
                Title = $"Select {layerType} file",
                Filter = string.Equals(layerType, "RasGeometry", StringComparison.OrdinalIgnoreCase)
                    ? "HDF Files (*.hdf)|*.hdf|All Files (*.*)|*.*"
                    : "Shapefiles (*.shp)|*.shp|All Files (*.*)|*.*",
                Multiselect = false,
                InitialDirectory = Directory.Exists(initialDir) ? initialDir : string.Empty
            };


            if (dialog.ShowDialog() != true) return;

            string shpPath = dialog.FileName;

            // OpenFileDialog guarantees the file exists at selection time, but guard anyway
            // for consistency with the EventBus path below and in case of a race with deletion.
            if (!File.Exists(shpPath)) return;

            if (SettingsRepo != null)
            {
                var settings = await SettingsRepo.GetSettingsFresh();
                settings.ShpPaths[shpPath] = new ShpPathEntry
                {
                    LastOpened = DateTime.Now,
                    LayerType = layerType ?? string.Empty
                };

                // Also persist to the active project's own settings, so the manual
                // choice survives the next BuildPaths refresh.
                if (!string.IsNullOrEmpty(settings.LastProjPath) &&
                    settings.Projects.TryGetValue(settings.LastProjPath, out var activeProj))
                {
                    if (string.Equals(layerType, "Boundary", StringComparison.OrdinalIgnoreCase))
                        activeProj.SpatialBndyPath = shpPath;
                    else if (string.Equals(layerType, "Sections", StringComparison.OrdinalIgnoreCase))
                        activeProj.SpatialXsPath = shpPath;
                    else if (string.Equals(layerType, "River", StringComparison.OrdinalIgnoreCase))
                        activeProj.SpatialRiverPath = shpPath;
                }

                await SettingsRepo.SaveSettings(settings);
            }

            var entry = new GeometryPathEntry
            {
                ShpPath = shpPath,
                Name = Path.GetFileName(shpPath),
                Directory = Path.GetDirectoryName(shpPath) ?? string.Empty,
                LayerType = layerType ?? string.Empty,
                LastOpened = DateTime.Now
            };

            Application.Current.Dispatcher.Invoke(() =>
            {
                var existing = GeometryPaths
                    .FirstOrDefault(p => string.Equals(p.ShpPath, shpPath,
                        StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                    GeometryPaths.Remove(existing);

                GeometryPaths.Insert(0, entry);

                if (string.Equals(layerType, "Sections", StringComparison.OrdinalIgnoreCase))
                    SelectedSectionsPath = entry;
                else if (string.Equals(layerType, "River", StringComparison.OrdinalIgnoreCase))
                    SelectedRiverPath = entry;
                else if (string.Equals(layerType, "Subbasins", StringComparison.OrdinalIgnoreCase))
                    SelectedSubbasinsPath = entry;
                else if (string.Equals(layerType, "Boundary", StringComparison.OrdinalIgnoreCase))
                    SelectedBoundaryPath = entry;
                else if (string.Equals(layerType, "RasGeometry", StringComparison.OrdinalIgnoreCase))
                    SelectedRasGeometryPath = entry;
            });
        }


        // ── EventBus ─────────────────────────────────────────────────────────
        protected TabViewModelBase()
        {
            EventBus.GeometryPathsResolved += OnGeometryPathsResolved;
            EventBus.ShpPathSelected += OnShpPathSelected;

        }

        /// <summary>
        /// When a .shp is selected directly in the file tree, infer its Active Paths
        /// role from the filename (BNDY → Boundary, XS → Sections, basin → Subbasins).
        /// Files that don't match a known pattern are left alone — the tree remains
        /// usable for general browsing without forcing every .shp into a role.
        /// </summary>
        private void OnShpPathSelected(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            string fileName = Path.GetFileNameWithoutExtension(path);

            string? layerType = null;
            if (fileName.Contains("BNDY", StringComparison.OrdinalIgnoreCase))
                layerType = "Boundary";
            else if (fileName.Contains("XS", StringComparison.OrdinalIgnoreCase))
                layerType = "Sections";
            else if (fileName.Contains("basin", StringComparison.OrdinalIgnoreCase))
                layerType = "Subbasins";

            if (layerType is null) return;

            var entry = new GeometryPathEntry
            {
                ShpPath = path,
                Name = Path.GetFileName(path),
                Directory = Path.GetDirectoryName(path) ?? string.Empty,
                LayerType = layerType,
                LastOpened = DateTime.Now
            };

            Application.Current.Dispatcher.Invoke(() =>
            {
                var existing = GeometryPaths
                    .FirstOrDefault(p => string.Equals(p.ShpPath, path,
                        StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                    GeometryPaths.Remove(existing);

                GeometryPaths.Insert(0, entry);

                switch (layerType)
                {
                    case "Boundary":
                        SelectedBoundaryPath = entry;
                        break;
                    case "Sections":
                        SelectedSectionsPath = entry;
                        break;
                    case "River":
                        SelectedRiverPath = entry;
                        break;
                    case "Subbasins":
                        SelectedSubbasinsPath = entry;
                        break;
                }
            });
        }

        private void OnGeometryPathsResolved(string pathSubBasins, string pathXS, string pathRiver, string pathBNDY)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Only populate Selected*Path when the file actually exists on disk.
                // EventBus.GeometryPathsResolved may fire with "expected" paths before
                // export/auto-discovery has finished writing the file, or for projects
                // that never produce one of these outputs at all.

                if (!string.IsNullOrEmpty(pathSubBasins) && File.Exists(pathSubBasins))
                {
                    SelectedSubbasinsPath = new GeometryPathEntry
                    {
                        ShpPath = pathSubBasins,
                        Name = Path.GetFileName(pathSubBasins),
                        Directory = Path.GetDirectoryName(pathSubBasins) ?? string.Empty,
                        LayerType = "Subbasins"
                    };
                }
                else
                {
                    SelectedSubbasinsPath = null;
                }


                if (!string.IsNullOrEmpty(pathXS) && File.Exists(pathXS))
                {
                    SelectedSectionsPath = new GeometryPathEntry
                    {
                        ShpPath = pathXS,
                        Name = Path.GetFileName(pathXS),
                        Directory = Path.GetDirectoryName(pathXS) ?? string.Empty,
                        LayerType = "Sections"
                    };
                }
                else
                {
                    SelectedSectionsPath = null;
                }


                if (!string.IsNullOrEmpty(pathRiver) && File.Exists(pathRiver))
                {
                    SelectedRiverPath = new GeometryPathEntry
                    {
                        ShpPath = pathRiver,
                        Name = Path.GetFileName(pathRiver),
                        Directory = Path.GetDirectoryName(pathRiver) ?? string.Empty,
                        LayerType = "River"
                    };
                }
                else
                {
                    SelectedRiverPath = null;
                }




                if (!string.IsNullOrEmpty(pathBNDY) && File.Exists(pathBNDY))
                {
                    SelectedBoundaryPath = new GeometryPathEntry
                    {
                        ShpPath = pathBNDY,
                        Name = Path.GetFileName(pathBNDY),
                        Directory = Path.GetDirectoryName(pathBNDY) ?? string.Empty,
                        LayerType = "Boundary"
                    };
                }
                else
                {
                    SelectedBoundaryPath = null;
                }
            });
        }


        // ── Load ─────────────────────────────────────────────────────────────
        protected async Task LoadRecentProjectsAsync(string? activePathOverride = null)
        {
            var settings = await SettingsRepo!.GetSettingsFresh();
            string activePath = activePathOverride ?? settings.LastProjPath;


            var rasEntries = settings.Projects
                .Where(kv => kv.Key.EndsWith(".rasmap", StringComparison.OrdinalIgnoreCase)
                    || kv.Key.EndsWith(".prj", StringComparison.OrdinalIgnoreCase))
                .Where(kv => File.Exists(kv.Key))
                .OrderBy(kv => kv.Value.OpenOrder)
                .Select(kv => new RecentProjectEntry
                {
                    Name = !string.IsNullOrEmpty(kv.Value.ProjName)
                        ? kv.Value.ProjName
                        : Path.GetFileNameWithoutExtension(kv.Key),
                    ProjName = !string.IsNullOrEmpty(kv.Value.ProjName)
                        ? kv.Value.ProjName
                        : Path.GetFileNameWithoutExtension(kv.Key),
                    FilePath = kv.Value.ProjRoot,  // ← use kv.Value not proj
                    ProjPath = kv.Key,
                    LastOpened = kv.Value.LastOpened,
                    OpenOrder = kv.Value.OpenOrder,
                    ProjectType = "RAS",
                    HmsRunFile = File.Exists(kv.Value.HmsPath) ? kv.Value.HmsPath : null
                })
                .ToList();

            var rasRootDirs = rasEntries
            .Select(e => e.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var hmsOnlyEntries = settings.HmsProjects
                .Where(kv => File.Exists(kv.Key))
                .Where(kv => settings.Projects.TryGetValue(kv.Key, out var p)
                    && !rasRootDirs.Contains(p.ProjRoot))
                .Select(kv => {
                    settings.Projects.TryGetValue(kv.Key, out var p);
                    return new RecentProjectEntry
                    {
                        Name = p?.ProjName ?? Path.GetFileNameWithoutExtension(kv.Key),
                        ProjName = p?.ProjName ?? Path.GetFileNameWithoutExtension(kv.Key),
                        FilePath = p?.ProjRoot ?? string.Empty,
                        ProjPath = kv.Key,
                        LastOpened = kv.Value,
                        OpenOrder = int.MaxValue,
                        ProjectType = "HMS",
                        HmsRunFile = kv.Key
                    };
                })
                .ToList();


            var allEntries = rasEntries
                .Concat(hmsOnlyEntries)
                .GroupBy(e => e.FilePath, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(e => e.OpenOrder).First())
                .ToList();

            foreach (var entry in allEntries)
                entry.IsActive = entry.ProjPath.Equals(activePath, StringComparison.OrdinalIgnoreCase);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                RecentProjects.Clear();
                foreach (var entry in allEntries)
                    RecentProjects.Add(entry);
            });

        }
    }
}