using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
        private List<PluginInfo> plugins { get; set; } = new List<PluginInfo>();
        public ICollectionView Plugins { get; set; } = null;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            
            Plugins = CollectionViewSource.GetDefaultView(plugins);
            Plugins.Filter = p => (p as PluginInfo).Present;

            Init();
        }

        private void Init()
        {
            plugins.AddRange(new[] {
                new PluginInfo() { Fixed = true, Active = true, Name = "Fallout4.esm" },
                new PluginInfo() { Fixed = true, Active = true, Name = "DLCRobot.esm" },
                new PluginInfo() { Fixed = true, Active = true, Name = "DLCWorkshop01.esm" },
                new PluginInfo() { Fixed = true, Active = true, Name = "DLCCoast.esm" },
                new PluginInfo() { Fixed = true, Active = true, Name = "DLCWorkshop02.esm" },
                new PluginInfo() { Fixed = true, Active = true, Name = "DLCWorkshop03.esm" },
                new PluginInfo() { Fixed = true, Active = true, Name = "DLCNukaWorld.esm" }
            });

            try
            {
                LoadStuff();
                Plugins.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(Environment.ExitCode);
            }
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

        public class PluginInfo
        {
            public long Size { get; set; }
            public bool Fixed { get; set; }
            public bool NotFixed { get { return !Fixed; } }
            public bool Active { get; set; }
            public bool Present { get; set; }
            public string Index { get; set; }
            public string Name { get; set; }
            public string Type { get; set; }
            public string Author { get; set; }
            public string Description { get; set; }
            public int NumberOfRecords { get; set; }
            public string[] Masters { get; set; }
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


        private void LoadStuff()
        {
            PopulatePaths();

            var pluginList = GetPluginList();
            foreach (var p in pluginList)
            {
                plugins.Add(p);
            }
            
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
            UpdateIndicies();
        }

        private void UpdateIndicies()
        {
            var idx = 0;
            foreach (var p in plugins)
            {
                if (p.Active && p.Present)
                {
                    p.Index = (++idx).ToString("X2");
                }
                else
                {
                    p.Index = "";
                }
            }
        }
        
        private bool TryGetSelectedPlugin(out PluginInfo plugin)
        {
            plugin = null;

            var selection = lbPlugins.SelectedItem;
            if (selection == null) return false;

            plugin = selection as PluginInfo;

            return true;
        }

        private void lbPlugins_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PluginInfo plugin;
            if (!TryGetSelectedPlugin(out plugin)) return;

            var sb = new StringBuilder();
            sb.AppendLine($"Author: {plugin.Author}");
            sb.AppendLine($"Description: {plugin.Description}");
            sb.AppendLine();
            sb.AppendLine($"Type: {plugin.Type}");
            sb.AppendLine($"Size: {plugin.Size} bytes");
            sb.AppendLine($"Total records: {plugin.NumberOfRecords}");
            sb.AppendLine();
            sb.Append($"Masters:\n  {string.Join("\n  ", plugin.Masters)}");

            pluginTextBlock.Text = sb.ToString();
       }

       private void Swap<T>(List<T> array, int a, int b)
        {
            var tmp = array[a];
            array[a] = array[b];
            array[b] = tmp;
        }

        private void SaveStuff()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# This file is used by the game to keep track of your downloaded content.");
            sb.AppendLine("# Please do not modify this file.");
            foreach (var p in plugins)
            {
                if (p.Fixed) continue;

                if (p.Active) sb.Append("*");
                sb.AppendLine(p.Name);
            }

            System.IO.File.WriteAllText(pluginListPath, sb.ToString());
        }

        private void btnMoveUp_Click(object sender, RoutedEventArgs e)
        {
            PluginInfo plugin;
            if (!TryGetSelectedPlugin(out plugin)) return;

            if (plugin.Fixed) return;

            var idx = plugins.IndexOf(plugin);
            
            for (var i = idx - 1; i >= 0; i--)
            {
                var p = plugins[i];
                if (p.Fixed) return;
                if (Array.Exists(plugin.Masters, s => s.ToLower() == p.Name.ToLower())) return;

                if (p.Present)
                {
                    Swap(plugins, i, idx);
                    UpdateIndicies();
                    Plugins.Refresh();
                    break;
                }
            }
        }

        private void btnMoveDown_Click(object sender, RoutedEventArgs e)
        {
            PluginInfo plugin;
            if (!TryGetSelectedPlugin(out plugin)) return;

            if (plugin.Fixed) return;

            var idx = plugins.IndexOf(plugin);

            for (var i = idx + 1; i < plugins.Count; i++)
            {
                var p = plugins[i];

                if (p.Present)
                {
                    Swap(plugins, i, idx);
                    UpdateIndicies();
                    Plugins.Refresh();
                    break;
                }
            }
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            SaveStuff();
        }

        private void btnReset_Click(object sender, RoutedEventArgs e)
        {
            plugins.Clear();
            pluginTextBlock.Text = "";
            Init();
        }

        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateIndicies();
            Plugins.Refresh();
        }
    }
}
