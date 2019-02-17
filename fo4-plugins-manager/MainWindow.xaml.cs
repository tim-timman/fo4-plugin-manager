using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace fo4_plugins_manager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private string dataPath = null;
        private string installPath = null;
        private string pluginListPath = null;

        private void PopulatePaths()
        {
            var regKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Bethesda Softworks\Fallout4";
            var regValue = "Installed Path";
            installPath = Microsoft.Win32.Registry.GetValue(regKey, regValue, null) as string;

            // Screw good code right now...
            if (installPath == null)
            {
                throw new ApplicationException("Failed to find installation path");
            }

            dataPath = System.IO.Path.Combine(installPath, "Data");

            pluginListPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Fallout4",
                "Plugins.txt"
            );
        }

        class PluginInfo
        {
            public long Size;
            public bool Fixed;
            public bool Active;
            public bool Present;
            public string Name;
            public string Type;
            public string Author;
            public string Description;
            public int NumberOfRecords;
            public string[] Masters;
        }

        private PluginInfo GetPluginInfo(System.IO.FileInfo plugin)
        {
            var pi = new PluginInfo()
            {
                Present = true,
                Name = plugin.Name,
                Size = plugin.Length,
            };
            byte[] record;
            byte[] data;
            using (var fs = plugin.OpenRead())
            {
                using (var br = new System.IO.BinaryReader(fs, Encoding.UTF8))
                {
                    record = br.ReadBytes(24);
                    var TES4 = Encoding.UTF8.GetString(record, 0, 4);

                    // First four bytes as chars should be TES4
                    if (TES4 != "TES4")
                    {
                        throw new ApplicationException("Not a valid Plugin");
                    }

                    var dataSize = BitConverter.ToUInt32(record, 4);
                    var flags = BitConverter.ToUInt32(record, 8);

                    if ((flags & 0x00000200) > 0)
                        pi.Type = "ESL";
                    else if ((flags & 0x00000001) > 0)
                        pi.Type = "ESM";
                    else
                        pi.Type = "ESP";

                    data = br.ReadBytes(Convert.ToInt32(dataSize));
                }
            }
            
            var idx = 0;

            var masters = new List<string>();
            
            while (idx < data.Length)
            {
                var field = Encoding.UTF8.GetString(data, idx, 4);
                var fDataSize = BitConverter.ToUInt16(data, idx + 4);
                idx += 6;

                switch (field)
                {
                    case "HEDR":
                        var numbRecords = BitConverter.ToInt32(data, idx + 4);
                        pi.NumberOfRecords = numbRecords;
                        break;
                    case "CNAM":
                    case "SNAM":
                    case "MAST":
                        var end = Array.FindIndex(data, idx, b => b == 0);
                        var text = Encoding.UTF8.GetString(data, idx, end - idx);

                        if (field == "CNAM")
                            pi.Author = text;
                        if (field == "SNAM")
                            pi.Description = text;
                        if (field == "MAST")
                            masters.Add(text);
                        break;
                    default:
                        break;
                }
                idx += fDataSize;
            }

            pi.Masters = masters.ToArray();
            return pi;
        }

        private PluginInfo[] GetPlugins()
        {
            var validExtensions = new[] { ".esl", ".esm", ".esp" };
            var fileInfos = new System.IO.DirectoryInfo(dataPath).GetFiles();
            var filteredFileInfos = fileInfos.Where(fi => validExtensions.Contains(fi.Extension));
            var pluginInfos = filteredFileInfos.Select(fi => GetPluginInfo(fi));
            return pluginInfos.ToArray();
        }

        private PluginInfo[] GetPluginList()
        {
            var pluginsStrings = System.IO.File.ReadAllLines(pluginListPath);
            var plugins = pluginsStrings.Where(s => !s.StartsWith("#"));
            var pluginInfos = plugins.Select(p => new PluginInfo()
            {
                Active = p.StartsWith("*"),
                Name = p.TrimStart('*'),
            });
            return pluginInfos.ToArray();
        }

        private List<PluginInfo> plugins = new List<PluginInfo>() {
            new PluginInfo() { Fixed = true, Active = true, Name = "Fallout4.esm" },
            new PluginInfo() { Fixed = true, Active = true, Name = "DLCRobot.esm" },
            new PluginInfo() { Fixed = true, Active = true, Name = "DLCWorkshop01.esm" },
            new PluginInfo() { Fixed = true, Active = true, Name = "DLCCoast.esm" },
            new PluginInfo() { Fixed = true, Active = true, Name = "DLCWorkshop02.esm" },
            new PluginInfo() { Fixed = true, Active = true, Name = "DLCWorkshop03.esm" },
            new PluginInfo() { Fixed = true, Active = true, Name = "DLCNukaWorld.esm" },
        };

        private void LoadStuff()
        {
            PopulatePaths();
            var pluginList = GetPluginList();
            plugins.AddRange(pluginList);
            
            var presentPlugins = GetPlugins();
            // Merge 'em
            foreach (var p in presentPlugins)
            {
                var match = plugins.FirstOrDefault(q => q.Name.ToLower() == p.Name.ToLower());
                if (match != null)
                {
                    match.Size = p.Size;
                    match.Type = p.Type;
                    match.Author = p.Author;
                    match.Present = p.Present;
                    match.Masters = p.Masters;
                    match.Description = p.Description;
                    match.NumberOfRecords = p.NumberOfRecords;
                }
                else
                {
                    plugins.Add(p);
                }
            }
        }

        private void DrawStuff()
        {
            var presentPlugins = plugins.Where(p => p.Present);
            foreach (var p in presentPlugins)
            {
                var checkbox = new CheckBox();
                checkbox.Content = p.Name;
                checkbox.IsChecked = p.Active;
                checkbox.IsEnabled = !p.Fixed;
                checkbox.Checked += Checkbox_Checked;
                lbPlugins.Items.Add(checkbox);
            }

            lbPlugins.SelectionChanged += LbPlugins_SelectionChanged;
        }

        private void LbPlugins_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selection = lbPlugins.SelectedItem;
            if (selection != null)
            {
                var checkbox = selection as CheckBox;
                var plugin = plugins.Find(p => p.Name == (string)checkbox.Content);

            }
        }

        private void Checkbox_Checked(object sender, RoutedEventArgs e)
        {
            var checkbox = sender as CheckBox;
            var plugin = plugins.Find(p => p.Name == (string)checkbox.Content);
            plugin.Active = checkbox.IsChecked ?? !plugin.Active;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadStuff();
                DrawStuff();
            } catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(Environment.ExitCode);
            }
        }
    }
}
