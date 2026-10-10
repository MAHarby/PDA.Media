using System.IO;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using PDA.Media.Utils.Services;
using PDA.Media.Utils.ViewModels;

namespace PDA.Media.Tests;

[TestClass]
public sealed class MainViewModelTests
{
    [TestMethod]
    public void TestFluentThemeColors_AllExpectedPaletteKeysExist()
    {
        var theme = new FluentTheme();

        var expectedKeys = new[]
        {
            "SystemBaseHighColor",
            "SystemBaseMediumHighColor",
            "SystemBaseMediumColor",
            "SystemBaseMediumLowColor",
            "SystemBaseLowColor",
            "SystemAltHighColor",
            "SystemAltMediumHighColor",
            "SystemAltMediumColor",
            "SystemAltMediumLowColor",
            "SystemAltLowColor",
            "SystemChromeHighColor",
            "SystemChromeMediumColor",
            "SystemChromeMediumLowColor",
            "SystemChromeLowColor",
            "SystemChromeAltLowColor",
            "SystemChromeBlackHighColor",
            "SystemChromeBlackMediumColor",
            "SystemChromeBlackMediumLowColor",
            "SystemChromeBlackLowColor",
            "SystemChromeWhiteColor",
            "SystemChromeGrayColor",
            "SystemChromeDisabledHighColor",
            "SystemChromeDisabledLowColor",
            "SystemListLowColor",
            "SystemListMediumColor",
            "SystemRevealListLowColor",
            "SystemRevealListMediumColor",
            "SystemRegionColor",
            "SystemErrorTextColor",
            "SystemAccentColor",
            "SystemAccentColorLight1",
            "SystemAccentColorLight2",
            "SystemAccentColorLight3",
            "SystemAccentColorDark1",
            "SystemAccentColorDark2",
            "SystemAccentColorDark3"
        };

        foreach (var key in expectedKeys)
        {
            bool hasLight = theme.TryGetResource(key, ThemeVariant.Light, out var valLight);
            bool hasDark = theme.TryGetResource(key, ThemeVariant.Dark, out var valDark);

            Assert.IsTrue(hasLight, $"Missing key {key} in Light theme variant");
            Assert.IsTrue(hasDark, $"Missing key {key} in Dark theme variant");
            Assert.IsInstanceOfType(valLight, typeof(Color), $"Key {key} Light is not a Color");
            Assert.IsInstanceOfType(valDark, typeof(Color), $"Key {key} Dark is not a Color");
        }
    }

    [TestMethod]
    public void TestMainViewModel_SourcePathChange_RefreshesMediaNodes()
    {
        string tempDir1 = Path.Combine(Path.GetTempPath(), "pda_test_dir1_" + Guid.NewGuid().ToString("N"));
        string tempDir2 = Path.Combine(Path.GetTempPath(), "pda_test_dir2_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempDir1);
            Directory.CreateDirectory(Path.Combine(tempDir1, "Season 1"));
            File.WriteAllText(Path.Combine(tempDir1, "Season 1", "Episode 1.mkv"), "dummy");

            Directory.CreateDirectory(tempDir2);
            Directory.CreateDirectory(Path.Combine(tempDir2, "Season 2"));
            File.WriteAllText(Path.Combine(tempDir2, "Season 2", "Episode 1.mkv"), "dummy");
            File.WriteAllText(Path.Combine(tempDir2, "Season 2", "Episode 2.mkv"), "dummy");

            using var storage = new TempServices();
            var vm = new MainViewModel(storage.SettingsService, storage.ProfileService)
            {
                SourcePath = tempDir1
            };

            Assert.HasCount(1, vm.SourceMediaNodes);
            var root1 = vm.SourceMediaNodes[0];
            Assert.AreEqual(Path.GetFileName(tempDir1), root1.Name);
            Assert.IsNotNull(root1.SubNodes);
            Assert.HasCount(1, root1.SubNodes);
            Assert.AreEqual("Season 1", root1.SubNodes[0].Name);

            // Change SourcePath to tempDir2
            vm.SourcePath = tempDir2;

            Assert.HasCount(1, vm.SourceMediaNodes);
            var root2 = vm.SourceMediaNodes[0];
            Assert.AreEqual(Path.GetFileName(tempDir2), root2.Name);
            Assert.IsNotNull(root2.SubNodes);
            Assert.HasCount(1, root2.SubNodes);
            Assert.AreEqual("Season 2", root2.SubNodes[0].Name);
            Assert.HasCount(2, root2.SubNodes[0].SubNodes!);
        }
        finally
        {
            if (Directory.Exists(tempDir1)) Directory.Delete(tempDir1, true);
            if (Directory.Exists(tempDir2)) Directory.Delete(tempDir2, true);
        }
    }

