// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections;
using System.Reflection;
using Pandora.API.Patch;
using Pandora.API.Patch.Skyrim64;
using Pandora.API.Patch.Skyrim64.AnimData;
using Pandora.API.Patch.Skyrim64.AnimSetData;

namespace TES4ConverterCompatibility;

public sealed class TES4ConverterCompatibilityPatch : ISkyrim64Patch
{
    public RuntimeMode Mode => RuntimeMode.Serial;
    public RunOrder Order => RunOrder.PreLaunch;

    private const string AnimDataFileName = "animationdatasinglefile.txt";
    private const string AnimSetDataFileName = "animationsetdatasinglefile.txt";
    private const string PrefixOblivion = "tes4oblivion_";
    private const string PrefixMorrowind = "tes4morrowind_";

    public void Run(IProjectManager projectManager)
    {
        string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Could not determine TES4ConverterCompatibility plugin directory.");
        string logPath = Path.Combine(pluginDir, "TES4ConverterPandoraPatch.log");

        try
        {
            Log(logPath, "=== TES4Converter Pandora Compatibility v0.3 ===");
            Log(logPath, $"Engine project manager: {projectManager.GetType().FullName}");

            var (animDataManager, animSetDataManager) = ResolveActiveManagers(projectManager);

            string animDataPath = Path.Combine(pluginDir, AnimDataFileName);
            string animSetDataPath = Path.Combine(pluginDir, AnimSetDataFileName);
            RequireFile(animDataPath);
            RequireFile(animSetDataPath);

            int animProjectsAdded = InjectAnimData(projectManager, animDataManager, animDataPath, logPath);
            int animSetProjectsAdded = InjectAnimSetData(animSetDataManager, animSetDataPath, logPath);

            Log(logPath, $"SUCCESS: added {animProjectsAdded} missing TES4 AnimData projects and {animSetProjectsAdded} missing TES4 AnimSetData projects.");
        }
        catch (Exception ex)
        {
            Log(logPath, "FATAL: " + ex);
            throw;
        }
    }

    private static (IAnimDataManager animData, IAnimSetDataManager animSetData) ResolveActiveManagers(IProjectManager projectManager)
    {
        Assembly engineAssembly = projectManager.GetType().Assembly;
        Type appType = engineAssembly.GetType("Pandora.App", throwOnError: true)!;

        PropertyInfo currentProperty = appType.GetProperty(
            "Current",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy
        ) ?? throw new MissingMemberException("Pandora.App.Current was not found.");

        object app = currentProperty.GetValue(null)
            ?? throw new InvalidOperationException("Pandora.App.Current is null.");

        PropertyInfo servicesProperty = appType.GetProperty(
            "Services",
            BindingFlags.Public | BindingFlags.Instance
        ) ?? throw new MissingMemberException("Pandora.App.Services was not found.");

        IServiceProvider rootProvider = servicesProperty.GetValue(app) as IServiceProvider
            ?? throw new InvalidOperationException("Pandora root service provider is unavailable.");

        Type patcherFactoryInterface = engineAssembly.GetType(
            "Pandora.Models.Patch.Skyrim64.IPatcherFactory",
            throwOnError: true
        )!;

        object patcherFactory = rootProvider.GetService(patcherFactoryInterface)
            ?? throw new InvalidOperationException("Pandora active patcher-factory service is unavailable.");

        FieldInfo scopeField = patcherFactory.GetType().GetField(
            "_scope",
            BindingFlags.NonPublic | BindingFlags.Instance
        ) ?? throw new MissingFieldException(patcherFactory.GetType().FullName, "_scope");

        object scope = scopeField.GetValue(patcherFactory)
            ?? throw new InvalidOperationException("Pandora active patcher scope is unavailable.");

        PropertyInfo scopeProviderProperty = scope.GetType().GetProperty(
            "ServiceProvider",
            BindingFlags.Public | BindingFlags.Instance
        ) ?? throw new MissingMemberException(scope.GetType().FullName, "ServiceProvider");

        IServiceProvider scopedProvider = scopeProviderProperty.GetValue(scope) as IServiceProvider
            ?? throw new InvalidOperationException("Pandora scoped service provider is unavailable.");

        var scopedProjectManager = scopedProvider.GetService(typeof(IProjectManager)) as IProjectManager;
        if (!ReferenceEquals(scopedProjectManager, projectManager))
            throw new InvalidOperationException("Resolved Pandora scope does not own the active project manager.");

        var animData = scopedProvider.GetService(typeof(IAnimDataManager)) as IAnimDataManager
            ?? throw new InvalidOperationException("Active IAnimDataManager service is unavailable.");
        var animSetData = scopedProvider.GetService(typeof(IAnimSetDataManager)) as IAnimSetDataManager
            ?? throw new InvalidOperationException("Active IAnimSetDataManager service is unavailable.");

        return (animData, animSetData);
    }

