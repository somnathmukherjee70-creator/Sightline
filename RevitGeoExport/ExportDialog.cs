using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using RevitGeoExport.Core;

namespace RevitGeoExport
{
    public sealed class ExportSettings
    {
        /// <summary>When set, the model is placed by its own shared coordinates in this system and the latitude/longitude anchor is ignored.</summary>
        public ProjectedCrs Crs { get; set; }

        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public AnchorPoint Anchor { get; set; }
        public double RotationDegrees { get; set; }
        public AltitudeMode AltitudeMode { get; set; }
        public double SiteElevationMeters { get; set; }
        public double OffsetMeters { get; set; }
        public bool WriteKmz { get; set; } = true;
        public bool WriteGlb { get; set; } = true;
        public bool WriteGeoJson { get; set; } = true;
        public bool WriteWebMap { get; set; } = true;
        public bool SaveLocationToProject { get; set; }

        /// <summary>Only walls, roofs, floors, windows and other categories seen from outside.</summary>
        public bool ExteriorOnly { get; set; } = true;

        public EarthWebDetail EarthWebDetail { get; set; } = EarthWebDetail.Balanced;

        /// <summary>Output path without extension; each format adds its own.</summary>
        public string OutputBasePath { get; set; }
    }

    /// <summary>Collects where to place the model and what to export.</summary>
    public sealed class ExportDialog : Form
    {
        private readonly TextBox _crs = new TextBox { Width = 260 };
        private bool _hasSurveyCoordinates;
        private readonly Label _crsStatus = Hint("");
        private readonly TextBox _coordinates = new TextBox { Width = 260 };
        private readonly ComboBox _anchor = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        private readonly NumericUpDown _rotation = Number(-360, 360, 2);
        private readonly ComboBox _detail = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        private readonly ComboBox _content = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        private readonly ComboBox _altitude = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        private readonly NumericUpDown _elevation = Number(-500, 9000, 2);
        private readonly NumericUpDown _offset = Number(-1000, 1000, 2);
        private readonly CheckBox _glb = new CheckBox { Text = "Google Earth Web 3D model (.glb, real architecture)", AutoSize = true, Checked = true };
        private readonly CheckBox _kmz = new CheckBox { Text = "Google Earth Web and Pro, placed automatically (.kmz)", AutoSize = true, Checked = true };
        private readonly CheckBox _geoJson = new CheckBox { Text = "GeoJSON footprints (.geojson)", AutoSize = true, Checked = true };
        private readonly CheckBox _webMap = new CheckBox { Text = "3D map that places itself (.html)", AutoSize = true, Checked = true };
        private readonly CheckBox _save = new CheckBox { Text = "Save these coordinates to the Revit project location", AutoSize = true };
        private readonly TextBox _output = new TextBox { Width = 260 };

