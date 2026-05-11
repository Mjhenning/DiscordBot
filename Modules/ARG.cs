namespace DiscordBot.Modules;

public class ARG
{
    Dictionary<FolderAndFiles, FolderAndFiles> filesystem = new Dictionary<FolderAndFiles, FolderAndFiles>();

    public void PopulateFilesystem()
    {
        string[] baseDirectories = Directory.GetDirectories(Config.ARGDirectory);
        
        for (int i = 0; i < baseDirectories.Length; i++)
        {
            //if basedirectory contains directories do x
            
            //else if contains no directories
            
            // filesystem.Add(baseDirectories[i]., Directory.GetDirectories(Config.ARGDirectory + "/" + baseDirectories[i]));
        }
    }
    
    
}

class FolderAndFiles()
{
    public bool containsSubFolders = false; //if false assume contains files OR root folder
    public string folder;
    public List<string> subfolders;
    public List<string> files;
}