    [TestMethod]
    public void TestAppSettingsService_SavesAndLoadsSettings()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), "pda_settings_test_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var service = new AppSettingsService(tempFile);
            var initial = service.LoadSettings();
            Assert.IsNotNull(initial);

            service.SaveSettings(new UserSettings
            {
                SourcePath = @"C:\Media\Source",
                DestinationPath = @"D:\Media\Destination"
            });

            Assert.IsTrue(File.Exists(tempFile));

            var loaded = service.LoadSettings();
            Assert.AreEqual(@"C:\Media\Source", loaded.SourcePath);
            Assert.AreEqual(@"D:\Media\Destination", loaded.DestinationPath);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [TestMethod]
    public void TestMainViewModel_PersistsAndRestoresSourceAndDestinationPaths()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), "pda_vm_settings_test_" + Guid.NewGuid().ToString("N") + ".json");
        string tempDir = Path.Combine(Path.GetTempPath(), "pda_vm_src_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "sample.mkv"), "dummy");

            var service = new AppSettingsService(tempFile);
            using var storage = new TempServices();

            // First session: Create ViewModel and update paths
            var vm1 = new MainViewModel(service, storage.ProfileService)
            {
                SourcePath = tempDir,
                DestinationPath = @"E:\Target\Output"
            };

            // Second session: New ViewModel with same settings file
            var vm2 = new MainViewModel(service, storage.ProfileService);

            Assert.AreEqual(tempDir, vm2.SourcePath);
            Assert.AreEqual(@"E:\Target\Output", vm2.DestinationPath);
            Assert.HasCount(1, vm2.SourceMediaNodes);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public void TestLoadDestinationItems_RecursivelyAddsOnlySelectedFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "pda_dest_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            string season1 = Path.Combine(tempDir, "Season 1");
            string season2 = Path.Combine(tempDir, "Season 2");
            Directory.CreateDirectory(season1);
            Directory.CreateDirectory(season2);

            File.WriteAllText(Path.Combine(season1, "S01E01.mkv"), "dummy");
            File.WriteAllText(Path.Combine(season1, "S01E02.mkv"), "dummy");
            File.WriteAllText(Path.Combine(season2, "S02E01.mkv"), "dummy");
            File.WriteAllText(Path.Combine(tempDir, "standalone.mp4"), "dummy");

            using var storage = new TempServices();
            var vm = new MainViewModel(storage.SettingsService, storage.ProfileService)
            {
                SourcePath = tempDir
            };

            // Select all via root node
            var root = vm.SourceMediaNodes[0];
            root.Selected = true;

            // Execute command
            vm.LoadDestinationItemsCommand.Execute(null);

            // Should contain all 4 files, and zero folders
            Assert.HasCount(4, vm.DestinationItems);
            Assert.IsNotNull(vm.SelectedDestinationItem);
            Assert.AreEqual(vm.DestinationItems[0], vm.SelectedDestinationItem);

            var paths = vm.DestinationItems.Select(d => d.FullPath).ToList();
            CollectionAssert.Contains(paths, Path.Combine(season1, "S01E01.mkv"));
            CollectionAssert.Contains(paths, Path.Combine(season1, "S01E02.mkv"));
            CollectionAssert.Contains(paths, Path.Combine(season2, "S02E01.mkv"));
            CollectionAssert.Contains(paths, Path.Combine(tempDir, "standalone.mp4"));

            // Deselect Season 2
            var season2Node = root.SubNodes!.First(n => n.Name == "Season 2");
            season2Node.Selected = false;

            vm.LoadDestinationItemsCommand.Execute(null);

            Assert.HasCount(3, vm.DestinationItems);
            var updatedPaths = vm.DestinationItems.Select(d => d.FullPath).ToList();
            CollectionAssert.Contains(updatedPaths, Path.Combine(season1, "S01E01.mkv"));
            CollectionAssert.Contains(updatedPaths, Path.Combine(season1, "S01E02.mkv"));
            CollectionAssert.Contains(updatedPaths, Path.Combine(tempDir, "standalone.mp4"));
            CollectionAssert.DoesNotContain(updatedPaths, Path.Combine(season2, "S02E01.mkv"));

            // Deselect all
            root.Selected = false;
            vm.LoadDestinationItemsCommand.Execute(null);

            Assert.HasCount(0, vm.DestinationItems);
            Assert.IsNull(vm.SelectedDestinationItem);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
