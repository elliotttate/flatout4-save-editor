using System.Collections.ObjectModel;
using FlatOut4SaveEditor.Models;
using FlatOut4SaveEditor.Services;
using Microsoft.UI.Text;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FlatOut4SaveEditor.Views
{
    public partial class MainPage : Page
    {
        private readonly FlatOut4SaveSchema schema = FlatOut4SaveSchema.Create();
        private readonly List<SaveFieldViewModel> allFields = [];
        private FlatOut4SaveDocument? document;

        public MainPage()
        {
            InitializeComponent();
            VisibleFields = [];
            DataContext = this;

            SectionBox.ItemsSource = new[]
            {
                "All",
                "Header",
                "Options",
                "Input",
                "Stats",
                "Trophies",
                "Career",
                "Garage",
                "Challenge",
                "Records",
                "Favorites",
                "Extra",
                "Padding",
                "Footer"
            };
            SectionBox.SelectedIndex = 0;
            UpdateChrome();
        }

        public ObservableCollection<SaveFieldViewModel> VisibleFields { get; }

        private async void OnOpenClicked(object sender, RoutedEventArgs e)
        {
            string? path = await ShowOpenFileDialogAsync();
            if (!string.IsNullOrWhiteSpace(path))
            {
                LoadSave(path);
            }
        }

        private async void OnDetectClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                IReadOnlyList<FlatOut4SaveCandidate> candidates = FlatOut4SaveFile.FindSaveCandidates(schema);
                if (candidates.Count == 0)
                {
                    string checkedLocations = string.Join(Environment.NewLine, FlatOut4SaveFile.GetCheckedSaveLocations());
                    await ShowMessage(
                        "No FlatOut 4 save found",
                        $"Checked exact Steam Cloud and offline save locations first, then fallback save-like files in those same folders.{Environment.NewLine}{Environment.NewLine}{checkedLocations}{Environment.NewLine}{Environment.NewLine}Use Open Save to browse manually if your file is somewhere else.");
                    return;
                }

                FlatOut4SaveCandidate? selected = candidates.Count == 1
                    ? candidates[0]
                    : await ShowSavePicker(candidates);

                if (selected is null)
                {
                    StatusText.Text = $"Found {candidates.Count:N0} supported save files.";
                    return;
                }

                LoadSave(selected.Path);
                StatusText.Text = candidates.Count == 1
                    ? $"Loaded detected save from {selected.Source}."
                    : $"Loaded selected save from {selected.Source}. Found {candidates.Count:N0} supported save files.";
            }
            catch (Exception ex)
            {
                await ShowMessage("Could not detect Steam save", ex.Message);
            }
        }

        private async void OnSaveClicked(object sender, RoutedEventArgs e)
        {
            if (document?.Path is null)
            {
                return;
            }

            if (!CommitAllDrafts())
            {
                await ShowMessage("Some edits are invalid", "Fix the rows with errors before saving.");
                return;
            }

            try
            {
                FlatOut4SaveFile.Save(document, document.Path, BackupCheckBox.IsChecked == true);
                StatusText.Text = $"Saved {document.Path}";
                ResetDrafts();
                UpdateChrome();
            }
            catch (Exception ex)
            {
                await ShowMessage("Save failed", ex.Message);
            }
        }

        private async void OnUnlockAllClicked(object sender, RoutedEventArgs e)
        {
            if (document is null)
            {
                return;
            }

            if (!CommitAllDrafts())
            {
                await ShowMessage("Some edits are invalid", "Fix the rows with errors before using Unlock All.");
                return;
            }

            try
            {
                FlatOut4UnlockAllResult result = FlatOut4SaveUnlocker.ApplyDebugUnlockAll(document, schema);
                ResetDrafts();
                ApplyFilter();
                UpdateChrome();

                StatusText.Text = result.Changed
                    ? $"Applied Unlock All: {result.CareerEventsCompleted:N0} career events completed, {result.GarageEntriesUnlocked:N0} garage entries unlocked, {result.ChallengeValuesMaxed:N0} challenge values maxed. Save to write the file."
                    : "Unlock All was already applied.";
            }
            catch (Exception ex)
            {
                await ShowMessage("Unlock All failed", ex.Message);
            }
        }

        private void OnFilterChanged(object sender, object e)
        {
            ApplyFilter();
        }

        private async void LoadSave(string path)
        {
            try
            {
                document = FlatOut4SaveFile.Load(path, schema);
                allFields.Clear();
                allFields.AddRange(schema.Fields.Select(field => new SaveFieldViewModel(field, document.Bytes)));
                ApplyFilter();
                ResetDrafts();
                UpdateChrome();

                FilePathText.Text = path;
                SummaryText.Text = $"Version {BitConverter.ToUInt32(document.Bytes, 4)} | {document.Bytes.Length:N0} bytes | {allFields.Count:N0} editable/derived values";
                WarningBar.IsOpen = !string.IsNullOrWhiteSpace(document.Warning);
                WarningBar.Message = document.Warning;
                StatusText.Text = document.Migrated
                    ? "Loaded and migrated save in memory. Save to write the migrated version."
                    : "Loaded save.";
            }
            catch (Exception ex)
            {
                await ShowMessage("Open failed", ex.Message);
            }
        }

        private void ApplyFilter()
        {
            string query = SearchBox.Text?.Trim() ?? string.Empty;
            string section = SectionBox.SelectedItem as string ?? "All";

            IEnumerable<SaveFieldViewModel> filtered = allFields;
            if (!string.Equals(section, "All", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(field => string.Equals(field.Section, section, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = filtered.Where(field =>
                    field.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    field.RawName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    field.Section.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    field.DisplayValue.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    field.LabelValue.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            VisibleFields.Clear();
            foreach (SaveFieldViewModel field in filtered)
            {
                VisibleFields.Add(field);
            }

            FieldCountText.Text = $"{VisibleFields.Count:N0} shown / {allFields.Count:N0} total";
        }

        private bool CommitAllDrafts()
        {
            bool ok = true;
            foreach (SaveFieldViewModel field in allFields)
            {
                ok &= field.CommitDraft();
            }

            if (ok)
            {
                ResetDrafts();
            }

            ApplyFilter();
            return ok;
        }

        private void ResetDrafts()
        {
            foreach (SaveFieldViewModel field in allFields)
            {
                field.ResetDraft();
            }
        }

        private void UpdateChrome()
        {
            bool hasDocument = document is not null;
            SaveButton.IsEnabled = hasDocument;
            UnlockAllButton.IsEnabled = hasDocument;
            if (!hasDocument)
            {
                FieldCountText.Text = $"0 shown / {schema.Fields.Count:N0} total";
            }
        }

        private async Task ShowMessage(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };

            _ = await dialog.ShowAsync();
        }

        private async Task<FlatOut4SaveCandidate?> ShowSavePicker(IReadOnlyList<FlatOut4SaveCandidate> candidates)
        {
            var list = new ListView
            {
                SelectionMode = ListViewSelectionMode.Single,
                MaxHeight = 420
            };

            foreach (FlatOut4SaveCandidate candidate in candidates)
            {
                list.Items.Add(CreateCandidateItem(candidate));
            }

            list.SelectedIndex = 0;

            var content = new StackPanel
            {
                Spacing = 12
            };
            content.Children.Add(new TextBlock
            {
                Text = "Multiple supported FlatOut 4 saves were found. Choose the one to open.",
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(list);

            var dialog = new ContentDialog
            {
                Title = "Choose save file",
                Content = content,
                PrimaryButtonText = "Open selected",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            return list.SelectedItem is ListViewItem { Tag: FlatOut4SaveCandidate selectedCandidate }
                ? selectedCandidate
                : candidates[0];
        }

        private static ListViewItem CreateCandidateItem(FlatOut4SaveCandidate candidate)
        {
            var content = new StackPanel
            {
                Spacing = 2
            };
            content.Children.Add(new TextBlock
            {
                Text = candidate.Title,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(new TextBlock
            {
                Text = candidate.Details,
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(new TextBlock
            {
                Text = candidate.Path,
                TextWrapping = TextWrapping.Wrap
            });

            return new ListViewItem
            {
                Content = content,
                Tag = candidate
            };
        }

        private async Task<string?> ShowOpenFileDialogAsync()
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                ViewMode = PickerViewMode.List
            };

            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));

            StorageFile? file = await picker.PickSingleFileAsync();
            return file?.Path;
        }
    }
}
