using HydroExplorer.Core;
using HydroExplorer.Helpers;
using Microsoft.Win32;
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
            _clearSelectedPathCommand ??= new RelayCommand(async param => await ExecuteClearSelectedPath(param as string));


        

        private async Task ExecuteClearSelectedPath(string? layerType)
        {
            if (layerType is not ("Boundary" or "Sections" or "River" or "Subbasins" or "RasGeometry"))
                return;

            switch (layerType)
            {
                case "Boundary": SelectedBoundaryPath = null; break;
                case "Sections": SelectedSectionsPath = null; break;
                case "River": SelectedRiverPath = null; break;
                case "Subbasins": SelectedSubbasinsPath = null; break;
                case "RasGeometry": SelectedRasGeometryPath = null; break;
            }

            if (SettingsRepo is null) return;
            var settings = await SettingsRepo.GetSettingsFresh();

            if (!string.IsNullOrEmpty(settings.LastProjPath) &&
                settings.Projects.TryGetValue(settings.LastProjPath, out var activeProj))
            {
                switch (layerType)
                {
                    case "Boundary": activeProj.SpatialBndyPath = string.Empty; break;
                    case "Sections": activeProj.SpatialXsPath = string.Empty; break;
                    case "River": activeProj.SpatialRiverPath = string.Empty; break;
                }
                await SettingsRepo.SaveSettings(settings);
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

            string projKey = PathHelpers.NormalizeProjKey(settings.LastProjPath);

            // CRS resolution shares GeometryExportCoordinator's cache with XS/river/
            // standard BNDY export — if a zone's already been resolved for this
            // project, reuse it silently instead of re-guessing or re-prompting.
            // Confirmation is silenced project-wide (see GeometryExportCoordinator),
            // so a guess here is auto-accepted the same way.
            string sourcePrjPath = Path.ChangeExtension(sourcePath, ".prj");

            if (!File.Exists(sourcePrjPath))
            {
                int? epsg = await GeometryExportCoordinator.ResolveSourceEpsgFromShapefileAsync(
                    SettingsRepo, projKey, sourcePath, "project boundary source");

                if (epsg is not > 0)
                {
                    Application.Current.Dispatcher.Invoke(() => MessageBox.Show(
                        "This shapefile has no projection (.prj) file, and a Texas State Plane " +
                        "zone could not be determined from its coordinates.\n\n" +
                        "Add a .prj file manually before retrying.",
                        "Unknown Projection", MessageBoxButton.OK, MessageBoxImage.Warning));
                    return;
                }
            }

            string pathTMP = Path.Combine(@"C:\Temp", $"tmp_{Guid.NewGuid():N}.shp");

            try
            {
                await ExporterBndy.ExportBNDY(
                    pathSubBasins: sourcePath,
                    pathTMP: pathTMP,
                    pathBNDY: bndyPath,
                    settingsRepo: SettingsRepo,
                    projKey: projKey
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

            // Nothing selected yet for this layer type — fall back to the active
            // project's own folder (prefer its Spatial subfolder if one already
            // exists) instead of leaving the dialog to open wherever it last was.
            if (!Directory.Exists(initialDir) && SettingsRepo != null)
            {
                var settings = await SettingsRepo.GetSettings();
                if (!string.IsNullOrEmpty(settings.LastProjPath) &&
                    settings.Projects.TryGetValue(settings.LastProjPath, out var activeProj))
                {
                    string projDir = Path.GetDirectoryName(activeProj.ProjPath) ?? string.Empty;
                    string spatialDir = Path.Combine(projDir, "Spatial");

                    initialDir = Directory.Exists(spatialDir) ? spatialDir
                        : Directory.Exists(projDir) ? projDir
                        : string.Empty;
                }
            }

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


        protected TabViewModelBase()
        {
            EventBus.GeometryPathsResolved += OnGeometryPathsResolved;
            EventBus.ShpPathSelected += OnShpPathSelected;
        }

        

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

            var rasKeys = settings.Projects
                .Where(kv => (kv.Key.EndsWith(".rasmap", StringComparison.OrdinalIgnoreCase)
                           || kv.Key.EndsWith(".prj", StringComparison.OrdinalIgnoreCase))
                          && File.Exists(kv.Key))
                .OrderBy(kv => kv.Value.OpenOrder)
                .ToList();

            var rasRootDirs = rasKeys
                .Select(kv => kv.Value.ProjRoot)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // HMS-only entries: anything else with a live HmsPath, whose ProjRoot
            // isn't already covered by a RAS entry (avoids showing a duplicate row
            // for an HMS run that's actually just part of an existing RAS project).
            var hmsOnlyKeys = settings.Projects
                .Where(kv => !rasKeys.Any(r => r.Key.Equals(kv.Key, StringComparison.OrdinalIgnoreCase))
                          && !string.IsNullOrEmpty(kv.Value.HmsPath)
                          && File.Exists(kv.Value.HmsPath)
                          && !rasRootDirs.Contains(kv.Value.ProjRoot))
                .ToList();

            var allEntries = rasKeys
                .Concat(hmsOnlyKeys)
                .GroupBy(kv => kv.Value.ProjRoot, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(kv => kv.Value.OpenOrder).First())
                .OrderBy(kv => kv.Value.OpenOrder)
                .Select(kv => new RecentProjectEntry
                {
                    Key = kv.Key,
                    ProjName = !string.IsNullOrEmpty(kv.Value.ProjName)
                        ? kv.Value.ProjName
                        : Path.GetFileNameWithoutExtension(kv.Key),
                    LastOpened = kv.Value.LastOpened,
                    IsActive = kv.Key.Equals(activePath, StringComparison.OrdinalIgnoreCase)
                })
                .ToList();

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                RecentProjects.Clear();
                foreach (var entry in allEntries)
                    RecentProjects.Add(entry);
            });
        }
    }
}