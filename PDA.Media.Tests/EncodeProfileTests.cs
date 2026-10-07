using System;
using System.IO;
using System.Linq;
using PDA.Media.Utils.Models;
using PDA.Media.Utils.Services;
using PDA.Media.Utils.ViewModels;

namespace PDA.Media.Tests;

[TestClass]
public sealed class EncodeProfileTests
{
    [TestMethod]
    public void TestEncodeProfile_Clone_CreatesDeepCopy()
    {
        var profile = new EncodeProfile
        {
            Name = "Bluray 4K",
            Description = "4K HEVC Profile",
            TargetCategory = "Movie",
            ContainerFormat = "mkv",
            VideoCodec = "libx265",
            Preset = "slow",
            Crf = 18,
            PixelFormat = "yuv420p10le",
            AudioCodec = "aac",
            AudioBitrate = 320,
            AudioChannels = "5.1 Surround",
            KeepAllStreams = true,
            CopySubtitles = true,
            CustomArguments = "-tune film",
            IsPredefined = true
        };

        var clone = profile.Clone();

        Assert.AreNotEqual(profile.Id, clone.Id);
        Assert.AreEqual(profile.Name, clone.Name);
        Assert.AreEqual(profile.Description, clone.Description);
        Assert.AreEqual(profile.TargetCategory, clone.TargetCategory);
        Assert.AreEqual(profile.ContainerFormat, clone.ContainerFormat);
        Assert.AreEqual(profile.VideoCodec, clone.VideoCodec);
        Assert.AreEqual(profile.Preset, clone.Preset);
        Assert.AreEqual(profile.Crf, clone.Crf);
        Assert.AreEqual(profile.PixelFormat, clone.PixelFormat);
        Assert.AreEqual(profile.AudioCodec, clone.AudioCodec);
        Assert.AreEqual(profile.AudioBitrate, clone.AudioBitrate);
        Assert.AreEqual(profile.AudioChannels, clone.AudioChannels);
        Assert.AreEqual(profile.KeepAllStreams, clone.KeepAllStreams);
        Assert.AreEqual(profile.CopySubtitles, clone.CopySubtitles);
        Assert.AreEqual(profile.CustomArguments, clone.CustomArguments);
        Assert.AreEqual(profile.IsPredefined, clone.IsPredefined);

        // Modifying clone does not mutate original
        clone.Name = "Modified Name";
        clone.Crf = 28;
        Assert.AreEqual("Bluray 4K", profile.Name);
        Assert.AreEqual(18, profile.Crf);
    }

    [TestMethod]
    public void TestEncodeProfile_GeneratedFFMpegArguments_ProducesExpectedOutput()
    {
        var profile = new EncodeProfile
        {
            VideoCodec = "libx265",
            Preset = "fast",
            Crf = 22,
            PixelFormat = "yuv420p10le",
            AudioCodec = "copy",
            KeepAllStreams = true,
            CopySubtitles = true
        };

        string args = profile.GeneratedFFMpegArguments;

        Assert.Contains("-map 0", args);
        Assert.Contains("-c:v:0 libx265", args);
        Assert.Contains("-preset fast", args);
        Assert.Contains("-crf 22", args);
        Assert.Contains("-pix_fmt yuv420p10le", args);
        Assert.Contains("-c:a copy", args);
        Assert.Contains("-c:s copy", args);
    }

