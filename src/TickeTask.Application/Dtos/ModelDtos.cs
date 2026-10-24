namespace TickeTask.Application.Dtos;

public record ModelStorageRoots(string Primary, string Secondary, string Custom, string PrimaryHost, string SecondaryHost);

public record ModelStorageSelection(string Slot, string Folder, string Label, string Path);

public record ModelDownloadJob(string State, string Model, int Progress, int Attempt, string Message);

public record ModelStorageStatus(ModelStorageRoots Roots, ModelStorageSelection Selection, ModelDownloadJob Job);

public record ModelFolderListing(string Slot, string Folder, List<string> Directories, string Path);

public record ModelStorageAction(string Action, string? Model, string? Slot, string? Folder, string? Name, string? Path);
