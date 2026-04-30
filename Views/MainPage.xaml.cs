using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text;
using FlatOut4SaveEditor.Models;
using FlatOut4SaveEditor.Services;
using Microsoft.UI.Xaml.Media;
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

        private void OnOpenClicked(object sender, RoutedEventArgs e)
        {
            string? path = ShowOpenFileDialog();
            if (!string.IsNullOrWhiteSpace(path))
            {
                LoadSave(path);
            }
        }

        private async void OnDetectClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                IReadOnlyList<string> saves = FlatOut4SaveFile.FindSteamCloudSaves();
                if (saves.Count == 0)
                {
                    string checkedLocations = string.Join(Environment.NewLine, FlatOut4SaveFile.GetCheckedSaveLocations());
                    await ShowMessage(
                        "No FlatOut 4 save found",
                        $"Checked Steam Cloud app ids 3844750 and 402130, plus offline save folders.{Environment.NewLine}{Environment.NewLine}{checkedLocations}{Environment.NewLine}{Environment.NewLine}Use Open Save to browse manually if your file is somewhere else.");
                    return;
                }

                LoadSave(saves[0]);
                StatusText.Text = saves.Count == 1
                    ? "Loaded detected Steam Cloud save."
                    : $"Loaded newest detected Steam Cloud save. Found {saves.Count} matching files.";
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

        private string GetBestInitialDirectory()
        {
            IReadOnlyList<string> saves = FlatOut4SaveFile.FindSteamCloudSaves();
            if (saves.Count > 0)
            {
                return Path.GetDirectoryName(saves[0]) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }

            string documentsSave = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "FlatOut 4");
            return Directory.Exists(documentsSave)
                ? documentsSave
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        private string? ShowOpenFileDialog()
        {
            var fileName = new StringBuilder(4096);
            var openFileName = new OpenFileName
            {
                lStructSize = Marshal.SizeOf<OpenFileName>(),
                hwndOwner = WindowNative.GetWindowHandle(App.MainWindow),
                lpstrFilter = "FlatOut 4 save (Save)\0Save\0All files (*.*)\0*.*\0\0",
                lpstrFile = fileName,
                nMaxFile = fileName.Capacity,
                lpstrInitialDir = GetBestInitialDirectory(),
                lpstrTitle = "Open FlatOut 4 Save",
                Flags = OfnExplorer | OfnFileMustExist | OfnPathMustExist | OfnHideReadOnly | OfnNoChangeDir
            };

            return GetOpenFileName(ref openFileName) ? openFileName.lpstrFile.ToString() : null;
        }

        private const int OfnHideReadOnly = 0x00000004;
        private const int OfnNoChangeDir = 0x00000008;
        private const int OfnFileMustExist = 0x00001000;
        private const int OfnPathMustExist = 0x00000800;
        private const int OfnExplorer = 0x00080000;

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetOpenFileName(ref OpenFileName openFileName);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string? lpstrFilter;
            public string? lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public StringBuilder lpstrFile;
            public int nMaxFile;
            public StringBuilder? lpstrFileTitle;
            public int nMaxFileTitle;
            public string? lpstrInitialDir;
            public string? lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string? lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string? lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }
    }
}
