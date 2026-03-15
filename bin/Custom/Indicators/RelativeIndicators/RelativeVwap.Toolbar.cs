#region Using declarations
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using NinjaTrader.Gui.Chart;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.RelativeIndicators
{
    public partial class RelativeVwap
    {
        // ====================================================
        // v3.0.2: CHART TOOLBAR INTEGRATION
        // Adds quick-access controls to the chart's main toolbar
        // Pattern based on PATSToolBar.cs (cw.MainMenu.Add)
        // ====================================================

        private NinjaTrader.Gui.Chart.Chart _chartWindow;
        private System.Collections.Generic.List<object> _toolBarItems;
        private bool _isToolBarAdded;
        private ComboBox _personalityCombo;
        private CheckBox _chkLabels;
        private CheckBox _chkSignalText;
        private CheckBox _chkAsia;
        private CheckBox _chkEurope;
        private CheckBox _chkUSA;
        private CheckBox _chkExtend;
        private Label _toolBarVersionLabel;

        // v3.0.8: Config toggle buttons for touch study visualization
        private CheckBox _chkCfgA, _chkCfgB, _chkCfgC, _chkCfgD;
        internal bool _showCfgA = true, _showCfgB = true, _showCfgC = true, _showCfgD = true;

        // v3.1.0: Config toggle buttons for AUTO-TRADING execution
        private CheckBox _chkTradeCfgA, _chkTradeCfgB, _chkTradeCfgC, _chkTradeCfgD;
        internal bool _tradeCfgA = false, _tradeCfgB = false, _tradeCfgC = false, _tradeCfgD = false;
        internal bool _autoTradeOpen = false;
        internal string _autoTradeConfig = "";
        internal string _autoTradeOcoId = "";

        // v3.1.2: Persistence file path for toolbar toggle states (survives F5)
        private string _toolbarStatePath;

        private string GetToolbarStatePath()
        {
            if (_toolbarStatePath != null) return _toolbarStatePath;

            string dir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "RelativeVwap");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string safeInstrument = Instrument != null
                ? Instrument.FullName.Replace(" ", "_").Replace("/", "_")
                : "Default";

            _toolbarStatePath = Path.Combine(dir, safeInstrument + "_toolbar.txt");
            return _toolbarStatePath;
        }

        private void SaveToolbarStates()
        {
            try
            {
                string path = GetToolbarStatePath();
                string[] lines = new string[]
                {
                    "A=" + (_showCfgA ? "1" : "0"),
                    "B=" + (_showCfgB ? "1" : "0"),
                    "C=" + (_showCfgC ? "1" : "0"),
                    "D=" + (_showCfgD ? "1" : "0"),
                    "TA=" + (_tradeCfgA ? "1" : "0"),
                    "TB=" + (_tradeCfgB ? "1" : "0"),
                    "TC=" + (_tradeCfgC ? "1" : "0"),
                    "TD=" + (_tradeCfgD ? "1" : "0")
                };
                File.WriteAllLines(path, lines);
            }
            catch (Exception ex)
            {
                Print("[TOOLBAR] Error saving states: " + ex.Message);
            }
        }

        private void LoadToolbarStates()
        {
            try
            {
                string path = GetToolbarStatePath();
                if (!File.Exists(path)) return;

                string[] lines = File.ReadAllLines(path);
                foreach (string line in lines)
                {
                    if (string.IsNullOrEmpty(line) || !line.Contains("=")) continue;
                    string[] parts = line.Split('=');
                    if (parts.Length != 2) continue;

                    bool val = parts[1].Trim() == "1";
                    // v3.2.0: Auto/Estudio templates override A/B/C/D — don't load saved state for those
                    bool isAutoTemplate = (StudyTemplate == TouchStudyTemplate.Auto || StudyTemplate == TouchStudyTemplate.Estudio);
                    switch (parts[0].Trim())
                    {
                        case "A":  if (!isAutoTemplate) _showCfgA  = val; break;
                        case "B":  if (!isAutoTemplate) _showCfgB  = val; break;
                        case "C":  if (!isAutoTemplate) _showCfgC  = val; break;
                        case "D":  if (!isAutoTemplate) _showCfgD  = val; break;
                        case "TA": _tradeCfgA = val; break;
                        case "TB": _tradeCfgB = val; break;
                        case "TC": _tradeCfgC = val; break;
                        case "TD": _tradeCfgD = val; break;
                    }
                }

                if (ShowDebugLogs) Print(string.Format("[TOOLBAR] States loaded: A={0} B={1} C={2} D={3} TA={4} TB={5} TC={6} TD={7}",
                    _showCfgA, _showCfgB, _showCfgC, _showCfgD, _tradeCfgA, _tradeCfgB, _tradeCfgC, _tradeCfgD));
            }
            catch (Exception ex)
            {
                Print("[TOOLBAR] Error loading states: " + ex.Message);
            }
        }

        private void AddToolBar()
        {
            if (_isToolBarAdded) return;

            _chartWindow = Window.GetWindow(ChartControl.Parent) as NinjaTrader.Gui.Chart.Chart;
            if (_chartWindow == null) return;

            // v3.1.2: Restore toggle states from disk before creating checkboxes
            LoadToolbarStates();

            _toolBarItems = new System.Collections.Generic.List<object>();

            Brush textBrush = Application.Current.FindResource("FontActionBrush") as Brush ?? Brushes.White;
            Brush bgBrush = new SolidColorBrush(Color.FromArgb(180, 30, 30, 80));
            bgBrush.Freeze();

            // --- Separator ---
            _toolBarItems.Add(new Separator() { Margin = new Thickness(8, 0, 4, 0) });

            // --- Version Label ---
            _toolBarVersionLabel = new Label()
            {
                Content = "RV",
                Foreground = Brushes.Cyan,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 2, 0),
                ToolTip = "RelativeVwap v" + VERSION
            };
            _toolBarItems.Add(_toolBarVersionLabel);

            // --- Personality ComboBox (theme-aware) ---
            Style comboStyle = Application.Current.TryFindResource("ComboBoxStyle") as Style;
            _personalityCombo = new ComboBox()
            {
                Width = 85,
                Height = 22,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 4, 0),
                ToolTip = "Personalidad del indicador"
            };
            // Apply NT8 theme style if available, otherwise fallback to manual dark styling
            if (comboStyle != null)
            {
                _personalityCombo.Style = comboStyle;
            }
            else
            {
                Brush comboBg = Application.Current.TryFindResource("BackgroundMainBrush") as Brush;
                if (comboBg != null)
                    _personalityCombo.Background = comboBg;
                else
                    _personalityCombo.Background = new SolidColorBrush(Color.FromRgb(43, 43, 57));

                _personalityCombo.Foreground = textBrush;
                _personalityCombo.BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 100));
            }
            _personalityCombo.Items.Add("Intraday");
            _personalityCombo.Items.Add("Weekly");
            _personalityCombo.Items.Add("Monthly");
            _personalityCombo.Items.Add("Quarterly");
            _personalityCombo.Items.Add("Yearly");
            _personalityCombo.SelectedItem = Personality.ToString();
            _personalityCombo.SelectionChanged += OnPersonalityChanged;
            _toolBarItems.Add(_personalityCombo);

            // --- Labels Checkbox ---
            _chkLabels = CreateToolBarCheckBox("Labels", ShowLabels, "Mostrar/ocultar etiquetas de niveles");
            _chkLabels.Checked += (s, e) => { ShowLabels = true; RefreshChart(); };
            _chkLabels.Unchecked += (s, e) => { ShowLabels = false; RefreshChart(); };
            _toolBarItems.Add(_chkLabels);

            // --- Signal Text Checkbox ---
            _chkSignalText = CreateToolBarCheckBox("Signals", ShowSignalText, "Mostrar/ocultar textos de señales");
            _chkSignalText.Checked += (s, e) => { ShowSignalText = true; RefreshChart(); };
            _chkSignalText.Unchecked += (s, e) => { ShowSignalText = false; RefreshChart(); };
            _toolBarItems.Add(_chkSignalText);

            // --- Session Checkboxes ---
            _chkAsia = CreateToolBarCheckBox("Asia", ShowAsia, "Mostrar/ocultar sesión Asia");
            _chkAsia.Checked += (s, e) => { ShowAsia = true; RefreshChart(); };
            _chkAsia.Unchecked += (s, e) => { ShowAsia = false; RefreshChart(); };
            _toolBarItems.Add(_chkAsia);

            _chkEurope = CreateToolBarCheckBox("Europe", ShowEurope, "Mostrar/ocultar sesión Europa");
            _chkEurope.Checked += (s, e) => { ShowEurope = true; RefreshChart(); };
            _chkEurope.Unchecked += (s, e) => { ShowEurope = false; RefreshChart(); };
            _toolBarItems.Add(_chkEurope);

            _chkUSA = CreateToolBarCheckBox("USA", ShowUS, "Mostrar/ocultar sesión USA");
            _chkUSA.Checked += (s, e) => { ShowUS = true; RefreshChart(); };
            _chkUSA.Unchecked += (s, e) => { ShowUS = false; RefreshChart(); };
            _toolBarItems.Add(_chkUSA);

            // --- ExtendLines Checkbox ---
            _chkExtend = CreateToolBarCheckBox("Extend", ExtendLinesUntilTouch, "Extender líneas hasta ser tocadas");
            _chkExtend.Checked += (s, e) => { ExtendLinesUntilTouch = true; RefreshChart(); };
            _chkExtend.Unchecked += (s, e) => { ExtendLinesUntilTouch = false; RefreshChart(); };
            _toolBarItems.Add(_chkExtend);

            // --- Config Toggle Checkboxes (v3.0.8) ---
            _toolBarItems.Add(new Separator() { Margin = new Thickness(6, 0, 2, 0) });

            _chkCfgA = CreateConfigCheckBox("A", Brushes.LimeGreen, _showCfgA, "Config A: LONG breakout (Supply touch + Demand fuerte)");
            _chkCfgA.Checked   += (s, e) => { _showCfgA = true; SaveToolbarStates(); RefreshChart(); };
            _chkCfgA.Unchecked += (s, e) => { _showCfgA = false; SaveToolbarStates(); RefreshChart(); };
            _toolBarItems.Add(_chkCfgA);

            _chkCfgB = CreateConfigCheckBox("B", new SolidColorBrush(Color.FromRgb(0xFF, 0x57, 0x22)), _showCfgB, "Config B: SHORT breakout (Demand touch + Supply fuerte)");
            _chkCfgB.Checked   += (s, e) => { _showCfgB = true; SaveToolbarStates(); RefreshChart(); };
            _chkCfgB.Unchecked += (s, e) => { _showCfgB = false; SaveToolbarStates(); RefreshChart(); };
            _toolBarItems.Add(_chkCfgB);

            _chkCfgC = CreateConfigCheckBox("C", new SolidColorBrush(Color.FromRgb(0xBA, 0x55, 0xD3)), _showCfgC, "Config C: SHORT reversal (Supply touch + Supply fuerte)");
            _chkCfgC.Checked   += (s, e) => { _showCfgC = true; SaveToolbarStates(); RefreshChart(); };
            _chkCfgC.Unchecked += (s, e) => { _showCfgC = false; SaveToolbarStates(); RefreshChart(); };
            _toolBarItems.Add(_chkCfgC);

            _chkCfgD = CreateConfigCheckBox("D", Brushes.Gold, _showCfgD, "Config D: LONG reversal (Demand touch + Demand fuerte)");
            _chkCfgD.Checked   += (s, e) => { _showCfgD = true; SaveToolbarStates(); RefreshChart(); };
            _chkCfgD.Unchecked += (s, e) => { _showCfgD = false; SaveToolbarStates(); RefreshChart(); };
            _toolBarItems.Add(_chkCfgD);

            // --- v3.1.0: AUTO-TRADE Config Checkboxes ---
            _toolBarItems.Add(new Separator() { Margin = new Thickness(6, 0, 2, 0) });
            var tradeLbl = new Label() { Content = "TRADE:", Foreground = Brushes.Red, FontWeight = FontWeights.Bold, FontSize = 10, Margin = new Thickness(0, 0, 2, 0), VerticalContentAlignment = VerticalAlignment.Center };
            _toolBarItems.Add(tradeLbl);

            _chkTradeCfgA = CreateConfigCheckBox("TA", Brushes.LimeGreen, _tradeCfgA, "AUTO-TRADE Config A: LONG breakout");
            _chkTradeCfgA.Checked   += (s, e) => { _tradeCfgA = true; SaveToolbarStates(); if (ShowDebugLogs) Print("[AUTO-TRADE] Config A ACTIVADO"); };
            _chkTradeCfgA.Unchecked += (s, e) => { _tradeCfgA = false; SaveToolbarStates(); if (ShowDebugLogs) Print("[AUTO-TRADE] Config A DESACTIVADO"); };
            _toolBarItems.Add(_chkTradeCfgA);

            _chkTradeCfgB = CreateConfigCheckBox("TB", new SolidColorBrush(Color.FromRgb(0xFF, 0x57, 0x22)), _tradeCfgB, "AUTO-TRADE Config B: SHORT breakout");
            _chkTradeCfgB.Checked   += (s, e) => { _tradeCfgB = true; SaveToolbarStates(); if (ShowDebugLogs) Print("[AUTO-TRADE] Config B ACTIVADO"); };
            _chkTradeCfgB.Unchecked += (s, e) => { _tradeCfgB = false; SaveToolbarStates(); if (ShowDebugLogs) Print("[AUTO-TRADE] Config B DESACTIVADO"); };
            _toolBarItems.Add(_chkTradeCfgB);

            _chkTradeCfgC = CreateConfigCheckBox("TC", new SolidColorBrush(Color.FromRgb(0xBA, 0x55, 0xD3)), _tradeCfgC, "AUTO-TRADE Config C: SHORT reversal");
            _chkTradeCfgC.Checked   += (s, e) => { _tradeCfgC = true; SaveToolbarStates(); if (ShowDebugLogs) Print("[AUTO-TRADE] Config C ACTIVADO"); };
            _chkTradeCfgC.Unchecked += (s, e) => { _tradeCfgC = false; SaveToolbarStates(); if (ShowDebugLogs) Print("[AUTO-TRADE] Config C DESACTIVADO"); };
            _toolBarItems.Add(_chkTradeCfgC);

            _chkTradeCfgD = CreateConfigCheckBox("TD", Brushes.Gold, _tradeCfgD, "AUTO-TRADE Config D: LONG reversal");
            _chkTradeCfgD.Checked   += (s, e) => { _tradeCfgD = true; SaveToolbarStates(); if (ShowDebugLogs) Print("[AUTO-TRADE] Config D ACTIVADO"); };
            _chkTradeCfgD.Unchecked += (s, e) => { _tradeCfgD = false; SaveToolbarStates(); if (ShowDebugLogs) Print("[AUTO-TRADE] Config D DESACTIVADO"); };
            _toolBarItems.Add(_chkTradeCfgD);

            // --- Add all to MainMenu ---
            ShowHideToolBar(IsTabSelected());
            _chartWindow.MainTabControl.SelectionChanged += OnToolBarTabChanged;
        }

        private CheckBox CreateToolBarCheckBox(string label, bool isChecked, string tooltip)
        {
            Brush cbTextBrush = Application.Current.FindResource("FontActionBrush") as Brush ?? Brushes.White;
            Style cbStyle = Application.Current.TryFindResource("CheckBoxStyle") as Style;

            var cb = new CheckBox()
            {
                Content = label,
                IsChecked = isChecked,
                Foreground = cbTextBrush,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 2, 0),
                ToolTip = tooltip
            };
            if (cbStyle != null) cb.Style = cbStyle;
            return cb;
        }

        /// <summary>
        /// Creates a config toggle checkbox with colored label for the toolbar.
        /// </summary>
        private CheckBox CreateConfigCheckBox(string label, Brush foreground, bool isChecked, string tooltip)
        {
            Style cbStyle = Application.Current.TryFindResource("CheckBoxStyle") as Style;
            if (foreground.CanFreeze) foreground.Freeze();
            var cb = new CheckBox()
            {
                Content = label,
                IsChecked = isChecked,
                Foreground = foreground,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 1, 0),
                ToolTip = tooltip
            };
            if (cbStyle != null) cb.Style = cbStyle;
            return cb;
        }

        private void OnPersonalityChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_personalityCombo == null || _personalityCombo.SelectedItem == null) return;

            string selected = _personalityCombo.SelectedItem.ToString();
            PersonalityMode newMode;
            if (!Enum.TryParse(selected, out newMode)) return;
            if (newMode == Personality) return;

            Personality = newMode;

            // Update session checkbox visibility based on mode
            bool isIntraday = (newMode == PersonalityMode.Intraday);
            if (_chkAsia != null) _chkAsia.Visibility = isIntraday ? Visibility.Visible : Visibility.Collapsed;
            if (_chkEurope != null) _chkEurope.Visibility = isIntraday ? Visibility.Visible : Visibility.Collapsed;
            if (_chkUSA != null) _chkUSA.Visibility = isIntraday ? Visibility.Visible : Visibility.Collapsed;

            if (ShowDebugLogs) Print(string.Format("[TOOLBAR] Personality changed to {0} - forcing chart reload", newMode));

            // Personality change requires full recalculation of historical bars (F5 equivalent)
            // InvalidateVisual only triggers OnRender, not OnBarUpdate for historical data
            ForceChartReload();
        }

        // Win32 interop for reliable F5 simulation
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP   = 0x0101;
        private const int  VK_F5      = 0x74;

        /// <summary>
        /// Send real F5 keypress to the chart window via Win32 PostMessage.
        /// Required when changing Personality because period sessions are built bar-by-bar.
        /// WPF RaiseEvent doesn't work because NT8 handles F5 at Win32 message level.
        /// </summary>
        private void ForceChartReload()
        {
            if (_chartWindow == null) return;

            try
            {
                var helper = new WindowInteropHelper(_chartWindow);
                IntPtr hwnd = helper.Handle;

                if (hwnd != IntPtr.Zero)
                {
                    PostMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_F5, IntPtr.Zero);
                    PostMessage(hwnd, WM_KEYUP,   (IntPtr)VK_F5, IntPtr.Zero);
                    if (ShowDebugLogs) Print("[TOOLBAR] F5 reload triggered via PostMessage");
                }
                else
                {
                    Print("[TOOLBAR] WARNING: Window handle null - manual F5 needed");
                    RefreshChart();
                }
            }
            catch (Exception ex)
            {
                Print("[TOOLBAR] ForceChartReload error: " + ex.Message);
                RefreshChart(); // Fallback
            }
        }

        private void RefreshChart()
        {
            if (ChartControl != null)
            {
                if (ChartControl.Dispatcher.CheckAccess())
                    ChartControl.InvalidateVisual();
                else
                    ChartControl.Dispatcher.InvokeAsync(() => ChartControl.InvalidateVisual());
            }
        }

        private bool IsTabSelected()
        {
            if (_chartWindow == null || ChartControl == null) return false;
            try
            {
                int idx = _chartWindow.MainTabControl.SelectedIndex;
                if (idx < 0) return false;
                var tabItem = _chartWindow.MainTabControl.Items.GetItemAt(idx) as TabItem;
                if (tabItem == null) return false;
                var chartTab = tabItem.Content as ChartTab;
                return (chartTab != null && ChartControl.ChartTab == chartTab);
            }
            catch { return false; }
        }

        private void ShowHideToolBar(bool show)
        {
            if (_toolBarItems == null || _chartWindow == null) return;

            if (show && !_isToolBarAdded)
            {
                foreach (object item in _toolBarItems)
                    _chartWindow.MainMenu.Add(item);
                _isToolBarAdded = true;
            }
            else if (!show && _isToolBarAdded)
            {
                foreach (object item in _toolBarItems)
                    _chartWindow.MainMenu.Remove(item);
                _isToolBarAdded = false;
            }
        }

        private void OnToolBarTabChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count <= 0) return;
            TabItem tabItem = e.AddedItems[0] as TabItem;
            if (tabItem == null) return;
            ChartTab temp = tabItem.Content as ChartTab;
            if (temp == null) return;
            ShowHideToolBar(IsTabSelected());
        }

        private void RemoveToolBar()
        {
            if (_chartWindow == null) return;

            // Unsubscribe from tab changes
            try { _chartWindow.MainTabControl.SelectionChanged -= OnToolBarTabChanged; } catch { }

            // Unsubscribe combo events
            if (_personalityCombo != null)
            {
                try { _personalityCombo.SelectionChanged -= OnPersonalityChanged; } catch { }
            }
            // Remove all items from MainMenu
            ShowHideToolBar(false);

            // Null out references
            _toolBarItems = null;
            _personalityCombo = null;
            _chkLabels = null;
            _chkSignalText = null;
            _chkAsia = null;
            _chkEurope = null;
            _chkUSA = null;
            _chkExtend = null;
            _chkCfgA = null;
            _chkCfgB = null;
            _chkCfgC = null;
            _chkCfgD = null;
            _chkTradeCfgA = null;
            _chkTradeCfgB = null;
            _chkTradeCfgC = null;
            _chkTradeCfgD = null;
            _toolBarVersionLabel = null;
            _chartWindow = null;
        }

        /// <summary>
        /// Sync toolbar checkboxes with current property values (called if properties change externally)
        /// </summary>
        private void SyncToolBarState()
        {
            if (!_isToolBarAdded) return;

            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                if (_personalityCombo != null && _personalityCombo.SelectedItem?.ToString() != Personality.ToString())
                    _personalityCombo.SelectedItem = Personality.ToString();

                if (_chkLabels != null) _chkLabels.IsChecked = ShowLabels;
                if (_chkSignalText != null) _chkSignalText.IsChecked = ShowSignalText;
                if (_chkAsia != null) _chkAsia.IsChecked = ShowAsia;
                if (_chkEurope != null) _chkEurope.IsChecked = ShowEurope;
                if (_chkUSA != null) _chkUSA.IsChecked = ShowUS;
                if (_chkExtend != null) _chkExtend.IsChecked = ExtendLinesUntilTouch;
                if (_chkCfgA != null) _chkCfgA.IsChecked = _showCfgA;
                if (_chkCfgB != null) _chkCfgB.IsChecked = _showCfgB;
                if (_chkCfgC != null) _chkCfgC.IsChecked = _showCfgC;
                if (_chkCfgD != null) _chkCfgD.IsChecked = _showCfgD;
                if (_chkTradeCfgA != null) _chkTradeCfgA.IsChecked = _tradeCfgA;
                if (_chkTradeCfgB != null) _chkTradeCfgB.IsChecked = _tradeCfgB;
                if (_chkTradeCfgC != null) _chkTradeCfgC.IsChecked = _tradeCfgC;
                if (_chkTradeCfgD != null) _chkTradeCfgD.IsChecked = _tradeCfgD;

                // Show/hide session checkboxes based on personality
                bool isIntraday = (Personality == PersonalityMode.Intraday);
                if (_chkAsia != null) _chkAsia.Visibility = isIntraday ? Visibility.Visible : Visibility.Collapsed;
                if (_chkEurope != null) _chkEurope.Visibility = isIntraday ? Visibility.Visible : Visibility.Collapsed;
                if (_chkUSA != null) _chkUSA.Visibility = isIntraday ? Visibility.Visible : Visibility.Collapsed;
            });
        }
    }
}