    [TestMethod]
    public void TestEncodeProfile_GeneratedFFMpegArguments_WithCustomAudioAndResolution()
    {
        var profile = new EncodeProfile
        {
            VideoCodec = "libx264",
            Preset = "medium",
            Crf = 20,
            PixelFormat = "yuv420p",
            Resolution = "1280x720 (720p HD)",
            FrameRate = "24",
            AudioCodec = "aac",
            AudioBitrate = 192,
            AudioChannels = "Stereo (2.0)",
            AudioSampleRate = "48000 Hz",
            KeepAllStreams = false,
            CopySubtitles = false,
            HardwareAcceleration = "cuda",
            CustomArguments = "-tune film"
        };

        string args = profile.GeneratedFFMpegArguments;

        Assert.Contains("-hwaccel cuda", args);
        Assert.DoesNotContain("-map 0", args);
        Assert.Contains("-c:v:0 libx264", args);
        Assert.Contains("-preset medium", args);
        Assert.Contains("-crf 20", args);
        Assert.Contains("-pix_fmt yuv420p", args);
        Assert.Contains("-vf scale=1280x720", args);
        Assert.Contains("-r 24", args);
        Assert.Contains("-c:a aac", args);
        Assert.Contains("-b:a 192k", args);
        Assert.Contains("-ac 2", args);
        Assert.Contains("-ar 48000", args);
        Assert.Contains("-tune film", args);
        Assert.DoesNotContain("-c:s copy", args);
    }

