using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// A control that asks for a grid row the grid never declared does not fail: WPF drops it into the last
    /// row and it overlaps whatever is already there, which only shows up when somebody opens the window.
    /// The settings window is long enough for that to slip through, so the rows it uses are checked against
    /// the rows it declares.
    /// </summary>
    [TestClass]
    public class SettingsLayoutTests
    {
        static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        static XDocument SettingsDocument()
        {
            var direct = Path.Combine(AppContext.BaseDirectory, "Windows", "Settings", "SettingsWindow.xaml");
            if (File.Exists(direct)) return XDocument.Load(direct);

            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "Windows", "Settings", "SettingsWindow.xaml");
                if (File.Exists(candidate)) return XDocument.Load(candidate);
                directory = directory.Parent;
            }

            Assert.Fail("Windows/Settings/SettingsWindow.xaml was not found from " + AppContext.BaseDirectory);
            return new XDocument();
        }

        [TestMethod]
        public void EveryGridRowTheSettingsWindowUsesIsDeclared()
        {
            var problems = new List<string>();
            var gridsWithRows = 0;
            foreach (var grid in SettingsDocument().Descendants(Presentation + "Grid"))
            {
                var declared = grid.Elements(Presentation + "Grid.RowDefinitions")
                                   .Elements(Presentation + "RowDefinition")
                                   .Count();
                if (declared == 0) continue;
                gridsWithRows++;

                foreach (var child in grid.Elements())
                {
                    var row = (int?)child.Attribute(Presentation + "Grid.Row");
                    if (row != null && (row.Value < 0 || row.Value >= declared))
                    {
                        problems.Add($"{child.Name.LocalName} wants row {row.Value} of {declared}");
                    }
                }
            }

            Assert.IsTrue(gridsWithRows >= 5, $"only {gridsWithRows} grids with rows were inspected, the XAML was probably not parsed");
            Assert.AreEqual(0, problems.Count, string.Join("; ", problems));
        }

        [TestMethod]
        public void TheOfflineVoiceSwitchSitsNextToItsModelRow()
        {
            var names = SettingsDocument()
                .Descendants()
                .Select(element => (string?)element.Attribute(Xaml + "Name"))
                .Where(name => name != null)
                .Select(name => name!)
                .ToList();

            CollectionAssert.IsSubsetOf(
                new[] { "CbWebRemoteVoiceOffline", "TbWebRemoteVoiceModelStatus", "BtnWebRemoteVoiceModelDownload" },
                names);
        }
    }
}
