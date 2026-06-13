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

        // ── Commands ─────────────────────────────────────────────────────────
        private ICommand? _browseShpCommand;
        public ICommand BrowseShpCommand =>
            _browseShpCommand ??= new RelayCommand(param => ExecuteBrowseShp(param as string));

        private async void ExecuteBrowseShp(string? layerType)
        {
            string initialDir = string.Empty;

            if (string.Equals(layerType, "Sections", StringComparison.OrdinalIgnoreCase))
                initialDir = SelectedSectionsPath?.Directory ?? string.Empty;
            else if (string.Equals(layerType, "Subbasins", StringComparison.OrdinalIgnoreCase))
                initialDir = SelectedSubbasinsPath?.Directory ?? string.Empty;
            else if (string.Equals(layerType, "Boundary", StringComparison.OrdinalIgnoreCase))
                initialDir = SelectedBoundaryPath?.Directory ?? string.Empty;

            var dialog = new OpenFileDialog
            {
                Title = $"Select {layerType} Shapefile",
                Filter = "Shapefiles (*.shp)|*.shp|All Files (*.*)|*.*",
                Multiselect = false,
                InitialDirectory = Directory.Exists(initialDir) ? initialDir : string.Empty
            };


            if (dialog.ShowDialog() != true) return;

            string shpPath = dialog.FileName;

            if (SettingsRepo != null)
            {
                var settings = await SettingsRepo.GetSettingsFresh();
                settings.ShpPaths[shpPath] = new ShpPathEntry
                {
                    LastOpened = DateTime.Now,
                    LayerType = layerType ?? string.Empty
                };
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
                else if (string.Equals(layerType, "Subbasins", StringComparison.OrdinalIgnoreCase))
                    SelectedSubbasinsPath = entry;
                else if (string.Equals(layerType, "Boundary", StringComparison.OrdinalIgnoreCase))
                    SelectedBoundaryPath = entry;
            });
        }


        // ── EventBus ─────────────────────────────────────────────────────────
        protected TabViewModelBase()
        {
            EventBus.GeometryPathsResolved += OnGeometryPathsResolved;
        }

        private void OnGeometryPathsResolved(string pathSubBasins, string pathXS, string pathBNDY)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrEmpty(pathSubBasins))
                    SelectedSubbasinsPath = new GeometryPathEntry
                    {
                        ShpPath = pathSubBasins,
                        Name = Path.GetFileName(pathSubBasins),
                        Directory = Path.GetDirectoryName(pathSubBasins) ?? string.Empty,
                        LayerType = "Subbasins"
                    };

                if (!string.IsNullOrEmpty(pathXS))
                    SelectedSectionsPath = new GeometryPathEntry
                    {
                        ShpPath = pathXS,
                        Name = Path.GetFileName(pathXS),
                        Directory = Path.GetDirectoryName(pathXS) ?? string.Empty,
                        LayerType = "Sections"
                    };

                if (!string.IsNullOrEmpty(pathBNDY))
                    SelectedBoundaryPath = new GeometryPathEntry
                    {
                        ShpPath = pathBNDY,
                        Name = Path.GetFileName(pathBNDY),
                        Directory = Path.GetDirectoryName(pathBNDY) ?? string.Empty,
                        LayerType = "Boundary"
                    };
            });
        }


        // ── Load ─────────────────────────────────────────────────────────────
        protected async Task LoadRecentProjectsAsync()
        {
            if (SettingsRepo == null) return;

            var settings = await SettingsRepo.GetSettingsFresh();

            var recent = settings.RecentProjects
                .GroupBy(kvp => kvp.Value.ProjDir, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(kvp => kvp.Value.LastOpened).First())
                .Take(10)
                .Select(kvp => new RecentProjectEntry
                {
                    Name = Path.GetFileNameWithoutExtension(kvp.Key),
                    FilePath = kvp.Value.ProjDir,
                    LastOpened = kvp.Value.LastOpened
                });

            var geometryPaths = settings.GeometryPaths
                .OrderByDescending(kvp => kvp.Value.LastOpened)
                .Take(20)
                .Select(kvp => new GeometryPathEntry
                {
                    ShpPath = kvp.Key,
                    Name = Path.GetFileName(kvp.Key),
                    Directory = Path.GetDirectoryName(kvp.Key) ?? string.Empty,
                    LayerType = kvp.Value.LayerType,
                    LastOpened = kvp.Value.LastOpened
                });

            Application.Current.Dispatcher.Invoke(() =>
            {
                RecentProjects = new ObservableCollection<RecentProjectEntry>(recent);
                GeometryPaths = new ObservableCollection<GeometryPathEntry>(geometryPaths);

                SelectedSectionsPath = GeometryPaths
                    .FirstOrDefault(p => string.Equals(p.LayerType, "Sections",
                        StringComparison.OrdinalIgnoreCase));

                SelectedSubbasinsPath = GeometryPaths
                    .FirstOrDefault(p => string.Equals(p.LayerType, "Subbasins",
                        StringComparison.OrdinalIgnoreCase));
            });
        }
    }
}