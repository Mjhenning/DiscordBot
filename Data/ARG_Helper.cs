using Newtonsoft.Json;

namespace DiscordBot.Data;

public class ArgFilesystem
{
    static readonly string FsRoot =
        Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "Twitch_Bot",
                "ARG",
                "_filesystem"));
    
    public string RootPath => FsRoot;
    public FsNode Root { get; private set; }
    public Dictionary<string, FsNode> PathIndex { get; private set; } = new();

    public ArgFilesystem()
    {
        Root = new FsNode
        {
            Name        = "/",
            FullPath    = FsRoot,
            IsDirectory = true
        };

        PathIndex[FsRoot] = Root;
        BuildTree(FsRoot, Root);
        WatchForChanges();
    }
    
    void BuildTree(string dirPath, FsNode parent)
{
    foreach (string dir in Directory.EnumerateDirectories(dirPath))
    {
        string name = Path.GetFileName(dir);
        FsNode node = new()
        {
            Name        = name,
            FullPath    = dir,
            IsDirectory = true,
            Parent      = parent
        };

        parent.Children[name] = node;
        PathIndex[dir]        = node;
        BuildTree(dir, node);
    }

    foreach (string file in Directory.EnumerateFiles(dirPath, "*.json"))
    {
        ParseAndAddFile(file, parent);
    }
}

void ParseAndAddFile(string filePath, FsNode parent)
{
    try
    {
        string raw      = File.ReadAllText(filePath);
        FsFileContent? json = JsonConvert.DeserializeObject<FsFileContent>(raw);
        if (json == null) return;

        FsNode node = new()
        {
            Name                = Path.GetFileName(filePath),
            FullPath            = filePath,
            IsDirectory         = false,
            Parent              = parent,
            Filename            = json.Filename,
            Corrupted           = json.Corrupted,
            UnlockedAtCoherence = json.UnlockedAtCoherence,
            Content             = json.Content
        };

        parent.Children[node.Name] = node;
        PathIndex[filePath]        = node;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ARG] Failed to parse {filePath}: {ex.Message}");
    }
}

void WatchForChanges()
{
    FileSystemWatcher watcher = new(FsRoot)
    {
        IncludeSubdirectories = true,
        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
        Filter = "*.json"
    };

    watcher.Changed += (_, e) =>
    {
        if (!PathIndex.TryGetValue(e.FullPath, out FsNode? node) || node.IsDirectory) return;
        try
        {
            string raw = File.ReadAllText(e.FullPath);
            FsFileContent? json = JsonConvert.DeserializeObject<FsFileContent>(raw);
            if (json == null) return;

            node.Filename            = json.Filename;
            node.Corrupted           = json.Corrupted;
            node.UnlockedAtCoherence = json.UnlockedAtCoherence;
            node.Content             = json.Content;
        }
        catch { /* file mid-write, ignore */ }
    };

    watcher.Created += (_, e) =>
    {
        string? parentPath = Path.GetDirectoryName(e.FullPath);
        if (parentPath == null) return;
        if (!PathIndex.TryGetValue(parentPath, out FsNode? parent)) return;

        if (Directory.Exists(e.FullPath))
        {
            FsNode node = new()
            {
                Name        = Path.GetFileName(e.FullPath),
                FullPath    = e.FullPath,
                IsDirectory = true,
                Parent      = parent
            };
            parent.Children[node.Name] = node;
            PathIndex[e.FullPath]      = node;
        }
        else if (e.FullPath.EndsWith(".json"))
        {
            ParseAndAddFile(e.FullPath, parent);
        }
    };

    watcher.Deleted += (_, e) =>
    {
        if (!PathIndex.TryGetValue(e.FullPath, out FsNode? node)) return;
        node.Parent?.Children.Remove(node.Name);
        PathIndex.Remove(e.FullPath);
    };

    watcher.EnableRaisingEvents = true;
}

public FsNode GetCurrentNode(string cwd)
{
    if (string.IsNullOrWhiteSpace(cwd) || cwd == "/")
        return Root;

    string fullPath = Path.Combine(RootPath, cwd);

    return PathIndex.TryGetValue(fullPath, out FsNode? node)
        ? node
        : Root;
}

public List<FsNode> GetDirectories(string cwd)
{
    FsNode node = GetCurrentNode(cwd);

    return node.Children.Values
        .Where(c => c.IsDirectory)
        .OrderBy(c => c.Name)
        .ToList();
}

public List<FsNode> GetReadableFiles(string cwd, int coherence)
{
    FsNode node = GetCurrentNode(cwd);

    return node.Children.Values
        .Where(c =>
            !c.IsDirectory &&
            (
                !c.UnlockedAtCoherence.HasValue ||
                coherence >= c.UnlockedAtCoherence.Value
            ))
        .OrderBy(c => c.Filename)
        .ToList();
}
}

public class FsNode
{
    public string Name                 { get; set; } = "";
    public string FullPath             { get; set; } = "";
    public bool   IsDirectory          { get; set; }
    public FsNode? Parent              { get; set; }
    public Dictionary<string, FsNode> Children { get; set; } = new();

    // file content fields
    public string?   Filename            { get; set; }
    public bool      Corrupted           { get; set; }
    public int?      UnlockedAtCoherence { get; set; }
    public string[]? Content             { get; set; }
}

public class FsFileContent
{
    [JsonProperty("filename")]
    public string Filename { get; set; } = "";

    [JsonProperty("corrupted")]
    public bool Corrupted { get; set; }

    [JsonProperty("unlockedAtCoherence")]
    public int? UnlockedAtCoherence { get; set; }

    [JsonProperty("content")]
    public string[]? Content { get; set; }
}