    private static int InjectAnimData(
        IProjectManager projectManager,
        IAnimDataManager manager,
        string sourcePath,
        string logPath
    )
    {
        Type managerType = manager.GetType();
        Assembly engineAssembly = managerType.Assembly;

        Type projectAnimDataType = engineAssembly.GetType(
            "Pandora.Models.Patch.Skyrim64.AnimData.ProjectAnimData",
            throwOnError: true
        )!;
        Type motionDataType = engineAssembly.GetType(
            "Pandora.Models.Patch.Skyrim64.AnimData.MotionData",
            throwOnError: true
        )!;

        MethodInfo readProjectMethod = projectAnimDataType.GetMethod(
            "TryReadProject",
            BindingFlags.Public | BindingFlags.Static
        ) ?? throw new MissingMethodException(projectAnimDataType.FullName, "TryReadProject");
        MethodInfo readMotionMethod = motionDataType.GetMethod(
            "TryReadProject",
            BindingFlags.Public | BindingFlags.Static
        ) ?? throw new MissingMethodException(motionDataType.FullName, "TryReadProject");

        IList projectNames = GetFieldValue<IList>(manager, "_projectNames");
        IList animDataList = GetPropertyValue<IList>(manager, "AnimDataList", nonPublic: true);
        object usedClipIds = GetFieldValue<object>(manager, "_usedClipIDs");
        MethodInfo addUsedClipId = usedClipIds.GetType().GetMethod("Add", new[] { typeof(int) })
            ?? throw new MissingMethodException(usedClipIds.GetType().FullName, "Add(int)");

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (object? item in projectNames)
        {
            if (item is string name)
                existing.Add(name);
        }

        int totalDeclared;
        int tes4Seen = 0;
        int added = 0;

        using var reader = new StreamReader(sourcePath, detectEncodingFromByteOrderMarks: true);
        if (!int.TryParse(reader.ReadLine(), out totalDeclared) || totalDeclared < 1)
            throw new FormatException("Invalid converter AnimData project count.");

        string[] names = new string[totalDeclared];
        for (int i = 0; i < totalDeclared; i++)
            names[i] = reader.ReadLine() ?? throw new EndOfStreamException("Unexpected EOF in AnimData project-name table.");

        for (int i = 0; i < totalDeclared; i++)
        {
            if (!int.TryParse(reader.ReadLine(), out int animLineCount))
                throw new FormatException($"Invalid AnimData line count for project {names[i]}.");

            object?[] animArgs = { reader, manager, animLineCount, null };
            bool animOk = (bool)(readProjectMethod.Invoke(null, animArgs) ?? false);
            object animData = animArgs[3]
                ?? throw new FormatException($"Pandora could not parse AnimData project {names[i]}.");
            if (!animOk)
                throw new FormatException($"Pandora rejected AnimData project {names[i]}.");

            object header = animData.GetType().GetProperty("Header", BindingFlags.Public | BindingFlags.Instance)!
                .GetValue(animData)!;
            int hasMotionData = (int)(header.GetType().GetProperty("HasMotionData")!.GetValue(header) ?? 0);

            object? motionData = null;
            if (hasMotionData == 1)
            {
                if (!int.TryParse(reader.ReadLine(), out int motionLineCount))
                    throw new FormatException($"Invalid MotionData line count for project {names[i]}.");

                object?[] motionArgs = { reader, motionLineCount, null };
                bool motionOk = (bool)(readMotionMethod.Invoke(null, motionArgs) ?? false);
                motionData = motionArgs[2]
                    ?? throw new FormatException($"Pandora could not parse MotionData project {names[i]}.");
                if (!motionOk)
                    throw new FormatException($"Pandora rejected MotionData project {names[i]}.");

                animData.GetType().GetProperty("BoundMotionDataProject", BindingFlags.Public | BindingFlags.Instance)!
                    .SetValue(animData, motionData);
            }

            string projectName = Path.GetFileNameWithoutExtension(names[i]);
            if (!IsTes4Project(projectName))
                continue;

            tes4Seen++;
            if (existing.Contains(projectName))
            {
                Log(logPath, $"AnimData already present; preserving active Pandora version: {projectName}");
                continue;
            }

            projectNames.Add(projectName);
            animDataList.Add(animData);
            MethodInfo getClipIds = animData.GetType().GetMethod("GetClipIDs", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new MissingMethodException(animData.GetType().FullName, "GetClipIDs");
            if (getClipIds.Invoke(animData, null) is IEnumerable clipIds)
            {
                foreach (object? clipIdValue in clipIds)
                {
                    if (clipIdValue is string clipIdText && int.TryParse(clipIdText, out int clipId))
                        _ = addUsedClipId.Invoke(usedClipIds, new object?[] { clipId });
                }
            }

            if (projectManager.TryGetProject(projectName, out IProject? loadedProject) && loadedProject is not null)
                loadedProject.AnimData = (IProjectAnimData)animData;

            existing.Add(projectName);
            added++;
        }

        if (tes4Seen == 0)
            throw new InvalidDataException("No tes4oblivion_/tes4morrowind_ projects were found in converter AnimData.");

        if (projectNames.Count != animDataList.Count)
            throw new InvalidOperationException($"Pandora AnimData alignment failed: names={projectNames.Count}, projects={animDataList.Count}.");

        Log(logPath, $"AnimData source declared {totalDeclared} projects; found {tes4Seen} TES4 projects; injected {added} missing projects.");
        return added;
    }

    private static int InjectAnimSetData(
        IAnimSetDataManager manager,
        string sourcePath,
        string logPath
    )
    {
        Type managerType = manager.GetType();
        Assembly engineAssembly = managerType.Assembly;
        Type projectAnimSetDataType = engineAssembly.GetType(
            "Pandora.Models.Patch.Skyrim64.AnimSetData.ProjectAnimSetData",
            throwOnError: true
        )!;
        MethodInfo tryReadMethod = projectAnimSetDataType.GetMethod(
            "TryRead",
            BindingFlags.Public | BindingFlags.Static
        ) ?? throw new MissingMethodException(projectAnimSetDataType.FullName, "TryRead");

        IList projectPaths = GetFieldValue<IList>(manager, "_projectPaths");
        IList animSetDataList = GetFieldValue<IList>(manager, "_animSetDataList");
        IDictionary animSetDataMap = managerType.GetProperty("AnimSetDataMap", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(manager) as IDictionary
            ?? throw new InvalidOperationException("Pandora AnimSetDataMap is unavailable.");

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in animSetDataMap)
        {
            if (entry.Key is string key)
                existing.Add(key);
        }

        using var reader = new StreamReader(sourcePath, detectEncodingFromByteOrderMarks: true);
        if (!int.TryParse(reader.ReadLine(), out int totalDeclared) || totalDeclared < 1)
            throw new FormatException("Invalid converter AnimSetData project count.");

        string[] paths = new string[totalDeclared];
        for (int i = 0; i < totalDeclared; i++)
            paths[i] = reader.ReadLine() ?? throw new EndOfStreamException("Unexpected EOF in AnimSetData project-path table.");

        int tes4Seen = 0;
        int added = 0;

        for (int i = 0; i < totalDeclared; i++)
        {
            object?[] args = { reader, null };
            bool ok = (bool)(tryReadMethod.Invoke(null, args) ?? false);
            object animSetData = args[1]
                ?? throw new FormatException($"Pandora could not parse AnimSetData project {paths[i]}.");
            if (!ok)
                throw new FormatException($"Pandora rejected AnimSetData project {paths[i]}.");

            string key = Path.GetFileNameWithoutExtension(paths[i]);
            if (!IsTes4Project(key))
                continue;

            tes4Seen++;
            if (existing.Contains(key))
            {
                Log(logPath, $"AnimSetData already present; preserving active Pandora version: {key}");
                continue;
            }

            projectPaths.Add(paths[i]);
            animSetDataList.Add(animSetData);
            animSetDataMap.Add(key, animSetData);
            existing.Add(key);
            added++;
        }

        if (tes4Seen == 0)
            throw new InvalidDataException("No tes4oblivion_/tes4morrowind_ projects were found in converter AnimSetData.");

        if (projectPaths.Count != animSetDataList.Count)
            throw new InvalidOperationException($"Pandora AnimSetData alignment failed: paths={projectPaths.Count}, projects={animSetDataList.Count}.");

        Log(logPath, $"AnimSetData source declared {totalDeclared} projects; found {tes4Seen} TES4 projects; injected {added} missing projects.");
        return added;
    }

    private static bool IsTes4Project(string projectName) =>
        projectName.StartsWith(PrefixOblivion, StringComparison.OrdinalIgnoreCase)
        || projectName.StartsWith(PrefixMorrowind, StringComparison.OrdinalIgnoreCase);

    private static T GetFieldValue<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException(target.GetType().FullName, fieldName);
        return (T)(field.GetValue(target)
            ?? throw new InvalidOperationException($"Field {target.GetType().FullName}.{fieldName} is null."));
    }

    private static T GetPropertyValue<T>(object target, string propertyName, bool nonPublic)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        if (nonPublic)
            flags |= BindingFlags.NonPublic;
        PropertyInfo property = target.GetType().GetProperty(propertyName, flags)
            ?? throw new MissingMemberException(target.GetType().FullName, propertyName);
        return (T)(property.GetValue(target)
            ?? throw new InvalidOperationException($"Property {target.GetType().FullName}.{propertyName} is null."));
    }

    private static void RequireFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Required TES4Converter compatibility data file was not found.", path);
    }

    private static void Log(string logPath, string message)
    {
        try
        {
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never interfere with Pandora generation.
        }
    }
}
