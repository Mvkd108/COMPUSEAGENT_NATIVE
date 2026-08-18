namespace Compuse.Requests;

public static class DropFilesRequestLimits
{
    public const int MinSourceCount = 1;
    public const int MaxSourceCount = 1024;
    public const int MaxPathLength = 32767;
    public const int MaxComponentLength = 255;
    public const int MaxProcessImageNameLength = 255;
    public const int MaxWindowTextLength = 1024;
}