        /// <param name="saved">This project's values from the last export, applied over the defaults.</param>
        public ExportDialog(RevitLocation location, string defaultOutput, IDictionary<string, string> saved = null)
        {
            // The build number shows which version is installed (set by the GitHub build).
            var version = typeof(App).Assembly.GetName().Version;
            Text = $"Place model on map — build {version.Build}";
            using (var icon = App.OpenResource("logo.ico"))
                Icon = new System.Drawing.Icon(icon);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(10);

            _crs.Text = location.DetectedCrs?.Epsg.ToString(CultureInfo.InvariantCulture) ?? "";
            _crs.TextChanged += (s, e) => UpdatePlacementMode(location.DetectionNote);
            _hasSurveyCoordinates = location.HasSurveyCoordinates;
            _coordinates.Text = string.Format(CultureInfo.InvariantCulture, "{0:0.0000000}, {1:0.0000000}", location.LatitudeDegrees, location.LongitudeDegrees);
            _anchor.Items.AddRange(new object[] { "Survey Point", "Project Base Point", "Internal Origin" });
            _anchor.SelectedIndex = 0;
            _content.Items.AddRange(new object[] { "Exterior shell (best for Google Earth)", "Everything visible" });
            _content.SelectedIndex = 0;
            _detail.Items.AddRange(new object[] { "Light (fastest)", "Balanced", "Full detail (slower)" });
            _detail.SelectedIndex = 1;
            _altitude.Items.AddRange(new object[] { "Sit on the terrain", "Above sea level" });
            _altitude.SelectedIndex = 0;
            // Shared coordinates in a survey system usually carry real elevations already.
            _elevation.Value = location.DetectedCrs != null ? 0 : (decimal)Math.Max(-500, Math.Min(9000, location.ElevationMeters));
            _output.Text = defaultOutput;

            var browse = new Button { Text = "Browse…", AutoSize = true };
            browse.Click += (s, e) => Browse();

            var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
            AddRow(grid, "Coordinate system (EPSG, optional)", _crs);
            AddRow(grid, "", _crsStatus);
            AddRow(grid, "Latitude, longitude", _coordinates);
            AddRow(grid, "", Hint($"Revit location: {location.PlaceName}. Paste from Google Maps (right-click a spot › copy the coordinates)."));
            AddRow(grid, "Coordinates are of the", _anchor);
            AddRow(grid, "Rotation adjustment (° clockwise)", _rotation);
            AddRow(grid, "Height", _altitude);
            AddRow(grid, "Height of Z = 0 above sea (m)", _elevation);
            AddRow(grid, "Vertical offset (m)", _offset);
            AddRow(grid, "Include", _content);
            AddRow(grid, "Google Earth Web detail", _detail);
            AddRow(grid, "Export", Stack(_glb, _kmz, _geoJson, _webMap));
            AddRow(grid, "Save to (no extension)", Stack(_output, browse));
            AddRow(grid, "", _save);

            var ok = new Button { Text = "Export", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            ok.Click += OnExport;
            AcceptButton = ok;
            CancelButton = cancel;
            AddRow(grid, "", Stack(ok, cancel));

            Controls.Add(grid);
            if (saved != null)
                Restore(saved);
            UpdatePlacementMode(location.DetectionNote);
        }

        /// <summary>The values to remember for this project.</summary>
        public Dictionary<string, string> ToSaved() => new Dictionary<string, string>
        {
            ["crs"] = _crs.Text.Trim(),
            ["coordinates"] = _coordinates.Text.Trim(),
            ["anchor"] = _anchor.SelectedIndex.ToString(CultureInfo.InvariantCulture),
            ["rotation"] = _rotation.Value.ToString(CultureInfo.InvariantCulture),
            ["altitude"] = _altitude.SelectedIndex.ToString(CultureInfo.InvariantCulture),
            ["elevation"] = _elevation.Value.ToString(CultureInfo.InvariantCulture),
            ["offset"] = _offset.Value.ToString(CultureInfo.InvariantCulture),
            ["content"] = _content.SelectedIndex.ToString(CultureInfo.InvariantCulture),
            ["detail"] = _detail.SelectedIndex.ToString(CultureInfo.InvariantCulture),
            ["glb"] = _glb.Checked.ToString(),
            ["kmz"] = _kmz.Checked.ToString(),
            ["geojson"] = _geoJson.Checked.ToString(),
            ["webmap"] = _webMap.Checked.ToString(),
            ["output"] = _output.Text.Trim(),
        };

        private void Restore(IDictionary<string, string> saved)
        {
            string Get(string key) => saved.TryGetValue(key, out var value) ? value : null;

            // A coordinate system typed last time wins; an empty one leaves the detected system in place.
            if (!string.IsNullOrEmpty(Get("crs")))
                _crs.Text = Get("crs");
            if (!string.IsNullOrEmpty(Get("coordinates")))
                _coordinates.Text = Get("coordinates");
            SetIndex(_anchor, Get("anchor"));
            SetIndex(_altitude, Get("altitude"));
            SetIndex(_content, Get("content"));
            SetIndex(_detail, Get("detail"));
            SetNumber(_rotation, Get("rotation"));
            SetNumber(_elevation, Get("elevation"));
            SetNumber(_offset, Get("offset"));
            SetCheck(_glb, Get("glb"));
            SetCheck(_kmz, Get("kmz"));
            SetCheck(_geoJson, Get("geojson"));
            SetCheck(_webMap, Get("webmap"));
            if (!string.IsNullOrEmpty(Get("output")))
                _output.Text = Get("output");
        }

        private static void SetIndex(ComboBox box, string value)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) && index >= 0 && index < box.Items.Count)
                box.SelectedIndex = index;
        }

        private static void SetNumber(NumericUpDown box, string value)
        {
            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number))
                box.Value = Math.Max(box.Minimum, Math.Min(box.Maximum, number));
        }

        private static void SetCheck(CheckBox box, string value)
        {
            if (bool.TryParse(value, out bool isChecked))
                box.Checked = isChecked;
        }

        private bool UsesCoordinateSystem => !string.IsNullOrWhiteSpace(_crs.Text);

        /// <summary>With a coordinate system the model places itself, so the manual placement fields are disabled.</summary>
        private void UpdatePlacementMode(string detectionNote)
        {
            bool parsed = ProjectedCrs.TryParse(_crs.Text, out var crs);
            bool misleading = UsesCoordinateSystem && parsed && !_hasSurveyCoordinates;
            _crsStatus.Text = !UsesCoordinateSystem ? detectionNote
                : misleading ? "This project's shared coordinates are near 0,0, so a grid would put the model at the grid's origin, far from the site. Leave this empty to place it by latitude/longitude."
                : parsed ? $"Placed automatically by the model's shared coordinates in {crs}."
                : "Not a supported coordinate system (UTM on WGS84/NAD83/ETRS89/GDA, Dubai DLTM 3997, Pennsylvania State Plane 2271/2272/32128/32129, or 3857).";
            _crsStatus.ForeColor = UsesCoordinateSystem && (!parsed || misleading) ? System.Drawing.Color.Firebrick : System.Drawing.SystemColors.GrayText;

            _coordinates.Enabled = _anchor.Enabled = _rotation.Enabled = _save.Enabled = !UsesCoordinateSystem;
            if (UsesCoordinateSystem)
                _save.Checked = false;
        }

        public ExportSettings Settings { get; private set; }

        private void OnExport(object sender, EventArgs e)
        {
            ProjectedCrs crs = null;
            if (UsesCoordinateSystem && !ProjectedCrs.TryParse(_crs.Text, out crs))
            {
                MessageBox.Show(this, "The coordinate system isn't supported. Clear it to place the model by latitude/longitude instead.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            double lat = 0, lon = 0;
            if (crs == null && !TryParseCoordinates(_coordinates.Text, out lat, out lon))
            {
                MessageBox.Show(this, "Enter the coordinates as \"latitude, longitude\" in decimal degrees, e.g. 22.544808, 88.342558.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            if (!(_glb.Checked || _kmz.Checked || _geoJson.Checked || _webMap.Checked) || string.IsNullOrWhiteSpace(_output.Text))
            {
                MessageBox.Show(this, "Pick at least one format and an output location.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            Settings = new ExportSettings
            {
                Crs = crs,
                Latitude = lat,
                Longitude = lon,
                Anchor = (AnchorPoint)_anchor.SelectedIndex,
                RotationDegrees = (double)_rotation.Value,
                AltitudeMode = (AltitudeMode)_altitude.SelectedIndex,
                SiteElevationMeters = (double)_elevation.Value,
                OffsetMeters = (double)_offset.Value,
                WriteKmz = _kmz.Checked,
                WriteGlb = _glb.Checked,
                WriteGeoJson = _geoJson.Checked,
                WriteWebMap = _webMap.Checked,
                SaveLocationToProject = _save.Checked,
                ExteriorOnly = _content.SelectedIndex == 0,
                EarthWebDetail = (EarthWebDetail)_detail.SelectedIndex,
                OutputBasePath = StripExtension(_output.Text.Trim()),
            };
        }

        internal static bool TryParseCoordinates(string text, out double latitude, out double longitude)
        {
            latitude = longitude = 0;
            var parts = (text ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 2
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out latitude)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out longitude)
                && Math.Abs(latitude) <= 90 && Math.Abs(longitude) <= 180;
        }

        private void Browse()
        {
            using (var dialog = new SaveFileDialog { Filter = "All exports|*.*", FileName = Path.GetFileName(_output.Text) })
            {
                var folder = Path.GetDirectoryName(_output.Text);
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    dialog.InitialDirectory = folder;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    _output.Text = StripExtension(dialog.FileName);
            }
        }

        private static string StripExtension(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".kmz" || ext == ".kml" || ext == ".geojson" || ext == ".html" || ext == ".glb"
                ? Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path))
                : path;
        }

        private static NumericUpDown Number(int min, int max, int decimals) =>
            new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = decimals, Width = 100 };

        private static Label Hint(string text) =>
            new Label { Text = text, AutoSize = true, MaximumSize = new System.Drawing.Size(280, 0), ForeColor = System.Drawing.SystemColors.GrayText };

        private static FlowLayoutPanel Stack(params Control[] controls)
        {
            var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = controls[0] is CheckBox ? FlowDirection.TopDown : FlowDirection.LeftToRight };
            panel.Controls.AddRange(controls);
            return panel;
        }

        private static void AddRow(TableLayoutPanel grid, string label, Control control)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 12, 3) });
            grid.Controls.Add(control);
        }
    }
}