    [TestMethod]
    public void TestEncoderProfileService_LoadsDefaultsWhenFileDoesNotExist()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), "pda_profiles_test_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var service = new EncoderProfileService(tempFile);
            var profiles = service.LoadProfiles();

            Assert.IsNotNull(profiles);
            Assert.IsGreaterThanOrEqualTo(profiles.Count, 7);
            Assert.IsTrue(File.Exists(tempFile), "Should save default profiles on disk");

            var blurayMovie = service.GetProfileByName("Bluray Movie");
            Assert.IsNotNull(blurayMovie);
            Assert.AreEqual("libx265", blurayMovie.VideoCodec);
            Assert.AreEqual(22, blurayMovie.Crf);
            Assert.AreEqual("yuv420p10le", blurayMovie.PixelFormat);
            Assert.AreEqual("copy", blurayMovie.AudioCodec);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [TestMethod]
    public void TestEncoderProfileService_SavesAndReloadsCustomProfiles()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), "pda_profiles_custom_test_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var service = new EncoderProfileService(tempFile);
            var initial = service.LoadProfiles();

            var customProfile = new EncodeProfile
            {
                Name = "My 4K HDR Profile",
                Description = "Custom profile for 4K remuxes",
                TargetCategory = "Movie",
                VideoCodec = "hevc_nvenc",
                Crf = 19,
                Preset = "slow",
                AudioCodec = "eac3",
                AudioBitrate = 640
            };

            service.SaveProfile(customProfile);

            var reloadedService = new EncoderProfileService(tempFile);
            var loadedProfile = reloadedService.GetProfileByName("My 4K HDR Profile");

            Assert.IsNotNull(loadedProfile);
            Assert.AreEqual("My 4K HDR Profile", loadedProfile.Name);
            Assert.AreEqual("Custom profile for 4K remuxes", loadedProfile.Description);
            Assert.AreEqual("hevc_nvenc", loadedProfile.VideoCodec);
            Assert.AreEqual(19, loadedProfile.Crf);
            Assert.AreEqual("slow", loadedProfile.Preset);
            Assert.AreEqual("eac3", loadedProfile.AudioCodec);
            Assert.AreEqual(640, loadedProfile.AudioBitrate);

            // Delete profile
            bool deleted = reloadedService.DeleteProfile("My 4K HDR Profile");
            Assert.IsTrue(deleted);
            Assert.IsNull(reloadedService.GetProfileByName("My 4K HDR Profile"));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [TestMethod]
    public void TestEncoderProfilesViewModel_AddDuplicateSaveAndFilterProfiles()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), "pda_profiles_vm_test_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var service = new EncoderProfileService(tempFile);
            var vm = new EncoderProfilesViewModel(service);

            int initialCount = vm.Profiles.Count;
            Assert.IsGreaterThanOrEqualTo(initialCount, 7);
            Assert.IsNotNull(vm.SelectedProfile);
            Assert.IsNotNull(vm.EditingProfile);

            // Add Profile
            vm.AddProfileCommand.Execute(null);
            Assert.HasCount(initialCount + 1, vm.Profiles);
            Assert.IsNotNull(vm.SelectedProfile);
            Assert.AreEqual("New Profile", vm.SelectedProfile.Name);

            // Edit and Save
            vm.EditingProfile!.Name = "Unique Action Movie Profile";
            vm.EditingProfile.Crf = 16;
            vm.EditingProfile.Preset = "veryslow";
            vm.SaveProfileCommand.Execute(null);

            Assert.AreEqual("Unique Action Movie Profile", vm.SelectedProfile.Name);
            Assert.AreEqual(16, vm.SelectedProfile.Crf);
            Assert.AreEqual("veryslow", vm.SelectedProfile.Preset);

            // Clone / Duplicate
            vm.DuplicateProfileCommand.Execute(null);
            Assert.HasCount(initialCount + 2, vm.Profiles);
            Assert.AreEqual("Unique Action Movie Profile (Copy)", vm.SelectedProfile.Name);

            // Search Filter
            vm.SearchText = "Unique Action";
            Assert.HasCount(2, vm.FilteredProfiles);

            vm.SearchText = string.Empty;
            Assert.HasCount(initialCount + 2, vm.FilteredProfiles);

            // Delete
            vm.DeleteProfileCommand.Execute(null);
            Assert.HasCount(initialCount + 1, vm.Profiles);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [TestMethod]
    public void TestMainViewModel_StartupLoadsProfilesAndMaintainsCurrentProfile()
    {
        string tempSettingsFile = Path.Combine(Path.GetTempPath(), "pda_main_settings_" + Guid.NewGuid().ToString("N") + ".json");
        string tempProfilesFile = Path.Combine(Path.GetTempPath(), "pda_main_profiles_" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            var settingsService = new AppSettingsService(tempSettingsFile);
            var profileService = new EncoderProfileService(tempProfilesFile);

            var mainVm = new MainViewModel(settingsService, profileService);

            Assert.IsNotNull(mainVm.EncoderProfiles);
            Assert.IsGreaterThanOrEqualTo(mainVm.EncoderProfiles.Count, 7);
            Assert.Contains("Bluray TV", mainVm.EncoderProfiles);
            Assert.Contains("Bluray Movie", mainVm.EncoderProfiles);

            // Select Bluray Movie
            mainVm.SelectedEncoderProfile = "Bluray Movie";
            Assert.IsNotNull(mainVm.CurrentEncodeProfile);
            Assert.AreEqual("Bluray Movie", mainVm.CurrentEncodeProfile.Name);
            Assert.AreEqual(22, mainVm.CurrentEncodeProfile.Crf);
            Assert.AreEqual("libx265", mainVm.CurrentEncodeProfile.VideoCodec);

            // Select DVD TV
            mainVm.SelectedEncoderProfile = "DVD TV";
            Assert.IsNotNull(mainVm.CurrentEncodeProfile);
            Assert.AreEqual("DVD TV", mainVm.CurrentEncodeProfile.Name);
            Assert.AreEqual("720x576 (576p)", mainVm.CurrentEncodeProfile.Resolution);

            // Verify settings persistence
            var savedSettings = settingsService.LoadSettings();
            Assert.AreEqual("DVD TV", savedSettings.EncoderProfile);

            // New session restores selected profile
            var mainVm2 = new MainViewModel(settingsService, profileService);
            Assert.AreEqual("DVD TV", mainVm2.SelectedEncoderProfile);
            Assert.IsNotNull(mainVm2.CurrentEncodeProfile);
            Assert.AreEqual("DVD TV", mainVm2.CurrentEncodeProfile.Name);
        }
        finally
        {
            if (File.Exists(tempSettingsFile)) File.Delete(tempSettingsFile);
            if (File.Exists(tempProfilesFile)) File.Delete(tempProfilesFile);
        }
    }
}
