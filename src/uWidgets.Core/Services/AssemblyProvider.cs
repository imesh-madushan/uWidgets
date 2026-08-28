using System.Reflection;
using System.Resources;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using uWidgets.Core.Interfaces;
using uWidgets.Core.Models;
using uWidgets.Core.Models.Attributes;

namespace uWidgets.Core.Services;
 
/// <inheritdoc />
public class AssemblyProvider : IAssemblyProvider
{
    private readonly Dictionary<string, AssemblyLoadContext> loadedContexts = new();
    private ILookup<string, AssemblyInfo> assemblyCache;
    private readonly IServiceProvider serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="AssemblyProvider"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    public AssemblyProvider(IServiceProvider serviceProvider)
    {
        assemblyCache = GetAssemblyInfos(Const.WidgetsFolder);
        this.serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public ILookup<string, AssemblyInfo> GetAssemblyInfos(string directoryPath)
    {
        var assemblies = Directory.Exists(directoryPath)
            ? Directory
                .GetFiles(directoryPath, "*.dll")
                .Select(GetAssemblyInfo)
                .Where(info => info != null)
                .Cast<AssemblyInfo>()
            : [];
        
        return assemblies
            .ToLookup(assembly => assembly.AssemblyName);
    }

    private AssemblyInfo? GetAssemblyInfo(string filePath)
    {
        try
        {
            // Use the same dependency-aware context used when activating a plugin.
            // A plain AssemblyLoadContext cannot resolve plugin-local NuGet dependencies
            // while DefinedTypes is being inspected.
            // Metadata inspection is short-lived, so it can use the original path.
            // Active widgets use shadow copies in LoadAssembly below.
            var context = PluginLoadContext.CreateForInspection(filePath);
            var assembly = context.LoadPluginAssembly();
            var localeAttribute = assembly.GetCustomAttributes<LocaleAttribute>().FirstOrDefault();
            var companyAttribute = assembly.GetCustomAttributes<AssemblyCompanyAttribute>().FirstOrDefault();
            var widgetAttributes = assembly.GetCustomAttributes<WidgetInfoAttribute>();

            if (!widgetAttributes.Any()) return null;

            var assemblyName = assembly.GetName().Name!;
            var version = assembly.GetName().Version!;
            var company = companyAttribute?.Company ?? "";
            var locale = GetLocaleResourceManager(assembly);
            var displayName = locale?.GetString(localeAttribute?.DisplayName ?? "") ?? assemblyName;

            context.Unload();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            context.DeleteShadowCopy();

            return new AssemblyInfo(filePath, assemblyName, displayName, company, version, localeAttribute?.IconData ?? "");
        }
        catch (Exception)
        {
            return null;
        }
       
    }
    
    /// <inheritdoc />
    public Assembly LoadAssembly(string name)
    {
        if (loadedContexts.TryGetValue(name, out var context))
            return context.Assemblies.Single(assembly => 
                assembly.ManifestModule.Name == $"{name}.dll");
        
        var filePath = GetAssemblyPath(name);
        context = PluginLoadContext.CreateShadowCopy(filePath);
        loadedContexts[name] = context;

        return ((PluginLoadContext)context).LoadPluginAssembly();
    }
    
    /// <inheritdoc />
    public void UnloadAssembly(string name)
    {
        if (!loadedContexts.TryGetValue(name, out var context))
            throw new InvalidOperationException($"Assembly {name} is not loaded");

        context.Unload();
        loadedContexts.Remove(name);
        GC.Collect();
        GC.WaitForPendingFinalizers();

        if (context is PluginLoadContext pluginContext)
            pluginContext.DeleteShadowCopy();
    }

    /// <inheritdoc />
    public object Activate(Type type, params object[] args)
    {
        try
        {
            return ActivatorUtilities.CreateInstance(serviceProvider, type, args);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"Failed to create an instance of {type.Name}", e);
        }
    }

    /// <inheritdoc />
    public ResourceManager? GetLocaleResourceManager(Assembly assembly)
    {
        return assembly
            .DefinedTypes
            .FirstOrDefault(type => type.Name == "Locale")?
            .GetProperty(nameof(ResourceManager), BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?
            .GetValue(null) as ResourceManager;
    }
    
    private string GetAssemblyPath(string name, bool updateCache = false)
    {
        if (updateCache) 
            assemblyCache = GetAssemblyInfos(Const.WidgetsFolder);
        
        var assemblyInfo = assemblyCache[name]
            .MaxBy(assembly => assembly.Version);

        if (assemblyInfo != default) 
            return assemblyInfo.FilePath;

        if (!updateCache)
            return GetAssemblyPath(name, true);
        
        throw new FileNotFoundException($"Assembly {name} not found");
    }

    /// <summary>
    /// Loads a widget from a private shadow directory. Loading directly from the build
    /// output would lock the widget DLLs and prevent Visual Studio from rebuilding them.
    /// </summary>
    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly string pluginPath;
        private readonly string? shadowDirectory;
        private readonly AssemblyDependencyResolver resolver;

        private PluginLoadContext(string pluginPath, string? shadowDirectory)
            : base(isCollectible: true)
        {
            this.pluginPath = pluginPath;
            this.shadowDirectory = shadowDirectory;
            resolver = new AssemblyDependencyResolver(pluginPath);
        }

        public static PluginLoadContext CreateForInspection(string pluginPath) =>
            new(pluginPath, shadowDirectory: null);

        public static PluginLoadContext CreateShadowCopy(string sourcePluginPath)
        {
            var shadowDirectory = Path.Combine(
                Path.GetTempPath(),
                Const.AppName,
                "PluginShadow",
                Environment.ProcessId.ToString(),
                Guid.NewGuid().ToString("N"));

            CopyDirectory(Path.GetDirectoryName(sourcePluginPath)!, shadowDirectory);
            var shadowPluginPath = Path.Combine(shadowDirectory, Path.GetFileName(sourcePluginPath));

            return new PluginLoadContext(shadowPluginPath, shadowDirectory);
        }

        public Assembly LoadPluginAssembly() => LoadFromAssemblyPath(pluginPath);

        public void DeleteShadowCopy()
        {
            try
            {
                if (shadowDirectory is not null && Directory.Exists(shadowDirectory))
                    Directory.Delete(shadowDirectory, recursive: true);
            }
            catch (IOException)
            {
                // A delayed runtime handle may keep the shadow copy alive briefly.
                // It is in the OS temp directory and can be reclaimed later.
            }
            catch (UnauthorizedAccessException)
            {
                // Cleanup failure must not prevent a widget from unloading.
            }
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);

            foreach (var filePath in Directory.EnumerateFiles(sourceDirectory))
                File.Copy(filePath, Path.Combine(destinationDirectory, Path.GetFileName(filePath)), overwrite: true);

            foreach (var directoryPath in Directory.EnumerateDirectories(sourceDirectory))
                CopyDirectory(directoryPath, Path.Combine(destinationDirectory, Path.GetFileName(directoryPath)));
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var assembly = Default.Assemblies.FirstOrDefault(a => a.GetName().Name == assemblyName.Name);
            if (assembly != null)
            {
                return assembly;
            }

            var assemblyPath = resolver.ResolveAssemblyToPath(assemblyName);
            return assemblyPath != null ? LoadFromAssemblyPath(assemblyPath) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var libraryPath = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
    
            if (libraryPath != null)
            {
                return LoadUnmanagedDllFromPath(libraryPath);
            }

            return IntPtr.Zero;
        }
    }
}
