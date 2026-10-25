namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor;
    using WallstopStudios.DataVisualizer.Editor.Data;
    using WallstopStudios.DataVisualizer.Editor.UI;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class EditorConfigurationTests
    {
        [Test]
        public void ShouldPersistAndClearInputProfileWithoutSavingUnrelatedAssets()
        {
            using TestCleanupScope cleanup = new();
            string folder = "Assets/ProfileConfiguration-" + Guid.NewGuid().ToString("N");
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            DataVisualizerInputProfile profile = ScriptableObject.CreateInstance<DataVisualizerInputProfile>();
            TestDataObject unrelated = ScriptableObject.CreateInstance<TestDataObject>();
            string settingsPath = folder + "/Settings.asset";
            string profilePath = folder + "/Profile.asset";
            string unrelatedPath = folder + "/Other.asset";
            AssetDatabase.CreateAsset(settings, settingsPath);
            AssetDatabase.CreateAsset(profile, profilePath);
            AssetDatabase.CreateAsset(unrelated, unrelatedPath);
            AssetDatabase.SaveAssets();
            byte[] before = File.ReadAllBytes(unrelatedPath);
            unrelated.name = "Dirty unrelated asset";
            EditorUtility.SetDirty(unrelated);
            EditorConfigurationService service = new(settings);
            string guid = AssetDatabase.AssetPathToGUID(profilePath);
            Assert.That(service.SetInputProfileGuid(guid), Is.True);
            Assert.That(settings.inputProfile, Is.SameAs(profile));
            Assert.That(service.InputProfileGuid, Is.EqualTo(guid));
            Assert.That(EditorUtility.IsDirty(settings), Is.False);
            Assert.That(File.ReadAllText(settingsPath), Does.Contain(guid));
            Assert.That(service.SetInputProfileGuid(guid), Is.False);
            Assert.That(service.SetInputProfileGuid(string.Empty), Is.True);
            Assert.That(settings.inputProfile == null);
            Assert.That(service.InputProfileGuid, Is.Empty);
            Assert.That(File.ReadAllText(settingsPath), Does.Not.Contain(guid));
            Assert.That(EditorUtility.IsDirty(unrelated), Is.True);
            Assert.That(File.ReadAllBytes(unrelatedPath), Is.EqualTo(before));
        }

        [Test]
        public void ShouldRejectReadOnlyInputProfileAssignmentBeforeMutation()
        {
            using TestCleanupScope cleanup = new();
            string folder = "Assets/ProfileReadOnly-" + Guid.NewGuid().ToString("N");
            string path = folder + "/Settings.asset";
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            DataVisualizerInputProfile profile = ScriptableObject.CreateInstance<DataVisualizerInputProfile>();
            AssetDatabase.CreateAsset(settings, path);
            AssetDatabase.CreateAsset(profile, folder + "/Profile.asset");
            AssetDatabase.SaveAssets();
            byte[] before = File.ReadAllBytes(path);
            FileAttributes original = File.GetAttributes(path);
            cleanup.Defer(() => File.SetAttributes(path, original));
            File.SetAttributes(path, original | FileAttributes.ReadOnly);
            EditorConfigurationException error = Assert.Throws<EditorConfigurationException>(() =>
                new EditorConfigurationService(settings).SetInputProfileGuid(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile)))
            );
            Assert.That(error.Code, Is.EqualTo("read_only"));
            Assert.That(settings.inputProfile == null);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }

        [TestCase(null)]
        [TestCase("invalid")]
        [TestCase("00000000000000000000000000000000")]
        public void ShouldRejectInvalidInputProfilesBeforeMutation(string guid)
        {
            using TestCleanupScope cleanup = new();
            DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));
            Assert.Throws<ArgumentException>(() => new EditorConfigurationService(settings).SetInputProfileGuid(guid));
            Assert.That(settings.inputProfile == null);
        }

        [Test]
        public void ShouldPersistBothPreferencesWithoutSavingUnrelatedDirtyAssets()
        {
            using TestCleanupScope cleanup = new();
            string folder = "Assets/Configuration-" + Guid.NewGuid().ToString("N");
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            string path = folder + "/Settings.asset";
            AssetDatabase.CreateAsset(settings, path);
            TestDataObject unrelated = ScriptableObject.CreateInstance<TestDataObject>();
            string unrelatedPath = folder + "/Unrelated.asset";
            AssetDatabase.CreateAsset(unrelated, unrelatedPath);
            AssetDatabase.SaveAssets();
            byte[] unrelatedBytes = File.ReadAllBytes(unrelatedPath);
            unrelated.name = "Unrelated dirty value";
            EditorUtility.SetDirty(unrelated);
            EditorConfigurationService service = EditorConfigurationService.FromGuid(AssetDatabase.AssetPathToGUID(path));
            Assert.That(service.SettingsPath, Is.EqualTo(path));
            Assert.That(service.SettingsGuid, Is.EqualTo(AssetDatabase.AssetPathToGUID(path)));

            Assert.That(service.Apply(folder, true), Is.True);
            Assert.That(service.DataFolderPath, Is.EqualTo(folder));
            Assert.That(service.SelectActiveObject, Is.True);
            Assert.That(EditorUtility.IsDirty(settings), Is.False);
            Assert.That(EditorUtility.IsDirty(unrelated), Is.True);
            Assert.That(File.ReadAllBytes(unrelatedPath), Is.EqualTo(unrelatedBytes));
            string saved = File.ReadAllText(path);
            Assert.That(saved, Does.Contain(folder));
            Assert.That(saved, Does.Contain("selectActiveObject: 1"));

            settings.persistStateInSettingsAsset = true;
            EditorUtility.SetDirty(settings);
            Assert.That(service.Apply(folder, true), Is.False);
            Assert.That(EditorUtility.IsDirty(settings), Is.True, "Idempotent apply does not save other pending edits.");
            Assert.That(File.ReadAllText(path), Is.EqualTo(saved));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("AssetsBackup")]
        [TestCase("Packages")]
        [TestCase("Assets/../Assets")]
        [TestCase("Assets/./")]
        [TestCase("Assets//Missing")]
        [TestCase("Assets\\Missing")]
        [TestCase("Assets/Missing-Configuration-Folder")]
        public void ShouldRejectInvalidFoldersBeforeChangingEitherPreference(string path)
        {
            using TestCleanupScope cleanup = new();
            DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));
            EditorUtility.ClearDirty(settings);
            EditorConfigurationService service = new(settings);
            Assert.Throws<ArgumentException>(() => service.Apply(path, true));
            Assert.That(service.DataFolderPath, Is.EqualTo(DataVisualizerSettings.DefaultDataFolderPath));
            Assert.That(service.SelectActiveObject, Is.False);
            Assert.That(EditorUtility.IsDirty(settings), Is.False);
        }

        [TestCase(null)]
        [TestCase("invalid")]
        [TestCase("00000000000000000000000000000000")]
        public void ShouldRejectMissingOrInvalidSettingsGuid(string guid)
        {
            Assert.Throws<ArgumentException>(() => EditorConfigurationService.FromGuid(guid));
        }

        [Test]
        public void ShouldShareSelectionPreferenceWithTheWindowWithoutChangingItsFolder()
        {
            using TestCleanupScope cleanup = new();
            DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));
            EditorConfigurationService service = new(settings);
            string originalPath = service.DataFolderPath;
            Assert.That(DataVisualizer.ApplySelectActiveObjectPreference(settings, true), Is.True);
            Assert.That(service.SelectActiveObject, Is.True);
            Assert.That(service.DataFolderPath, Is.EqualTo(originalPath));
            EditorUtility.ClearDirty(settings);
            Assert.That(service.SetSelectActiveObject(true), Is.False);
            Assert.That(EditorUtility.IsDirty(settings), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShouldResolveFolderPickerSpellingBeforeApplyingStrictConfiguration(bool varyCase)
        {
            using TestCleanupScope cleanup = new();
            string leaf = "PickerCase-" + Guid.NewGuid().ToString("N");
            string folder = "Assets/" + leaf;
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            AssetDatabase.CreateFolder("Assets", leaf);
            string selected = varyCase ? "Assets/" + leaf.ToLowerInvariant() : folder;
            string canonical = AssetsFolderUtility.GetCanonicalAssetsFolderOrNull(selected);
            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(selected)))
            {
                Assert.That(canonical == null, "A case-sensitive database must reject a path it cannot resolve.");
                return;
            }
            Assert.That(canonical, Is.EqualTo(folder));
            EditorConfigurationService.ValidateDataFolder(canonical);
            if (varyCase)
            {
                Assert.Throws<ArgumentException>(() => EditorConfigurationService.ValidateDataFolder(selected), "The headless contract stays strict.");
            }
        }

        [Test]
        public void ShouldValidateAnExistingFolderWithoutChangingSettingsOrDirtyState()
        {
            using TestCleanupScope cleanup = new();
            DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));
            EditorUtility.ClearDirty(settings);
            EditorConfigurationService.ValidateDataFolder("Assets");
            Assert.That(settings.DataFolderPath, Is.EqualTo(DataVisualizerSettings.DefaultDataFolderPath));
            Assert.That(EditorUtility.IsDirty(settings), Is.False);
            EditorConfigurationService service = new(settings);
            UnityEngine.Object.DestroyImmediate(settings);
            Assert.Throws<ObjectDisposedException>(() => service.SetSelectActiveObject(true));
            Assert.Throws<ArgumentNullException>(() => new EditorConfigurationService(settings));
        }
    }
}
